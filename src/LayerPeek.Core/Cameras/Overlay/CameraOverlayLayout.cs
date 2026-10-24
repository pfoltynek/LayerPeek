using System.Collections.ObjectModel;
using System.Text;

using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cameras.Overlay;

/// <summary>
/// The widgets drawn over the image of the camera window of one printer and where (T-077), part of
/// <see cref="CameraSettings"/>. Immutable; every operation returns a new layout, or the same instance when nothing changes
/// (cheap change detection). Each widget id is in it at most once (ignoring case). <see cref="Widgets"/> is grouped by anchor
/// in <see cref="OverlayAnchor"/> order; within an anchor the order is the stacking order from the window edge inward.
/// Equality compares the widgets element by element.
/// </summary>
public sealed record CameraOverlayLayout
{
    /// <summary>
    /// The anchor of the main group of the default layout, i.e. of the Times widget (T-076 3.3); an old T-036 overlay corner
    /// moves it there (<see cref="FromLegacyCorner"/>).
    /// </summary>
    public const OverlayAnchor MainAnchor = OverlayAnchor.BottomLeft;

    /// <summary>Most widgets a layout holds (a safeguard against garbage files; v1 has five).</summary>
    public const int MaxWidgets = 32;

    private readonly OverlayWidgetPlacement[] _widgets;

    /// <exception cref="ArgumentNullException"><paramref name="widgets"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A <see langword="null"/> entry, a duplicate id or more than <see cref="MaxWidgets"/> widgets.</exception>
    public CameraOverlayLayout(IEnumerable<OverlayWidgetPlacement> widgets)
    {
        ArgumentNullException.ThrowIfNull(widgets);
        OverlayWidgetPlacement[] copy = [.. widgets];
        if (copy.Length > MaxWidgets)
        {
            throw new ArgumentException($"An overlay layout holds at most {MaxWidgets} widgets.", nameof(widgets));
        }

        HashSet<string> ids = new(copy.Length, StringComparer.OrdinalIgnoreCase);
        foreach (OverlayWidgetPlacement? widget in copy)
        {
            if (widget is null)
            {
                throw new ArgumentException("The widget list must not contain null entries.", nameof(widgets));
            }

            if (!ids.Add(widget.WidgetId))
            {
                throw new ArgumentException("Each widget can be in the layout only once.", nameof(widgets));
            }
        }

        _widgets = SortByAnchor(copy);
        Widgets = new ReadOnlyCollection<OverlayWidgetPlacement>(_widgets);
    }

    /// <summary>The default layout of the approved design (T-076 3.3): status, print name, times, cool-down, temperatures.</summary>
    public static CameraOverlayLayout Default { get; } = new(
    [
        new OverlayWidgetPlacement(OverlayWidgetIds.Status, OverlayAnchor.TopLeft, emphasized: true),
        new OverlayWidgetPlacement(OverlayWidgetIds.PrintName, OverlayAnchor.TopCenter),
        new OverlayWidgetPlacement(OverlayWidgetIds.Times, OverlayAnchor.BottomLeft),
        new OverlayWidgetPlacement(OverlayWidgetIds.Cooldown, OverlayAnchor.BottomLeft),
        new OverlayWidgetPlacement(OverlayWidgetIds.Temperatures, OverlayAnchor.BottomRight),
    ]);

    /// <summary>The widgets grouped by anchor (<see cref="OverlayAnchor"/> order), each group from the window edge inward. Read-only.</summary>
    public IReadOnlyList<OverlayWidgetPlacement> Widgets { get; }

    /// <summary>The placement of <paramref name="widgetId"/> in <see cref="Default"/>; <see langword="null"/> for an id unknown to this version.</summary>
    public static OverlayWidgetPlacement? DefaultPlacement(string widgetId) => Default.Find(widgetId);

    /// <summary>
    /// The layout for an old T-036 overlay corner (T-077 Q4): <see cref="Default"/> mirrored so that its
    /// <see cref="MainAnchor"/> lands in <paramref name="corner"/>; exactly <see cref="Default"/> for the main anchor itself.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="corner"/> is not one of the four corners.</exception>
    public static CameraOverlayLayout FromLegacyCorner(OverlayAnchor corner)
    {
        OverlayAnchors.RequireCorner(corner, nameof(corner));
        return Default.Mirrored(
            horizontally: OverlayAnchors.IsLeft(MainAnchor) != OverlayAnchors.IsLeft(corner),
            vertically: OverlayAnchors.IsTop(MainAnchor) != OverlayAnchors.IsTop(corner));
    }

    /// <summary>
    /// "Use this layout for all printers" (T-077 Q1): <paramref name="printers"/> with <paramref name="layout"/> as the overlay
    /// of each, also of printers whose camera is off; nothing else changes and printers that already have it are kept as
    /// they are. Returns <paramref name="printers"/> itself when no printer changes.
    /// </summary>
    /// <exception cref="ArgumentException">The list contains <see langword="null"/>.</exception>
    public static IReadOnlyList<PrinterDefinition> ApplyToAll(IReadOnlyList<PrinterDefinition> printers, CameraOverlayLayout layout)
    {
        ArgumentNullException.ThrowIfNull(printers);
        ArgumentNullException.ThrowIfNull(layout);
        PrinterDefinition[]? updated = null;
        for (int i = 0; i < printers.Count; i++)
        {
            PrinterDefinition? printer = printers[i] ?? throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            if (printer.Camera.Overlay != layout)
            {
                updated ??= [.. printers];
                updated[i] = printer with { Camera = printer.Camera with { Overlay = layout } };
            }
        }

        return updated ?? printers;
    }

    /// <summary>The placement of <paramref name="widgetId"/> (ignoring case); <see langword="null"/> when it is not in the layout.</summary>
    public OverlayWidgetPlacement? Find(string widgetId)
    {
        int index = IndexOf(widgetId);
        return index < 0 ? null : _widgets[index];
    }

    public bool Contains(string widgetId) => IndexOf(widgetId) >= 0;

    /// <summary>The widgets of <paramref name="anchor"/> in stacking order, from the window edge inward.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Unknown anchor.</exception>
    public IReadOnlyList<OverlayWidgetPlacement> InAnchor(OverlayAnchor anchor)
    {
        OverlayAnchors.Validate(anchor, nameof(anchor));
        return Array.FindAll(_widgets, widget => widget.Anchor == anchor);
    }

    /// <summary>
    /// Moves <paramref name="widgetId"/> into <paramref name="anchor"/> at <paramref name="position"/>: the index among the
    /// other widgets of that anchor (0 = at the window edge, their count = innermost). Size and flags stay.
    /// </summary>
    /// <exception cref="ArgumentException">The widget is not in the layout.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Unknown anchor, or <paramref name="position"/> outside 0 … the count of the other widgets.</exception>
    public CameraOverlayLayout MoveTo(string widgetId, OverlayAnchor anchor, int position)
    {
        int index = RequireIndex(widgetId);
        OverlayAnchors.Validate(anchor, nameof(anchor));
        OverlayWidgetPlacement moved = _widgets[index];
        List<OverlayWidgetPlacement> others = [.. _widgets];
        others.RemoveAt(index);

        int first = others.FindIndex(widget => widget.Anchor >= anchor);
        if (first < 0)
        {
            first = others.Count;
        }

        int count = 0;
        while (first + count < others.Count && others[first + count].Anchor == anchor)
        {
            count++;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, count);
        if (moved.Anchor == anchor && first + position == index)
        {
            return this;
        }

        others.Insert(first + position, moved with { Anchor = anchor });
        return new CameraOverlayLayout(others);
    }

    /// <exception cref="ArgumentException">The widget is not in the layout.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Unknown size.</exception>
    public CameraOverlayLayout WithSize(string widgetId, OverlayWidgetSize size)
    {
        int index = RequireIndex(widgetId);
        return _widgets[index].Size == size ? this : Replace(index, _widgets[index] with { Size = size });
    }

    /// <exception cref="ArgumentException">The widget is not in the layout.</exception>
    public CameraOverlayLayout WithEmphasized(string widgetId, bool emphasized)
    {
        int index = RequireIndex(widgetId);
        return _widgets[index].Emphasized == emphasized ? this : Replace(index, _widgets[index] with { Emphasized = emphasized });
    }

    /// <exception cref="ArgumentException">The widget is not in the layout.</exception>
    public CameraOverlayLayout WithVisible(string widgetId, bool visible)
    {
        int index = RequireIndex(widgetId);
        return _widgets[index].Visible == visible ? this : Replace(index, _widgets[index] with { Visible = visible });
    }

    /// <summary>
    /// "Restore defaults": <see cref="Default"/>, keeping the placements of widgets unknown to this version (written by a
    /// newer one) unchanged so that they are not lost.
    /// </summary>
    public CameraOverlayLayout ResetToDefault()
    {
        OverlayWidgetPlacement[] unknown = Array.FindAll(_widgets, static widget => !OverlayWidgetIds.IsKnown(widget.WidgetId));
        return unknown.Length == 0 ? Default : new CameraOverlayLayout([.. Default._widgets, .. unknown]);
    }

    /// <summary>
    /// Every widget mirrored (<see cref="OverlayAnchors.Mirror"/>); order within each anchor, sizes and flags stay, so the
    /// cards keep their relation to the window edge.
    /// </summary>
    public CameraOverlayLayout Mirrored(bool horizontally, bool vertically) =>
        !horizontally && !vertically
            ? this
            : new CameraOverlayLayout(_widgets.Select(widget => widget with { Anchor = OverlayAnchors.Mirror(widget.Anchor, horizontally, vertically) }));

    public bool Equals(CameraOverlayLayout? other) =>
        other is not null && (ReferenceEquals(this, other) || _widgets.AsSpan().SequenceEqual(other._widgets));

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (OverlayWidgetPlacement widget in _widgets)
        {
            hash.Add(widget);
        }

        return hash.ToHashCode();
    }

    // Replaces the synthesized member list, which would print the collection type instead of the widgets.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Widgets = [");
        for (int i = 0; i < _widgets.Length; i++)
        {
            builder.Append(i == 0 ? " " : ", ").Append(_widgets[i]);
        }

        builder.Append(" ]");
        return true;
    }

    /// <summary>Stable sort by anchor; keeps the order within each anchor.</summary>
    private static OverlayWidgetPlacement[] SortByAnchor(OverlayWidgetPlacement[] widgets)
    {
        OverlayWidgetPlacement[] sorted = new OverlayWidgetPlacement[widgets.Length];
        int next = 0;
        foreach (OverlayAnchor anchor in OverlayAnchors.All)
        {
            foreach (OverlayWidgetPlacement widget in widgets)
            {
                if (widget.Anchor == anchor)
                {
                    sorted[next++] = widget;
                }
            }
        }

        return sorted;
    }

    private int IndexOf(string widgetId)
    {
        ArgumentNullException.ThrowIfNull(widgetId);
        for (int i = 0; i < _widgets.Length; i++)
        {
            if (string.Equals(_widgets[i].WidgetId, widgetId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int RequireIndex(string widgetId)
    {
        int index = IndexOf(widgetId);
        return index >= 0 ? index : throw new ArgumentException("The widget is not in the layout.", nameof(widgetId));
    }

    private CameraOverlayLayout Replace(int index, OverlayWidgetPlacement widget)
    {
        OverlayWidgetPlacement[] copy = [.. _widgets];
        copy[index] = widget;
        return new CameraOverlayLayout(copy);
    }
}
