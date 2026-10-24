namespace LayerPeek.App.Diagnostics.Chart;

/// <summary>Axis ranges and tick positions of the temperature chart ("nice" round numbers and clock times).</summary>
public static class ChartScale
{
    /// <summary>The value axis reaches at least this high (°C), so that a cold printer does not look like noise.</summary>
    public const double MinimumTop = 50;

    /// <summary>When the maximum is closer than this fraction of a step to the top, one more step is added.</summary>
    private const double Headroom = 0.05;

    private static readonly double[] NiceFractions = [1, 2, 2.5, 5, 10];

    private static readonly TimeSpan[] TimeSteps =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(20),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
    ];

    /// <summary>Smallest step of 1, 2, 2.5 or 5 × 10ⁿ that divides <paramref name="range"/> into at most <paramref name="maxTicks"/> parts.</summary>
    public static double NiceStep(double range, int maxTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(range);
        double raw = range / Math.Max(maxTicks, 1);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double fraction = raw / magnitude;
        foreach (double nice in NiceFractions)
        {
            // Tolerance: raw / magnitude of an exact power of ten may come out as 1.0000000000000002.
            if (fraction <= nice * (1 + 1e-9))
            {
                return nice * magnitude;
            }
        }

        return 10 * magnitude;
    }

    /// <summary>
    /// Value axis from 0 to a multiple of the step above <paramref name="maxValue"/> (at least <see cref="MinimumTop"/>);
    /// NaN (no data) gives the minimum range.
    /// </summary>
    public static (double Top, double Step) ValueAxis(float maxValue, int maxTicks)
    {
        double max = float.IsFinite(maxValue) ? maxValue : 0;
        double step = NiceStep(Math.Max(max, MinimumTop), maxTicks);
        double top = Math.Ceiling(Math.Max(max, MinimumTop) / step) * step;
        if (top - max < step * Headroom)
        {
            top += step;
        }

        return (top, step);
    }

    /// <summary>Smallest clock-friendly step (1 min … 1 day) that gives at most <paramref name="maxTicks"/> ticks in <paramref name="window"/>.</summary>
    public static TimeSpan TimeStep(TimeSpan window, int maxTicks)
    {
        long ticks = Math.Max(maxTicks, 1);
        foreach (TimeSpan step in TimeSteps)
        {
            if (window.Ticks <= step.Ticks * ticks)
            {
                return step;
            }
        }

        return TimeSteps[^1];
    }

    /// <summary>
    /// Fills <paramref name="ticks"/> (cleared first) with the instants within <paramref name="from"/> – <paramref name="to"/>
    /// (both inclusive) whose local wall-clock time in <paramref name="zone"/> is a multiple of <paramref name="step"/>
    /// since midnight, so that labels stay round (e.g. 0:00, 4:00, 8:00) also across a daylight saving change.
    /// A wall-clock time skipped by the change (spring forward) has no tick; a repeated one (fall back) gets one, at its
    /// standard-time occurrence. Allocation-free once <paramref name="ticks"/> has the capacity.
    /// </summary>
    public static void TimeTicks(DateTimeOffset from, DateTimeOffset to, TimeSpan step, TimeZoneInfo zone, List<DateTimeOffset> ticks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(ticks);
        ticks.Clear();
        if (from > to)
        {
            return;
        }

        // Start one step early: the offset may differ between "from" and the first tick.
        long local = TimeZoneInfo.ConvertTime(from, zone).Ticks - step.Ticks;
        long remainder = local % step.Ticks;
        long wall = remainder == 0 ? local : local - remainder + step.Ticks;
        long maxWall = TimeZoneInfo.ConvertTime(to, zone).Ticks + step.Ticks;
        for (; wall <= maxWall; wall += step.Ticks)
        {
            DateTime wallTime = new(wall, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(wallTime))
            {
                continue;
            }

            DateTimeOffset tick = new(TimeZoneInfo.ConvertTimeToUtc(wallTime, zone), TimeSpan.Zero);
            if (tick >= from && tick <= to && (ticks.Count == 0 || tick > ticks[^1]))
            {
                ticks.Add(tick);
            }
        }
    }
}
