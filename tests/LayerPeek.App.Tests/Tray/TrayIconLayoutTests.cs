using Avalonia;

using LayerPeek.App.Tray.Icons;

namespace LayerPeek.App.Tests.Tray;

/// <summary>Per-size geometry of the tray icon (T-025).</summary>
public sealed class TrayIconLayoutTests
{
    public static TheoryData<int> Sizes() => [.. TrayIconLayout.Sizes];

    [Fact]
    public void SizesCoverScalingFrom100To200Percent() =>
        Assert.Equal([16, 20, 24, 32], TrayIconLayout.Sizes);

    [Theory]
    [InlineData(0)]
    [InlineData(28)]
    [InlineData(64)]
    public void OtherSizesAreRejected(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TrayIconLayout.For(size));

    [Theory]
    [MemberData(nameof(Sizes))]
    public void EveryPartLiesInsideTheIcon(int size)
    {
        TrayIconLayout layout = TrayIconLayout.For(size);
        PixelRect bounds = new(0, 0, size, size);

        Assert.Equal(size, layout.Size);
        foreach (PixelRect part in Parts(layout))
        {
            Assert.True(part.Width > 0 && part.Height > 0);
            Assert.True(bounds.Contains(part), $"{part} is outside {size} px.");
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void ThreeEqualLayersBottomFirstWithGaps(int size)
    {
        IReadOnlyList<PixelRect> layers = TrayIconLayout.For(size).Layers;

        Assert.Equal(3, layers.Count);
        Assert.Equal(size, layers[0].Bottom);
        Assert.All(layers, layer => Assert.Equal((layers[0].X, layers[0].Width, layers[0].Height), (layer.X, layer.Width, layer.Height)));
        for (int i = 1; i < layers.Count; i++)
        {
            Assert.True(layers[i].Bottom < layers[i - 1].Y, $"Layer {i} at {size} px touches the one below.");
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void HotendStandsAboveTheLayers(int size)
    {
        TrayIconLayout layout = TrayIconLayout.For(size);

        Assert.True(layout.Fin.Bottom < layout.Block.Y);
        Assert.Equal(layout.Block.Bottom, layout.Tip.Y);
        Assert.True(layout.Tip.Bottom < layout.Layers[^1].Y);
        // Fin and tip are centred on the heater block.
        Assert.Equal(layout.Block.X + layout.Block.Right, layout.Fin.X + layout.Fin.Right);
        Assert.Equal(layout.Block.X + layout.Block.Right, layout.Tip.X + layout.Tip.Right);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void BadgeNeverCoversTheMotifAndStaysInside(int size)
    {
        TrayIconLayout layout = TrayIconLayout.For(size);
        Rect clearance = layout.BadgeClearance;

        foreach (PixelRect part in Parts(layout))
        {
            Rect rect = new(part.X, part.Y, part.Width, part.Height);
            if (layout.PixelBadge is null)
            {
                // Distance from the badge centre to the nearest point of the part.
                double dx = Math.Max(Math.Max(rect.X - layout.BadgeCentre.X, 0), layout.BadgeCentre.X - rect.Right);
                double dy = Math.Max(Math.Max(rect.Y - layout.BadgeCentre.Y, 0), layout.BadgeCentre.Y - rect.Bottom);
                Assert.True(Math.Sqrt((dx * dx) + (dy * dy)) >= layout.ClearanceRadius, $"{part} at {size} px is inside the badge clearance.");
            }
            else
            {
                Assert.False(clearance.Intersects(rect), $"{part} at {size} px is inside the badge clearance.");
            }
        }

        double outer = 7 * layout.BadgeScale;
        if (layout.PixelBadge is { } corner)
        {
            Assert.True(new PixelRect(0, 0, size, size).Contains(new PixelRect(corner.X, corner.Y, 7, 7)));
        }
        else
        {
            Assert.True(layout.BadgeCentre.X + outer <= size && layout.BadgeCentre.Y - outer >= 0);
        }
    }

    [Fact]
    public void OnlySixteenPixelsUsesThePixelArtBadge()
    {
        Assert.Equal(new PixelPoint(9, 0), TrayIconLayout.For(16).PixelBadge);
        Assert.All([20, 24, 32], size => Assert.Null(TrayIconLayout.For(size).PixelBadge));
        Assert.Equal(new Point(25, 7), TrayIconLayout.For(32).BadgeCentre);
        Assert.Equal(1, TrayIconLayout.For(32).BadgeScale);
    }

    [Theory]
    [InlineData(16, 0.0, new[] { 0, 0, 0 })]
    [InlineData(16, 0.05, new[] { 2, 0, 0 })]
    [InlineData(16, 0.35, new[] { 14, 0, 0 })]
    [InlineData(16, 0.5, new[] { 14, 7, 0 })]
    [InlineData(16, 0.95, new[] { 14, 14, 11 })]
    [InlineData(16, 1.0, new[] { 14, 14, 14 })]
    [InlineData(32, 0.5, new[] { 28, 14, 0 })]
    [InlineData(32, 0.99, new[] { 28, 28, 27 })]
    [InlineData(24, 1.5, new[] { 22, 22, 22 })]
    [InlineData(20, -1.0, new[] { 0, 0, 0 })]
    public void LayersFillBottomFirstAndAreFullOnlyAtTheEnd(int size, double fraction, int[] expected) =>
        Assert.Equal(expected, TrayIconLayout.For(size).FilledPixels(fraction));

    [Theory]
    [MemberData(nameof(Sizes))]
    public void FillGrowsWithProgress(int size)
    {
        TrayIconLayout layout = TrayIconLayout.For(size);
        int previous = -1;
        for (int percent = 0; percent <= 100; percent += 5)
        {
            int filled = layout.FilledPixels(percent / 100.0).Sum();
            Assert.True(filled > previous, $"{percent} % at {size} px fills no more than the step before.");
            previous = filled;
        }
    }

    private static PixelRect[] Parts(TrayIconLayout layout) => [layout.Fin, layout.Block, layout.Tip, .. layout.Layers];
}
