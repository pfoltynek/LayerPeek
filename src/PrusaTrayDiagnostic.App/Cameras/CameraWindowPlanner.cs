using Avalonia;

using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>
/// Where the camera window goes when it opens or switches modes (T-035), as pure functions over the monitors of an
/// <see cref="IScreenCatalog"/>: a remembered placement while it is reachable, otherwise the defaults of the printer
/// (<see cref="CameraWindowDefaultPlacement"/>). Positions are screen pixels, sizes device-independent units.
/// </summary>
/// <remarks>
/// The frame: Avalonia's <c>Position</c> is the top left corner of the whole window and <c>Width</c> / <c>Height</c> the
/// client area, so the frame is part of the outer rectangle the defaults are placed with. On Windows 11 the frame of a
/// normal window is 16 × 39 DIP (measured 2026-10-19: client 400 × 300, <c>FrameSize</c> 416 × 339; 8 DIP on the left,
/// right and bottom are the invisible resize borders, so the visible gap to the work area edge is about 8 DIP more than
/// the 16 DIP margin). The mini preview has no frame.
/// </remarks>
internal static class CameraWindowPlanner
{
    /// <summary>Frame of the normal window assumed until the real one is measured after opening (see remarks).</summary>
    public static Size AssumedNormalFrame { get; } = OperatingSystem.IsWindows() ? new Size(16, 39) : new Size(0, 32);

    /// <summary>The frame of a window in <paramref name="mode"/> before it is shown.</summary>
    public static Size AssumedFrame(CameraWindowMode mode) => mode == CameraWindowMode.Mini ? default : AssumedNormalFrame;

    /// <summary>
    /// Where the window opens in <paramref name="mode"/> (normal window or mini preview; full screen opens the normal
    /// window on its monitor first): <paramref name="remembered"/> when it is valid and reachable on
    /// <paramref name="screens"/>, else the defaults on the chosen monitor. <see langword="null"/> without screens and
    /// without a usable remembered placement (the window then keeps its default size and centres itself).
    /// </summary>
    public static CameraWindowPlacement? ForOpening(
        CameraWindowDefaults defaults,
        CameraWindowMode mode,
        CameraWindowPlacement? remembered,
        IReadOnlyList<CameraScreen> screens,
        Size frame)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(screens);
        if (remembered is { IsValid: true } placement && IsReachable(placement, screens))
        {
            return placement;
        }

        CameraWindowMode layout = mode == CameraWindowMode.Mini ? CameraWindowMode.Mini : CameraWindowMode.Normal;
        return CameraWindowDefaultPlacement.Compute(defaults, layout, screens, frame.Width, frame.Height) is { } target
            ? ToPlacement(target)
            : null;
    }

    /// <summary>
    /// <paramref name="layout"/> on the monitor the window is on now (T-035 Q5: switching modes in an open window, e.g. to
    /// the mini preview that has no remembered placement). <see langword="null"/> without screens.
    /// </summary>
    public static CameraWindowPlacement? OnCurrentScreen(CameraWindowLayout layout, PixelBounds window, IReadOnlyList<CameraScreen> screens, Size frame)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return ScreenOf(window, screens) is { } screen
            ? ToPlacement(CameraWindowDefaultPlacement.Place(layout, screen, frame.Width, frame.Height))
            : null;
    }

    /// <summary>
    /// The monitor a window is on: the one it overlaps most, else (outside every monitor) the primary one. Like the OS
    /// ("nearest" would differ only for a window that is entirely off screen). <see langword="null"/> without screens.
    /// </summary>
    public static CameraScreen? ScreenOf(PixelBounds window, IReadOnlyList<CameraScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        CameraScreen? best = null;
        long bestArea = 0;
        foreach (CameraScreen screen in screens)
        {
            long area = Overlap(window, screen.Bounds);
            if (area > bestArea)
            {
                best = screen;
                bestArea = area;
            }
        }

        return best ?? CameraWindowDefaultPlacement.SelectScreen(null, screens)?.Screen;
    }

    /// <summary>
    /// Whether the title bar of <paramref name="placement"/> can be grabbed on the work areas of <paramref name="screens"/>
    /// (<see cref="CameraWindowPlacementRules.IsReachable"/>); the size is converted with the scaling of the monitor the
    /// window is on. Without screens there is nothing to check.
    /// </summary>
    public static bool IsReachable(CameraWindowPlacement placement, IReadOnlyList<CameraScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (screens.Count == 0)
        {
            return true;
        }

        PixelBounds corner = new(placement.X, placement.Y, 1, 1);
        double scaling = ScreenOf(corner, screens)?.Scaling ?? 1.0;
        PixelRect window = new(placement.X, placement.Y, ToPixels(placement.Width, scaling), ToPixels(placement.Height, scaling));
        return CameraWindowPlacementRules.IsReachable(window, [.. screens.Select(static screen => ToRect(screen.UsableArea))]);
    }

    /// <summary>
    /// The frame around the client area from the measured window size (DIP); <see langword="null"/> while nothing usable is
    /// measured (not shown yet, or a platform that reports the client size as the frame).
    /// </summary>
    public static Size? FrameOf(Size? frameSize, Size clientSize) =>
        frameSize is { } frame && frame.Width >= clientSize.Width && frame.Height >= clientSize.Height && frame != clientSize
            ? new Size(frame.Width - clientSize.Width, frame.Height - clientSize.Height)
            : null;

    private static CameraWindowPlacement ToPlacement(CameraWindowTarget target) =>
        new(target.Bounds.X, target.Bounds.Y, target.ClientWidth, target.ClientHeight);

    private static long Overlap(PixelBounds a, PixelBounds b)
    {
        long width = (long)Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        long height = (long)Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }

    private static int ToPixels(double dip, double scaling) => (int)Math.Clamp(Math.Round(dip * scaling), 1, int.MaxValue / 4);

    private static PixelRect ToRect(PixelBounds bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
}
