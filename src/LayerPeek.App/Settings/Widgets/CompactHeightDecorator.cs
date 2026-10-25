using Avalonia;
using Avalonia.Controls;

namespace LayerPeek.App.Settings.Widgets;

/// <summary>
/// Hosts a list that scrolls by itself (the widget list of the Widgets tab, T-092 design 3.2) in a row of height "*" inside
/// a page that scrolls: the child is measured with at most <see cref="CompactHeight"/>, so that the page grows only for
/// the other content of the row, and it is arranged at whatever height the row gets, scrolling inside. Without it the page
/// would measure the whole list and scroll instead of the list.
/// </summary>
public sealed class CompactHeightDecorator : Decorator
{
    /// <summary>The most height the child is measured with (and asks for); default unlimited.</summary>
    public static readonly StyledProperty<double> CompactHeightProperty =
        AvaloniaProperty.Register<CompactHeightDecorator, double>(nameof(CompactHeight), double.PositiveInfinity, validate: static value => value >= 0);

    static CompactHeightDecorator()
    {
        AffectsMeasure<CompactHeightDecorator>(CompactHeightProperty);
    }

    public double CompactHeight
    {
        get => GetValue(CompactHeightProperty);
        set => SetValue(CompactHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        base.MeasureOverride(availableSize.WithHeight(Math.Min(availableSize.Height, CompactHeight)));
}
