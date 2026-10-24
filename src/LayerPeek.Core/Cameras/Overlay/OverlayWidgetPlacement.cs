namespace LayerPeek.Core.Cameras.Overlay;

/// <summary>
/// Where and how one widget is drawn over the camera image (T-077). The normal window and full screen share it; the mini
/// preview draws no cards (user decision 2026-10-24), so there is no visibility for it. The order within the anchor is the
/// order in <see cref="CameraOverlayLayout.Widgets"/>.
/// </summary>
public sealed record OverlayWidgetPlacement
{
    /// <exception cref="ArgumentNullException"><paramref name="widgetId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="widgetId"/> is not <see cref="OverlayWidgetIds.IsValid">valid</see>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Unknown <paramref name="anchor"/> or <paramref name="size"/>.</exception>
    public OverlayWidgetPlacement(string widgetId, OverlayAnchor anchor, OverlayWidgetSize size = OverlayWidgetSize.Medium, bool emphasized = false, bool visible = true)
    {
        WidgetId = widgetId;
        Anchor = anchor;
        Size = size;
        Emphasized = emphasized;
        Visible = visible;
    }

    /// <summary>One of <see cref="OverlayWidgetIds.Known"/>, or an id of a newer version that is kept but not drawn.</summary>
    public string WidgetId
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!OverlayWidgetIds.IsValid(value))
            {
                // Not echoed: the id may come from a garbage file.
                throw new ArgumentException("Invalid overlay widget id.", nameof(value));
            }

            field = value;
        }
    }

    public OverlayAnchor Anchor
    {
        get;
        init => field = OverlayAnchors.Validate(value, nameof(value));
    }

    public OverlayWidgetSize Size
    {
        get;
        init => field = value is >= OverlayWidgetSize.Small and <= OverlayWidgetSize.Large
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown overlay widget size.");
    }

    /// <summary>The key value of the widget is drawn in the accent colour (T-076 Q8).</summary>
    public bool Emphasized { get; init; }

    /// <summary>Drawn in the normal window and on full screen.</summary>
    public bool Visible { get; init; }
}
