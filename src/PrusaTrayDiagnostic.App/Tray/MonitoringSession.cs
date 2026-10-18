using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Tray;

/// <summary>
/// Runs the monitoring service for the lifetime of the app: loads the settings, hands them to the
/// observers (icon, menu, notifications) and polls the printers until stopped; settings saved in the settings window
/// restart it (<see cref="ApplyAsync"/>). Never throws into the UI: failures are logged and shown in the tray.
/// </summary>
internal sealed partial class MonitoringSession : IAsyncDisposable, IDisposable, ISettingsApplier
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(3);

    private readonly Func<ISettingsStore> _settingsStoreFactory;
    private readonly IPrinterMonitor _monitor;
    private readonly IMonitoringObserver[] _observers;
    private readonly ILogger<MonitoringSession> _logger;
    private readonly RemovedCameraCredentials? _removedCameraCredentials;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();

    // Serializes handing settings to observers (run start vs. notification-only update); taken before _gate, never inside it.
    private readonly Lock _delivery = new();
    private int _started;

    // Guarded by _gate. The first stop request; later requests reuse it.
    private Task? _stopping;
    private bool _disposed;

    // Guarded by _gate. The run loop (see RunLoopAsync) and settings waiting for it (Apply).
    private Task _completion = Task.CompletedTask;
    private bool _loopActive;
    private AppSettings? _pending;
    private CancellationTokenSource? _run;
    private Task _runCancelling = Task.CompletedTask;

    // Guarded by _gate. Settings of the current run, and completion of the current run (for ApplyAsync).
    private AppSettings? _current;
    private TaskCompletionSource? _runEnded;

    /// <param name="settingsStoreFactory">Called once on a background thread; may throw (e.g. no per-user data folder).</param>
    /// <param name="removedCameraCredentials">
    /// Receives the printers whose camera address lost its credentials while loading (the settings window shows it).
    /// </param>
    public MonitoringSession(
        Func<ISettingsStore> settingsStoreFactory,
        IPrinterMonitor monitor,
        IEnumerable<IMonitoringObserver> observers,
        ILogger<MonitoringSession> logger,
        RemovedCameraCredentials? removedCameraCredentials = null)
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
        _removedCameraCredentials = removedCameraCredentials;
    }

    /// <summary>Settings of the current run, including a notification-only update; <see langword="null"/> before the first run.</summary>
    internal AppSettings? CurrentSettings
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Completes when monitoring ended (stopped or failed); never faults. Already completed before <see cref="Start"/>.
    /// After a failure, <see cref="ApplyAsync"/> starts monitoring again with a new completion task.
    /// </summary>
    public Task Completion
    {
        get
        {
            lock (_gate)
            {
                return _completion;
            }
        }
    }

    /// <exception cref="InvalidOperationException">The session was already started.</exception>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The monitoring session was already started.");
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _loopActive = true;
            CancellationToken stopToken = _stop.Token;
            // Task.Run: loading the settings does file I/O and must not run on the UI thread.
            _completion = Task.Run(() => RunLoopAsync(loadSettings: true, stopToken), CancellationToken.None);
        }
    }

    /// <summary>
    /// Restarts monitoring with <paramref name="settings"/> (saved in the settings window): the current run is cancelled
    /// and, once it ended, observers get the new settings and a new run starts. Settings applied in quick succession
    /// collapse into the latest one. Before <see cref="Start"/> the settings replace the loaded ones; after a monitoring
    /// failure monitoring starts again; after a stop request they are ignored. When the printers (id, name, address,
    /// connection type, order) and the polling interval stay the same, nothing restarts: observers only get
    /// <see cref="IMonitoringObserver.UpdateSettings"/>, unless <paramref name="credentialsChanged"/>: a changed password
    /// is read by a running client only after the next rejection (and a rejected printer is on backoff), so it always
    /// restarts. Thread-safe; blocks only while observers receive settings.
    /// </summary>
    /// <returns>
    /// Completes when the previous run ended, i.e. nothing polls with the old settings any more (immediately when nothing
    /// had to be restarted). Never faults.
    /// </returns>
    public Task ApplyAsync(AppSettings settings, bool credentialsChanged)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Task oldRunEnded = Task.CompletedTask;
        bool updateOnly = false;
        lock (_gate)
        {
            if (_stopping is not null || _disposed)
            {
                return Task.CompletedTask;
            }

            if (!credentialsChanged && _loopActive && _run is { IsCancellationRequested: false } && _pending is null && _current is { } current && KeepsMonitoring(current, settings))
            {
                // Only notification settings changed: no restart, so the tray keeps the current state (no "connecting").
                _current = settings;
                updateOnly = true;
            }
            else
            {
                _pending = settings;
                if (_loopActive)
                {
                    // The loop picks _pending up when the run ends; between runs it finds it without a cancellation.
                    if (_run is { IsCancellationRequested: false } run)
                    {
                        // CancelAsync: the monitor's cancellation work runs on the thread pool, not on the (UI) caller.
                        _runCancelling = run.CancelAsync();
                    }

                    oldRunEnded = _runEnded?.Task ?? Task.CompletedTask;
                }
                else if (Volatile.Read(ref _started) != 0)
                {
                    // Monitoring failed earlier (the loop ended): start again.
                    _loopActive = true;
                    CancellationToken stopToken = _stop.Token;
                    _completion = Task.Run(() => RunLoopAsync(loadSettings: false, stopToken), CancellationToken.None);
                }
            }
        }

        if (updateOnly)
        {
            UpdateObservers(settings);
            return Task.CompletedTask;
        }

        return oldRunEnded;
    }

    /// <summary>Same printers (id, name, address, connection type, order) and polling interval: the run can go on.</summary>
    private static bool KeepsMonitoring(AppSettings current, AppSettings next) =>
        current.PollingInterval == next.PollingInterval && current.Printers.SequenceEqual(next.Printers);

    /// <summary>
    /// Start of a run: observers get its settings, or the newer ones a notification-only update stored meanwhile.
    /// Serialized with <see cref="UpdateObservers"/> so that observers always end with the latest settings.
    /// </summary>
    private void DeliverRunSettings(AppSettings runSettings)
    {
        lock (_delivery)
        {
            AppSettings latest;
            lock (_gate)
            {
                latest = _current ?? runSettings;
            }

            foreach (IMonitoringObserver observer in _observers)
            {
                observer.ApplySettings(latest);
            }
        }
    }

    /// <summary>Notification-only update; skipped when newer settings replaced it (their own delivery follows).</summary>
    private void UpdateObservers(AppSettings settings)
    {
        lock (_delivery)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_current, settings))
                {
                    return;
                }
            }

            foreach (IMonitoringObserver observer in _observers)
            {
                try
                {
                    observer.UpdateSettings(settings);
                }
                catch (Exception ex)
                {
                    LogObserverUpdateFailed(ex);
                }
            }
        }
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
        // Stable from here on: once the stop is requested, Apply never starts another run.
        Task completion = Completion;
        await completion.WaitAsync(timeout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        // A zero timeout means "do not wait"; not finishing is then expected and not worth a warning.
        if (!completion.IsCompleted && timeout > TimeSpan.Zero)
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

    /// <summary>
    /// Runs the monitor until stopped; each <see cref="ApplyAsync"/> ends the current run and starts the next one with the
    /// applied settings. A failed run is reported and ends the loop unless new settings are already waiting.
    /// </summary>
    private async Task RunLoopAsync(bool loadSettings, CancellationToken stopToken)
    {
        try
        {
            AppSettings? settings = loadSettings ? await LoadSettingsAsync(stopToken).ConfigureAwait(false) : null;
            while (true)
            {
                CancellationTokenSource run;
                lock (_gate)
                {
                    if (_pending is not null)
                    {
                        settings = _pending;
                        _pending = null;
                    }

                    if (stopToken.IsCancellationRequested || settings is null)
                    {
                        _loopActive = false;
                        return;
                    }

                    run = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
                    _run = run;
                    _current = settings;
                    _runEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                await RunOnceAsync(settings, run).ConfigureAwait(false);

                lock (_gate)
                {
                    if (stopToken.IsCancellationRequested || _pending is null)
                    {
                        _loopActive = false;
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Stopped while loading the settings.
            lock (_gate)
            {
                _loopActive = false;
            }
        }
    }

    private async Task RunOnceAsync(AppSettings settings, CancellationTokenSource run)
    {
        try
        {
            DeliverRunSettings(settings);

            await _monitor.RunAsync(settings.Printers, settings.PollingInterval, run.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            // Normal shutdown or restart with applied settings.
        }
        catch (Exception ex)
        {
            LogMonitoringFailed(ex);
            ReportFailure();
        }
        finally
        {
            Task cancelling;
            TaskCompletionSource? ended;
            lock (_gate)
            {
                _run = null;
                cancelling = _runCancelling;
                _runCancelling = Task.CompletedTask;
                ended = _runEnded;
                _runEnded = null;
            }

            // The source must not be disposed while its asynchronous cancellation callbacks still run.
            await cancelling.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            run.Dispose();
            // Nothing of this run polls any more (the monitor waited for its loops): ApplyAsync callers may go on.
            ended?.TrySetResult();
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
        ISettingsStore store;
        SettingsLoadResult result;
        try
        {
            store = _settingsStoreFactory();
            result = await store.LoadAsync(stopToken).ConfigureAwait(false);
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

        if (result.Status == SettingsLoadStatus.Loaded)
        {
            await RemoveCameraCredentialsFromFileAsync(store, result.Settings, stopToken).ConfigureAwait(false);
        }

        return result.Settings;
    }

    /// <summary>
    /// Credentials found in a camera address were removed while loading, but they stay in the file until it is written
    /// again: it is saved right away (T-014 decision), so that the password does not remain on disk. A failure is logged
    /// and monitoring goes on (the next start tries again). The settings window is told which cameras were affected.
    /// </summary>
    private async Task RemoveCameraCredentialsFromFileAsync(ISettingsStore store, AppSettings settings, CancellationToken stopToken)
    {
        List<string>? affected = null;
        foreach (PrinterDefinition printer in settings.Printers)
        {
            if (printer.Camera.CredentialsRemoved)
            {
                (affected ??= []).Add(printer.Id);
            }
        }

        if (affected is null)
        {
            return;
        }

        _removedCameraCredentials?.Add(affected);
        try
        {
            await store.SaveAsync(settings, stopToken).ConfigureAwait(false);
            LogCameraCredentialsRemoved(affected.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stopToken.IsCancellationRequested)
        {
            LogCameraCredentialsNotRemoved(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Settings could not be loaded; defaults are used.")]
    private partial void LogSettingsUnavailable(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored settings were not used ({Status}): {ErrorMessage}")]
    private partial void LogSettingsNotUsed(SettingsLoadStatus status, string? errorMessage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings: {Warning}")]
    private partial void LogSettingsWarning(string warning);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Credentials were removed from {Count} camera address(es); the settings file was saved without them.")]
    private partial void LogCameraCredentialsRemoved(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings file could not be saved after removing credentials from camera addresses; they stay in the file until it is saved.")]
    private partial void LogCameraCredentialsNotRemoved(Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Printer monitoring stopped unexpectedly.")]
    private partial void LogMonitoringFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Showing the monitoring failure in the tray failed.")]
    private partial void LogObserverFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handing updated settings to a tray observer failed.")]
    private partial void LogObserverUpdateFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer monitoring did not stop within {Timeout}.")]
    private partial void LogStopTimedOut(TimeSpan timeout);
}
