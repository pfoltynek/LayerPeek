using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.App.Tests.Printers;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

/// <summary>
/// Printer icons in the tray menu (T-039): the printer item shows the printer's picture where the platform shows icons
/// (Windows), follows a detected model or a saved image without a restart, and keeps the printer state meanwhile.
/// </summary>
public sealed class TrayMenuIconTests : IDisposable
{
    private static readonly PrinterModelCode Mk4SCode = new(1, 4, 1);

    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly CustomPrinterImages _images = new();
    private readonly TrayMenuViewModel _viewModel;

    public TrayMenuIconTests() =>
        _viewModel = new TrayMenuViewModel(_monitor, _dispatcher, TestWebOpener.Create(new FakeUrlLauncher()), TrayMenuOptions.Default with { PrinterIcons = true }, _images);

    public void Dispose()
    {
        _viewModel.Dispose();
        _culture.Dispose();
    }

    private void Apply(params PrinterDefinition[] printers)
    {
        ((IMonitoringObserver)_viewModel).ApplySettings(AppSettings.Default with { Printers = printers });
        _dispatcher.RunAll();
    }

    private void Update(params PrinterDefinition[] printers)
    {
        ((IMonitoringObserver)_viewModel).UpdateSettings(AppSettings.Default with { Printers = printers });
        _dispatcher.RunAll();
    }

    [Fact]
    public void SinglePrinterItemShowsThePrinterPicture()
    {
        Apply(Definition("a", "Alpha") with { DetectedModel = Mk4SCode });

        TrayMenuEntry entry = _viewModel.PrinterItems[0];
        Assert.Equal("mk4", entry.Icon!.IllustrationId);
    }

    [Fact]
    public void EveryPrinterSubmenuHeaderShowsItsPictureButNotItsItems()
    {
        Apply(Definition("a", "Alpha") with { DetectedModel = Mk4SCode }, Definition("b", "Beta") with { Image = PrinterImageChoice.ForModel(PrinterModel.Mini) });

        Assert.Equal(["mk4", "mini"], _viewModel.PrinterItems.Select(static entry => entry.Icon!.IllustrationId));
        Assert.All(_viewModel.PrinterItems, static entry => Assert.All(entry.Submenu!, static item => Assert.Null(item.Icon)));
    }

    [Fact]
    public void StatusMessagesAndCameraItemsHaveNoIcon()
    {
        Assert.Null(Assert.Single(_viewModel.PrinterItems).Icon);

        Apply(Definition("a", "Alpha") with { Camera = new Core.Cameras.CameraSettings(enabled: true, new Uri("rtsp://10.0.0.5/live")) });

        TrayMenuEntry printer = Assert.Single(_viewModel.PrinterItems);
        Assert.NotNull(printer.Icon);
        // Actions have no icons (T-042), the camera check item included.
        Assert.Equal(2, printer.Submenu!.Count);
        Assert.All(printer.Submenu, static item => Assert.Null(item.Icon));

        Apply();
        Assert.All(_viewModel.PrinterItems, static item => Assert.Null(item.Icon));
        Assert.All(_viewModel.FixedItems, static item => Assert.Null(item.Icon));
    }

    [Fact]
    public void WithoutPrinterIconsNothingHasAnIcon()
    {
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(new FakeUrlLauncher()), TrayMenuOptions.Default, _images);
        ((IMonitoringObserver)viewModel).ApplySettings(AppSettings.Default with { Printers = [Definition("a", "Alpha")] });
        _dispatcher.RunAll();

        Assert.Null(viewModel.PrinterItems[0].Icon);
        Assert.All(viewModel.PrinterItems[0].Submenu!, static item => Assert.Null(item.Icon));
        Assert.False(TrayMenuOptions.Default.PrinterIcons);
        // Printer icons on Windows only; macOS shows no icons in the menu (T-018 / T-042).
        Assert.Equal(OperatingSystem.IsWindows(), TrayMenuOptions.ForCurrentPlatform.PrinterIcons);
    }

    [Fact]
    public void DetectedModelUpdatesTheIconAndKeepsTheState()
    {
        Apply(Definition("a", "Alpha"));
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42), "a"));
        _dispatcher.RunAll();
        Assert.Equal("Alpha: Printing – 42%", _viewModel.PrinterItems[0].Header);
        Assert.Equal(PrinterIllustrations.Generic, _viewModel.PrinterItems[0].Icon!.IllustrationId);

        Update(Definition("a", "Alpha") with { DetectedModel = Mk4SCode });

        Assert.Equal("Alpha: Printing – 42%", _viewModel.PrinterItems[0].Header);
        Assert.Equal("mk4", _viewModel.PrinterItems[0].Icon!.IllustrationId);
    }

    [Fact]
    public void SavedCustomImageReplacesTheIcon()
    {
        Apply(Definition("a", "Alpha") with { Image = PrinterImageChoice.Custom });
        Assert.False(_viewModel.PrinterItems[0].Icon!.IsCustom);
        byte[] png = TestImages.Png();

        _images.Set("a", png);
        _dispatcher.RunAll();

        Assert.Same(png, _viewModel.PrinterItems[0].Icon!.CustomPng);

        _images.Remove("a");
        _dispatcher.RunAll();

        Assert.False(_viewModel.PrinterItems[0].Icon!.IsCustom);
    }

    [Fact]
    public void UnrelatedImageChangePostsNothing()
    {
        Apply(Definition("a", "Alpha"));
        int posts = _dispatcher.PostCount;

        _images.Set("other", TestImages.Png());

        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public void FailureStaysReportedOnAnUpdate()
    {
        Apply(Definition("a", "Alpha"));
        _viewModel.ReportMonitoringFailed();
        _dispatcher.RunAll();

        Update(Definition("a", "Alpha") with { DetectedModel = Mk4SCode });

        Assert.Equal(Resources.Strings.ToolTipMonitoringFailed, Assert.Single(_viewModel.PrinterItems).Header);
    }

    [Fact]
    public void DisposedMenuIgnoresImageChanges()
    {
        Apply(Definition("a", "Alpha") with { Image = PrinterImageChoice.Custom });
        _viewModel.Dispose();
        int posts = _dispatcher.PostCount;

        _images.Set("a", TestImages.Png());

        Assert.Equal(posts, _dispatcher.PostCount);
    }
}
