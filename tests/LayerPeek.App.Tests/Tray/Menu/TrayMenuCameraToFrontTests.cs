using System.Globalization;

using LayerPeek.App.Tray.Menu;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Tray.Menu;

/// <summary>
/// "Show camera" for an open camera window (T-102): right above "Hide camera" while the window is open, with
/// <see cref="TrayMenuOptions.CameraToFront"/>, which the app turns on for macOS and Windows alike (user decision
/// 2026-10-29: the same menu on both); clicking it brings the window to the front. Without the option the menu of T-040.
/// </summary>
public sealed class TrayMenuCameraToFrontTests : IDisposable
{
    private static readonly TrayMenuOptions AppOptions = TrayMenuOptions.ForCurrentPlatform;

    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeMenuTarget _target = new();
    private readonly FakeAppCommands _commands = new();
    private readonly ListLogger<TrayMenuCameraToFrontTests> _logger = new();

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    public void Dispose() => _culture.Dispose();

    private static PrinterDefinition WithCamera(PrinterDefinition printer) =>
        printer with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) };

    private TrayMenuViewModel Create(TrayMenuOptions options, params PrinterDefinition[] printers)
    {
        TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(new FakeUrlLauncher()), options);
        viewModel.SetPrinters(printers);
        _dispatcher.RunAll();
        return viewModel;
    }

    [Fact]
    public void EveryPlatformOffersTheItem()
    {
        // Both OSes (2026-10-29); only the bare defaults of the tests leave it out.
        Assert.True(TrayMenuOptions.ForCurrentPlatform.CameraToFront);
        Assert.False(TrayMenuOptions.Default.CameraToFront);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothPlatformMenusAddShowCameraAboveHideCamera(bool printerIcons)
    {
        // The Windows menu (with printer icons, T-039) and the macOS menu (without) get the same submenu.
        using TrayMenuViewModel viewModel = Create(AppOptions with { PrinterIcons = printerIcons }, WithCamera(Definition()));

        viewModel.SetCameraWindowOpen("mk4", isOpen: true);

        Assert.Equal(
            [TrayMenuCommand.OpenWeb, TrayMenuCommand.CameraToFront, TrayMenuCommand.Camera],
            viewModel.PrinterItems[0].Submenu!.Select(static item => item.Command));
        Assert.All(viewModel.PrinterItems[0].Submenu!, static item => Assert.Null(item.Icon));
    }

    [Fact]
    public void OpenWindowAddsShowCameraAboveHideCameraUntilItCloses()
    {
        using TrayMenuViewModel viewModel = Create(AppOptions, WithCamera(Definition()));
        Assert.Equal([TrayMenuCommand.OpenWeb, TrayMenuCommand.Camera], viewModel.PrinterItems[0].Submenu!.Select(static item => item.Command));

        viewModel.SetCameraWindowOpen("mk4", isOpen: true);

        Assert.Collection(
            viewModel.PrinterItems[0].Submenu!,
            web => Assert.Equal(TrayMenuCommand.OpenWeb, web.Command),
            front =>
            {
                Assert.Equal((TrayMenuCommand.CameraToFront, "_Show camera", "mk4"), (front.Command, front.Header, front.PrinterId));
                Assert.True(front.HasAccessKey);
                Assert.True(front.IsEnabled);
            },
            hide => Assert.Equal((TrayMenuCommand.Camera, "Hide _camera"), (hide.Command, hide.Header)));

        viewModel.SetCameraWindowOpen("mk4", isOpen: false);
        Assert.Equal(["_Open PrusaLink", "Show _camera"], viewModel.PrinterItems[0].Submenu!.Select(static item => item.Header));
    }

    [Fact]
    public void WithoutTheOptionTheMenuOfT040Stays()
    {
        using TrayMenuViewModel viewModel = Create(TrayMenuOptions.Default with { PrinterIcons = true }, WithCamera(Definition()));

        viewModel.SetCameraWindowOpen("mk4", isOpen: true);

        Assert.Equal([TrayMenuCommand.OpenWeb, TrayMenuCommand.Camera], viewModel.PrinterItems[0].Submenu!.Select(static item => item.Command));
    }

    [Fact]
    public void OnlyThePrinterWithTheOpenWindowGetsTheItem()
    {
        using TrayMenuViewModel viewModel = Create(AppOptions, WithCamera(Definition("a", "Alpha")), WithCamera(Definition("b", "Beta")), Definition("c", "NoCamera"));

        viewModel.SetCameraWindowOpen("b", isOpen: true);
        // A window report for a printer without camera item changes nothing.
        viewModel.SetCameraWindowOpen("c", isOpen: true);

        Assert.Equal([2, 3, 1], viewModel.PrinterItems.Select(static item => item.Submenu!.Count));
        Assert.Equal("b", viewModel.PrinterItems[1].Submenu![1].PrinterId);
    }

    [Fact]
    public void ClickBringsTheWindowToFrontAndHideCameraStillCloses()
    {
        using TrayMenuViewModel viewModel = Create(AppOptions, WithCamera(Definition()));
        using TrayMenuPresenter presenter = new(_target, viewModel, _commands, _logger);
        viewModel.SetCameraWindowOpen("mk4", isOpen: true);

        _target.Click(_target.Printers[0].Submenu![1]);
        _target.Click(_target.Printers[0].Submenu![^1]);

        Assert.Equal(["mk4"], _commands.CameraToFrontRequests);
        Assert.Equal(["mk4"], _commands.CameraRequests);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void ItemWithoutPrinterDoesNothing()
    {
        using TrayMenuViewModel viewModel = Create(AppOptions);
        using TrayMenuPresenter presenter = new(_target, viewModel, _commands, _logger);

        _target.Click(new TrayMenuEntry(TrayMenuCommand.CameraToFront, "Show camera", IsEnabled: true));

        Assert.Empty(_commands.CameraToFrontRequests);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void SubmenuWithTheItemHasUniqueAccessKeysAndTheCameraItemKeepsItsKey(string language)
    {
        using CultureScope culture = new(language);
        PrinterDefinition connect = WithCamera(new PrinterDefinition { Id = "c", Name = "Connect", ConnectionType = ConnectionType.PrusaConnect, Address = "uuid" });
        using TrayMenuViewModel viewModel = Create(AppOptions, WithCamera(Definition("a", "Alpha")), connect);
        char?[] closedKeys = [.. viewModel.PrinterItems.Select(static printer => AccessKey(printer.Submenu![^1]))];

        viewModel.SetCameraWindowOpen("a", isOpen: true);
        viewModel.SetCameraWindowOpen("c", isOpen: true);

        CultureInfo cultureInfo = CultureInfo.GetCultureInfo(language);
        for (int i = 0; i < viewModel.PrinterItems.Count; i++)
        {
            TrayMenuEntryList submenu = viewModel.PrinterItems[i].Submenu!;
            Assert.Equal(TrayMenuCommand.CameraToFront, submenu[1].Command);
            List<string> keys = [];
            foreach (TrayMenuEntry item in submenu)
            {
                Assert.True(item.HasAccessKey, $"{language}: '{item.Header}' has no access key.");
                string native = NativeMenuText.HeaderOf(item);
                Assert.Equal(1, native.Count(static c => c == '_'));
                char key = Assert.NotNull(NativeMenuText.AccessKeyOf(native));
                Assert.True(char.IsLetterOrDigit(key), $"{language}: '{item.Header}' has the access key '{key}'.");
                keys.Add(char.ToUpper(key, cultureInfo).ToString());
            }

            Assert.True(keys.Distinct(StringComparer.Ordinal).Count() == keys.Count, $"{language}: access keys {string.Join(", ", keys)} are not unique.");
            // "Hide camera" keeps the key of "Show camera" (T-042), the new item has its own.
            Assert.Equal(closedKeys[i], AccessKey(submenu[^1]));
            // Same wording as "Show camera" of a closed window, only the access key moved.
            Assert.Equal(
                NativeMenuText.DisplayTextOf(LayerPeek.App.Resources.Strings.MenuShowCamera),
                NativeMenuText.DisplayTextOf(NativeMenuText.HeaderOf(submenu[1])));
        }
    }

    private static char? AccessKey(TrayMenuEntry item) =>
        NativeMenuText.AccessKeyOf(NativeMenuText.HeaderOf(item)) is { } key ? char.ToUpperInvariant(key) : null;
}
