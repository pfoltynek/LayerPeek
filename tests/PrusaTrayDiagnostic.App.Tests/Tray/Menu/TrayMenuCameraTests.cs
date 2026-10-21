using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

/// <summary>"Camera" items of the tray menu (T-014).</summary>
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

    public TrayMenuCameraTests() => _viewModel = new TrayMenuViewModel(_monitor, _dispatcher, TestWebOpener.Create(_launcher), TrayMenuOptions.Default);

    public void Dispose()
    {
        _viewModel.Dispose();
        _culture.Dispose();
    }

    private static PrinterDefinition WithCamera(PrinterDefinition printer, bool enabled = true) =>
        printer with { Camera = new CameraSettings(enabled, new Uri("rtsp://192.168.0.227/live")) };

    /// <summary>The camera item of the only printer: the last item of its submenu (T-042).</summary>
    private TrayMenuEntry SingleCamera => Assert.Single(_viewModel.PrinterItems).Submenu![^1];

    [Fact]
    public void SinglePrinterWithCameraHasTheCameraItemInItsSubmenu()
    {
        _viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();

        TrayMenuEntry printer = Assert.Single(_viewModel.PrinterItems);
        Assert.Equal(TrayMenuCommand.None, printer.Command);
        Assert.Collection(
            printer.Submenu!,
            web => Assert.Equal(TrayMenuCommand.OpenWeb, web.Command),
            camera =>
            {
                Assert.Equal(TrayMenuCommand.Camera, camera.Command);
                Assert.Equal("_Camera", camera.Header);
                Assert.True(camera.HasAccessKey);
                Assert.True(camera.IsEnabled);
                Assert.Equal("mk4", camera.PrinterId);
            });
    }

    [Fact]
    public void LanguageSwitchTranslatesTheCameraItemOfOneAndOfSeveralPrinters()
    {
        _viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();

        using (new CultureScope("de-DE"))
        {
            _viewModel.RefreshTexts();
            _dispatcher.RunAll();
        }

        TrayMenuEntry single = SingleCamera;
        Assert.Equal((TrayMenuCommand.Camera, "_Kamera"), (single.Command, single.Header));

        _viewModel.SetPrinters([WithCamera(Definition("a", "Alpha")), Definition("b", "Beta")]);
        _dispatcher.RunAll();
        using (new CultureScope("it-IT"))
        {
            _viewModel.RefreshTexts();
            _dispatcher.RunAll();
        }

        TrayMenuEntry grouped = _viewModel.PrinterItems[0].Submenu![^1];
        Assert.Equal((TrayMenuCommand.Camera, "_Telecamera", "a"), (grouped.Command, grouped.Header, grouped.PrinterId));
    }

    [Fact]
    public void PrinterWithoutEnabledCameraHasNoCameraItem()
    {
        _viewModel.SetPrinters([WithCamera(Definition(), enabled: false)]);
        _dispatcher.RunAll();

        Assert.DoesNotContain(Assert.Single(_viewModel.PrinterItems).Submenu!, static item => item.Command == TrayMenuCommand.Camera);
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

        Assert.Equal("MK4: Printing – 10%", Assert.Single(_viewModel.PrinterItems).Header);
        Assert.Equal("_Camera", SingleCamera.Header);
    }

    [Fact]
    public void DisablingTheCameraRemovesTheItem()
    {
        _viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();

        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        Assert.Equal(TrayMenuCommand.OpenWeb, Assert.Single(Assert.Single(_viewModel.PrinterItems).Submenu!).Command);
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

        _target.Click(_target.Printers[0].Submenu![^1]);

        Assert.Equal(["mk4"], _commands.CameraRequests);
    }

    [Fact]
    public void CameraItemWithoutPrinterDoesNothing()
    {
        using TrayMenuPresenter presenter = new(_target, _viewModel, _commands, _logger);

        _target.Click(new TrayMenuEntry(TrayMenuCommand.Camera, "Camera", IsEnabled: true));

        Assert.Empty(_commands.CameraRequests);
    }

    // ---- Check item (T-040) ----

    [Fact]
    public void CameraItemIsAnUncheckedCheckItemWhileTheWindowIsClosed()
    {
        _viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();

        Assert.False(SingleCamera.IsChecked);
        Assert.Null(_viewModel.PrinterItems[0].IsChecked);
        Assert.All(_viewModel.PrinterItems[0].Submenu!.SkipLast(1), static item => Assert.Null(item.IsChecked));
    }

    [Fact]
    public void OpenWindowChecksTheSinglePrinterCameraItemUntilItCloses()
    {
        _viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();

        _viewModel.SetCameraWindowOpen("mk4", isOpen: true);
        Assert.True(SingleCamera.IsChecked);
        Assert.Equal("_Camera", SingleCamera.Header);

        _viewModel.SetCameraWindowOpen("MK4", isOpen: false);
        Assert.False(SingleCamera.IsChecked);
    }

    [Fact]
    public void OpenWindowChecksOnlyTheItemOfItsPrinter()
    {
        _viewModel.SetPrinters([WithCamera(Definition("a", "Alpha")), WithCamera(Definition("b", "Beta"))]);
        _dispatcher.RunAll();
        TrayMenuEntryList betaSubmenu = _viewModel.PrinterItems[1].Submenu!;

        _viewModel.SetCameraWindowOpen("a", isOpen: true);

        Assert.True(_viewModel.PrinterItems[0].Submenu![^1].IsChecked);
        Assert.False(_viewModel.PrinterItems[1].Submenu![^1].IsChecked);
        // The other printer's item is untouched.
        Assert.Same(betaSubmenu, _viewModel.PrinterItems[1].Submenu);
    }

    [Fact]
    public void CheckStateSurvivesStateUpdatesLanguageSwitchesAndSettings()
    {
        _viewModel.SetPrinters([WithCamera(Definition("a", "Alpha")), WithCamera(Definition("b", "Beta"))]);
        _dispatcher.RunAll();
        _viewModel.SetCameraWindowOpen("b", isOpen: true);

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10), printerId: "b"));
        _dispatcher.RunAll();
        Assert.True(_viewModel.PrinterItems[1].Submenu![^1].IsChecked);

        using (new CultureScope("cs-CZ"))
        {
            _viewModel.RefreshTexts();
            _dispatcher.RunAll();
        }

        Assert.True(_viewModel.PrinterItems[1].Submenu![^1].IsChecked);

        // Another printer removed: the remaining single printer keeps its open window checked.
        _viewModel.SetPrinters([WithCamera(Definition("b", "Beta"))]);
        _dispatcher.RunAll();
        Assert.True(SingleCamera.IsChecked);
    }

    [Fact]
    public void RepeatedReportsChangeNothing()
    {
        _viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();
        _viewModel.SetCameraWindowOpen("mk4", isOpen: true);
        int changes = 0;
        ((System.Collections.Specialized.INotifyCollectionChanged)_viewModel.PrinterItems).CollectionChanged += (_, _) => changes++;

        _viewModel.SetCameraWindowOpen("mk4", isOpen: true);
        _viewModel.SetCameraWindowOpen("other", isOpen: false);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void WindowOfAPrinterWithoutCameraItemChangesNothingVisible()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        TrayMenuEntry before = Assert.Single(_viewModel.PrinterItems);

        _viewModel.SetCameraWindowOpen("mk4", isOpen: true);

        Assert.Equal(before, Assert.Single(_viewModel.PrinterItems));
    }

    [Fact]
    public void PresenterShowsTheCheckMarkAndClicksToggle()
    {
        _viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();
        using TrayMenuPresenter presenter = new(_target, _viewModel, _commands, _logger);
        Assert.False(_target.Printers[0].Submenu![^1].IsChecked);

        _viewModel.SetCameraWindowOpen("mk4", isOpen: true);
        Assert.True(_target.Printers[0].Submenu![^1].IsChecked);
        _target.Click(_target.Printers[0].Submenu![^1]);

        // The click asks to toggle: the app closes the open window (its close reports back, unchecking the item).
        Assert.Equal(["mk4"], _commands.CameraRequests);
    }

    [Fact]
    public void DisposedViewModelIgnoresWindowReports()
    {
        _viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();
        _viewModel.Dispose();

        _viewModel.SetCameraWindowOpen("mk4", isOpen: true);

        Assert.False(SingleCamera.IsChecked);
    }
}
