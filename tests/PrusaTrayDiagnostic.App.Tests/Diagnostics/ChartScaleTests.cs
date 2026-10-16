using PrusaTrayDiagnostic.App.Diagnostics.Chart;

namespace PrusaTrayDiagnostic.App.Tests.Diagnostics;

public sealed class ChartScaleTests
{
    [Theory]
    [InlineData(250, 5, 50)]
    [InlineData(215, 5, 50)]
    [InlineData(60, 5, 20)]
    [InlineData(100, 4, 25)]
    [InlineData(50, 5, 10)]
    [InlineData(7, 5, 2)]
    [InlineData(1000, 5, 200)]
    public void NiceStepIsOneTwoTwoAndHalfOrFiveTimesAPowerOfTen(double range, int maxTicks, double expected) =>
        Assert.Equal(expected, ChartScale.NiceStep(range, maxTicks), 9);

    [Theory]
    [InlineData(215.5f, 5, 250, 50)]
    [InlineData(60f, 5, 80, 20)]
    [InlineData(250f, 5, 300, 50)]
    [InlineData(20f, 5, 50, 10)]
    public void ValueAxisStartsAtZeroAndLeavesRoomAboveTheMaximum(float max, int maxTicks, double expectedTop, double expectedStep)
    {
        (double top, double step) = ChartScale.ValueAxis(max, maxTicks);

        Assert.Equal(expectedTop, top, 9);
        Assert.Equal(expectedStep, step, 9);
    }

    [Fact]
    public void ValueAxisWithoutDataHasAUsableDefaultRange()
    {
        (double top, double step) = ChartScale.ValueAxis(float.NaN, 5);

        Assert.True(top > 0);
        Assert.True(step > 0);
    }

    [Theory]
    [InlineData(30, 6, 5)]
    [InlineData(120, 6, 20)]
    [InlineData(1440, 6, 240)]
    [InlineData(1440, 3, 720)]
    [InlineData(30, 1, 30)]
    [InlineData(30, 0, 30)]
    [InlineData(100_000, 6, 1440)]
    public void TimeStepKeepsTheNumberOfTicksWithinTheLimit(int windowMinutes, int maxTicks, int expectedMinutes) =>
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), ChartScale.TimeStep(TimeSpan.FromMinutes(windowMinutes), maxTicks));

    [Fact]
    public void TimeTicksAreAlignedToLocalTime()
    {
        List<DateTimeOffset> ticks = [];
        DateTimeOffset from = new(2026, 10, 16, 10, 7, 30, TimeSpan.Zero);

        ChartScale.TimeTicks(from, from.AddMinutes(20), TimeSpan.FromMinutes(5), TimeZoneInfo.Utc, ticks);
        Assert.Equal([Utc(10, 10), Utc(10, 15), Utc(10, 20), Utc(10, 25)], ticks);

        // UTC+5:30: 15:37 local → first tick 16:00 local = 10:30 UTC.
        ChartScale.TimeTicks(from, from.AddHours(2), TimeSpan.FromHours(1), Fixed(new TimeSpan(5, 30, 0)), ticks);
        Assert.Equal([Utc(10, 30), Utc(11, 30)], ticks);
    }

    [Fact]
    public void TickOnTheStartOrEndOfTheRangeIsIncluded()
    {
        List<DateTimeOffset> ticks = [];

        ChartScale.TimeTicks(Utc(10, 0), Utc(11, 0), TimeSpan.FromMinutes(20), TimeZoneInfo.Utc, ticks);

        Assert.Equal([Utc(10, 0), Utc(10, 20), Utc(10, 40), Utc(11, 0)], ticks);
    }

    [Fact]
    public void TicksStayOnRoundLocalHoursWhenSummerTimeEnds()
    {
        // 25 Oct 2026, 03:00 CEST → 02:00 CET (01:00 UTC).
        List<DateTimeOffset> ticks = [];
        DateTimeOffset from = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

        ChartScale.TimeTicks(from, from.AddDays(1), TimeSpan.FromHours(4), CentralEurope, ticks);

        Assert.NotEmpty(ticks);
        AssertIncreasing(ticks);
        Assert.All(ticks, tick =>
        {
            DateTimeOffset local = TimeZoneInfo.ConvertTime(tick, CentralEurope);
            Assert.Equal(0, local.Hour % 4);
            Assert.Equal(0, local.Minute);
        });
        Assert.Contains(ticks, tick => TimeZoneInfo.ConvertTime(tick, CentralEurope).Offset == TimeSpan.FromHours(2));
        Assert.Contains(ticks, tick => TimeZoneInfo.ConvertTime(tick, CentralEurope).Offset == TimeSpan.FromHours(1));
    }

    [Fact]
    public void RepeatedHourWhenSummerTimeEndsGetsOneTick()
    {
        // 25 Oct 2026: local 02:00 occurs twice (CEST at 00:00 UTC, CET at 01:00 UTC).
        List<DateTimeOffset> ticks = [];
        DateTimeOffset from = new(2026, 10, 24, 23, 0, 0, TimeSpan.Zero);

        ChartScale.TimeTicks(from, from.AddHours(4), TimeSpan.FromHours(1), CentralEurope, ticks);

        AssertIncreasing(ticks);
        Assert.Equal([1, 2, 3, 4], ticks.Select(tick => TimeZoneInfo.ConvertTime(tick, CentralEurope).Hour));
    }

    [Fact]
    public void TicksSkipTheMissingHourWhenSummerTimeStarts()
    {
        // 29 Mar 2026, 02:00 CET → 03:00 CEST: local 02:00 does not exist.
        List<DateTimeOffset> ticks = [];
        DateTimeOffset from = new(2026, 3, 28, 23, 0, 0, TimeSpan.Zero);

        ChartScale.TimeTicks(from, from.AddHours(4), TimeSpan.FromHours(1), CentralEurope, ticks);

        AssertIncreasing(ticks);
        Assert.Equal([0, 1, 3, 4, 5], ticks.Select(tick => TimeZoneInfo.ConvertTime(tick, CentralEurope).Hour));
    }

    [Fact]
    public void TicksReuseTheCallersList()
    {
        List<DateTimeOffset> ticks = [Utc(1, 0), Utc(2, 0)];

        ChartScale.TimeTicks(Utc(10, 0), Utc(10, 30), TimeSpan.FromHours(1), TimeZoneInfo.Utc, ticks);

        Assert.Equal([Utc(10, 0)], ticks);
    }

    /// <summary>A fixed CET/CEST zone (EU rules), independent of the zones installed on the machine.</summary>
    private static readonly TimeZoneInfo CentralEurope = TimeZoneInfo.CreateCustomTimeZone(
        "Test CET",
        TimeSpan.FromHours(1),
        "Test CET",
        "Test CET",
        "Test CEST",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1),
                new DateTime(2099, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)),
        ]);

    private static DateTimeOffset Utc(int hour, int minute) => new(2026, 10, 16, hour, minute, 0, TimeSpan.Zero);

    private static TimeZoneInfo Fixed(TimeSpan offset) => TimeZoneInfo.CreateCustomTimeZone("Test", offset, "Test", "Test");

    private static void AssertIncreasing(List<DateTimeOffset> ticks)
    {
        for (int i = 1; i < ticks.Count; i++)
        {
            Assert.True(ticks[i] > ticks[i - 1], $"Tick {i} ({ticks[i]:O}) is not after {ticks[i - 1]:O}.");
        }
    }
}
