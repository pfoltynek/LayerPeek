using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>
/// Saves the device path a saved camera monitor was found at on another connector (user decision 2026-10-23, see
/// <see cref="CameraWindowDefaultPlacement.Relocate"/>).
/// </summary>
internal interface ICameraMonitorPathUpdater
{
    /// <summary>
    /// Saves <paramref name="updated"/> as the camera monitor of <paramref name="printerId"/> while the stored one is still
    /// <paramref name="saved"/>. Thread-safe, returns at once (the save runs in the background) and never throws for
    /// storage failures.
    /// </summary>
    void Request(string printerId, CameraMonitorId saved, CameraMonitorId updated);
}

/// <summary>
/// <see cref="ICameraMonitorPathUpdater"/> through <see cref="ISettingsStore.SetCameraMonitorAsync"/> (the normal atomic
/// save with its retries of a file held by another process): silently, once per printer and change, in the background
/// (one save after another), with one Information line when the file was written. A failure is logged as a warning and
/// does not affect the camera window; the next request of the same change (the next time the window opens) tries again.
/// Device paths are never logged (T-071 privacy decision).
/// </summary>
internal sealed partial class CameraMonitorPathUpdater : ICameraMonitorPathUpdater
{
    private readonly Func<ISettingsStore> _settingsStore;
    private readonly ILogger<CameraMonitorPathUpdater> _logger;
    private readonly Lock _gate = new();

    // Guarded by _gate: the changes requested and not failed (saved, saving or found unnecessary), and the last save.
    private readonly HashSet<Change> _requested = [];
    private Task _pending = Task.CompletedTask;

    /// <param name="settingsStore">Resolved on each save; may throw (e.g. no per-user data folder).</param>
    public CameraMonitorPathUpdater(Func<ISettingsStore> settingsStore, ILogger<CameraMonitorPathUpdater> logger)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(logger);
        _settingsStore = settingsStore;
        _logger = logger;
    }

    public void Request(string printerId, CameraMonitorId saved, CameraMonitorId updated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(updated);
        Change change = new(printerId.Trim().ToUpperInvariant(), saved, updated);
        lock (_gate)
        {
            if (!_requested.Add(change))
            {
                return;
            }

            Task previous = _pending;
            _pending = Task.Run(() => SaveAsync(previous, printerId, change), CancellationToken.None);
        }
    }

    /// <summary>
    /// Waits at most <paramref name="timeout"/> for the requested saves (tests, quitting). <see langword="true"/> when none
    /// is running any more.
    /// </summary>
    public async Task<bool> WaitForPendingAsync(TimeSpan timeout)
    {
        Task pending;
        lock (_gate)
        {
            pending = _pending;
        }

        // SaveAsync never faults; only the timeout matters here.
        await pending.WaitAsync(timeout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return pending.IsCompleted;
    }

    /// <summary>After the previous save; never faults.</summary>
    private async Task SaveAsync(Task previous, string printerId, Change change)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        try
        {
            if (await _settingsStore().SetCameraMonitorAsync(printerId, change.Saved, change.Updated, CancellationToken.None).ConfigureAwait(false))
            {
                LogPathUpdated(printerId);
            }
            else
            {
                LogNothingToUpdate(printerId);
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _requested.Remove(change);
            }

            LogNotSaved(ex, printerId);
        }
    }

    /// <param name="PrinterId">Trimmed, upper case: ids are compared case-insensitively.</param>
    private readonly record struct Change(string PrinterId, CameraMonitorId Saved, CameraMonitorId Updated);

    [LoggerMessage(Level = LogLevel.Information, Message = "The camera monitor of printer '{PrinterId}' was found on another connector; its saved device path was updated.")]
    private partial void LogPathUpdated(string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The camera monitor of printer '{PrinterId}' was found on another connector, but the settings file was not changed (the monitor changed meanwhile, or the file has problems the settings window shows).")]
    private partial void LogNothingToUpdate(string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The new device path of the camera monitor of printer '{PrinterId}' could not be saved; it is tried again the next time the camera window opens.")]
    private partial void LogNotSaved(Exception exception, string printerId);
}
