using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace LayerPeek.App.Cameras.Mini;

/// <summary>
/// The summary of the mini preview strip (T-103 3.3, like <see cref="Overlay.OverlayChipsPanel"/>): its visible children are
/// the variants of the summary row, longest first; the panel draws the <b>first one that fits whole</b>, measured in the
/// current font (no fixed width thresholds: the system fonts of Windows and macOS are wider or narrower than the test font).
/// When none fits, the last one is drawn in the available width and its summary text trims itself ("…"); the last variant
/// never has the finish time, so the flag group is either drawn whole or not at all. The other variants are placed beyond
/// the right edge, which the panel clips, and are left out of the automation tree (the summary's name has the whole text).
/// </summary>
/// <remarks>
/// A variant is drawn at its own width from the left; the free room is to its right. Nothing is allocated by a layout pass
/// without a change of the texts or the width (the children cache their measure).
/// </remarks>
public sealed class CameraMiniSummaryPanel : Panel
{
    // Layout rounding may leave the measured row a hair narrower in arrange.
    private const double Tolerance = 0.01;

    private Control? _shown;
    private Control? _arranged;
    private bool _trimmed;

    static CameraMiniSummaryPanel()
    {
        ClipToBoundsProperty.OverrideDefaultValue<CameraMiniSummaryPanel>(true);
    }

    /// <summary>The index of the drawn variant among the visible children after the last measure; -1 without any.</summary>
    public int ShownIndex { get; private set; } = -1;

    /// <summary>The drawn variant after the last measure.</summary>
    public Control? Shown => _shown;

    /// <summary>No variant fitted: the last one is drawn in the available width and may be trimmed.</summary>
    public bool IsTrimmed => _trimmed;

    protected override AutomationPeer OnCreateAutomationPeer() => new SummaryPanelAutomationPeer(this);

    protected override Size MeasureOverride(Size availableSize)
    {
        Control? first = null;
        Control? last = null;
        int lastIndex = -1;
        int index = 0;
        _shown = null;
        ShownIndex = -1;
        foreach (Control child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            child.Measure(Size.Infinity);
            first ??= child;
            if (_shown is null && child.DesiredSize.Width <= availableSize.Width + Tolerance)
            {
                _shown = child;
                ShownIndex = index;
            }

            last = child;
            lastIndex = index++;
        }

        _trimmed = _shown is null && last is not null;
        if (_trimmed)
        {
            // None fits: the shortest variant in the available width (its summary text trims itself).
            _shown = last;
            ShownIndex = lastIndex;
            last!.Measure(availableSize);
        }

        if (_shown is null)
        {
            return default;
        }

        Size size = _shown.DesiredSize;
        return new Size(Math.Min(size.Width, availableSize.Width), size.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (Control child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            Size size = child.DesiredSize;
            double y = (finalSize.Height - size.Height) / 2;
            if (ReferenceEquals(child, _shown))
            {
                double width = _trimmed ? finalSize.Width : size.Width;
                child.Arrange(new Rect(0, y, width, size.Height));
            }
            else
            {
                // Beyond the clipped right edge: not drawn.
                child.Arrange(new Rect(finalSize.Width + 1, y, size.Width, size.Height));
            }
        }

        if (!ReferenceEquals(_arranged, _shown))
        {
            _arranged = _shown;
            (ControlAutomationPeer.FromElement(this) as SummaryPanelAutomationPeer)?.ShownChanged();
        }

        return finalSize;
    }

    /// <summary>The panel in the automation tree: only the drawn variant is its child.</summary>
    private sealed class SummaryPanelAutomationPeer(CameraMiniSummaryPanel owner) : NoneAutomationPeer(owner)
    {
        public void ShownChanged() => InvalidateChildren();

        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() =>
            owner._arranged is { } shown ? [CreatePeerForElement(shown)] : [];
    }
}
