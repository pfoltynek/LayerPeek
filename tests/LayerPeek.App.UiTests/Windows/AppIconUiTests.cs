using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using LayerPeek.App.Printers;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.UiTests.Tray;
using LayerPeek.Tests.Shared;
using LayerPeek.App.Windows;

namespace LayerPeek.App.UiTests.Windows;

/// <summary>The application icon (T-025): the asset, its sizes and that every window and the static tray icon use it.</summary>
public sealed class AppIconUiTests(HeadlessSession session)
{
    /// <summary>16–32 px: title bar, notification area and taskbar at 100–200 %; 48–256 px: Explorer and Alt+Tab.</summary>
    private static readonly int[] RequiredSizes = [256, 64, 48, 32, 24, 20, 16];

    [Fact]
    public Task OnlyExpectedAssetsAreBundled() =>
        session.RunAsync(_ =>
        {
            // Exact list: a new asset must be added here. The SVG sources and the images of the macOS .icns (app-icon-macos-*.png,
            // T-098) stay out of the binary;
            // the old tray-idle.png is gone. T-039: the printer illustrations, one PNG per illustration and size.
            Uri[] expected =
            [
                AppIcon.AssetUri,
                .. PrinterIllustrations.Ids.SelectMany(static id => PrinterIllustrations.PixelSizes.Select(size => PrinterIllustrations.AssetUri(id, size))),
            ];

            Assert.Equal(expected.Select(static uri => uri.OriginalString).Order(StringComparer.Ordinal), BundledAssets().Select(static uri => uri.OriginalString).Order(StringComparer.Ordinal));
            Assert.Equal(56, expected.Length);
            return Task.CompletedTask;
        });

    [Fact]
    public Task EveryAssetLoads() =>
        session.RunAsync(_ =>
        {
            Uri[] assets = BundledAssets();

            Assert.NotEmpty(assets);
            foreach (Uri asset in assets)
            {
                using Stream stream = AssetLoader.Open(asset);
                Assert.True(stream.Length > 0, $"{asset} is empty.");
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task AppIconHoldsEveryRequiredSize() =>
        session.RunAsync(_ =>
        {
            byte[] ico = ReadAppIcon();

            IReadOnlyList<IcoEntry> entries = IcoEntry.ReadAll(ico);

            Assert.Equal(RequiredSizes, entries.Select(static e => e.Size));
            Assert.All(entries, static e => Assert.Equal(32, e.BitCount));
            IcoEntry large = entries[0];
            Assert.True(large.IsPng, "The 256 px image is PNG-compressed, as Windows expects.");
            using MemoryStream png = new(large.Data(ico));
            using Bitmap image = new(png);
            Assert.Equal(new PixelSize(256, 256), image.PixelSize);
            return Task.CompletedTask;
        });

    /// <summary>
    /// T-098: the images of the macOS <c>.icns</c> (<c>app-icon-macos-*.png</c>, not bundled) follow Apple's app icon grid:
    /// the body is 824 of 1024 px centred (12 px at 16 px and 26 px at 32 px on whole pixels), the margin around it is
    /// transparent, at 1024 px it only holds the drop shadow. The Windows <c>.ico</c> keeps the full-size tile (T-025).
    /// </summary>
    [Theory]
    [InlineData(16, 2, 14, 0)]
    [InlineData(32, 3, 29, 0)]
    [InlineData(1024, 100, 924, 64)]
    public Task MacIconImagesFollowTheAppleGrid(int size, int bodyStart, int bodyEnd, int maxMarginAlpha) =>
        session.RunAsync(_ =>
        {
            using Bitmap png = new(MacIconPath(size));
            Assert.Equal(new PixelSize(size, size), png.PixelSize);
            IconPixels pixels = IconPixels.Read(png);

            List<(int X, int Y, Avalonia.Media.Color Color)> body = [.. pixels.All().Where(static p => p.Color.A >= 128)];
            Assert.Equal(bodyStart, body.Min(static p => p.X));
            Assert.Equal(bodyStart, body.Min(static p => p.Y));
            Assert.Equal(bodyEnd - 1, body.Max(static p => p.X));
            Assert.Equal(bodyEnd - 1, body.Max(static p => p.Y));
            Assert.Equal(255, pixels[size / 2, size / 2].A);
            Assert.Equal(255, pixels[bodyStart, size / 2].A);
            Assert.Equal(255, pixels[size / 2, bodyEnd - 1].A);

            // One pixel around the body is its anti-aliased edge (at 1024 px); everything beyond is margin.
            int edge = maxMarginAlpha == 0 ? 0 : 1;
            int margin = pixels.All()
                .Where(p => p.X < bodyStart - edge || p.X >= bodyEnd + edge || p.Y < bodyStart - edge || p.Y >= bodyEnd + edge)
                .Max(static p => (int)p.Color.A);
            Assert.True(margin <= maxMarginAlpha, $"The margin has alpha {margin}.");
            if (maxMarginAlpha > 0)
            {
                // The drop shadow: below the body, darker than above it (offset down), and nothing in the corners of the canvas.
                Assert.True(pixels[size / 2, bodyEnd + 4].A > pixels[size / 2, bodyStart - 5].A);
                Assert.Equal(0, pixels[0, 0].A);
                Assert.Equal(0, pixels[size - 1, size - 1].A);
            }

            return Task.CompletedTask;
        });

    /// <summary>
    /// T-098 (T-019: 16 and 32 px from the simplified design of T-025): the small macOS images are drawn on whole pixels in
    /// the colours of the app icon – rim, tile, print head and two full layers – so they stay sharp in Finder lists and menus.
    /// </summary>
    [Theory]
    [InlineData(16, 2, new[] { 3, 4, 5, 6 }, new[] { 7, 10 }, 4, 12, 2, 8, 11)]
    [InlineData(32, 3, new[] { 6, 7, 9, 10, 11, 12, 13 }, new[] { 8, 14, 21, 22 }, 6, 26, 3, 18, 23)]
    public Task MacIconSmallSizesAreTheSimplifiedDesignOnWholePixels(int size, int rim, int[] headRows, int[] gapRows, int layerLeft, int layerRight, int layerHeight, int upperTop, int lowerTop) =>
        session.RunAsync(_ =>
        {
            using Bitmap png = new(MacIconPath(size));
            IconPixels pixels = IconPixels.Read(png);
            Avalonia.Media.Color rimColour = Avalonia.Media.Color.Parse("#4A4C55");
            Avalonia.Media.Color tile = Avalonia.Media.Color.Parse("#2B2C33");
            Avalonia.Media.Color head = Avalonia.Media.Color.Parse("#F4F4F6");
            Avalonia.Media.Color layer = Avalonia.Media.Color.Parse("#FD5000");
            int middle = size / 2;

            Assert.True(IconPixels.Same(rimColour, pixels[rim, middle]), $"Rim at ({rim}, {middle}): {pixels[rim, middle]}");
            Assert.True(IconPixels.Same(rimColour, pixels[middle, size - 1 - rim]), $"Rim at ({middle}, {size - 1 - rim})");
            Assert.True(IconPixels.Same(tile, pixels[rim + 1, middle - 1]), $"Tile at ({rim + 1}, {middle - 1}): {pixels[rim + 1, middle - 1]}");
            foreach (int y in headRows)
            {
                Assert.True(IconPixels.Same(head, pixels[middle, y]), $"Print head at ({middle}, {y}): {pixels[middle, y]}");
                Assert.True(IconPixels.Same(head, pixels[middle - 1, y]), $"Print head at ({middle - 1}, {y}): {pixels[middle - 1, y]}");
            }

            foreach (int y in gapRows)
            {
                Assert.True(IconPixels.Same(tile, pixels[middle, y]), $"Gap at ({middle}, {y}): {pixels[middle, y]}");
            }

            foreach (int top in new[] { upperTop, lowerTop })
            {
                for (int y = top; y < top + layerHeight; y++)
                {
                    for (int x = layerLeft; x < layerRight; x++)
                    {
                        Assert.True(IconPixels.Same(layer, pixels[x, y]), $"Layer at ({x}, {y}): {pixels[x, y]}");
                    }
                }

                // Sharp edges: the tile right next to the layer (the lower corners of the body are anti-aliased, so the top row).
                Assert.True(IconPixels.Same(tile, pixels[layerLeft - 1, top]), $"Left end of the layer at ({layerLeft - 1}, {top})");
                Assert.True(IconPixels.Same(tile, pixels[layerRight, top]), $"Right end of the layer at ({layerRight}, {top})");
                Assert.True(IconPixels.Same(tile, pixels[middle, top - 1]), $"Above the layer at ({middle}, {top - 1})");
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task EveryWindowUsesTheAppIcon() =>
        session.RunAsync(scope =>
        {
            Type[] windows = WindowTypes();
            Assert.Superset(
                new HashSet<Type> { typeof(AboutWindow), typeof(CameraWindow), typeof(DiagnosticsWindow), typeof(LicensesWindow), typeof(SettingsWindow) },
                windows.ToHashSet());

            foreach (Type type in windows)
            {
                Window window = scope.Track(Create(type));
                window.Show();

                Assert.True(ReferenceEquals(AppIcon.Window, window.Icon), $"{type.Name} does not use the app icon.");
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task StaticTrayIconIsTheAppIcon() =>
        session.RunAsync(_ =>
        {
            TrayIcon tray = Assert.Single(TrayIcon.GetIcons(Application.Current!)!);

            Assert.Same(AppIcon.Window, tray.Icon);
            return Task.CompletedTask;
        });

    private static Uri[] BundledAssets() =>
        [.. AssetLoader.GetAssets(new Uri("avares://LayerPeek/Assets/"), null)];

    private static string MacIconPath(int size) =>
        Path.Combine(TestWorktree.Root, "src", "LayerPeek.App", "Assets", "AppIcon", $"app-icon-macos-{size}.png");

    private static byte[] ReadAppIcon()
    {
        using Stream stream = AssetLoader.Open(AppIcon.AssetUri);
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Every window class of the app, so that a new window cannot be added without the icon.</summary>
    private static Type[] WindowTypes() =>
        [.. typeof(App).Assembly.GetTypes().Where(static t => t.IsSubclassOf(typeof(Window)) && !t.IsAbstract)];

    private static Window Create(Type type) => (Window)Activator.CreateInstance(type)!;
}
