using Avalonia;
using Avalonia.Controls;

using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Cameras.Layout;

/// <summary>
/// The six zones of the layout mode (T-076 5.2): each child (a zone, its data context a
/// <see cref="CameraLayoutZoneViewModel"/>) at its anchor, 12 DIP from the edges, in the size of
/// <see cref="CameraLayoutGeometry.ZoneSize"/> or bigger when its cards need more.
/// </summary>
public sealed class CameraLayoutZonesPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        Size zone = ZoneSize(availableSize);
        foreach (Control child in Children)
        {
            child.Measure(Size.Infinity);
        }

        return new Size(
            double.IsFinite(availableSize.Width) ? availableSize.Width : (3 * zone.Width) + (4 * CameraLayoutGeometry.EdgeMargin),
            double.IsFinite(availableSize.Height) ? availableSize.Height : (2 * zone.Height) + (3 * CameraLayoutGeometry.EdgeMargin));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size zone = ZoneSize(finalSize);
        foreach (Control child in Children)
        {
            if (child.DataContext is not CameraLayoutZoneViewModel { Anchor: var anchor })
            {
                child.Arrange(new Rect(finalSize));
                continue;
            }

            double width = Math.Max(zone.Width, child.DesiredSize.Width);
            double height = Math.Max(zone.Height, child.DesiredSize.Height);
            double x = OverlayAnchors.IsLeft(anchor) ? CameraLayoutGeometry.EdgeMargin
                : OverlayAnchors.IsRight(anchor) ? finalSize.Width - CameraLayoutGeometry.EdgeMargin - width
                : (finalSize.Width - width) / 2;
            double y = OverlayAnchors.IsTop(anchor) ? CameraLayoutGeometry.EdgeMargin : finalSize.Height - CameraLayoutGeometry.EdgeMargin - height;
            child.Arrange(new Rect(x, y, width, height));
        }

        return finalSize;
    }

    private static Size ZoneSize(Size size) => CameraLayoutGeometry.ZoneSize(
        double.IsFinite(size.Width) ? size.Width : 960,
        double.IsFinite(size.Height) ? size.Height : 540);
}

/// <summary>
/// The content of the layout mode toolbar (T-076 5.2): the title with the hint (first child) and the buttons (second
/// child) in one row when they fit side by side, otherwise the buttons in a second row aligned to the right.
/// </summary>
public sealed class CameraLayoutToolbarPanel : Panel
{
    /// <summary>Gap between the text and the buttons in one row, and between the two rows.</summary>
    public const double Gap = 16;

    /// <summary>The buttons are in a second row (for tests and the snapshots).</summary>
    public bool IsWrapped { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count < 2)
        {
            return MeasureSingle(availableSize);
        }

        Control text = Children[0];
        Control buttons = Children[1];
        buttons.Measure(availableSize);
        text.Measure(Size.Infinity);
        double oneRow = text.DesiredSize.Width + Gap + buttons.DesiredSize.Width;
        IsWrapped = oneRow > availableSize.Width;
        if (!IsWrapped)
        {
            return new Size(oneRow, Math.Max(text.DesiredSize.Height, buttons.DesiredSize.Height));
        }

        text.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(
            Math.Min(availableSize.Width, Math.Max(text.DesiredSize.Width, buttons.DesiredSize.Width)),
            text.DesiredSize.Height + Gap + buttons.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count < 2)
        {
            foreach (Control child in Children)
            {
                child.Arrange(new Rect(finalSize));
            }

            return finalSize;
        }

        Control text = Children[0];
        Control buttons = Children[1];
        Size buttonsSize = buttons.DesiredSize;
        if (!IsWrapped)
        {
            text.Arrange(new Rect(0, (finalSize.Height - text.DesiredSize.Height) / 2, Math.Max(0, finalSize.Width - Gap - buttonsSize.Width), text.DesiredSize.Height));
            buttons.Arrange(new Rect(finalSize.Width - buttonsSize.Width, (finalSize.Height - buttonsSize.Height) / 2, buttonsSize.Width, buttonsSize.Height));
            return finalSize;
        }

        text.Arrange(new Rect(0, 0, finalSize.Width, text.DesiredSize.Height));
        buttons.Arrange(new Rect(Math.Max(0, finalSize.Width - buttonsSize.Width), text.DesiredSize.Height + Gap, buttonsSize.Width, buttonsSize.Height));
        return finalSize;
    }

    private Size MeasureSingle(Size availableSize)
    {
        Size size = default;
        foreach (Control child in Children)
        {
            child.Measure(availableSize);
            size = new Size(Math.Max(size.Width, child.DesiredSize.Width), Math.Max(size.Height, child.DesiredSize.Height));
        }

        return size;
    }
}
