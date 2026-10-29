using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Icons;
using LayerPeek.App.UiTests.Infrastructure;

namespace LayerPeek.App.UiTests.Tray;

/// <summary>
/// macOS menu bar template icons (T-018, <c>docs/design/macos-tray-icons.md</c>) drawn by Skia: snapshot references in
/// the macOS set (<c>Snapshots/macOS/MenuBarIcons</c>, the icons are used on macOS only) and the rules of the design
/// checked on the pixels on every OS.
/// </summary>
public sealed class MenuBarIconRendererTests(HeadlessSession session)
{
    private const int S = MenuBarIconRenderer.Scale;

    /// <summary>Every status plus the progress steps of the designer's SVGs (and the first step and 50 %).</summary>
    public static TheoryData<string, TrayStatus, int> Cases() => new()
    {
        { "idle", TrayStatus.Idle, 0 },
        { "busy", TrayStatus.Busy, 0 },
        { "printing-0", TrayStatus.Printing, 0 },
        { "printing-5", TrayStatus.Printing, 5 },
        { "printing-10", TrayStatus.Printing, 10 },
        { "printing-35", TrayStatus.Printing, 35 },
        { "printing-50", TrayStatus.Printing, 50 },
        { "printing-70", TrayStatus.Printing, 70 },
        { "printing-95", TrayStatus.Printing, 95 },
        { "printing-100", TrayStatus.Printing, 100 },
        { "paused-0", TrayStatus.Paused, 0 },
        { "paused-50", TrayStatus.Paused, 50 },
        { "finished", TrayStatus.Finished, 0 },
        { "attention", TrayStatus.Attention, 0 },
        { "error", TrayStatus.Error, 0 },
        { "offline", TrayStatus.Offline, 0 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public Task IconMatchesReference(string name, TrayStatus status, int progress) =>
        session.RunAsync(_ =>
        {
            Assert.SkipUnless(OperatingSystem.IsMacOS(), "The menu bar template icons are used and referenced on macOS only.");
            using RenderTargetBitmap bitmap = Render(status, progress);

            Assert.Equal(new PixelSize(36, 36), bitmap.PixelSize);
            SnapshotAssert.Matches(bitmap, $"MenuBarIcons/{name}");
            return Task.CompletedTask;
        });

    [Fact]
    public Task IconsAreBlackWithAlphaOnly() =>
        session.RunAsync(_ =>
        {
            // A template image: macOS uses only the alpha channel, any colour would be a design error.
            foreach (TrayStatus status in Enum.GetValues<TrayStatus>())
            {
                foreach ((int x, int y, Color color) in Pixels(status, 35).All())
                {
                    Assert.True(color.A < 8 || (color.R <= 2 && color.G <= 2 && color.B <= 2), $"{status}: pixel ({x}, {y}) is {color}.");
                }
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task MotifIsSharpOutsideTheSymbol() =>
        session.RunAsync(_ =>
        {
            // Every edge of the fin, the block and the layers is on whole points (even pixels): no blurred pixel outside
            // the symbol box and the nozzle tip, whose sides are diagonal.
            byte[] allowed = [0, MenuBarIconAppearance.Dimmed, 255];
            Rect symbol = Px(MenuBarIconRenderer.SymbolBox).Inflate(2);
            Rect tip = new(3 * S, 6 * S, 2 * S, 1 * S);
            foreach (TrayStatus status in Enum.GetValues<TrayStatus>().Where(static s => s != TrayStatus.Offline))
            {
                foreach ((int x, int y, Color color) in Pixels(status, 35).All())
                {
                    Rect pixel = new(x, y, 1, 1);
                    if (!symbol.Intersects(pixel) && !tip.Intersects(pixel))
                    {
                        Assert.True(allowed.Any(a => Math.Abs(a - color.A) <= 1), $"{status}: pixel ({x}, {y}) has alpha {color.A}.");
                    }
                }
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task SymbolKeepsClearOfTheHotendAndTheLayers() =>
        session.RunAsync(_ =>
        {
            // 1.5 pt between the hotend (ends at x 7) and the symbol, and the row between the symbol and the top layer
            // (y 8.5–9) stay empty for every status with a corner symbol.
            foreach (TrayStatus status in Enum.GetValues<TrayStatus>().Where(static s => s != TrayStatus.Offline))
            {
                IconPixels pixels = Pixels(status, 35);
                for (int y = 0; y < 9 * S; y++)
                {
                    for (int x = 7 * S; x < (int)(8.5 * S); x++)
                    {
                        Assert.True(pixels[x, y].A == 0, $"{status}: pixel ({x}, {y}) in the gap has alpha {pixels[x, y].A}.");
                    }
                }

                for (int x = 0; x < pixels.Size; x++)
                {
                    Assert.True(pixels[x, 17].A == 0, $"{status}: pixel ({x}, 17) above the top layer has alpha {pixels[x, 17].A}.");
                }
            }

            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(TrayStatus.Printing, 0)]
    [InlineData(TrayStatus.Printing, 5)]
    [InlineData(TrayStatus.Printing, 35)]
    [InlineData(TrayStatus.Printing, 70)]
    [InlineData(TrayStatus.Printing, 95)]
    [InlineData(TrayStatus.Printing, 100)]
    [InlineData(TrayStatus.Paused, 50)]
    [InlineData(TrayStatus.Finished, 0)]
    [InlineData(TrayStatus.Error, 0)]
    [InlineData(TrayStatus.Idle, 0)]
    public Task LayersFillWholePointsBottomFirstFromTheLeft(TrayStatus status, int progress) =>
        session.RunAsync(_ =>
        {
            MenuBarIconAppearance appearance = MenuBarIconAppearance.For(TrayIconKey.For(status, progress));
            int[] filled = MenuBarIconRenderer.FilledPoints(appearance.FilledFraction);
            IconPixels pixels = Pixels(status, progress);
            for (int i = 0; i < MenuBarIconRenderer.Layers.Count; i++)
            {
                Rect layer = Px(MenuBarIconRenderer.Layers[i]);
                for (int x = (int)layer.X; x < (int)layer.Right; x++)
                {
                    for (int y = (int)layer.Y; y < (int)layer.Bottom; y++)
                    {
                        byte expected = (x - (int)layer.X) / S < filled[i] ? appearance.Ink : appearance.Track;
                        Assert.True(Math.Abs(expected - pixels[x, y].A) <= 1, $"{status} {progress} %, layer {i}, ({x}, {y}): expected alpha {expected}, was {pixels[x, y].A}.");
                    }
                }
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task OfflineIsDimmedAndSlashedWithAGap() =>
        session.RunAsync(_ =>
        {
            IconPixels offline = Pixels(TrayStatus.Offline, 0);

            AssertAlpha(MenuBarIconAppearance.Dimmed, offline, 3, 11);   // heater block (1.5, 5.5 pt)
            AssertAlpha(MenuBarIconAppearance.Faint, offline, 33, 19);   // top layer, right end (16.5, 9.5 pt)
            AssertAlpha(255, offline, 18, 18);                           // middle of the slash (9, 9 pt)
            AssertAlpha(0, offline, 23, 20);                             // top layer next to the slash: cut out
            Assert.True(offline.All().Where(static p => p.X >= 9 * S && p.Y < 8 * S).All(static p => p.Color.A == 0 || IsOnSlash(p.X, p.Y)), "Offline draws a corner symbol.");
            return Task.CompletedTask;
        });

    [Fact]
    public Task EveryStatusLooksDifferent() =>
        session.RunAsync(_ =>
        {
            // Printing 0 % in particular differs from idle by its ▶ alone (no colour on the menu bar).
            List<(TrayStatus Status, IconPixels Pixels)> icons = [.. Enum.GetValues<TrayStatus>().Select(static s => (s, Pixels(s, 0)))];
            for (int i = 0; i < icons.Count; i++)
            {
                for (int j = i + 1; j < icons.Count; j++)
                {
                    int different = icons[i].Pixels.All().Count(p => Math.Abs(p.Color.A - icons[j].Pixels[p.X, p.Y].A) > 32);
                    Assert.True(different >= 8, $"{icons[i].Status} and {icons[j].Status} differ in only {different} pixels.");
                }
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task IconSetRendersEveryKeyOnce() =>
        session.RunAsync(_ =>
        {
            using TemplateTrayIconSet icons = new();
            TrayIconKey key = TrayIconKey.For(TrayStatus.Printing, 42);

            Assert.Same(icons.GetIcon(key), icons.GetIcon(TrayIconKey.For(TrayStatus.Printing, 44)));
            Assert.NotSame(icons.GetIcon(key), icons.GetIcon(TrayIconKey.For(TrayStatus.Printing, 45)));
            Assert.NotSame(icons.GetIcon(key), icons.GetIcon(TrayIconKey.For(TrayStatus.Paused, 42)));
            return Task.CompletedTask;
        });

    private static void AssertAlpha(byte expected, IconPixels pixels, int x, int y) =>
        Assert.True(Math.Abs(expected - pixels[x, y].A) <= 1, $"Pixel ({x}, {y}): expected alpha {expected}, was {pixels[x, y].A}.");

    /// <summary>Within 1 pt of the slash line (1.5, 1)–(16.5, 17) pt, measured from the pixel centre.</summary>
    private static bool IsOnSlash(int x, int y)
    {
        double px = (x + 0.5) / S;
        double py = (y + 0.5) / S;
        double distance = Math.Abs((16 * (px - 1.5)) - (15 * (py - 1))) / Math.Sqrt((16 * 16) + (15 * 15));
        return distance <= 1;
    }

    private static Rect Px(Rect points) => new(points.X * S, points.Y * S, points.Width * S, points.Height * S);

    private static RenderTargetBitmap Render(TrayStatus status, int progress) =>
        MenuBarIconRenderer.RenderBitmap(MenuBarIconAppearance.For(TrayIconKey.For(status, progress)));

    private static IconPixels Pixels(TrayStatus status, int progress)
    {
        using RenderTargetBitmap bitmap = Render(status, progress);
        return IconPixels.Read(bitmap);
    }
}
