using Avalonia;
using Avalonia.Controls;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>
/// One row of chips that never wraps (T-076 2.4): the chips are laid out from the left with <see cref="Spacing"/>; those
/// that do not fit the width any more are left out from the right (nozzle, then layer height, then material), whole
/// (never cut). Left-out chips are placed beyond the right edge, which the panel clips. Hidden children take no room.
/// </summary>
public sealed class OverlayChipsPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<OverlayChipsPanel, double>(nameof(Spacing), 4);

    static OverlayChipsPanel()
    {
        AffectsMeasure<OverlayChipsPanel>(SpacingProperty);
        ClipToBoundsProperty.OverrideDefaultValue<OverlayChipsPanel>(true);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>How many of the visible chips are drawn after the last arrange (the first ones, from the left).</summary>
    public int ShownCount { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0;
        foreach (Control child in Children)
        {
            child.Measure(Size.Infinity);
        }

        double width = Fit(availableSize.Width, out int count);
        int index = 0;
        foreach (Control child in Children)
        {
            if (child.IsVisible && index++ < count)
            {
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Fit(finalSize.Width, out int count);
        double x = 0;
        int index = 0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            Size size = child.DesiredSize;
            double y = (finalSize.Height - size.Height) / 2;
            if (index++ < count)
            {
                child.Arrange(new Rect(x, y, size.Width, size.Height));
                x += size.Width + Spacing;
            }
            else
            {
                // Beyond the clipped right edge: not drawn.
                child.Arrange(new Rect(finalSize.Width + Spacing, y, size.Width, size.Height));
            }
        }

        ShownCount = count;
        return finalSize;
    }

    /// <summary>The width of the first visible chips that fit in <paramref name="width"/>, and their count.</summary>
    private double Fit(double width, out int count)
    {
        // Layout rounding may leave the measured row a hair narrower in arrange.
        const double Tolerance = 0.01;
        double used = 0;
        count = 0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            double next = (count == 0 ? 0 : used + Spacing) + child.DesiredSize.Width;
            if (next > width + Tolerance)
            {
                break;
            }

            used = next;
            count++;
        }

        return used;
    }
}
