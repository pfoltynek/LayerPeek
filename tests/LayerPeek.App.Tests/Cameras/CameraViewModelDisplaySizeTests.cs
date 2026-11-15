using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// T-184: the camera window asks for frames of the size its image is drawn at (rounded up, changes debounced), takes the
/// aspect ratio from the camera's size, writes each frame once on the streaming thread and never after its run stopped;
/// a full resolution capture next to the window still gets the camera's resolution.
/// </summary>
public sealed class CameraViewModelDisplaySizeTests : IAsyncDisposable
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

    public CameraViewModelDisplaySizeTests()
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

    private CameraViewModel Create()
    {
        PrinterDefinition printer = Definition() with { Camera = new CameraSettings(enabled: true, StreamUri) };
        CameraViewModel viewModel = new(CameraTarget.From(printer)!, _hub, _monitor, _printers, _dispatcher, _states, _time, CameraFeedOptions.Default, NullLogger<CameraViewModel>.Instance);
        _created.Add(viewModel);
        viewModel.AttachFrameSink(_sink);
        return viewModel;
    }

    /// <summary>Sends a camera frame and returns the size of the frame the window showed.</summary>
    private async Task<(int Width, int Height)> ShowFrameAsync(FakeCameraFeed feed, int width = 1280, int height = 720, byte fill = 1)
    {
        int shown = _sink.Frames.Count;
        feed.Send(width, height, fill);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return _sink.Frames.Count > shown; }, $"{width}×{height} frame drawn");
        _dispatcher.RunAll();
        return (_sink.Frames[^1].Width, _sink.Frames[^1].Height);
    }

    [Theory]
    [InlineData(640, 360, 1.0, 640, 384)]
    [InlineData(640, 360, 2.0, 1280, 736)]
    [InlineData(320, 180, 1.25, 416, 256)]
    [InlineData(1, 1, 1.0, 32, 32)]
    [InlineData(100000, 100, 1.0, CameraFrameSize.MaxSide, 128)]
    public void TheAreaIsRequestedInDevicePixelsRoundedUpToTheStep(double width, double height, double scaling, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(new CameraFrameSize(expectedWidth, expectedHeight), CameraViewModel.VideoSizeOf(width, height, scaling));
    }

    [Theory]
    [InlineData(0, 100, 1.0)]
    [InlineData(100, 0, 1.0)]
    [InlineData(100, 100, 0)]
    [InlineData(double.NaN, 100, 1.0)]
    [InlineData(100, double.PositiveInfinity, 1.0)]
    public void AnEmptyOrInvalidAreaRequestsNothing(double width, double height, double scaling)
    {
        Assert.Null(CameraViewModel.VideoSizeOf(width, height, scaling));
    }

    [Fact]
    public async Task AWindowLaidOutBeforeItStreamsStartsWithItsSize()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetVideoArea(320, 180, 1.0);
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        (int width, int height) = await ShowFrameAsync(feed);

        Assert.Equal(new CameraFrameSize(320, 192), viewModel.RequestedVideoSize);
        Assert.Equal(new CameraFrameSize(320, 192), feed.LastDisplaySize);
        // 1280 × 720 fitted into 320 × 192: the width limits.
        Assert.Equal((320, 180), (width, height));
    }

    [Fact]
    public async Task TheFirstLayoutOfAStreamingWindowIsRequestedAtOnceWithoutReconnecting()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        Assert.Equal((1280, 720), await ShowFrameAsync(feed));
        Assert.Null(viewModel.RequestedVideoSize);

        viewModel.SetVideoArea(640, 360, 1.0);

        Assert.Equal(new CameraFrameSize(640, 384), viewModel.RequestedVideoSize);
        Assert.Equal((640, 360), await ShowFrameAsync(feed));
        Assert.Single(_factory.Feeds);
    }

    [Fact]
    public async Task LaterChangesAreRequestedOnceTheAreaSettled()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetVideoArea(320, 180, 1.0);
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        // Dragging an edge: every new area restarts the wait.
        viewModel.SetVideoArea(400, 225, 1.0);
        _time.Advance(CameraViewModel.VideoSizeSettleTime - TimeSpan.FromMilliseconds(50));
        viewModel.SetVideoArea(480, 270, 1.0);
        _time.Advance(CameraViewModel.VideoSizeSettleTime - TimeSpan.FromMilliseconds(50));
        _dispatcher.RunAll();
        Assert.Equal(new CameraFrameSize(320, 192), viewModel.RequestedVideoSize);

        _time.Advance(TimeSpan.FromMilliseconds(50));
        _dispatcher.RunAll();

        Assert.Equal(new CameraFrameSize(480, 288), viewModel.RequestedVideoSize);
        Assert.Equal((480, 270), await ShowFrameAsync(feed));
        Assert.Single(_factory.Feeds);
    }

    [Fact]
    public async Task AChangeWithinTheStepRequestsNothing()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetVideoArea(320, 180, 1.0);
        viewModel.SetActive(true);
        await _factory.WaitForFeedAsync(1);
        int posts = _dispatcher.PostCount;

        // E.g. the mini preview correcting its height by a pixel.
        viewModel.SetVideoArea(318.5, 181, 1.0);
        _time.Advance(CameraViewModel.VideoSizeSettleTime);

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(new CameraFrameSize(320, 192), viewModel.RequestedVideoSize);
    }

    [Fact]
    public async Task AnotherMonitorScalingRequestsTheNewDevicePixels()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetVideoArea(320, 180, 1.0);
        viewModel.SetActive(true);
        await _factory.WaitForFeedAsync(1);

        viewModel.SetVideoArea(320, 180, 2.0);
        _time.Advance(CameraViewModel.VideoSizeSettleTime);
        _dispatcher.RunAll();

        Assert.Equal(new CameraFrameSize(640, 384), viewModel.RequestedVideoSize);
    }

    [Fact]
    public async Task FullScreenLargerThanTheCameraGetsItsFullResolution()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetVideoArea(1920, 1080, 2.0);
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        Assert.Equal((1280, 720), await ShowFrameAsync(feed));
    }

    [Fact]
    public async Task AnEmptyAreaKeepsTheLastSize()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetVideoArea(320, 180, 1.0);
        viewModel.SetActive(true);
        await _factory.WaitForFeedAsync(1);

        viewModel.SetVideoArea(0, 0, 1.0);
        _time.Advance(CameraViewModel.VideoSizeSettleTime);
        _dispatcher.RunAll();

        Assert.Equal(new CameraFrameSize(320, 192), viewModel.RequestedVideoSize);
    }

    [Fact]
    public async Task AnAreaChangedWhileMinimizedIsUsedByTheNextRun()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetVideoArea(320, 180, 1.0);
        viewModel.SetActive(true);
        FakeCameraFeed first = await _factory.WaitForFeedAsync(1);
        viewModel.SetActive(false);
        await first.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        viewModel.SetVideoArea(640, 360, 1.0);
        viewModel.SetActive(true);
        await _factory.WaitForFeedAsync(2);

        Assert.Equal(new CameraFrameSize(640, 384), viewModel.RequestedVideoSize);
    }

    [Fact]
    public async Task TheAspectRatioIsTheCamerasNotTheScaledFrames()
    {
        CameraViewModel viewModel = Create();
        // 1280 × 720 into 352 × 192 (330 × 190 rounded up): 341 × 192, whose own ratio is 0.5630…
        viewModel.SetVideoArea(330, 190, 1.0);
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        (int width, int height) = await ShowFrameAsync(feed);

        Assert.Equal((341, 192), (width, height));
        Assert.Equal(720.0 / 1280.0, viewModel.VideoAspectRatio);
    }

    [Fact]
    public async Task EachFrameIsWrittenOnTheStreamingThreadAndABusyUiShowsTheNewest()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        feed.Send(fill: 1);
        feed.Send(fill: 2);
        feed.Send(fill: 3);
        await Waiting.UntilAsync(() => _sink.Writes >= 3, "three frames written");
        _dispatcher.RunAll();

        // Every frame was copied once into the sink, the UI only showed the bitmap once.
        Assert.Equal(3, _sink.Writes);
        Assert.Equal((4, 2, (byte)3), Assert.Single(_sink.Frames));
    }

    [Fact]
    public async Task StoppingWaitsForAFrameBeingWrittenAndWritesNoneAfterIt()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        using SemaphoreSlim writing = new(0);
        using SemaphoreSlim release = new(0);
        _sink.Written = () =>
        {
            writing.Release();
            release.Wait(TestTimeouts.Wait);
        };

        feed.Send(fill: 1);
        Assert.True(await writing.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
        _dispatcher.RunAll();
        // The window closing (stopping the run, then disposing its bitmaps) from another thread, so that the test can watch it.
        Task stopping = Task.Run(() => viewModel.SetActive(false), TestContext.Current.CancellationToken);

        // It waits until the frame is written.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(stopping.IsCompleted);
        _sink.Written = null;
        release.Release();
        await stopping.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(1, _sink.Writes);
        Assert.False(viewModel.IsStreaming);
    }

    [Fact]
    public async Task ADetachedSinkGetsNoFrames()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        viewModel.AttachFrameSink(null);

        feed.Send(fill: 1);
        await Waiting.UntilAsync(() => feed.Consumed >= 1, "frame consumed");
        _dispatcher.RunAll();

        Assert.Equal(0, _sink.Writes);
        Assert.False(viewModel.HasFrame);
    }

    [Fact]
    public async Task AFullResolutionCaptureNextToTheWindowGetsTheCamerasResolution()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetVideoArea(320, 180, 1.0);
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        await ShowFrameAsync(feed);

        // User decision 2026-11-10: snapshots are always full resolution (the capture of the settings and detection).
        Task<CameraImage> capture = CameraSnapshotTester.CaptureSharedAsync(_hub, "mk4", StreamUri, TestContext.Current.CancellationToken);
        await Waiting.UntilAsync(() => { feed.Send(1280, 720, 7); _dispatcher.RunAll(); return capture.IsCompleted; }, "captured");
        CameraImage image = await capture;
        (int width, int height) = await ShowFrameAsync(feed, fill: 8);

        Assert.Equal((1280, 720), (image.Width, image.Height));
        Assert.Equal((320, 180), (width, height));
        Assert.Single(_factory.Feeds);
    }
}
