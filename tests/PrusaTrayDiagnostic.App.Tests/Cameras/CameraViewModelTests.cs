using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

public sealed class CameraViewModelTests : IAsyncDisposable
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

    public async ValueTask DisposeAsync()
    {
        foreach (CameraViewModel viewModel in _created)
        {
            viewModel.Dispose();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }

        _culture.Dispose();
    }

    private static PrinterDefinition CameraPrinter(string id = "mk4", string name = "MK4", string url = "rtsp://192.168.0.227/live") =>
        Definition(id, name) with { Camera = new CameraSettings(enabled: true, new Uri(url)) };

    private CameraViewModel Create(string id = "mk4", string name = "MK4", IUiDispatcher? dispatcher = null, ListLogger<CameraViewModel>? logger = null)
    {
        ReconnectingCameraFeed feed = new(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraViewModel viewModel = new(new CameraTarget(id, name, StreamUri), feed, _monitor, _printers, dispatcher ?? _dispatcher, _states, _time, CameraFeedOptions.Default, (Microsoft.Extensions.Logging.ILogger<CameraViewModel>?)logger ?? NullLogger<CameraViewModel>.Instance);
        _created.Add(viewModel);
        viewModel.AttachFrameSink(_sink);
        return viewModel;
    }

    internal static string? Line(CameraViewModel viewModel, CameraOverlayItems item) =>
        viewModel.OverlayLines.FirstOrDefault(line => line.Item == item)?.Text;

    private static string? StateLine(CameraViewModel viewModel) => Line(viewModel, CameraOverlayItems.State);

    private static string? RemainingLine(CameraViewModel viewModel) => Line(viewModel, CameraOverlayItems.RemainingTime);

    private void ApplyPrinters(params PrinterDefinition[] printers) =>
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = printers });

    // ---- Stream lifetime ----

    [Fact]
    public void NothingStreamsBeforeTheWindowIsActive()
    {
        CameraViewModel viewModel = Create();

        Assert.False(viewModel.IsStreaming);
        Assert.Empty(_factory.Uris);
        Assert.True(viewModel.IsStatusVisible);
        Assert.Equal("Connecting to the camera…", viewModel.StatusText);
    }

    [Fact]
    public async Task ActiveWindowStreamsFramesIntoTheSink()
    {
        CameraViewModel viewModel = Create();

        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        feed.Send(width: 8, height: 4, fill: 5);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return _sink.Frames.Count > 0; }, "frame drawn");
        _dispatcher.RunAll();

        Assert.Equal(StreamUri, Assert.Single(_factory.Uris));
        Assert.Equal((8, 4, (byte)5), Assert.Single(_sink.Frames));
        Assert.True(viewModel.HasFrame);
        Assert.False(viewModel.IsStatusVisible);
    }

    [Fact]
    public async Task BusyUiIsNotifiedOnceAndShowsTheNewestFrame()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        feed.Send(fill: 1);
        feed.Send(fill: 2);
        feed.Send(fill: 3);
        // The UI is busy (nothing runs) until the stream handed over all three frames.
        await Waiting.UntilAsync(() => feed.Consumed >= 3, "three frames consumed");
        _dispatcher.RunAll();

        // Posts: "connecting" and "streaming" statuses and a single frame callback; only the newest frame is drawn.
        Assert.Equal(3, _dispatcher.PostCount);
        Assert.Equal((4, 2, (byte)3), Assert.Single(_sink.Frames));
    }

    [Fact]
    public async Task MinimizingStopsTheStreamAndRestoringReconnects()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed first = await _factory.WaitForFeedAsync(1);

        viewModel.SetActive(false);
        await first.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        Assert.False(viewModel.IsStreaming);

        viewModel.SetActive(true);
        FakeCameraFeed second = await _factory.WaitForFeedAsync(2);

        Assert.True(viewModel.IsStreaming);
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public async Task DisposingClosesTheConnection()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        viewModel.Dispose();

        await feed.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await feed.EnumerationEnded.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        Assert.Single(_factory.Feeds);
    }

    [Fact]
    public async Task FramesAfterDisposeAreNotDrawn()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        feed.Send();
        await Waiting.UntilAsync(() => feed.Consumed >= 1, "frame published");

        viewModel.Dispose();
        _dispatcher.RunAll();

        Assert.Empty(_sink.Frames);
    }

    // ---- Status overlay ----

    [Fact]
    public async Task StreamFailureShowsErrorWithReasonAndKeepsLastFrame()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        feed.Send();
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return _sink.Frames.Count > 0; }, "frame drawn");
        _dispatcher.RunAll();

        feed.Fail(new CameraFeedException("Decoding failed."));
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return viewModel.StatusText == "Camera error"; }, "error shown");
        _dispatcher.RunAll();

        Assert.True(viewModel.IsStatusVisible);
        Assert.Equal("Camera error", viewModel.StatusText);
        Assert.Equal("Decoding failed.", viewModel.StatusDetail);
        Assert.True(viewModel.HasFrame);
    }

    [Fact]
    public async Task EndedStreamShowsNoSignalAndReconnectsAfterTheDelay()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        feed.End();
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return viewModel.StatusText == "No signal"; }, "no signal shown");
        _dispatcher.RunAll();
        Assert.Equal("No signal", viewModel.StatusText);
        Assert.Equal("The camera ended the stream.", viewModel.StatusDetail);

        _time.Advance(CameraFeedOptions.Default.ReconnectDelay);
        await _factory.WaitForFeedAsync(2);
    }

    [Fact]
    public async Task StatusOfAStoppedRunIsIgnored()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        feed.Send();
        await Waiting.UntilAsync(() => _dispatcher.PostCount >= 2, "streaming status posted");

        viewModel.SetActive(false);
        _dispatcher.RunAll();

        // Paused: the overlay shows "Connecting…" for the next start, not "Streaming" of the stopped run.
        Assert.True(viewModel.IsStatusVisible);
    }

    // ---- Modes ----

    [Fact]
    public void ModesFollowCommandsAndDoubleClick()
    {
        CameraViewModel viewModel = Create();
        Assert.True(viewModel.IsNormal);
        Assert.False(viewModel.ShowNormalCommand.CanExecute(null));

        viewModel.ToggleFullScreenCommand.Execute(null);
        Assert.True(viewModel.IsFullScreen);

        viewModel.ShowNormalCommand.Execute(null);
        Assert.True(viewModel.IsNormal);

        viewModel.OnImageDoubleClicked();
        Assert.True(viewModel.IsFullScreen);
        viewModel.OnImageDoubleClicked();
        Assert.True(viewModel.IsNormal);

        viewModel.ShowMiniCommand.Execute(null);
        Assert.True(viewModel.IsMini);
        Assert.False(viewModel.ShowMiniCommand.CanExecute(null));
        viewModel.OnImageDoubleClicked();
        Assert.True(viewModel.IsNormal);
    }

    [Fact]
    public void EveryModeOffersTheOtherTwo()
    {
        CameraViewModel viewModel = Create();
        AssertOffers(viewModel, fullScreen: true, normal: false, mini: true);

        viewModel.ShowFullScreenCommand.Execute(null);
        Assert.True(viewModel.IsFullScreen);
        AssertOffers(viewModel, fullScreen: false, normal: true, mini: true);

        viewModel.ShowMiniCommand.Execute(null);
        Assert.True(viewModel.IsMini);
        AssertOffers(viewModel, fullScreen: true, normal: true, mini: false);

        static void AssertOffers(CameraViewModel viewModel, bool fullScreen, bool normal, bool mini)
        {
            Assert.Equal(fullScreen, viewModel.ShowFullScreenCommand.CanExecute(null));
            Assert.Equal(normal, viewModel.ShowNormalCommand.CanExecute(null));
            Assert.Equal(mini, viewModel.ShowMiniCommand.CanExecute(null));
        }
    }

    [Fact]
    public void FullScreenEnteredFromTheMiniPreviewGoesStraightToTheNormalWindow()
    {
        CameraViewModel viewModel = Create();
        viewModel.ShowMiniCommand.Execute(null);
        viewModel.ShowFullScreenCommand.Execute(null);

        viewModel.ShowNormalCommand.Execute(null);

        Assert.True(viewModel.IsNormal);
        // Full screen entered again from the normal window returns there.
        viewModel.ShowFullScreenCommand.Execute(null);
        viewModel.EscapeCommand.Execute(null);
        Assert.True(viewModel.IsNormal);
    }

    [Fact]
    public void FullScreenEnteredFromTheNormalWindowGoesStraightToTheMiniPreview()
    {
        CameraViewModel viewModel = Create();
        viewModel.ShowFullScreenCommand.Execute(null);

        viewModel.ShowMiniCommand.Execute(null);

        Assert.True(viewModel.IsMini);
        // Full screen entered again from the mini preview returns there.
        viewModel.ToggleFullScreenCommand.Execute(null);
        viewModel.ToggleFullScreenCommand.Execute(null);
        Assert.True(viewModel.IsMini);
    }

    [Fact]
    public void NormalWindowTipMentionsTheDoubleClickOnlyWhereItLeadsThere()
    {
        CameraViewModel viewModel = Create();
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.ShowFullScreenCommand.Execute(null);
        Assert.Equal("Back to the normal window (double-click)", viewModel.NormalToolTip);

        viewModel.ShowMiniCommand.Execute(null);
        Assert.Equal("Back to the normal window (double-click)", viewModel.NormalToolTip);

        // Full screen from the mini preview: a double-click (and Esc) goes back to the mini preview.
        changed.Clear();
        viewModel.ShowFullScreenCommand.Execute(null);
        Assert.Equal("Normal window", viewModel.NormalToolTip);
        Assert.Contains(nameof(CameraViewModel.NormalToolTip), changed);

        changed.Clear();
        viewModel.ShowMiniCommand.Execute(null);
        Assert.Equal("Back to the normal window (double-click)", viewModel.NormalToolTip);
        Assert.Contains(nameof(CameraViewModel.NormalToolTip), changed);
    }

    [Fact]
    public void OsFullScreenChangeIsFollowed()
    {
        CameraViewModel viewModel = Create();

        viewModel.OnWindowFullScreenChanged(true);
        Assert.True(viewModel.IsFullScreen);

        viewModel.OnWindowFullScreenChanged(false);
        Assert.True(viewModel.IsNormal);
    }

    [Fact]
    public void ModeChangeRaisesDependentProperties()
    {
        CameraViewModel viewModel = Create();
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.ShowMiniCommand.Execute(null);

        Assert.Contains(nameof(CameraViewModel.Mode), changed);
        Assert.Contains(nameof(CameraViewModel.IsMini), changed);
    }

    // ---- Remembered state ----

    [Fact]
    public void StateIsRememberedOnDisposeAndRestored()
    {
        CameraViewModel first = Create();
        first.RememberPlacement(CameraWindowMode.Normal, new CameraWindowPlacement(10, 20, 800, 450));
        first.ShowMiniCommand.Execute(null);
        first.RememberPlacement(CameraWindowMode.Mini, new CameraWindowPlacement(1500, 900, 320, 180));
        first.RememberPlacement(CameraWindowMode.FullScreen, new CameraWindowPlacement(0, 0, 1920, 1080));
        first.Dispose();

        Assert.Equal(
            new CameraWindowState(CameraWindowMode.Mini, new CameraWindowPlacement(10, 20, 800, 450), new CameraWindowPlacement(1500, 900, 320, 180)),
            _states.Get("MK4"));

        CameraViewModel second = Create();
        Assert.True(second.IsMini);
        Assert.Equal(new CameraWindowPlacement(10, 20, 800, 450), second.NormalPlacement);
        Assert.Equal(new CameraWindowPlacement(1500, 900, 320, 180), second.MiniPlacement);
    }

    [Fact]
    public void InvalidPlacementIsIgnored()
    {
        CameraViewModel viewModel = Create();

        viewModel.RememberPlacement(CameraWindowMode.Normal, new CameraWindowPlacement(0, 0, double.NaN, 100));

        Assert.Null(viewModel.NormalPlacement);
    }

    [Fact]
    public void WindowsOfDifferentPrintersAreRememberedSeparately()
    {
        CameraViewModel alpha = Create("a", "Alpha");
        CameraViewModel beta = Create("b", "Beta");
        alpha.ShowMiniCommand.Execute(null);

        alpha.Dispose();
        beta.Dispose();

        Assert.Equal(CameraWindowMode.Mini, _states.Get("a").Mode);
        Assert.Equal(CameraWindowMode.Normal, _states.Get("b").Mode);
    }

    // ---- Language switch (T-029) ----

    [Fact]
    public void RefreshTextsFormatsTitleStatusTipAndPrintOverlayInTheNewLanguage()
    {
        CameraViewModel viewModel = Create(name: "MK4");
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42.7, TimeSpan.FromMinutes(65))));
        _dispatcher.RunAll();
        Assert.Equal("MK4 – Camera", viewModel.Title);
        List<string?> changes = [];
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        using (new CultureScope("de-DE"))
        {
            viewModel.RefreshTexts();

            Assert.Equal("MK4 – Kamera", viewModel.Title);
            Assert.Equal("Verbindung zur Kamera wird hergestellt…", viewModel.StatusText);
            Assert.Equal("Zurück zum normalen Fenster (Doppelklick)", viewModel.NormalToolTip);
            Assert.StartsWith("Druckt – 42", StateLine(viewModel), StringComparison.Ordinal);
            Assert.StartsWith("Noch ", RemainingLine(viewModel), StringComparison.Ordinal);
        }

        Assert.Contains(nameof(CameraViewModel.Title), changes);
        Assert.Contains(nameof(CameraViewModel.StatusText), changes);
        Assert.Contains(nameof(CameraViewModel.NormalToolTip), changes);
        Assert.Contains(nameof(CameraViewModel.OverlayLines), changes);
        // Nothing else changed: no new connection, the mode stays.
        Assert.True(viewModel.IsNormal);
        Assert.Empty(_factory.Uris);
    }

    [Fact]
    public void RefreshTextsBeforeThePrinterWasPolledKeepsTheOverlayHidden()
    {
        CameraViewModel viewModel = Create();

        using (new CultureScope("de-DE"))
        {
            viewModel.RefreshTexts();
        }

        Assert.Null(StateLine(viewModel));
        Assert.False(viewModel.IsPrintOverlayVisible);
    }

    [Fact]
    public void RefreshTextsAfterDisposeDoesNothing()
    {
        CameraViewModel viewModel = Create();
        viewModel.Dispose();
        List<string?> changes = [];
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        viewModel.RefreshTexts();

        Assert.Empty(changes);
    }

    // ---- Print overlay ----

    [Fact]
    public void MiniPreviewShowsProgressAndRemainingTime()
    {
        CameraViewModel viewModel = Create();
        viewModel.ShowMiniCommand.Execute(null);

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42.7, TimeSpan.FromMinutes(65))));
        _dispatcher.RunAll();

        Assert.True(viewModel.IsPrintOverlayVisible);
        Assert.Equal("Printing – 42%", StateLine(viewModel));
        Assert.Equal("1 h 5 min remaining", RemainingLine(viewModel));
        Assert.NotNull(RemainingLine(viewModel));
    }

    [Theory]
    [InlineData(CameraWindowMode.Normal)]
    [InlineData(CameraWindowMode.FullScreen)]
    [InlineData(CameraWindowMode.Mini)]
    public void PrintOverlayIsShownInEveryMode(CameraWindowMode mode)
    {
        CameraViewModel viewModel = Create();
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10, TimeSpan.FromMinutes(5))));
        _dispatcher.RunAll();

        if (mode == CameraWindowMode.FullScreen)
        {
            viewModel.ToggleFullScreenCommand.Execute(null);
        }
        else if (mode == CameraWindowMode.Mini)
        {
            viewModel.ShowMiniCommand.Execute(null);
        }

        Assert.Equal(mode, viewModel.Mode);
        Assert.True(viewModel.IsPrintOverlayVisible);
        Assert.Equal("Printing – 10%", StateLine(viewModel));
        Assert.Equal("5 min remaining", RemainingLine(viewModel));
    }

    [Fact]
    public void PrintOverlayIsHiddenBeforeThePrinterWasPolled()
    {
        CameraViewModel viewModel = Create();
        viewModel.ShowMiniCommand.Execute(null);

        Assert.False(viewModel.IsPrintOverlayVisible);
        Assert.Null(StateLine(viewModel));
    }

    [Fact]
    public void OverlayStartsWithTheLatestSnapshot()
    {
        _monitor.Raise(Snapshot(PrinterState.Idle));

        CameraViewModel viewModel = Create();

        Assert.Equal("Ready", StateLine(viewModel));
        Assert.Null(RemainingLine(viewModel));
        // Shown in every mode (user decision 2026-10-18), also in the normal window.
        Assert.True(viewModel.IsPrintOverlayVisible);
    }

    [Fact]
    public void SnapshotsOfOtherPrintersAndInvisibleChangesPostNothing()
    {
        CameraViewModel viewModel = Create();
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42.1)));
        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;

        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42.9)));
        _monitor.Raise(Snapshot(PrinterState.Error, printerId: "other"));

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal("Printing – 42%", StateLine(viewModel));
    }

    [Fact]
    public void DisposeUnsubscribesFromMonitor()
    {
        CameraViewModel viewModel = Create();
        Assert.Equal(1, _monitor.SubscriberCount);

        viewModel.Dispose();

        Assert.Equal(0, _monitor.SubscriberCount);
    }

    // ---- Settings changes ----

    [Fact]
    public void DisabledCameraClosesTheWindow()
    {
        CameraViewModel viewModel = Create();
        int closeRequests = 0;
        viewModel.CloseRequested += (_, _) => closeRequests++;

        ApplyPrinters(Definition());
        _dispatcher.RunAll();

        Assert.Equal(1, closeRequests);
    }

    [Fact]
    public void RemovedPrinterClosesTheWindow()
    {
        CameraViewModel viewModel = Create();
        int closeRequests = 0;
        viewModel.CloseRequested += (_, _) => closeRequests++;

        ApplyPrinters(CameraPrinter("other", "Other"));
        _dispatcher.RunAll();

        Assert.Equal(1, closeRequests);
    }

    [Fact]
    public void RenamedPrinterUpdatesTheTitle()
    {
        CameraViewModel viewModel = Create();
        Assert.Equal("MK4 – Camera", viewModel.Title);
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        ApplyPrinters(CameraPrinter(name: "Workshop"));
        _dispatcher.RunAll();

        Assert.Equal("Workshop – Camera", viewModel.Title);
        Assert.Contains(nameof(CameraViewModel.Title), changed);
    }

    [Fact]
    public async Task ChangedAddressRestartsTheStream()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed first = await _factory.WaitForFeedAsync(1);

        ApplyPrinters(CameraPrinter(url: "rtsp://192.168.0.228/live"));
        _dispatcher.RunAll();

        await first.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await _factory.WaitForFeedAsync(2);
        Assert.Equal(new Uri("rtsp://192.168.0.228/live"), _factory.Uris[^1]);
    }

    [Fact]
    public void UnchangedCameraKeepsTheWindowOpen()
    {
        CameraViewModel viewModel = Create();
        int closeRequests = 0;
        viewModel.CloseRequested += (_, _) => closeRequests++;

        ApplyPrinters(CameraPrinter());
        _printers.ReportMonitoringFailed();
        _dispatcher.RunAll();

        Assert.Equal(0, closeRequests);
    }

    [Fact]
    public void TargetNeedsAnEnabledCamera()
    {
        Assert.Null(CameraTarget.From(Definition()));
        Assert.Null(CameraTarget.From(Definition() with { Camera = new CameraSettings(enabled: false, StreamUri) }));
        Assert.Equal(new CameraTarget("mk4", "MK4", StreamUri), CameraTarget.From(CameraPrinter()));
    }

    // ---- Review round 1 ----

    [Fact]
    public async Task FailingSinkDoesNotBreakPausingAndReconnecting()
    {
        CameraViewModel viewModel = Create();
        _sink.FailNext = 1;
        viewModel.SetActive(true);
        FakeCameraFeed first = await _factory.WaitForFeedAsync(1);
        first.Send(fill: 1);
        await Waiting.UntilAsync(() => first.Consumed >= 1, "frame published");
        // The broken bitmap is reported and logged, never thrown into the dispatcher.
        _dispatcher.RunAll();
        Assert.False(viewModel.HasFrame);

        viewModel.SetActive(false);
        await first.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        _dispatcher.RunAll();
        viewModel.SetActive(true);
        FakeCameraFeed second = await _factory.WaitForFeedAsync(2);
        second.Send(fill: 2);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return _sink.Frames.Count == 1; }, "second stream drawn");

        Assert.Equal(2, _sink.Frames[0].First);
        Assert.True(viewModel.IsStreaming);
    }

    [Fact]
    public async Task RunThatEndsByItselfCanBePausedAndRestartedWithoutException()
    {
        // The third post (the first frame, after "connecting" and "streaming") throws: the run ends by itself.
        FaultyDispatcher dispatcher = new(_dispatcher, failingPost: 3);
        CameraViewModel viewModel = Create(dispatcher: dispatcher);
        viewModel.SetActive(true);
        FakeCameraFeed first = await _factory.WaitForFeedAsync(1);

        first.Send();
        await first.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        _dispatcher.RunAll();
        Assert.False(viewModel.IsStreaming);
        Assert.Equal("Camera error", viewModel.StatusText);

        viewModel.SetActive(false);
        viewModel.SetActive(true);
        await _factory.WaitForFeedAsync(2);
        Assert.True(viewModel.IsStreaming);
    }

    /// <summary>A window whose run ended by itself (the third post, the first frame, throws) and shows the error.</summary>
    private async Task<CameraViewModel> RunEndedByItselfAsync()
    {
        CameraViewModel viewModel = Create(dispatcher: new FaultyDispatcher(_dispatcher, failingPost: 3));
        viewModel.SetActive(true);
        FakeCameraFeed first = await _factory.WaitForFeedAsync(1);
        first.Send();
        await first.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        _dispatcher.RunAll();
        Assert.False(viewModel.IsStreaming);
        Assert.Equal("Camera error", viewModel.StatusText);
        return viewModel;
    }

    [Fact]
    public async Task RunEndedByItselfIsRetriedAfterTheReconnectDelay()
    {
        CameraViewModel viewModel = await RunEndedByItselfAsync();

        _time.Advance(CameraFeedOptions.Default.ReconnectDelay - TimeSpan.FromMilliseconds(100));
        _dispatcher.RunAll();
        Assert.Single(_factory.Feeds);

        _time.Advance(TimeSpan.FromMilliseconds(100));
        _dispatcher.RunAll();
        await _factory.WaitForFeedAsync(2);
        Assert.True(viewModel.IsStreaming);
        Assert.Equal("Connecting to the camera…", viewModel.StatusText);
    }

    [Fact]
    public async Task MinimizingCancelsThePendingRetry()
    {
        CameraViewModel viewModel = await RunEndedByItselfAsync();

        viewModel.SetActive(false);
        _time.Advance(CameraFeedOptions.Default.ReconnectDelay * 2);
        _dispatcher.RunAll();

        Assert.Single(_factory.Feeds);
        Assert.False(viewModel.IsStreaming);
    }

    [Fact]
    public async Task ClosingCancelsThePendingRetry()
    {
        CameraViewModel viewModel = await RunEndedByItselfAsync();

        viewModel.Dispose();
        _time.Advance(CameraFeedOptions.Default.ReconnectDelay * 2);
        _dispatcher.RunAll();

        Assert.Single(_factory.Feeds);
    }

    [Fact]
    public async Task AddressChangeReplacesThePendingRetry()
    {
        CameraViewModel viewModel = await RunEndedByItselfAsync();

        ApplyPrinters(CameraPrinter(url: "rtsp://192.168.0.228/live"));
        _dispatcher.RunAll();
        await _factory.WaitForFeedAsync(2);
        _time.Advance(CameraFeedOptions.Default.ReconnectDelay * 2);
        _dispatcher.RunAll();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // The new address started at once; the retry of the old run did not start a second connection.
        Assert.Equal(2, _factory.Feeds.Count);
        Assert.True(viewModel.IsStreaming);
    }

    [Fact]
    public async Task RetryAlreadyPostedIsDroppedWhenTheWindowIsMinimizedBeforeItRuns()
    {
        CameraViewModel viewModel = await RunEndedByItselfAsync();

        // The timer fired and posted the retry, but the UI thread has not run it yet.
        _time.Advance(CameraFeedOptions.Default.ReconnectDelay);
        viewModel.SetActive(false);
        _dispatcher.RunAll();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Single(_factory.Feeds);
        Assert.False(viewModel.IsStreaming);
    }

    [Fact]
    public async Task RetryThatCannotBePostedIsLoggedAsShutdown()
    {
        ListLogger<CameraViewModel> logger = new();
        FrameFailingDispatcher dispatcher = new(_dispatcher) { FailRetries = true };
        CameraViewModel viewModel = Create(dispatcher: dispatcher, logger: logger);
        viewModel.SetActive(true);
        FakeCameraFeed first = await _factory.WaitForFeedAsync(1);
        first.Send();
        await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        _dispatcher.RunAll();

        _time.Advance(CameraFeedOptions.Default.ReconnectDelay);
        _dispatcher.RunAll();

        (LogLevel level, string message, Exception? exception) = Assert.Single(logger.Entries, static entry => entry.Message.Contains("restarted", StringComparison.Ordinal));
        Assert.Equal("The camera stream could not be restarted because the UI is shutting down.", message);
        Assert.Equal(LogLevel.Warning, level);
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Single(_factory.Feeds);
    }

    [Fact]
    public async Task RunFailingAgainTheSameWayIsLoggedInFullOnlyOnce()
    {
        ListLogger<CameraViewModel> logger = new();
        FrameFailingDispatcher dispatcher = new(_dispatcher);
        CameraViewModel viewModel = Create(dispatcher: dispatcher, logger: logger);
        viewModel.SetActive(true);

        // Two runs end by themselves with the same failure (showing the first frame throws), the second one after the retry.
        for (int run = 1; run <= 2; run++)
        {
            FakeCameraFeed feed = await _factory.WaitForFeedAsync(run);
            feed.Send();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
            _dispatcher.RunAll();
            _time.Advance(CameraFeedOptions.Default.ReconnectDelay);
            _dispatcher.RunAll();
        }

        // A frame is shown in the third run, then the same failure again: it counts as new.
        FakeCameraFeed third = await _factory.WaitForFeedAsync(3);
        dispatcher.FailFrames = false;
        third.Send(fill: 1);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return _sink.Frames.Count > 0; }, "frame drawn");
        dispatcher.FailFrames = true;
        third.Send(fill: 2);
        await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        List<(LogLevel Level, string Message, Exception? Exception)> failures = [.. logger.Entries.Where(static entry => entry.Message.Contains("failed", StringComparison.Ordinal))];
        Assert.Collection(
            failures,
            full => Assert.Equal((LogLevel.Error, "The camera stream of the camera window failed unexpectedly."), (full.Level, full.Message)),
            again =>
            {
                Assert.Equal((LogLevel.Warning, "The camera stream of the camera window failed again (InvalidOperationException)."), (again.Level, again.Message));
                Assert.Null(again.Exception);
            },
            fullAfterAFrame => Assert.IsType<InvalidOperationException>(fullAfterAFrame.Exception));
    }

    [Fact]
    public async Task RestartWaitsUntilThePreviousConnectionIsClosed()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed first = await _factory.WaitForFeedAsync(1);
        TaskCompletionSource closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        first.DisposeGate = closing.Task;

        viewModel.SetActive(false);
        viewModel.SetActive(true);
        await first.DisposeStarted.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Single(_factory.Feeds);

        closing.SetResult();
        await _factory.WaitForFeedAsync(2);
        Assert.True(first.IsDisposed);
    }

    [Fact]
    public async Task ChangedAddressClearsTheOldImage()
    {
        CameraViewModel viewModel = Create();
        viewModel.SetActive(true);
        FakeCameraFeed first = await _factory.WaitForFeedAsync(1);
        first.Send(fill: 1);
        await Waiting.UntilAsync(() => { _dispatcher.RunAll(); return viewModel.HasFrame && !viewModel.IsStatusVisible; }, "first camera streaming");

        ApplyPrinters(CameraPrinter(url: "rtsp://192.168.0.228/live"));
        // A frame of the old camera that is still on its way when the address changes.
        first.Send(fill: 9);
        await Waiting.UntilAsync(() => first.Consumed >= 2, "late frame published");
        _dispatcher.RunAll();

        Assert.Equal(1, _sink.ClearCount);
        Assert.False(viewModel.HasFrame);
        Assert.True(viewModel.IsStatusVisible);
        Assert.Equal("Connecting to the camera…", viewModel.StatusText);
        Assert.DoesNotContain(_sink.Frames, static frame => frame.First == 9);
    }

    /// <summary>Forwards to an inner dispatcher; the given post (1-based) throws instead.</summary>
    /// <summary>
    /// Like a dispatcher that is shutting down, but only for some callbacks of the view model, told apart by their method:
    /// showing a frame (<see cref="FailFrames"/>) and the automatic retry (<see cref="FailRetries"/>).
    /// </summary>
    private sealed class FrameFailingDispatcher(ManualDispatcher inner) : IUiDispatcher
    {
        public bool FailFrames { get; set; } = true;

        public bool FailRetries { get; set; }

        public void Post(Action action)
        {
            string method = action.Method.Name;
            if ((FailFrames && method == "PresentFrame") || (FailRetries && method == "Retry"))
            {
                throw new InvalidOperationException("The dispatcher is shutting down.");
            }

            inner.Post(action);
        }
    }

    private sealed class FaultyDispatcher(ManualDispatcher inner, int failingPost) : IUiDispatcher
    {
        private int _posts;

        public void Post(Action action)
        {
            if (Interlocked.Increment(ref _posts) == failingPost)
            {
                throw new InvalidOperationException("The dispatcher is shutting down.");
            }

            inner.Post(action);
        }
    }
}
