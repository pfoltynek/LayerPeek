using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// T-185: only the mini preview limits its frame rate (the setting's, 10 fps by default); the normal window and full screen
/// take every frame. A mode switch and saved settings change the limit of the running lease at once, without reconnecting.
/// </summary>
public sealed class CameraViewModelFrameRateTests : IAsyncDisposable
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
    private readonly List<CameraViewModel> _created = [];
    private readonly CameraStreamHub _hub;

    public CameraViewModelFrameRateTests()
    {
        _hub = CameraHubs.Immediate(new ReconnectingCameraFeed(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (CameraViewModel viewModel in _created)
        {
            viewModel.Dispose();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }

        await _hub.DisposeAsync();
        _culture.Dispose();
    }

    private static PrinterDefinition Printer() => Definition() with { Camera = new CameraSettings(enabled: true, StreamUri) };

    private CameraViewModel Create()
    {
        CameraViewModel viewModel = new(CameraTarget.From(Printer())!, _hub, _monitor, _printers, _dispatcher, _states, _time, CameraFeedOptions.Default, NullLogger<CameraViewModel>.Instance);
        _created.Add(viewModel);
        viewModel.AttachFrameSink(_sink);
        return viewModel;
    }

    /// <summary>"Save" in the settings with <paramref name="rate"/>: the monitoring run applies them, the window on the UI thread.</summary>
    private void Save(int? rate)
    {
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer()], MiniPreviewFrameRate = rate });
        _dispatcher.RunAll();
    }

    [Fact]
    public async Task OnlyTheMiniPreviewIsLimitedAndASwitchAppliesAtOnceWithoutReconnecting()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        await _factory.WaitForFeedAsync(1);
        Assert.Equal(CameraWindowMode.Normal, viewModel.Mode);
        Assert.Null(viewModel.RequestedFrameRate);

        viewModel.ShowMiniCommand.Execute(null);
        Assert.Equal(MiniPreviewFrameRate.Default, viewModel.RequestedFrameRate);

        // Full screen entered from the mini preview: every frame, and the limit again on the way back.
        viewModel.ShowFullScreenCommand.Execute(null);
        Assert.Null(viewModel.RequestedFrameRate);
        viewModel.EscapeCommand.Execute(null);
        Assert.Equal(CameraWindowMode.Mini, viewModel.Mode);
        Assert.Equal(10, viewModel.RequestedFrameRate);

        viewModel.ShowNormalCommand.Execute(null);
        Assert.Null(viewModel.RequestedFrameRate);
        Assert.Single(_factory.Feeds);
    }

    [Fact]
    public async Task AWindowThatStartsInTheMiniPreviewTakesItsFirstLeaseWithTheLimit()
    {
        Save(5);
        CameraViewModel viewModel = Create();
        viewModel.ShowMiniCommand.Execute(null);

        viewModel.SetActive(true);
        await _factory.WaitForFeedAsync(1);

        Assert.Equal(5, viewModel.RequestedFrameRate);
    }

    [Fact]
    public async Task SavedSettingsApplyToTheOpenMiniPreviewAtOnce()
    {
        CameraViewModel viewModel = Create();
        viewModel.ShowMiniCommand.Execute(null);
        viewModel.SetActive(true);
        await _factory.WaitForFeedAsync(1);

        Save(5);
        Assert.Equal(5, viewModel.RequestedFrameRate);

        // "Smooth": no limit.
        Save(null);
        Assert.Null(viewModel.RequestedFrameRate);

        Save(17);
        Assert.Equal(17, viewModel.RequestedFrameRate);
        Assert.Single(_factory.Feeds);
    }

    [Fact]
    public async Task SettingsSavedInTheNormalWindowApplyWhenItSwitchesToTheMiniPreview()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        await _factory.WaitForFeedAsync(1);

        Save(3);
        Assert.Null(viewModel.RequestedFrameRate);

        viewModel.ShowMiniCommand.Execute(null);
        Assert.Equal(3, viewModel.RequestedFrameRate);
    }

    [Fact]
    public async Task ANewRunOfTheMiniPreviewKeepsItsLimit()
    {
        CameraViewModel viewModel = Create();
        viewModel.ShowMiniCommand.Execute(null);
        viewModel.SetActive(true);
        await _factory.WaitForFeedAsync(1);

        // Minimized and restored: a new lease.
        viewModel.SetActive(false);
        Assert.Null(viewModel.RequestedFrameRate);
        viewModel.SetActive(true);

        Assert.Equal(10, viewModel.RequestedFrameRate);
    }

    [Fact]
    public async Task TheNormalWindowAfterTheMiniPreviewShowsEveryFrame()
    {
        CameraViewModel viewModel = Create();
        viewModel.ShowMiniCommand.Execute(null);
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        viewModel.ShowNormalCommand.Execute(null);
        for (byte fill = 1; fill <= 5; fill++)
        {
            int shown = _sink.Frames.Count;
            feed.Send(1280, 720, fill);
            await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return _sink.Frames.Count > shown; }, $"frame {fill} drawn");
        }

        Assert.Equal(5, _sink.Frames.Count);
    }
}
