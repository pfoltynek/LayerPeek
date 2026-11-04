using Microsoft.Extensions.Logging;

using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// The dashcam (T-148, user decisions 2026-11-04) of printers with <see cref="CameraSettings.Dashcam"/>:
/// <list type="bullet">
/// <item><b>When it buffers:</b> only during a print (<see cref="PrinterState.Printing"/>, <see cref="PrinterState.Paused"/>,
/// <see cref="PrinterState.Attention"/>; an unreachable printer for <see cref="CameraRecordingServiceOptions.OfflineGrace"/>)
/// and only while the whole print is not recorded (manual or automatic recording: that file already has it). Otherwise no
/// connection, no memory: a print that ends (any other reachable state) or a recording that starts lets a running save
/// finish and ends the dashcam; one that fails by itself is not started again before the next print.</item>
/// <item><b>Triggers:</b> the printer turns to <c>Error</c>, <c>Attention</c> or <c>Stopped</c> while the dashcam runs
/// (each can be switched off, <see cref="CameraRecordingSettings.SavesDashcamOn"/>), the print failure detection (T-155,
/// <see cref="SaveDashcam"/> with <see cref="CameraDashcamTrigger.FailureDetected"/>) and the user ("Save the last
/// minutes", always).</item>
/// <item><b>A save</b> goes into the printer's folder (<see cref="CameraRecordingFolders.DashcamFileName"/>), after the folder
/// was prepared and the free space checked like a recording, and goes on
/// <see cref="CameraRecordingServiceOptions.DashcamPostEventDuration"/> after the event (another event extends it). The
/// rotation keeps it within the size limit of the recordings.</item>
/// </list>
/// All state is under the service's lock; one worker per entry starts, saves and ends dashcams off that lock (like the
/// recording worker), so a request is never acted on twice and never lost.
/// </summary>
public sealed partial class CameraRecordingService
{
    /// <summary>
    /// Saves the last minutes of <paramref name="printerId"/>'s dashcam (and the stream after now) into a file: "Save the
    /// last minutes" of the UI (<see cref="CameraDashcamTrigger.Manual"/>, always), or the print failure detection (T-155,
    /// <see cref="CameraDashcamTrigger.FailureDetected"/>, unless switched off). During a running save, extends it. How it
    /// goes is told by <see cref="Changed"/> and, when it fails, <see cref="Failed"/>.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when nothing can be saved: the printer is not configured, its dashcam does not buffer
    /// (<see cref="CameraRecordingStatus.Dashcam"/>), the trigger is switched off, or the service is stopping.
    /// </returns>
    public bool SaveDashcam(string printerId, CameraDashcamTrigger trigger = CameraDashcamTrigger.Manual)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        lock (_gate)
        {
            if (_stopping || !_entries.TryGetValue(printerId, out Entry? entry) || !_settings.SavesDashcamOn(trigger)
                || DashcamActivityOf(entry) is not (CameraDashcamActivity.Buffering or CameraDashcamActivity.Saving))
            {
                return false;
            }

            RequestDashcamSave(entry, trigger, entry.DashcamJob);
            return true;
        }
    }

    /// <summary>What the printer's dashcam does; under <see cref="_gate"/>.</summary>
    private CameraDashcamActivity DashcamActivityOf(Entry entry)
    {
        if (!entry.UsesDashcam || entry.Removed || !_recorder.Availability.IsAvailable)
        {
            return CameraDashcamActivity.Off;
        }

        return entry.Dashcam?.State switch
        {
            CameraDashcamState.Saving => CameraDashcamActivity.Saving,
            CameraDashcamState.Buffering when entry.DashcamEnd == DashcamEnd.None => CameraDashcamActivity.Buffering,
            _ => CameraDashcamActivity.Waiting,
        };
    }

    /// <summary>Follows the print for the dashcam and notes the triggers of this snapshot; under <see cref="_gate"/>.</summary>
    private void ApplyDashcamState(Entry entry, PrinterState previous, PrinterState state, PrintJob? job)
    {
        if (!entry.UsesDashcam)
        {
            // Only printers with a dashcam are followed: no timers for the others.
            ResetDashcamPrint(entry);
            return;
        }

        switch (state)
        {
            case PrinterState.Printing or PrinterState.Paused or PrinterState.Attention:
                DisposeTimer(ref entry.DashcamOfflineTimer);
                if (!entry.DashcamPrinting)
                {
                    // A new print: named after its own job, or after none rather than the previous one.
                    entry.DashcamJob = job?.FileName;
                }
                else if (job?.FileName is { } fileName)
                {
                    entry.DashcamJob = fileName;
                }

                entry.DashcamPrinting = true;
                break;

            case PrinterState.Offline or PrinterState.Unknown or PrinterState.Unauthorized:
                // Unknown whether it still prints: tolerated for a while like the automatic recording (T-079).
                if (entry.DashcamPrinting && entry.DashcamOfflineTimer is null)
                {
                    DashcamTimerState timerState = new(this, entry);
                    timerState.Timer = _time.CreateTimer(static s => ((DashcamTimerState)s!).Owner.OnDashcamOfflineTimer((DashcamTimerState)s!), timerState, _options.OfflineGrace, Timeout.InfiniteTimeSpan);
                    entry.DashcamOfflineTimer = timerState.Timer;
                }

                break;

            default:
                // Idle, Busy, Finished, Stopped, Error: the print is over (a save of this snapshot's trigger still runs).
                DisposeTimer(ref entry.DashcamOfflineTimer);
                entry.DashcamPrinting = false;
                entry.DashcamHeldBack = false;
                break;
        }

        // A change into a trigger state while the dashcam holds the print (it exists only during a print or its last save).
        if (state != previous && entry.Dashcam is not null && TriggerOf(state) is { } trigger && _settings.SavesDashcamOn(trigger))
        {
            RequestDashcamSave(entry, trigger, job?.FileName ?? entry.DashcamJob);
        }
    }

    private static CameraDashcamTrigger? TriggerOf(PrinterState state) => state switch
    {
        PrinterState.Error => CameraDashcamTrigger.Error,
        PrinterState.Attention => CameraDashcamTrigger.Attention,
        PrinterState.Stopped => CameraDashcamTrigger.Stopped,
        _ => null,
    };

    private void OnDashcamOfflineTimer(DashcamTimerState state)
    {
        lock (_gate)
        {
            Entry entry = state.Entry;
            if (entry.DashcamOfflineTimer is null || !ReferenceEquals(entry.DashcamOfflineTimer, state.Timer))
            {
                return;
            }

            DisposeTimer(ref entry.DashcamOfflineTimer);
            entry.DashcamPrinting = false;
            entry.DashcamHeldBack = false;
            LogDashcamOfflineEnded(entry.Printer.Id);
            ReconcileDashcamIfNeeded(entry);
        }
    }

    /// <summary>Forgets the print the dashcam followed (dashcam off, printer removed, quitting); under <see cref="_gate"/>.</summary>
    private static void ResetDashcamPrint(Entry entry)
    {
        DisposeTimer(ref entry.DashcamOfflineTimer);
        entry.DashcamPrinting = false;
        entry.DashcamHeldBack = false;
    }

    /// <summary>Notes a save for the worker (the first request names the file, later ones extend it); under <see cref="_gate"/>.</summary>
    private void RequestDashcamSave(Entry entry, CameraDashcamTrigger trigger, string? job)
    {
        entry.DashcamSave ??= new DashcamSaveRequest(trigger, _time.GetLocalNow(), job);
        LogDashcamTrigger(entry.Printer.Id, trigger);
        ReconcileDashcamIfNeeded(entry);
    }

    /// <summary>Whether the printer's dashcam should buffer now; under <see cref="_gate"/>.</summary>
    private bool IsDashcamWanted(Entry entry) =>
        !_stopping && !entry.Removed && entry.UsesDashcam && entry.DashcamPrinting && !entry.DashcamHeldBack
        && _recorder.Availability.IsAvailable
        // The whole print is recorded (or about to be): the recording has the last minutes (user decision 2026-11-04).
        && entry.Activity == CameraRecordingActivity.Idle && entry.Recording is null && !IsWanted(entry);

    /// <summary>How a dashcam that is not wanted ends: at once when it cannot go on at all, otherwise after its save.</summary>
    private DashcamEnd DesiredDashcamEnd(Entry entry) =>
        IsDashcamWanted(entry) ? DashcamEnd.None
        : _stopping || entry.Removed || !entry.UsesDashcam || !_recorder.Availability.IsAvailable ? DashcamEnd.Now
        : DashcamEnd.AfterSave;

    /// <summary>Starts the entry's dashcam worker when there is something to do; under <see cref="_gate"/>.</summary>
    private void ReconcileDashcamIfNeeded(Entry entry)
    {
        bool work = entry.Dashcam is null
            ? IsDashcamWanted(entry)
            : entry.DashcamSave is not null || DesiredDashcamEnd(entry) > entry.DashcamEnd;
        if (entry.Dashcam is null)
        {
            // Nothing holds the last minutes: there is nothing to save.
            entry.DashcamSave = null;
        }

        if (!work)
        {
            return;
        }

        entry.DashcamDirty = true;
        if (entry.DashcamWorker is null && !_stopping)
        {
            entry.DashcamWorker = Track(Task.Run(() => ReconcileDashcamAsync(entry), CancellationToken.None));
        }
    }

    /// <summary>One dashcam worker per entry at a time: saves, starts and ends until nothing is left to do.</summary>
    private async Task ReconcileDashcamAsync(Entry entry)
    {
        while (true)
        {
            DashcamStep step;
            lock (_gate)
            {
                if (!entry.DashcamDirty || _stopping)
                {
                    entry.DashcamWorker = null;
                    entry.DashcamDirty = false;
                    return;
                }

                entry.DashcamDirty = false;
                step = NextDashcamStep(entry);
                if (step.Kind != DashcamStepKind.None)
                {
                    // Look again after this step: e.g. a save first, then the end of the print.
                    entry.DashcamDirty = true;
                }
            }

            switch (step.Kind)
            {
                case DashcamStepKind.Start:
                    StartDashcam(entry, step.Options!);
                    break;
                case DashcamStepKind.Save:
                    BeginDashcamSave(entry, step);
                    break;
                case DashcamStepKind.EndAfterSave:
                    LogDashcamEnding(entry.Printer.Id);
                    step.Dashcam!.EndAfterSave();
                    RaiseChanged(entry.Printer.Id);
                    break;
                case DashcamStepKind.StopNow:
                    RaiseChanged(entry.Printer.Id);
                    await step.Dashcam!.StopAsync().ConfigureAwait(false);
                    break;
            }
        }
    }

    /// <summary>What the worker does next (a save before anything else, so that the end of a print never loses its save); under <see cref="_gate"/>.</summary>
    private DashcamStep NextDashcamStep(Entry entry)
    {
        if (entry.Dashcam is not { } dashcam)
        {
            entry.DashcamSave = null;
            if (!IsDashcamWanted(entry))
            {
                return default;
            }

            entry.DashcamEnd = DashcamEnd.None;
            CameraDashcamOptions options = new()
            {
                BufferDuration = TimeSpan.FromMinutes(_settings.DashcamMinutes),
                PostEventDuration = _options.DashcamPostEventDuration,
            };
            return new DashcamStep(DashcamStepKind.Start, null, options, null, null, null);
        }

        if (entry.DashcamSave is { } request)
        {
            entry.DashcamSave = null;
            string folder = PrinterFolder(entry);
            string path = Path.Combine(folder, CameraRecordingFolders.DashcamFileName(request.EventTime, request.Job, request.Trigger));
            // Protected from the rotation from now on: the dashcam creates the file before it lists it. (When the request
            // only extends a running save, the path is not used; the running save's files are protected as they are listed.)
            entry.DashcamPlannedFile = path;

            return new DashcamStep(DashcamStepKind.Save, dashcam, null, folder, path, request);
        }

        DashcamEnd desired = DesiredDashcamEnd(entry);
        if (desired <= entry.DashcamEnd)
        {
            return default;
        }

        entry.DashcamEnd = desired;
        return new DashcamStep(desired == DashcamEnd.Now ? DashcamStepKind.StopNow : DashcamStepKind.EndAfterSave, dashcam, null, null, null, null);
    }

    /// <summary>On the worker: starts the dashcam and watches its end.</summary>
    private void StartDashcam(Entry entry, CameraDashcamOptions options)
    {
        ICameraDashcam dashcam = _recorder.StartDashcam(
            entry.Printer.Id,
            options,
            changed: _ => RaiseChanged(entry.Printer.Id),
            saved: result => OnDashcamSaved(entry, result));
        bool stopNow;
        lock (_gate)
        {
            entry.Dashcam = dashcam;
            // Quitting meanwhile: the quit no longer saw it, so it is stopped here (and waited for through the tracking).
            stopNow = _stopping;
            if (stopNow)
            {
                entry.DashcamEnd = DashcamEnd.Now;
            }

            Track(ObserveDashcamAsync(entry, dashcam));
        }

        RaiseChanged(entry.Printer.Id);
        if (stopNow)
        {
            Track(dashcam.StopAsync());
        }
    }

    /// <summary>On the worker: prepares the folder and checks the disk like a recording, then asks the dashcam to save.</summary>
    private void BeginDashcamSave(Entry entry, DashcamStep step)
    {
        ICameraDashcam dashcam = step.Dashcam!;
        string printerId = entry.Printer.Id;
        if (dashcam.State == CameraDashcamState.Saving)
        {
            // Another event during the save: the same file goes on (the path is not used).
            dashcam.Save(step.FilePath!);
            return;
        }

        (CameraRecordingProblem Problem, string Message)? problem = null;
        try
        {
            _storage.PrepareFolder(step.Folder!);
            if (_storage.GetAvailableFreeSpace(step.Folder!) is { } free && free < _options.MinFreeBytes)
            {
                problem = (CameraRecordingProblem.DiskSpaceLow, $"Less than {FormatGigabytes(_options.MinFreeBytes)} GB free on the disk of the recording folder.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            bool full = ex is IOException && CameraRecording.IsDiskFull(ex);
            problem = (
                full ? CameraRecordingProblem.DiskFull : CameraRecordingProblem.FolderUnavailable,
                full ? "The disk of the recording folder is full."
                    : ex is ForeignRecordingFolderException ? ex.Message
                    : ex is UnauthorizedAccessException ? "The recording folder cannot be written to." : $"The recording folder cannot be created ({ex.GetType().Name}).");
        }

        if (problem is null)
        {
            try
            {
                if (dashcam.Save(step.FilePath!))
                {
                    string fileName = Path.GetFileName(step.FilePath!);
                    LogDashcamSaveStarted(printerId, step.Request!.Trigger, fileName);
                    return;
                }

                // Ended (or ends after a save) meanwhile: nothing to save.
            }
            catch (ArgumentException ex)
            {
                problem = (CameraRecordingProblem.WriteFailed, $"The recording file cannot be created ({ex.GetType().Name}).");
            }
        }

        CameraRecordingError? error = problem is { } p ? new(printerId, p.Problem, p.Message, _time.GetUtcNow(), CameraRecordingMode.Dashcam) : null;
        lock (_gate)
        {
            if (string.Equals(entry.DashcamPlannedFile, step.FilePath, StringComparison.Ordinal))
            {
                entry.DashcamPlannedFile = null;
            }

            if (error is not null)
            {
                entry.LastError = error;
            }
        }

        if (error is not null)
        {
            LogDashcamSaveFailed(printerId, error.Problem, error.Message);
            RaiseChanged(printerId);
            RaiseFailed(error);
        }
    }

    /// <summary>A save ended (on the dashcam's thread): the newest file, an error, and the rotation.</summary>
    private void OnDashcamSaved(Entry entry, CameraDashcamSave result)
    {
        CameraRecordingError? error = null;
        lock (_gate)
        {
            if (result.Files.Count > 0)
            {
                entry.LastFile = result.Files[^1];
            }

            if (result.Failure != CameraRecordingFailure.None)
            {
                error = new CameraRecordingError(entry.Printer.Id, ToProblem(result.Failure), result.FailureMessage ?? "Saving failed.", _time.GetUtcNow(), CameraRecordingMode.Dashcam);
                entry.LastError = error;
            }
            else if (result.Files.Count > 0)
            {
                entry.LastError = null;
            }

            if (!_stopping)
            {
                RequestRotation();
            }
        }

        RaiseChanged(entry.Printer.Id);
        if (error is not null)
        {
            RaiseFailed(error);
        }
    }

    /// <summary>Clean-up once a dashcam ended, whoever ended it.</summary>
    private async Task ObserveDashcamAsync(Entry entry, ICameraDashcam dashcam)
    {
        // Always after the caller released the lock, also for a dashcam that ended at once.
        await dashcam.Completion.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        CameraRecordingError? error = null;
        lock (_gate)
        {
            if (!ReferenceEquals(entry.Dashcam, dashcam))
            {
                return;
            }

            bool requested = entry.DashcamEnd != DashcamEnd.None;
            // The camera went away together with what ended the dashcam (camera disabled, printer removed): as intended.
            bool cameraGone = dashcam.Failure == CameraRecordingFailure.CameraUnavailable && (requested || entry.Removed || !entry.UsesDashcam);
            if (dashcam.Failure != CameraRecordingFailure.None && !cameraGone)
            {
                error = new CameraRecordingError(
                    entry.Printer.Id, ToProblem(dashcam.Failure), dashcam.FailureMessage ?? "The dashcam failed.", _time.GetUtcNow(), CameraRecordingMode.Dashcam);
                entry.LastError = error;
            }

            if (!requested)
            {
                // Ended by itself (failed, the camera went away, the application quits): not again during this print.
                entry.DashcamHeldBack = true;
            }

            entry.Dashcam = null;
            entry.DashcamEnd = DashcamEnd.None;
            entry.DashcamSave = null;
            entry.DashcamPlannedFile = null;
            ReconcileIfNeeded(entry);
        }

        RaiseChanged(entry.Printer.Id);
        if (error is not null)
        {
            RaiseFailed(error);
        }
    }

    /// <summary>The newest save and the files and bytes of the running one, for the rotation; under <see cref="_gate"/>.</summary>
    private static void CollectActiveDashcam(Entry entry, string root, HashSet<string> files, ref long bytes)
    {
        if (entry.DashcamPlannedFile is { } planned)
        {
            files.Add(planned);
        }

        if (entry.Dashcam is not { } dashcam)
        {
            return;
        }

        IReadOnlyList<string> saving = dashcam.SaveFiles;
        foreach (string file in saving)
        {
            files.Add(file);
        }

        if (saving.Count > 0 && saving[0].StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            bytes += dashcam.SaveBytesWritten;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dashcam of printer '{PrinterId}' saves ({Trigger}).")]
    private partial void LogDashcamTrigger(string printerId, CameraDashcamTrigger trigger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dashcam of printer '{PrinterId}' saves ({Trigger}) into '{FileName}'.")]
    private partial void LogDashcamSaveStarted(string printerId, CameraDashcamTrigger trigger, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dashcam of printer '{PrinterId}' could not save ({Problem}): {Reason}")]
    private partial void LogDashcamSaveFailed(string printerId, CameraRecordingProblem problem, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dashcam of printer '{PrinterId}' ends after its save.")]
    private partial void LogDashcamEnding(string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Printer '{PrinterId}' stayed unreachable; its dashcam ends.")]
    private partial void LogDashcamOfflineEnded(string printerId);

    /// <summary>How a dashcam that is no longer wanted is being ended (ordered: a later value is a harder end).</summary>
    private enum DashcamEnd
    {
        None,
        AfterSave,
        Now,
    }

    private enum DashcamStepKind
    {
        None,
        Start,
        Save,
        EndAfterSave,
        StopNow,
    }

    /// <param name="Trigger">What asked for it (names the file).</param>
    /// <param name="EventTime">When (local time, names the file).</param>
    /// <param name="Job">The print's file name (names the file).</param>
    private sealed record DashcamSaveRequest(CameraDashcamTrigger Trigger, DateTimeOffset EventTime, string? Job);

    private readonly record struct DashcamStep(
        DashcamStepKind Kind,
        ICameraDashcam? Dashcam,
        CameraDashcamOptions? Options,
        string? Folder,
        string? FilePath,
        DashcamSaveRequest? Request);

    private sealed class DashcamTimerState(CameraRecordingService owner, Entry entry)
    {
        public CameraRecordingService Owner { get; } = owner;

        public Entry Entry { get; } = entry;

        public ITimer? Timer { get; set; }
    }
}
