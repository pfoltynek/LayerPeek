using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>
/// The overlay widgets the app can draw (T-081), filled with an <b>explicit list</b> in the composition root
/// (<c>Composition/OverlayWidgets</c>): no assembly scanning, no <c>Activator</c>, so it is Native AOT safe. A layout id
/// without a registered widget (a widget of a newer version, or one not implemented yet) is kept in the layout and simply
/// not drawn. Immutable and thread-safe.
/// </summary>
public sealed class OverlayWidgetRegistry
{
    private readonly IOverlayWidgetDefinition[] _definitions;

    /// <exception cref="ArgumentException">A <see langword="null"/> entry, an invalid id or an id registered twice (ignoring case, like the layout).</exception>
    public OverlayWidgetRegistry(IEnumerable<IOverlayWidgetDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        IOverlayWidgetDefinition[] copy = [.. definitions];
        HashSet<string> ids = new(copy.Length, StringComparer.OrdinalIgnoreCase);
        foreach (IOverlayWidgetDefinition? definition in copy)
        {
            if (definition is null)
            {
                throw new ArgumentException("The widget list must not contain null entries.", nameof(definitions));
            }

            if (!OverlayWidgetIds.IsValid(definition.Id))
            {
                throw new ArgumentException("Invalid overlay widget id.", nameof(definitions));
            }

            if (!ids.Add(definition.Id))
            {
                throw new ArgumentException($"The overlay widget '{definition.Id}' is registered twice.", nameof(definitions));
            }
        }

        _definitions = copy;
    }

    /// <summary>No widgets.</summary>
    public static OverlayWidgetRegistry Empty { get; } = new([]);

    /// <summary>In registration order.</summary>
    public IReadOnlyList<IOverlayWidgetDefinition> Definitions => _definitions;

    /// <summary>The widget registered for <paramref name="widgetId"/> (ignoring case); <see langword="null"/> when there is none.</summary>
    public IOverlayWidgetDefinition? Find(string widgetId)
    {
        ArgumentNullException.ThrowIfNull(widgetId);
        foreach (IOverlayWidgetDefinition definition in _definitions)
        {
            if (string.Equals(definition.Id, widgetId, StringComparison.OrdinalIgnoreCase))
            {
                return definition;
            }
        }

        return null;
    }
}
