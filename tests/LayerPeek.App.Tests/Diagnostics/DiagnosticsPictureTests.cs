using LayerPeek.App.Diagnostics;
using LayerPeek.App.Printers;
using LayerPeek.App.Tests.Printers;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.History;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Diagnostics;

/// <summary>The picture of the selected printer in the diagnostics window (T-039).</summary>
public sealed class DiagnosticsPictureTests : IDisposable
{
    private static readonly PrinterModelCode Mk4SCode = new(1, 4, 1);

    private readonly FakePrinterMonitor _monitor = new();
    private readonly MonitoredPrinters _printers = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakePrinterInfoSource _info = new();
    private readonly CustomPrinterImages _images = new();
    private DiagnosticsViewModel? _viewModel;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _info.CancelPending();
    }

    private void Configure(params PrinterDefinition[] printers) =>
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = printers });

    private void Update(params PrinterDefinition[] printers)
    {
        ((IMonitoringObserver)_printers).UpdateSettings(new AppSettings { Printers = printers });
        _dispatcher.RunAll();
    }

    private DiagnosticsViewModel Create(string? initialPrinterId = null) =>
        _viewModel = new DiagnosticsViewModel(_monitor, _printers, new TelemetryHistory(), _dispatcher, _info, new FixedClock(Now), initialPrinterId, _images);

    [Fact]
    public void NoPrinterNoPicture()
    {
        Assert.Null(Create().Picture);

        Configure();
        _dispatcher.RunAll();

        Assert.Null(_viewModel!.Picture);
    }

    [Fact]
    public void SelectedPrinterShowsItsPicture()
    {
        Configure(Definition("a", "Alpha") with { DetectedModel = Mk4SCode }, Definition("b", "Beta") with { Image = PrinterImageChoice.ForModel(PrinterModel.Mini) });

        DiagnosticsViewModel viewModel = Create();
        Assert.Equal("mk4", viewModel.Picture!.IllustrationId);

        viewModel.SelectedPrinter = viewModel.Printers[1];

        Assert.Equal("mini", viewModel.Picture!.IllustrationId);
    }

    [Fact]
    public void DetectedModelUpdatesThePictureWithoutAnotherInformationRequest()
    {
        Configure(Definition("a", "Alpha"));
        DiagnosticsViewModel viewModel = Create();
        Assert.Same(PrinterPicture.Generic, viewModel.Picture);
        int requests = _info.Requests.Count;

        Update(Definition("a", "Alpha") with { DetectedModel = Mk4SCode });

        Assert.Equal("mk4", viewModel.Picture!.IllustrationId);
        Assert.Equal(requests, _info.Requests.Count);
    }

    [Fact]
    public void SavedCustomImageIsShown()
    {
        Configure(Definition("a", "Alpha") with { Image = PrinterImageChoice.Custom });
        DiagnosticsViewModel viewModel = Create();
        byte[] png = TestImages.Png();

        _images.Set("a", png);
        _dispatcher.RunAll();

        Assert.Same(png, viewModel.Picture!.CustomPng);
    }

    [Fact]
    public void ClosedWindowStopsListeningToImages()
    {
        Configure(Definition("a", "Alpha") with { Image = PrinterImageChoice.Custom });
        DiagnosticsViewModel viewModel = Create();
        viewModel.Dispose();
        int posts = _dispatcher.PostCount;

        _images.Set("a", TestImages.Png());

        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public void MonitoredPrintersUpdateKeepsTheHealthAndPublishesTheDefinitions()
    {
        Configure(Definition("a", "Alpha"));
        _printers.ReportMonitoringFailed();
        int changes = 0;
        _printers.Changed += (_, _) => changes++;

        ((IMonitoringObserver)_printers).UpdateSettings(new AppSettings { Printers = [Definition("a", "Alpha") with { DetectedModel = Mk4SCode }] });

        Assert.Equal(1, changes);
        Assert.True(_printers.Current.MonitoringFailed);
        Assert.Equal(Mk4SCode, Assert.Single(_printers.Current.Printers!).DetectedModel);
    }
}
