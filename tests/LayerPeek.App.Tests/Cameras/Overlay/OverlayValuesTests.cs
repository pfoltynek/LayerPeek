using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay;

/// <summary>
/// The values the overlay widgets show and when (rules of T-036, moved from the old overlay to the shared helpers of
/// T-081): job values only during a job, rounding like the display, finish time in the local zone.
/// </summary>
public sealed class OverlayValuesTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    /// <summary>A running print at <see cref="TestPrinters.Now"/> (12:00 UTC) with every value known.</summary>
    private static PrinterSnapshot Printing(PrinterState state = PrinterState.Printing, Telemetry? telemetry = null, PrintJob? job = null) => new()
    {
        PrinterId = "mk4",
        State = state,
        Timestamp = Now,
        Job = job ?? new PrintJob
        {
            FileName = "benchy_0.4n_0.2mm_PLA_MK4S_2h10m.bgcode",
            ProgressPercent = 42.7,
            Elapsed = TimeSpan.FromMinutes(130),
            Remaining = TimeSpan.FromMinutes(65),
        },
        Telemetry = telemetry ?? new Telemetry { Nozzle = new Temperature(214.6, 215), Bed = new Temperature(59.8, 60) },
    };

    // ---- When job values are shown ----

    [Theory]
    [InlineData(PrinterState.Printing)]
    [InlineData(PrinterState.Paused)]
    [InlineData(PrinterState.Attention)]
    public void JobValuesDuringARunningPausedOrWaitingJob(PrinterState state) =>
        Assert.NotNull(OverlayValues.ActiveJob(Printing(state)));

    [Theory]
    [InlineData(PrinterState.Idle)]
    [InlineData(PrinterState.Busy)]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Stopped)]
    [InlineData(PrinterState.Error)]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Unauthorized)]
    [InlineData(PrinterState.Unknown)]
    public void NoJobValuesWithoutAJobOrWhileUnreachable(PrinterState state) =>
        Assert.Null(OverlayValues.ActiveJob(Printing(state)));

    [Fact]
    public void NoJobValuesBeforeTheFirstPollOrWithoutAJob()
    {
        Assert.Null(OverlayValues.ActiveJob(null));
        Assert.Null(OverlayValues.ActiveJob(Printing() with { Job = null }));
    }

    // ---- Rounding like the display ----

    [Fact]
    public void ProgressIsRoundedDown()
    {
        Assert.Equal(42, OverlayValues.ProgressPercent(Printing().Job!));
        Assert.Equal(99, OverlayValues.ProgressPercent(new PrintJob { ProgressPercent = 99.99 }));
        Assert.Equal(0, OverlayValues.ProgressPercent(new PrintJob { ProgressPercent = double.NaN }));
    }

    [Fact]
    public void TimesAreWholeMinutesRoundedUp()
    {
        PrintJob job = new() { ProgressPercent = 1, Remaining = TimeSpan.FromSeconds(270), Elapsed = TimeSpan.FromSeconds(61) };

        Assert.Equal(5, OverlayValues.RemainingMinutes(job));
        Assert.Equal(2, OverlayValues.PrintTimeMinutes(job));
    }

    [Fact]
    public void RemainingTimeIsExtrapolatedLikeTheTooltip()
    {
        PrintJob job = new() { ProgressPercent = 50, Elapsed = TimeSpan.FromMinutes(30) };

        Assert.Equal(30, OverlayValues.RemainingMinutes(job));
    }

    [Fact]
    public void UnknownTimesAreLeftOut()
    {
        PrintJob job = new() { ProgressPercent = 10 };

        Assert.Null(OverlayValues.RemainingMinutes(job));
        Assert.Null(OverlayValues.PrintTimeMinutes(job));
        Assert.Null(OverlayValues.Finish(Printing(job: job), Utc));
    }

    [Theory]
    [InlineData(25.4, null, 25L, null)]
    [InlineData(25.4, 0.0, 25L, null)]
    [InlineData(25.4, 0.4, 25L, null)]
    [InlineData(170.5, 215.0, 171L, 215L)]
    [InlineData(-3.6, 0.0, -4L, null)]
    [InlineData(25.4, double.NaN, 25L, null)]
    public void TemperaturesAreWholeDegreesWithTheTargetOnlyWhileHeating(double actual, double? target, long shown, long? shownTarget) =>
        Assert.Equal(new OverlayTemperature(shown, shownTarget), OverlayValues.Temperature(new Temperature(actual, target)));

    [Fact]
    public void UnknownTemperaturesAreLeftOut()
    {
        Assert.Null(OverlayValues.Temperature(null));
        Assert.Null(OverlayValues.Temperature(new Temperature(double.NaN, 60)));
        Assert.Null(OverlayValues.Temperature(new Temperature(double.PositiveInfinity, 60)));
    }

    [Fact]
    public void ChangesBelowTheDisplayedPrecisionGiveEqualValues()
    {
        Assert.Equal(OverlayValues.Temperature(new Temperature(214.6, 215)), OverlayValues.Temperature(new Temperature(215.4, 215)));
        Assert.Equal(OverlayValues.ProgressPercent(new PrintJob { ProgressPercent = 42.1 }), OverlayValues.ProgressPercent(new PrintJob { ProgressPercent = 42.9 }));
        Assert.Equal(
            OverlayValues.RemainingMinutes(new PrintJob { Remaining = TimeSpan.FromMinutes(64.5) }),
            OverlayValues.RemainingMinutes(new PrintJob { Remaining = TimeSpan.FromMinutes(65) }));
    }

    // ---- Finish time ----

    [Fact]
    public void FinishTimeIsRoundedUpToTheMinuteLikeTheRemainingTime()
    {
        PrinterSnapshot snapshot = Printing() with
        {
            Timestamp = Now.AddSeconds(20),
            Job = new PrintJob { ProgressPercent = 1, Remaining = TimeSpan.FromSeconds(270) },
        };

        Assert.Equal(new OverlayFinishTime(new DateTime(2026, 10, 16, 12, 5, 0), false), OverlayValues.Finish(snapshot, Utc));
        Assert.Equal(5, OverlayValues.RemainingMinutes(snapshot.Job!));
    }

    [Fact]
    public void FinishTimeIsLocalTime()
    {
        TimeZoneInfo plusTwo = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

        Assert.Equal(new OverlayFinishTime(new DateTime(2026, 10, 16, 15, 5, 0), false), OverlayValues.Finish(Printing(), plusTwo));
    }

    [Fact]
    public void FinishOnAnotherDayIsMarked()
    {
        PrinterSnapshot snapshot = Printing(job: new PrintJob { ProgressPercent = 5, Remaining = TimeSpan.FromHours(20) });

        Assert.Equal(new OverlayFinishTime(new DateTime(2026, 10, 17, 8, 0, 0), true), OverlayValues.Finish(snapshot, Utc));
    }

    [Fact]
    public void LocalZoneAloneCanMoveTheFinishToTheNextDay()
    {
        // 20:00 UTC + 3 h 30 min = 23:30 UTC: the same day in UTC, but 01:30 the next day at UTC+2.
        PrinterSnapshot snapshot = Printing(job: new PrintJob { ProgressPercent = 5, Remaining = TimeSpan.FromMinutes(210) }) with
        {
            Timestamp = new DateTimeOffset(2026, 10, 16, 20, 0, 0, TimeSpan.Zero),
        };
        TimeZoneInfo plusTwo = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

        Assert.False(OverlayValues.Finish(snapshot, Utc)!.Value.IsOnAnotherDay);
        Assert.Equal(new OverlayFinishTime(new DateTime(2026, 10, 17, 1, 30, 0), true), OverlayValues.Finish(snapshot, plusTwo));
    }

    [Fact]
    public void FinishAcrossADaylightSavingTransitionUsesTheOffsetAtTheFinish()
    {
        // Like Central Europe: UTC+1, summer time UTC+2 from the last Sunday of March to the last Sunday of October
        // (2026-10-25, 03:00 CEST → 02:00 CET, i.e. 01:00 UTC).
        TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));
        TimeZoneInfo central = TimeZoneInfo.CreateCustomTimeZone("Test CET", TimeSpan.FromHours(1), "Test CET", "Test CET", "Test CEST", [rule]);

        // Started at 22:00 UTC on 24 October = 00:00 CEST on 25 October; 4 h later is 02:00 UTC = 03:00 CET (not 04:00).
        PrinterSnapshot snapshot = Printing(job: new PrintJob { ProgressPercent = 5, Remaining = TimeSpan.FromHours(4) }) with
        {
            Timestamp = new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero),
        };
        Assert.Equal(new OverlayFinishTime(new DateTime(2026, 10, 25, 3, 0, 0), false), OverlayValues.Finish(snapshot, central));

        // 21:30 UTC on 24 October = 23:30 CEST: finishing at 01:30 UTC = 02:30 CET is already the next local day.
        PrinterSnapshot evening = snapshot with { Timestamp = new DateTimeOffset(2026, 10, 24, 21, 30, 0, TimeSpan.Zero) };
        Assert.Equal(new OverlayFinishTime(new DateTime(2026, 10, 25, 2, 30, 0), true), OverlayValues.Finish(evening, central));
    }

    [Fact]
    public void FinishTimeOfAnEndlessEstimateDoesNotOverflow()
    {
        PrinterSnapshot snapshot = Printing(job: new PrintJob { ProgressPercent = 1, Remaining = TimeSpan.MaxValue });

        Assert.Null(OverlayValues.Finish(snapshot, Utc));
        // The remaining time of such an estimate is still formatted, clamped instead of overflowing.
        Assert.NotEmpty(OverlayFormat.Duration(OverlayValues.RemainingMinutes(snapshot.Job!)!.Value));
        Assert.NotEmpty(OverlayFormat.Duration(long.MaxValue));
    }
}
