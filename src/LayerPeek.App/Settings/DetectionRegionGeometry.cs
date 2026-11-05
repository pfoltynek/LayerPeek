using LayerPeek.Core.Detection;

namespace LayerPeek.App.Settings;

/// <summary>A rectangle in DIPs of the bed region editor (no Avalonia types, so the geometry is testable on its own).</summary>
internal readonly record struct ViewRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}

/// <summary>What a press in the bed region editor starts.</summary>
internal enum RegionDragKind
{
    /// <summary>A new rectangle from the pressed point (outside the region).</summary>
    Draw,

    /// <summary>The whole region (inside it).</summary>
    Move,

    /// <summary>The top left corner (the bottom right one stays).</summary>
    ResizeTopLeft,

    ResizeTopRight,

    ResizeBottomLeft,

    ResizeBottomRight,
}

/// <summary>
/// The geometry of the bed region editor (T-154): the image is drawn like the camera preview, uniformly scaled and centred
/// in the editor (black bands where the aspect ratios differ, "letterbox"), and the region (<see cref="DetectionRegion"/>,
/// normalized to the frame, T-153) is drawn over the image, never over the bands. Pointer positions are converted to the
/// frame with the bands taken off and clamped into it, so a drag that leaves the image keeps the region inside the frame.
/// Every operation returns a valid region (inside the frame, at least <see cref="DetectionRegion.MinSize"/>).
/// </summary>
internal static class DetectionRegionGeometry
{
    /// <summary>Aspect ratio of the editor without a frame (and of most printer cameras): 16:9.</summary>
    public const double DefaultAspectRatio = 16.0 / 9.0;

    /// <summary>A press this close to a corner of the region (DIPs) grabs the corner.</summary>
    public const double HandleRadius = 8;

    /// <summary>One arrow key press: 1 % of the frame; with Ctrl (⌘ on macOS) 10 %.</summary>
    public const double KeyStep = 0.01;

    public const double LargeKeyStep = 0.1;

    /// <summary>Where an image of <paramref name="aspectRatio"/> (width / height) is drawn in a <paramref name="width"/> × <paramref name="height"/> editor.</summary>
    public static ViewRect ImageRect(double width, double height, double aspectRatio)
    {
        if (!(width > 0) || !(height > 0))
        {
            return default;
        }

        double aspect = aspectRatio > 0 && double.IsFinite(aspectRatio) ? aspectRatio : DefaultAspectRatio;
        double drawnWidth = Math.Min(width, height * aspect);
        double drawnHeight = drawnWidth / aspect;
        return new ViewRect((width - drawnWidth) / 2, (height - drawnHeight) / 2, drawnWidth, drawnHeight);
    }

    /// <summary>The region in editor DIPs over <paramref name="image"/>.</summary>
    public static ViewRect ToView(DetectionRegion region, ViewRect image) =>
        region == default
            ? image
            : new ViewRect(image.X + (region.X * image.Width), image.Y + (region.Y * image.Height), region.Width * image.Width, region.Height * image.Height);

    /// <summary>A point of the editor in frame coordinates (0–1), clamped into the frame.</summary>
    public static (double X, double Y) ToFrame(double x, double y, ViewRect image) =>
        image.Width <= 0 || image.Height <= 0
            ? (0, 0)
            : (Math.Clamp((x - image.X) / image.Width, 0, 1), Math.Clamp((y - image.Y) / image.Height, 0, 1));

    /// <summary>What a press at (<paramref name="x"/>, <paramref name="y"/>) grabs: a corner, the region, or nothing (draw).</summary>
    public static RegionDragKind HitTest(DetectionRegion region, ViewRect image, double x, double y)
    {
        ViewRect view = ToView(region, image);
        RegionDragKind corner = RegionDragKind.Draw;
        double best = HandleRadius * HandleRadius;
        Consider(view.X, view.Y, RegionDragKind.ResizeTopLeft);
        Consider(view.Right, view.Y, RegionDragKind.ResizeTopRight);
        Consider(view.X, view.Bottom, RegionDragKind.ResizeBottomLeft);
        Consider(view.Right, view.Bottom, RegionDragKind.ResizeBottomRight);
        if (corner != RegionDragKind.Draw)
        {
            return corner;
        }

        return view.Contains(x, y) ? RegionDragKind.Move : RegionDragKind.Draw;

        void Consider(double cornerX, double cornerY, RegionDragKind kind)
        {
            double distance = ((x - cornerX) * (x - cornerX)) + ((y - cornerY) * (y - cornerY));
            if (distance <= best)
            {
                best = distance;
                corner = kind;
            }
        }
    }

    /// <summary>
    /// The region while dragging from <paramref name="start"/> (frame coordinates of the press) to <paramref name="current"/>,
    /// starting with <paramref name="original"/>.
    /// </summary>
    public static DetectionRegion Drag(RegionDragKind kind, DetectionRegion original, (double X, double Y) start, (double X, double Y) current)
    {
        DetectionRegion region = Normalize(original);
        switch (kind)
        {
            case RegionDragKind.Move:
                return Move(region, current.X - start.X, current.Y - start.Y);
            case RegionDragKind.ResizeTopLeft:
                return Between((region.X + region.Width, region.Y + region.Height), current);
            case RegionDragKind.ResizeTopRight:
                return Between((region.X, region.Y + region.Height), current);
            case RegionDragKind.ResizeBottomLeft:
                return Between((region.X + region.Width, region.Y), current);
            case RegionDragKind.ResizeBottomRight:
                return Between((region.X, region.Y), current);
            default:
                return Between(start, current);
        }
    }

    /// <summary>The region moved by (<paramref name="dx"/>, <paramref name="dy"/>), stopped at the edges of the frame (its size kept).</summary>
    public static DetectionRegion Move(DetectionRegion region, double dx, double dy)
    {
        region = Normalize(region);
        double x = Math.Clamp(region.X + Finite(dx), 0, 1 - region.Width);
        double y = Math.Clamp(region.Y + Finite(dy), 0, 1 - region.Height);
        return new DetectionRegion(x, y, region.Width, region.Height);
    }

    /// <summary>
    /// The region with its right and bottom edges moved by (<paramref name="dWidth"/>, <paramref name="dHeight"/>) (Shift +
    /// arrows): the top left corner stays, the size stays within the frame and at least <see cref="DetectionRegion.MinSize"/>.
    /// </summary>
    public static DetectionRegion Resize(DetectionRegion region, double dWidth, double dHeight)
    {
        region = Normalize(region);
        double width = Math.Clamp(region.Width + Finite(dWidth), DetectionRegion.MinSize, 1 - region.X);
        double height = Math.Clamp(region.Height + Finite(dHeight), DetectionRegion.MinSize, 1 - region.Y);
        return new DetectionRegion(region.X, region.Y, width, height);
    }

    /// <summary>The rectangle spanned by two frame points (in any order), at least <see cref="DetectionRegion.MinSize"/> and inside the frame.</summary>
    public static DetectionRegion Between((double X, double Y) a, (double X, double Y) b)
    {
        (double left, double width) = Span(a.X, b.X);
        (double top, double height) = Span(a.Y, b.Y);
        return new DetectionRegion(left, top, width, height);
    }

    /// <summary><c>default</c> (never set) is the whole frame.</summary>
    private static DetectionRegion Normalize(DetectionRegion region) => region == default ? DetectionRegion.Full : region;

    /// <summary>One axis of <see cref="Between"/>: grows away from the edge it would cross when it is too small.</summary>
    private static (double Start, double Size) Span(double a, double b)
    {
        double low = Math.Clamp(Math.Min(Finite(a), Finite(b)), 0, 1);
        double high = Math.Clamp(Math.Max(Finite(a), Finite(b)), 0, 1);
        if (high - low >= DetectionRegion.MinSize)
        {
            return (low, high - low);
        }

        double start = Math.Min(low, 1 - DetectionRegion.MinSize);
        return (start, DetectionRegion.MinSize);
    }

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
}
