using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Support;

/// <summary>Where the support actions find the log of the process (T-024).</summary>
/// <param name="Directory">The log folder, or <see langword="null"/> when the app logs nowhere.</param>
/// <param name="Flush">Writes the queued records to the file within the given time; never throws.</param>
internal sealed record SupportLog(string? Directory, Func<TimeSpan, bool> Flush);

/// <summary>
/// The support actions of the About window (T-048, T-049): the text for a problem report, the diagnostic bundle and the
/// log folder. Everything slow runs off the UI thread.
/// </summary>
internal sealed partial class SupportActions
{
    /// <summary>Waiting for the log queue never holds an action up longer (a hanging disk).</summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    private readonly SupportLog _log;
    private readonly Func<CancellationToken, Task<AppSettings>> _settings;
    private readonly IPrinterMonitor _monitor;
    private readonly IFolderLauncher _folders;
    private readonly Func<string?, SupportEnvironment> _environment;
    private readonly TimeProvider _time;
    private readonly ILogger<SupportActions> _logger;

    /// <param name="settings">The settings in use (the current run, else the saved ones).</param>
    /// <param name="environment">The system the app runs on, for the saved language setting.</param>
    public SupportActions(
        SupportLog log,
        Func<CancellationToken, Task<AppSettings>> settings,
        IPrinterMonitor monitor,
        IFolderLauncher folders,
        Func<string?, SupportEnvironment> environment,
        TimeProvider time,
        ILogger<SupportActions> logger)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _log = log;
        _settings = settings;
        _monitor = monitor;
        _folders = folders;
        _environment = environment;
        _time = time;
        _logger = logger;
    }

    /// <summary>The log folder of the app, or <see langword="null"/> when it logs nowhere.</summary>
    public string? LogDirectory => _log.Directory;

    /// <summary>The English text about the app, the system and the printers (<see cref="SupportInfo"/>).</summary>
    public async Task<string> BuildInfoAsync(CancellationToken cancellationToken)
    {
        AppSettings settings = await _settings(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => BuildInfo(settings), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The text for the clipboard (T-049): <see cref="SupportInfo"/> and the last <see cref="LogTail.DefaultLineCount"/> log
    /// lines, at most about 64 KB (<see cref="SupportText"/>). The log queue is written first; the files are read off the
    /// UI thread.
    /// </summary>
    public async Task<string> BuildClipboardTextAsync(CancellationToken cancellationToken)
    {
        AppSettings settings = await _settings(cancellationToken).ConfigureAwait(false);
        return await Task.Run(
            () =>
            {
                _log.Flush(FlushTimeout);
                return SupportText.Compose(BuildInfo(settings), LogTail.Read(_log.Directory));
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// "Copy info to clipboard" (T-049): builds the text off the UI thread, then writes it into
    /// <paramref name="clipboard"/>. Call on the UI thread (the clipboard write continues there). Never throws: a clipboard
    /// held by another program (Windows reports a timeout) or a closed window is logged.
    /// </summary>
    /// <returns><see langword="true"/> when the text is in the clipboard.</returns>
    public async Task<bool> CopyInfoAsync(ISupportClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        try
        {
            // Back on the caller's (UI) thread for the clipboard.
            string text = await BuildClipboardTextAsync(CancellationToken.None).ConfigureAwait(true);
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            LogCopyFailed(ex);
            return false;
        }
    }

    /// <summary>
    /// Writes the diagnostic bundle into <paramref name="destination"/> (left open): the log queue is written first, so the
    /// bundle holds everything logged so far, including the line of this save.
    /// </summary>
    /// <exception cref="IOException">The bundle cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The destination refused the data.</exception>
    public async Task SaveBundleAsync(Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        AppSettings settings = await _settings(cancellationToken).ConfigureAwait(false);
        LogSavingBundle();
        await Task.Run(
            () =>
            {
                _log.Flush(FlushTimeout);
                SupportBundle.Write(destination, BuildInfo(settings), SettingsExport.ToSupportJson(settings), _log.Directory, cancellationToken);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the diagnostic bundle into the file chosen by the user. A bundle that fails half-way is deleted, so no broken
    /// ZIP is left behind; the outcome is logged (the window may be closed meanwhile). Never throws.
    /// </summary>
    /// <returns><see langword="true"/> when the bundle is complete.</returns>
    public async Task<bool> SaveBundleAsync(IBundleTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Stream stream;
        try
        {
            stream = await target.OpenWriteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Not opened: an existing file the user chose to overwrite is left as it was.
            LogBundleFailed(ex);
            return false;
        }

        try
        {
            await using (stream.ConfigureAwait(false))
            {
                await SaveBundleAsync(stream, CancellationToken.None).ConfigureAwait(false);
            }

            LogBundleSaved();
            return true;
        }
        catch (Exception ex)
        {
            // Quitting the app in the middle ends this task with the process; such a partial file stays (accepted).
            LogBundleFailed(ex);
            await DeleteAsync(target).ConfigureAwait(false);
            return false;
        }
    }

    /// <summary>The suggested name of a new bundle, with the current time.</summary>
    public string DefaultBundleFileName() => SupportBundle.DefaultFileName(_time.GetLocalNow());

    /// <summary>Opens the log folder (created when missing); <see langword="false"/> when the app logs nowhere or it failed.</summary>
    public Task<bool> OpenLogFolderAsync() => _log.Directory is { } directory ? _folders.OpenAsync(directory) : Task.FromResult(false);

    private string BuildInfo(AppSettings settings) =>
        SupportInfo.Build(_environment(settings.Language), settings, _monitor.Snapshots, _time.GetLocalNow());

    private async Task DeleteAsync(IBundleTarget target)
    {
        try
        {
            await target.DeleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogBundleNotDeleted(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Copying the support information to the clipboard failed.")]
    private partial void LogCopyFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saving a diagnostic bundle.")]
    private partial void LogSavingBundle();

    [LoggerMessage(Level = LogLevel.Information, Message = "The diagnostic bundle was saved.")]
    private partial void LogBundleSaved();

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the diagnostic bundle failed.")]
    private partial void LogBundleFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The incomplete diagnostic bundle could not be deleted.")]
    private partial void LogBundleNotDeleted(Exception exception);
}
