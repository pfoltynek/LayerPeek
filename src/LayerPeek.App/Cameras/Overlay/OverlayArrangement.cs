using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>One measured card of a stack, for <see cref="OverlayArrangement.Overflowing"/>.</summary>
/// <param name="Height">Measured height in DIP.</param>
/// <param name="Priority">Collision priority (higher stays longer).</param>
internal readonly record struct OverlayStackCard(double Height, int Priority);

/// <summary>
/// Geometry and small-window rules of the overlay cards (T-076 1.1, 1.4, 3.1, 3.2) as pure functions without Avalonia:
/// given the client size and the measured cards, which cards are drawn, in which size and where. Deterministic; the overlay
/// panel calls them while measuring.
/// </summary>
internal static class OverlayArrangement
{
    /// <summary>Distance of the anchors from the edges of the client area (T-076 1.1).</summary>
    public const double EdgeMargin = 12;

    /// <summary>Gap between two cards of one anchor, and the least gap between stacks that do not overlap (T-076 1.1, 1.4).</summary>
    public const double CardSpacing = 8;

    /// <summary>
    /// Where the top right anchor starts, always: below the window buttons (6 + 30 + 8, T-076 3.2), so that cards do not
    /// jump when the buttons appear.
    /// </summary>
    public const double TopRightStart = 44;

    /// <summary>
    /// Room the window buttons need at the top right, counted as the least width of the right column next to the top centre
    /// anchor (6 + 132 + 8, four buttons of the normal window: T-076 3.2 and the pin, T-117).
    /// </summary>
    public const double WindowButtonsReserve = 146;

    /// <summary>
    /// The small-window stage of a client area of <paramref name="width"/> × <paramref name="height"/> DIP (T-076 1.4):
    /// 0 = sizes of the layout, 1 = one size smaller, 2 = corners only in size S, 3 = no cards.
    /// </summary>
    public static int Stage(double width, double height) =>
        width >= 720 && height >= 405 ? 0
        : width >= 480 && height >= 270 ? 1
        : width >= 320 && height >= 180 ? 2
        : 3;

    /// <summary>The size a card of <paramref name="size"/> in <paramref name="anchor"/> gets in <paramref name="stage"/>; <see langword="null"/> = not drawn.</summary>
    public static OverlayWidgetSize? SizeInStage(OverlayWidgetSize size, OverlayAnchor anchor, int stage) => stage switch
    {
        0 => size,
        1 => size == OverlayWidgetSize.Small ? size : size - 1,
        2 => OverlayAnchors.IsCenter(anchor) ? null : OverlayWidgetSize.Small,
        _ => null,
    };

    /// <summary>
    /// The size a widget that draws only <paramref name="supported"/> uses for <paramref name="size"/>: the largest
    /// supported one not above it, else the smallest supported one.
    /// </summary>
    public static OverlayWidgetSize NearestSupported(OverlayWidgetSize size, IReadOnlyList<OverlayWidgetSize> supported)
    {
        ArgumentNullException.ThrowIfNull(supported);
        OverlayWidgetSize? best = null;
        OverlayWidgetSize smallest = supported[0];
        foreach (OverlayWidgetSize candidate in supported)
        {
            if (candidate <= size && (best is null || candidate > best))
            {
                best = candidate;
            }

            if (candidate < smallest)
            {
                smallest = candidate;
            }
        }

        return best ?? smallest;
    }

    /// <summary>Top edge of the first card of a top anchor.</summary>
    public static double TopStart(OverlayAnchor anchor) => anchor == OverlayAnchor.TopRight ? TopRightStart : EdgeMargin;

    /// <summary>
    /// Rule 1 (T-076 1.4): the widest a card of a centre anchor may be, given the widest card of the left and the right
    /// anchor of the same row: <c>W − 2 × (max(left, right) + 8 + 12)</c>, where the top row counts at least
    /// <see cref="WindowButtonsReserve"/> for the right side. May be negative.
    /// </summary>
    public static double CenterMaxWidth(double width, double leftWidth, double rightWidth, bool top)
    {
        double right = top ? Math.Max(rightWidth, WindowButtonsReserve) : rightWidth;
        return width - (2 * (Math.Max(leftWidth, right) + CardSpacing + EdgeMargin));
    }

    /// <summary>
    /// Rule 1: the size a centre card is drawn in when it may be <paramref name="maxWidth"/> wide: <paramref name="size"/>,
    /// or as many sizes smaller as needed until its minimum width fits; <see langword="null"/> when not even S fits.
    /// </summary>
    public static OverlayWidgetSize? FitCenter(OverlayWidgetSize size, double maxWidth, Func<OverlayWidgetSize, double> minWidth)
    {
        ArgumentNullException.ThrowIfNull(minWidth);
        if (maxWidth <= 0)
        {
            return null;
        }

        for (OverlayWidgetSize candidate = size; candidate >= OverlayWidgetSize.Small; candidate--)
        {
            if (minWidth(candidate) <= maxWidth)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Rules 2 and 3 (T-076 1.4) for one column (the top and the bottom anchor of the same side): while the two stacks do
    /// not fit between <paramref name="topStart"/> and the bottom margin with at least <see cref="CardSpacing"/> between
    /// them, the card with the lowest priority of both is left out (on equal priority the one farther from the window
    /// edge). A single stack that is taller than the column loses cards the same way.
    /// </summary>
    /// <param name="hiddenTop">Set to <see langword="true"/> for the left-out cards of <paramref name="top"/> (same length; entries already set stay left out).</param>
    /// <param name="hiddenBottom">The same for <paramref name="bottom"/>.</param>
    /// <returns>Whether a card was left out.</returns>
    public static bool Overflowing(
        double height,
        double topStart,
        ReadOnlySpan<OverlayStackCard> top,
        ReadOnlySpan<OverlayStackCard> bottom,
        Span<bool> hiddenTop,
        Span<bool> hiddenBottom)
    {
        if (hiddenTop.Length != top.Length || hiddenBottom.Length != bottom.Length)
        {
            throw new ArgumentException("Each card needs exactly one hidden flag.");
        }

        double room = height - EdgeMargin - topStart;
        bool hidAny = false;
        while (Used(top, hiddenTop) + Used(bottom, hiddenBottom) + (Any(top, hiddenTop) && Any(bottom, hiddenBottom) ? CardSpacing : 0) > room)
        {
            // The lowest priority; on a tie the card farther from its window edge (later in its stack), then the bottom one.
            int victim = -1;
            bool inBottom = false;
            int lowest = int.MaxValue;
            for (int i = 0; i < top.Length; i++)
            {
                if (!hiddenTop[i] && top[i].Priority <= lowest)
                {
                    lowest = top[i].Priority;
                    victim = i;
                }
            }

            for (int i = 0; i < bottom.Length; i++)
            {
                if (!hiddenBottom[i] && (bottom[i].Priority < lowest || (bottom[i].Priority == lowest && (inBottom || i >= victim))))
                {
                    lowest = bottom[i].Priority;
                    victim = i;
                    inBottom = true;
                }
            }

            if (victim < 0)
            {
                break;
            }

            if (inBottom)
            {
                hiddenBottom[victim] = true;
            }
            else
            {
                hiddenTop[victim] = true;
            }

            hidAny = true;
        }

        return hidAny;
    }

    /// <summary>Height of a stack: the cards that are not hidden and the gaps between them.</summary>
    public static double Used(ReadOnlySpan<OverlayStackCard> cards, ReadOnlySpan<bool> hidden)
    {
        double used = 0;
        int count = 0;
        for (int i = 0; i < cards.Length; i++)
        {
            if (!hidden[i])
            {
                used += cards[i].Height;
                count++;
            }
        }

        return count == 0 ? 0 : used + ((count - 1) * CardSpacing);
    }

    private static bool Any(ReadOnlySpan<OverlayStackCard> cards, ReadOnlySpan<bool> hidden)
    {
        for (int i = 0; i < cards.Length; i++)
        {
            if (!hidden[i])
            {
                return true;
            }
        }

        return false;
    }
}
