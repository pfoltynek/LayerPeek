using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;

using static LayerPeek.Core.Tests.Cooldown.CooldownTestData;

namespace LayerPeek.Core.Tests.Cooldown;

/// <summary>
/// T-109 (user decision 2026-10-29): the nozzle and the bed decide together. With the default thresholds (40 / 40 °C) the
/// simulated nozzle (τ = 60 s) gets there after about 145 s, the bed (τ = 300 s) only after about 233 s.
/// </summary>
public sealed class CooldownTrackerBedTests
{
    private const double End = 5;

    private readonly CooldownTracker _tracker = new();
    private readonly List<CooldownEvent> _events = [];

    /// <summary>When the simulated bed reaches <paramref name="threshold"/>, in seconds after the heater was switched off.</summary>
    private static double BedSecondsToReach(double threshold) =>
        BedTauSeconds * Math.Log((BedPrintTemperature - Ambient) / (threshold - Ambient));

    [Fact]
    public void DefaultThresholdsAreFortyAndForty()
    {
        Assert.Equal(CooldownThresholds.Default, _tracker.Thresholds);
        Process(Printing(0));

        CooldownState state = Process(Cooling(PrinterState.Finished, End, End));

        Assert.Equal(40, state.NozzleThresholdCelsius);
        Assert.Equal(40, state.BedThresholdCelsius);
        Assert.Equal(CooldownThresholds.Default, state.Thresholds);
    }

    [Fact]
    public void CooledNozzleWithAWarmBedIsStillCooling()
    {
        Process(Printing(0));

        // Well after the nozzle is below 40 °C, the bed is still above it.
        double nozzleCool = End + SecondsToReach(40) + (3 * PollSeconds);
        CooldownState state = default;
        for (double t = End; t <= nozzleCool; t += PollSeconds)
        {
            state = Process(Cooling(PrinterState.Finished, End, t));
        }

        Assert.True(state.NozzleCelsius <= 40);
        Assert.True(state.BedCelsius > 40);
        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Equal(CooldownSensor.Bed, state.Limiting);
        Assert.Equal(state.BedCelsius, state.LimitingCelsius);
        Assert.True(state.Progress < 1);
        Assert.Empty(_events);
    }

    [Fact]
    public void BothBelowTheirThresholdsCoolsDownWithOneEventCarryingBothReadings()
    {
        Process(Printing(0));

        double cooledAt = RunUntilCooledDown(End);

        // The bed decides: one poll confirms its first reading at or below 40 °C.
        double firstBelow = End + (Math.Ceiling(BedSecondsToReach(40) / PollSeconds) * PollSeconds);
        Assert.Equal(firstBelow + PollSeconds, cooledAt);
        CooldownEvent cooled = Assert.Single(_events);
        Assert.Equal(Start.AddSeconds(cooledAt), cooled.Timestamp);
        Assert.True(cooled.NozzleCelsius <= 40);
        Assert.True(cooled.BedCelsius <= 40);
        Assert.Equal(40, cooled.NozzleThresholdCelsius);
        Assert.Equal(40, cooled.BedThresholdCelsius);
        CooldownState state = _tracker.State;
        Assert.Equal(cooled.NozzleCelsius, state.NozzleCelsius);
        Assert.Equal(cooled.BedCelsius, state.BedCelsius);
        Assert.Equal(1, state.Progress);
        // Once cooled down, the temperature that was limiting last stays the one compact texts show.
        Assert.Equal(CooldownSensor.Bed, state.Limiting);
        Assert.False(state.EndedBelowThreshold);
    }

    [Fact]
    public void WarmNozzleWithACooledBedIsStillCooling()
    {
        // A bed threshold above the bed: the nozzle decides alone and limits.
        CooldownTracker tracker = new(new CooldownThresholds(40, 70));
        tracker.Process(Printing(0));

        CooldownState state = tracker.Process(Cooling(PrinterState.Finished, End, End + 60)).State;

        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Equal(CooldownSensor.Nozzle, state.Limiting);
        Assert.Equal(state.NozzleCelsius, state.LimitingCelsius);
    }

    [Fact]
    public void ProgressIsTheSmallerOfTheTwo()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 210, 0, 60, Job()));

        // Nozzle (210 − 130) / (210 − 40) = 0.47; bed (60 − 55) / (60 − 40) = 0.25.
        CooldownState state = Process(Snap(PrinterState.Finished, 10, 130, 0, 55, Job()));

        Assert.Equal((60 - 55) / (60.0 - 40), state.Progress, 1e-9);
        Assert.Equal(CooldownSensor.Bed, state.Limiting);

        // The nozzle limits once its progress is the smaller one: nozzle 0.53, bed 0.75.
        state = Process(Snap(PrinterState.Finished, 15, 120, 0, 45, Job()));
        Assert.Equal((210 - 120) / (210.0 - 40), state.Progress, 1e-9);
        Assert.Equal(CooldownSensor.Nozzle, state.Limiting);
    }

    [Fact]
    public void BedThatWasNeverAboveItsThresholdDoesNotHoldProgressBack()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 210, 0, 35, Job()));

        CooldownState state = Process(Snap(PrinterState.Finished, 10, 130, 0, 34, Job()));

        Assert.Equal((210 - 130) / (210.0 - 40), state.Progress, 1e-9);
        Assert.Equal(CooldownSensor.Nozzle, state.Limiting);
    }

    [Fact]
    public void EstimateIsTheLongerOfTheTwo()
    {
        CooldownTracker nozzleOnly = new(new CooldownThresholds(40, CooldownThreshold.MaxCelsius));
        Process(Printing(0));
        nozzleOnly.Process(Printing(0));
        int compared = 0;
        for (double t = End; t < End + 140; t += PollSeconds)
        {
            PrinterSnapshot snapshot = Cooling(PrinterState.Finished, End, t);
            CooldownState both = Process(snapshot);
            CooldownState nozzle = nozzleOnly.Process(snapshot).State;
            if (both.EstimatedRemaining is { } bothEstimate && nozzle.EstimatedRemaining is { } nozzleEstimate)
            {
                Assert.True(bothEstimate > nozzleEstimate, $"{bothEstimate} ≤ {nozzleEstimate} at {t} s.");
                compared++;
            }
        }

        Assert.True(compared > 10, $"Only {compared} estimates.");
    }

    [Fact]
    public void EstimateConvergesOnTheBedInTheSecondHalf()
    {
        Process(Printing(0));
        double reach = End + BedSecondsToReach(40);
        int checkedEstimates = 0;
        for (double t = End; t < reach - PollSeconds; t += PollSeconds)
        {
            CooldownState state = Process(Cooling(PrinterState.Finished, End, t));
            if (t - End > (reach - End) / 2 && state.EstimatedRemaining is { } estimate)
            {
                double actual = reach - t;
                Assert.InRange(estimate.TotalSeconds, actual * 0.8, actual * 1.2);
                checkedEstimates++;
            }
        }

        Assert.True(checkedEstimates > 5, $"Only {checkedEstimates} estimates.");
    }

    [Fact]
    public void EstimateAfterTheNozzleIsThereFollowsTheBedAlone()
    {
        Process(Printing(0));
        CooldownState state = default;
        double t = End;
        for (; t <= End + SecondsToReach(40) + (10 * PollSeconds); t += PollSeconds)
        {
            state = Process(Cooling(PrinterState.Finished, End, t));
        }

        // The nozzle is flat near the ambient (it alone would have no estimate), the bed still falls.
        Assert.True(state.NozzleCelsius <= 40);
        TimeSpan estimate = Assert.NotNull(state.EstimatedRemaining);
        double actual = End + BedSecondsToReach(40) - (t - PollSeconds);
        Assert.InRange(estimate.TotalSeconds, actual * 0.8, actual * 1.2);
    }

    [Fact]
    public void NoEstimateWhileTheBedAboveItsThresholdHasNone()
    {
        // The bed stays at 45 °C (not falling): the slower one is not known, so neither is the estimate.
        Process(Printing(0));
        CooldownState state = default;
        for (double t = End; t < End + 100; t += PollSeconds)
        {
            state = Process(Snap(PrinterState.Finished, t, SimulatedNozzle(t - End), 0, 45, Job()));
        }

        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Null(state.EstimatedRemaining);
    }

    [Fact]
    public void SingleBedGlitchBelowTheThresholdIsNotCooledDown()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 200, 0, 60, Job()));
        Process(Snap(PrinterState.Finished, 10, 35, 0, 45, Job()));
        Process(Snap(PrinterState.Finished, 15, 34, 0, 39, Job()));

        Assert.Equal(CooldownPhase.Cooling, Process(Snap(PrinterState.Finished, 20, 33, 0, 41, Job())).Phase);
        Assert.Equal(CooldownPhase.Cooling, Process(Snap(PrinterState.Finished, 25, 33, 0, 40, Job())).Phase);
        Assert.Equal(CooldownPhase.CooledDown, Process(Snap(PrinterState.Finished, 30, 33, 0, 40, Job())).Phase);
        Assert.Single(_events);
    }

    [Fact]
    public void InvalidBedReadingsAreSkipped()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 200, 0, 60, Job()));
        Process(Snap(PrinterState.Finished, 10, 35, 0, 39, Job()));

        // NaN / implausible bed readings neither confirm nor break the confirmation.
        Assert.Equal(CooldownPhase.Cooling, Process(Snap(PrinterState.Finished, 15, 34, 0, double.NaN, Job())).Phase);
        Assert.Equal(CooldownPhase.Cooling, Process(Snap(PrinterState.Finished, 20, 34, 0, -5, Job())).Phase);
        CooldownState state = Process(Snap(PrinterState.Finished, 25, 33, 0, 38, Job()));

        Assert.Equal(CooldownPhase.CooledDown, state.Phase);
        Assert.Equal(38, state.BedCelsius);
    }

    [Fact]
    public void PrinterWithoutBedReadingsIsDecidedByTheNozzleAlone()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 200, 0, job: Job()));
        Process(Snap(PrinterState.Finished, 10, 39, 0, job: Job()));

        CooldownState state = Process(Snap(PrinterState.Finished, 15, 38, 0, job: Job()));

        Assert.Equal(CooldownPhase.CooledDown, state.Phase);
        Assert.Null(state.BedCelsius);
        CooldownEvent cooled = Assert.Single(_events);
        Assert.Null(cooled.BedCelsius);
        Assert.Equal(40, cooled.BedThresholdCelsius);
    }

    [Fact]
    public void PrintEndedBelowBothThresholdsIsCooledDownWithoutEvent()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 39, 0, 38, Job()));

        CooldownState state = Process(Snap(PrinterState.Finished, 10, 38, 0, 37, Job()));

        Assert.Equal(CooldownPhase.CooledDown, state.Phase);
        Assert.True(state.EndedBelowThreshold);
        Assert.Empty(_events);
    }

    [Fact]
    public void PrintEndedWithACoolNozzleButAWarmBedCoolsDownWithEvent()
    {
        Process(Printing(0));
        Assert.Equal(CooldownPhase.Cooling, Process(Snap(PrinterState.Finished, End, 38, 0, 50, Job())).Phase);
        Process(Snap(PrinterState.Finished, 10, 37, 0, 45, Job()));
        Process(Snap(PrinterState.Finished, 15, 36, 0, 40, Job()));

        CooldownState state = Process(Snap(PrinterState.Finished, 20, 35, 0, 39, Job()));

        Assert.Equal(CooldownPhase.CooledDown, state.Phase);
        Assert.False(state.EndedBelowThreshold);
        Assert.Equal(39, Assert.Single(_events).BedCelsius);
    }

    // ---- Heating again ----

    [Fact]
    public void BedHeatingAgainWhileCoolingEndsItWithoutEvent()
    {
        Process(Printing(0));
        RunCurve(End, End + 60);

        CooldownState state = Process(SnapWithBedTarget(End + 65, 60, 50, 60));

        Assert.Equal(CooldownState.None, state);
        Assert.Equal(CooldownState.None, Process(Cooling(PrinterState.Finished, End, End + 400)));
        Assert.Empty(_events);
    }

    [Fact]
    public void BedHeatingAgainAfterCoolingDownEndsIt()
    {
        Process(Printing(0));
        double cooledAt = RunUntilCooledDown(End);
        Assert.Equal(CooldownPhase.CooledDown, Process(Cooling(PrinterState.Finished, End, cooledAt + PollSeconds)).Phase);

        Assert.Equal(CooldownState.None, Process(SnapWithBedTarget(cooledAt + (2 * PollSeconds), 30, 38, 60)));
        Assert.Single(_events);
    }

    [Fact]
    public void CoolDownWaitsUntilTheBedHeaterIsOff()
    {
        Process(Printing(0));

        // The print ended, the nozzle heater is off but the bed heater a poll later.
        Assert.Equal(CooldownState.None, Process(SnapWithBedTarget(End, 200, 60, 60)));
        CooldownState state = Process(Snap(PrinterState.Finished, End + 5, 190, 0, 59, Job()));

        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Equal(Start.AddSeconds(End + 5), state.StartedAt);
        Assert.Equal(Start.AddSeconds(End), state.PrintEndedAt);
    }

    // ---- Offline ----

    [Fact]
    public void OfflineLongerThanTheGraceEndsTheCoolDownWithACooledNozzle()
    {
        Process(Printing(0));
        RunCurve(End, End + SecondsToReach(40) + 20);
        Assert.Equal(CooldownPhase.Cooling, _tracker.State.Phase);

        double t = End + SecondsToReach(40) + 25;
        Process(Snap(PrinterState.Offline, t));
        Assert.Equal(CooldownPhase.Cooling, Process(Snap(PrinterState.Offline, t + 30)).Phase);

        Assert.Equal(CooldownState.None, Process(Snap(PrinterState.Offline, t + 60)));
        Assert.Equal(CooldownState.None, Process(Cooling(PrinterState.Finished, End, t + 65)));
        Assert.Empty(_events);
    }

    [Fact]
    public void ShortOutageKeepsBothSeries()
    {
        Process(Printing(0));
        RunCurve(End, End + 200);
        Process(Snap(PrinterState.Offline, End + 205));

        double cooledAt = RunUntilCooledDown(End, from: End + 210);

        Assert.True(cooledAt > End + BedSecondsToReach(40));
        Assert.Single(_events);
    }

    // ---- Threshold changes while cooling ----

    [Fact]
    public void RaisedBedThresholdThatTheBedMeetsCoolsDownRightAwayWithEvent()
    {
        Process(Printing(0));
        RunCurve(End, End + 180);
        Assert.Equal(CooldownPhase.Cooling, _tracker.State.Phase);

        // The bed is at about 43 °C, the nozzle below 40 °C.
        CooldownUpdate update = _tracker.SetThresholds(new CooldownThresholds(40, 50));

        Assert.Equal(CooldownPhase.CooledDown, update.State.Phase);
        CooldownEvent cooled = Assert.IsType<CooldownEvent>(update.Event);
        Assert.Equal(50, cooled.BedThresholdCelsius);
        Assert.Equal(Start.AddSeconds(End + 180), cooled.Timestamp);
    }

    [Fact]
    public void RaisedNozzleThresholdAloneDoesNotCoolDownAWarmBed()
    {
        Process(Printing(0));
        RunCurve(End, End + 90);

        CooldownUpdate update = _tracker.SetThresholds(new CooldownThresholds(100, 40));

        Assert.Equal(CooldownPhase.Cooling, update.State.Phase);
        Assert.Equal(CooldownSensor.Bed, update.State.Limiting);
        Assert.Null(update.Event);
    }

    [Fact]
    public void LoweredBedThresholdWhileCoolingRecomputesProgressAndHoldsItBack()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 210, 0, 60, Job()));
        Process(Snap(PrinterState.Finished, 10, 39, 0, 42, Job()));
        double before = Process(Snap(PrinterState.Finished, 15, 38, 0, 41, Job())).Progress;

        CooldownUpdate update = _tracker.SetThresholds(new CooldownThresholds(40, 30));

        Assert.Equal(CooldownPhase.Cooling, update.State.Phase);
        Assert.Equal((60 - 41) / (60.0 - 30), update.State.Progress, 1e-9);
        Assert.True(update.State.Progress < before);
        Assert.Null(update.Event);
    }

    [Fact]
    public void LoweredBedThresholdKeepsCooledDown()
    {
        Process(Printing(0));
        RunUntilCooledDown(End);

        CooldownUpdate update = _tracker.SetThresholds(new CooldownThresholds(30, 30));

        Assert.Equal(CooldownPhase.CooledDown, update.State.Phase);
        Assert.Equal(new CooldownThresholds(30, 30), update.State.Thresholds);
        Assert.Null(update.Event);
    }

    [Fact]
    public void SameThresholdsChangeNothing()
    {
        Process(Printing(0));
        CooldownState state = Process(Cooling(PrinterState.Finished, End, End));

        CooldownUpdate update = _tracker.SetThresholds(CooldownThresholds.Default);

        Assert.Equal(state, update.State);
        Assert.Null(update.Event);
    }

    [Fact]
    public void InvalidThresholdsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CooldownTracker(new CooldownThresholds(40, 29)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CooldownTracker(new CooldownThresholds(40, 101)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CooldownTracker(default(CooldownThresholds)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _tracker.SetThresholds(new CooldownThresholds(40, 29)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _tracker.SetThresholds(default));
        Assert.Equal(CooldownThresholds.Default, _tracker.Thresholds);
    }

    [Fact]
    public void NextPrintEndsTheCoolDownAndClearsTheLimitingTemperature()
    {
        Process(Printing(0));
        RunCurve(End, End + 200);
        Assert.Equal(CooldownSensor.Bed, _tracker.State.Limiting);

        Assert.Equal(CooldownState.None, Process(Printing(300, job: Job(8))));

        // The next cool-down starts afresh with the nozzle limiting (215 °C vs 60 °C at the start: both progress 0).
        Assert.Equal(CooldownSensor.Nozzle, Process(Cooling(PrinterState.Finished, 305, 305, job: Job(8))).Limiting);
    }

    private static PrinterSnapshot SnapWithBedTarget(double seconds, double nozzle, double bed, double bedTarget) => new()
    {
        PrinterId = "mk4",
        State = PrinterState.Finished,
        Timestamp = Start.AddSeconds(seconds),
        Job = Job(),
        Telemetry = new Telemetry { Nozzle = new Temperature(nozzle, 0), Bed = new Temperature(bed, bedTarget) },
    };

    private void RunCurve(double end, double until)
    {
        for (double t = end; t <= until; t += PollSeconds)
        {
            Process(Cooling(PrinterState.Finished, end, t));
        }
    }

    private double RunUntilCooledDown(double end, double? from = null)
    {
        for (double t = from ?? end; t < end + 2000; t += PollSeconds)
        {
            if (Process(Cooling(PrinterState.Finished, end, t)).Phase == CooldownPhase.CooledDown)
            {
                return t;
            }
        }

        throw new InvalidOperationException("Did not cool down.");
    }

    private CooldownState Process(PrinterSnapshot snapshot)
    {
        CooldownUpdate update = _tracker.Process(snapshot);
        if (update.Event is { } cooled)
        {
            _events.Add(cooled);
        }

        Assert.Equal(_tracker.State, update.State);
        return update.State;
    }
}
