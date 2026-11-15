using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Display;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// T-186 (user decision 2026-11-11): the camera window takes frames only while it can be seen. A locked session, a display
/// that is off and a cloaked window (another virtual desktop) release its lease like minimizing; back to visible it takes
/// one again. The other consumers of the shared connection (recording, dashcam, timelapse, detection) go on.
/// </summary>
public sealed class CameraViewModelVisibilityTests : IAsyncDisposable
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
    private readonly FakeDisplayActivity _display = new();
    private readonly List<CameraViewModel> _created = [];
    private readonly CameraStreamHub _hub;

    public CameraViewModelVisibilityTests()
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

    private CameraViewModel Create(IDisplayActivityService? display = null)
    {
        PrinterDefinition printer = Definition("mk4", "MK4") with { Camera = new CameraSettings(enabled: true, StreamUri) };
        CameraViewModel viewModel = new(CameraTarget.From(printer)!, _hub, _monitor, _printers, _dispatcher, _states, _time, CameraFeedOptions.Default, NullLogger<CameraViewModel>.Instance, displayActivity: display ?? _display);
        _created.Add(viewModel);
        viewModel.AttachFrameSink(_sink);
        return viewModel;
    }

    /// <summary>The screen changes on another thread; the window follows once the UI thread ran.</summary>
    private async Task SetScreenVisibleAsync(bool visible)
    {
        await Task.Run(() => _display.Set(visible), TestContext.Current.CancellationToken);
        _dispatcher.RunAll();
    }

    private async Task<FakeCameraFeed> StreamingAsync(CameraViewModel viewModel, int feedNumber, byte fill)
    {
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(feedNumber);
        int drawn = _sink.Frames.Count;
        feed.Send(fill: fill);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return _sink.Frames.Count > drawn; }, "frame drawn");
        Assert.True(viewModel.IsStreaming);
        return feed;
    }

    [Fact]
    public async Task LockingTheScreenStopsTheStreamAndUnlockingResumesIt()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed first = await StreamingAsync(viewModel, 1, fill: 1);

        await SetScreenVisibleAsync(false);

        Assert.False(viewModel.IsStreaming);
        // The only consumer left: the connection closes (the test hub does not linger).
        await first.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        await SetScreenVisibleAsync(true);

        await StreamingAsync(viewModel, 2, fill: 2);
        Assert.Equal(2, _sink.Frames[^1].First);
    }

    [Fact]
    public async Task ScreenChangeIsAppliedOnTheUiThreadOnly()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        await StreamingAsync(viewModel, 1, fill: 1);

        await Task.Run(() => _display.Set(false), TestContext.Current.CancellationToken);

        // Posted, not applied on the raising thread.
        Assert.True(viewModel.IsStreaming);
        _dispatcher.RunAll();
        Assert.False(viewModel.IsStreaming);
        Assert.False(viewModel.IsScreenVisible);
    }

    [Fact]
    public async Task CloakedWindowStopsTheStreamAndUncloakingResumesIt()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed first = await StreamingAsync(viewModel, 1, fill: 1);

        viewModel.SetCloaked(true);

        Assert.False(viewModel.IsStreaming);
        await first.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        viewModel.SetCloaked(false);

        await StreamingAsync(viewModel, 2, fill: 3);
    }

    [Fact]
    public async Task WindowOpenedWhileTheScreenIsLockedStartsOnlyAfterUnlock()
    {
        _display.Set(false);
        CameraViewModel viewModel = Create();

        viewModel.SetActive(true);

        Assert.False(viewModel.IsStreaming);
        Assert.Empty(_factory.Uris);

        await SetScreenVisibleAsync(true);

        await StreamingAsync(viewModel, 1, fill: 1);
    }

    [Fact]
    public async Task CloakedWindowStaysPausedWhenTheScreenComesBack()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        await StreamingAsync(viewModel, 1, fill: 1);

        viewModel.SetCloaked(true);
        await SetScreenVisibleAsync(false);
        await SetScreenVisibleAsync(true);

        Assert.False(viewModel.IsStreaming);
        viewModel.SetCloaked(false);
        await StreamingAsync(viewModel, 2, fill: 2);
    }

    [Fact]
    public async Task MinimizedWindowStaysPausedWhenTheScreenComesBack()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        await StreamingAsync(viewModel, 1, fill: 1);
        viewModel.SetActive(false);

        await SetScreenVisibleAsync(false);
        viewModel.SetCloaked(true);
        await SetScreenVisibleAsync(true);
        viewModel.SetCloaked(false);

        Assert.False(viewModel.IsStreaming);
        Assert.Single(_factory.Feeds);
    }

    [Fact]
    public async Task RecordingTimelapseAndDetectionLeasesGoOnWhileTheScreenIsLocked()
    {
        // The recording and the dashcam read packets, the timelapse and detection take frames of their own (T-145, T-153).
        using CameraPacketSubscription recording = _hub.SubscribePackets("mk4", StreamUri);
        int detected = 0;
        using CameraFrameSubscription detection = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions { FrameReceived = (in CameraFrame _) => Interlocked.Increment(ref detected) });
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await StreamingAsync(viewModel, 1, fill: 1);
        int drawn = _sink.Frames.Count;

        await SetScreenVisibleAsync(false);
        int before = Volatile.Read(ref detected);
        feed.Send(fill: 2);
        feed.Send(fill: 3);
        await Waiting.UntilAsync(() => Volatile.Read(ref detected) > before && feed.Consumed >= 3, "frames go on for detection");
        _dispatcher.RunAll();

        Assert.False(viewModel.IsStreaming);
        Assert.Equal(drawn, _sink.Frames.Count);
        Assert.False(feed.IsDisposed);
        Assert.False(recording.IsEnded);
        Assert.False(detection.IsEnded);

        await SetScreenVisibleAsync(true);
        feed.Send(fill: 4);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return _sink.Frames.Count > drawn; }, "frame drawn again");

        // The same connection all along.
        Assert.Single(_factory.Feeds);
        Assert.Equal(4, _sink.Frames[^1].First);
    }

    [Fact]
    public void DisposedWindowStopsFollowingTheScreen()
    {
        CameraViewModel viewModel = Create();
        Assert.Equal(1, _display.Subscribers);

        viewModel.Dispose();

        Assert.Equal(0, _display.Subscribers);
    }

    [Fact]
    public async Task WithoutADisplayServiceTheScreenIsAlwaysVisible()
    {
        CameraViewModel viewModel = Create(AlwaysVisibleDisplayActivity.Instance);

        viewModel.SetActive(true);

        Assert.True(viewModel.IsScreenVisible);
        await StreamingAsync(viewModel, 1, fill: 1);
    }

    private sealed class FakeDisplayActivity : IDisplayActivityService
    {
        private readonly Lock _gate = new();
        private EventHandler? _changed;
        private bool _visible = true;

        public bool IsScreenVisible
        {
            get
            {
                lock (_gate)
                {
                    return _visible;
                }
            }
        }

        public int Subscribers
        {
            get
            {
                lock (_gate)
                {
                    return _changed?.GetInvocationList().Length ?? 0;
                }
            }
        }

        public event EventHandler? ScreenVisibilityChanged
        {
            add
            {
                lock (_gate)
                {
                    _changed += value;
                }
            }

            remove
            {
                lock (_gate)
                {
                    _changed -= value;
                }
            }
        }

        public void Set(bool visible)
        {
            EventHandler? changed;
            lock (_gate)
            {
                if (_visible == visible)
                {
                    return;
                }

                _visible = visible;
                changed = _changed;
            }

            changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
