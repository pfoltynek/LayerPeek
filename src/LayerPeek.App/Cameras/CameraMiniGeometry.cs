using Avalonia.Controls;

using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Cameras;

/// <summary>
/// Size and position of the mini preview with its info strip below the image (T-089), as pure functions in screen pixels.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The window is the image (as wide as the window, its height from the aspect ratio of the stream, 16:9 until the
/// first frame, Q5) with the strip below it: height = width × ratio + strip.</item>
/// <item>A window that changes its height grows (or shrinks) away from the work area edge it is nearest to (Q2): nearer the
/// bottom edge, the bottom edge stays and it grows upwards; nearer the top edge (or as near to both), the top edge stays,
/// except that a window exactly in the middle (within a pixel) grows by half to both sides and stays centred. The left
/// edge always stays (only the height follows the strip). The result is always moved (and at most made as big as the
/// work area) so that the whole window is on the work area of its monitor.</item>
/// <item>A remembered or default placement of the mini preview (<see cref="CameraWindowPlacement"/>, Q3) is the
/// <em>image</em>: the rectangle the window grows from when the strip is added (<see cref="FromImage"/>). Remembering is
/// the inverse (<see cref="ToImage"/>), so a window that is closed and opened again comes back at the same place. A
/// placement remembered before the strip existed is a window without a strip, i.e. exactly such an image: it grows by the
/// strip by the same rule and stays on the screen (no conversion of the file needed).</item>
/// <item>The window is at least <see cref="MinWidth"/> wide (Q4).</item>
/// </list>
/// Sizes in device-independent units are converted with the scaling of the monitor the window is on (rounded half away
/// from zero, like the rest of the placement code).
/// </remarks>
internal static class CameraMiniGeometry
{
    /// <summary>Narrowest mini preview (DIP, T-089 Q4).</summary>
    public const double MinWidth = CameraWindowSize.MinMiniWidth;

    /// <summary>Height / width of the image until the stream tells its own (16:9, T-089 Q5).</summary>
    public const double DefaultAspectRatio = 9.0 / 16.0;

    /// <summary>A window this close (pixels) to being in the middle of the work area grows by half to both sides.</summary>
    private const int CentredTolerance = 1;

    /// <summary>Height / width of a frame of <paramref name="width"/> × <paramref name="height"/> pixels; <see langword="null"/> for an empty one.</summary>
    public static double? AspectRatioOf(int width, int height) => width > 0 && height > 0 ? (double)height / width : null;

    /// <summary><paramref name="aspectRatio"/> when it is usable (finite, positive), else <see cref="DefaultAspectRatio"/>.</summary>
    public static double UsableAspectRatio(double? aspectRatio) =>
        aspectRatio is { } ratio && double.IsFinite(ratio) && ratio > 0 ? ratio : DefaultAspectRatio;

    /// <summary>Height (DIP) of the image of a <paramref name="width"/> wide window with <paramref name="aspectRatio"/> (16:9 when unknown).</summary>
    public static double ImageHeight(double width, double? aspectRatio) => width * UsableAspectRatio(aspectRatio);

    /// <summary>
    /// The mini preview <paramref name="window"/> (pixels) with its height computed from its <paramref name="width"/>
    /// again: after the user resized it, after the first frame or another aspect ratio of the stream, after the strip
    /// changed its height. The width stays unless it is below <see cref="MinWidth"/> or the window would not fit the work
    /// area (then it becomes as narrow as needed, but not below <see cref="MinWidth"/>).
    /// </summary>
    /// <param name="window">The window now, in pixels.</param>
    /// <param name="width">Its client width in DIP (exactly as the window has it).</param>
    /// <param name="scaling">The scaling of the monitor the window is on.</param>
    /// <param name="workArea">The work area of that monitor; <see langword="null"/> when unknown (nothing is moved then, the top edge stays).</param>
    /// <param name="aspectRatio">Height / width of the stream; <see langword="null"/> = 16:9.</param>
    /// <param name="stripHeight">Height of the info strip (DIP).</param>
    /// <returns>The window: position in pixels, size in DIP (exact unless it had to be made as big as the work area).</returns>
    public static CameraWindowPlacement Correct(PixelBounds window, double width, double scaling, PixelBounds? workArea, double? aspectRatio, double stripHeight)
    {
        scaling = UsableScaling(scaling);
        return Fit(window, width, UsableAspectRatio(aspectRatio), scaling, workArea, stripHeight);
    }

    /// <summary>
    /// The mini preview window for a remembered or default placement <paramref name="image"/> (position in pixels, size in
    /// DIP): the image with the strip added by the growth rule (see remarks), on the work area. The image keeps its own
    /// height (the remembered aspect ratio) unless <paramref name="aspectRatio"/> of the running stream is known.
    /// </summary>
    /// <returns>The window: position in pixels, size in DIP.</returns>
    public static CameraWindowPlacement FromImage(CameraWindowPlacement image, double scaling, PixelBounds? workArea, double? aspectRatio, double stripHeight)
    {
        scaling = UsableScaling(scaling);
        double width = Math.Max(image.Width, 1);
        double ratio = aspectRatio is not null ? UsableAspectRatio(aspectRatio) : UsableAspectRatio(image.Height / width);
        PixelBounds from = new(image.X, image.Y, ToPixels(width, scaling), ToPixels(Math.Max(image.Height, 1), scaling));
        return Fit(from, width, ratio, scaling, workArea, stripHeight);
    }

    /// <summary>
    /// The placement to remember for the mini preview <paramref name="window"/> (pixels): the image rectangle that
    /// <see cref="FromImage"/> grows into this window again (the inverse of the growth rule), its size in DIP.
    /// </summary>
    public static CameraWindowPlacement ToImage(PixelBounds window, double scaling, PixelBounds? workArea, double stripHeight)
    {
        scaling = UsableScaling(scaling);
        int strip = stripHeight > 0 ? Math.Clamp(ToPixels(stripHeight, scaling), 0, Math.Max(window.Height - 1, 0)) : 0;
        int y = window.Y;
        if (workArea is { IsEmpty: false } area)
        {
            // The image must grow back by the same rule: its own nearest edge decides, not the window's. Near the middle
            // the two can differ (the strip shifts the image off-centre), so the window's direction is tried first and
            // then the others; one of "top stays" and "bottom stays" always fits.
            Growth own = Direction(window, area);
            y = ImageTop(window, strip, own);
            if (Direction(window with { Y = y, Height = window.Height - strip }, area) != own)
            {
                y = ImageTop(window, strip, Growth.Down);
                if (Direction(window with { Y = y, Height = window.Height - strip }, area) != Growth.Down)
                {
                    y = ImageTop(window, strip, Growth.Up);
                }
            }
        }

        return new CameraWindowPlacement(window.X, y, window.Width / scaling, (window.Height - strip) / scaling);

        static int ImageTop(PixelBounds window, int strip, Growth growth) => growth switch
        {
            Growth.Up => window.Y + strip,
            Growth.Both => window.Y + (strip / 2),
            _ => window.Y,
        };
    }

    /// <summary>
    /// The mini preview while the user drags its grip at <paramref name="edge"/> by <paramref name="dx"/> ×
    /// <paramref name="dy"/> screen pixels from where the window was when the grip was pressed (<paramref name="start"/>:
    /// position in pixels, client size in DIP). For platforms where the app resizes the window itself (macOS, T-038): the
    /// dragged edges follow the pointer and the opposite edges stay; a dragged edge stops at <paramref name="minWidth"/> ×
    /// <paramref name="minHeight"/>. The height follows the width only after the grip is released (<see cref="Correct"/>).
    /// </summary>
    /// <returns>The window: position in pixels, size in DIP.</returns>
    public static CameraWindowPlacement Resized(CameraWindowPlacement start, WindowEdge edge, int dx, int dy, double scaling, double minWidth, double minHeight)
    {
        scaling = UsableScaling(scaling);
        (bool west, bool east, bool north, bool south) = edge switch
        {
            WindowEdge.NorthWest => (true, false, true, false),
            WindowEdge.North => (false, false, true, false),
            WindowEdge.NorthEast => (false, true, true, false),
            WindowEdge.West => (true, false, false, false),
            WindowEdge.East => (false, true, false, false),
            WindowEdge.SouthWest => (true, false, false, true),
            WindowEdge.South => (false, false, false, true),
            WindowEdge.SouthEast => (false, true, false, true),
            _ => (false, false, false, false),
        };

        double width = start.Width;
        double height = start.Height;
        int x = start.X;
        int y = start.Y;
        if (east || west)
        {
            width = Math.Max(start.Width + ((east ? dx : -dx) / scaling), Math.Max(minWidth, 1));
            // The right edge stays: the left one moves by what the window gained or lost.
            x = west ? start.X - (ToPixels(width, scaling) - ToPixels(start.Width, scaling)) : start.X;
        }

        if (north || south)
        {
            height = Math.Max(start.Height + ((south ? dy : -dy) / scaling), Math.Max(minHeight, 1));
            y = north ? start.Y - (ToPixels(height, scaling) - ToPixels(start.Height, scaling)) : start.Y;
        }

        return new CameraWindowPlacement(x, y, width, height);
    }

    /// <summary>
    /// <paramref name="from"/> resized to <paramref name="width"/> × <paramref name="height"/> pixels: the left edge stays,
    /// vertically it grows away from the nearest edge of <paramref name="workArea"/> (see remarks); then it is moved (and at
    /// most made as big as the work area) so that it is entirely on the work area. Without a work area the top left corner stays.
    /// </summary>
    public static PixelBounds Grow(PixelBounds from, int width, int height, PixelBounds? workArea)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        if (workArea is not { IsEmpty: false } area)
        {
            return new PixelBounds(from.X, from.Y, width, height);
        }

        int grown = height - from.Height;
        int y = Direction(from, area) switch
        {
            Growth.Up => from.Bottom - height,
            Growth.Both => from.Y - (grown / 2),
            _ => from.Y,
        };

        width = Math.Min(width, area.Width);
        height = Math.Min(height, area.Height);
        int x = Math.Clamp(from.X, area.X, area.Right - width);
        y = Math.Clamp(y, area.Y, area.Bottom - height);
        return new PixelBounds(x, y, width, height);
    }

    /// <summary>The window for <paramref name="width"/> (DIP) and <paramref name="ratio"/>, grown from <paramref name="from"/>.</summary>
    private static CameraWindowPlacement Fit(PixelBounds from, double width, double ratio, double scaling, PixelBounds? workArea, double stripHeight)
    {
        double strip = double.IsFinite(stripHeight) && stripHeight > 0 ? stripHeight : 0;
        width = Math.Max(double.IsFinite(width) ? width : MinWidth, MinWidth);
        if (workArea is { IsEmpty: false } area)
        {
            double areaWidth = area.Width / scaling;
            double areaHeight = area.Height / scaling;
            width = Math.Min(width, Math.Max(areaWidth, 1));
            if ((width * ratio) + strip > areaHeight)
            {
                // Too high for the monitor: as narrow as the image needs to fit above the strip, not below the minimum.
                width = Math.Min(width, Math.Max((areaHeight - strip) / ratio, Math.Min(MinWidth, areaWidth)));
            }
        }

        double height = (width * ratio) + strip;
        int widthPixels = ToPixels(width, scaling);
        int heightPixels = ToPixels(height, scaling);
        PixelBounds grown = Grow(from, widthPixels, heightPixels, workArea);
        // The exact DIP size, unless the work area cut it.
        return new CameraWindowPlacement(
            grown.X,
            grown.Y,
            grown.Width == widthPixels ? width : grown.Width / scaling,
            grown.Height == heightPixels ? height : grown.Height / scaling);
    }

    private static Growth Direction(PixelBounds window, PixelBounds area)
    {
        long above = (long)window.Y - area.Y;
        long below = (long)area.Bottom - window.Bottom;
        if (Math.Abs(above - below) <= CentredTolerance)
        {
            return Growth.Both;
        }

        return below < above ? Growth.Up : Growth.Down;
    }

    private static double UsableScaling(double scaling) => double.IsFinite(scaling) && scaling > 0 ? scaling : 1.0;

    private static int ToPixels(double dip, double scaling) =>
        (int)Math.Clamp(Math.Round(dip * scaling, MidpointRounding.AwayFromZero), 1, int.MaxValue / 4);

    /// <summary>Which edge stays when the height changes.</summary>
    private enum Growth
    {
        /// <summary>The top edge stays.</summary>
        Down,

        /// <summary>The bottom edge stays.</summary>
        Up,

        /// <summary>Centred: half to both sides.</summary>
        Both,
    }
}
