namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>How the camera window is shown. Public only because compiled bindings and xUnit theory data need it.</summary>
public enum CameraWindowMode
{
    /// <summary>A regular resizable window with a title bar.</summary>
    Normal,

    /// <summary>The whole screen.</summary>
    FullScreen,

    /// <summary>Small borderless preview, always on top of other windows (picture-in-picture).</summary>
    Mini,
}

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
/// Remembered state of the camera window of one printer (T-014): the mode and, separately, where the normal window and
/// the mini preview were. <see langword="null"/> placements: never placed (the window chooses its default).
/// </summary>
internal sealed record CameraWindowState(CameraWindowMode Mode, CameraWindowPlacement? Normal, CameraWindowPlacement? Mini)
{
    public static CameraWindowState Default { get; } = new(CameraWindowMode.Normal, null, null);
}
