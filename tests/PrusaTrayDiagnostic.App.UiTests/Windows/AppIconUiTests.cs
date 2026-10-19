using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;

namespace PrusaTrayDiagnostic.App.UiTests.Windows;

/// <summary>The application icon (T-025): the asset, its sizes and that every window and the static tray icon use it.</summary>
public sealed class AppIconUiTests(HeadlessSession session)
{
    /// <summary>16–32 px: title bar, notification area and taskbar at 100–200 %; 48–256 px: Explorer and Alt+Tab.</summary>
    private static readonly int[] RequiredSizes = [256, 64, 48, 32, 24, 20, 16];

    [Fact]
    public Task OnlyExpectedAssetsAreBundled() =>
        session.RunAsync(_ =>
        {
            // Exact list: a new asset must be added here. The SVG sources and the macOS master stay out of the binary;
            // the old tray-idle.png is gone.
            Uri[] expected = [AppIcon.AssetUri];

            Assert.Equal(expected, BundledAssets());
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
        [.. AssetLoader.GetAssets(new Uri("avares://PrusaTrayDiagnostic/Assets/"), null)];

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
