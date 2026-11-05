using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using LayerPeek.Core.Detection;

namespace LayerPeek.App.Settings;

/// <summary>
/// The bed region editor of the Detection sub-tab (T-154): a camera frame (<see cref="Source"/>, uniformly scaled with black
/// bands like the camera preview; black without a frame) with the region (<see cref="Region"/>, normalized to the frame)
/// drawn over it; outside the region the frame is dimmed. Mouse: drag outside the region to draw a new one, inside it to move
/// it, at a corner to resize it; Esc cancels a drag. Keyboard (focusable): arrows move it, Shift + arrows change its size
/// (right / bottom edge), with Ctrl (⌘ on macOS) by 10 % instead of 1 %. The region is written back only when a drag ends or
/// per key press (not on every pointer move), so the window validates and compares its settings rarely. No timer, no
/// animation: it draws only when something changed. The geometry is <see cref="DetectionRegionGeometry"/>.
/// </summary>
internal sealed class DetectionRegionEditor : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<DetectionRegionEditor, IImage?>(nameof(Source));

    public static readonly StyledProperty<DetectionRegion> RegionProperty =
        AvaloniaProperty.Register<DetectionRegionEditor, DetectionRegion>(nameof(Region), DetectionRegion.Full, defaultBindingMode: BindingMode.TwoWay);

    private static readonly IBrush DimBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0, 0, 0));
    private static readonly IPen OuterPen = new ImmutablePen(Brushes.Black, 3);
    private static readonly IPen InnerPen = new ImmutablePen(Brushes.White, 1.5);
    private static readonly IPen FocusedInnerPen = new ImmutablePen(Brushes.White, 2.5);
    private static readonly IPen HandlePen = new ImmutablePen(Brushes.Black, 1);

    /// <summary>
    /// A press outside the region draws a new one only once the pointer moved this far (DIPs): a plain click (for example
    /// to focus the editor for the keyboard) keeps the region instead of shrinking it to the smallest size.
    /// </summary>
    internal const double DrawThreshold = 3;

    private RegionDragKind? _drag;
    private Point _pressPoint;
    private bool _drawStarted;
    private (double X, double Y) _dragStart;
    private DetectionRegion _dragOriginal;
    private DetectionRegion _preview;
    private RegionDragKind? _cursorKind;

    // One cursor per kind of drag, created on first use on the UI thread and kept for the process (a handful of handles).
    private static readonly Cursor?[] Cursors = new Cursor?[(int)RegionDragKind.ResizeBottomRight + 1];

    static DetectionRegionEditor()
    {
        AffectsRender<DetectionRegionEditor>(SourceProperty, RegionProperty);
        FocusableProperty.OverrideDefaultValue<DetectionRegionEditor>(true);
        ClipToBoundsProperty.OverrideDefaultValue<DetectionRegionEditor>(true);
    }

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public DetectionRegion Region
    {
        get => GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    /// <summary>Whether a drag is in progress (tests).</summary>
    internal bool IsDragging => _drag is not null;

    /// <summary>Where the frame is drawn now.</summary>
    internal ViewRect ImageRect => DetectionRegionGeometry.ImageRect(Bounds.Width, Bounds.Height, AspectRatio);

    private double AspectRatio => Source is { Size: { Width: > 0, Height: > 0 } size } ? size.Width / size.Height : DetectionRegionGeometry.DefaultAspectRatio;

    /// <summary>The region drawn: the one being dragged, else <see cref="Region"/>.</summary>
    private DetectionRegion Shown => _drag is not null ? _preview : Region == default ? DetectionRegion.Full : Region;

    public override void Render(DrawingContext context)
    {
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(Brushes.Black, bounds);
        ViewRect image = ImageRect;
        if (image.Width <= 0 || image.Height <= 0)
        {
            return;
        }

        if (Source is { } source)
        {
            context.DrawImage(source, new Rect(source.Size), new Rect(image.X, image.Y, image.Width, image.Height));
        }

        ViewRect region = DetectionRegionGeometry.ToView(Shown, image);

        // Dimmed outside the region, within the frame: above, below, left and right of it.
        FillIfAny(context, image.X, image.Y, image.Width, region.Y - image.Y);
        FillIfAny(context, image.X, region.Bottom, image.Width, image.Bottom - region.Bottom);
        FillIfAny(context, image.X, region.Y, region.X - image.X, region.Height);
        FillIfAny(context, region.Right, region.Y, image.Right - region.Right, region.Height);

        Rect outline = new(region.X, region.Y, region.Width, region.Height);
        context.DrawRectangle(null, OuterPen, outline);
        context.DrawRectangle(null, IsKeyboardFocusWithin ? FocusedInnerPen : InnerPen, outline);
        double half = DetectionRegionGeometry.HandleRadius / 2;
        foreach ((double x, double y) in (ReadOnlySpan<(double, double)>)[(region.X, region.Y), (region.Right, region.Y), (region.X, region.Bottom), (region.Right, region.Bottom)])
        {
            context.DrawRectangle(Brushes.White, HandlePen, new Rect(x - half, y - half, half * 2, half * 2));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus(NavigationMethod.Pointer);
        Point point = e.GetPosition(this);
        ViewRect image = ImageRect;
        _dragOriginal = Shown;
        _drag = DetectionRegionGeometry.HitTest(_dragOriginal, image, point.X, point.Y);
        _dragStart = DetectionRegionGeometry.ToFrame(point.X, point.Y, image);
        _pressPoint = point;
        _drawStarted = false;
        _preview = _dragOriginal;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);
        ViewRect image = ImageRect;
        if (_drag is { } kind)
        {
            if (kind == RegionDragKind.Draw && !_drawStarted)
            {
                Vector moved = point - _pressPoint;
                if (Math.Abs(moved.X) < DrawThreshold && Math.Abs(moved.Y) < DrawThreshold)
                {
                    e.Handled = true;
                    return;
                }

                _drawStarted = true;
            }

            DetectionRegion next = DetectionRegionGeometry.Drag(kind, _dragOriginal, _dragStart, DetectionRegionGeometry.ToFrame(point.X, point.Y, image));
            if (next != _preview)
            {
                _preview = next;
                InvalidateVisual();
            }

            e.Handled = true;
            return;
        }

        UpdateCursor(DetectionRegionGeometry.HitTest(Shown, image, point.X, point.Y));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is null)
        {
            return;
        }

        DetectionRegion committed = _preview;
        _drag = null;
        e.Pointer.Capture(null);
        Commit(committed);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        // Interrupted (another window took the pointer): the region stays as it was.
        CancelDrag();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        if (e.Key == Key.Escape && _drag is not null)
        {
            CancelDrag();
            e.Handled = true;
            return;
        }

        (double dx, double dy) = e.Key switch
        {
            Key.Left => (-1.0, 0.0),
            Key.Right => (1.0, 0.0),
            Key.Up => (0.0, -1.0),
            Key.Down => (0.0, 1.0),
            _ => (0.0, 0.0),
        };
        if ((dx == 0 && dy == 0) || _drag is not null)
        {
            return;
        }

        KeyModifiers command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        double step = (e.KeyModifiers & command) != 0 ? DetectionRegionGeometry.LargeKeyStep : DetectionRegionGeometry.KeyStep;
        DetectionRegion region = Shown;
        Commit((e.KeyModifiers & KeyModifiers.Shift) != 0
            ? DetectionRegionGeometry.Resize(region, dx * step, dy * step)
            : DetectionRegionGeometry.Move(region, dx * step, dy * step));
        // Also at an edge: the arrow never leaves the editor (nor switches the sub-tab).
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    private static void FillIfAny(DrawingContext context, double x, double y, double width, double height)
    {
        if (width > 0.5 && height > 0.5)
        {
            context.FillRectangle(DimBrush, new Rect(x, y, width, height));
        }
    }

    private void Commit(DetectionRegion region)
    {
        if (region != Region)
        {
            SetCurrentValue(RegionProperty, region);
        }
        else
        {
            InvalidateVisual();
        }
    }

    private void CancelDrag()
    {
        if (_drag is null)
        {
            return;
        }

        _drag = null;
        InvalidateVisual();
    }

    private void UpdateCursor(RegionDragKind kind)
    {
        if (_cursorKind == kind)
        {
            return;
        }

        _cursorKind = kind;
        Cursor = Cursors[(int)kind] ??= new Cursor(kind switch
        {
            RegionDragKind.Move => StandardCursorType.SizeAll,
            RegionDragKind.ResizeTopLeft => StandardCursorType.TopLeftCorner,
            RegionDragKind.ResizeTopRight => StandardCursorType.TopRightCorner,
            RegionDragKind.ResizeBottomLeft => StandardCursorType.BottomLeftCorner,
            RegionDragKind.ResizeBottomRight => StandardCursorType.BottomRightCorner,
            _ => StandardCursorType.Cross,
        });
    }
}
