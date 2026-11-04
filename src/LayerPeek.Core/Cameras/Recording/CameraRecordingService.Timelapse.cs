using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// The timelapse (T-160, user decisions 2026-11-04; experimental), over <see cref="ICameraTimelapseRecorder"/> (T-158):
/// <list type="bullet">
/// <item><b>Automatic</b> (printers with <see cref="CameraSettings.TimelapsePrints"/>): follows the same prints with the same
/// rules as the automatic recording: starts with <see cref="PrinterState.Printing"/> / <see cref="PrinterState.Paused"/>,
/// ends <see cref="CameraRecordingServiceOptions.EndDelay"/> after the print or after
/// <see cref="CameraRecordingServiceOptions.OfflineGrace"/> of an unreachable printer, and another job goes into its own
/// file (the same job identity as the recording).</item>
/// <item><b>Manual:</b> <see cref="StartTimelapse"/> / <see cref="StopTimelapse"/>. A manual timelapse ends when it is
/// stopped, fails, or with the end of the print it saw (started during a print, or a print that started meanwhile), by the
/// rules of the automatic one; started without a print and no print follows, it runs until it is stopped. Like a recording:
/// one timelapse per printer, Start during an automatic one does nothing, Stop also ends an automatic one, which does not
/// start again before its print ended; one that fails is not restarted during that print.</item>
/// <item><b>Frames:</b> a trigger (<see cref="ICameraTimelapseTrigger"/>, T-160 the time trigger, one timer per running
/// timelapse) asks for a key frame every <see cref="CameraRecordingSettings.TimelapseIntervalSeconds"/>; none while the
/// printer is <see cref="PrinterState.Paused"/> or <see cref="PrinterState.Attention"/> (the file stays open, the camera
/// connection too; a request still waiting for its key frame is withdrawn, so at most a frame already being written lands
/// after the pause started).</item>
/// <item><b>Independent</b> of the recording and the dashcam: each has its own packet lease of the same connection (T-143),
/// and stopping one never touches the other.</item>
/// <item><b>Files:</b> the printer's folder (<see cref="CameraRecordingFolders.TimelapseFileName"/>), prepared and checked
/// like a recording; counted in the size limit and deleted by the rotation like a recording; the disk check runs while a
/// timelapse runs.</item>
/// </list>
/// All state is under the service's lock; one worker per entry starts and stops timelapses off that lock.
/// </summary>
public sealed partial class CameraRecordingService
{
    private static readonly CameraRecorderAvailability NoTimelapseRecorder = CameraRecorderAvailability.Unavailable("This build takes no timelapses.");

    /// <summary>Whether this build can take timelapses (the same MP4 muxer as recordings); the UI disables them otherwise.</summary>
    public CameraRecorderAvailability TimelapseAvailability => _timelapses?.Availability ?? NoTimelapseRecorder;

    /// <summary>
    /// Starts a manual timelapse of <paramref name="printerId"/> (tray menu, camera window, T-161). Does nothing while one
    /// runs already (also automatically). How it goes is told by <see cref="Changed"/> and, when it cannot start,
    /// <see cref="Failed"/> (<see cref="CameraRecordingError.IsTimelapse"/>).
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when it cannot even be tried: the printer is not configured, its camera is disabled, this
    /// service takes no timelapses or it is stopping. <see langword="true"/> otherwise (also when one runs already).
    /// </returns>
    public bool StartTimelapse(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        lock (_gate)
        {
            if (_stopping || _timelapses is null || !_entries.TryGetValue(printerId, out Entry? entry) || !entry.CanRecord)
            {
                return false;
            }

            TimelapseSlot timelapse = entry.Timelapse;
            if (timelapse.Activity == CameraRecordingActivity.Idle && timelapse.Current is null)
            {
                bool followed = entry.FollowsPrints;
                timelapse.ManualWanted = true;
                timelapse.ManualSawPrint = false;
                if (!followed)
                {
                    // Prints were not followed so far: the one it is started in (if any) is from now on, it ends with it.
                    ApplyState(entry, entry.LastState, entry.LastJob);
                }

                // A print that already ended (its minute runs) is not this timelapse's: it goes on until a print it sees.
                timelapse.ManualSawPrint = entry.Printing && entry.EndTimer is null;
                ReconcileIfNeeded(entry);
            }

            return true;
        }
    }

    /// <summary>
    /// Stops the timelapse of <paramref name="printerId"/>, manual or automatic; an automatic one does not start again
    /// before its print ended (like a recording). Does nothing when none runs. The recording is not touched.
    /// </summary>
    public void StopTimelapse(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        lock (_gate)
        {
            if (!_entries.TryGetValue(printerId, out Entry? entry))
            {
                return;
            }

            TimelapseSlot timelapse = entry.Timelapse;
            timelapse.ManualWanted = false;
            timelapse.ManualSawPrint = false;
            if (entry.Printing)
            {
                timelapse.Suppressed = true;
            }

            if (!entry.FollowsPrints)
            {
                StopFollowingPrints(entry);
            }

            ReconcileIfNeeded(entry);
        }
    }

    /// <summary>Frames are taken in every state but a paused or waiting print (user decision 2026-11-04).</summary>
    private static bool IsCapturingState(PrinterState state) => state is not (PrinterState.Paused or PrinterState.Attention);

    /// <summary>The timelapse part of <see cref="GetStatus"/>; under <see cref="_gate"/>.</summary>
    private CameraTimelapseStatus TimelapseStatusOf(Entry entry)
    {
        TimelapseSlot timelapse = entry.Timelapse;
        ICameraTimelapse? current = timelapse.Current;
        bool idle = timelapse.Activity == CameraRecordingActivity.Idle;
        IReadOnlyList<string> files = current?.Files ?? [];
        return new CameraTimelapseStatus
        {
            Activity = timelapse.Activity,
            Mode = idle ? null : timelapse.Mode,
            StartedAt = idle ? null : timelapse.StartedAt,
            IsCapturing = timelapse.Activity is CameraRecordingActivity.Starting or CameraRecordingActivity.Recording && IsCapturingState(entry.LastState),
            CurrentFile = files.Count > 0 && timelapse.Activity == CameraRecordingActivity.Recording ? files[^1] : null,
            FrameCount = current?.FrameCount ?? 0,
            BytesWritten = current?.BytesWritten ?? 0,
            Duration = current?.Duration ?? TimeSpan.Zero,
            LastFile = timelapse.LastFile ?? _newestTimelapseByFolder.GetValueOrDefault(entry.FolderName),
            LastError = timelapse.LastError,
            TimelapsePrints = entry.TimelapsesPrints,
            Interval = _settings.TimelapseInterval,
        };
    }

    /// <summary>
    /// Hands the snapshot to the running timelapse's trigger and pauses or resumes it; under <see cref="_gate"/>.
    /// </summary>
    /// <returns>Whether <see cref="CameraTimelapseStatus.IsCapturing"/> changed (raise <see cref="Changed"/>).</returns>
    private static bool ApplyTimelapseState(Entry entry, PrinterState previous, PrinterSnapshot snapshot)
    {
        TimelapseSlot timelapse = entry.Timelapse;
        if (timelapse.Trigger is { } trigger)
        {
            trigger.OnSnapshot(snapshot);
            trigger.SetCapturing(IsCapturingState(snapshot.State));
        }

        return timelapse.Activity is CameraRecordingActivity.Starting or CameraRecordingActivity.Recording
            && IsCapturingState(previous) != IsCapturingState(snapshot.State);
    }

    /// <summary>
    /// The printer prints another job than the followed one: the old print is over, so a manual timelapse of it ends and a
    /// Stop no longer holds the next one back; a running timelapse ends now and the new job gets its own file; under
    /// <see cref="_gate"/>.
    /// </summary>
    private void StartNextTimelapsePrint(Entry entry)
    {
        TimelapseSlot timelapse = entry.Timelapse;
        timelapse.Suppressed = false;
        EndManualTimelapseWithPrint(entry);
        if (timelapse.Activity is CameraRecordingActivity.Starting or CameraRecordingActivity.Recording)
        {
            timelapse.NextFile = true;
            LogTimelapseNextPrint(entry.Printer.Id);
        }
    }

    /// <summary>The followed print ended: so does a manual timelapse that saw it; under <see cref="_gate"/>.</summary>
    private void EndManualTimelapseWithPrint(Entry entry)
    {
        TimelapseSlot timelapse = entry.Timelapse;
        if (timelapse.ManualWanted && timelapse.ManualSawPrint)
        {
            timelapse.ManualWanted = false;
            LogTimelapseEndsWithPrint(entry.Printer.Id);
        }

        timelapse.ManualSawPrint = false;
    }

    /// <summary>The interval changed: the running timelapses go on with triggers of the new interval; under <see cref="_gate"/>.</summary>
    private void ReplaceTimelapseTriggers()
    {
        foreach (Entry entry in _entries.Values)
        {
            TimelapseSlot timelapse = entry.Timelapse;
            if (timelapse is { Trigger: not null, Current: { } current })
            {
                DisposeTrigger(timelapse);
                StartTrigger(entry, current);
            }
        }
    }

    /// <summary>
    /// Creates the trigger of a timelapse instead of the one chosen by the settings (tests: a trigger that records its
    /// calls). <see langword="null"/>: by the settings. Set before the first timelapse starts.
    /// </summary>
    internal Func<ICameraTimelapse, ICameraTimelapseTrigger>? TriggerOverride { get; init; }

    /// <summary>
    /// The only place that chooses the trigger of a timelapse (T-160: by time; T-159 adds the layer trigger here) and starts
    /// it in the printer's current state; under <see cref="_gate"/>.
    /// </summary>
    private void StartTrigger(Entry entry, ICameraTimelapse current)
    {
        TimelapseSlot timelapse = entry.Timelapse;
        timelapse.Trigger = TriggerOverride is { } create ? create(current) : new CameraTimelapseTimeTrigger(current, _settings.TimelapseInterval, _time);
        timelapse.Trigger.SetCapturing(IsCapturingState(entry.LastState));
    }

    private static void DisposeTrigger(TimelapseSlot timelapse)
    {
        timelapse.Trigger?.Dispose();
        timelapse.Trigger = null;
    }

    /// <summary>Quitting: the running timelapse to stop (and wait for), if any; under <see cref="_gate"/>.</summary>
    private static ICameraTimelapse? StopTimelapseForQuit(Entry entry)
    {
        TimelapseSlot timelapse = entry.Timelapse;
        timelapse.ManualWanted = false;
        DisposeTrigger(timelapse);
        if (timelapse.Current is not { } current || timelapse.StopRequested)
        {
            return null;
        }

        timelapse.StopRequested = true;
        timelapse.Activity = CameraRecordingActivity.Stopping;
        return current;
    }

    /// <summary>Whether the printer should take a timelapse now; under <see cref="_gate"/>.</summary>
    private bool IsTimelapseWanted(Entry entry) =>
        !_stopping && !entry.Removed && entry.CanRecord && _timelapses is not null
        && (entry.Timelapse.ManualWanted || (entry.Printing && entry.TimelapsesPrints && !entry.Timelapse.Suppressed));

    /// <summary>Starts the entry's timelapse worker when what should be differs from what is; under <see cref="_gate"/>.</summary>
    private void ReconcileTimelapseIfNeeded(Entry entry)
    {
        TimelapseSlot timelapse = entry.Timelapse;
        bool running = timelapse.Activity is CameraRecordingActivity.Starting or CameraRecordingActivity.Recording;
        if (IsTimelapseWanted(entry) == running && !(running && timelapse.NextFile))
        {
            return;
        }

        timelapse.Dirty = true;
        if (timelapse.Worker is null && !_stopping)
        {
            timelapse.Worker = Track(Task.Run(() => ReconcileTimelapseAsync(entry), CancellationToken.None));
        }
    }

    /// <summary>
    /// One timelapse worker per entry at a time: starts or stops until what is matches what should be. A timelapse asked for
    /// a next file (<see cref="TimelapseSlot.NextFile"/>) is stopped and, once it ended, a new one starts.
    /// </summary>
    private async Task ReconcileTimelapseAsync(Entry entry)
    {
        TimelapseSlot timelapse = entry.Timelapse;
        while (true)
        {
            TimelapseStartPlan? start = null;
            ICameraTimelapse? stop = null;
            lock (_gate)
            {
                if (!timelapse.Dirty)
                {
                    timelapse.Worker = null;
                    return;
                }

                timelapse.Dirty = false;
                bool wanted = IsTimelapseWanted(entry);
                if (wanted && timelapse.Activity == CameraRecordingActivity.Idle)
                {
                    timelapse.Activity = CameraRecordingActivity.Starting;
                    timelapse.NextFile = false;
                    timelapse.Mode = timelapse.ManualWanted ? CameraRecordingMode.Manual : CameraRecordingMode.Automatic;
                    timelapse.StartedAt = _time.GetUtcNow();
                    string folder = PrinterFolder(entry);
                    bool hasJob = entry.Printing || entry.LastState is PrinterState.Printing or PrinterState.Paused or PrinterState.Attention;
                    string fileName = CameraRecordingFolders.TimelapseFileName(_time.GetLocalNow(), hasJob ? entry.JobFileName : null);
                    // Protected from the rotation from now on: the recorder creates the file before it lists it.
                    timelapse.PlannedFile = Path.Combine(folder, fileName);
                    start = new TimelapseStartPlan(entry.Printer.Id, _root, folder, fileName, timelapse.Mode);
                }
                else if ((!wanted || timelapse.NextFile) && timelapse.Current is { } current && !timelapse.StopRequested)
                {
                    timelapse.StopRequested = true;
                    timelapse.Activity = CameraRecordingActivity.Stopping;
                    DisposeTrigger(timelapse);
                    stop = current;
                }
            }

            if (start is not null)
            {
                RaiseChanged(entry.Printer.Id);
                StartTimelapseCore(entry, start);
            }
            else if (stop is not null)
            {
                RaiseChanged(entry.Printer.Id);
                LogTimelapseStopping(entry.Printer.Id);
                await stop.StopAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Prepares the folder, checks the disk and starts the timelapse with its trigger; on the worker (thread pool).</summary>
    private void StartTimelapseCore(Entry entry, TimelapseStartPlan plan)
    {
        ICameraTimelapseRecorder recorder = _timelapses!;
        CameraRecorderAvailability availability = recorder.Availability;
        if (!availability.IsAvailable)
        {
            FailTimelapseStart(entry, plan, CameraRecordingProblem.Unavailable, availability.Reason ?? "Timelapses are not available.");
            return;
        }

        if (PrepareToWrite(plan.Folder) is { } problem)
        {
            FailTimelapseStart(entry, plan, problem.Problem, problem.Message);
            return;
        }

        ICameraTimelapse current;
        try
        {
            current = recorder.Start(plan.PrinterId, Path.Combine(plan.Folder, plan.FileName), t => OnTimelapseChanged(entry, t));
        }
        catch (ArgumentException ex)
        {
            FailTimelapseStart(entry, plan, CameraRecordingProblem.WriteFailed, $"The timelapse file cannot be created ({ex.GetType().Name}).");
            return;
        }

        TimelapseSlot timelapse = entry.Timelapse;
        bool stopNow;
        lock (_gate)
        {
            timelapse.Current = current;
            timelapse.Root = plan.Root;
            timelapse.Activity = current.State == CameraRecordingState.Recording ? CameraRecordingActivity.Recording : CameraRecordingActivity.Starting;
            // Quitting meanwhile: the quit no longer saw it, so it is stopped here (and waited for through the tracking).
            stopNow = _stopping && !timelapse.StopRequested;
            if (stopNow)
            {
                timelapse.StopRequested = true;
                timelapse.Activity = CameraRecordingActivity.Stopping;
            }
            else
            {
                StartTrigger(entry, current);
            }

            // Stopped or removed meanwhile: the worker's next round stops it.
            timelapse.Dirty = true;
            EnsureDiskTimer();
            Track(ObserveTimelapseAsync(entry, current));
        }

        LogTimelapseStarted(plan.PrinterId, plan.Mode, plan.FileName);
        RaiseChanged(plan.PrinterId);
        if (stopNow)
        {
            Track(current.StopAsync());
        }
    }

    private void FailTimelapseStart(Entry entry, TimelapseStartPlan plan, CameraRecordingProblem problem, string message)
    {
        CameraRecordingError error = new(plan.PrinterId, problem, message, _time.GetUtcNow(), plan.Mode) { IsTimelapse = true };
        lock (_gate)
        {
            TimelapseSlot timelapse = entry.Timelapse;
            timelapse.Activity = CameraRecordingActivity.Idle;
            timelapse.PlannedFile = null;
            HoldBackTimelapse(entry);
            timelapse.LastError = error;
        }

        LogTimelapseStartFailed(plan.PrinterId, problem, message);
        RaiseChanged(plan.PrinterId);
        RaiseFailed(error);
    }

    /// <summary>After an error: neither the manual wish nor this print starts it again (no retry loop); under <see cref="_gate"/>.</summary>
    private static void HoldBackTimelapse(Entry entry)
    {
        TimelapseSlot timelapse = entry.Timelapse;
        timelapse.ManualWanted = false;
        timelapse.ManualSawPrint = false;
        if (entry.Printing)
        {
            timelapse.Suppressed = true;
        }
    }

    /// <summary>The timelapse's callback (state or new file), on the timelapse's thread.</summary>
    private void OnTimelapseChanged(Entry entry, ICameraTimelapse current)
    {
        lock (_gate)
        {
            TimelapseSlot timelapse = entry.Timelapse;
            if (!ReferenceEquals(timelapse.Current, current) || current.State != CameraRecordingState.Recording)
            {
                // Not assigned yet (StartTimelapseCore reads the state itself) or ending (ObserveTimelapseAsync handles it).
                return;
            }

            if (timelapse.Activity == CameraRecordingActivity.Starting)
            {
                timelapse.Activity = CameraRecordingActivity.Recording;
                timelapse.LastError = null;
            }
        }

        RaiseChanged(entry.Printer.Id);
    }

    /// <summary>Clean-up once a timelapse ended, whoever ended it.</summary>
    private async Task ObserveTimelapseAsync(Entry entry, ICameraTimelapse current)
    {
        // Always after the caller released the lock, also for a timelapse that ended at once.
        await current.Completion.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        TimelapseSlot timelapse = entry.Timelapse;
        CameraRecordingError? error = null;
        lock (_gate)
        {
            if (!ReferenceEquals(timelapse.Current, current))
            {
                return;
            }

            IReadOnlyList<string> files = current.Files;
            if (files.Count > 0)
            {
                timelapse.LastFile = files[^1];
            }

            if (timelapse.PendingProblem is { } pending)
            {
                error = new CameraRecordingError(entry.Printer.Id, pending.Problem, pending.Message, _time.GetUtcNow(), timelapse.Mode) { IsTimelapse = true };
            }
            else if (current.State == CameraRecordingState.Failed && !IsIntendedTimelapseEnd(entry, current))
            {
                error = new CameraRecordingError(
                    entry.Printer.Id, ToProblem(current.Failure), current.FailureMessage ?? "The timelapse failed.", _time.GetUtcNow(), timelapse.Mode)
                {
                    IsTimelapse = true,
                };
            }

            bool requested = timelapse.StopRequested;
            DisposeTrigger(timelapse);
            timelapse.Current = null;
            timelapse.Root = null;
            timelapse.PlannedFile = null;
            timelapse.StopRequested = false;
            timelapse.PendingProblem = null;
            timelapse.Activity = CameraRecordingActivity.Idle;
            if (error is not null || !requested)
            {
                // Failed, or ended on its own (the camera stopped for good, the app quits): not started again by itself.
                HoldBackTimelapse(entry);
            }

            if (error is not null)
            {
                timelapse.LastError = error;
            }

            DisposeDiskTimerWhenIdle();
            ReconcileIfNeeded(entry);
            if (!_stopping)
            {
                RequestRotation();
            }
        }

        LogTimelapseEnded(entry.Printer.Id, current.FrameCount, current.Files.Count);
        RaiseChanged(entry.Printer.Id);
        if (error is not null)
        {
            RaiseFailed(error);
        }
    }

    /// <summary>
    /// A timelapse that the service stopped and that failed only because its camera went away at the same moment (camera
    /// disabled, printer removed: the hub ends the lease too) ended as intended.
    /// </summary>
    private static bool IsIntendedTimelapseEnd(Entry entry, ICameraTimelapse current) =>
        current.Failure == CameraRecordingFailure.CameraUnavailable && (entry.Timelapse.StopRequested || entry.Removed || !entry.CanRecord);

    /// <summary>Stops a running timelapse on the service's own account; <see cref="ObserveTimelapseAsync"/> reports the error.</summary>
    private void StopTimelapseWithError(Entry entry, ICameraTimelapse current, CameraRecordingProblem problem, string message)
    {
        lock (_gate)
        {
            TimelapseSlot timelapse = entry.Timelapse;
            if (!ReferenceEquals(timelapse.Current, current) || timelapse.StopRequested)
            {
                return;
            }

            timelapse.StopRequested = true;
            timelapse.PendingProblem = (problem, message);
            timelapse.Activity = CameraRecordingActivity.Stopping;
            DisposeTrigger(timelapse);
        }

        LogTimelapseStoppingWithError(entry.Printer.Id, problem);
        Track(current.StopAsync());
        RaiseChanged(entry.Printer.Id);
    }

    /// <summary>The files and bytes of a starting or running timelapse, for the rotation; under <see cref="_gate"/>.</summary>
    private static void CollectActiveTimelapse(Entry entry, string root, HashSet<string> files, ref long bytes)
    {
        TimelapseSlot timelapse = entry.Timelapse;
        if (timelapse.PlannedFile is { } planned)
        {
            files.Add(planned);
        }

        if (timelapse.Current is not { } current)
        {
            return;
        }

        foreach (string file in current.Files)
        {
            files.Add(file);
        }

        if (string.Equals(timelapse.Root, root, StringComparison.Ordinal))
        {
            bytes += current.BytesWritten;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Timelapse of printer '{PrinterId}' started ({Mode}) into '{FileName}'.")]
    private partial void LogTimelapseStarted(string printerId, CameraRecordingMode mode, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Timelapse of printer '{PrinterId}' could not start ({Problem}): {Reason}")]
    private partial void LogTimelapseStartFailed(string printerId, CameraRecordingProblem problem, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stopping the timelapse of printer '{PrinterId}'.")]
    private partial void LogTimelapseStopping(string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping the timelapse of printer '{PrinterId}' because the application quits.")]
    private partial void LogTimelapseStoppingForQuit(string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping the timelapse of printer '{PrinterId}': {Problem}.")]
    private partial void LogTimelapseStoppingWithError(string printerId, CameraRecordingProblem problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "Printer '{PrinterId}' prints another job; its timelapse continues in a new file.")]
    private partial void LogTimelapseNextPrint(string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The print of printer '{PrinterId}' ended; so does its manual timelapse.")]
    private partial void LogTimelapseEndsWithPrint(string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Timelapse of printer '{PrinterId}' ended ({Frames} frame(s), {Files} file(s)).")]
    private partial void LogTimelapseEnded(string printerId, long frames, int files);

    private sealed record TimelapseStartPlan(string PrinterId, string Root, string Folder, string FileName, CameraRecordingMode Mode);

    /// <summary>The timelapse of one printer (<see cref="Entry.Timelapse"/>); every field guarded by <see cref="_gate"/>.</summary>
    private sealed class TimelapseSlot
    {
        // The wish.
        public bool ManualWanted;

        /// <summary>The manual timelapse saw a print (<see cref="Entry.Printing"/>): it ends with that print.</summary>
        public bool ManualSawPrint;

        /// <summary>Stopped (or failed) during the followed print: the automatic timelapse waits for the next print.</summary>
        public bool Suppressed;

        /// <summary>Another print started: the running timelapse ends and a new one starts (a new file).</summary>
        public bool NextFile;

        // What is.
        public ICameraTimelapse? Current;

        /// <summary>Asks <see cref="Current"/> for frames; exists only while it runs and is not being stopped.</summary>
        public ICameraTimelapseTrigger? Trigger;
        public string? Root;

        /// <summary>The file a starting or running timelapse was asked to write (its later files add <c>-2</c>, <c>-3</c> …).</summary>
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
    }
}
