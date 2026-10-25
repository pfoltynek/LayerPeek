using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Cameras;

/// <summary>Where a window is: position in screen pixels, size in device-independent units.</summary>
internal readonly record struct CameraWindowPlacement(int X, int Y, double Width, double Height)
{
    /// <summary>Largest accepted width or height (anything bigger comes from a damaged file).</summary>
    public const double MaxSize = 16384;

    /// <summary>Usable values (finite, positive size within <see cref="MaxSize"/>).</summary>
    public bool IsValid =>
        double.IsFinite(Width) && double.IsFinite(Height)
        && Width >= 1 && Height >= 1 && Width <= MaxSize && Height <= MaxSize;
}

/// <summary>
/// Which collapsible rows of the mini preview strip are expanded (T-090): the print name row and the temperature chart
/// row. <c>default</c>: both collapsed (T-090 Q1).
/// </summary>
/// <param name="Name">The print name row is expanded (it still hides without a job).</param>
/// <param name="Chart">The temperature chart row is expanded.</param>
internal readonly record struct CameraMiniRows(bool Name, bool Chart);

/// <summary>
/// Remembered state of the camera window of one printer (T-014): the mode and, separately, where the normal window and
/// the mini preview were. <see langword="null"/> placements: never placed (the window chooses its default).
/// </summary>
/// <param name="MiniRows">
/// The expanded rows of the mini preview strip (T-090 Q2/Q3): remembered also with "remember last placement" off (that
/// option is only about the position and size) and forgotten together with the rest of the state.
/// </param>
internal sealed record CameraWindowState(CameraWindowMode Mode, CameraWindowPlacement? Normal, CameraWindowPlacement? Mini, CameraMiniRows MiniRows = default)
{
    public static CameraWindowState Default { get; } = new(CameraWindowMode.Normal, null, null);
}
