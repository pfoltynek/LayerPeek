using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Tray;

/// <summary>
/// Exactly the values the tooltip of one printer shows, already rounded the way they are displayed.
/// Cheap to build and compare (no formatting, no resource lookups), so polls that change nothing visible
/// are detected before any text is formatted.
/// </summary>
/// <param name="State">Printer state; <see langword="null"/> before the first snapshot ("Connecting…").</param>
/// <param name="ProgressPercent">Whole percent, rounded down; <see langword="null"/> when not shown.</param>
/// <param name="RemainingMinutes">Remaining time rounded up to minutes; <see langword="null"/> when not shown.</param>
internal readonly record struct ToolTipContent(
    string Name,
    PrinterState? State,
    int? ProgressPercent,
    long? RemainingMinutes,
    string? FileName,
    int OtherPrinterCount)
{
    /// <summary>What the icon would show for this printer; not polled yet counts as offline.</summary>
    public TrayStatus Status => State is { } state ? TrayStatusRules.FromPrinterState(state) : TrayStatus.Offline;

    public static ToolTipContent From(TrayPrinter printer, int otherPrinterCount)
    {
        PrinterSnapshot? snapshot = printer.Snapshot;
        int? progress = null;
        long? remaining = null;
        string? fileName = null;

        // Job data of an unreachable printer would be stale or missing, so it is shown only for live states.
        if (snapshot is { Job: { } job } && snapshot.State.IsReachable())
        {
            fileName = job.FileName;
            if (snapshot.State.HasActiveJob() || snapshot.State == PrinterState.Attention)
            {
                progress = WholePercent(job.ProgressPercent);
                remaining = job.EstimatedRemaining is { } estimate ? CeilingMinutes(estimate) : null;
            }
        }

        return new(printer.Definition.Name, snapshot?.State, progress, remaining, fileName, Math.Max(otherPrinterCount, 0));
    }

    /// <summary>Rounded down, so that an unfinished print never shows 100.</summary>
    public static int WholePercent(double percent) =>
        double.IsNaN(percent) ? 0 : (int)Math.Floor(Math.Clamp(percent, 0, 100));

    public static long CeilingMinutes(TimeSpan duration)
    {
        long ticks = Math.Max(duration.Ticks, 0);
        return (ticks / TimeSpan.TicksPerMinute) + (ticks % TimeSpan.TicksPerMinute == 0 ? 0 : 1);
    }
}
