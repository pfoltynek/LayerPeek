using System.Runtime.InteropServices;

using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Events;

/// <summary>
/// Derives <see cref="PrintEvent"/>s from the snapshot sequence of each printer. Rules:
/// <list type="bullet">
/// <item>The first informative snapshot of a printer is the baseline. Problems present then are reported (error,
/// waiting for the user, rejected credentials); anything else (a print already running, an offline printer) is only
/// shown by the tray.</item>
/// <item>Job events compare with the last <em>reachable</em> state, so an outage in between neither repeats nor hides
/// them. A job counts as new when it starts after none was running, or when its identity changes
/// (<see cref="PrintJob.Id"/>, otherwise <see cref="PrintJob.FileName"/>; unknown identity means the same job).
/// <see cref="PrinterState.Busy"/> during a job or after an error / wait for the user is transparent (it neither ends
/// the job nor replaces the pause, wait or error it interrupts).</item>
/// <item>Every event is produced once per change: repeated polls with the same state produce nothing.</item>
/// <item><see cref="PrinterState.Unauthorized"/> produces <see cref="PrintEventKind.CredentialsRejected"/> once, again
/// only after a reachable (authorized) snapshot. Otherwise it, <see cref="PrinterState.Unknown"/> and undefined states
/// carry no information about the printer and are skipped.</item>
/// <item><see cref="PrintEventKind.PrinterOffline"/> only after <see cref="PrintEventDetectorOptions.OfflineDelay"/>,
/// and <see cref="PrintEventKind.PrinterOnline"/> only after a reported offline.</item>
/// <item><see cref="PrintEventKind.RemainingTime"/> once per job, when the estimate drops to the threshold after it was
/// seen above it (a job shorter than the threshold is not reported; estimate jitter around it is ignored).</item>
/// </list>
/// Not thread-safe: callers serialize all calls. Allocates only when a printer is seen for the first time and when
/// events are returned.
/// </summary>
public sealed class PrintEventDetector
{
    private readonly Dictionary<string, PrinterTracker> _trackers = new(StringComparer.OrdinalIgnoreCase);
    private TimeSpan _remainingTimeThreshold;
    private TimeSpan _offlineDelay;

    public PrintEventDetector(PrintEventDetectorOptions? options = null) => UpdateOptions(options ?? PrintEventDetectorOptions.Default);

    /// <summary>Applies new options to future snapshots; what is known about the printers is kept.</summary>
    public void UpdateOptions(PrintEventDetectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _remainingTimeThreshold = options.RemainingTimeThreshold;
        _offlineDelay = options.OfflineDelay;
    }

    /// <summary>Forgets every printer for which <paramref name="isKept"/> returns <see langword="false"/>.</summary>
    public void RetainPrinters(Func<string, bool> isKept)
    {
        ArgumentNullException.ThrowIfNull(isKept);
        foreach (string printerId in _trackers.Keys)
        {
            if (!isKept(printerId))
            {
                // Removing while enumerating is supported by Dictionary.
                _trackers.Remove(printerId);
            }
        }
    }

    /// <summary>Events revealed by <paramref name="snapshot"/>, in the order they happened; empty when nothing changed.</summary>
    public IReadOnlyList<PrintEvent> Process(PrinterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ref PrinterTracker? tracker = ref CollectionsMarshal.GetValueRefOrAddDefault(_trackers, snapshot.PrinterId, out _);
        tracker ??= new PrinterTracker();

        EventList events = new(snapshot);
        switch (snapshot.State)
        {
            case PrinterState.Offline:
                ProcessOffline(tracker, ref events);
                break;
            case PrinterState.Unauthorized:
                ProcessUnauthorized(tracker, ref events);
                break;
            case PrinterState state when state.IsReachable():
                ProcessReachable(tracker, ref events);
                break;
        }

        return events.ToArray();
    }

    private void ProcessOffline(PrinterTracker tracker, ref EventList events)
    {
        DateTimeOffset now = events.Snapshot.Timestamp;
        if (!tracker.IsOffline)
        {
            tracker.IsOffline = true;
            tracker.OfflineSince = now;
            tracker.OfflineNotified = false;
            // Offline at startup is the baseline, like any other first state.
            tracker.OfflineSilent = !tracker.HasBaseline;
        }
        else if (now < tracker.OfflineSince)
        {
            // The clock went back: measure from the earlier time, otherwise the report would be postponed by the jump.
            tracker.OfflineSince = now;
        }

        tracker.HasBaseline = true;
        if (!tracker.OfflineSilent && !tracker.OfflineNotified && now - tracker.OfflineSince >= _offlineDelay)
        {
            tracker.OfflineNotified = true;
            events.Add(PrintEventKind.PrinterOffline, tracker.JobActive ? tracker.JobFileName : null);
        }
    }

    /// <summary>Does not end an outage: the printer answered, but its state is still unknown.</summary>
    private static void ProcessUnauthorized(PrinterTracker tracker, ref EventList events)
    {
        tracker.HasBaseline = true;
        if (!tracker.CredentialsRejectedNotified)
        {
            tracker.CredentialsRejectedNotified = true;
            events.Add(PrintEventKind.CredentialsRejected, fileName: null);
        }
    }

    private void ProcessReachable(PrinterTracker tracker, ref EventList events)
    {
        if (tracker.IsOffline)
        {
            if (tracker.OfflineNotified)
            {
                events.Add(PrintEventKind.PrinterOnline, fileName: null);
            }

            tracker.IsOffline = false;
            tracker.OfflineNotified = false;
        }

        tracker.HasBaseline = true;
        tracker.CredentialsRejectedNotified = false;
        PrinterState state = events.Snapshot.State;
        PrintJob? job = events.Snapshot.Job;
        if (!tracker.HasState)
        {
            StartTracking(tracker, ref events);
            return;
        }

        PrinterState previous = tracker.State;
        bool sameJob = tracker.IsSameJob(job);
        if (sameJob)
        {
            tracker.MergeJob(job);
        }
        else
        {
            // Another job than the one in progress: whatever the state, the old job (and its remaining-time alert) is over.
            tracker.EndJob();
            tracker.SetJob(job);
        }

        switch (state)
        {
            case PrinterState.Printing:
                if (!tracker.JobActive)
                {
                    StartJob(tracker, ref events);
                }
                else if (previous is PrinterState.Paused or PrinterState.Attention)
                {
                    events.Add(PrintEventKind.PrintResumed, tracker.JobFileName);
                }

                break;

            case PrinterState.Paused:
                bool newJob = !tracker.JobActive;
                if (newJob)
                {
                    // Started and paused between two polls.
                    StartJob(tracker, ref events);
                }

                if (newJob || previous != PrinterState.Paused)
                {
                    events.Add(PrintEventKind.PrintPaused, tracker.JobFileName);
                }

                break;

            case PrinterState.Finished:
            case PrinterState.Stopped:
                // Also when the job ran entirely between two polls (or during an outage) and is now reported as ended.
                if (previous != state || !sameJob)
                {
                    events.Add(state == PrinterState.Finished ? PrintEventKind.PrintFinished : PrintEventKind.PrintStopped, tracker.JobFileName);
                }

                tracker.EndJob();
                break;

            case PrinterState.Error:
            case PrinterState.Attention:
                if (previous != state)
                {
                    AddProblem(tracker, state, ref events);
                }

                break;

            case PrinterState.Idle:
                // A job that ended unobserved (e.g. acknowledged during an outage) is forgotten without an event.
                tracker.EndJob();
                tracker.SetJob(null);
                break;

            default:
                // Busy (calibration, preheating...) neither starts nor ends a job.
                break;
        }

        // Busy during a job or a problem is transparent, so that e.g. paused → busy → printing is still a resume and
        // error → busy → error is not reported twice.
        if (state != PrinterState.Busy || (!tracker.JobActive && previous is not (PrinterState.Error or PrinterState.Attention)))
        {
            tracker.State = state;
        }

        UpdateRemainingTime(tracker, ref events);
    }

    /// <summary>First reachable snapshot: only problems the user has to act on are reported.</summary>
    private void StartTracking(PrinterTracker tracker, ref EventList events)
    {
        PrinterState state = events.Snapshot.State;
        PrintJob? job = events.Snapshot.Job;
        tracker.HasState = true;
        tracker.State = state;
        // Waiting for the user or busy with a job means mid-print (e.g. filament runout), so printing it is no new start.
        tracker.JobActive = state.HasActiveJob() || (state is PrinterState.Attention or PrinterState.Busy && job is not null);
        tracker.SetJob(job);
        if (state is PrinterState.Error or PrinterState.Attention)
        {
            AddProblem(tracker, state, ref events);
        }

        UpdateRemainingTime(tracker, ref events);
    }

    private static void AddProblem(PrinterTracker tracker, PrinterState state, ref EventList events) =>
        events.Add(
            state == PrinterState.Error ? PrintEventKind.Error : PrintEventKind.AttentionRequired,
            tracker.JobActive ? tracker.JobFileName : null);

    private static void StartJob(PrinterTracker tracker, ref EventList events)
    {
        tracker.EndJob();
        tracker.JobActive = true;
        events.Add(PrintEventKind.PrintStarted, tracker.JobFileName);
    }

    /// <summary>Arms the alert while the estimate is above the threshold and fires once when it drops to it while printing.</summary>
    private void UpdateRemainingTime(PrinterTracker tracker, ref EventList events)
    {
        if (_remainingTimeThreshold <= TimeSpan.Zero || !tracker.JobActive || events.Snapshot.Job?.EstimatedRemaining is not { } remaining)
        {
            return;
        }

        if (remaining > _remainingTimeThreshold)
        {
            tracker.RemainingTimeArmed = true;
        }
        else if (tracker.RemainingTimeArmed && !tracker.RemainingTimeNotified && events.Snapshot.State == PrinterState.Printing)
        {
            tracker.RemainingTimeNotified = true;
            events.Add(PrintEventKind.RemainingTime, tracker.JobFileName, remaining);
        }
    }

    /// <summary>What is known about one printer.</summary>
    private sealed class PrinterTracker
    {
        /// <summary>A reachable, offline or unauthorized snapshot was seen.</summary>
        public bool HasBaseline;

        /// <summary>A reachable snapshot was seen; <see cref="State"/> is the last reachable state (Busy during a job excluded).</summary>
        public bool HasState;

        public PrinterState State;

        /// <summary>A job is in progress (printing, paused, or waiting for the user during a print).</summary>
        public bool JobActive;

        public long? JobId;

        public string? JobFileName;

        public bool RemainingTimeArmed;

        public bool RemainingTimeNotified;

        public bool IsOffline;

        public DateTimeOffset OfflineSince;

        public bool OfflineNotified;

        /// <summary>The outage started at the baseline; it is neither reported as offline nor as back online.</summary>
        public bool OfflineSilent;

        /// <summary>Reported since the last reachable snapshot.</summary>
        public bool CredentialsRejectedNotified;

        /// <summary>Unknown identity on either side counts as the same job: absent data must not invent a new print.</summary>
        public bool IsSameJob(PrintJob? job)
        {
            if (job is null)
            {
                return true;
            }

            if (job.Id is { } id && JobId is { } knownId)
            {
                return id == knownId;
            }

            return job.FileName is not { } fileName || JobFileName is not { } knownFileName
                || string.Equals(fileName, knownFileName, StringComparison.Ordinal);
        }

        public void SetJob(PrintJob? job)
        {
            JobId = job?.Id;
            JobFileName = job?.FileName;
        }

        /// <summary>Completes the identity of the same job, e.g. a file name that arrives a poll later.</summary>
        public void MergeJob(PrintJob? job)
        {
            if (job is not null)
            {
                JobId = job.Id ?? JobId;
                JobFileName = job.FileName ?? JobFileName;
            }
        }

        public void EndJob()
        {
            JobActive = false;
            RemainingTimeArmed = false;
            RemainingTimeNotified = false;
        }
    }

    /// <summary>At most a few events per snapshot; the array is allocated only when there is one.</summary>
    private ref struct EventList(PrinterSnapshot snapshot)
    {
        private PrintEvent[]? _events;

        public readonly PrinterSnapshot Snapshot { get; } = snapshot;

        public void Add(PrintEventKind kind, string? fileName, TimeSpan? remaining = null)
        {
            PrintEvent printEvent = new(kind, Snapshot.PrinterId, Snapshot.Timestamp, fileName, remaining);
            _events = _events is null ? [printEvent] : [.. _events, printEvent];
        }

        public readonly PrintEvent[] ToArray() => _events ?? [];
    }
}
