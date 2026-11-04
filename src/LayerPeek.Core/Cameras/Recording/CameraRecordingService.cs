using Microsoft.Extensions.Logging;

using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// When and where the cameras are recorded (T-145, user decisions 2026-11-03), over <see cref="ICameraRecorder"/> (T-144):
/// <list type="bullet">
/// <item><b>Manual:</b> <see cref="Start"/> / <see cref="Stop"/>; a manual recording runs until it is stopped or fails.</item>
/// <item><b>Automatic</b> (printers with <see cref="CameraSettings.RecordPrints"/>): starts when the printer prints
/// (<see cref="PrinterState.Printing"/> or <see cref="PrinterState.Paused"/>), goes on through <c>Paused</c> /
/// <c>Attention</c>, and ends <see cref="CameraRecordingServiceOptions.EndDelay"/> after the print ended (any reachable
/// state other than printing, paused or attention), or after <see cref="CameraRecordingServiceOptions.OfflineGrace"/>
/// of an unreachable printer. A print that goes on within these times keeps its recording.</item>
/// <item>One recording per printer: Start during an automatic recording does nothing; Stop ends it, and the automatic
/// recording does not start again before that print ended. A recording that fails is not restarted either (no loop on a
/// full disk).</item>
/// <item>Files: <see cref="CameraRecordingFolders"/>; before a start the folder is prepared and the free space checked
/// (<see cref="CameraRecordingServiceOptions.MinFreeBytes"/>); while something records, free space and the size limit are
/// checked every <see cref="CameraRecordingServiceOptions.DiskCheckInterval"/>. The rotation
/// (<see cref="CameraRecordingRotation"/>) runs at the first <see cref="Configure"/>, when the folder or limit changes,
/// after every recording and when the limit is exceeded.</item>
/// </list>
/// <para>
/// Idle costs nothing: no timer and no task exists while nothing records and no recorded print is about to end. Thread-safe:
/// snapshots arrive concurrently from the monitor, commands from the UI thread; the state is kept under one lock, file work
/// and recordings run on the thread pool, and <see cref="Changed"/> / <see cref="Failed"/> are raised outside the lock on
/// any thread (subscribers marshal themselves; a failing subscriber is logged). Never throws for runtime conditions.
/// </para>
/// </summary>
public sealed partial class CameraRecordingService : IAsyncDisposable, IDisposable
{
    private readonly IPrinterMonitor _monitor;
    private readonly ICameraRecorder _recorder;
    private readonly ICameraRecordingStorage _storage;
    private readonly TimeProvider _time;
    private readonly ILogger<CameraRecordingService> _logger;
    private readonly CameraRecordingServiceOptions _options;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _work = [];
    private CameraRecordingSettings _settings = CameraRecordingSettings.Default;
    private string _root;
    private bool _configured;
    private bool _stopping;
    private Task? _stopped;
    private ITimer? _diskTimer;
    private bool _rotationRunning;
    private bool _rotationPending;
    private long _storedBytes;
    private IReadOnlyDictionary<string, string> _newestByFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public CameraRecordingService(
        IPrinterMonitor monitor,
        ICameraRecorder recorder,
        ICameraRecordingStorage storage,
        TimeProvider timeProvider,
        ILogger<CameraRecordingService> logger,
        CameraRecordingServiceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _monitor = monitor;
        _recorder = recorder;
        _storage = storage;
        _time = timeProvider;
        _logger = logger;
        _options = options ?? CameraRecordingServiceOptions.Default;
        _root = NormalizeRoot(_options.DefaultRoot);
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
    }

    /// <summary>The status of a printer changed (id as configured); read it with <see cref="GetStatus"/>. Not raised for the growing size.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>A recording could not start or ended with an error (the only one notified, user decision 2026-11-03).</summary>
    public event EventHandler<CameraRecordingError>? Failed;

    /// <summary>Whether this build can record at all (T-144: e.g. no MP4 muxer); the UI disables recording otherwise.</summary>
    public CameraRecorderAvailability Availability => _recorder.Availability;

    /// <summary>The folder new recordings go to (the root; every printer has a subfolder in it), e.g. for "Open folder".</summary>
    public string RecordingsFolder
    {
        get
        {
            lock (_gate)
            {
                return _root;
            }
        }
    }

    /// <summary>
    /// Records <paramref name="printers"/> with <paramref name="settings"/> from now on. Call before every monitoring run and
    /// whenever the settings change. Printers that stay configured and point to the same machine keep their state and
    /// running recording (a renamed printer records into its new folder from its next recording; a new camera address
    /// continues in a new file, T-144). A removed printer, a disabled camera or a turned off
    /// <see cref="CameraSettings.RecordPrints"/> of a running automatic recording stop the recording (no error). A new
    /// folder applies to the next recording.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="printers"/> contains <see langword="null"/>.</exception>
    public void Configure(IReadOnlyList<PrinterDefinition> printers, CameraRecordingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(printers);
        ArgumentNullException.ThrowIfNull(settings);
        foreach (PrinterDefinition? printer in printers)
        {
            if (printer is null)
            {
                throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            }
        }

        IReadOnlyDictionary<string, string> folders = CameraRecordingFolders.PrinterFolderNames(printers);
        List<string> changed = [];
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            Dictionary<string, Entry> entries = new(printers.Count, StringComparer.OrdinalIgnoreCase);
            foreach (PrinterDefinition printer in printers)
            {
                if (entries.ContainsKey(printer.Id))
                {
                    continue;
                }

                if (_entries.TryGetValue(printer.Id, out Entry? entry) && IsSameMachine(entry.Printer, printer))
                {
                    bool recordedPrints = entry.RecordsPrints;
                    bool couldRecord = entry.CanRecord;
                    entry.Printer = printer;
                    entry.FolderName = folders[printer.Id];
                    if (!entry.CanRecord)
                    {
                        entry.ManualWanted = false;
                    }

                    if (!entry.RecordsPrints)
                    {
                        CancelPrintTimers(entry);
                        entry.Printing = false;
                        entry.Suppressed = false;
                    }

                    if (recordedPrints != entry.RecordsPrints || couldRecord != entry.CanRecord)
                    {
                        changed.Add(printer.Id);
                    }
                }
                else
                {
                    entry = new Entry(printer, folders[printer.Id]);
                }

                entries.Add(printer.Id, entry);
                ReconcileIfNeeded(entry);
            }

            foreach (Entry previous in _entries.Values)
            {
                if (!entries.TryGetValue(previous.Printer.Id, out Entry? current) || !ReferenceEquals(current, previous))
                {
                    previous.Removed = true;
                    CancelPrintTimers(previous);
                    ReconcileIfNeeded(previous);
                    if (previous.Activity != CameraRecordingActivity.Idle)
                    {
                        changed.Add(previous.Printer.Id);
                    }
                }
            }

            _entries = entries;
            string root = NormalizeRoot(settings.Folder ?? _options.DefaultRoot);
            bool rotate = !_configured || !string.Equals(root, _root, StringComparison.Ordinal) || settings.SizeLimitGigabytes != _settings.SizeLimitGigabytes;
            _settings = settings;
            _root = root;
            _configured = true;
            if (rotate)
            {
                RequestRotation();
            }
        }

        RaiseChanged(changed);
    }

    /// <summary>
    /// The folder the recordings of <paramref name="printerId"/> (case-insensitive) go to: its subfolder of
    /// <see cref="RecordingsFolder"/> (<see cref="CameraRecordingFolders.PrinterFolderNames"/>), which may not exist yet;
    /// <see langword="null"/> when the printer is not configured.
    /// </summary>
    public string? GetPrinterFolder(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        lock (_gate)
        {
            return _entries.TryGetValue(printerId, out Entry? entry) ? PrinterFolder(entry) : null;
        }
    }

    /// <summary>The recording state of <paramref name="printerId"/> (case-insensitive); <see cref="CameraRecordingStatus.None"/> when not configured.</summary>
    public CameraRecordingStatus GetStatus(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        lock (_gate)
        {
            if (!_entries.TryGetValue(printerId, out Entry? entry))
            {
                return CameraRecordingStatus.None;
            }

            ICameraRecording? recording = entry.Recording;
            IReadOnlyList<string> files = recording?.Files ?? [];
            return new CameraRecordingStatus
            {
                Activity = entry.Activity,
                Mode = entry.Activity == CameraRecordingActivity.Idle ? null : entry.Mode,
                StartedAt = entry.Activity == CameraRecordingActivity.Idle ? null : entry.StartedAt,
                CurrentFile = files.Count > 0 && entry.Activity == CameraRecordingActivity.Recording ? files[^1] : null,
                BytesWritten = recording?.BytesWritten ?? 0,
                Duration = recording?.Duration ?? TimeSpan.Zero,
                LastFile = entry.LastFile ?? _newestByFolder.GetValueOrDefault(entry.FolderName),
                LastError = entry.LastError,
                RecordsPrints = entry.RecordsPrints,
                CanRecord = entry.CanRecord,
            };
        }
    }

    /// <summary>
    /// Starts a manual recording of <paramref name="printerId"/>. Does nothing while it records already (also
    /// automatically: one recording, user decision 2026-11-03). How it goes is told by <see cref="Changed"/> and, when it
    /// cannot start, <see cref="Failed"/>.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when it cannot even be tried: the printer is not configured, its camera is disabled or the
    /// service is stopping. <see langword="true"/> otherwise (also when it records already).
    /// </returns>
    public bool Start(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        lock (_gate)
        {
            if (_stopping || !_entries.TryGetValue(printerId, out Entry? entry) || !entry.CanRecord)
            {
                return false;
            }

            if (entry.Activity == CameraRecordingActivity.Idle && entry.Recording is null)
            {
                entry.ManualWanted = true;
                ReconcileIfNeeded(entry);
            }

            return true;
        }
    }

    /// <summary>
    /// Stops the recording of <paramref name="printerId"/>, manual or automatic; an automatic one does not start again
    /// before its print ended (user decision 2026-11-03). Does nothing when it does not record.
    /// </summary>
    public void Stop(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        lock (_gate)
        {
            if (!_entries.TryGetValue(printerId, out Entry? entry))
            {
                return;
            }

            entry.ManualWanted = false;
            if (entry.Printing)
            {
                entry.Suppressed = true;
            }

            ReconcileIfNeeded(entry);
        }
    }

    /// <summary>
    /// Stops every recording and waits until each file is complete (quitting: the last fragment and the end are written),
    /// then starts nothing any more. Idempotent; never throws. The caller limits the wait.
    /// </summary>
    public Task StopAllAsync()
    {
        lock (_gate)
        {
            if (_stopped is { } stopped)
            {
                return stopped;
            }

            _stopping = true;
            _diskTimer?.Dispose();
            _diskTimer = null;
            List<ICameraRecording> recordings = [];
            foreach (Entry entry in _entries.Values)
            {
                CancelPrintTimers(entry);
                entry.ManualWanted = false;
                if (entry.Recording is { } recording && !entry.StopRequested)
                {
                    entry.StopRequested = true;
                    entry.Activity = CameraRecordingActivity.Stopping;
                    recordings.Add(recording);
                }
            }

            // Off this thread: nothing of a recording's stop runs under the lock.
            _stopped = Task.Run(() => StopAllCoreAsync(recordings), CancellationToken.None);
            return _stopped;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        await StopAllAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Stops every recording without waiting (a container disposed synchronously, e.g. after a failed start); quitting
    /// waits with <see cref="StopAllAsync"/>.
    /// </summary>
    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        _ = StopAllAsync();
    }

    private async Task StopAllCoreAsync(List<ICameraRecording> recordings)
    {
        foreach (ICameraRecording recording in recordings)
        {
            LogStoppingForQuit(recording.PrinterId);
        }

        // StopAsync never throws (T-144); each completes its file.
        await Task.WhenAll(recordings.Select(static recording => recording.StopAsync())).ConfigureAwait(false);

        // Starts in progress (they see _stopping and stop what they started), their observers and rotations.
        while (true)
        {
            Task[] work;
            lock (_gate)
            {
                _work.RemoveAll(static task => task.IsCompleted);
                work = [.. _work];
            }

            if (work.Length == 0)
            {
                return;
            }

            await Task.WhenAll(work).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    internal void OnSnapshotUpdated(object? sender, PrinterSnapshot snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        lock (_gate)
        {
            // Snapshots of printers that are not (or no longer) configured are ignored.
            if (_stopping || !_entries.TryGetValue(snapshot.PrinterId, out Entry? entry))
            {
                return;
            }

            if (snapshot.Job?.FileName is { } fileName)
            {
                entry.JobFileName = fileName;
            }

            entry.LastState = snapshot.State;
            ApplyState(entry, snapshot.State);
            ReconcileIfNeeded(entry);
        }
    }

    /// <summary>The print rules of the automatic recording (see the class); under <see cref="_gate"/>.</summary>
    private void ApplyState(Entry entry, PrinterState state)
    {
        if (!entry.RecordsPrints)
        {
            // Prints are only followed for printers that record them: no timers for the others. Switched on mid-print,
            // the next snapshot of the print starts it.
            CancelPrintTimers(entry);
            entry.Printing = false;
            entry.Suppressed = false;
            return;
        }

        switch (state)
        {
            case PrinterState.Printing or PrinterState.Paused:
                CancelPrintTimers(entry);
                entry.Printing = true;
                break;

            case PrinterState.Attention:
                // Waiting for the user mid-print: it goes on (decision 3). Without a print it starts nothing.
                if (entry.Printing)
                {
                    CancelPrintTimers(entry);
                }

                break;

            case PrinterState.Finished or PrinterState.Stopped or PrinterState.Error or PrinterState.Idle or PrinterState.Busy:
                DisposeTimer(ref entry.OfflineTimer);
                if (entry.Printing)
                {
                    // One more minute (decision 2); a print that goes on within it keeps the recording.
                    entry.EndTimer ??= CreatePrintTimer(entry, _options.EndDelay, end: true);
                }
                else
                {
                    // The print ended: a manual Stop no longer holds back the next one.
                    entry.Suppressed = false;
                }

                break;

            default:
                // Offline, Unknown, Unauthorized: unknown whether it still prints; tolerated for a while (decision 2, T-079).
                if (entry.Printing && entry.EndTimer is null)
                {
                    entry.OfflineTimer ??= CreatePrintTimer(entry, _options.OfflineGrace, end: false);
                }

                break;
        }
    }

    private ITimer CreatePrintTimer(Entry entry, TimeSpan due, bool end)
    {
        // The callback finds its timer through the entry: a cancelled or replaced timer does nothing.
        TimerState state = new(this, entry, end);
        ITimer timer = _time.CreateTimer(static s => ((TimerState)s!).Owner.OnPrintTimer((TimerState)s!), state, due, Timeout.InfiniteTimeSpan);
        state.Timer = timer;
        return timer;
    }

    private void OnPrintTimer(TimerState state)
    {
        lock (_gate)
        {
            Entry entry = state.Entry;
            ref ITimer? slot = ref state.End ? ref entry.EndTimer : ref entry.OfflineTimer;
            if (slot is null || !ReferenceEquals(slot, state.Timer))
            {
                return;
            }

            DisposeTimer(ref slot);
            entry.Printing = false;
            if (state.End)
            {
                entry.Suppressed = false;
            }
            else
            {
                LogOfflineEnded(entry.Printer.Id);
            }

            ReconcileIfNeeded(entry);
        }
    }

    private static void CancelPrintTimers(Entry entry)
    {
        DisposeTimer(ref entry.EndTimer);
        DisposeTimer(ref entry.OfflineTimer);
    }

    private static void DisposeTimer(ref ITimer? timer)
    {
        timer?.Dispose();
        timer = null;
    }

    /// <summary>Whether the printer should record now; under <see cref="_gate"/>.</summary>
    private bool IsWanted(Entry entry) =>
        !_stopping && !entry.Removed && entry.CanRecord
        && (entry.ManualWanted || (entry.Printing && entry.RecordsPrints && !entry.Suppressed));

    /// <summary>Starts the entry's worker when what should be differs from what is; under <see cref="_gate"/>.</summary>
    private void ReconcileIfNeeded(Entry entry)
    {
        bool running = entry.Activity is CameraRecordingActivity.Starting or CameraRecordingActivity.Recording;
        if (IsWanted(entry) == running)
        {
            return;
        }

        if (entry.Worker is not null)
        {
            entry.Dirty = true;
            return;
        }

        if (_stopping)
        {
            return;
        }

        entry.Dirty = true;
        entry.Worker = Track(Task.Run(() => ReconcileAsync(entry), CancellationToken.None));
    }

    /// <summary>One worker per entry at a time: starts or stops until what is matches what should be.</summary>
    private async Task ReconcileAsync(Entry entry)
    {
        while (true)
        {
            StartPlan? start = null;
            ICameraRecording? stop = null;
            lock (_gate)
            {
                if (!entry.Dirty)
                {
                    entry.Worker = null;
                    return;
                }

                entry.Dirty = false;
                bool wanted = IsWanted(entry);
                if (wanted && entry.Activity == CameraRecordingActivity.Idle)
                {
                    entry.Activity = CameraRecordingActivity.Starting;
                    entry.Mode = entry.ManualWanted ? CameraRecordingMode.Manual : CameraRecordingMode.Automatic;
                    entry.StartedAt = _time.GetUtcNow();
                    string folder = PrinterFolder(entry);
                    bool hasJob = entry.Printing || entry.LastState is PrinterState.Printing or PrinterState.Paused or PrinterState.Attention;
                    string fileName = CameraRecordingFolders.FileName(_time.GetLocalNow(), hasJob ? entry.JobFileName : null);
                    // Protected from the rotation from now on: the recorder creates the file before it lists it.
                    entry.PlannedFile = Path.Combine(folder, fileName);
                    start = new StartPlan(entry.Printer.Id, _root, folder, fileName, entry.Mode);
                }
                else if (!wanted && entry.Recording is { } recording && !entry.StopRequested)
                {
                    entry.StopRequested = true;
                    entry.Activity = CameraRecordingActivity.Stopping;
                    stop = recording;
                }
            }

            if (start is not null)
            {
                RaiseChanged(entry.Printer.Id);
                StartRecording(entry, start);
            }
            else if (stop is not null)
            {
                RaiseChanged(entry.Printer.Id);
                LogStopping(entry.Printer.Id);
                await stop.StopAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Prepares the folder, checks the disk and starts the recorder; on the worker (thread pool).</summary>
    private void StartRecording(Entry entry, StartPlan plan)
    {
        CameraRecorderAvailability availability = _recorder.Availability;
        if (!availability.IsAvailable)
        {
            FailStart(entry, plan, CameraRecordingProblem.Unavailable, availability.Reason ?? "Recording is not available.");
            return;
        }

        try
        {
            _storage.PrepareFolder(plan.Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            bool full = ex is IOException && CameraRecording.IsDiskFull(ex);
            FailStart(
                entry,
                plan,
                full ? CameraRecordingProblem.DiskFull : CameraRecordingProblem.FolderUnavailable,
                full ? "The disk of the recording folder is full."
                    : ex is ForeignRecordingFolderException ? ex.Message
                    : ex is UnauthorizedAccessException ? "The recording folder cannot be written to." : $"The recording folder cannot be created ({ex.GetType().Name}).");
            return;
        }

        if (_storage.GetAvailableFreeSpace(plan.Folder) is { } free && free < _options.MinFreeBytes)
        {
            FailStart(entry, plan, CameraRecordingProblem.DiskSpaceLow, $"Less than {FormatGigabytes(_options.MinFreeBytes)} GB free on the disk of the recording folder.");
            return;
        }

        ICameraRecording recording;
        try
        {
            recording = _recorder.Start(plan.PrinterId, Path.Combine(plan.Folder, plan.FileName), r => OnRecordingChanged(entry, r));
        }
        catch (ArgumentException ex)
        {
            FailStart(entry, plan, CameraRecordingProblem.WriteFailed, $"The recording file cannot be created ({ex.GetType().Name}).");
            return;
        }

        bool stopNow;
        lock (_gate)
        {
            entry.Recording = recording;
            entry.Root = plan.Root;
            entry.Activity = recording.State == CameraRecordingState.Recording ? CameraRecordingActivity.Recording : CameraRecordingActivity.Starting;
            // Stopped, removed or quitting meanwhile: the worker's next round (or the quit) stops it.
            stopNow = _stopping && !entry.StopRequested;
            if (stopNow)
            {
                entry.StopRequested = true;
                entry.Activity = CameraRecordingActivity.Stopping;
            }

            entry.Dirty = true;
            EnsureDiskTimer();
            Track(ObserveAsync(entry, recording));
        }

        LogStarted(plan.PrinterId, plan.Mode, plan.FileName);
        RaiseChanged(plan.PrinterId);
        if (stopNow)
        {
            Track(recording.StopAsync());
        }

    }

    private void FailStart(Entry entry, StartPlan plan, CameraRecordingProblem problem, string message)
    {
        CameraRecordingError error = new(plan.PrinterId, problem, message, _time.GetUtcNow(), plan.Mode);
        lock (_gate)
        {
            entry.Activity = CameraRecordingActivity.Idle;
            entry.PlannedFile = null;
            HoldBack(entry);
            entry.LastError = error;
        }

        LogStartFailed(plan.PrinterId, problem, message);
        RaiseChanged(plan.PrinterId);
        RaiseFailed(error);
    }

    /// <summary>After an error: neither the manual wish nor this print starts it again (no retry loop); under <see cref="_gate"/>.</summary>
    private static void HoldBack(Entry entry)
    {
        entry.ManualWanted = false;
        if (entry.Printing)
        {
            entry.Suppressed = true;
        }
    }

    /// <summary>The recorder's callback (state or new file), on the recording's thread.</summary>
    private void OnRecordingChanged(Entry entry, ICameraRecording recording)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(entry.Recording, recording) || recording.State != CameraRecordingState.Recording)
            {
                // Not assigned yet (StartRecording reads the state itself) or ending (ObserveAsync handles it).
                return;
            }

            if (entry.Activity == CameraRecordingActivity.Starting)
            {
                entry.Activity = CameraRecordingActivity.Recording;
                entry.LastError = null;
            }
        }

        RaiseChanged(entry.Printer.Id);
    }

    /// <summary>Clean-up once a recording ended, whoever ended it.</summary>
    private async Task ObserveAsync(Entry entry, ICameraRecording recording)
    {
        // Always after the caller released the lock, also for a recording that ended at once.
        await recording.Completion.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        CameraRecordingError? error = null;
        lock (_gate)
        {
            if (!ReferenceEquals(entry.Recording, recording))
            {
                return;
            }

            IReadOnlyList<string> files = recording.Files;
            if (files.Count > 0)
            {
                entry.LastFile = files[^1];
            }

            if (entry.PendingProblem is { } pending)
            {
                error = new CameraRecordingError(entry.Printer.Id, pending.Problem, pending.Message, _time.GetUtcNow(), entry.Mode);
            }
            else if (recording.State == CameraRecordingState.Failed && !IsIntendedEnd(entry, recording))
            {
                error = new CameraRecordingError(
                    entry.Printer.Id, ToProblem(recording.Failure), recording.FailureMessage ?? "Recording failed.", _time.GetUtcNow(), entry.Mode);
            }

            bool requested = entry.StopRequested;
            entry.Recording = null;
            entry.Root = null;
            entry.PlannedFile = null;
            entry.StopRequested = false;
            entry.PendingProblem = null;
            entry.Activity = CameraRecordingActivity.Idle;
            if (error is not null || !requested)
            {
                // Failed, or ended on its own (the camera stopped for good, the app quits): not started again by itself.
                HoldBack(entry);
            }

            if (error is not null)
            {
                entry.LastError = error;
            }

            if (!AnyRecording())
            {
                _diskTimer?.Dispose();
                _diskTimer = null;
            }

            ReconcileIfNeeded(entry);
            if (!_stopping)
            {
                RequestRotation();
            }
        }

        LogEnded(entry.Printer.Id, recording.Files.Count);
        RaiseChanged(entry.Printer.Id);
        if (error is not null)
        {
            RaiseFailed(error);
        }
    }

    /// <summary>
    /// A recording that the service stopped and that failed only because its camera went away at the same moment (camera
    /// disabled, printer removed: the hub ends the lease too) ended as intended.
    /// </summary>
    private static bool IsIntendedEnd(Entry entry, ICameraRecording recording) =>
        recording.Failure == CameraRecordingFailure.CameraUnavailable && (entry.StopRequested || entry.Removed || !entry.CanRecord);

    private static CameraRecordingProblem ToProblem(CameraRecordingFailure failure) => failure switch
    {
        CameraRecordingFailure.Unavailable => CameraRecordingProblem.Unavailable,
        CameraRecordingFailure.CameraUnavailable => CameraRecordingProblem.CameraUnavailable,
        CameraRecordingFailure.DiskFull => CameraRecordingProblem.DiskFull,
        CameraRecordingFailure.TooSlow => CameraRecordingProblem.TooSlow,
        CameraRecordingFailure.StreamFailed => CameraRecordingProblem.StreamFailed,
        _ => CameraRecordingProblem.WriteFailed,
    };

    private bool AnyRecording()
    {
        foreach (Entry entry in _entries.Values)
        {
            if (entry.Recording is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The periodic disk check exists only while something records; under <see cref="_gate"/>.</summary>
    private void EnsureDiskTimer()
    {
        if (_diskTimer is null && !_stopping)
        {
            _diskTimer = _time.CreateTimer(static s => ((CameraRecordingService)s!).CheckDisk(), this, _options.DiskCheckInterval, _options.DiskCheckInterval);
        }
    }

    /// <summary>Free space of every running recording's folder, and the size limit by the bytes written (no file enumeration).</summary>
    internal void CheckDisk()
    {
        List<(Entry Entry, ICameraRecording Recording, string Folder)> running = [];
        long activeBytes = 0;
        long limit;
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            foreach (Entry entry in _entries.Values)
            {
                if (entry is { Recording: { } recording, Root: { } root, StopRequested: false })
                {
                    running.Add((entry, recording, Path.Combine(root, entry.FolderName)));
                    if (string.Equals(root, _root, StringComparison.Ordinal))
                    {
                        activeBytes += recording.BytesWritten;
                    }
                }
            }

            limit = _settings.SizeLimitBytes;
            if (_storedBytes + activeBytes > limit)
            {
                RequestRotation();
            }
        }

        foreach ((Entry entry, ICameraRecording recording, string folder) in running)
        {
            if (_storage.GetAvailableFreeSpace(folder) is { } free && free < _options.MinFreeBytes)
            {
                StopWithError(entry, recording, CameraRecordingProblem.DiskSpaceLow, $"Less than {FormatGigabytes(_options.MinFreeBytes)} GB free on the disk of the recording folder.");
            }
        }
    }

    /// <summary>Stops a running recording on the service's own account; <see cref="ObserveAsync"/> reports the error.</summary>
    private void StopWithError(Entry entry, ICameraRecording recording, CameraRecordingProblem problem, string message)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(entry.Recording, recording) || entry.StopRequested)
            {
                return;
            }

            entry.StopRequested = true;
            entry.PendingProblem = (problem, message);
            entry.Activity = CameraRecordingActivity.Stopping;
        }

        LogStoppingWithError(entry.Printer.Id, problem);
        Track(recording.StopAsync());
        RaiseChanged(entry.Printer.Id);
    }

    /// <summary>Runs the rotation on the thread pool, one at a time (a request meanwhile runs it once more); under <see cref="_gate"/>.</summary>
    private void RequestRotation()
    {
        if (_rotationRunning)
        {
            _rotationPending = true;
            return;
        }

        _rotationRunning = true;
        Track(Task.Run(Rotate, CancellationToken.None));
    }

    private void Rotate()
    {
        while (true)
        {
            string root;
            long limit;
            HashSet<string> activeFiles = new(StringComparer.OrdinalIgnoreCase);
            long activeBytes = 0;
            lock (_gate)
            {
                _rotationPending = false;
                root = _root;
                limit = _settings.SizeLimitBytes;
                foreach (Entry entry in _entries.Values)
                {
                    CollectActive(entry, root, activeFiles, ref activeBytes);
                }
            }

            CameraRecordingRotationResult? result = null;
            try
            {
                result = CameraRecordingRotation.Run(_storage, root, limit, activeFiles, activeBytes, _time.GetUtcNow().UtcDateTime);
            }
            catch (Exception ex)
            {
                // Defensive: the storage never throws, but a rotation must never take the service down.
                LogRotationFailed(ex);
            }

            List<string> changed = [];
            List<(Entry, ICameraRecording)> tooBig = [];
            bool done;
            lock (_gate)
            {
                if (result is not null && string.Equals(root, _root, StringComparison.Ordinal))
                {
                    HashSet<string> deleted = new(result.Deleted, StringComparer.OrdinalIgnoreCase);
                    foreach (Entry entry in _entries.Values)
                    {
                        string? before = entry.LastFile ?? _newestByFolder.GetValueOrDefault(entry.FolderName);
                        if (entry.LastFile is { } last && deleted.Contains(last))
                        {
                            entry.LastFile = null;
                        }

                        string? after = entry.LastFile ?? result.NewestByFolder.GetValueOrDefault(entry.FolderName);
                        if (!string.Equals(before, after, StringComparison.Ordinal))
                        {
                            changed.Add(entry.Printer.Id);
                        }

                        if (result.OverLimit && entry is { Recording: { } recording, StopRequested: false } && string.Equals(entry.Root, root, StringComparison.Ordinal))
                        {
                            tooBig.Add((entry, recording));
                        }
                    }

                    _storedBytes = result.StoredBytes;
                    _newestByFolder = result.NewestByFolder;
                }

                done = !_rotationPending || _stopping;
                if (done)
                {
                    _rotationRunning = false;
                }
            }

            if (result is not null)
            {
                if (result.Deleted.Count > 0 || result.FailedDeletes > 0)
                {
                    LogRotated(result.Deleted.Count, result.FailedDeletes, result.StoredBytes);
                }
            }

            RaiseChanged(changed);
            foreach ((Entry entry, ICameraRecording recording) in tooBig)
            {
                StopWithError(entry, recording, CameraRecordingProblem.SizeLimitReached, "The running recording alone exceeds the size limit of the recordings.");
            }

            if (done)
            {
                return;
            }
        }
    }

    private static void CollectActive(Entry entry, string root, HashSet<string> files, ref long bytes)
    {
        if (entry.PlannedFile is { } planned)
        {
            files.Add(planned);
        }

        if (entry.Recording is not { } recording)
        {
            return;
        }

        foreach (string file in recording.Files)
        {
            files.Add(file);
        }

        if (string.Equals(entry.Root, root, StringComparison.Ordinal))
        {
            bytes += recording.BytesWritten;
        }
    }

    /// <summary>Remembers a task until it completed, so that <see cref="StopAllAsync"/> can wait for it (takes the lock, which is re-entrant).</summary>
    private Task Track(Task task)
    {
        lock (_gate)
        {
            _work.RemoveAll(static t => t.IsCompleted);
            _work.Add(task);
        }

        return task;
    }

    private static bool IsSameMachine(PrinterDefinition before, PrinterDefinition after) =>
        before.ConnectionType == after.ConnectionType && string.Equals(before.Address, after.Address, StringComparison.Ordinal);

    /// <summary>
    /// The root as the file system names it (no <c>..</c>, doubled or trailing separators), so that the paths of running
    /// recordings compare equal to the enumerated ones of the rotation.
    /// </summary>
    private static string NormalizeRoot(string root)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return root;
        }
    }

    private static string FormatGigabytes(long bytes) => (bytes / 1_000_000_000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private void RaiseChanged(List<string> printerIds)
    {
        foreach (string printerId in printerIds)
        {
            RaiseChanged(printerId);
        }
    }

    private void RaiseChanged(string printerId)
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        foreach (EventHandler<string> handler in Delegate.EnumerateInvocationList(handlers))
        {
            try
            {
                handler(this, printerId);
            }
            catch (Exception ex)
            {
                LogSubscriberFailed(ex, nameof(Changed), printerId);
            }
        }
    }

    private void RaiseFailed(CameraRecordingError error)
    {
        if (Failed is not { } handlers)
        {
            return;
        }

        foreach (EventHandler<CameraRecordingError> handler in Delegate.EnumerateInvocationList(handlers))
        {
            try
            {
                handler(this, error);
            }
            catch (Exception ex)
            {
                LogSubscriberFailed(ex, nameof(Failed), error.PrinterId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording of printer '{PrinterId}' started ({Mode}) into '{FileName}'.")]
    private partial void LogStarted(string printerId, CameraRecordingMode mode, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording of printer '{PrinterId}' could not start ({Problem}): {Reason}")]
    private partial void LogStartFailed(string printerId, CameraRecordingProblem problem, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stopping the recording of printer '{PrinterId}'.")]
    private partial void LogStopping(string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping the recording of printer '{PrinterId}' because the application quits.")]
    private partial void LogStoppingForQuit(string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping the recording of printer '{PrinterId}': {Problem}.")]
    private partial void LogStoppingWithError(string printerId, CameraRecordingProblem problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "Printer '{PrinterId}' stayed unreachable; its automatic recording ends.")]
    private partial void LogOfflineEnded(string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recording of printer '{PrinterId}' ended ({Files} file(s)).")]
    private partial void LogEnded(string printerId, int files);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording rotation deleted {Deleted} file(s), {Failed} could not be deleted; {StoredBytes} bytes of recordings remain.")]
    private partial void LogRotated(int deleted, int failed, long storedBytes);

    [LoggerMessage(Level = LogLevel.Error, Message = "The recording rotation failed.")]
    private partial void LogRotationFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A {EventName} subscriber of the recording service failed for printer '{PrinterId}'.")]
    private partial void LogSubscriberFailed(Exception exception, string eventName, string printerId);

    private sealed record StartPlan(string PrinterId, string Root, string Folder, string FileName, CameraRecordingMode Mode);

    private sealed class TimerState(CameraRecordingService owner, Entry entry, bool end)
    {
        public CameraRecordingService Owner { get; } = owner;

        public Entry Entry { get; } = entry;

        public bool End { get; } = end;

        public ITimer? Timer { get; set; }
    }

    /// <summary>The folder of <paramref name="entry"/> in the current root. Under <see cref="_gate"/>.</summary>
    private string PrinterFolder(Entry entry) => Path.Combine(_root, entry.FolderName);

    /// <summary>One configured printer; every field guarded by <see cref="_gate"/>.</summary>
    private sealed class Entry(PrinterDefinition printer, string folderName)
    {
        public PrinterDefinition Printer = printer;
        public string FolderName = folderName;

        // The print (automatic recording).
        public bool Printing;
        public bool Suppressed;
        public ITimer? EndTimer;
        public ITimer? OfflineTimer;
        public string? JobFileName;
        public PrinterState LastState;

        // The wish and what is.
        public bool ManualWanted;
        public bool Removed;
        public ICameraRecording? Recording;
        public string? Root;

        /// <summary>The file a starting or running recording was asked to write (its later files add <c>-2</c>, <c>-3</c> …).</summary>
        public string? PlannedFile;
        public CameraRecordingActivity Activity;
        public CameraRecordingMode Mode;
        public DateTimeOffset? StartedAt;
        public bool StopRequested;
        public (CameraRecordingProblem Problem, string Message)? PendingProblem;
        public string? LastFile;
        public CameraRecordingError? LastError;
        public Task? Worker;
        public bool Dirty;

        public bool CanRecord => Printer.Camera.Enabled;

        public bool RecordsPrints => Printer.Camera.Enabled && Printer.Camera.RecordPrints;
    }
}
