using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// T-188 (user decision 2026-11-11): the camera window says roughly when the next reconnect attempt starts, computed once
/// per status from <see cref="CameraStatus.NextAttemptAt"/> on the fake clock (no timer), not in the mini preview; opening
/// or restoring the window ends the wait of a connection another consumer keeps.
/// </summary>
public sealed class CameraViewModelNextAttemptTests : IAsyncDisposable
{
    private const char Nbsp = ' ';
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeCameraFeedFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakePrinterMonitor _monitor = new();
    private readonly MonitoredPrinters _printers = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly MemoryCameraWindowStateStore _states = new();
    private readonly RecordingFrameSink _sink = new();
    private readonly CameraStreamHub _hub;
    private CameraViewModel? _viewModel;

    public CameraViewModelNextAttemptTests()
    {
        _hub = CameraHubs.Immediate(new ReconnectingCameraFeed(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default));
    }

    public async ValueTask DisposeAsync()
    {
        if (_viewModel is { } viewModel)
        {
            viewModel.Dispose();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }

        await _hub.DisposeAsync();
        _culture.Dispose();
    }

    public static TheoryData<double, string> Rounding => new()
    {
        { 0.2, $"Next attempt in about 1{Nbsp}s" },
        { 4.98, $"Next attempt in about 5{Nbsp}s" },
        { 5, $"Next attempt in about 5{Nbsp}s" },
        { 39.5, $"Next attempt in about 40{Nbsp}s" },
        { 58.7, $"Next attempt in about 59{Nbsp}s" },
        { 59.2, $"Next attempt in about 1{Nbsp}min" },
        { 60, $"Next attempt in about 1{Nbsp}min" },
        { 89, $"Next attempt in about 1{Nbsp}min" },
        { 150, $"Next attempt in about 3{Nbsp}min" },
    };

    [Theory]
    [MemberData(nameof(Rounding))]
    public void SecondsBelowAMinuteAndWholeMinutesFromAMinuteOn(double seconds, string expected) =>
        Assert.Equal(expected, CameraViewModel.FormatNextAttempt(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public async Task FailureShowsTheDelayToTheNextAttemptAndConnectingClearsIt()
    {
        CameraViewModel viewModel = Create();
        Assert.Null(viewModel.NextAttemptText);
        viewModel.SetActive(true);
        FakeCameraFeed first = await _factory.WaitForFeedAsync(1);

        first.Fail(new CameraFeedException("Connection refused."));
        await UntilAsync(() => viewModel.HasNextAttempt, "next attempt shown");
        Assert.Equal($"Next attempt in about 5{Nbsp}s", viewModel.NextAttemptText);
        Assert.Equal("Connection refused.", viewModel.StatusDetail);

        // No timer: the text stays as computed while time passes.
        int posts = _dispatcher.PostCount;
        _time.Advance(TimeSpan.FromSeconds(3));
        _dispatcher.RunAll();
        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal($"Next attempt in about 5{Nbsp}s", viewModel.NextAttemptText);

        // The attempt starts: connecting, no time to show; its failure shows the doubled delay.
        _time.Advance(TimeSpan.FromSeconds(2));
        FakeCameraFeed second = await _factory.WaitForFeedAsync(2);
        await UntilAsync(() => viewModel.StatusText == "Connecting to the camera…", "connecting");
        Assert.Null(viewModel.NextAttemptText);
        Assert.False(viewModel.HasNextAttempt);

        second.Fail(new CameraFeedException("Connection refused."));
        await UntilAsync(() => viewModel.HasNextAttempt, "next attempt shown again");
        Assert.Equal($"Next attempt in about 10{Nbsp}s", viewModel.NextAttemptText);
    }

    [Fact]
    public async Task StreamingClearsTheNextAttempt()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        (await _factory.WaitForFeedAsync(1)).End();
        await UntilAsync(() => viewModel.HasNextAttempt, "no signal with the next attempt");
        Assert.Equal("No signal", viewModel.StatusText);

        _time.Advance(CameraFeedOptions.Default.ReconnectDelay);
        (await _factory.WaitForFeedAsync(2)).Send();
        await UntilAsync(() => !viewModel.IsStatusVisible, "streaming");

        Assert.Null(viewModel.NextAttemptText);
        Assert.False(viewModel.HasNextAttempt);
    }

    [Fact]
    public async Task MiniPreviewHidesTheNextAttemptAndTheNormalWindowShowsItAgain()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        (await _factory.WaitForFeedAsync(1)).Fail(new CameraFeedException("Connection refused."));
        await UntilAsync(() => viewModel.HasNextAttempt, "next attempt shown");
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.ShowMiniCommand.Execute(null);
        Assert.Null(viewModel.NextAttemptText);
        Assert.False(viewModel.HasNextAttempt);
        Assert.Contains(nameof(CameraViewModel.HasNextAttempt), changed);

        // The value computed with the status, not a new one: time passed meanwhile.
        _time.Advance(TimeSpan.FromSeconds(2));
        viewModel.ShowNormalCommand.Execute(null);
        Assert.Equal($"Next attempt in about 5{Nbsp}s", viewModel.NextAttemptText);
        viewModel.ShowFullScreenCommand.Execute(null);
        Assert.True(viewModel.HasNextAttempt);
    }

    [Fact]
    public async Task LanguageSwitchFormatsTheNextAttemptAgain()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        (await _factory.WaitForFeedAsync(1)).Fail(new CameraFeedException("Connection refused."));
        await UntilAsync(() => viewModel.HasNextAttempt, "next attempt shown");
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        using (new UiCultureScope("cs"))
        {
            viewModel.RefreshTexts();

            Assert.Equal($"Další pokus asi za 5{Nbsp}s", viewModel.NextAttemptText);
            Assert.Contains(nameof(CameraViewModel.NextAttemptText), changed);
        }
    }

    [Fact]
    public async Task OpeningTheWindowDuringTheWaitOfAnotherConsumerRetriesAtOnce()
    {
        // Recording keeps the connection, which fails and waits 5 s, then 10 s.
        await using CameraPacketSubscription recording = _hub.SubscribePackets("mk4", StreamUri);
        (await _factory.WaitForFeedAsync(1)).Fail(new CameraFeedException("Connection refused."));
        await Waiting.UntilAsync(() => recording.Status.NextAttemptAt is not null, "waiting");
        _time.Advance(CameraFeedOptions.Default.ReconnectDelay);
        (await _factory.WaitForFeedAsync(2)).Fail(new CameraFeedException("Connection refused."));
        await Waiting.UntilAsync(() => recording.Status.NextAttemptAt == _time.GetUtcNow() + TimeSpan.FromSeconds(10), "waiting 10 s");

        // Opened: no time passes, the attempt starts now and the backoff starts over.
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed third = await _factory.WaitForFeedAsync(3);
        _dispatcher.RunAll();
        Assert.Equal("Connecting to the camera…", viewModel.StatusText);
        third.Fail(new CameraFeedException("Connection refused."));
        await UntilAsync(() => viewModel.HasNextAttempt, "next attempt shown");
        Assert.Equal($"Next attempt in about 5{Nbsp}s", viewModel.NextAttemptText);

        // Mode switches of the shown window do not shorten the wait.
        viewModel.ShowMiniCommand.Execute(null);
        viewModel.ShowNormalCommand.Execute(null);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(3, _factory.Feeds.Count);

        // Minimized and restored: retries at once again.
        viewModel.SetActive(false);
        viewModel.SetActive(true);
        await _factory.WaitForFeedAsync(4);
    }

    private CameraViewModel Create()
    {
        PrinterDefinition printer = Definition("mk4", "MK4") with { Camera = new CameraSettings(enabled: true, StreamUri) };
        _viewModel = new(CameraTarget.From(printer)!, _hub, _monitor, _printers, _dispatcher, _states, _time, CameraFeedOptions.Default, NullLogger<CameraViewModel>.Instance);
        _viewModel.AttachFrameSink(_sink);
        return _viewModel;
    }

    /// <summary>Runs the posted UI work until <paramref name="condition"/> holds.</summary>
    private Task UntilAsync(Func<bool> condition, string description) =>
        Waiting.UntilAsync(() => { _dispatcher.RunAll(); return condition(); }, description);
}
