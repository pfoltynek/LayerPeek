using LayerPeek.Core.Cooldown;
using LayerPeek.Core.History;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.Core.Tests.Cooldown.CooldownTestData;

namespace LayerPeek.Core.Tests.Cooldown;

public sealed class CooldownTrackerTests
{
    private const double End = 5;

    private readonly CooldownTracker _tracker = new();
    private readonly List<CooldownEvent> _events = [];

    [Theory]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Stopped)]
    public void FinishedOrStoppedPrintStartsCooling(PrinterState ended)
    {
        Process(Printing(0));

        CooldownState state = Process(Cooling(ended, End, End));

        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Equal(NozzlePrintTemperature, state.NozzleCelsius);
        Assert.Equal(BedPrintTemperature, state.BedCelsius);
        Assert.Equal(CooldownThreshold.DefaultCelsius, state.ThresholdCelsius);
        Assert.Equal(0, state.Progress);
        Assert.Null(state.EstimatedRemaining);
        Assert.Equal(Start.AddSeconds(End), state.StartedAt);
        Assert.Equal(Start.AddSeconds(End), state.UpdatedAt);
        Assert.Empty(_events);
    }

    [Fact]
    public void ErrorDoesNotStartCooling()
    {
        Process(Printing(0));

        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Error, End, End)).Phase);
        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Error, End, 10)).Phase);
        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Idle, End, 15)).Phase);
        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Idle, End, 300)).Phase);
        Assert.Empty(_events);
    }

    [Fact]
    public void StoppedAfterErrorDoesNotStartCooling()
    {
        Process(Printing(0));
        Process(Cooling(PrinterState.Error, End, End));

        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Stopped, End, 10)).Phase);
        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Stopped, End, 15)).Phase);
    }

    [Fact]
    public void ErrorAtStartupThenStoppedDoesNotStartCooling()
    {
        Process(Cooling(PrinterState.Error, End, End));

        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Stopped, End, 10)).Phase);
    }

    [Fact]
    public void PrintAfterErrorCoolsDownNormally()
    {
        Process(Printing(0));
        Process(Cooling(PrinterState.Error, End, End));
        Process(Cooling(PrinterState.Idle, End, 10));
        Process(Printing(20, job: Job(8)));

        Assert.Equal(CooldownPhase.Cooling, Process(Cooling(PrinterState.Finished, 25, 25, job: Job(8))).Phase);
    }

    [Fact]
    public void ProgressRisesAndEstimateShrinksAsTheNozzleCools()
    {
        Process(Printing(0));
        double previousProgress = -1;
        TimeSpan? previousEstimate = null;
        int estimates = 0;
        for (double t = End; t < End + 110; t += PollSeconds)
        {
            CooldownState state = Process(Cooling(PrinterState.Finished, End, t));

            Assert.Equal(CooldownPhase.Cooling, state.Phase);
            Assert.InRange(state.Progress, 0, 1);
            Assert.True(state.Progress > previousProgress);
            if (state.EstimatedRemaining is { } estimate)
            {
                Assert.True(previousEstimate is null || estimate < previousEstimate);
                previousEstimate = estimate;
                estimates++;
            }

            previousProgress = state.Progress;
        }

        Assert.True(estimates > 10, $"Only {estimates} estimates.");
        Assert.True(previousProgress > 0.9);
    }

    [Fact]
    public void ProgressIsMeasuredFromTheStartTemperatureToTheThreshold()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 210, 0, job: Job()));

        CooldownState state = Process(Snap(PrinterState.Finished, 10, 130, 0, job: Job()));

        Assert.Equal((210 - 130) / (210.0 - 50), state.Progress, 1e-9);
    }

    [Fact]
    public void ReachingTheThresholdCoolsDownWithExactlyOneEvent()
    {
        Process(Printing(0));

        double cooledAt = RunUntilCooledDown(End);

        // One poll confirms the first reading at or below the threshold.
        double firstBelow = End + (Math.Ceiling(SecondsToReach(50) / PollSeconds) * PollSeconds);
        Assert.Equal(firstBelow + PollSeconds, cooledAt);
        CooldownEvent cooled = Assert.Single(_events);
        Assert.Equal("mk4", cooled.PrinterId);
        Assert.Equal(Start.AddSeconds(cooledAt), cooled.Timestamp);
        Assert.Equal("benchy.bgcode", cooled.FileName);
        Assert.Equal(CooldownPrintOutcome.Finished, cooled.Outcome);

        CooldownState state = _tracker.State;
        Assert.Equal(CooldownPhase.CooledDown, state.Phase);
        Assert.Equal(1, state.Progress);
        Assert.Null(state.EstimatedRemaining);
        Assert.Equal(Start.AddSeconds(End), state.StartedAt);

        // "Cooled down" lasts: more polls, acknowledging the print on the printer and waiting change nothing.
        for (double t = cooledAt + PollSeconds; t < cooledAt + 600; t += PollSeconds)
        {
            Assert.Equal(CooldownPhase.CooledDown, Process(Cooling(t < 300 ? PrinterState.Finished : PrinterState.Idle, End, t)).Phase);
        }

        Assert.Single(_events);
    }

    [Fact]
    public void ExactlyTheThresholdCountsAsCooledDown()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 200, 0, job: Job()));
        Process(Snap(PrinterState.Finished, 10, 50, 0, job: Job()));

        Assert.Equal(CooldownPhase.CooledDown, Process(Snap(PrinterState.Finished, 15, 50, 0, job: Job())).Phase);
        Assert.Single(_events);
    }

    [Fact]
    public void SingleGlitchBelowTheThresholdIsNotCooledDown()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 200, 0, job: Job()));

        CooldownState glitch = Process(Snap(PrinterState.Finished, 10, 12, 0, job: Job()));
        CooldownState after = Process(Snap(PrinterState.Finished, 15, 180, 0, job: Job()));

        Assert.Equal(CooldownPhase.Cooling, glitch.Phase);
        Assert.Equal(CooldownPhase.Cooling, after.Phase);
        Assert.Empty(_events);
    }

    [Theory]
    [InlineData(PrinterState.Printing)]
    [InlineData(PrinterState.Paused)]
    public void NextPrintEndsCoolingWithoutEvent(PrinterState next)
    {
        Process(Printing(0));
        Process(Cooling(PrinterState.Finished, End, End));
        Process(Cooling(PrinterState.Finished, End, 10));

        CooldownState state = Process(Snap(next, 15, 150, 215, job: Job(8)));

        Assert.Equal(CooldownPhase.None, state.Phase);
        Assert.Null(state.NozzleCelsius);
        Assert.Null(state.StartedAt);
        Assert.Empty(_events);
    }

    [Fact]
    public void NextPrintEndsCooledDown()
    {
        Process(Printing(0));
        double cooledAt = RunUntilCooledDown(End);

        Assert.Equal(CooldownPhase.None, Process(Printing(cooledAt + 100, job: Job(8))).Phase);

        Assert.Single(_events);
    }

    [Fact]
    public void NextPrintCoolsDownAgainWithItsOwnEvent()
    {
        Process(Printing(0));
        double cooledAt = RunUntilCooledDown(End);
        Process(Printing(cooledAt + 100, job: Job(8, "second.bgcode")));

        RunUntilCooledDown(cooledAt + 200, Job(8, "second.bgcode"));

        Assert.Equal(["benchy.bgcode", "second.bgcode"], _events.Select(static e => e.FileName));
    }

    [Fact]
    public void ReheatingEndsCoolingWithoutEventAndTheSameJobDoesNotCoolAgain()
    {
        Process(Printing(0));
        Process(Cooling(PrinterState.Finished, End, End));
        Process(Cooling(PrinterState.Finished, End, 10));

        // Preheat from the printer's menu.
        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Busy, 15, 150, 170, job: Job())).Phase);
        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Idle, 20, 160, 170)).Phase);
        // Preheat cancelled: the ended print does not start a second cool-down.
        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Idle, 25, 160, 0)).Phase);
        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Finished, 30, 140, 0, job: Job())).Phase);
        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Idle, 300, 40, 0)).Phase);
        Assert.Empty(_events);
    }

    [Fact]
    public void ReheatingEndsCooledDown()
    {
        Process(Printing(0));
        double cooledAt = RunUntilCooledDown(End);

        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Idle, cooledAt + 10, 40, 215)).Phase);
        Assert.Single(_events);
    }

    [Fact]
    public void HeaterStillOnWhenThePrintEndsDelaysTheStart()
    {
        Process(Printing(0));

        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Finished, End, 215, 215, job: Job())).Phase);
        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Finished, 10, 215, 215, job: Job())).Phase);
        CooldownState state = Process(Snap(PrinterState.Finished, 15, 214, 0, job: Job()));

        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Equal(Start.AddSeconds(15), state.StartedAt);
    }

    [Fact]
    public void PrintEndedBelowTheThresholdIsCooledDownWithoutEvent()
    {
        Process(Printing(0));

        // The first reading waits for confirmation and shows nothing, then "cooled down" right away, never "cooling".
        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Finished, End, 45, 0, 30, Job())).Phase);
        CooldownState state = Process(Snap(PrinterState.Finished, 10, 44, 0, 30, Job()));

        Assert.Equal(CooldownPhase.CooledDown, state.Phase);
        Assert.Equal(1, state.Progress);
        Assert.Equal(44, state.NozzleCelsius);
        Assert.Equal(Start.AddSeconds(End), state.StartedAt);
        for (double t = 15; t < 200; t += PollSeconds)
        {
            Assert.Equal(CooldownPhase.CooledDown, Process(Snap(PrinterState.Finished, t, 40, 0, 30, Job())).Phase);
        }

        Assert.Empty(_events);
    }

    [Theory]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Stopped)]
    public void PrintAlreadyEndedAtTheFirstSnapshotIsNotTracked(PrinterState ended)
    {
        // Decision 11: the app starts (or the printer is added) while the printer still shows the ended print; its end
        // was not observed, so there is no cool-down at all, not even after it cools down below the threshold.
        for (double t = 20; t < 1000; t += PollSeconds)
        {
            Assert.Equal(CooldownState.None, Process(Cooling(ended, End, t)));
        }

        Assert.Empty(_events);
    }

    [Theory]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Unauthorized)]
    [InlineData(PrinterState.Unknown)]
    public void PrintEndedBeforeTheFirstReadableSnapshotIsNotTracked(PrinterState unreadable)
    {
        Process(Snap(unreadable, 0));
        Process(Snap(unreadable, 100));

        Assert.Equal(CooldownState.None, Process(Cooling(PrinterState.Finished, End, 105)));
        Assert.Equal(CooldownState.None, Process(Cooling(PrinterState.Finished, End, 110)));
    }

    [Fact]
    public void PrintAlreadyEndedAtStartupBelowTheThresholdIsNotTracked()
    {
        Assert.Equal(CooldownState.None, Process(Snap(PrinterState.Finished, 0, 30, 0, 25, Job())));
        Assert.Equal(CooldownState.None, Process(Snap(PrinterState.Finished, 5, 30, 0, 25, Job())));
    }

    [Fact]
    public void NextPrintAfterAnUntrackedEndedPrintIsTracked()
    {
        Process(Cooling(PrinterState.Finished, End, 20));
        Process(Cooling(PrinterState.Idle, End, 25));
        Process(Printing(100, job: Job(8, "second.bgcode")));

        RunUntilCooledDown(200, Job(8, "second.bgcode"));

        Assert.Equal("second.bgcode", Assert.Single(_events).FileName);
    }

    [Fact]
    public void ReheatingAfterAnUntrackedEndedPrintDoesNotTrackIt()
    {
        Process(Cooling(PrinterState.Finished, End, 20));
        Process(Snap(PrinterState.Finished, 25, 150, 170, job: Job()));

        Assert.Equal(CooldownState.None, Process(Cooling(PrinterState.Finished, 30, 30)));
        Assert.Equal(CooldownState.None, Process(Cooling(PrinterState.Finished, 30, 35)));
    }

    [Fact]
    public void JobThatRanBetweenTwoPollsAfterAnUntrackedEndedPrintIsTracked()
    {
        Process(Cooling(PrinterState.Finished, End, 20));

        CooldownState state = Process(Cooling(PrinterState.Finished, 25, 25, job: Job(8)));

        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Equal(Start.AddSeconds(25), state.PrintEndedAt);
    }

    [Fact]
    public void HotIdlePrinterAtStartupIsNotTracked()
    {
        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Idle, End, 20)).Phase);
        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Idle, End, 25)).Phase);
    }

    [Fact]
    public void JobThatRanBetweenTwoPollsStartsCooling()
    {
        Process(Snap(PrinterState.Idle, 0, 25, 0));
        Process(Snap(PrinterState.Idle, 5, 25, 0));

        Assert.Equal(CooldownPhase.Cooling, Process(Cooling(PrinterState.Finished, 10, 10, job: Job(9))).Phase);
    }

    [Fact]
    public void AnotherFinishedJobRestartsTheCoolDown()
    {
        Process(Printing(0));
        double cooledAt = RunUntilCooledDown(End);

        // A whole print ran between two polls and is reported as finished.
        CooldownState state = Process(Cooling(PrinterState.Finished, cooledAt + 50, cooledAt + 50, job: Job(8, "second.bgcode")));

        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Equal(Start.AddSeconds(cooledAt + 50), state.StartedAt);
        Assert.Single(_events);
    }

    [Fact]
    public void SameJobWithUnknownIdentityDoesNotRestart()
    {
        Process(Printing(0));
        double cooledAt = RunUntilCooledDown(End);

        Assert.Equal(CooldownPhase.CooledDown, Process(Snap(PrinterState.Finished, cooledAt + 5, 30, 0, job: Job(null, null))).Phase);
        Assert.Equal(CooldownPhase.CooledDown, Process(Snap(PrinterState.Finished, cooledAt + 10, 30, 0, job: Job(7, null))).Phase);
        Assert.Equal(CooldownPhase.CooledDown, Process(Snap(PrinterState.Finished, cooledAt + 15, 30, 0)).Phase);
    }

    [Fact]
    public void FileNameOfTheEventComesFromThePrintWhenTheEndedSnapshotHasNone()
    {
        Process(Printing(0, job: Job(7, "part.bgcode")));

        RunUntilCooledDown(End, Job(7, null));

        Assert.Equal("part.bgcode", Assert.Single(_events).FileName);
    }

    [Fact]
    public void ShortOutageKeepsCoolingAndTheEstimate()
    {
        Process(Printing(0));
        for (double t = End; t <= 60; t += PollSeconds)
        {
            Process(Cooling(PrinterState.Finished, End, t));
        }

        CooldownState before = _tracker.State;
        Assert.NotNull(before.EstimatedRemaining);

        // Weak Wi-Fi: a few missed polls, shorter than the grace period.
        Assert.Equal(before, Process(Snap(PrinterState.Offline, 65)));
        Assert.Equal(before, Process(Snap(PrinterState.Offline, 100)));
        CooldownState after = Process(Cooling(PrinterState.Finished, End, 105));

        Assert.Equal(CooldownPhase.Cooling, after.Phase);
        Assert.NotNull(after.EstimatedRemaining);
        Assert.True(after.EstimatedRemaining < before.EstimatedRemaining);
        Assert.Equal(before.StartedAt, after.StartedAt);
    }

    [Fact]
    public void OfflineEndsCoolingWithoutEventAndItDoesNotResume()
    {
        Process(Printing(0));
        Process(Cooling(PrinterState.Finished, End, End));
        Process(Cooling(PrinterState.Finished, End, 10));

        Assert.Equal(CooldownPhase.Cooling, Process(Snap(PrinterState.Offline, 15)).Phase);
        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Offline, 15 + CooldownTrackerOptions.Default.OfflineGrace.TotalSeconds)).Phase);

        // Back online with the same finished print: it stays ended.
        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Finished, End, 200)).Phase);
        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Finished, End, 205)).Phase);
        Assert.Empty(_events);
    }

    [Fact]
    public void OfflineEndsCooledDown()
    {
        Process(Printing(0));
        double cooledAt = RunUntilCooledDown(End);

        Process(Snap(PrinterState.Offline, cooledAt + 5));
        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Offline, cooledAt + 65)).Phase);
    }

    [Fact]
    public void ZeroGraceEndsOnTheFirstOfflineSnapshot()
    {
        CooldownTracker tracker = new(options: new CooldownTrackerOptions { OfflineGrace = TimeSpan.Zero });
        tracker.Process(Printing(0));
        tracker.Process(Cooling(PrinterState.Finished, End, End));

        Assert.Equal(CooldownPhase.None, tracker.Process(Snap(PrinterState.Offline, 10)).State.Phase);
    }

    [Fact]
    public void OutageGraceRestartsAfterReachableSnapshot()
    {
        Process(Printing(0));
        Process(Cooling(PrinterState.Finished, End, End));

        Process(Snap(PrinterState.Offline, 10));
        Process(Snap(PrinterState.Offline, 60));
        Process(Cooling(PrinterState.Finished, End, 65));
        Process(Snap(PrinterState.Offline, 70));

        // 60 s offline in total, but never 60 s in a row.
        Assert.Equal(CooldownPhase.Cooling, Process(Snap(PrinterState.Offline, 120)).Phase);
    }

    [Fact]
    public void PrintThatEndedDuringAnOutageStartsCoolingWhenBack()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Offline, 5));
        Process(Snap(PrinterState.Offline, 200));

        Assert.Equal(CooldownPhase.Cooling, Process(Cooling(PrinterState.Finished, 190, 205)).Phase);
    }

    [Fact]
    public void AnotherJobThatEndedDuringAnOutageIsNotTracked()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Offline, 5));
        Process(Snap(PrinterState.Offline, 200));

        // Neither the print nor its end was observed.
        Assert.Equal(CooldownState.None, Process(Cooling(PrinterState.Finished, 190, 205, job: Job(8))));
        Assert.Equal(CooldownState.None, Process(Cooling(PrinterState.Finished, 190, 210, job: Job(8))));
    }

    [Fact]
    public void AnotherJobThatEndedDuringAShortOutageEndsTheCoolDownWithoutANewOne()
    {
        Process(Printing(0));
        Process(Cooling(PrinterState.Finished, End, End));
        Process(Snap(PrinterState.Offline, 10));

        Assert.Equal(CooldownState.None, Process(Cooling(PrinterState.Finished, 30, 40, job: Job(8))));
        Assert.Equal(CooldownState.None, Process(Cooling(PrinterState.Finished, 30, 45, job: Job(8))));
        Assert.Empty(_events);
    }

    [Fact]
    public void PausedPrintThatEndedDuringAnOutageIsTracked()
    {
        Process(Snap(PrinterState.Paused, 0, 215, 215, 60, Job()));
        Process(Snap(PrinterState.Offline, 5));

        CooldownState state = Process(Cooling(PrinterState.Stopped, 20, 30));

        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Null(state.PrintEndedAt);
        Assert.Equal(CooldownPrintOutcome.Stopped, state.Outcome);
    }

    [Theory]
    [InlineData(PrinterState.Unauthorized)]
    [InlineData(PrinterState.Unknown)]
    [InlineData((PrinterState)99)]
    public void UninformativeSnapshotsChangeNothing(PrinterState state)
    {
        Process(Printing(0));
        CooldownState cooling = Process(Cooling(PrinterState.Finished, End, End));

        // Polled every few seconds for minutes (a gap without snapshots would be another matter): with and without
        // telemetry, even a heating nozzle.
        for (double t = 10; t <= 500; t += PollSeconds)
        {
            Assert.Equal(cooling, Process(t % 10 == 0 ? Snap(state, t) : Snap(state, t, 20, 215)));
        }

        Assert.Equal(CooldownPhase.Cooling, Process(Cooling(PrinterState.Finished, End, 505)).Phase);
    }

    [Fact]
    public void IdleBusyAttentionAndErrorAfterTheEndKeepCooling()
    {
        Process(Printing(0));
        Process(Cooling(PrinterState.Finished, End, End));

        Assert.Equal(CooldownPhase.Cooling, Process(Cooling(PrinterState.Idle, End, 10)).Phase);
        Assert.Equal(CooldownPhase.Cooling, Process(Cooling(PrinterState.Busy, End, 15)).Phase);
        Assert.Equal(CooldownPhase.Cooling, Process(Cooling(PrinterState.Attention, End, 20)).Phase);
        Assert.Equal(CooldownPhase.Cooling, Process(Cooling(PrinterState.Error, End, 25)).Phase);
    }

    [Fact]
    public void MissingOrInvalidTemperaturesNeitherThrowNorStart()
    {
        Process(Printing(0));

        foreach (PrinterSnapshot snapshot in (PrinterSnapshot[])
        [
            Snap(PrinterState.Finished, End),
            Snap(PrinterState.Finished, 10, bed: 55),
            Snap(PrinterState.Finished, 15, double.NaN, 0, double.NaN, Job()),
            Snap(PrinterState.Finished, 20, double.PositiveInfinity, 0, double.NegativeInfinity, Job()),
            Snap(PrinterState.Finished, 25, -5, 0, job: Job()),
            Snap(PrinterState.Finished, 30, 1e6, 0, job: Job()),
        ])
        {
            CooldownState state = Process(snapshot);
            Assert.Equal(CooldownPhase.None, state.Phase);
            Assert.Null(state.NozzleCelsius);
        }

        CooldownState started = Process(Snap(PrinterState.Finished, 35, 200, 0, job: Job()));
        Assert.Equal(CooldownPhase.Cooling, started.Phase);
        // The cool-down started when the print ended, the first reading only tells its phase.
        Assert.Equal(Start.AddSeconds(End), started.StartedAt);
        Assert.Empty(_events);
    }

    [Fact]
    public void MissingTemperaturesWhileCoolingKeepTheLastValues()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 200, 0, 58, Job()));

        CooldownState state = Process(Snap(PrinterState.Finished, 10, job: Job()));
        Assert.Equal((CooldownPhase.Cooling, 200.0, 58.0), (state.Phase, state.NozzleCelsius!.Value, state.BedCelsius!.Value));

        state = Process(Snap(PrinterState.Finished, 15, double.NaN, double.NaN, double.NaN, Job()));
        Assert.Equal((CooldownPhase.Cooling, 200.0, 58.0), (state.Phase, state.NozzleCelsius!.Value, state.BedCelsius!.Value));
        Assert.Empty(_events);
    }

    [Fact]
    public void MissingNozzleTargetIsNotReheating()
    {
        Process(Printing(0));

        Assert.Equal(CooldownPhase.Cooling, Process(Snap(PrinterState.Finished, End, 200, null, job: Job())).Phase);
        Assert.Equal(CooldownPhase.Cooling, Process(Snap(PrinterState.Finished, 10, 190, double.NaN, job: Job())).Phase);
    }

    [Fact]
    public void BedIsOnlyShown()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 200, 0, 60, Job()));
        Process(Snap(PrinterState.Finished, 10, 45, 0, 59, Job()));

        CooldownState state = Process(Snap(PrinterState.Finished, 15, 44, 0, 58, Job()));

        Assert.Equal(CooldownPhase.CooledDown, state.Phase);
        Assert.Equal(58, state.BedCelsius);
    }

    [Fact]
    public void PrinterWithoutBedReadingHasNoBedTemperature()
    {
        Process(Printing(0));

        Assert.Null(Process(Snap(PrinterState.Finished, End, 200, 0, job: Job())).BedCelsius);
    }

    [Fact]
    public void RaisedThresholdCoolsDownRightAwayWithEvent()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 200, 0, job: Job()));
        Process(Snap(PrinterState.Finished, 10, 75, 0, job: Job()));
        Process(Snap(PrinterState.Finished, 15, 70, 0, job: Job()));

        CooldownUpdate update = _tracker.SetThreshold(80);

        Assert.Equal(CooldownPhase.CooledDown, update.State.Phase);
        Assert.Equal(80, update.State.ThresholdCelsius);
        Assert.Equal(80, _tracker.ThresholdCelsius);
        CooldownEvent cooled = Assert.IsType<CooldownEvent>(update.Event);
        Assert.Equal(Start.AddSeconds(15), cooled.Timestamp);
        Assert.Equal("mk4", cooled.PrinterId);
    }

    [Fact]
    public void RaisedThresholdNeedsTwoReadingsBelowIt()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 200, 0, job: Job()));
        Process(Snap(PrinterState.Finished, 10, 95, 0, job: Job()));
        Process(Snap(PrinterState.Finished, 15, 75, 0, job: Job()));

        CooldownUpdate update = _tracker.SetThreshold(80);

        Assert.Equal(CooldownPhase.Cooling, update.State.Phase);
        Assert.Null(update.Event);
        Assert.Equal(CooldownPhase.CooledDown, Process(Snap(PrinterState.Finished, 20, 72, 0, job: Job())).Phase);
        Assert.Single(_events);
    }

    [Fact]
    public void LoweredThresholdKeepsCooledDown()
    {
        Process(Printing(0));
        RunUntilCooledDown(End);

        CooldownUpdate update = _tracker.SetThreshold(CooldownThreshold.MinCelsius);

        Assert.Equal(CooldownPhase.CooledDown, update.State.Phase);
        Assert.Equal(CooldownThreshold.MinCelsius, update.State.ThresholdCelsius);
        Assert.Null(update.Event);
    }

    [Fact]
    public void LoweredThresholdWhileCoolingRecomputesProgress()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 210, 0, job: Job()));
        double before = Process(Snap(PrinterState.Finished, 10, 130, 0, job: Job())).Progress;

        CooldownUpdate update = _tracker.SetThreshold(30);

        Assert.Equal(CooldownPhase.Cooling, update.State.Phase);
        Assert.Equal((210 - 130) / (210.0 - 30), update.State.Progress, 1e-9);
        Assert.True(update.State.Progress < before);
    }

    [Fact]
    public void ThresholdAboveTheStartTemperatureDoesNotBreakProgress()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 95, 0, job: Job()));
        Process(Snap(PrinterState.Finished, 10, 97, 0, job: Job()));

        CooldownUpdate update = _tracker.SetThreshold(100);

        Assert.InRange(update.State.Progress, 0, 1);
        Assert.False(double.IsNaN(update.State.Progress));
    }

    [Fact]
    public void SameThresholdChangesNothing()
    {
        Process(Printing(0));
        CooldownState state = Process(Cooling(PrinterState.Finished, End, End));

        CooldownUpdate update = _tracker.SetThreshold(CooldownThreshold.DefaultCelsius);

        Assert.Equal(state, update.State);
        Assert.Null(update.Event);
    }

    [Fact]
    public void ThresholdOutsideTheRangeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CooldownTracker(29));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CooldownTracker(101));
        Assert.Throws<ArgumentOutOfRangeException>(() => _tracker.SetThreshold(29));
        Assert.Throws<ArgumentOutOfRangeException>(() => _tracker.SetThreshold(101));
        Assert.Equal(30, new CooldownTracker(30).ThresholdCelsius);
        Assert.Equal(100, new CooldownTracker(100).ThresholdCelsius);
    }

    [Fact]
    public void WithoutCoolDownTheStateIsEmpty()
    {
        CooldownTracker tracker = new(65);

        Assert.Equal(CooldownState.None, tracker.State);
        Assert.Equal(CooldownState.None, tracker.Process(Snap(PrinterState.Idle, 0, 25, 0)).State);
        Assert.Equal(65, tracker.ThresholdCelsius);
        Assert.Equal(CooldownState.None, tracker.SetThreshold(70).State);
    }

    [Fact]
    public void ClockGoingBackNeverYieldsANegativeEstimate()
    {
        Process(Printing(0));
        for (double t = End; t <= 60; t += PollSeconds)
        {
            Process(Cooling(PrinterState.Finished, End, t));
        }

        // The system clock jumped back by an hour.
        for (double t = -3600; t < -3500; t += PollSeconds)
        {
            CooldownState state = Process(Cooling(PrinterState.Finished, -3600 - 60, t));
            Assert.True(state.EstimatedRemaining is null || state.EstimatedRemaining >= TimeSpan.Zero);
            Assert.NotEqual(CooldownPhase.None, state.Phase);
        }

        Assert.Single(_events);
    }

    [Theory]
    [InlineData(PrinterState.Finished, CooldownPrintOutcome.Finished)]
    [InlineData(PrinterState.Stopped, CooldownPrintOutcome.Stopped)]
    public void StateCarriesHowAndWhenThePrintEnded(PrinterState ended, CooldownPrintOutcome outcome)
    {
        Process(Printing(0));

        CooldownState cooling = Process(Cooling(ended, End, End));
        CooldownState later = Process(Cooling(ended, End, 30));

        Assert.Equal(outcome, cooling.Outcome);
        Assert.Equal(Start.AddSeconds(End), cooling.PrintEndedAt);
        Assert.Null(cooling.CooledDownAt);
        Assert.Equal(outcome, later.Outcome);
        Assert.Equal(Start.AddSeconds(End), later.PrintEndedAt);
    }

    [Theory]
    [InlineData(PrinterState.Finished, CooldownPrintOutcome.Finished)]
    [InlineData(PrinterState.Stopped, CooldownPrintOutcome.Stopped)]
    public void EventCarriesHowThePrintEnded(PrinterState ended, CooldownPrintOutcome outcome)
    {
        Process(Printing(0));
        Process(Snap(ended, End, 200, 0, job: Job()));
        Process(Snap(ended, 10, 45, 0, job: Job()));
        Process(Snap(ended, 15, 44, 0, job: Job()));

        CooldownEvent cooled = Assert.Single(_events);
        Assert.Equal(outcome, cooled.Outcome);
        Assert.Equal(outcome, _tracker.State.Outcome);
    }

    [Fact]
    public void EventFromARaisedThresholdCarriesHowThePrintEnded()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Stopped, End, 200, 0, job: Job()));
        Process(Snap(PrinterState.Stopped, 10, 75, 0, job: Job()));
        Process(Snap(PrinterState.Stopped, 15, 70, 0, job: Job()));

        CooldownUpdate update = _tracker.SetThreshold(80);

        Assert.Equal(CooldownPrintOutcome.Stopped, update.Event?.Outcome);
    }

    [Fact]
    public void CooledDownAtIsTheTimeOfTheCooledDownTransition()
    {
        Process(Printing(0));

        double cooledAt = RunUntilCooledDown(End);
        CooldownState state = _tracker.State;
        for (double t = cooledAt + PollSeconds; t <= cooledAt + 300; t += PollSeconds)
        {
            state = Process(Cooling(PrinterState.Idle, End, t));
        }

        Assert.Equal(CooldownPhase.CooledDown, state.Phase);
        Assert.Equal(Start.AddSeconds(cooledAt + 300), state.UpdatedAt);
        Assert.Equal(Start.AddSeconds(cooledAt), state.CooledDownAt);
        Assert.Equal(Assert.Single(_events).Timestamp, state.CooledDownAt);
        Assert.Equal(Start.AddSeconds(End), state.PrintEndedAt);
        Assert.Equal(CooldownPrintOutcome.Finished, state.Outcome);
    }

    [Fact]
    public void PrintEndIsTheEndedSnapshotEvenWhenTheHeaterGoesOffLater()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Stopped, End, 215, 215, job: Job()));

        CooldownState state = Process(Snap(PrinterState.Stopped, 15, 214, 0, job: Job()));

        Assert.Equal(Start.AddSeconds(End), state.PrintEndedAt);
        Assert.Equal(Start.AddSeconds(15), state.StartedAt);
        Assert.Equal(CooldownPrintOutcome.Stopped, state.Outcome);
    }

    [Fact]
    public void PrintEndedBelowTheThresholdIsCooledDownAtTheConfirmingReading()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 45, 0, job: Job()));

        CooldownState state = Process(Snap(PrinterState.Finished, 10, 44, 0, job: Job()));

        Assert.Equal(CooldownPhase.CooledDown, state.Phase);
        Assert.Equal(Start.AddSeconds(10), state.CooledDownAt);
        Assert.Equal(Start.AddSeconds(End), state.PrintEndedAt);
        Assert.Empty(_events);
    }

    [Fact]
    public void RaisedThresholdCoolsDownAtTheLastReading()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 200, 0, job: Job()));
        Process(Snap(PrinterState.Finished, 10, 75, 0, job: Job()));
        Process(Snap(PrinterState.Finished, 15, 70, 0, job: Job()));

        CooldownUpdate update = _tracker.SetThreshold(80);

        Assert.Equal(Start.AddSeconds(15), update.State.CooledDownAt);
        Assert.Equal(update.Event?.Timestamp, update.State.CooledDownAt);
    }

    [Fact]
    public void JobThatRanBetweenTwoPollsEndedAtTheFirstEndedSnapshot()
    {
        Process(Snap(PrinterState.Idle, 0, 25, 0));

        CooldownState state = Process(Cooling(PrinterState.Finished, 5, 5, job: Job(9)));

        Assert.Equal(Start.AddSeconds(5), state.PrintEndedAt);
    }

    [Theory]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Unauthorized)]
    [InlineData(PrinterState.Unknown)]
    public void PrintEndIsUnknownWhenItEndedWhileThePrinterWasNotReadable(PrinterState unreadable)
    {
        Process(Printing(0));
        Process(Snap(unreadable, 5));
        Process(Snap(unreadable, 200));

        CooldownState state = Process(Cooling(PrinterState.Finished, 190, 205));

        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Null(state.PrintEndedAt);
        Assert.Equal(CooldownPrintOutcome.Finished, state.Outcome);
    }

    [Fact]
    public void PrintEndIsUnknownWhenItEndedDuringAGapWithoutSnapshots()
    {
        // The computer slept: no snapshot at all, not even an offline one, for longer than the gap limit.
        Process(Printing(0));

        CooldownState state = Process(Cooling(PrinterState.Finished, 190, 200));

        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Null(state.PrintEndedAt);
        Assert.Equal(CooldownPrintOutcome.Finished, state.Outcome);
    }

    [Fact]
    public void GapUpToTheLimitIsObserved()
    {
        Assert.Equal(CooldownTrackerOptions.Default.OfflineGrace, _tracker.GapLimit);
        double limit = _tracker.GapLimit.TotalSeconds;
        Process(Printing(0));

        CooldownState state = Process(Cooling(PrinterState.Finished, limit, limit));

        Assert.Equal(Start.AddSeconds(limit), state.PrintEndedAt);
    }

    [Theory]
    [InlineData(PrinterState.Printing)]
    [InlineData(PrinterState.Idle)]
    public void OtherJobThatRanAndEndedDuringAGapStartsNoCoolDown(PrinterState before)
    {
        Process(before == PrinterState.Printing ? Printing(0) : Snap(PrinterState.Idle, 0, 25, 0));

        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Finished, 190, 200, job: Job(8))).Phase);
        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Finished, 190, 205, job: Job(8))).Phase);
        Assert.Empty(_events);
    }

    [Fact]
    public void GapLongerThanTheLimitEndsCoolingWithoutEventAndItDoesNotResume()
    {
        Process(Printing(0));
        Process(Cooling(PrinterState.Finished, End, End));
        Assert.Equal(CooldownPhase.Cooling, Process(Cooling(PrinterState.Finished, End, 10)).Phase);

        double afterGap = 10 + _tracker.GapLimit.TotalSeconds + 1;
        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Finished, End, afterGap)).Phase);

        // The same finished print stays ended, like after an outage longer than the offline grace.
        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Finished, End, afterGap + 5)).Phase);
        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Finished, End, afterGap + 10)).Phase);
        Assert.Empty(_events);
    }

    [Fact]
    public void GapUpToTheLimitKeepsCooling()
    {
        Process(Printing(0));
        Process(Cooling(PrinterState.Finished, End, End));
        CooldownState before = Process(Cooling(PrinterState.Finished, End, 10));

        CooldownState after = Process(Cooling(PrinterState.Finished, End, 10 + _tracker.GapLimit.TotalSeconds));

        Assert.Equal(CooldownPhase.Cooling, after.Phase);
        Assert.Equal(before.StartedAt, after.StartedAt);
        Assert.Equal(Start.AddSeconds(End), after.PrintEndedAt);
    }

    [Fact]
    public void GapEndsACoolDownWaitingForTheHeaterToTurnOff()
    {
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, End, 215, 215, job: Job()));

        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Finished, 200, 30, 0, job: Job())).Phase);
        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Finished, 205, 29, 0, job: Job())).Phase);
        Assert.Empty(_events);
    }

    [Fact]
    public void GapEndsCooledDown()
    {
        Process(Printing(0));
        double cooledAt = RunUntilCooledDown(End);

        Assert.Equal(CooldownPhase.None, Process(Cooling(PrinterState.Idle, End, cooledAt + 300)).Phase);
    }

    [Fact]
    public void GapBeforeAnOfflineSnapshotEndsCooling()
    {
        Process(Printing(0));
        Process(Cooling(PrinterState.Finished, End, End));

        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Offline, 200)).Phase);
    }

    [Fact]
    public void ClockGoingBackIsNoGap()
    {
        Process(Printing(500));

        CooldownState state = Process(Cooling(PrinterState.Finished, End, End));

        Assert.Equal(Start.AddSeconds(End), state.PrintEndedAt);
    }

    [Fact]
    public void GapLimitFollowsThePollingInterval()
    {
        // Three polling intervals (TelemetryHistory.GapThreshold), at least the offline grace.
        _tracker.PollingInterval = TimeSpan.FromMinutes(5);
        Assert.Equal(TimeSpan.FromMinutes(15), _tracker.GapLimit);

        Process(Printing(0));
        CooldownState state = Process(Cooling(PrinterState.Finished, 300, 300));

        Assert.Equal(Start.AddSeconds(300), state.PrintEndedAt);

        _tracker.PollingInterval = TimeSpan.FromSeconds(30);
        Assert.Equal(TimeSpan.FromSeconds(90), _tracker.GapLimit);
        _tracker.PollingInterval = TimeSpan.FromSeconds(1);
        Assert.Equal(CooldownTrackerOptions.Default.OfflineGrace, _tracker.GapLimit);
        Assert.Equal(TimeSpan.FromSeconds(1), _tracker.PollingInterval);
    }

    [Fact]
    public void ShortenedPollingIntervalDoesNotTurnThePendingPollIntoAGap()
    {
        // Settings saved while cooling at a 2 minute interval: the poll that follows was due under the old interval.
        _tracker.PollingInterval = TimeSpan.FromMinutes(2);
        Process(Printing(0));
        Process(Snap(PrinterState.Finished, 120, 200, 0, job: Job()));

        _tracker.PollingInterval = TimeSpan.FromSeconds(5);
        CooldownState state = Process(Snap(PrinterState.Finished, 240, 120, 0, job: Job()));

        Assert.Equal(CooldownPhase.Cooling, state.Phase);
        Assert.Equal(Start.AddSeconds(120), state.PrintEndedAt);
        Assert.Equal(CooldownTrackerOptions.Default.OfflineGrace, _tracker.GapLimit);

        // From then on the new interval applies.
        Assert.Equal(CooldownPhase.Cooling, Process(Snap(PrinterState.Finished, 245, 118, 0, job: Job())).Phase);
        Assert.Equal(CooldownPhase.None, Process(Snap(PrinterState.Finished, 245 + _tracker.GapLimit.TotalSeconds + 1, 100, 0, job: Job())).Phase);
        Assert.Empty(_events);
    }

    [Theory]
    [InlineData(PrinterState.Unauthorized)]
    [InlineData(PrinterState.Unknown)]
    [InlineData(PrinterState.Offline)]
    public void BackoffBetweenUnreadableSnapshotsIsNoGap(PrinterState unreadable)
    {
        // An unreadable printer is polled with an exponential backoff of up to a minute (PrinterMonitorOptions.MaxBackoff),
        // plus the poll itself: more than the gap limit at the default interval, yet the monitor was running all along.
        Process(Printing(0));
        CooldownState cooling = Process(Cooling(PrinterState.Finished, End, End));
        double t = End;
        foreach (double delay in new[] { 5.0, 10, 20, 40, 61, 61, 61 })
        {
            t += delay;
            Process(Snap(unreadable, t));
        }

        CooldownState state = Process(Cooling(PrinterState.Finished, End, t + 61));

        if (unreadable == PrinterState.Offline)
        {
            // Offline for longer than the grace ends the cool-down, as before.
            Assert.Equal(CooldownPhase.None, state.Phase);
        }
        else
        {
            Assert.Equal(CooldownPhase.Cooling, state.Phase);
            Assert.Equal(cooling.StartedAt, state.StartedAt);
            Assert.Equal(cooling.PrintEndedAt, state.PrintEndedAt);
        }

        Assert.Empty(_events);
    }

    [Fact]
    public void GapLimitIsAtLeastTheTelemetryGapWithoutOfflineGrace()
    {
        CooldownTracker tracker = new(options: new CooldownTrackerOptions { OfflineGrace = TimeSpan.Zero });

        Assert.Equal(AppSettings.DefaultPollingInterval, tracker.PollingInterval);
        Assert.Equal(TelemetryHistory.MinGapThreshold, tracker.GapLimit);
    }

    [Fact]
    public void PollingIntervalMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _tracker.PollingInterval = TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => _tracker.PollingInterval = TimeSpan.FromSeconds(-1));
    }

    [Fact]
    public void EndedCoolDownClearsTheEndDetails()
    {
        Process(Printing(0));
        RunUntilCooledDown(End);

        CooldownState state = Process(Printing(1000, job: Job(8)));

        Assert.Equal(CooldownState.None, state);
        Assert.Equal(CooldownPrintOutcome.None, state.Outcome);
        Assert.Null(state.PrintEndedAt);
        Assert.Null(state.CooledDownAt);
    }

    [Fact]
    public void ProcessRejectsNull() => Assert.Throws<ArgumentNullException>(() => _tracker.Process(null!));

    [Fact]
    public void OptionsRejectNegativeGrace() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CooldownTrackerOptions { OfflineGrace = TimeSpan.FromSeconds(-1) });

    /// <summary>Feeds the simulated curve after a print that ended at <paramref name="end"/> until "cooled down".</summary>
    /// <returns>Seconds of the snapshot that cooled down.</returns>
    private double RunUntilCooledDown(double end, PrintJob? job = null, double? from = null)
    {
        for (double t = from ?? end; t < end + 1000; t += PollSeconds)
        {
            if (Process(Cooling(PrinterState.Finished, end, t, job: job)).Phase == CooldownPhase.CooledDown)
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
