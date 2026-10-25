using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Cameras.Layout;

/// <summary>
/// How much room the layout mode needs (T-076 Q4) and the temporary enlargement of a smaller normal window when the layout
/// mode is started from outside the camera window (T-091 Q5 and Q1, user decisions 2026-10-25). Pure functions.
/// </summary>
internal static class CameraLayoutSpace
{
    /// <summary>Below this client width (DIP) the layout mode is disabled in the window (button and menu item).</summary>
    public const double MinWidth = 480;

    /// <summary>Below this client height (DIP) the layout mode is disabled in the window.</summary>
    public const double MinHeight = 300;

    /// <summary>The client width (DIP) a smaller window gets for the layout mode started from outside.</summary>
    public const double TemporaryWidth = 640;

    /// <summary>The client height (DIP) a smaller window gets for the layout mode started from outside.</summary>
    public const double TemporaryHeight = 360;

    /// <summary>Whether a client area of <paramref name="width"/> × <paramref name="height"/> DIP is big enough for the layout mode.</summary>
    public static bool Fits(double width, double height) => width >= MinWidth && height >= MinHeight;

    /// <summary>
    /// Where a normal window at <paramref name="current"/> goes for the layout mode: <see langword="null"/> when it is big
    /// enough already; otherwise at least <see cref="TemporaryWidth"/> × <see cref="TemporaryHeight"/> (a dimension that is
    /// already larger stays, the window never shrinks) at the same position, moved just enough that the visible window stays
    /// inside the work area of its monitor (Q1). Without monitors the position stays.
    /// </summary>
    /// <param name="current">The window now: position in screen pixels, client size in DIP.</param>
    /// <param name="screens">The monitors.</param>
    /// <param name="chrome">The frame of the window (DIP).</param>
    public static CameraWindowPlacement? Enlarged(CameraWindowPlacement current, IReadOnlyList<CameraScreen> screens, WindowChrome chrome)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (Fits(current.Width, current.Height))
        {
            return null;
        }

        double width = Math.Max(current.Width, TemporaryWidth);
        double height = Math.Max(current.Height, TemporaryHeight);
        CameraScreen? screen = screens.Count == 0
            ? null
            : CameraWindowPlanner.ScreenOf(new PixelBounds(current.X, current.Y, Math.Max(ToPixels(current.Width, 1), 1), Math.Max(ToPixels(current.Height, 1), 1)), screens);
        if (screen is null)
        {
            return current with { Width = width, Height = height };
        }

        double scaling = screen.Scaling;
        PixelBounds area = screen.UsableArea;
        int left = current.X + ToPixels(chrome.InvisibleBorders.Left, scaling);
        int top = current.Y + ToPixels(chrome.InvisibleBorders.Top, scaling);
        int right = current.X + ToPixels(width + chrome.Frame.Width - chrome.InvisibleBorders.Right, scaling);
        int bottom = current.Y + ToPixels(height + chrome.Frame.Height - chrome.InvisibleBorders.Bottom, scaling);
        return current with
        {
            X = current.X + Shift(left, right, area.X, area.Right),
            Y = current.Y + Shift(top, bottom, area.Y, area.Bottom),
            Width = width,
            Height = height,
        };
    }

    /// <summary>
    /// How far a span <paramref name="start"/>–<paramref name="end"/> moves to lie inside <paramref name="min"/>–<paramref name="max"/>;
    /// a span longer than the area keeps its start at the area's start.
    /// </summary>
    private static int Shift(int start, int end, int min, int max)
    {
        int shift = end > max ? max - end : 0;
        return start + shift < min ? min - start : shift;
    }

    private static int ToPixels(double dip, double scaling) => (int)Math.Round(dip * scaling, MidpointRounding.AwayFromZero);
}
