using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.Core.Settings;
using PrusaTrayDiagnostic.Core.Web;

namespace PrusaTrayDiagnostic.App.Settings;

/// <summary>
/// Loads and saves what the settings window edits. Saving order (so that a failure never leaves the saved settings pointing
/// to a printer whose new password is missing, and never leaves a new password behind as an orphan):
/// <list type="number">
/// <item>New passwords are written, each after reading the previous value (kept in memory only for the rollback). If a
/// previous value cannot be read, nothing is written at all.</item>
/// <item>The settings file is written atomically (<see cref="ISettingsStore.SaveAsync"/>).</item>
/// <item>If step 1 or 2 fails, every password written in step 1 is restored (or deleted when there was none), newest first.</item>
/// <item>The settings are applied to monitoring; the save waits (at most <see cref="DefaultApplyWaitTimeout"/>) until the
/// previous monitoring run ended, so that nothing polls a removed printer any more. Meanwhile the local web proxies (T-028)
/// of printers that were removed or whose address, user name, connection type or password changed are stopped (waiting at
/// most as long), so that an open browser tab never keeps working with the old login.</item>
/// <item>Passwords of removed printers are deleted. A failure leaves an orphan that nothing references (harmless, reported).</item>
/// </list>
/// Any exception in steps 1–2 (documented or not) triggers the rollback; storage failures never escape. Everything runs on
/// the thread pool (the secret stores call the OS synchronously). The last save can be awaited when the app quits
/// (<see cref="WaitForPendingSaveAsync"/>).
/// </summary>
internal sealed partial class SettingsEditorStore : ISettingsEditorStore
{
    private readonly Func<ISettingsStore> _settingsStore;
    private readonly ISecretStore _secrets;
    private readonly PasswordPolicy _policy;
    private readonly ISettingsApplier _applier;
    private readonly IPrinterWebGateway _webGateway;
    private readonly ILogger<SettingsEditorStore> _logger;
    private readonly TimeSpan _applyWaitTimeout;
    private readonly Lock _pendingLock = new();

    // Guarded by _pendingLock. The last save, awaited when the app quits (an interrupted save could leave an orphan).
    private Task _pendingSave = Task.CompletedTask;

    /// <summary>How long a save waits for the previous monitoring run to end before deleting passwords of removed printers.</summary>
    public static readonly TimeSpan DefaultApplyWaitTimeout = TimeSpan.FromSeconds(5);

    /// <param name="settingsStore">Resolved on first use; may throw (e.g. no per-user data folder).</param>
    /// <param name="webGateway">Its proxies are stopped for printers whose login changed.</param>
    /// <param name="applyWaitTimeout">
    /// Default <see cref="DefaultApplyWaitTimeout"/>; a stuck printer client or proxy must not block the save forever.
    /// </param>
    public SettingsEditorStore(
        Func<ISettingsStore> settingsStore,
        ISecretStore secrets,
        PasswordPolicy policy,
        ISettingsApplier applier,
        IPrinterWebGateway webGateway,
        ILogger<SettingsEditorStore> logger,
        TimeSpan? applyWaitTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(webGateway);
        ArgumentNullException.ThrowIfNull(logger);
        _settingsStore = settingsStore;
        _secrets = secrets;
        _policy = policy;
        _applier = applier;
        _webGateway = webGateway;
        _logger = logger;
        _applyWaitTimeout = applyWaitTimeout ?? DefaultApplyWaitTimeout;
    }

    public Task<SettingsEditorData> LoadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => LoadCoreAsync(cancellationToken), cancellationToken);

    public async Task<bool> WaitForPendingSaveAsync(TimeSpan timeout)
    {
        Task pending;
        lock (_pendingLock)
        {
            pending = _pendingSave;
        }

        // SaveCoreAsync never faults; only the timeout matters here.
        await pending.WaitAsync(timeout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return pending.IsCompleted;
    }

    public Task<SettingsSaveResult> SaveAsync(SettingsChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        HashSet<string> updatedIds = Ids(change.Updated);
        foreach (string id in change.NewPasswords.Keys)
        {
            if (!_policy.IsSupported)
            {
                throw new ArgumentException("Passwords cannot be stored on this operating system.", nameof(change));
            }

            if (!updatedIds.Contains(id))
            {
                throw new ArgumentException($"Password for unknown printer '{id}'.", nameof(change));
            }
        }

        Task<SettingsSaveResult> save = Task.Run(() => SaveCoreAsync(change, updatedIds), CancellationToken.None);
        lock (_pendingLock)
        {
            _pendingSave = Task.WhenAll(_pendingSave, save);
        }

        return save;
    }

    private async Task<SettingsEditorData> LoadCoreAsync(CancellationToken cancellationToken)
    {
        SettingsLoadResult result;
        try
        {
            result = await _settingsStore().LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogLoadFailed(ex);
            return new SettingsEditorData(AppSettings.Default, SettingsLoadStatus.Unreadable, [], null);
        }

        IReadOnlySet<string>? withPassword = await FindSavedPasswordsAsync(result.Settings, cancellationToken).ConfigureAwait(false);
        return new SettingsEditorData(result.Settings, result.Status, result.Warnings, withPassword);
    }

    /// <summary>Only whether a password exists; the value is dropped right away.</summary>
    private async Task<IReadOnlySet<string>?> FindSavedPasswordsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (!_policy.IsSupported)
        {
            return null;
        }

        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in settings.Printers)
        {
            // Prusa Connect credentials belong to the account, not to the printer (docs/architecture.md).
            if (printer.ConnectionType != ConnectionType.PrusaLink)
            {
                continue;
            }

            try
            {
                if (!string.IsNullOrEmpty(await _secrets.GetAsync(SecretKeys.ForPrinter(printer.Id), cancellationToken).ConfigureAwait(false)))
                {
                    ids.Add(printer.Id);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPasswordStatusUnknown(ex, printer.Id);
                return null;
            }
        }

        return ids;
    }

    private async Task<SettingsSaveResult> SaveCoreAsync(SettingsChange change, HashSet<string> updatedIds)
    {
        // Step 1: new passwords, remembering what they replaced.
        List<(string Key, string? Previous)> written = [];
        foreach ((string id, string password) in change.NewPasswords)
        {
            string key = SecretKeys.ForPrinter(id);
            string? previous;
            try
            {
                previous = await _secrets.GetAsync(key, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogPasswordNotSaved(ex, id);
                return new SettingsSaveResult(SettingsSaveStatus.PasswordNotSaved, RollbackIncomplete: !await RollbackAsync(written).ConfigureAwait(false));
            }

            written.Add((key, previous));
            try
            {
                await _secrets.SetAsync(key, password, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogPasswordNotSaved(ex, id);
                return new SettingsSaveResult(SettingsSaveStatus.PasswordNotSaved, RollbackIncomplete: !await RollbackAsync(written).ConfigureAwait(false));
            }
        }

        // Step 2: the settings file.
        try
        {
            await _settingsStore().SaveAsync(change.Updated, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSettingsNotSaved(ex);
            return new SettingsSaveResult(SettingsSaveStatus.SettingsNotSaved, RollbackIncomplete: !await RollbackAsync(written).ConfigureAwait(false));
        }

        // Step 4: monitoring restarts with the new settings. Waiting for the old run to end means no poll of a removed
        // printer can find its password deleted below (that would be a spurious "password rejected" notification).
        // The web proxies of changed printers stop at the same time (never faults).
        Task webProxiesStopped = StopWebProxiesAsync(change);
        try
        {
            Task oldRunEnded = _applier.ApplyAsync(change.Updated, credentialsChanged: change.NewPasswords.Count > 0);
            await oldRunEnded.WaitAsync(_applyWaitTimeout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (!oldRunEnded.IsCompleted)
            {
                LogOldRunStillRunning(_applyWaitTimeout);
            }
        }
        catch (Exception ex)
        {
            LogApplyFailed(ex);
        }

        // Before the passwords of removed printers go: their proxies must not read them any more.
        await webProxiesStopped.ConfigureAwait(false);

        // Step 5: passwords nothing references any more.
        int undeleted = 0;
        if (_policy.IsSupported)
        {
            foreach (PrinterDefinition removed in change.Original.Printers)
            {
                if (updatedIds.Contains(removed.Id))
                {
                    continue;
                }

                try
                {
                    await _secrets.DeleteAsync(SecretKeys.ForPrinter(removed.Id), CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    undeleted++;
                    LogPasswordNotDeleted(ex, removed.Id);
                }
            }
        }

        return new SettingsSaveResult(SettingsSaveStatus.Saved, UndeletedPasswords: undeleted);
    }

    /// <summary>Step 3: undoes written passwords, newest first. <see langword="false"/> when something could not be undone.</summary>
    private async Task<bool> RollbackAsync(List<(string Key, string? Previous)> written)
    {
        bool complete = true;
        for (int i = written.Count - 1; i >= 0; i--)
        {
            (string key, string? previous) = written[i];
            try
            {
                if (previous is null)
                {
                    await _secrets.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await _secrets.SetAsync(key, previous, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                complete = false;
                LogRollbackFailed(ex, key);
            }
        }

        written.Clear();
        return complete;
    }

    /// <summary>
    /// Stops the local web proxies (T-028) of printers whose login is no longer what the proxy uses: removed printers,
    /// a changed address, user name or connection type, and a new password. Renamed or untouched printers keep theirs,
    /// so an open browser tab goes on working. Waits at most the apply timeout; never faults.
    /// </summary>
    private async Task StopWebProxiesAsync(SettingsChange change)
    {
        List<string> ids = WebLoginChanged(change);
        if (ids.Count == 0)
        {
            return;
        }

        // The gateway stops the proxies anyway when the wait is cancelled; the save only stops waiting for them.
        using CancellationTokenSource timeout = new(_applyWaitTimeout);
        await Task.WhenAll(ids.Select(id => StopWebProxyAsync(id, timeout.Token))).ConfigureAwait(false);
    }

    private async Task StopWebProxyAsync(string printerId, CancellationToken cancellationToken)
    {
        try
        {
            await _webGateway.StopAsync(printerId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogWebProxyStopTimedOut(printerId, _applyWaitTimeout);
        }
        catch (Exception ex)
        {
            // E.g. the gateway was disposed because the app is quitting: nothing serves the old login any more anyway.
            LogWebProxyStopFailed(ex, printerId);
        }
    }

    private static List<string> WebLoginChanged(SettingsChange change)
    {
        Dictionary<string, PrinterDefinition> updated = new(change.Updated.Printers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in change.Updated.Printers)
        {
            updated.TryAdd(printer.Id, printer);
        }

        HashSet<string> newPasswords = new(change.NewPasswords.Keys, StringComparer.OrdinalIgnoreCase);
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        // Only printers of the running configuration can have a proxy; a printer added now has none yet.
        foreach (PrinterDefinition before in change.Original.Printers)
        {
            if (newPasswords.Contains(before.Id)
                || !updated.TryGetValue(before.Id, out PrinterDefinition? after)
                || after.ConnectionType != before.ConnectionType
                || !string.Equals(after.Address, before.Address, StringComparison.Ordinal)
                || !string.Equals(after.UserName, before.UserName, StringComparison.Ordinal))
            {
                ids.Add(before.Id);
            }
        }

        return [.. ids];
    }

    private static HashSet<string> Ids(AppSettings settings)
    {
        HashSet<string> ids = new(settings.Printers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in settings.Printers)
        {
            ids.Add(printer.Id);
        }

        return ids;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Settings could not be loaded for the settings window; defaults are shown.")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Whether printer '{PrinterId}' has a saved password could not be determined.")]
    private partial void LogPasswordStatusUnknown(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The password of printer '{PrinterId}' could not be saved; nothing was saved.")]
    private partial void LogPasswordNotSaved(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Settings could not be saved; password changes are undone.")]
    private partial void LogSettingsNotSaved(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Undoing the password change '{Key}' failed.")]
    private partial void LogRollbackFailed(Exception exception, string key);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saved settings could not be applied to monitoring.")]
    private partial void LogApplyFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Monitoring did not restart within {Timeout}; passwords of removed printers are deleted anyway.")]
    private partial void LogOldRunStillRunning(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The web proxy of printer '{PrinterId}' did not stop within {Timeout}; it is stopped in the background.")]
    private partial void LogWebProxyStopTimedOut(string printerId, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stopping the web proxy of printer '{PrinterId}' after a settings change failed.")]
    private partial void LogWebProxyStopFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The password of removed printer '{PrinterId}' could not be deleted.")]
    private partial void LogPasswordNotDeleted(Exception exception, string printerId);
}
