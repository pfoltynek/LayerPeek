using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// The mini preview part of the camera window view model (T-089): the aspect ratio of the stream from the frames shown
/// and the info strip that follows the printer's snapshots and cool-down.
/// </summary>
public sealed class CameraViewModelMiniTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeCameraFeedFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakePrinterMonitor _monitor = new();
    private readonly MonitoredPrinters _printers = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly MemoryCameraWindowStateStore _states = new();
    private readonly RecordingFrameSink _sink = new();
    private readonly CooldownMonitor _cooldown;
    private readonly List<CameraViewModel> _created = [];

    public CameraViewModelMiniTests()
    {
        _cooldown = new CooldownMonitor(_monitor, TimeProvider.System, new ListLogger<CooldownMonitor>());
        _cooldown.Configure([Printer()], TimeSpan.FromSeconds(5));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (CameraViewModel viewModel in _created)
        {
            viewModel.Dispose();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }

        _cooldown.Dispose();
        _culture.Dispose();
    }

    private static PrinterDefinition Printer(string url = "rtsp://192.168.0.227/live") =>
        Definition() with { Camera = new CameraSettings(enabled: true, new Uri(url)) };

    private CameraViewModel Create()
    {
        ReconnectingCameraFeed feed = new(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraViewModel viewModel = new(CameraTarget.From(Printer())!, feed, _monitor, _printers, _dispatcher, _states, _time, CameraFeedOptions.Default, NullLogger<CameraViewModel>.Instance, cooldown: _cooldown);
        _created.Add(viewModel);
        viewModel.AttachFrameSink(_sink);
        return viewModel;
    }

    private async Task ShowFrameAsync(FakeCameraFeed feed, int width, int height)
    {
        int shown = _sink.Frames.Count;
        feed.Send(width, height);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return _sink.Frames.Count > shown; }, $"{width}×{height} frame drawn");
        _dispatcher.RunAll();
    }

    [Fact]
    public void UntilTheFirstFrameTheAspectRatioIsUnknown()
    {
        CameraViewModel viewModel = Create();

        Assert.Null(viewModel.VideoAspectRatio);
        Assert.Equal(33, viewModel.MiniStrip.StripHeight);
    }

    [Fact]
    public async Task FramesTellTheAspectRatioOnceAndAgainOnlyWhenItChanges()
    {
        CameraViewModel viewModel = Create();
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        await ShowFrameAsync(feed, 16, 9);
        await ShowFrameAsync(feed, 16, 9);
        await ShowFrameAsync(feed, 16, 9);

        Assert.Equal(9.0 / 16.0, viewModel.VideoAspectRatio);
        Assert.Single(changed, name => name == nameof(CameraViewModel.VideoAspectRatio));

        // Another resolution with the same ratio changes nothing; a 4:3 stream does.
        await ShowFrameAsync(feed, 32, 18);
        Assert.Single(changed, name => name == nameof(CameraViewModel.VideoAspectRatio));
        await ShowFrameAsync(feed, 8, 6);
        Assert.Equal(0.75, viewModel.VideoAspectRatio);
        Assert.Equal(2, changed.Count(name => name == nameof(CameraViewModel.VideoAspectRatio)));
    }

    [Fact]
    public async Task AnotherCameraKeepsTheLastRatioUntilItsFirstFrame()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        await ShowFrameAsync(feed, 8, 6);

        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer("rtsp://192.168.0.228/live")] });
        _dispatcher.RunAll();

        Assert.False(viewModel.HasFrame);
        Assert.Equal(0.75, viewModel.VideoAspectRatio);
        FakeCameraFeed next = await _factory.WaitForFeedAsync(2);
        await ShowFrameAsync(next, 16, 9);
        Assert.Equal(9.0 / 16.0, viewModel.VideoAspectRatio);
    }

    [Fact]
    public void StripShowsTheCurrentSnapshotWhenTheWindowOpensAndFollowsTheNextOnes()
    {
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(19.9, TimeSpan.FromMinutes(154.2))));

        CameraViewModel viewModel = Create();

        Assert.Equal("19% · 2\u00A0h 35\u00A0min", viewModel.MiniStrip.Summary);
        _monitor.Raise(Snapshot(PrinterState.Paused, Job(20.1, TimeSpan.FromMinutes(150))));
        _dispatcher.RunAll();
        Assert.Equal("20% · Paused", viewModel.MiniStrip.Summary);
        // Another printer's snapshot is not this window's.
        _monitor.Raise(Snapshot(PrinterState.Idle, printerId: "other"));
        _dispatcher.RunAll();
        Assert.Equal("20% · Paused", viewModel.MiniStrip.Summary);
    }

    [Fact]
    public void StripShowsTheCoolDownOfThePrinter()
    {
        CameraViewModel viewModel = Create();
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(50)) with { Telemetry = new Telemetry { Nozzle = new Temperature(215, 215) } });

        _monitor.Raise(Snapshot(PrinterState.Finished, Job(100)) with { Timestamp = Now.AddSeconds(5), Telemetry = new Telemetry { Nozzle = new Temperature(200, 0) } });
        _dispatcher.RunAll();

        Assert.True(viewModel.MiniStrip.ShowsCoolingIcon);
        Assert.StartsWith("Cooling down… 200", viewModel.MiniStrip.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void LanguageSwitchFormatsTheStripAgain()
    {
        _monitor.Raise(Snapshot(PrinterState.Idle));
        CameraViewModel viewModel = Create();
        Assert.Equal("Ready", viewModel.MiniStrip.Summary);

        using (new UiCultureScope("cs"))
        {
            viewModel.RefreshTexts();

            Assert.Equal("Připravena", viewModel.MiniStrip.Summary);
        }
    }

    [Fact]
    public void ClosingTheWindowStopsTheStrip()
    {
        CameraViewModel viewModel = Create();
        _monitor.Raise(Snapshot(PrinterState.Idle));
        _dispatcher.RunAll();

        viewModel.Dispose();
        int posts = _dispatcher.PostCount;
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(50)) with { Telemetry = new Telemetry { Nozzle = new Temperature(215, 215) } });
        _monitor.Raise(Snapshot(PrinterState.Finished, Job(100)) with { Timestamp = Now.AddSeconds(5), Telemetry = new Telemetry { Nozzle = new Temperature(200, 0) } });

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal("Ready", viewModel.MiniStrip.Summary);
    }
}
