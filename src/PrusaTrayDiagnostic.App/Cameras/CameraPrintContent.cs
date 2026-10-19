using System.Globalization;

using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>
/// What the print overlay of the camera preview shows, already rounded like the display (same rules as the tray tooltip:
/// progress and remaining time only for a running, paused or waiting job of a reachable printer). Cheap to compare, so
/// polls that change nothing visible are dropped before any text is formatted.
/// </summary>
/// <param name="State">Printer state; <see langword="null"/> before the first snapshot (nothing is shown).</param>
internal readonly record struct CameraPrintContent(PrinterState? State, int? ProgressPercent, long? RemainingMinutes)
{
    public static CameraPrintContent From(PrinterSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return default;
        }

        int? progress = null;
        long? remaining = null;
        if (snapshot.Job is { } job
            && snapshot.State.IsReachable()
            && (snapshot.State.HasActiveJob() || snapshot.State == PrinterState.Attention))
        {
            progress = ToolTipContent.WholePercent(job.ProgressPercent);
            remaining = job.EstimatedRemaining is { } estimate ? ToolTipContent.CeilingMinutes(estimate) : null;
        }

        return new(snapshot.State, progress, remaining);
    }

    /// <summary>"Printing – 42%"; <see langword="null"/> before the first snapshot.</summary>
    public string? StateText()
    {
        if (State is not { } state)
        {
            return null;
        }

        string text = TrayText.StateName(state);
        return ProgressPercent is { } progress
            ? Format(Strings.ToolTipStateWithProgress, text, Format(Strings.ToolTipProgress, progress))
            : text;
    }

    /// <summary>"1 h 5 min remaining"; <see langword="null"/> when not shown.</summary>
    public string? RemainingText() =>
        RemainingMinutes is { } minutes
            ? Format(Strings.ToolTipRemaining, TrayToolTipFormatter.FormatDuration(TimeSpan.FromMinutes(minutes)))
            : null;

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);
}
