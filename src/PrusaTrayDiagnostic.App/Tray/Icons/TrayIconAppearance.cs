using Avalonia.Media;

namespace PrusaTrayDiagnostic.App.Tray.Icons;

internal enum TrayIconShape
{
    /// <summary>Filled circle.</summary>
    Dot,

    /// <summary>Small dot inside a ring that fills clockwise from the top with the progress (printing, paused).</summary>
    ProgressRing,

    /// <summary>Hollow ring with a diagonal stroke ("no data").</summary>
    CrossedRing,
}

/// <summary>Platform-independent description of one icon; drawn by <see cref="TrayIconRenderer"/>.</summary>
/// <param name="Fill">Main colour (dot, progress arc, ring).</param>
/// <param name="Outline">Darker edge that keeps the icon visible on light and dark taskbars.</param>
/// <param name="ProgressSweepDegrees">Arc length for <see cref="TrayIconShape.ProgressRing"/> (0–360), otherwise 0.</param>
internal readonly record struct TrayIconAppearance(TrayIconShape Shape, Color Fill, Color Outline, double ProgressSweepDegrees);
