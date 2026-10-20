using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.App.Tests.Printers;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.UiTests.Printers;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.UiTests.Tray;

/// <summary>
/// Printer icons in the tray menu (T-039, Windows): the real <see cref="NativeMenuItem.Icon"/> gets a bitmap with 16 DIP
/// worth of pixels at the system scaling, and Avalonia's Windows tray menu (a menu flyout presenter, like the one
/// <c>Win32 TrayIconImpl</c> opens) fits it into 16 × 16 DIP. Whether Windows shows it in the real notification-area
/// menu stays a manual check.
/// </summary>
public sealed class TrayMenuIconUiTests(HeadlessSession session)
{
    private static readonly PrinterModelCode Mk4SCode = new(1, 4, 1);

    [Theory]
    [InlineData(1.0, 16)]
    [InlineData(1.25, 20)]
    [InlineData(1.5, 24)]
    [InlineData(2.0, 32)]
    public Task PrinterItemsGetAnIconForTheSystemScaling(double scaling, int pixels) =>
        session.RunAsync(scope =>
        {
            (NativeMenu menu, TrayMenuViewModel viewModel, PrinterMenuIcons icons) = Create(scope, scaling);

            viewModel.SetPrinters([TestPrinters.Definition("a", "Alpha") with { DetectedModel = Mk4SCode }, TestPrinters.Definition("b", "Beta")]);
            Dispatcher.UIThread.RunJobs();

            NativeMenuItem[] printers = [.. menu.Items.OfType<NativeMenuItem>().Take(2)];
            Assert.All(printers, item => Assert.Equal(new PixelSize(pixels, pixels), Assert.IsType<Bitmap>(item.Icon, exactMatch: false).PixelSize));
            Assert.Same(PrinterBitmaps.Illustration("mk4", pixels), printers[0].Icon);
            Assert.Same(PrinterBitmaps.Illustration("generic", pixels), printers[1].Icon);
            // Submenu items and the fixed items have none.
            Assert.All(printers[0].Menu!.Items.OfType<NativeMenuItem>(), static item => Assert.Null(item.Icon));
            Assert.All(menu.Items.OfType<NativeMenuItem>().Skip(2), static item => Assert.Null(item.Icon));
            Assert.Equal(pixels, icons.PixelSize);
            return Task.CompletedTask;
        });

    [Fact]
    public Task CustomPictureBecomesASquareIcon() =>
        session.RunAsync(static scope =>
        {
            CustomPrinterImages images = new();
            (NativeMenu menu, TrayMenuViewModel viewModel, _) = Create(scope, 1.5, images);
            viewModel.SetPrinters([TestPrinters.Definition("a", "Alpha") with { Image = PrinterImageChoice.Custom }]);
            Dispatcher.UIThread.RunJobs();

            images.Set("a", UiTestImages.Png(64, 32, 0xFFFF0000));
            Dispatcher.UIThread.RunJobs();

            Bitmap icon = Assert.IsType<Bitmap>(menu.Items.OfType<NativeMenuItem>().First().Icon, exactMatch: false);
            Assert.Equal(new PixelSize(24, 24), icon.PixelSize);
            return Task.CompletedTask;
        });

    [Fact]
    public Task ReplacedCustomPictureReleasesItsBitmap() =>
        session.RunAsync(static _ =>
        {
            using PrinterMenuIcons icons = new(1.0);
            PrinterPicture first = Custom(UiTestImages.Png(16, 16, 0xFFFF0000));
            PrinterPicture second = Custom(UiTestImages.Png(16, 16, 0xFF0000FF));

            Bitmap firstBitmap = icons.Get(first, "a");
            Assert.Same(firstBitmap, icons.Get(first, "A"));
            Bitmap secondBitmap = icons.Get(second, "a");
            icons.Get(Custom(UiTestImages.Png(16, 16, 0xFF00FF00)), "b");

            // One bitmap per printer: the first picture of "a" is gone, "b" has its own.
            Assert.NotSame(firstBitmap, secondBitmap);
            Assert.Equal(2, icons.CustomBitmapCount);

            // Back to an illustration: the custom bitmap of "a" goes too.
            icons.Get(PrinterPicture.Generic, "a");
            Assert.Equal(1, icons.CustomBitmapCount);
            return Task.CompletedTask;
        });

    [Fact]
    public Task DamagedCustomPictureShowsTheAutomaticIllustration() =>
        session.RunAsync(static _ =>
        {
            using PrinterMenuIcons icons = new(1.5);
            PrinterPicture damaged = PrinterPicture.For(PrinterImageChoice.Custom, ConnectionType.PrusaLink, Mk4SCode, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

            Assert.Same(PrinterBitmaps.Illustration("mk4", 24), icons.Get(damaged, "a"));
            return Task.CompletedTask;
        });

    [Fact]
    public Task WithoutIconsTheItemsHaveNone() =>
        session.RunAsync(static scope =>
        {
            TrayIcon tray = Assert.Single(TrayIcon.GetIcons(Application.Current!)!);
            TrayMenuViewModel viewModel = scope.Defer(new TrayMenuViewModel(new FakePrinterMonitor(), new AvaloniaUiDispatcher(), TestWebOpener.Create(new FakeUrlLauncher()), TrayMenuOptions.Default));
            scope.Defer(new TrayMenuPresenter(new TrayMenuTarget(new AvaloniaNativeTrayMenu(tray)), viewModel, new FakeAppCommands(), NullLogger.Instance));
            viewModel.SetPrinters([TestPrinters.Definition("a", "Alpha") with { DetectedModel = Mk4SCode }]);
            Dispatcher.UIThread.RunJobs();

            Assert.All(tray.Menu!.Items.OfType<NativeMenuItem>(), static item => Assert.Null(item.Icon));
            return Task.CompletedTask;
        });

    [Fact]
    public Task WindowsTrayMenuFitsTheIconInto16Dip() =>
        session.RunAsync(static scope =>
        {
            // What Avalonia's Win32 tray menu builds from a NativeMenuItem with an icon (NativeMenuBarPresenter): a
            // MenuItem whose Icon is an Image of the bitmap, inside a MenuFlyoutPresenter.
            using PrinterMenuIcons icons = new(2.0);
            Bitmap bitmap = icons.Get(PrinterPicture.FromImage(new PrinterImage(PrinterImageKind.Model, PrinterModel.Mk4S)), "a");
            MenuItem item = new() { Header = "Alpha: Idle", Icon = new Image { Source = bitmap } };
            Window window = scope.Track(new Window { Width = 300, Height = 200, Content = new MenuFlyoutPresenter { ItemsSource = new[] { item } } });
            window.Show();
            window.Settle();

            ContentControl presenter = item.GetVisualDescendants().OfType<ContentControl>().Single(static control => control.Name == "PART_IconPresenter");

            Assert.True(presenter.IsVisible);
            Assert.Equal(new Size(16, 16), presenter.Bounds.Size);
            Assert.Equal(new PixelSize(32, 32), bitmap.PixelSize);
            return Task.CompletedTask;
        });

    private static PrinterPicture Custom(byte[] png) => PrinterPicture.For(PrinterImageChoice.Custom, ConnectionType.PrusaLink, null, png);

    private static (NativeMenu Menu, TrayMenuViewModel ViewModel, PrinterMenuIcons Icons) Create(UiTestScope scope, double scaling, CustomPrinterImages? images = null)
    {
        TrayIcon tray = Assert.Single(TrayIcon.GetIcons(Application.Current!)!);
        PrinterMenuIcons icons = new(scaling);
        TrayMenuViewModel viewModel = scope.Defer(new TrayMenuViewModel(
            new FakePrinterMonitor(),
            new AvaloniaUiDispatcher(),
            TestWebOpener.Create(new FakeUrlLauncher()),
            TrayMenuOptions.Default with { PrinterIcons = true },
            images ?? new CustomPrinterImages()));
        // Disposed after the presenter (reverse order), like TrayHost does it.
        scope.Defer(icons);
        scope.Defer(new TrayMenuPresenter(new TrayMenuTarget(new AvaloniaNativeTrayMenu(tray, icons)), viewModel, new FakeAppCommands(), NullLogger.Instance));
        Dispatcher.UIThread.RunJobs();
        return (tray.Menu!, viewModel, icons);
    }
}
