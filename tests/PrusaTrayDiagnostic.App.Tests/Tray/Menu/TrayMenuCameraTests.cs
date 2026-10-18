using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

/// <summary>"Camera…" items of the tray menu (T-014).</summary>
public sealed class TrayMenuCameraTests : IDisposable
{
    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeUrlLauncher _launcher = new();
    private readonly FakeMenuTarget _target = new();
    private readonly FakeAppCommands _commands = new();
    private readonly ListLogger<TrayMenuCameraTests> _logger = new();
    private readonly TrayMenuViewModel _viewModel;

    public TrayMenuCameraTests() => _viewModel = new TrayMenuViewModel(_monitor, _dispatcher, _launcher, TrayMenuOptions.Default);

    public void Dispose()
    {
        _viewModel.Dispose();
        _culture.Dispose();
    }

    private static PrinterDefinition WithCamera(PrinterDefinition printer, bool enabled = true) =>
        printer with { Camera = new CameraSettings(enabled, new Uri("rtsp://192.168.0.227/live")) };

    [Fact]
    public void SinglePrinterWithCameraGetsACameraItemAfterIt()
    {
        _viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();

        Assert.Collection(
            _viewModel.PrinterItems,
            printer => Assert.Equal(TrayMenuCommand.OpenWeb, printer.Command),
            camera =>
            {
                Assert.Equal(TrayMenuCommand.Camera, camera.Command);
                Assert.Equal("Camera…", camera.Header);
                Assert.True(camera.IsEnabled);
                Assert.Equal("mk4", camera.PrinterId);
            });
    }

    [Fact]
    public void PrinterWithoutEnabledCameraHasNoCameraItem()
    {
        _viewModel.SetPrinters([WithCamera(Definition(), enabled: false)]);
        _dispatcher.RunAll();

        Assert.Single(_viewModel.PrinterItems);
    }

    [Fact]
    public void SubmenuOffersCameraOnlyForPrintersWithACamera()
    {
        _viewModel.SetPrinters([WithCamera(Definition("a", "Alpha")), Definition("b", "Beta")]);
        _dispatcher.RunAll();

        Assert.Equal(2, _viewModel.PrinterItems.Count);
        TrayMenuEntry camera = _viewModel.PrinterItems[0].Submenu![^1];
        Assert.Equal((TrayMenuCommand.Camera, "a"), (camera.Command, camera.PrinterId));
        Assert.DoesNotContain(_viewModel.PrinterItems[1].Submenu!, item => item.Command == TrayMenuCommand.Camera);
    }

    [Fact]
    public void StateUpdateOfSinglePrinterKeepsTheCameraItem()
    {
        _viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10)));
        _dispatcher.RunAll();

        Assert.Equal(["MK4: Printing – 10%", "Camera…"], _viewModel.PrinterItems.Select(item => item.Header));
    }

    [Fact]
    public void DisablingTheCameraRemovesTheItem()
    {
        _viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();

        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        Assert.Single(_viewModel.PrinterItems);
    }

    [Fact]
    public void CameraItemsOpenTheCameraWindowOfTheirPrinter()
    {
        _viewModel.SetPrinters([WithCamera(Definition("a", "Alpha")), WithCamera(Definition("b", "Beta"))]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = new(_target, _viewModel, _commands, _logger);

        _target.Click(_target.Printers[1].Submenu!.Single(item => item.Command == TrayMenuCommand.Camera));
        _target.Click(_target.Printers[0].Submenu!.Single(item => item.Command == TrayMenuCommand.Camera));

        Assert.Equal(["b", "a"], _commands.CameraRequests);
    }

    [Fact]
    public void SinglePrinterCameraItemOpensItsWindow()
    {
        _viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = new(_target, _viewModel, _commands, _logger);

        _target.Click(_target.Printers[1]);

        Assert.Equal(["mk4"], _commands.CameraRequests);
    }

    [Fact]
    public void CameraItemWithoutPrinterDoesNothing()
    {
        using TrayMenuPresenter presenter = new(_target, _viewModel, _commands, _logger);

        _target.Click(new TrayMenuEntry(TrayMenuCommand.Camera, "Camera…", IsEnabled: true));

        Assert.Empty(_commands.CameraRequests);
    }
}
