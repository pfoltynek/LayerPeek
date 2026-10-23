using Avalonia;

using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>
/// The frame around the client area of a window, in device-independent units: <paramref name="Frame"/> is the whole
/// window minus the client area (what Avalonia's <c>FrameSize</c> adds), <paramref name="InvisibleBorders"/> the part of
/// it the user does not see, per side (the resize borders of Windows 10/11). Placement keeps the <em>visible</em> window
/// inside the work area with the margin (T-035 Q9).
/// </summary>
internal readonly record struct WindowChrome(Size Frame, Thickness InvisibleBorders)
{
    /// <summary>No frame at all (the borderless mini preview).</summary>
    public static WindowChrome None => default;

    /// <summary>The visible part of the frame (title bar and the thin visible border).</summary>
    public Size VisibleFrame => new(
        Math.Max(Frame.Width - InvisibleBorders.Left - InvisibleBorders.Right, 0),
        Math.Max(Frame.Height - InvisibleBorders.Top - InvisibleBorders.Bottom, 0));

    /// <summary>
    /// The chrome of <paramref name="frame"/> on a monitor with <paramref name="scaling"/> (T-071 Q3: known before the
    /// window is shown). The left, right and bottom sides are invisible except for the visible border; the top is all
    /// visible (Windows 10/11 draw the top resize border inside the title bar; measured: <c>DWMWA_EXTENDED_FRAME_BOUNDS</c>
    /// has the top of <c>GetWindowRect</c>). Pixels are divided by the scaling, so that placing converts them back exactly.
    /// </summary>
    public static WindowChrome FromPixels(PixelFrame frame, double scaling)
    {
        if (!double.IsFinite(scaling) || scaling <= 0)
        {
            scaling = 1.0;
        }

        return new WindowChrome(
            new Size((frame.Left + frame.Right) / scaling, (frame.Top + frame.Bottom) / scaling),
            new Thickness(
                Math.Max(frame.Left - frame.VisibleBorder, 0) / scaling,
                0,
                Math.Max(frame.Right - frame.VisibleBorder, 0) / scaling,
                Math.Max(frame.Bottom - frame.VisibleBorder, 0) / scaling));
    }

    /// <summary>Whether <paramref name="other"/> differs by less than half a DIP everywhere.</summary>
    public bool IsCloseTo(WindowChrome other) => IsCloseTo(other, 1.0);

    /// <summary>
    /// Whether <paramref name="other"/> differs by less than half a pixel everywhere on a monitor with
    /// <paramref name="scaling"/>, i.e. it places the window on the same pixels (no correction needed).
    /// </summary>
    public bool IsCloseTo(WindowChrome other, double scaling)
    {
        double limit = 0.5 / (double.IsFinite(scaling) && scaling > 0 ? scaling : 1.0);
        return Close(Frame.Width, other.Frame.Width, limit) && Close(Frame.Height, other.Frame.Height, limit)
            && Close(InvisibleBorders.Left, other.InvisibleBorders.Left, limit) && Close(InvisibleBorders.Top, other.InvisibleBorders.Top, limit)
            && Close(InvisibleBorders.Right, other.InvisibleBorders.Right, limit) && Close(InvisibleBorders.Bottom, other.InvisibleBorders.Bottom, limit);
    }

    private static bool Close(double a, double b, double limit) => Math.Abs(a - b) < limit;
}

/// <summary>
/// The frame Windows gives a window at one DPI, in pixels per side (<c>AdjustWindowRectExForDpi</c> of its style), and
/// how many of those pixels the window manager draws as the visible border (the rest are invisible resize borders).
/// </summary>
internal readonly record struct PixelFrame(int Left, int Top, int Right, int Bottom, int VisibleBorder);

/// <summary>
/// Where the camera window goes when it opens or switches modes (T-035), as pure functions over the monitors of an
/// <see cref="IScreenCatalog"/>: a remembered placement while it is reachable, otherwise the defaults of the printer
/// (<see cref="CameraWindowDefaultPlacement"/>). Positions are screen pixels, sizes device-independent units.
/// </summary>
/// <remarks>
/// The frame: Avalonia's <c>Position</c> is the top left corner of the whole window and <c>Width</c> / <c>Height</c> the
/// client area. On Windows 11 (measured 2026-10-19 at 100 %: client 400 × 300, <c>FrameSize</c> 416 × 339,
/// <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> 7 px inside on the left, right and bottom) the frame of a normal window is 16 × 39
/// DIP, of which 7 / 0 / 7 / 7 DIP are invisible resize borders. The defaults place the <em>visible</em> window (client +
/// visible frame) with the 16 DIP margin and the position is moved out by the invisible left and top borders, so that the
/// visible gap is exactly the margin (Q9). The frame does not scale linearly with the DPI (Windows 11: 8 / 31 px per side
/// / title bar at 100 %, 9 / 38 at 125 %, 11 / 45 at 150 %), so the camera window asks Windows for the frame at the DPI of
/// the monitor it goes to before it is shown (T-071 Q3, <see cref="WindowChrome.FromPixels"/>); the chrome measured at
/// 100 % (<see cref="AssumedNormalChrome"/>) is only the fallback where that is not possible. The mini preview has no frame.
/// </remarks>
internal static class CameraWindowPlanner
{
    /// <summary>
    /// Chrome of the normal window when the real one cannot be asked for before showing (not Windows, no window handle):
    /// the one measured at 100 % (see remarks).
    /// </summary>
    public static WindowChrome AssumedNormalChrome { get; } = OperatingSystem.IsWindows()
        ? new WindowChrome(new Size(16, 39), new Thickness(7, 0, 7, 7))
        : new WindowChrome(new Size(0, 32), default);

    /// <summary>The fallback chrome of a window in <paramref name="mode"/> (see <see cref="AssumedNormalChrome"/>).</summary>
    public static WindowChrome AssumedChrome(CameraWindowMode mode) => mode == CameraWindowMode.Mini ? WindowChrome.None : AssumedNormalChrome;

    /// <inheritdoc cref="ForOpening(CameraWindowDefaults, CameraWindowMode, CameraWindowPlacement?, IReadOnlyList{CameraScreen}, Func{CameraScreen, WindowChrome})"/>
    public static CameraWindowPlacement? ForOpening(
        CameraWindowDefaults defaults,
        CameraWindowMode mode,
        CameraWindowPlacement? remembered,
        IReadOnlyList<CameraScreen> screens,
        WindowChrome chrome) =>
        ForOpening(defaults, mode, remembered, screens, _ => chrome);

    /// <summary>
    /// Where the window opens in <paramref name="mode"/> (normal window or mini preview; full screen opens the normal
    /// window on its monitor first): <paramref name="remembered"/> when it is valid and reachable on
    /// <paramref name="screens"/>, else the defaults on the chosen monitor with the chrome the window has there
    /// (<paramref name="chromeOn"/>). <see langword="null"/> without screens and without a usable remembered placement (the
    /// window then keeps its default size and centres itself).
    /// </summary>
    public static CameraWindowPlacement? ForOpening(
        CameraWindowDefaults defaults,
        CameraWindowMode mode,
        CameraWindowPlacement? remembered,
        IReadOnlyList<CameraScreen> screens,
        Func<CameraScreen, WindowChrome> chromeOn)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(chromeOn);
        if (remembered is { IsValid: true } placement && IsReachable(placement, screens))
        {
            return placement;
        }

        CameraWindowLayout layout = mode == CameraWindowMode.Mini ? defaults.Mini : defaults.Normal;
        return CameraWindowDefaultPlacement.SelectScreen(defaults.Monitor, screens) is { } selection
            ? Place(layout, selection.Screen, chromeOn(selection.Screen))
            : null;
    }

    /// <inheritdoc cref="OnCurrentScreen(CameraWindowLayout, PixelBounds, IReadOnlyList{CameraScreen}, Func{CameraScreen, WindowChrome})"/>
    public static CameraWindowPlacement? OnCurrentScreen(CameraWindowLayout layout, PixelBounds window, IReadOnlyList<CameraScreen> screens, WindowChrome chrome) =>
        OnCurrentScreen(layout, window, screens, _ => chrome);

    /// <summary>
    /// <paramref name="layout"/> on the monitor the window is on now (T-035 Q5: switching modes in an open window, e.g. to
    /// the mini preview that has no remembered placement), with the chrome the window has there
    /// (<paramref name="chromeOn"/>). <see langword="null"/> without screens.
    /// </summary>
    public static CameraWindowPlacement? OnCurrentScreen(CameraWindowLayout layout, PixelBounds window, IReadOnlyList<CameraScreen> screens, Func<CameraScreen, WindowChrome> chromeOn)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(chromeOn);
        return ScreenOf(window, screens) is { } screen ? Place(layout, screen, chromeOn(screen)) : null;
    }

    /// <summary>
    /// <paramref name="layout"/> on <paramref name="screen"/> so that the visible window (client area + visible frame) keeps
    /// the margin from the work area; the returned position is the corner of the whole window, i.e. moved left and up by
    /// the invisible borders (pixels of the monitor's scaling, rounded half away from zero like Core).
    /// </summary>
    public static CameraWindowPlacement Place(CameraWindowLayout layout, CameraScreen screen, WindowChrome chrome)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(screen);
        Size visible = chrome.VisibleFrame;
        CameraWindowTarget target = CameraWindowDefaultPlacement.Place(layout, screen, visible.Width, visible.Height);
        return new CameraWindowPlacement(
            target.Bounds.X - ToOffset(chrome.InvisibleBorders.Left, screen.Scaling),
            target.Bounds.Y - ToOffset(chrome.InvisibleBorders.Top, screen.Scaling),
            target.ClientWidth,
            target.ClientHeight);
    }

    /// <summary>
    /// The invisible borders (DIP) from the whole window rectangle and the visible one (Windows: <c>GetWindowRect</c> and
    /// <c>DWMWA_EXTENDED_FRAME_BOUNDS</c>, both in pixels); a side where the visible rectangle is not inside counts as 0.
    /// </summary>
    public static Thickness InvisibleBordersOf(PixelRect window, PixelRect visible, double scaling)
    {
        if (!double.IsFinite(scaling) || scaling <= 0)
        {
            scaling = 1.0;
        }

        return new Thickness(
            Math.Max(visible.X - window.X, 0) / scaling,
            Math.Max(visible.Y - window.Y, 0) / scaling,
            Math.Max(window.Right - visible.Right, 0) / scaling,
            Math.Max(window.Bottom - visible.Bottom, 0) / scaling);
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

    private static long Overlap(PixelBounds a, PixelBounds b)
    {
        long width = (long)Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        long height = (long)Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }

    private static int ToOffset(double dip, double scaling) => (int)Math.Round(dip * scaling, MidpointRounding.AwayFromZero);

    private static int ToPixels(double dip, double scaling) => (int)Math.Clamp(Math.Round(dip * scaling, MidpointRounding.AwayFromZero), 1, int.MaxValue / 4);

    private static PixelRect ToRect(PixelBounds bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
}
