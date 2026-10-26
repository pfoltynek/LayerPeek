using LayerPeek.App.Tray;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>A temperature as the overlay shows it: whole degrees; <see cref="Target"/> only while the heater is on.</summary>
public readonly record struct OverlayTemperature(long Actual, long? Target);

/// <summary>
/// When a print finishes, as the overlay shows it (T-036): local time rounded up to the minute, and whether that is
/// another local day than the snapshot.
/// </summary>
public readonly record struct OverlayFinishTime(DateTime Local, bool IsOnAnotherDay);

/// <summary>
/// The values of a snapshot the overlay widgets show, already rounded like the display (T-036 rules, shared by all
/// widgets): a widget builds its cheaply comparable content from these, so that polls that change nothing visible are
/// dropped before any text is formatted. Pure functions without allocations; any thread.
/// </summary>
/// <remarks>
/// Visibility rules (T-036 decision, same as the T-014 overlay and the tray tooltip for the job data):
/// <list type="bullet">
/// <item>State: once the printer was polled.</item>
/// <item>Temperatures: whenever the snapshot has them (finite values; none while the printer is offline).</item>
/// <item>Job values (progress, remaining time, finish time, print time, file name): only while the printer is reachable
/// and has a running, paused or waiting job (<see cref="PrinterStateExtensions.HasActiveJob"/> or
/// <see cref="PrinterState.Attention"/>), each when it is known.</item>
/// </list>
/// </remarks>
public static class OverlayValues
{
    /// <summary>
    /// The job whose values are shown: only while the printer is reachable and has a running, paused or waiting job;
    /// <see langword="null"/> otherwise (also before the first poll).
    /// </summary>
    public static PrintJob? ActiveJob(PrinterSnapshot? snapshot) =>
        snapshot is { Job: { } job } && snapshot.State.IsReachable() && (snapshot.State.HasActiveJob() || snapshot.State == PrinterState.Attention)
            ? job
            : null;

    /// <summary>Whole percent, rounded down so that an unfinished print never shows 100.</summary>
    public static int ProgressPercent(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return ToolTipContent.WholePercent(job.ProgressPercent);
    }

    /// <summary>Remaining minutes rounded up (extrapolated like the tooltip); <see langword="null"/> when unknown.</summary>
    public static long? RemainingMinutes(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.EstimatedRemaining is { } remaining ? ToolTipContent.CeilingMinutes(remaining) : null;
    }

    /// <summary>Print time so far in minutes, rounded up; <see langword="null"/> when unknown.</summary>
    public static long? PrintTimeMinutes(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Elapsed is { } elapsed ? ToolTipContent.CeilingMinutes(elapsed) : null;
    }

    /// <summary>
    /// When the job of <paramref name="snapshot"/> finishes in <paramref name="zone"/> (the local one): rounded up to the
    /// minute like the remaining time, so that both agree ("5 min" → "14:35"); <see langword="null"/> without an estimate.
    /// The offset is the one at the finish (daylight saving changes in between are taken into account).
    /// </summary>
    public static OverlayFinishTime? Finish(PrinterSnapshot snapshot, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(zone);
        if (snapshot.EstimatedCompletion is not { } completion)
        {
            return null;
        }

        DateTimeOffset local = TimeZoneInfo.ConvertTime(CeilingToMinute(completion), zone);
        return new OverlayFinishTime(local.DateTime, local.Date != TimeZoneInfo.ConvertTime(snapshot.Timestamp, zone).Date);
    }

    /// <summary>
    /// Whole degrees; the target only while heating (a target of 0 means the heater is off); <see langword="null"/> when
    /// the temperature is unknown or not finite.
    /// </summary>
    public static OverlayTemperature? Temperature(Temperature? temperature)
    {
        if (temperature is not { } value || !double.IsFinite(value.Actual))
        {
            return null;
        }

        long? target = value.Target is { } t && double.IsFinite(t) ? WholeDegrees(t) : null;
        return new OverlayTemperature(WholeDegrees(value.Actual), target is > 0 ? target : null);
    }

    private static long WholeDegrees(double celsius) => (long)Math.Round(Math.Clamp(celsius, -1e6, 1e6), MidpointRounding.AwayFromZero);

    private static DateTimeOffset CeilingToMinute(DateTimeOffset value)
    {
        long rest = value.Ticks % TimeSpan.TicksPerMinute;
        long missing = rest == 0 ? 0 : TimeSpan.TicksPerMinute - rest;
        return missing <= DateTime.MaxValue.Ticks - Math.Max(value.Ticks, value.UtcTicks) ? value.AddTicks(missing) : value;
    }
}
