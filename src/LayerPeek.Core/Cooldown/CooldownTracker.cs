using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cooldown;

/// <summary>
/// Follows the nozzle of one printer cooling down after a print (T-079), from that printer's snapshot sequence. Rules
/// (user decisions 2026-10-24):
/// <list type="bullet">
/// <item>A cool-down starts when a print ends as <see cref="PrinterState.Finished"/> or <see cref="PrinterState.Stopped"/>,
/// not after <see cref="PrinterState.Error"/> (an error during the print, or before the stop, rules the print out). A print
/// ends when one of these states follows a printing / paused snapshot, when it is the first ended print seen (e.g. at app
/// start), or when another job (<see cref="PrintJob.Id"/>, otherwise <see cref="PrintJob.FileName"/>) is reported as
/// ended. Every print starts at most one cool-down.</item>
/// <item>It is tracked from the first snapshot with the nozzle heater off (target 0 or unknown), so that a heater that
/// is switched off a poll late does not count as heating again.</item>
/// <item>It ends without an event when the next print starts (<see cref="PrinterState.Printing"/> /
/// <see cref="PrinterState.Paused"/>), when the printer stays <see cref="PrinterState.Offline"/> for
/// <see cref="CooldownTrackerOptions.OfflineGrace"/>, or when the nozzle heats again (target above 0). Otherwise
/// <see cref="CooldownPhase.CooledDown"/> lasts, also when the threshold is lowered later.</item>
/// <item>Only the nozzle decides. Readings that are missing or implausible (not finite, below
/// <see cref="MinPlausibleCelsius"/> or above <see cref="MaxPlausibleCelsius"/>) are skipped. The nozzle counts as cooled
/// down after <see cref="ConfirmReadings"/> consecutive valid readings at or below the threshold, so that a single sensor
/// glitch neither cools it down nor fires the event.</item>
/// <item>The event fires once, on <see cref="CooldownPhase.Cooling"/> → <see cref="CooldownPhase.CooledDown"/>. A print that
/// ended with the nozzle already at or below the threshold goes straight to <see cref="CooldownPhase.CooledDown"/>
/// (after the confirming reading) without an event.</item>
/// <item><see cref="PrinterState.Unauthorized"/>, <see cref="PrinterState.Unknown"/> and undefined states carry no
/// information and change nothing.</item>
/// </list>
/// Not thread-safe: callers serialize all calls. Allocates nothing after construction.
/// </summary>
public sealed class CooldownTracker
{
    /// <summary>Consecutive valid readings at or below the threshold that confirm "cooled down".</summary>
    public const int ConfirmReadings = 2;

    public const double MinPlausibleCelsius = 0;

    public const double MaxPlausibleCelsius = 500;

    private readonly CooldownEstimator _estimator = new();
    private readonly TimeSpan _offlineGrace;

    private CooldownState _state;
    private string? _printerId;
    private DateTimeOffset _lastTimestamp;

    // The current or last job and what happened to it.
    private bool _printSeen;
    private bool _endedByError;
    private bool _anyPrintEnded;
    private long? _jobId;
    private string? _jobFileName;

    private DateTimeOffset? _offlineSince;

    // The cool-down.
    private Stage _stage;
    private DateTimeOffset _startedAt;
    private string? _coolingFileName;
    private bool _cooled;
    private double _highestNozzle = double.NaN;
    private double _lastNozzle = double.NaN;
    private double _previousNozzle = double.NaN;
    private double? _bed;

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="thresholdCelsius"/> is not a valid <see cref="CooldownThreshold"/>.</exception>
    public CooldownTracker(int thresholdCelsius = CooldownThreshold.DefaultCelsius, CooldownTrackerOptions? options = null)
    {
        CooldownThreshold.Validate(thresholdCelsius, nameof(thresholdCelsius));
        ThresholdCelsius = thresholdCelsius;
        _offlineGrace = (options ?? CooldownTrackerOptions.Default).OfflineGrace;
    }

    private enum Stage
    {
        None,

        /// <summary>The print ended while the nozzle heater was still on.</summary>
        WaitingForHeaterOff,

        Active,
    }

    public int ThresholdCelsius { get; private set; }

    /// <summary>The state after the last call.</summary>
    public CooldownState State => _state;

    public CooldownUpdate Process(PrinterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _printerId = snapshot.PrinterId;
        _lastTimestamp = snapshot.Timestamp;
        switch (snapshot.State)
        {
            case PrinterState.Offline:
                ProcessOffline(snapshot.Timestamp);
                return new CooldownUpdate(_state, null);
            case PrinterState state when state.IsReachable():
                _offlineSince = null;
                return ProcessReachable(snapshot);
            default:
                return new CooldownUpdate(_state, null);
        }
    }

    /// <summary>
    /// Applies a new threshold right away: progress and estimate follow it, and a raised threshold that the last readings
    /// already meet cools the printer down (with the event when the nozzle was above it). "Cooled down" stays when it is
    /// lowered.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="thresholdCelsius"/> is not a valid <see cref="CooldownThreshold"/>.</exception>
    public CooldownUpdate SetThreshold(int thresholdCelsius)
    {
        CooldownThreshold.Validate(thresholdCelsius, nameof(thresholdCelsius));
        if (thresholdCelsius == ThresholdCelsius)
        {
            return new CooldownUpdate(_state, null);
        }

        ThresholdCelsius = thresholdCelsius;
        if (_stage != Stage.Active)
        {
            return new CooldownUpdate(_state, null);
        }

        CooldownEvent? cooled = EvaluateCooledDown();
        _state = BuildState(_lastTimestamp);
        return new CooldownUpdate(_state, cooled);
    }

    private void ProcessOffline(DateTimeOffset timestamp)
    {
        if (_offlineSince is not { } since || timestamp < since)
        {
            // Also when the clock went back: measure from the earlier time.
            _offlineSince = timestamp;
        }

        if (_stage != Stage.None && timestamp - _offlineSince >= _offlineGrace)
        {
            EndCoolDown();
        }
    }

    private CooldownUpdate ProcessReachable(PrinterSnapshot snapshot)
    {
        PrinterState state = snapshot.State;
        PrintJob? job = snapshot.Job;
        if (state.HasActiveJob())
        {
            // The next print.
            EndCoolDown();
            if (_printSeen && !IsOtherJob(job))
            {
                MergeJob(job);
            }
            else
            {
                SetJob(job);
            }

            _printSeen = true;
            _endedByError = false;
            return new CooldownUpdate(_state, null);
        }

        switch (state)
        {
            case PrinterState.Error:
                _endedByError = true;
                break;
            case PrinterState.Idle:
                // The error was acknowledged; it says nothing about a later print.
                _endedByError = false;
                break;
            case PrinterState.Finished:
            case PrinterState.Stopped:
                EndPrintIfNew(job);
                break;
        }

        if (_stage == Stage.None)
        {
            return new CooldownUpdate(_state, null);
        }

        Telemetry? telemetry = snapshot.Telemetry;
        Temperature? nozzle = telemetry?.Nozzle;
        // NaN compares false: an unknown target is not heating.
        bool heating = nozzle?.Target > 0;
        if (_stage == Stage.WaitingForHeaterOff)
        {
            if (heating)
            {
                return new CooldownUpdate(_state, null);
            }

            _stage = Stage.Active;
            _startedAt = snapshot.Timestamp;
        }
        else if (heating)
        {
            // Preheat or another heating: the printer is in use again.
            EndCoolDown();
            return new CooldownUpdate(_state, null);
        }

        if (nozzle is { Actual: var nozzleCelsius } && IsPlausible(nozzleCelsius))
        {
            AddNozzleReading(snapshot.Timestamp, nozzleCelsius);
        }

        if (telemetry?.Bed is { Actual: var bedCelsius } && IsPlausible(bedCelsius))
        {
            _bed = bedCelsius;
        }

        CooldownEvent? cooled = EvaluateCooledDown();
        _state = BuildState(snapshot.Timestamp);
        return new CooldownUpdate(_state, cooled);
    }

    /// <summary>A Finished / Stopped snapshot: starts a cool-down when it reports a print that has not ended yet.</summary>
    private void EndPrintIfNew(PrintJob? job)
    {
        bool otherJob = IsOtherJob(job);
        if (!_printSeen && _anyPrintEnded && !otherJob)
        {
            // The print that already ended (repeated polls, after an outage, after reheating...).
            return;
        }

        if (_printSeen && !otherJob)
        {
            MergeJob(job);
        }
        else
        {
            SetJob(job);
        }

        _printSeen = false;
        _anyPrintEnded = true;
        EndCoolDown();
        if (!_endedByError)
        {
            _stage = Stage.WaitingForHeaterOff;
            _coolingFileName = _jobFileName;
        }
    }

    private void AddNozzleReading(DateTimeOffset timestamp, double celsius)
    {
        _previousNozzle = _lastNozzle;
        _lastNozzle = celsius;
        _highestNozzle = double.IsNaN(_highestNozzle) ? celsius : Math.Max(_highestNozzle, celsius);
        _estimator.Add(timestamp, celsius);
    }

    /// <summary>Confirms "cooled down" from the last readings; the event only when the nozzle was above the threshold.</summary>
    private CooldownEvent? EvaluateCooledDown()
    {
        int threshold = ThresholdCelsius;
        if (_cooled || !(_lastNozzle <= threshold) || !(_previousNozzle <= threshold))
        {
            // ConfirmReadings = 2: the last and the previous reading (NaN while missing compares false).
            return null;
        }

        _cooled = true;
        return _highestNozzle > threshold ? new CooldownEvent(_printerId!, _lastTimestamp, _coolingFileName) : null;
    }

    private CooldownState BuildState(DateTimeOffset timestamp)
    {
        int threshold = ThresholdCelsius;
        CooldownPhase phase = _cooled ? CooldownPhase.CooledDown
            : _highestNozzle > threshold ? CooldownPhase.Cooling
            : CooldownPhase.None;
        if (phase == CooldownPhase.None)
        {
            // No reading yet, or the print ended below the threshold and the confirming reading is pending.
            return CooldownState.None;
        }

        double progress = 1;
        TimeSpan? remaining = null;
        if (phase == CooldownPhase.Cooling)
        {
            progress = Math.Clamp((_highestNozzle - _lastNozzle) / (_highestNozzle - threshold), 0, 1);
            remaining = _estimator.EstimateRemaining(threshold, timestamp);
        }

        return new CooldownState(phase, _lastNozzle, _bed, threshold, progress, remaining, _startedAt, timestamp);
    }

    private void EndCoolDown()
    {
        _stage = Stage.None;
        _state = CooldownState.None;
        _cooled = false;
        _coolingFileName = null;
        _highestNozzle = _lastNozzle = _previousNozzle = double.NaN;
        _bed = null;
        _estimator.Clear();
    }

    private static bool IsPlausible(double celsius) => celsius is >= MinPlausibleCelsius and <= MaxPlausibleCelsius;

    /// <summary>Unknown identity on either side counts as the same job: absent data must not invent a new print.</summary>
    private bool IsOtherJob(PrintJob? job)
    {
        if (job is null)
        {
            return false;
        }

        if (job.Id is { } id && _jobId is { } knownId)
        {
            return id != knownId;
        }

        return job.FileName is { } fileName && _jobFileName is { } knownFileName
            && !string.Equals(fileName, knownFileName, StringComparison.Ordinal);
    }

    private void SetJob(PrintJob? job)
    {
        _jobId = job?.Id;
        _jobFileName = job?.FileName;
    }

    private void MergeJob(PrintJob? job)
    {
        if (job is not null)
        {
            _jobId = job.Id ?? _jobId;
            _jobFileName = job.FileName ?? _jobFileName;
        }
    }
}
