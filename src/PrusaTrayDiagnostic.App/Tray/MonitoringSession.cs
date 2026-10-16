using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Tray;

/// <summary>
/// Runs the monitoring service for the lifetime of the app: loads the settings, hands them to the
/// observers (icon, menu, notifications) and polls the printers until stopped. Never throws into the UI: failures are logged and shown in the tray.
/// </summary>
internal sealed partial class MonitoringSession : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(3);

    private readonly Func<ISettingsStore> _settingsStoreFactory;
    private readonly IPrinterMonitor _monitor;
    private readonly IMonitoringObserver[] _observers;
    private readonly ILogger<MonitoringSession> _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private int _started;

    // Guarded by _gate. The first stop request; later requests reuse it.
    private Task? _stopping;
    private bool _disposed;

    /// <param name="settingsStoreFactory">Called once on a background thread; may throw (e.g. no per-user data folder).</param>
    public MonitoringSession(
        Func<ISettingsStore> settingsStoreFactory,
        IPrinterMonitor monitor,
        IEnumerable<IMonitoringObserver> observers,
        ILogger<MonitoringSession> logger)
    {
        ArgumentNullException.ThrowIfNull(settingsStoreFactory);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(logger);
        _settingsStoreFactory = settingsStoreFactory;
        _monitor = monitor;
        _observers = [.. observers];
        if (Array.Exists(_observers, static observer => observer is null))
        {
            throw new ArgumentException("Observers must not contain null entries.", nameof(observers));
        }

        _logger = logger;
    }

    /// <summary>Completes when monitoring ended (stopped or failed); never faults. Already completed before <see cref="Start"/>.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <exception cref="InvalidOperationException">The session was already started.</exception>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The monitoring session was already started.");
        }

        // Task.Run: loading the settings does file I/O and must not run on the UI thread.
        Completion = Task.Run(() => RunAsync(_stop.Token), CancellationToken.None);
    }

    /// <summary>
    /// Requests the stop without waiting for monitoring to end and without logging (e.g. the OS is ending the
    /// session). Safe to call repeatedly, before <see cref="Start"/> and after disposal.
    /// </summary>
    public void RequestStop() => _ = RequestStopCore();

    /// <summary>
    /// Stops monitoring and waits at most <paramref name="timeout"/> for it to end, so that quitting is never blocked
    /// by a stuck printer client. Safe to call repeatedly and before <see cref="Start"/>.
    /// </summary>
    public async Task StopAsync(TimeSpan timeout)
    {
        await RequestStopCore().ConfigureAwait(false);
        await Completion.WaitAsync(timeout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        // A zero timeout means "do not wait"; not finishing is then expected and not worth a warning.
        if (!Completion.IsCompleted && timeout > TimeSpan.Zero)
        {
            LogStopTimedOut(timeout);
        }
    }

    /// <summary>
    /// Stops monitoring. Waits (at most 3 s) only if nobody requested the stop before: after a
    /// <see cref="StopAsync"/> that timed out, the caller already waited and disposal returns immediately.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Task? stopping;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            stopping = _stopping;
        }

        if (stopping is null)
        {
            await StopAsync(DisposeTimeout).ConfigureAwait(false);
        }
        else
        {
            // Only the cancellation callbacks (already running or done), not the monitor itself.
            await stopping.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        Dispose();
    }

    /// <summary>Requests the stop without waiting for it (synchronous disposal, e.g. a failed startup).</summary>
    public void Dispose()
    {
        bool canDisposeSource;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_stopping is null)
            {
                _stop.Cancel();
                _stopping = Task.CompletedTask;
            }

            // The source must not be disposed while its asynchronous cancellation callbacks still run.
            canDisposeSource = _stopping.IsCompleted;
        }

        if (canDisposeSource)
        {
            // Safe even if a stuck monitor still holds the token: a cancelled token stays cancelled after Dispose.
            _stop.Dispose();
        }
    }

    private Task RequestStopCore()
    {
        lock (_gate)
        {
            // CancelAsync runs the registered callbacks on the thread pool, never on the (UI) caller.
            return _stopping ??= _disposed ? Task.CompletedTask : _stop.CancelAsync();
        }
    }

    private async Task RunAsync(CancellationToken stopToken)
    {
        try
        {
            AppSettings settings = await LoadSettingsAsync(stopToken).ConfigureAwait(false);
            stopToken.ThrowIfCancellationRequested();
            foreach (IMonitoringObserver observer in _observers)
            {
                observer.ApplySettings(settings);
            }

            await _monitor.RunAsync(settings.Printers, settings.PollingInterval, stopToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            LogMonitoringFailed(ex);
            ReportFailure();
        }
    }

    /// <summary>Tells every observer, even if one of them fails, so that the session never faults.</summary>
    private void ReportFailure()
    {
        foreach (IMonitoringObserver observer in _observers)
        {
            try
            {
                observer.ReportMonitoringFailed();
            }
            catch (Exception ex)
            {
                LogObserverFailed(ex);
            }
        }
    }

    private async Task<AppSettings> LoadSettingsAsync(CancellationToken stopToken)
    {
        SettingsLoadResult result;
        try
        {
            result = await _settingsStoreFactory().LoadAsync(stopToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stopToken.IsCancellationRequested)
        {
            // File problems come back in the result; this covers a missing data folder and the unexpected.
            LogSettingsUnavailable(ex);
            return AppSettings.Default;
        }

        if (result.Status is SettingsLoadStatus.Corrupted or SettingsLoadStatus.Unreadable)
        {
            LogSettingsNotUsed(result.Status, result.ErrorMessage);
        }

        foreach (string warning in result.Warnings)
        {
            LogSettingsWarning(warning);
        }

        return result.Settings;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Settings could not be loaded; defaults are used.")]
    private partial void LogSettingsUnavailable(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored settings were not used ({Status}): {ErrorMessage}")]
    private partial void LogSettingsNotUsed(SettingsLoadStatus status, string? errorMessage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings: {Warning}")]
    private partial void LogSettingsWarning(string warning);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Printer monitoring stopped unexpectedly.")]
    private partial void LogMonitoringFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Showing the monitoring failure in the tray failed.")]
    private partial void LogObserverFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer monitoring did not stop within {Timeout}.")]
    private partial void LogStopTimedOut(TimeSpan timeout);
}
