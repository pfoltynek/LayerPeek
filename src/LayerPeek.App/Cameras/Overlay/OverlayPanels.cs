using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;

using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>
/// The cards of one anchor (T-081): an <see cref="ItemsControl"/> of <see cref="OverlayWidgetViewModel"/>s in stacking
/// order (from the window edge inward) laid out by an <see cref="OverlayStackPanel"/>. Placed in an
/// <see cref="OverlayAnchorsPanel"/>.
/// </summary>
public sealed class OverlayAnchorItems : ItemsControl
{
    public static readonly StyledProperty<OverlayAnchor> AnchorProperty =
        AvaloniaProperty.Register<OverlayAnchorItems, OverlayAnchor>(nameof(Anchor));

    private static readonly FuncTemplate<Panel?> StackPanelTemplate = new(static () => new OverlayStackPanel());

    static OverlayAnchorItems()
    {
        ItemsPanelProperty.OverrideDefaultValue<OverlayAnchorItems>(StackPanelTemplate);
    }

    /// <summary>
    /// Class <c>overlayAnchor</c>: the shared styles give it the card template (type selectors match the style key, which is
    /// <see cref="ItemsControl"/> here).
    /// </summary>
    public OverlayAnchorItems() => Classes.Add("overlayAnchor");

    public OverlayAnchor Anchor
    {
        get => GetValue(AnchorProperty);
        set => SetValue(AnchorProperty, value);
    }

    // The theme of a plain ItemsControl.
    protected override Type StyleKeyOverride => typeof(ItemsControl);
}

/// <summary>
/// Stacks the cards of one anchor (T-076 3.1): from the window edge inward (top anchors downward, bottom anchors upward)
/// with <see cref="OverlayArrangement.CardSpacing"/> between the drawn cards, aligned to the anchor's side. Cards without
/// content take no room; cards left out by the small-window rules (<see cref="OverlayWidgetViewModel.IsCollapsed"/>) are
/// measured but placed outside the window.
/// </summary>
public sealed class OverlayStackPanel : Panel
{
    /// <summary>Where left-out cards go: far outside the (clipped) overlay, like recycled items of a virtualizing panel.</summary>
    private static readonly Point Outside = new(-100_000, -100_000);

    private OverlayAnchor Anchor => this.FindAncestorOfType<OverlayAnchorItems>()?.Anchor ?? OverlayAnchor.TopLeft;

    internal static bool IsDrawn(Control child) =>
        child.IsVisible && child.DataContext is OverlayWidgetViewModel { IsShown: true, IsCollapsed: false };

    protected override Size MeasureOverride(Size availableSize)
    {
        Size constraint = new(availableSize.Width, double.PositiveInfinity);
        double width = 0;
        double height = 0;
        int count = 0;
        foreach (Control child in Children)
        {
            child.Measure(constraint);
            if (child.IsVisible && child.DataContext is OverlayWidgetViewModel { IsShown: true })
            {
                width = Math.Max(width, child.DesiredSize.Width);
                height += child.DesiredSize.Height;
                count++;
            }
        }

        return new Size(width, count == 0 ? 0 : height + ((count - 1) * OverlayArrangement.CardSpacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        OverlayAnchor anchor = Anchor;
        bool fromBottom = !OverlayAnchors.IsTop(anchor);
        double cursor = fromBottom ? finalSize.Height : 0;
        foreach (Control child in Children)
        {
            Size size = child.DesiredSize;
            if (!IsDrawn(child))
            {
                child.Arrange(new Rect(Outside, size));
                continue;
            }

            double width = Math.Min(size.Width, finalSize.Width);
            double x = OverlayAnchors.IsLeft(anchor) ? 0
                : OverlayAnchors.IsRight(anchor) ? finalSize.Width - width
                : (finalSize.Width - width) / 2;
            double y = fromBottom ? cursor - size.Height : cursor;
            child.Arrange(new Rect(x, y, width, size.Height));
            cursor = fromBottom ? y - OverlayArrangement.CardSpacing : y + size.Height + OverlayArrangement.CardSpacing;
        }

        return finalSize;
    }
}

/// <summary>
/// The six anchors over the camera image (T-076 1.4, 3.1, 3.2): places each <see cref="OverlayAnchorItems"/> child 12 DIP
/// from the edges (the top right one below the window buttons) and applies the small-window rules of
/// <see cref="OverlayArrangement"/> while measuring: the stage of the window size, the width of the centre cards next to
/// the corners and, per column, which cards are left out when the top and the bottom stack would overlap.
/// </summary>
public sealed class OverlayAnchorsPanel : Panel
{
    private static readonly Controls NoContainers = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        double height = double.IsFinite(availableSize.Height) ? availableSize.Height : 0;
        int stage = OverlayArrangement.Stage(width, height);
        OverlayAnchorItems?[] anchors = new OverlayAnchorItems?[OverlayAnchors.All.Count];
        foreach (Control child in Children)
        {
            if (child is OverlayAnchorItems items)
            {
                anchors[(int)items.Anchor] = items;
            }
        }

        // The stage decides the size of the corner cards, then the corners are measured. Every size is set once per pass
        // (a changed size restyles the card).
        Size corner = new(Math.Max(0, width - (2 * OverlayArrangement.EdgeMargin)), double.PositiveInfinity);
        foreach (OverlayAnchorItems? items in anchors)
        {
            if (items is null || OverlayAnchors.IsCenter(items.Anchor))
            {
                continue;
            }

            foreach (OverlayWidgetViewModel widget in Widgets(items))
            {
                OverlayWidgetSize? size = OverlayArrangement.SizeInStage(widget.Size, items.Anchor, stage);
                widget.IsCollapsed = size is null;
                widget.RenderedSize = Supported(widget, size ?? widget.Size);
            }

            items.Measure(corner);
        }

        // Rule 1: the centre cards get the width left between the corners of their row.
        MeasureCenter(anchors[(int)OverlayAnchor.TopCenter], width, stage, CardsWidth(anchors[(int)OverlayAnchor.TopLeft]), CardsWidth(anchors[(int)OverlayAnchor.TopRight]), top: true);
        MeasureCenter(anchors[(int)OverlayAnchor.BottomCenter], width, stage, CardsWidth(anchors[(int)OverlayAnchor.BottomLeft]), CardsWidth(anchors[(int)OverlayAnchor.BottomRight]), top: false);

        // Rules 2 and 3: per column, the top and the bottom stack must not overlap.
        LeaveOutOverlapping(height, anchors[(int)OverlayAnchor.TopLeft], anchors[(int)OverlayAnchor.BottomLeft]);
        LeaveOutOverlapping(height, anchors[(int)OverlayAnchor.TopCenter], anchors[(int)OverlayAnchor.BottomCenter]);
        LeaveOutOverlapping(height, anchors[(int)OverlayAnchor.TopRight], anchors[(int)OverlayAnchor.BottomRight]);

        // Left-out cards changed after the stacks were measured: they place their cards again.
        foreach (OverlayAnchorItems? items in anchors)
        {
            items?.ItemsPanelRoot?.InvalidateArrange();
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (Control child in Children)
        {
            if (child is not OverlayAnchorItems items)
            {
                child.Arrange(new Rect(finalSize));
                continue;
            }

            OverlayAnchor anchor = items.Anchor;
            Size size = items.DesiredSize;
            double x = OverlayAnchors.IsLeft(anchor) ? OverlayArrangement.EdgeMargin
                : OverlayAnchors.IsRight(anchor) ? finalSize.Width - OverlayArrangement.EdgeMargin - size.Width
                : (finalSize.Width - size.Width) / 2;
            double y = OverlayAnchors.IsTop(anchor)
                ? OverlayArrangement.TopStart(anchor)
                : finalSize.Height - OverlayArrangement.EdgeMargin - size.Height;
            items.Arrange(new Rect(x, y, size.Width, size.Height));
        }

        return finalSize;
    }

    private static IEnumerable<OverlayWidgetViewModel> Widgets(ItemsControl items) => items.Items.OfType<OverlayWidgetViewModel>();

    // Never larger than the stage asks for (a widget without the smaller size uses the next smaller one it has).
    private static OverlayWidgetSize Supported(OverlayWidgetViewModel widget, OverlayWidgetSize size) =>
        OverlayArrangement.NearestSupported(size, widget.Definition.SupportedSizes);

    /// <summary>Width of the widest drawn card of an anchor (0 without cards).</summary>
    private static double CardsWidth(OverlayAnchorItems? items)
    {
        double width = 0;
        foreach (Control child in Containers(items))
        {
            if (OverlayStackPanel.IsDrawn(child))
            {
                width = Math.Max(width, child.DesiredSize.Width);
            }
        }

        return width;
    }

    private static Controls Containers(OverlayAnchorItems? items) =>
        items?.ItemsPanelRoot is { } panel ? panel.Children : NoContainers;

    private static void MeasureCenter(OverlayAnchorItems? items, double width, int stage, double leftWidth, double rightWidth, bool top)
    {
        if (items is null)
        {
            return;
        }

        double maxWidth = OverlayArrangement.CenterMaxWidth(width, leftWidth, rightWidth, top);
        foreach (OverlayWidgetViewModel widget in Widgets(items))
        {
            OverlayWidgetSize? size = OverlayArrangement.SizeInStage(widget.Size, items.Anchor, stage) is { } staged
                ? OverlayArrangement.FitCenter(staged, maxWidth, widget.Definition.MinWidth)
                : null;
            widget.IsCollapsed = size is null;
            widget.RenderedSize = Supported(widget, size ?? widget.Size);
        }

        items.Measure(new Size(Math.Max(0, maxWidth), double.PositiveInfinity));
    }

    private static void LeaveOutOverlapping(double height, OverlayAnchorItems? top, OverlayAnchorItems? bottom)
    {
        List<Control> topCards = [.. Containers(top).Where(OverlayStackPanel.IsDrawn)];
        List<Control> bottomCards = [.. Containers(bottom).Where(OverlayStackPanel.IsDrawn)];
        if (topCards.Count + bottomCards.Count == 0)
        {
            return;
        }

        OverlayStackCard[] topMeasured = [.. topCards.Select(Measured)];
        OverlayStackCard[] bottomMeasured = [.. bottomCards.Select(Measured)];
        bool[] hiddenTop = new bool[topMeasured.Length];
        bool[] hiddenBottom = new bool[bottomMeasured.Length];
        double topStart = OverlayArrangement.TopStart(top?.Anchor ?? OverlayAnchor.TopLeft);
        if (!OverlayArrangement.Overflowing(height, topStart, topMeasured, bottomMeasured, hiddenTop, hiddenBottom))
        {
            return;
        }

        for (int i = 0; i < topCards.Count; i++)
        {
            ((OverlayWidgetViewModel)topCards[i].DataContext!).IsCollapsed |= hiddenTop[i];
        }

        for (int i = 0; i < bottomCards.Count; i++)
        {
            ((OverlayWidgetViewModel)bottomCards[i].DataContext!).IsCollapsed |= hiddenBottom[i];
        }

        static OverlayStackCard Measured(Control card) =>
            new(card.DesiredSize.Height, ((OverlayWidgetViewModel)card.DataContext!).Priority);
    }
}
