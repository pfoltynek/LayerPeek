using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Icons;
using LayerPeek.App.UiTests.Infrastructure;

namespace LayerPeek.App.UiTests.Tray;

/// <summary>
/// Tray icons of design B "Layers" (T-025) drawn by Skia at every notification-area size: snapshot references in
/// <c>Snapshots/TrayIcons</c> (one per state and size) and the rules of the design checked on the pixels.
/// </summary>
public sealed class TrayIconRendererTests(HeadlessSession session)
{
    /// <summary>Every status plus the progress values worth a look (empty, first step, partial layers, full).</summary>
    public static TheoryData<string, TrayStatus, int, int> Cases()
    {
        (string Name, TrayStatus Status, int Progress)[] states =
        [
            ("idle", TrayStatus.Idle, 0),
            ("busy", TrayStatus.Busy, 0),
            ("printing-0", TrayStatus.Printing, 0),
            ("printing-5", TrayStatus.Printing, 5),
            ("printing-10", TrayStatus.Printing, 10),
            ("printing-35", TrayStatus.Printing, 35),
            ("printing-50", TrayStatus.Printing, 50),
            ("printing-70", TrayStatus.Printing, 70),
            ("printing-95", TrayStatus.Printing, 95),
            ("printing-100", TrayStatus.Printing, 100),
            ("paused-50", TrayStatus.Paused, 50),
            ("finished", TrayStatus.Finished, 0),
            ("attention", TrayStatus.Attention, 0),
            ("error", TrayStatus.Error, 0),
            ("offline", TrayStatus.Offline, 0),
        ];
        TheoryData<string, TrayStatus, int, int> data = [];
        foreach ((string name, TrayStatus status, int progress) in states)
        {
            foreach (int size in TrayIconLayout.Sizes)
            {
                data.Add(name, status, progress, size);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task IconMatchesReference(string name, TrayStatus status, int progress, int size) =>
        session.RunAsync(_ =>
        {
            using RenderTargetBitmap bitmap = Render(status, progress, size);

            Assert.Equal(new PixelSize(size, size), bitmap.PixelSize);
            SnapshotAssert.Matches(bitmap, $"TrayIcons/{name}-{size}");
            return Task.CompletedTask;
        });

    [Fact]
    public Task SixteenPixelIconsHaveNoBlurredPixels() =>
        session.RunAsync(_ =>
        {
            // Pixel-aligned geometry: every pixel is transparent, a track pixel or fully opaque (no anti-aliasing).
            byte[] allowed = [0, ColorTrayIconPalette.Track.A, ColorTrayIconPalette.OfflineTrack.A, 255];
            foreach (TrayStatus status in Enum.GetValues<TrayStatus>())
            {
                IconPixels pixels = Pixels(status, 35, 16);
                foreach ((int x, int y, Color color) in pixels.All())
                {
                    Assert.True(allowed.Any(a => Math.Abs(a - color.A) <= 1), $"{status}: pixel ({x}, {y}) has alpha {color.A}.");
                }
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task MotifIsSharpAtEverySize() =>
        session.RunAsync(_ =>
        {
            // Outside the badge the hotend and the layers sit on whole pixels at every size, not only at 16 px.
            byte[] allowed = [0, ColorTrayIconPalette.Track.A, ColorTrayIconPalette.OfflineTrack.A, 255];
            foreach (int size in TrayIconLayout.Sizes)
            {
                Rect badge = TrayIconLayout.For(size).BadgeClearance;
                foreach (TrayStatus status in Enum.GetValues<TrayStatus>())
                {
                    foreach ((int x, int y, Color color) in Pixels(status, 35, size).All())
                    {
                        if (!badge.Intersects(new Rect(x, y, 1, 1)))
                        {
                            Assert.True(allowed.Any(a => Math.Abs(a - color.A) <= 1), $"{status} at {size} px: pixel ({x}, {y}) has alpha {color.A}.");
                        }
                    }
                }
            }

            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(TrayStatus.Printing, 0)]
    [InlineData(TrayStatus.Printing, 5)]
    [InlineData(TrayStatus.Printing, 35)]
    [InlineData(TrayStatus.Printing, 50)]
    [InlineData(TrayStatus.Printing, 70)]
    [InlineData(TrayStatus.Printing, 100)]
    [InlineData(TrayStatus.Paused, 50)]
    public Task ProgressFillsTheBottomLayerFirstFromTheLeft(TrayStatus status, int progress) =>
        session.RunAsync(_ =>
        {
            TrayIconAppearance appearance = ColorTrayIconPalette.GetAppearance(TrayIconKey.For(status, progress));
            foreach (int size in TrayIconLayout.Sizes)
            {
                TrayIconLayout layout = TrayIconLayout.For(size);
                IconPixels pixels = Pixels(status, progress, size);
                int[] filled = layout.FilledPixels(progress / 100.0);
                Assert.Equal((int)Math.Floor(3 * layout.Layers[0].Width * progress / 100.0), filled.Sum());
                for (int i = 0; i < layout.Layers.Count; i++)
                {
                    PixelRect layer = layout.Layers[i];
                    for (int x = layer.X; x < layer.Right; x++)
                    {
                        Color expected = x - layer.X < filled[i] ? appearance.Fill : appearance.Track;
                        Color actual = pixels[x, layer.Y + (layer.Height / 2)];
                        Assert.True(IconPixels.Same(expected, actual), $"{status} {progress} % at {size} px, layer {i}, x {x}: expected {expected}, was {actual}.");
                    }
                }
            }

            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(TrayStatus.Finished)]
    [InlineData(TrayStatus.Error)]
    public Task FinishedAndErrorColourEveryLayer(TrayStatus status) =>
        session.RunAsync(_ =>
        {
            Color fill = ColorTrayIconPalette.GetAppearance(TrayIconKey.For(status, null)).Fill;
            foreach (int size in TrayIconLayout.Sizes)
            {
                IconPixels pixels = Pixels(status, 0, size);
                foreach (PixelRect layer in TrayIconLayout.For(size).Layers)
                {
                    Assert.True(IconPixels.Same(fill, pixels[layer.X, layer.Y]), $"{status} at {size} px.");
                    Assert.True(IconPixels.Same(fill, pixels[layer.Right - 1, layer.Bottom - 1]), $"{status} at {size} px.");
                }
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task OnlyIdleAndPrintingHaveNoBadge() =>
        session.RunAsync(_ =>
        {
            foreach (int size in TrayIconLayout.Sizes)
            {
                Rect badge = TrayIconLayout.For(size).BadgeClearance;
                foreach (TrayStatus status in Enum.GetValues<TrayStatus>())
                {
                    TrayIconAppearance appearance = ColorTrayIconPalette.GetAppearance(TrayIconKey.For(status, 50));
                    List<Color> inBadge = [.. Pixels(status, 50, size).All()
                        .Where(p => badge.Intersects(new Rect(p.X, p.Y, 1, 1)) && p.Color.A > 0)
                        .Select(p => p.Color)];
                    if (status is TrayStatus.Idle or TrayStatus.Printing)
                    {
                        Assert.True(inBadge.Count == 0, $"{status} at {size} px draws in the badge area.");
                    }
                    else
                    {
                        Assert.True(inBadge.Any(c => IconPixels.Near(appearance.BadgeColors.Fill, c, size == 16 ? 2 : 48)), $"{status} at {size} px: no badge fill.");
                        Assert.True(inBadge.Any(c => IconPixels.Near(appearance.BadgeColors.Outline, c, size == 16 ? 2 : 48)), $"{status} at {size} px: no badge outline.");
                        Assert.True(inBadge.Any(c => IconPixels.Near(appearance.BadgeColors.Symbol, c, size == 16 ? 2 : 48)), $"{status} at {size} px: no badge symbol.");
                    }
                }
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task HotendIsOrangeExceptPrintingAndOffline() =>
        session.RunAsync(_ =>
        {
            foreach (int size in TrayIconLayout.Sizes)
            {
                PixelRect block = TrayIconLayout.For(size).Block;
                foreach (TrayStatus status in Enum.GetValues<TrayStatus>())
                {
                    Color expected = status switch
                    {
                        TrayStatus.Offline => ColorTrayIconPalette.Grey,
                        TrayStatus.Printing => ColorTrayIconPalette.GetAppearance(TrayIconKey.For(status, 0)).Fill,
                        _ => ColorTrayIconPalette.Orange,
                    };
                    Color actual = Pixels(status, 0, size)[block.X + (block.Width / 2), block.Y + (block.Height / 2)];
                    Assert.True(IconPixels.Same(expected, actual), $"{status} at {size} px: hotend {actual}.");
                }
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task PrintingWithoutVisibleProgressDiffersFromIdle() =>
        session.RunAsync(_ =>
        {
            // 0-4 % fills no pixel and printing has no badge: the blue hotend alone tells it from idle (2026-10-19).
            foreach (int size in TrayIconLayout.Sizes)
            {
                foreach (int progress in (int[])[0, 4])
                {
                    IconPixels idle = Pixels(TrayStatus.Idle, 0, size);
                    IconPixels printing = Pixels(TrayStatus.Printing, progress, size);
                    int different = idle.All().Count(p => !IconPixels.Same(p.Color, printing[p.X, p.Y]));
                    Assert.True(different > 0, $"Printing {progress} % looks like idle at {size} px.");
                }
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task IcoHoldsOneImagePerTraySizeLargestFirst() =>
        session.RunAsync(_ =>
        {
            byte[] ico = TrayIconRenderer.RenderIco(ColorTrayIconPalette.GetAppearance(TrayIconKey.For(TrayStatus.Printing, 35)));

            IReadOnlyList<IcoEntry> entries = IcoEntry.ReadAll(ico);
            Assert.Equal([32, 24, 20, 16], entries.Select(static e => e.Size));
            foreach (IcoEntry entry in entries)
            {
                Assert.True(entry.IsPng);
                using MemoryStream png = new(entry.Data(ico));
                using Bitmap image = new(png);
                Assert.Equal(new PixelSize(entry.Size, entry.Size), image.PixelSize);
            }

            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task IconSetRendersEveryKeyOnce(bool multiSize) =>
        session.RunAsync(_ =>
        {
            using ColorTrayIconSet icons = new(multiSize);
            TrayIconKey key = TrayIconKey.For(TrayStatus.Paused, 42);

            Assert.Same(icons.GetIcon(key), icons.GetIcon(TrayIconKey.For(TrayStatus.Paused, 44)));
            Assert.NotSame(icons.GetIcon(key), icons.GetIcon(TrayIconKey.For(TrayStatus.Paused, 45)));
            return Task.CompletedTask;
        });

    private static RenderTargetBitmap Render(TrayStatus status, int progress, int size) =>
        TrayIconRenderer.RenderBitmap(ColorTrayIconPalette.GetAppearance(TrayIconKey.For(status, progress)), size);

    private static IconPixels Pixels(TrayStatus status, int progress, int size)
    {
        using RenderTargetBitmap bitmap = Render(status, progress, size);
        return IconPixels.Read(bitmap);
    }
}
