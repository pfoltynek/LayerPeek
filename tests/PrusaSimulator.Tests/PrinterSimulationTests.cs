using Microsoft.Extensions.Time.Testing;

using LayerPeek.Simulator.Printers;

namespace LayerPeek.Simulator.Tests;

/// <summary>The time model of a simulated printer, on a fake clock.</summary>
public sealed class PrinterSimulationTests
{
    private static readonly SimulationTiming Timing = new()
    {
        HeatUp = TimeSpan.FromSeconds(90),
        Print = TimeSpan.FromMinutes(30),
        FinishedHold = TimeSpan.FromMinutes(2),
        IdleHold = TimeSpan.FromMinutes(1),
        FlakyOnline = TimeSpan.FromSeconds(60),
        FlakyOffline = TimeSpan.FromSeconds(20),
    };

    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public void IdlePrinterIsColdWithoutJob()
    {
        PrinterSimulation printer = Create(PrinterScenario.Idle);
        _clock.Advance(TimeSpan.FromMinutes(5));

        SimulatedStatus status = printer.GetStatus();

        Assert.Equal("IDLE", status.State);
        Assert.Null(status.Job);
        Assert.InRange(status.TempNozzle, 22, 24);
        Assert.InRange(status.TempBed, 22, 24);
        Assert.Equal(0, status.TargetNozzle);
        Assert.Equal(0, status.FanHotend);
        Assert.True(printer.IsOnline);
    }

    [Fact]
    public void PrintStartsWithHeatingWithoutEstimate()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromSeconds(10));
        SimulatedStatus early = printer.GetStatus();
        _clock.Advance(TimeSpan.FromSeconds(30));
        SimulatedStatus later = printer.GetStatus();

        Assert.Equal("PRINTING", early.State);
        Assert.Equal(PrinterSimulation.FirstJobId, early.Job!.Id);
        Assert.Equal(0, early.Job.Progress);
        Assert.Null(early.Job.TimeRemaining);
        Assert.Equal(10, early.Job.TimePrinting);
        Assert.Equal(215, early.TargetNozzle);
        Assert.Equal(60, early.TargetBed);
        Assert.True(later.TempNozzle > early.TempNozzle + 10, $"{early.TempNozzle} -> {later.TempNozzle}");
        Assert.True(later.TempBed > early.TempBed + 5, $"{early.TempBed} -> {later.TempBed}");
        Assert.True(later.TempNozzle < 215);
    }

    [Fact]
    public void ProgressRemainingTimeAndTemperaturesFollowTheClock()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromSeconds(90) + TimeSpan.FromMinutes(15));

        SimulatedStatus status = printer.GetStatus();

        Assert.Equal("PRINTING", status.State);
        Assert.Equal(50, status.Job!.Progress, 2);
        Assert.Equal(900, status.Job.TimeRemaining);
        Assert.Equal(990, status.Job.TimePrinting);
        Assert.InRange(status.TempNozzle, 214, 216);
        Assert.InRange(status.TempBed, 59, 61);
        Assert.Equal(4900, status.FanPrint);
        Assert.Equal(7200, status.FanHotend);
        Assert.Equal("benchy_0.4n_0.2mm_PLA.bgcode", status.Job.File.DisplayName);
    }

    [Fact]
    public void ProgressNeverDecreasesDuringAPrint()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        double last = -1;
        for (int i = 0; i < 210; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(9));
            if (printer.GetStatus().Job is { } job)
            {
                Assert.True(job.Progress >= last, $"{job.Progress} after {last}");
                last = job.Progress;
            }
        }

        Assert.True(last > 99, $"Last progress {last}");
    }

    [Fact]
    public void SpeedFactorSpeedsUpSimulatedTime()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing, speed: 10);
        // 990 simulated seconds: 90 s heating + 900 s (half) of the print.
        _clock.Advance(TimeSpan.FromSeconds(99));

        SimulatedStatus status = printer.GetStatus();

        Assert.Equal(50, status.Job!.Progress, 2);
        Assert.Equal(900, status.Job.TimeRemaining);
    }

    [Fact]
    public void PrintFinishesThenIdlesThenStartsTheNextJob()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromSeconds(90) + TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(10));
        SimulatedStatus finished = printer.GetStatus();
        _clock.Advance(TimeSpan.FromMinutes(2));
        SimulatedStatus idle = printer.GetStatus();
        _clock.Advance(TimeSpan.FromMinutes(1));
        SimulatedStatus next = printer.GetStatus();

        Assert.Equal("FINISHED", finished.State);
        Assert.Null(finished.Job);
        Assert.Equal(0, finished.TargetNozzle);
        Assert.True(finished.TempNozzle > 150, $"Just finished: {finished.TempNozzle}");
        Assert.Equal("IDLE", idle.State);
        Assert.True(idle.TempNozzle < finished.TempNozzle);
        Assert.Equal("PRINTING", next.State);
        Assert.Equal(PrinterSimulation.FirstJobId + 1, next.Job!.Id);
        Assert.NotEqual("benchy_0.4n_0.2mm_PLA.bgcode", next.Job.File.DisplayName);
    }

    [Fact]
    public void PausingKeepsTheJobAndCoolsTheNozzle()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromSeconds(90) + TimeSpan.FromMinutes(9));
        SimulatedJob running = printer.GetStatus().Job!;

        printer.SetScenario(PrinterScenario.Paused);
        _clock.Advance(TimeSpan.FromMinutes(10));
        SimulatedStatus paused = printer.GetStatus();

        Assert.Equal("PAUSED", paused.State);
        Assert.Equal(running, paused.Job);
        Assert.Equal(170, paused.TargetNozzle);
        Assert.InRange(paused.TempNozzle, 169, 172);
        Assert.Equal(60, paused.TargetBed);
    }

    [Fact]
    public void ResumingContinuesTheSameJobWhereItWasPaused()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromSeconds(90) + TimeSpan.FromMinutes(9));
        SimulatedJob running = printer.GetStatus().Job!;
        printer.SetScenario(PrinterScenario.Paused);
        _clock.Advance(TimeSpan.FromMinutes(10));

        printer.SetScenario(PrinterScenario.Printing);
        SimulatedStatus resumed = printer.GetStatus();
        _clock.Advance(TimeSpan.FromMinutes(3));
        SimulatedStatus later = printer.GetStatus();

        Assert.Equal("PRINTING", resumed.State);
        Assert.Equal(running.Id, resumed.Job!.Id);
        Assert.Equal(running.Progress, resumed.Job.Progress, 1);
        Assert.Equal(running.Progress + 10, later.Job!.Progress, 1);
        Assert.InRange(resumed.TempNozzle, 165, 175);
    }

    [Fact]
    public void PausedWithoutARunningJobShowsTheDefaultJob()
    {
        PrinterSimulation printer = Create(PrinterScenario.Paused);

        SimulatedStatus status = printer.GetStatus();

        Assert.Equal("PAUSED", status.State);
        Assert.Equal(52, status.Job!.Progress);
        Assert.Equal(2400, status.Job.TimeRemaining);
    }

    [Fact]
    public void AttentionHoldsTheJobAndTheTemperatures()
    {
        PrinterSimulation printer = Create(PrinterScenario.Attention);
        _clock.Advance(TimeSpan.FromMinutes(10));

        SimulatedStatus status = printer.GetStatus();

        Assert.Equal("ATTENTION", status.State);
        Assert.Equal(61, status.Job!.Progress);
        Assert.InRange(status.TempNozzle, 214, 216);
        Assert.Equal(215, status.TargetNozzle);
    }

    [Fact]
    public void FinishedPrinterCoolsDown()
    {
        PrinterSimulation printer = Create(PrinterScenario.Finished);
        SimulatedStatus hot = printer.GetStatus();
        _clock.Advance(TimeSpan.FromMinutes(10));
        SimulatedStatus cold = printer.GetStatus();

        Assert.Equal("FINISHED", hot.State);
        Assert.Null(hot.Job);
        Assert.InRange(hot.TempNozzle, 214, 216);
        Assert.InRange(cold.TempNozzle, 22, 30);
        Assert.True(cold.TempBed < hot.TempBed);
    }

    [Fact]
    public void ErrorHasNoJobAndCoolsDown()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromMinutes(5));
        printer.SetScenario(PrinterScenario.Error);
        SimulatedStatus status = printer.GetStatus();

        Assert.Equal("ERROR", status.State);
        Assert.Null(status.Job);
        Assert.Equal(0, status.TargetNozzle);
        Assert.InRange(status.TempNozzle, 200, 216);
    }

    [Fact]
    public void TemperaturesAreContinuousAcrossScenarioSwitches()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromMinutes(5));
        double before = printer.GetStatus().TempNozzle;

        printer.SetScenario(PrinterScenario.Idle);

        Assert.InRange(printer.GetStatus().TempNozzle, before - 0.5, before + 0.5);
    }

    [Fact]
    public void LeavingAPrintStartsTheNextOneWithANewJob()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromMinutes(5));
        printer.SetScenario(PrinterScenario.Idle);
        _clock.Advance(TimeSpan.FromMinutes(1));

        printer.SetScenario(PrinterScenario.Printing);

        Assert.Equal(PrinterSimulation.FirstJobId + 1, printer.GetStatus().Job!.Id);
    }

    [Fact]
    public void OfflinePrinterRecoversWithItsScenarioRunning()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        printer.SetOffline(true);
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.False(printer.IsOnline);
        Assert.True(printer.IsForcedOffline);

        printer.SetOffline(false);

        Assert.True(printer.IsOnline);
        Assert.True(printer.GetStatus().Job!.Progress > 0);
    }

    [Fact]
    public void FlakyPrinterAlternatesBetweenOnlineAndOfflineOnRealTime()
    {
        PrinterSimulation printer = Create(PrinterScenario.Flaky, speed: 10);
        List<bool> online = [];
        for (int second = 0; second < 160; second += 10)
        {
            online.Add(printer.IsOnline);
            _clock.Advance(TimeSpan.FromSeconds(10));
        }

        // 60 s online, 20 s offline, twice.
        Assert.Equal([true, true, true, true, true, true, false, false, true, true, true, true, true, true, false, false], online);
        Assert.Equal("PRINTING", printer.GetStatus().State);
    }

    [Fact]
    public void SwitchingBetweenPrintingAndFlakyKeepsThePrint()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromMinutes(10));
        SimulatedJob before = printer.GetStatus().Job!;

        printer.SetScenario(PrinterScenario.Flaky);

        Assert.Equal(before, printer.GetStatus().Job);
    }

    [Fact]
    public void PausedDefaultJobIsTheNextJob()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromMinutes(5));
        printer.SetScenario(PrinterScenario.Idle);

        printer.SetScenario(PrinterScenario.Paused);
        SimulatedJob paused = printer.GetStatus().Job!;
        printer.SetScenario(PrinterScenario.Idle);
        printer.SetScenario(PrinterScenario.Attention);
        SimulatedJob attention = printer.GetStatus().Job!;

        Assert.Equal(PrinterSimulation.FirstJobId + 1, paused.Id);
        Assert.Equal(PrinterSimulation.FileOf(PrinterSimulation.FirstJobId + 1), paused.File);
        Assert.Equal(PrinterSimulation.FirstJobId + 1, attention.Id);
    }

    [Fact]
    public void ResumingAPauseDuringHeatUpContinuesHeating()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromSeconds(30));
        printer.SetScenario(PrinterScenario.Paused);
        _clock.Advance(TimeSpan.FromMinutes(5));

        printer.SetScenario(PrinterScenario.Printing);
        SimulatedJob resumed = printer.GetStatus().Job!;
        _clock.Advance(TimeSpan.FromSeconds(59));
        SimulatedJob stillHeating = printer.GetStatus().Job!;
        _clock.Advance(TimeSpan.FromSeconds(2));
        SimulatedJob printing = printer.GetStatus().Job!;

        Assert.Equal(PrinterSimulation.FirstJobId, resumed.Id);
        Assert.Equal(30, resumed.TimePrinting);
        Assert.Null(resumed.TimeRemaining);
        Assert.Null(stillHeating.TimeRemaining);
        Assert.Equal(0, stillHeating.Progress);
        Assert.NotNull(printing.TimeRemaining);
    }

    [Fact]
    public void FlakyOutagesCountFromTheSwitch()
    {
        PrinterSimulation printer = Create(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromSeconds(70));

        printer.SetScenario(PrinterScenario.Flaky);
        bool atSwitch = printer.IsOnline;
        _clock.Advance(TimeSpan.FromSeconds(59));
        bool beforeOutage = printer.IsOnline;
        _clock.Advance(TimeSpan.FromSeconds(2));
        bool inOutage = printer.IsOnline;

        Assert.True(atSwitch);
        Assert.True(beforeOutage);
        Assert.False(inOutage);
    }

    [Fact]
    public void FilesAreStablePerJob()
    {
        Assert.Equal(PrinterSimulation.FileOf(42), PrinterSimulation.FileOf(46));
        Assert.NotEqual(PrinterSimulation.FileOf(42), PrinterSimulation.FileOf(43));
        Assert.NotNull(PrinterSimulation.FileOf(1));
    }

    private PrinterSimulation Create(PrinterScenario scenario, double speed = 1) =>
        new(SimulatedModel.CoreOne, scenario, _clock, speed, Timing);
}
