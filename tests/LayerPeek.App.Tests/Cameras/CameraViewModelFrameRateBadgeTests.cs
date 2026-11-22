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
/// T-251 (user decisions 2026-11-22): the mini preview shows the frame rate it really shows and its limit ("14 / 15 fps",
/// "24 fps / no limit" for Smooth) while the picture streams. The real rate counts the frames shown over the last 2 s and
/// is refreshed once a second (a dash until the first refresh); the timer exists only while the badge is visible. The
/// option of the settings (on by default) applies to the open window at once. The hub runs on the same fake clock as the
/// window, so the limit of the mini preview lets the frames of the test through.
/// </summary>
public sealed class CameraViewModelFrameRateBadgeTests : IAsyncDisposable
{
    private const string Dash = "\u2007\u2012";

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

    public CameraViewModelFrameRateBadgeTests()
    {
        _hub = new CameraStreamHub(
            new ReconnectingCameraFeed(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default),
            _time,
            NullLogger<CameraStreamHub>.Instance,
            TimeSpan.Zero);
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

    /// <summary>"Save" in the settings: the monitoring run applies them, the window on the UI thread.</summary>
    private void Save(int? rate = MiniPreviewFrameRate.Default, bool badge = true)
    {
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer()], MiniPreviewFrameRate = rate, ShowMiniPreviewFrameRate = badge });
        _dispatcher.RunAll();
    }

    /// <summary>A streaming mini preview whose first frame is shown.</summary>
    private async Task<(CameraViewModel ViewModel, FakeCameraFeed Feed)> StreamingMiniAsync()
    {
        CameraViewModel viewModel = Create();
        viewModel.ShowMiniCommand.Execute(null);
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        await ShowFrameAsync(feed);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return viewModel.HasFrame && !viewModel.IsStatusVisible; }, "streaming");
        return (viewModel, feed);
    }

    /// <summary>Sends a frame and waits until the window showed it.</summary>
    private async Task ShowFrameAsync(FakeCameraFeed feed)
    {
        int shown = _sink.Frames.Count;
        feed.Send(1280, 720);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return _sink.Frames.Count > shown; }, "frame shown");
    }

    /// <summary>Shows frames at <paramref name="fps"/> on the fake clock for <paramref name="seconds"/> (the timer refreshes on the way).</summary>
    private async Task StreamAsync(FakeCameraFeed feed, int fps, double seconds)
    {
        TimeSpan interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / fps);
        for (int i = 0; i < (int)Math.Round(fps * seconds); i++)
        {
            _time.Advance(interval);
            _dispatcher.RunAll();
            await ShowFrameAsync(feed);
        }
    }

    private void Advance(TimeSpan by)
    {
        _time.Advance(by);
        _dispatcher.RunAll();
    }

    [Fact]
    public async Task TheBadgeShowsADashThenTheShownFrameRateAndTheLimit()
    {
        (CameraViewModel viewModel, FakeCameraFeed feed) = await StreamingMiniAsync();

        Assert.True(viewModel.IsFrameRateBadgeVisible);
        Assert.Equal($"{Dash} / 15\u00A0fps", viewModel.FrameRateBadgeText);
        Assert.Equal("Frame rate: measuring, limit 15\u00A0fps", viewModel.FrameRateBadgeName);

        await StreamAsync(feed, fps: 10, seconds: 2.5);

        Assert.Equal("10 / 15\u00A0fps", viewModel.FrameRateBadgeText);
        Assert.Equal("Frame rate 10 of 15\u00A0fps", viewModel.FrameRateBadgeName);
    }

    [Fact]
    public async Task AOneDigitRateIsPaddedToTheWidthOfTwoDigits()
    {
        (CameraViewModel viewModel, FakeCameraFeed feed) = await StreamingMiniAsync();

        await StreamAsync(feed, fps: 5, seconds: 2.5);

        Assert.Equal("\u20075 / 15\u00A0fps", viewModel.FrameRateBadgeText);
        // The screen reader gets the plain number.
        Assert.Equal("Frame rate 5 of 15\u00A0fps", viewModel.FrameRateBadgeName);
    }

    [Fact]
    public async Task SmoothShowsNoLimit()
    {
        Save(rate: null);
        (CameraViewModel viewModel, FakeCameraFeed feed) = await StreamingMiniAsync();

        await StreamAsync(feed, fps: 25, seconds: 2.5);

        Assert.Equal("25\u00A0fps / no limit", viewModel.FrameRateBadgeText);
        Assert.Equal("Frame rate 25\u00A0fps, no limit", viewModel.FrameRateBadgeName);
    }

    [Fact]
    public async Task TheValueChangesAtMostOnceASecond()
    {
        (CameraViewModel viewModel, FakeCameraFeed feed) = await StreamingMiniAsync();
        List<TimeSpan> changes = [];
        DateTimeOffset start = _time.GetUtcNow();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CameraViewModel.FrameRateBadgeText))
            {
                changes.Add(_time.GetUtcNow() - start);
            }
        };

        // A rate that keeps changing: 15, then 5, then 12 fps.
        await StreamAsync(feed, fps: 15, seconds: 2);
        await StreamAsync(feed, fps: 5, seconds: 2);
        await StreamAsync(feed, fps: 12, seconds: 2);

        // Only the refresh due each whole second changes the text. It runs with the next step of the test's clock (at most
        // one frame interval, 0.2 s at 5 fps, after it was due), and at most once per second.
        Assert.NotEmpty(changes);
        string all = string.Join(", ", changes);
        List<int> seconds = [];
        foreach (TimeSpan change in changes)
        {
            int second = (int)Math.Floor(change.TotalSeconds);
            Assert.True(second >= 1 && change.TotalSeconds - second <= 0.2 + 1e-6, $"changes at {all}");
            seconds.Add(second);
        }

        Assert.Equal(seconds.Distinct().Count(), seconds.Count);
    }

    [Fact]
    public async Task FramesThatStopLetTheValueFallToZero()
    {
        (CameraViewModel viewModel, FakeCameraFeed feed) = await StreamingMiniAsync();
        await StreamAsync(feed, fps: 10, seconds: 2.5);

        Advance(TimeSpan.FromSeconds(3));

        Assert.True(viewModel.IsFrameRateBadgeVisible);
        Assert.Equal("\u20070 / 15\u00A0fps", viewModel.FrameRateBadgeText);
    }

    [Fact]
    public async Task TheBadgeAndItsTimerExistOnlyInTheStreamingMiniPreview()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        await ShowFrameAsync(feed);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return !viewModel.IsStatusVisible; }, "streaming");

        // The normal window: none.
        Assert.False(viewModel.IsFrameRateBadgeVisible);
        Assert.False(viewModel.HasFrameRateBadgeTimer);

        viewModel.ShowMiniCommand.Execute(null);
        Assert.True(viewModel.IsFrameRateBadgeVisible);
        Assert.True(viewModel.HasFrameRateBadgeTimer);

        // Full screen entered from the mini preview: none.
        viewModel.ShowFullScreenCommand.Execute(null);
        Assert.False(viewModel.IsFrameRateBadgeVisible);
        Assert.False(viewModel.HasFrameRateBadgeTimer);
        viewModel.EscapeCommand.Execute(null);
        Assert.True(viewModel.IsFrameRateBadgeVisible);

        // Minimized: none.
        viewModel.SetActive(false);
        Assert.False(viewModel.IsFrameRateBadgeVisible);
        Assert.False(viewModel.HasFrameRateBadgeTimer);
    }

    [Fact]
    public async Task TheBadgeIsHiddenWhileThePictureDoesNotStream()
    {
        (CameraViewModel viewModel, FakeCameraFeed feed) = await StreamingMiniAsync();
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        feed.Fail(new IOException("lost"));
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return viewModel.IsStatusVisible; }, "reconnecting");

        Assert.False(viewModel.IsFrameRateBadgeVisible);
        Assert.False(viewModel.HasFrameRateBadgeTimer);
        Assert.Contains(nameof(CameraViewModel.IsFrameRateBadgeVisible), changed);
    }

    [Fact]
    public async Task AFreshStartOfThePictureStartsWithADashAgain()
    {
        (CameraViewModel viewModel, FakeCameraFeed feed) = await StreamingMiniAsync();
        await StreamAsync(feed, fps: 10, seconds: 2.5);

        viewModel.SetActive(false);
        viewModel.SetActive(true);
        FakeCameraFeed again = await _factory.WaitForFeedAsync(2);
        await ShowFrameAsync(again);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return viewModel.IsFrameRateBadgeVisible; }, "streaming again");

        Assert.Equal($"{Dash} / 15\u00A0fps", viewModel.FrameRateBadgeText);
    }

    [Fact]
    public async Task ARefreshPostedForAnEarlierShowingChangesNothing()
    {
        (CameraViewModel viewModel, _) = await StreamingMiniAsync();

        // The timer is due and posts its refresh; before it runs, the badge is hidden and shown again.
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(_dispatcher.Pending > 0);
        viewModel.ShowNormalCommand.Execute(null);
        viewModel.ShowMiniCommand.Execute(null);
        Assert.True(viewModel.IsFrameRateBadgeVisible);
        _dispatcher.RunAll();

        // Still the dash of the new measurement, not a value of a second without frames.
        Assert.Equal($"{Dash} / 15\u00A0fps", viewModel.FrameRateBadgeText);
        Assert.Equal("Frame rate: measuring, limit 15\u00A0fps", viewModel.FrameRateBadgeName);
    }

    [Fact]
    public async Task TheHubEndingTheRunHidesTheBadge()
    {
        (CameraViewModel viewModel, _) = await StreamingMiniAsync();

        // The hub stops (quitting) without a failure: the status stays, but there is no run any more.
        await _hub.DisposeAsync();
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return !viewModel.IsStreaming; }, "run ended");

        Assert.False(viewModel.IsFrameRateBadgeVisible);
        Assert.False(viewModel.HasFrameRateBadgeTimer);
    }

    [Fact]
    public async Task SavedSettingsApplyToTheOpenMiniPreviewAtOnce()
    {
        (CameraViewModel viewModel, _) = await StreamingMiniAsync();
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Save(badge: false);
        Assert.False(viewModel.IsFrameRateBadgeVisible);
        Assert.False(viewModel.HasFrameRateBadgeTimer);

        Save(rate: 7, badge: true);
        Assert.True(viewModel.IsFrameRateBadgeVisible);
        Assert.True(viewModel.HasFrameRateBadgeTimer);
        Assert.Equal($"{Dash} / 7\u00A0fps", viewModel.FrameRateBadgeText);

        Save(rate: 20, badge: true);
        Assert.EndsWith("/ 20\u00A0fps", viewModel.FrameRateBadgeText, StringComparison.Ordinal);
        Assert.Contains(nameof(CameraViewModel.FrameRateBadgeText), changed);
        Assert.Contains(nameof(CameraViewModel.FrameRateBadgeName), changed);
    }

    [Fact]
    public async Task TheTextsFollowTheLanguage()
    {
        Save(rate: null);
        (CameraViewModel viewModel, _) = await StreamingMiniAsync();

        using (new CultureScope("cs-CZ"))
        {
            viewModel.RefreshTexts();

            Assert.Equal($"{Dash}\u00A0fps / bez limitu", viewModel.FrameRateBadgeText);
            Assert.StartsWith("Snímková frekvence", viewModel.FrameRateBadgeName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DisposingStopsTheTimer()
    {
        (CameraViewModel viewModel, _) = await StreamingMiniAsync();

        viewModel.Dispose();

        Assert.False(viewModel.HasFrameRateBadgeTimer);
        Assert.False(viewModel.IsFrameRateBadgeVisible);
        // A refresh due later does nothing.
        Advance(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShowingFramesWithTheBadgeOnDoesNotAllocateOnTheUiThread()
    {
        (_, FakeCameraFeed feed) = await StreamingMiniAsync();
        await StreamAsync(feed, fps: 10, seconds: 1.5);
        // Room in the test sink's list, so that its growth is not counted.
        _sink.Frames.Capacity = _sink.Frames.Count + 16;

        // The UI callback of one frame, measured on this thread (no refresh due in between).
        _time.Advance(TimeSpan.FromMilliseconds(100));
        int shown = _sink.Frames.Count;
        feed.Send(1280, 720);
        await Waiting.UntilAsync(() => _dispatcher.Pending > 0, "frame queued");
        long before = GC.GetAllocatedBytesForCurrentThread();
        _dispatcher.RunAll();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(_sink.Frames.Count > shown);
        Assert.Equal(0, allocated);
    }
}
