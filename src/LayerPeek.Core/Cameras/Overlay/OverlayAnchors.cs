namespace LayerPeek.Core.Cameras.Overlay;

/// <summary>Geometry of <see cref="OverlayAnchor"/> and mirroring (T-077): left ↔ right, top ↔ bottom; centers keep their side.</summary>
public static class OverlayAnchors
{
    /// <summary>All anchors in declaration order.</summary>
    public static IReadOnlyList<OverlayAnchor> All { get; } =
    [
        OverlayAnchor.TopLeft,
        OverlayAnchor.TopCenter,
        OverlayAnchor.TopRight,
        OverlayAnchor.BottomLeft,
        OverlayAnchor.BottomCenter,
        OverlayAnchor.BottomRight,
    ];

    public static bool IsDefined(OverlayAnchor anchor) => anchor is >= OverlayAnchor.TopLeft and <= OverlayAnchor.BottomRight;

    /// <exception cref="ArgumentOutOfRangeException">Unknown anchor (also for the other queries).</exception>
    public static bool IsTop(OverlayAnchor anchor) => Validate(anchor) is OverlayAnchor.TopLeft or OverlayAnchor.TopCenter or OverlayAnchor.TopRight;

    public static bool IsLeft(OverlayAnchor anchor) => Validate(anchor) is OverlayAnchor.TopLeft or OverlayAnchor.BottomLeft;

    public static bool IsRight(OverlayAnchor anchor) => Validate(anchor) is OverlayAnchor.TopRight or OverlayAnchor.BottomRight;

    /// <summary>Horizontally centered (<see cref="OverlayAnchor.TopCenter"/>, <see cref="OverlayAnchor.BottomCenter"/>).</summary>
    public static bool IsCenter(OverlayAnchor anchor) => Validate(anchor) is OverlayAnchor.TopCenter or OverlayAnchor.BottomCenter;

    /// <summary>One of the four corners (the old T-036 overlay corners).</summary>
    public static bool IsCorner(OverlayAnchor anchor) => !IsCenter(anchor);

    /// <summary>
    /// <paramref name="anchor"/> mirrored: <paramref name="horizontally"/> swaps left and right (centers stay),
    /// <paramref name="vertically"/> swaps top and bottom.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Unknown anchor.</exception>
    public static OverlayAnchor Mirror(OverlayAnchor anchor, bool horizontally, bool vertically)
    {
        bool top = IsTop(anchor) != vertically;
        int column = Column(anchor);
        if (horizontally)
        {
            column = 2 - column;
        }

        return (OverlayAnchor)((top ? 0 : 3) + column);
    }

    /// <summary>
    /// <paramref name="anchor"/> mirrored so that corner <paramref name="from"/> lands in corner <paramref name="to"/>:
    /// horizontally when their sides differ, vertically when their heights differ.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">An unknown anchor, or <paramref name="from"/> / <paramref name="to"/> is not a corner.</exception>
    public static OverlayAnchor MirrorBetweenCorners(OverlayAnchor anchor, OverlayAnchor from, OverlayAnchor to)
    {
        RequireCorner(from, nameof(from));
        RequireCorner(to, nameof(to));
        return Mirror(anchor, horizontally: IsLeft(from) != IsLeft(to), vertically: IsTop(from) != IsTop(to));
    }

    /// <exception cref="ArgumentOutOfRangeException">Unknown anchor.</exception>
    internal static OverlayAnchor Validate(OverlayAnchor anchor, string? paramName = null) =>
        IsDefined(anchor) ? anchor : throw new ArgumentOutOfRangeException(paramName ?? nameof(anchor), anchor, "Unknown overlay anchor.");

    /// <exception cref="ArgumentOutOfRangeException">Unknown anchor or a center anchor.</exception>
    internal static void RequireCorner(OverlayAnchor anchor, string paramName)
    {
        if (!IsDefined(anchor) || IsCenter(anchor))
        {
            throw new ArgumentOutOfRangeException(paramName, anchor, "Not a corner anchor.");
        }
    }

    /// <summary>0 = left, 1 = center, 2 = right.</summary>
    private static int Column(OverlayAnchor anchor) => (int)anchor % 3;
}
