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
            // Exact list: a new asset must be added here. The SVG sources and the images of the macOS .icns stay out of the binary;
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
    /// T-019 (user decision 2026-10-29): the 16 and 32 px images of the macOS <c>.icns</c> (<c>app-icon-16.png</c>,
    /// <c>app-icon-32.png</c>, not bundled) are the simplified variant <c>app-icon-small.svg</c>, pixel for pixel the
    /// images of <c>app-icon.ico</c>. Regenerated together with the <c>.ico</c> when the SVG changes.
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public Task MacIconSmallSizesAreThePixelsOfTheAppIcon(int size) =>
        session.RunAsync(_ =>
        {
            byte[] ico = ReadAppIcon();
            IcoEntry entry = Assert.Single(IcoEntry.ReadAll(ico), e => e.Size == size);
            Assert.False(entry.IsPng);
            byte[] dib = entry.Data(ico);
            int header = BitConverter.ToInt32(dib, 0);

            using Bitmap png = new(Path.Combine(TestWorktree.Root, "src", "LayerPeek.App", "Assets", "AppIcon", $"app-icon-{size}.png"));
            Assert.Equal(new PixelSize(size, size), png.PixelSize);
            IconPixels actual = IconPixels.Read(png);
            List<string> different = [];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 32-bit BMP of the .ico: straight BGRA, bottom-up.
                    int at = header + ((((size - 1 - y) * size) + x) * 4);
                    Avalonia.Media.Color expected = Avalonia.Media.Color.FromArgb(dib[at + 3], dib[at + 2], dib[at + 1], dib[at]);
                    Avalonia.Media.Color pixel = actual[x, y];
                    // Colours of nearly transparent pixels lose precision in the premultiplied decoder.
                    bool same = Math.Abs(expected.A - pixel.A) <= 1 && (expected.A < 128 || IconPixels.Near(expected, pixel, 2));
                    if (!same)
                    {
                        different.Add($"({x}, {y}): {expected} vs {pixel}");
                    }
                }
            }

            Assert.True(different.Count == 0, string.Join(Environment.NewLine, different));
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
