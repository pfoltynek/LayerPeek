using Avalonia;

using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Cameras.Layout;

/// <summary>A direction of the arrow keys in the layout mode.</summary>
public enum CameraLayoutDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>A zone of the layout mode and where it is (overlay coordinates).</summary>
internal readonly record struct CameraLayoutZoneBounds(OverlayAnchor Anchor, Rect Bounds);

/// <summary>
/// Geometry of the layout mode (T-076 5.2–5.5) as pure functions over rectangles, so that the view only measures and the
/// rules are testable without a window: the zone sizes, the target zone of a drag, the position in it and the card the
/// arrow keys move the focus to.
/// </summary>
internal static class CameraLayoutGeometry
{
    /// <summary>Distance of the zones from the window edges (like the anchors).</summary>
    public const double EdgeMargin = 12;

    /// <summary>Room around the cards inside a zone.</summary>
    public const double ZonePadding = 8;

    /// <summary>
    /// The base size of every zone in a client area of <paramref name="width"/> × <paramref name="height"/> DIP:
    /// width <c>clamp((W − 4 × 12) / 3, 160, 260)</c>, height <c>clamp((H − 3 × 12 − 56) / 2, 90, 150)</c> (T-076 5.2),
    /// but never wider than a third of the room between the margins, so that the zones of a row do not overlap in a narrow
    /// window (below 528 DIP the minimum 160 would). A zone grows beyond it when its cards need more.
    /// </summary>
    public static Size ZoneSize(double width, double height)
    {
        double third = Math.Max(0, (width - (4 * EdgeMargin)) / 3);
        return new Size(
            Math.Min(Math.Clamp(third, 160, 260), third),
            Math.Clamp((height - (3 * EdgeMargin) - 56) / 2, 90, 150));
    }

    /// <summary>
    /// The zone a dragged card goes to with the pointer at <paramref name="pointer"/>: the zone under it, else the zone with
    /// the nearest centre; none over <paramref name="excluded"/> (the toolbar: dropping there returns the card, T-076 5.3).
    /// </summary>
    public static OverlayAnchor? TargetZone(Point pointer, IReadOnlyList<CameraLayoutZoneBounds> zones, Rect? excluded)
    {
        ArgumentNullException.ThrowIfNull(zones);
        if (excluded is { } toolbar && toolbar.Contains(pointer))
        {
            return null;
        }

        OverlayAnchor? nearest = null;
        double best = double.PositiveInfinity;
        foreach (CameraLayoutZoneBounds zone in zones)
        {
            if (zone.Bounds.Contains(pointer))
            {
                return zone.Anchor;
            }

            Vector offset = zone.Bounds.Center - pointer;
            double distance = (offset.X * offset.X) + (offset.Y * offset.Y);
            if (distance < best)
            {
                best = distance;
                nearest = zone.Anchor;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Where a card dropped at height <paramref name="y"/> goes among the other cards of a zone, from the top of the screen
    /// (0 = above the first): the number of cards whose centre is above the pointer (T-076 5.3).
    /// </summary>
    /// <param name="y">The pointer.</param>
    /// <param name="cards">The other cards of the zone, top to bottom.</param>
    public static int InsertIndex(double y, IReadOnlyList<Rect> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        int index = 0;
        foreach (Rect card in cards)
        {
            if (card.Center.Y < y)
            {
                index++;
            }
        }

        return index;
    }

    /// <summary>
    /// The card the arrow keys move the focus to (a card not picked up, T-076 5.5): of the cards whose centre lies in
    /// <paramref name="direction"/> from the centre of <paramref name="from"/>, the one with the smallest gap that way plus
    /// twice the gap across (0 when the cards overlap across, e.g. the next card of the same column);
    /// <see langword="null"/> when there is none that way.
    /// </summary>
    public static int? Nearest(Rect from, IReadOnlyList<Rect> candidates, CameraLayoutDirection direction)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        int? nearest = null;
        double best = double.PositiveInfinity;
        for (int i = 0; i < candidates.Count; i++)
        {
            Rect to = candidates[i];
            Vector offset = to.Center - from.Center;
            bool horizontal = direction is CameraLayoutDirection.Left or CameraLayoutDirection.Right;
            double ahead = direction switch
            {
                CameraLayoutDirection.Left => -offset.X,
                CameraLayoutDirection.Right => offset.X,
                CameraLayoutDirection.Up => -offset.Y,
                _ => offset.Y,
            };

            if (ahead < 0.5)
            {
                continue;
            }

            double along = direction switch
            {
                CameraLayoutDirection.Left => from.Left - to.Right,
                CameraLayoutDirection.Right => to.Left - from.Right,
                CameraLayoutDirection.Up => from.Top - to.Bottom,
                _ => to.Top - from.Bottom,
            };
            double across = horizontal
                ? Math.Max(0, Math.Max(to.Top - from.Bottom, from.Top - to.Bottom))
                : Math.Max(0, Math.Max(to.Left - from.Right, from.Left - to.Right));
            double distance = Math.Max(0, along) + (2 * across);
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }

        return nearest;
    }
}
