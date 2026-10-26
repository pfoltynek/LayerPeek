using LayerPeek.Core.Previews;

namespace LayerPeek.Core.Printers;

/// <summary>
/// The job currently handled by the printer. Values are normalized on assignment:
/// progress is clamped to 0–100 and negative durations become unknown (<see langword="null"/>).
/// </summary>
public sealed record PrintJob
{
    /// <summary>
    /// Identifier assigned by the data source, unique per printer and job (a reprint of the same file gets a new one);
    /// <see langword="null"/> when unknown. Tells consecutive jobs apart even when their file names are equal.
    /// </summary>
    public long? Id { get; init; }

    /// <summary>File name of the job, <see langword="null"/> when unknown.</summary>
    public string? FileName
    {
        get;
        init => field = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Where the data source keeps the G-code preview of the job, for <see cref="IPrintPreviewSource"/>;
    /// <see langword="null"/> when the source offers none or it is unknown. Opaque outside the client that set it.
    /// </summary>
    public PrintPreviewReference? PreviewReference { get; init; }

    /// <summary>Progress in percent, always within 0–100.</summary>
    public double ProgressPercent
    {
        get;
        init => field = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 100);
    }

    /// <summary>Time spent printing.</summary>
    public TimeSpan? Elapsed
    {
        get;
        init => field = NonNegativeOrNull(value);
    }

    /// <summary>Remaining time as reported by the printer.</summary>
    public TimeSpan? Remaining
    {
        get;
        init => field = NonNegativeOrNull(value);
    }

    /// <summary>
    /// <see cref="Remaining"/> when reported, otherwise a linear extrapolation from
    /// <see cref="Elapsed"/> and <see cref="ProgressPercent"/>; <see langword="null"/> when neither is possible.
    /// The extrapolation is rough early in a print because <see cref="Elapsed"/> includes heating.
    /// </summary>
    public TimeSpan? EstimatedRemaining => Remaining ?? ExtrapolateRemaining();

    private TimeSpan? ExtrapolateRemaining()
    {
        if (Elapsed is not { } elapsed || ProgressPercent <= 0)
        {
            return null;
        }

        if (ProgressPercent >= 100)
        {
            return TimeSpan.Zero;
        }

        double remainingTicks = elapsed.Ticks * ((100 - ProgressPercent) / ProgressPercent);
        return remainingTicks < TimeSpan.MaxValue.Ticks ? TimeSpan.FromTicks((long)remainingTicks) : null;
    }

    private static TimeSpan? NonNegativeOrNull(TimeSpan? value) =>
        value < TimeSpan.Zero ? null : value;
}
