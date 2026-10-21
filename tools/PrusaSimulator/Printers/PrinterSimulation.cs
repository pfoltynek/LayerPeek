namespace PrusaTrayDiagnostic.Simulator.Printers;

/// <summary>
/// State of one simulated printer as a function of time: the current <see cref="PrinterScenario"/>, when it started and
/// the temperatures and job at that moment. Temperatures follow a first-order curve towards their targets; the
/// <see cref="Speed"/> factor speeds up simulated time (print progress, heating) but not the
/// <see cref="PrinterScenario.Flaky"/> outages. Thread-safe.
/// </summary>
public sealed class PrinterSimulation
{
    public const double Ambient = 23.0;

    /// <summary>Job id of the first simulated print (as in the recorded fixtures).</summary>
    public const long FirstJobId = 42;

    private const double NozzleHeatingTau = 15;
    private const double NozzleCoolingTau = 60;
    private const double BedHeatingTau = 30;
    private const double BedCoolingTau = 300;
    private const double PausedNozzleTarget = 170;
    private const double ParkedZ = 5;

    // Where the head parks after a print (as in the recorded fixtures).
    private static readonly (double X, double Y) Parked = (242, -9);

    private static readonly SimulatedFile[] Files =
    [
        new("BENCHY~1.BGC", "benchy_0.4n_0.2mm_PLA.bgcode", 754535),
        new("CUBE~1.BGC", "calibration-cube_0.4n_0.2mm_PLA.bgcode", 120000),
        new("PHONES~1.BGC", "phone-stand_0.4n_0.2mm_PETG.bgcode", 2318842),
        new("SPOOLH~1.BGC", "spool-holder_0.4n_0.15mm_PLA.bgcode", 5120330),
    ];

    private readonly Lock _lock = new();
    private readonly TimeProvider _timeProvider;
    private Phase _phase;
    private long _nextJobId = FirstJobId;
    private bool _forcedOffline;

    /// <param name="speed">Simulated seconds per real second (1 = real time).</param>
    public PrinterSimulation(SimulatedModel model, PrinterScenario scenario, TimeProvider timeProvider, double speed = 1, SimulationTiming? timing = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(speed, 0);
        if (!Enum.IsDefined(scenario))
        {
            throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        Model = model;
        Speed = speed;
        Timing = timing ?? SimulationTiming.Default;
        _timeProvider = timeProvider;
        bool hot = scenario is PrinterScenario.Paused or PrinterScenario.Attention or PrinterScenario.Finished;
        long started = timeProvider.GetTimestamp();
        _phase = new Phase(
            scenario,
            started,
            SimOffset: 0,
            StartNozzle: hot ? model.NozzleTarget : Ambient,
            StartBed: hot ? model.BedTarget : Ambient,
            FrozenJob: null,
            JobId: _nextJobId,
            ResumeAt: null,
            FlakySince: started);
    }

    public SimulatedModel Model { get; }

    public double Speed { get; }

    public SimulationTiming Timing { get; }

    public PrinterScenario Scenario
    {
        get
        {
            lock (_lock)
            {
                return _phase.Scenario;
            }
        }
    }

    /// <summary>Switched offline by <see cref="SetOffline"/> (independent of the scenario).</summary>
    public bool IsForcedOffline
    {
        get
        {
            lock (_lock)
            {
                return _forcedOffline;
            }
        }
    }

    /// <summary>Whether the printer answers requests now (not forced offline and not in a flaky outage).</summary>
    public bool IsOnline
    {
        get
        {
            lock (_lock)
            {
                if (_forcedOffline)
                {
                    return false;
                }

                if (_phase.Scenario != PrinterScenario.Flaky)
                {
                    return true;
                }

                double cycle = (Timing.FlakyOnline + Timing.FlakyOffline).TotalSeconds;
                double real = _timeProvider.GetElapsedTime(_phase.FlakySince).TotalSeconds;
                return cycle <= 0 || real % cycle < Timing.FlakyOnline.TotalSeconds;
            }
        }
    }

    /// <summary>Stops (true) or resumes (false) answering requests; the scenario keeps running meanwhile.</summary>
    public void SetOffline(bool offline)
    {
        lock (_lock)
        {
            _forcedOffline = offline;
        }
    }

    /// <summary>
    /// Switches the scenario, continuing from the current temperatures. Pausing or attention keeps the running job; printing
    /// after them resumes it. Printing and flaky share one timeline. The same scenario again changes nothing.
    /// </summary>
    public void SetScenario(PrinterScenario scenario)
    {
        if (!Enum.IsDefined(scenario))
        {
            throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        lock (_lock)
        {
            Phase current = _phase;
            if (scenario == current.Scenario)
            {
                return;
            }

            long now = _timeProvider.GetTimestamp();
            if (IsPrinting(scenario) && IsPrinting(current.Scenario))
            {
                // One print timeline; a flaky printer's outages count from the switch.
                _phase = current with { Scenario = scenario, FlakySince = now };
                return;
            }

            SimulatedStatus status = Compute(current, now);
            if (IsPrinting(current.Scenario))
            {
                _nextJobId = (status.Job?.Id ?? JobIdAt(current, now)) + 1;
            }

            SimulatedJob? frozen = status.Job ?? current.FrozenJob;
            // Where in the print cycle a held job stopped (also during heating), so that printing resumes exactly there.
            double? resumeAt = IsPrinting(current.Scenario) ? (status.Job is null ? null : CycleTime(current, now)) : current.ResumeAt;
            double offset = 0;
            long jobId = _nextJobId;
            if (IsPrinting(scenario) && frozen is not null && current.Scenario is PrinterScenario.Paused or PrinterScenario.Attention)
            {
                // Resume the paused job where it stopped.
                offset = resumeAt ?? Timing.HeatUp.TotalSeconds + (frozen.Progress / 100 * Timing.Print.TotalSeconds);
                jobId = frozen.Id;
            }

            bool holds = scenario is PrinterScenario.Paused or PrinterScenario.Attention;

            _phase = new Phase(
                scenario,
                now,
                offset,
                status.TempNozzle,
                status.TempBed,
                holds ? frozen : null,
                jobId,
                holds ? resumeAt : null,
                now);
        }
    }

    public SimulatedStatus GetStatus()
    {
        lock (_lock)
        {
            return Compute(_phase, _timeProvider.GetTimestamp());
        }
    }

    /// <summary>The file of a job id (stable: the same id always has the same file).</summary>
    public static SimulatedFile FileOf(long jobId) => Files[(int)(((jobId - FirstJobId) % Files.Length + Files.Length) % Files.Length)];

    private static bool IsPrinting(PrinterScenario scenario) => scenario is PrinterScenario.Printing or PrinterScenario.Flaky;

    private double SimSeconds(Phase phase, long now) => phase.SimOffset + (_timeProvider.GetElapsedTime(phase.Started, now).TotalSeconds * Speed);

    private double CycleSeconds =>
        Timing.HeatUp.TotalSeconds + Timing.Print.TotalSeconds + Timing.FinishedHold.TotalSeconds + Timing.IdleHold.TotalSeconds;

    private double CycleTime(Phase phase, long now)
    {
        double s = SimSeconds(phase, now);
        return s - (Math.Floor(s / CycleSeconds) * CycleSeconds);
    }

    private long JobIdAt(Phase phase, long now) => phase.JobId + (long)Math.Floor(SimSeconds(phase, now) / CycleSeconds);

    private SimulatedStatus Compute(Phase phase, long now)
    {
        double s = SimSeconds(phase, now);
        double sincePhase = s - phase.SimOffset;
        return phase.Scenario switch
        {
            PrinterScenario.Printing or PrinterScenario.Flaky => ComputePrinting(phase, s),
            PrinterScenario.Idle => Static("IDLE", null, phase, sincePhase, 0, 0, ParkedZ) with { AxisXY = Parked },
            PrinterScenario.Finished => Static("FINISHED", null, phase, sincePhase, 0, 0, Model.MaxZ) with { AxisXY = Parked },
            PrinterScenario.Error => Static("ERROR", null, phase, sincePhase, 0, 0, ParkedZ) with { AxisXY = (0, 0) },
            PrinterScenario.Paused => Frozen("PAUSED", phase, sincePhase, PausedNozzleTarget, new SimulatedJob(phase.JobId, 52, 2400, 2600, FileOf(phase.JobId))) with { AxisXY = (10, 200) },
            PrinterScenario.Attention => Frozen("ATTENTION", phase, sincePhase, Model.NozzleTarget, new SimulatedJob(phase.JobId, 61, 1900, 3100, FileOf(phase.JobId))),
            _ => throw new InvalidOperationException("Unknown scenario."),
        };
    }

    private SimulatedStatus Frozen(string state, Phase phase, double sincePhase, double nozzleTarget, SimulatedJob defaultJob)
    {
        SimulatedJob job = phase.FrozenJob ?? defaultJob;
        double z = 0.2 + ((Model.MaxZ - 0.2) * job.Progress / 100);
        return Static(state, job, phase, sincePhase, nozzleTarget, Model.BedTarget, z);
    }

    private static SimulatedStatus Static(string state, SimulatedJob? job, Phase phase, double sincePhase, double nozzleTarget, double bedTarget, double z)
    {
        double nozzle = Approach(phase.StartNozzle, nozzleTarget, sincePhase, NozzleHeatingTau, NozzleCoolingTau);
        double bed = Approach(phase.StartBed, bedTarget, sincePhase, BedHeatingTau, BedCoolingTau);
        return new SimulatedStatus(state, job, Wobble(nozzle, sincePhase), nozzleTarget, Wobble(bed, sincePhase * 0.7), bedTarget, z, 100, 100, HotendFan(nozzle), 0);
    }

    private SimulatedStatus ComputePrinting(Phase phase, double s)
    {
        double heat = Timing.HeatUp.TotalSeconds;
        double print = Timing.Print.TotalSeconds;
        double finished = Timing.FinishedHold.TotalSeconds;
        double cycleLength = CycleSeconds;
        long cycle = (long)Math.Floor(s / cycleLength);
        double t = s - (cycle * cycleLength);
        long jobId = phase.JobId + cycle;

        // Cycle 0 starts where the phase started; later cycles from where a whole cycle (begun cold) ends.
        double t0 = cycle == 0 ? phase.SimOffset : 0;
        double startNozzle = cycle == 0 ? phase.StartNozzle : CycleEnd(Ambient, Model.NozzleTarget, NozzleHeatingTau, NozzleCoolingTau);
        double startBed = cycle == 0 ? phase.StartBed : CycleEnd(Ambient, Model.BedTarget, BedHeatingTau, BedCoolingTau);
        double printEnd = heat + print;
        double nozzle;
        double bed;
        if (t < printEnd)
        {
            nozzle = Approach(startNozzle, Model.NozzleTarget, t - t0, NozzleHeatingTau, NozzleCoolingTau);
            bed = Approach(startBed, Model.BedTarget, t - t0, BedHeatingTau, BedCoolingTau);
        }
        else
        {
            double hotFor = Math.Max(0, printEnd - t0);
            double cooledFor = t - Math.Max(t0, printEnd);
            nozzle = Approach(Approach(startNozzle, Model.NozzleTarget, hotFor, NozzleHeatingTau, NozzleCoolingTau), Ambient, cooledFor, NozzleHeatingTau, NozzleCoolingTau);
            bed = Approach(Approach(startBed, Model.BedTarget, hotFor, BedHeatingTau, BedCoolingTau), Ambient, cooledFor, BedHeatingTau, BedCoolingTau);
        }

        double nozzleShown = Wobble(nozzle, s);
        double bedShown = Wobble(bed, s * 0.7);
        if (t < heat)
        {
            SimulatedJob heating = new(jobId, 0, null, (int)t, FileOf(jobId));
            return new SimulatedStatus("PRINTING", heating, nozzleShown, Model.NozzleTarget, bedShown, Model.BedTarget, 0.2, 100, 100, HotendFan(nozzle), 0);
        }

        if (t < printEnd)
        {
            double progress = Math.Min(100, (t - heat) / print * 100);
            int remaining = (int)Math.Ceiling(printEnd - t);
            SimulatedJob job = new(jobId, Math.Round(progress, 2), remaining, (int)t, FileOf(jobId));
            double z = 0.2 + ((Model.MaxZ - 0.2) * progress / 100);
            return new SimulatedStatus("PRINTING", job, nozzleShown, Model.NozzleTarget, bedShown, Model.BedTarget, Math.Round(z, 2), 100, 100, HotendFan(nozzle), progress >= 1 ? 4900 : 0);
        }

        string state = t < printEnd + finished ? "FINISHED" : "IDLE";
        return new SimulatedStatus(state, null, nozzleShown, 0, bedShown, 0, Model.MaxZ, 100, 100, HotendFan(nozzle), 0) { AxisXY = Parked };
    }

    /// <summary>Temperature at the end of a print cycle that started at <paramref name="start"/>.</summary>
    private double CycleEnd(double start, double target, double heatingTau, double coolingTau)
    {
        double hot = Approach(start, target, (Timing.HeatUp + Timing.Print).TotalSeconds, heatingTau, coolingTau);
        return Approach(hot, Ambient, (Timing.FinishedHold + Timing.IdleHold).TotalSeconds, heatingTau, coolingTau);
    }

    /// <summary>First-order approach to <paramref name="target"/> (0 = heater off: towards ambient).</summary>
    private static double Approach(double start, double target, double seconds, double heatingTau, double coolingTau)
    {
        double goal = target <= 0 ? Ambient : target;
        double tau = goal > start ? heatingTau : coolingTau;
        return goal + ((start - goal) * Math.Exp(-Math.Max(0, seconds) / tau));
    }

    /// <summary>A small, deterministic sensor ripple (±0.2 °C).</summary>
    private static double Wobble(double value, double seconds) => value + (0.2 * Math.Sin(seconds * 0.9));

    /// <summary>The heatbreak fan runs while the nozzle is above 50 °C.</summary>
    private static int HotendFan(double nozzle) => nozzle > 50 ? 7200 : 0;

    /// <param name="Started">Real start (<see cref="TimeProvider.GetTimestamp"/>).</param>
    /// <param name="SimOffset">Simulated seconds already elapsed at the start (resumed print).</param>
    /// <param name="FrozenJob">Job held by a paused / attention printer; null for the default job.</param>
    /// <param name="JobId">Id of the first job printed in this phase.</param>
    /// <param name="ResumeAt">Paused / attention: simulated position in the print cycle where printing resumes; null for the default job.</param>
    /// <param name="FlakySince">Real start of the flaky outage cycle (<see cref="TimeProvider.GetTimestamp"/>).</param>
    private sealed record Phase(PrinterScenario Scenario, long Started, double SimOffset, double StartNozzle, double StartBed, SimulatedJob? FrozenJob, long JobId, double? ResumeAt, long FlakySince);
}
