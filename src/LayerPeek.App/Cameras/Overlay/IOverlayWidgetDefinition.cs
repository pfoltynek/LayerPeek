using Avalonia.Controls.Templates;

using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>
/// One type of overlay widget (T-081): its id, name, sizes, collision priority, card template and how to create its view
/// model for a camera window. Registered explicitly in <see cref="OverlayWidgetRegistry"/> (Native AOT: no scanning, no
/// <c>Activator</c>). Usually an <see cref="OverlayWidgetDefinition"/>.
/// </summary>
public interface IOverlayWidgetDefinition
{
    /// <summary>The stable id in the layout (one of <see cref="OverlayWidgetIds"/> for the widgets of this version).</summary>
    string Id { get; }

    /// <summary>Key of the widget's name in <c>Strings</c> (settings table, layout mode, logs use the id).</summary>
    string NameKey { get; }

    /// <summary>The sizes the widget can draw, smallest first; never empty.</summary>
    IReadOnlyList<OverlayWidgetSize> SupportedSizes { get; }

    /// <summary>
    /// Which card stays when cards collide in a small window (T-076 1.4): the higher value wins (Status &gt; Times &gt;
    /// Cool-down &gt; Temperatures &gt; Print name).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// The content of the card (without the card itself, which the overlay draws): a <c>DataTemplate</c> whose
    /// <c>x:DataType</c> is the widget's view model (compiled bindings).
    /// </summary>
    IDataTemplate CardTemplate { get; }

    /// <summary>
    /// The narrowest the card can be drawn in <paramref name="size"/> (DIP, without the card padding counted twice): a centre
    /// card that gets less room is drawn one size smaller, or not at all (T-076 1.4 rule 1). 0 = no minimum.
    /// </summary>
    double MinWidth(OverlayWidgetSize size);

    /// <summary>A new view model of the widget for one camera window; disposed with the window or when it leaves the layout.</summary>
    OverlayWidgetViewModel Create(OverlayWidgetContext context);
}

/// <summary>The narrowest card per size of a widget (T-076 2: "min" of each widget's widths); 0 = no minimum.</summary>
public readonly record struct OverlayWidgetMinWidths(double Small, double Medium, double Large)
{
    public double For(OverlayWidgetSize size) => size switch
    {
        OverlayWidgetSize.Small => Small,
        OverlayWidgetSize.Medium => Medium,
        OverlayWidgetSize.Large => Large,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown overlay widget size."),
    };
}

/// <summary>
/// The usual <see cref="IOverlayWidgetDefinition"/>: a new widget is its view model class, its card template and one
/// <c>new OverlayWidgetDefinition(…)</c> line in the explicit registration (<c>Composition/OverlayWidgets</c>).
/// </summary>
public sealed class OverlayWidgetDefinition : IOverlayWidgetDefinition
{
    private static readonly OverlayWidgetSize[] AllSizes = [OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large];

    private readonly Func<OverlayWidgetContext, OverlayWidgetViewModel> _create;
    private readonly OverlayWidgetMinWidths _minWidths;

    /// <param name="supportedSizes">The sizes the widget draws; <see langword="null"/> = all three (every v1 widget).</param>
    /// <exception cref="ArgumentException">An invalid id or name key, or no or unknown sizes.</exception>
    public OverlayWidgetDefinition(
        string id,
        string nameKey,
        int priority,
        IDataTemplate cardTemplate,
        Func<OverlayWidgetContext, OverlayWidgetViewModel> create,
        OverlayWidgetMinWidths minWidths = default,
        IReadOnlyList<OverlayWidgetSize>? supportedSizes = null)
    {
        if (!OverlayWidgetIds.IsValid(id))
        {
            throw new ArgumentException("Invalid overlay widget id.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(nameKey);
        ArgumentNullException.ThrowIfNull(cardTemplate);
        ArgumentNullException.ThrowIfNull(create);
        OverlayWidgetSize[] sizes = supportedSizes is null ? AllSizes : [.. supportedSizes.Distinct().Order()];
        if (sizes.Length == 0 || Array.Exists(sizes, static size => size is < OverlayWidgetSize.Small or > OverlayWidgetSize.Large))
        {
            throw new ArgumentException("A widget needs at least one known size.", nameof(supportedSizes));
        }

        Id = id;
        NameKey = nameKey;
        Priority = priority;
        CardTemplate = cardTemplate;
        SupportedSizes = sizes;
        _create = create;
        _minWidths = minWidths;
    }

    public string Id { get; }

    public string NameKey { get; }

    public IReadOnlyList<OverlayWidgetSize> SupportedSizes { get; }

    public int Priority { get; }

    public IDataTemplate CardTemplate { get; }

    public double MinWidth(OverlayWidgetSize size) => _minWidths.For(size);

    public OverlayWidgetViewModel Create(OverlayWidgetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _create(context);
    }
}
