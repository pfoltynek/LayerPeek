using Avalonia;

namespace PrusaTrayDiagnostic.App.Tray.Icons;

/// <summary>
/// Geometry of the tray icon at one pixel size. Design B is drawn on a 32-unit grid; every size has its own layout with
/// all rectangles on whole pixels, so the hotend and the layers stay sharp instead of being scaled with blurred edges.
/// 32 px is the design grid itself, 16 px is the designer's hand-hinted variant (pixel-art badge), 20 and 24 px are
/// derived from them. The badge is a vector disc at 20–32 px and never overlaps the hotend or the layers.
/// </summary>
internal sealed class TrayIconLayout
{
    private TrayIconLayout(int size)
    {
        Size = size;
    }

    /// <summary>Sizes rendered for the notification area: 16 px × 100 / 125 / 150 / 200 % scaling.</summary>
    public static IReadOnlyList<int> Sizes { get; } = [16, 20, 24, 32];

    public int Size { get; }

    /// <summary>Heatsink fin (top part of the hotend).</summary>
    public PixelRect Fin { get; private init; }

    /// <summary>Heater block.</summary>
    public PixelRect Block { get; private init; }

    /// <summary>Nozzle tip.</summary>
    public PixelRect Tip { get; private init; }

    /// <summary>The three layers, bottom (filled first) to top.</summary>
    public IReadOnlyList<PixelRect> Layers { get; private init; } = [];

    /// <summary>Centre of the vector badge (20–32 px).</summary>
    public Point BadgeCentre { get; private init; }

    /// <summary>Scale of the badge relative to the 32-unit design (disc radius 7 × scale including its outline).</summary>
    public double BadgeScale { get; private init; }

    /// <summary>Top-left corner of the 7 × 7 pixel-art badge; set at 16 px only, where a disc would be a blur.</summary>
    public PixelPoint? PixelBadge { get; private init; }

    /// <summary>Area kept free around the badge so it reads as separate from the motif.</summary>
    public Rect BadgeClearance => PixelBadge is { } corner
        ? new Rect(corner.X - 1, corner.Y - 1, 9, 9)
        : new Rect(BadgeCentre.X - ClearanceRadius, BadgeCentre.Y - ClearanceRadius, 2 * ClearanceRadius, 2 * ClearanceRadius);

    /// <summary>Radius of the free area around the vector badge (8.5 units of the design).</summary>
    public double ClearanceRadius => 8.5 * BadgeScale;

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not one of <see cref="Sizes"/>.</exception>
    public static TrayIconLayout For(int size) => size switch
    {
        16 => new(16)
        {
            Fin = new(2, 0, 4, 1),
            Block = new(1, 2, 6, 3),
            Tip = new(3, 5, 2, 1),
            Layers = Bars(x: 1, width: 14, height: 2, 14, 11, 8),
            PixelBadge = new PixelPoint(9, 0),
        },
        20 => new(20)
        {
            Fin = new(3, 0, 4, 1),
            Block = new(1, 2, 8, 4),
            Tip = new(4, 6, 2, 2),
            Layers = Bars(x: 1, width: 18, height: 2, 18, 14, 10),
            BadgeCentre = new Point(15.5, 4.5),
            BadgeScale = 0.625,
        },
        24 => new(24)
        {
            Fin = new(3, 0, 6, 2),
            Block = new(1, 3, 10, 5),
            Tip = new(4, 8, 4, 2),
            Layers = Bars(x: 1, width: 22, height: 3, 21, 17, 13),
            BadgeCentre = new Point(18.5, 5.5),
            BadgeScale = 0.75,
        },
        32 => new(32)
        {
            Fin = new(4, 0, 8, 2),
            Block = new(2, 4, 12, 6),
            Tip = new(6, 10, 4, 2),
            Layers = Bars(x: 2, width: 28, height: 4, 28, 22, 16),
            BadgeCentre = new Point(25, 7),
            BadgeScale = 1,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "The tray icon is drawn at 16, 20, 24 and 32 px."),
    };

    /// <summary>
    /// How many pixels of each layer (bottom first) are filled. Rounded down, so the layers are full only at 100 %.
    /// </summary>
    public int[] FilledPixels(double fraction)
    {
        int width = Layers[0].Width;
        int remaining = (int)Math.Floor((Layers.Count * width * Math.Clamp(fraction, 0, 1)) + 1e-9);
        int[] filled = new int[Layers.Count];
        for (int i = 0; i < filled.Length; i++)
        {
            filled[i] = Math.Min(remaining, width);
            remaining -= filled[i];
        }

        return filled;
    }

    private static PixelRect[] Bars(int x, int width, int height, params int[] tops) =>
        [.. tops.Select(top => new PixelRect(x, top, width, height))];
}
