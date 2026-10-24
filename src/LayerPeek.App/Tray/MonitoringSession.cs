using Microsoft.Extensions.Logging;

using LayerPeek.App.Cameras;
using LayerPeek.App.Settings;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tray;

/// <summary>
/// Runs the monitoring service for the lifetime of the app: loads the settings, hands them to the
/// observers (icon, menu, notifications) and polls the printers until stopped; settings saved in the settings window
/// restart it (<see cref="ApplyAsync"/>). Never throws into the UI: failures are logged and shown in the tray.
/// </summary>
internal sealed partial class MonitoringSession : IAsyncDisposable, IDisposable, ISettingsApplier
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long after a failed save of a detected model (also after the store's own short retries) it is tried again (user
    /// decision 2026-10-22).
    /// </summary>
    internal static readonly TimeSpan ModelSaveRetryDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How many times a failed save of a detected model is tried again before the session gives up (user decision
    /// 2026-10-22); a new detected model, a successful save or a save of the settings window starts a new streak.
    /// </summary>
    internal const int MaxModelSaveRetries = 5;

    private readonly Func<ISettingsStore> _settingsStoreFactory;
    private readonly IPrinterMonitor _monitor;
    private readonly IMonitoringObserver[] _observers;
    private readonly ILogger<MonitoringSession> _logger;
    private readonly RemovedCameraCredentials? _removedCameraCredentials;
    private readonly TimeProvider _timeProvider;
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

    // Guarded by _gate. Background saves of detected models (T-039), one after another.
    private Task _modelSaves = Task.CompletedTask;

    // Guarded by _gate. Printers whose detected model could not be saved, with the timer of the next attempt: at most one
    // per printer; it saves the model the printer has when it fires.
    private readonly Dictionary<string, PendingModelRetry> _modelRetries = new(StringComparer.OrdinalIgnoreCase);

    // Guarded by _gate. Failed saves of each printer's detected model in the current streak (see MaxModelSaveRetries); no
    // entry: no failure since the last reset.
    private readonly Dictionary<string, int> _modelSaveFailures = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="settingsStoreFactory">
    /// Called on background threads (loading, saving a detected model); may throw (e.g. no per-user data folder).
    /// </param>
    /// <param name="removedCameraCredentials">
    /// Receives the printers whose camera address lost its credentials while loading (the settings window shows it).
    /// </param>
    /// <param name="timeProvider">Clock of the delayed start (T-067); <see cref="TimeProvider.System"/> by default.</param>
    public MonitoringSession(
        Func<ISettingsStore> settingsStoreFactory,
        IPrinterMonitor monitor,
        IEnumerable<IMonitoringObserver> observers,
        ILogger<MonitoringSession> logger,
        RemovedCameraCredentials? removedCameraCredentials = null,
        TimeProvider? timeProvider = null)
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
        _timeProvider = timeProvider ?? TimeProvider.System;
        _monitor.ModelDetected += OnModelDetected;
    }

    /// <summary>
    /// The background saves of detected models (T-039) queued so far; completes when they are done. Never faults. For tests
    /// and quitting.
    /// </summary>
    internal Task ModelSaves
    {
        get
        {
            lock (_gate)
            {
                return _modelSaves;
            }
        }
    }

    /// <summary>Receivers of the settings of every run, in notification order.</summary>
    internal IReadOnlyList<IMonitoringObserver> Observers => _observers;

    /// <summary>Settings of the current run, including a notification-only update; <see langword="null"/> before the first run.</summary>
    public AppSettings? CurrentSettings
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

    /// <summary>Starts monitoring; the settings are loaded first (on a background thread).</summary>
    /// <exception cref="InvalidOperationException">The session was already started.</exception>
    public void Start() => StartCore(initialSettings: null, TimeSpan.Zero);

    /// <summary>
    /// Starts monitoring with settings the caller already loaded (<see cref="LoadSettingsAsync(Func{ISettingsStore}, ILogger, CancellationToken, RemovedCameraCredentials)"/>
    /// at startup, so that the language is applied before the tray shows any text); settings applied before win.
    /// </summary>
    /// <param name="startDelay">
    /// How much later the first run starts (the Debug test mode, T-067: the tray stays in its "Starting…" state meanwhile);
    /// settings applied meanwhile are used instead, a stop ends the wait.
    /// </param>
    /// <exception cref="InvalidOperationException">The session was already started.</exception>
    public void Start(AppSettings initialSettings, TimeSpan startDelay = default)
    {
        ArgumentNullException.ThrowIfNull(initialSettings);
        ArgumentOutOfRangeException.ThrowIfLessThan(startDelay, TimeSpan.Zero);
        StartCore(initialSettings, startDelay);
    }

    /// <summary>
    /// Loads the settings like <see cref="Start()"/> does: never throws except for cancellation; problems are logged and
    /// defaults (or the corrected settings) are returned.
    /// </summary>
    /// <param name="settingsStoreFactory">May throw (e.g. no per-user data folder): the defaults are used.</param>
    /// <param name="removedCameraCredentials">
    /// Receives the printers whose camera address lost its credentials while loading (the settings window shows it); the
    /// credentials are removed from the file as well (T-014).
    /// </param>
    public static async Task<AppSettings> LoadSettingsAsync(
        Func<ISettingsStore> settingsStoreFactory,
        ILogger logger,
        CancellationToken cancellationToken,
        RemovedCameraCredentials? removedCameraCredentials = null) =>
        (await LoadSettingsWithStatusAsync(settingsStoreFactory, logger, cancellationToken, removedCameraCredentials).ConfigureAwait(false)).Settings;

    /// <summary>
    /// <see cref="LoadSettingsAsync(Func{ISettingsStore}, ILogger, CancellationToken, RemovedCameraCredentials)"/> with how the
    /// settings were loaded: only <see cref="SettingsLoadStatus.Loaded"/> settings are the user's (e.g. for the orphan sweep of
    /// printer images, T-039), and the ids of every printer entry in the file, also of entries the loader skipped
    /// (<see cref="SettingsLoadResult.StoredPrinterIds"/>). A store that could not be created counts as
    /// <see cref="SettingsLoadStatus.Unreadable"/>.
    /// </summary>
    public static async Task<(AppSettings Settings, SettingsLoadStatus Status, IReadOnlyList<string> StoredPrinterIds)> LoadSettingsWithStatusAsync(
        Func<ISettingsStore> settingsStoreFactory,
        ILogger logger,
        CancellationToken cancellationToken,
        RemovedCameraCredentials? removedCameraCredentials = null)
    {
        ArgumentNullException.ThrowIfNull(settingsStoreFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ISettingsStore store;
        SettingsLoadResult result;
        try
        {
            store = settingsStoreFactory();
            result = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // File problems come back in the result; this covers a missing data folder and the unexpected.
            LogSettingsUnavailable(logger, ex);
            return (AppSettings.Default, SettingsLoadStatus.Unreadable, []);
        }

        if (result.Status is SettingsLoadStatus.Corrupted or SettingsLoadStatus.Unreadable)
        {
            LogSettingsNotUsed(logger, result.Status, result.ErrorMessage);
        }

        foreach (string warning in result.Warnings)
        {
            LogSettingsWarning(logger, warning);
        }

        // A corrupted file is cleaned as well when it is valid JSON (user decision): nothing of it is used, but the password
        // must not stay on disk.
        if (result.Status is SettingsLoadStatus.Loaded or SettingsLoadStatus.Corrupted)
        {
            await RemoveCameraCredentialsFromFileAsync(store, result.Settings, logger, removedCameraCredentials, cancellationToken).ConfigureAwait(false);
        }

        return (result.Settings, result.Status, result.StoredPrinterIds);
    }

    private void StartCore(AppSettings? initialSettings, TimeSpan startDelay)
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
            _completion = initialSettings is null
                ? Task.Run(() => RunLoopAsync(loadSettings: true, stopToken), CancellationToken.None)
                : Task.Run(() => RunLoopAsync(loadSettings: false, stopToken, initialSettings, startDelay), CancellationToken.None);
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

            // The window saved these settings: a model they hold no longer needs its retry, and every failure streak starts
            // over (a model they lack is written below or by the pending retry).
            CancelModelRetriesSavedWithLocked(settings);
            _modelSaveFailures.Clear();

            if (!credentialsChanged && _loopActive && _run is { IsCancellationRequested: false } && _pending is null && _current is { } current && KeepsMonitoring(current, settings))
            {
                // Only notification settings changed: no restart, so the tray keeps the current state (no "connecting").
                // A model detected after the settings window read the running settings must not get lost (T-039).
                settings = WithRunningDetectedModels(settings, current, out List<PrinterDefinition> merged);
                _current = settings;
                foreach (PrinterDefinition printer in merged)
                {
                    QueueModelSaveLocked(new PrinterModelDetected(printer.Id, printer.DetectedModel!.Value));
                }

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
    /// <remarks>
    /// The detected model and the image choice (T-039) only change what is shown, never what is polled: a change of only
    /// those does not restart monitoring (a background save of a detected model must not show "Connecting…").
    /// </remarks>
    private static bool KeepsMonitoring(AppSettings current, AppSettings next) =>
        current.PollingInterval == next.PollingInterval
        && current.Printers.Count == next.Printers.Count
        && current.Printers.Select(WhatIsPolled).SequenceEqual(next.Printers.Select(WhatIsPolled));

    private static PrinterDefinition WhatIsPolled(PrinterDefinition printer) =>
        printer.DetectedModel is null && printer.Image == PrinterImageChoice.Automatic
            ? printer
            : printer with { DetectedModel = null, Image = PrinterImageChoice.Automatic };

    /// <summary>
    /// Monitor thread: a printer of the current run reported its model (T-039, user decision 2026-10-20). Only a real
    /// change is saved, in the background and without restarting monitoring: the current settings get the code, observers
    /// get them as an update (the tray menu shows the new illustration), and the settings file gets only this value
    /// (<see cref="ISettingsStore.SetDetectedModelAsync"/>, which keeps everything else of the file). A report from a run
    /// that is being replaced is dropped (its printer may have another address now); the next run detects again.
    /// </summary>
    private void OnModelDetected(object? sender, PrinterModelDetected detected)
    {
        if (detected is null)
        {
            return;
        }

        AppSettings updated;
        lock (_gate)
        {
            if (_stopping is not null || _disposed || _pending is not null || _run is not { IsCancellationRequested: false } || _current is not { } current)
            {
                return;
            }

            updated = current.WithDetectedModel(detected.PrinterId, detected.Code);
            if (ReferenceEquals(updated, current))
            {
                return;
            }

            _current = updated;
            // Another model: its save gets all retries again.
            _modelSaveFailures.Remove(detected.PrinterId);
            QueueModelSaveLocked(detected);
        }

        UpdateObservers(updated);
    }

    /// <summary>Under <c>_gate</c>: one more background write, after the ones queued before.</summary>
    private void QueueModelSaveLocked(PrinterModelDetected detected)
    {
        Task previous = _modelSaves;
        _modelSaves = Task.Run(() => SaveDetectedModelAsync(previous, detected), CancellationToken.None);
    }

    /// <summary>
    /// After the previous save; never faults. A failure (also a file another process still holds after the store's retries)
    /// is logged and the save is tried again after <see cref="ModelSaveRetryDelay"/>, at most <see cref="MaxModelSaveRetries"/>
    /// times in a row (see <see cref="OnModelSaveFailedLocked"/>);
    /// the model stays in the current settings meanwhile, so a save of the settings window writes it too. Written only while
    /// the current settings still hold this code for the printer, and only into the entry of the same machine (address and
    /// connection type of the current settings): settings applied meanwhile may have moved the printer.
    /// </summary>
    private async Task SaveDetectedModelAsync(Task previous, PrinterModelDetected detected)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        PrinterDefinition? printer;
        lock (_gate)
        {
            printer = FindPrinter(_current, detected.PrinterId);
        }

        if (printer is null || printer.DetectedModel != detected.Code)
        {
            return;
        }

        try
        {
            if (await _settingsStoreFactory().SetDetectedModelAsync(printer.Id, detected.Code, printer.Address, printer.ConnectionType, CancellationToken.None).ConfigureAwait(false))
            {
                LogModelSaved(detected.PrinterId, detected.Code);
            }
        }
        catch (Exception ex)
        {
            int failures;
            bool retrying;
            lock (_gate)
            {
                (failures, retrying) = OnModelSaveFailedLocked(printer.Id);
            }

            if (retrying)
            {
                LogModelNotSavedRetrying(ex, detected.PrinterId, ModelSaveRetryDelay, failures, MaxModelSaveRetries);
            }
            else if (failures > MaxModelSaveRetries)
            {
                LogModelSaveGivenUp(ex, detected.PrinterId, MaxModelSaveRetries);
            }
            else
            {
                LogModelNotSaved(ex, detected.PrinterId);
            }

            return;
        }

        // The file has the latest model now (or never will: the entry moved); an older pending retry has nothing to do.
        lock (_gate)
        {
            CancelModelRetryLocked(printer.Id);
            _modelSaveFailures.Remove(printer.Id);
        }
    }

    /// <summary>
    /// Under <c>_gate</c>: one more failed save of <paramref name="printerId"/>'s detected model. It is tried again after
    /// <see cref="ModelSaveRetryDelay"/> while the streak has retries left (<see cref="MaxModelSaveRetries"/>); after the
    /// last one the session gives up until a new streak starts (a new model, a successful save, a save of the settings
    /// window, the next start).
    /// </summary>
    /// <returns>
    /// The failures of the streak so far (more than <see cref="MaxModelSaveRetries"/>: given up now) and whether a retry is
    /// pending; (0, false) once the session stops.
    /// </returns>
    private (int Failures, bool Retrying) OnModelSaveFailedLocked(string printerId)
    {
        if (_stopping is not null || _disposed)
        {
            return (0, false);
        }

        int failures = _modelSaveFailures.GetValueOrDefault(printerId) + 1;
        if (failures > MaxModelSaveRetries)
        {
            _modelSaveFailures.Remove(printerId);
            CancelModelRetryLocked(printerId);
            return (failures, false);
        }

        _modelSaveFailures[printerId] = failures;
        return (failures, ScheduleModelRetryLocked(printerId));
    }

    /// <summary>
    /// Under <c>_gate</c>: the save of <paramref name="printerId"/>'s detected model is tried again after
    /// <see cref="ModelSaveRetryDelay"/>, unless one is pending already (it saves the latest model) or the session stops.
    /// </summary>
    /// <returns>Whether a retry is pending now.</returns>
    private bool ScheduleModelRetryLocked(string printerId)
    {
        if (_stopping is not null || _disposed)
        {
            return false;
        }

        if (_modelRetries.ContainsKey(printerId))
        {
            return true;
        }

        PendingModelRetry retry = new(this, printerId);
        // Created stopped and armed only once registered, so that it can never fire before OnModelRetryDue can find it.
        retry.Timer = _timeProvider.CreateTimer(static state => ((PendingModelRetry)state!).Fire(), retry, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _modelRetries.Add(printerId, retry);
        retry.Timer.Change(ModelSaveRetryDelay, Timeout.InfiniteTimeSpan);
        return true;
    }

    /// <summary>Timer thread: the retry of a failed save is due; it saves the model the printer has now.</summary>
    private void OnModelRetryDue(PendingModelRetry retry)
    {
        lock (_gate)
        {
            if (!_modelRetries.TryGetValue(retry.PrinterId, out PendingModelRetry? pending) || !ReferenceEquals(pending, retry))
            {
                return;
            }

            CancelModelRetryLocked(retry.PrinterId);
            if (_stopping is not null || _disposed || FindPrinter(_current, retry.PrinterId) is not { DetectedModel: { } code } printer)
            {
                return;
            }

            QueueModelSaveLocked(new PrinterModelDetected(printer.Id, code));
        }
    }

    /// <summary>Under <c>_gate</c>.</summary>
    private void CancelModelRetryLocked(string printerId)
    {
        if (_modelRetries.Remove(printerId, out PendingModelRetry? retry))
        {
            retry.Timer?.Dispose();
        }
    }

    /// <summary>Under <c>_gate</c>: the session stops; no retry may start a save any more.</summary>
    private void CancelModelRetriesLocked()
    {
        foreach (PendingModelRetry retry in _modelRetries.Values)
        {
            retry.Timer?.Dispose();
        }

        _modelRetries.Clear();
        _modelSaveFailures.Clear();
    }

    /// <summary>
    /// Under <c>_gate</c>: <paramref name="saved"/> (saved in the settings window) already holds the running model of a printer
    /// whose save failed, so its retry has nothing left to do.
    /// </summary>
    private void CancelModelRetriesSavedWithLocked(AppSettings saved)
    {
        if (_modelRetries.Count == 0 || _current is not { } current)
        {
            return;
        }

        foreach (PrinterDefinition printer in saved.Printers)
        {
            if (printer.DetectedModel is { } code && _modelRetries.ContainsKey(printer.Id) && FindPrinter(current, printer.Id)?.DetectedModel == code)
            {
                CancelModelRetryLocked(printer.Id);
            }
        }
    }

    private static PrinterDefinition? FindPrinter(AppSettings? settings, string printerId)
    {
        foreach (PrinterDefinition printer in settings?.Printers ?? [])
        {
            if (string.Equals(printer.Id, printerId, StringComparison.OrdinalIgnoreCase))
            {
                return printer;
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="settings"/> (saved in the settings window) with the detected models of the running settings for
    /// printers on the same machine (id, address, connection type): a model detected after the window took the running
    /// settings is newer than what the window saved. <paramref name="merged"/>: printers whose model was taken, whose file
    /// entry still lacks it.
    /// </summary>
    private static AppSettings WithRunningDetectedModels(AppSettings settings, AppSettings running, out List<PrinterDefinition> merged)
    {
        merged = [];
        AppSettings result = settings;
        foreach (PrinterDefinition printer in settings.Printers)
        {
            PrinterDefinition? current = running.Printers.FirstOrDefault(candidate => string.Equals(candidate.Id, printer.Id, StringComparison.OrdinalIgnoreCase));
            if (current?.DetectedModel is { } code
                && code != printer.DetectedModel
                && current.ConnectionType == printer.ConnectionType
                && string.Equals(current.Address, printer.Address, StringComparison.OrdinalIgnoreCase))
            {
                result = result.WithDetectedModel(printer.Id, code);
                merged.Add(printer with { DetectedModel = code });
            }
        }

        return result;
    }

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

            _monitor.ModelDetected -= OnModelDetected;
            CancelModelRetriesLocked();

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
            CancelModelRetriesLocked();
            // CancelAsync runs the registered callbacks on the thread pool, never on the (UI) caller.
            return _stopping ??= _disposed ? Task.CompletedTask : _stop.CancelAsync();
        }
    }

    /// <summary>
    /// Runs the monitor until stopped; each <see cref="ApplyAsync"/> ends the current run and starts the next one with the
    /// applied settings. A failed run is reported and ends the loop unless new settings are already waiting.
    /// </summary>
    private async Task RunLoopAsync(bool loadSettings, CancellationToken stopToken, AppSettings? initialSettings = null, TimeSpan startDelay = default)
    {
        try
        {
            if (startDelay > TimeSpan.Zero)
            {
                await Task.Delay(startDelay, _timeProvider, stopToken).ConfigureAwait(false);
            }

            AppSettings? settings = loadSettings ? await LoadSettingsAsync(_settingsStoreFactory, _logger, stopToken, _removedCameraCredentials).ConfigureAwait(false) : initialSettings;
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
            // Stopped while loading the settings or before the delayed start.
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

    /// <summary>
    /// Credentials found in camera addresses were removed while loading, but they stay in the file until it is written
    /// again: the store removes them from the file right away (T-014 decisions), changing only those addresses, so that
    /// the password does not remain on disk and nothing else of the file (skipped entries, unknown values) is lost. A
    /// failure is logged and monitoring goes on (the next start tries again). The settings window is told which cameras
    /// were affected.
    /// </summary>
    private static async Task RemoveCameraCredentialsFromFileAsync(
        ISettingsStore store,
        AppSettings settings,
        ILogger logger,
        RemovedCameraCredentials? removedCameraCredentials,
        CancellationToken stopToken)
    {
        HashSet<string> affected = new(StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in settings.Printers)
        {
            if (printer.Camera.CredentialsRemoved)
            {
                affected.Add(printer.Id);
            }
        }

        try
        {
            // Also cleans entries that were not loaded (skipped printers, a corrupted file). The notice is shown only for
            // loaded printers with a camera: the settings window cannot show any other.
            IReadOnlyList<string> cleaned = await store.RemoveCameraCredentialsAsync(stopToken).ConfigureAwait(false);
            foreach (string id in cleaned)
            {
                if (settings.Printers.Any(printer => string.Equals(printer.Id, id, StringComparison.OrdinalIgnoreCase) && HasCamera(printer.Camera)))
                {
                    affected.Add(id);
                }
            }

            if (cleaned.Count > 0)
            {
                LogCameraCredentialsRemoved(logger, cleaned.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stopToken.IsCancellationRequested)
        {
            LogCameraCredentialsNotRemoved(logger, ex);
        }

        if (affected.Count > 0)
        {
            removedCameraCredentials?.Add(affected);
        }
    }

    private static bool HasCamera(CameraSettings camera) =>
        camera.StreamUri is not null || camera.InvalidStreamUrl is not null || camera.CredentialsRemoved;

    [LoggerMessage(Level = LogLevel.Error, Message = "Settings could not be loaded; defaults are used.")]
    private static partial void LogSettingsUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored settings were not used ({Status}): {ErrorMessage}")]
    private static partial void LogSettingsNotUsed(ILogger logger, SettingsLoadStatus status, string? errorMessage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings: {Warning}")]
    private static partial void LogSettingsWarning(ILogger logger, string warning);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Credentials were removed from the camera addresses of {Count} printer(s) in the settings file; nothing else was changed.")]
    private static partial void LogCameraCredentialsRemoved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Credentials could not be removed from camera addresses in the settings file; they stay there until the settings are saved.")]
    private static partial void LogCameraCredentialsNotRemoved(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Printer monitoring stopped unexpectedly.")]
    private partial void LogMonitoringFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Showing the monitoring failure in the tray failed.")]
    private partial void LogObserverFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handing updated settings to a tray observer failed.")]
    private partial void LogObserverUpdateFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer monitoring did not stop within {Timeout}.")]
    private partial void LogStopTimedOut(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Information, Message = "The detected model {Code} of printer '{PrinterId}' was saved.")]
    private partial void LogModelSaved(string printerId, PrinterModelCode code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The detected model of printer '{PrinterId}' could not be saved; it is written with the next save of the settings window or detected again at the next start.")]
    private partial void LogModelNotSaved(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The detected model of printer '{PrinterId}' could not be saved; trying again in {RetryDelay} (retry {Retry} of {MaxRetries}).")]
    private partial void LogModelNotSavedRetrying(Exception exception, string printerId, TimeSpan retryDelay, int retry, int maxRetries);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The detected model of printer '{PrinterId}' could still not be saved after {MaxRetries} retries; giving up until the settings window saves the settings, another model is detected or the app starts again.")]
    private partial void LogModelSaveGivenUp(Exception exception, string printerId, int maxRetries);

    /// <summary>The timer of a retry of a failed detected-model save, and whose it is.</summary>
    private sealed class PendingModelRetry(MonitoringSession session, string printerId)
    {
        public string PrinterId { get; } = printerId;

        /// <summary>Set right after creation, before the timer is armed.</summary>
        public ITimer? Timer { get; set; }

        public void Fire() => session.OnModelRetryDue(this);
    }
}
