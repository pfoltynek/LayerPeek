using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Platform;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Tests.Settings;

public sealed class CameraSnapshotTesterTests
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly FakeCameraFeedFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ListLogger<CameraSnapshotTester> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CameraSnapshotTester Create() => new(_factory, _time, _logger);

    [Fact]
    public void DefaultTimeoutIsTheDecidedFifteenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), Create().Timeout);
        Assert.Equal(TimeSpan.FromSeconds(15), CameraSnapshotTester.DefaultTimeout);
    }

    [Fact]
    public async Task FirstImageMeansSuccessWithItsSizeAndClosesTheStream()
    {
        Task<CameraTestResult> test = Create().TestAsync(StreamUri, Ct);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        feed.Send(width: 1280, height: 720);
        CameraTestResult result = await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(CameraTestOutcome.Success, result.Outcome);
        Assert.Equal((1280, 720), (result.Width, result.Height));
        Assert.Null(result.Reason);
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, Ct);
    }

    [Fact]
    public async Task SuccessCarriesAnOwnedCopyOfTheFirstImage()
    {
        Task<CameraTestResult> test = Create().TestAsync(StreamUri, Ct);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        // Red (B, G, R, A in memory); the feed reuses its buffer for the next frame, the image must not change.
        feed.SendColor(8, 6, 0xFFFF0000);
        CameraTestResult result = await test.WaitAsync(TestTimeouts.Wait, Ct);
        feed.SendColor(8, 6, 0xFF0000FF);

        CameraImage image = Assert.IsType<CameraImage>(result.Image);
        Assert.Equal((8, 6, 32), (image.Width, image.Height, image.Stride));
        Assert.Equal(8 * 6 * 4, image.Pixels.Length);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, image.Pixels[..4].ToArray());
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, image.Pixels[^4..].ToArray());
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, Ct);
    }

    [Fact]
    public async Task FailuresCarryNoImage()
    {
        Task<CameraTestResult> timedOut = Create().TestAsync(StreamUri, Ct);
        await _factory.WaitForFeedAsync(1);
        _time.Advance(TimeSpan.FromSeconds(15));
        Task<CameraTestResult> failed = Create().TestAsync(StreamUri, Ct);
        (await _factory.WaitForFeedAsync(2)).Fail(new CameraFeedException("Connection refused."));
        Task<CameraTestResult> ended = Create().TestAsync(StreamUri, Ct);
        (await _factory.WaitForFeedAsync(3)).End();

        foreach (Task<CameraTestResult> test in new[] { timedOut, failed, ended })
        {
            CameraTestResult result = await test.WaitAsync(TestTimeouts.Wait, Ct);
            Assert.NotEqual(CameraTestOutcome.Success, result.Outcome);
            Assert.Null(result.Image);
        }
    }

    [Fact]
    public async Task NoImageWithinTheTimeoutIsReportedAsTimeout()
    {
        Task<CameraTestResult> test = Create().TestAsync(StreamUri, Ct);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        _time.Advance(TimeSpan.FromSeconds(15));
        CameraTestResult result = await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(CameraTestOutcome.Timeout, result.Outcome);
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, Ct);
    }

    [Fact]
    public async Task DecoderFailureCarriesItsMessage()
    {
        _factory.CreateFailure = new CameraFeedException("No video decoder is available.");

        CameraTestResult result = await Create().TestAsync(StreamUri, Ct).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(new CameraTestResult(CameraTestOutcome.Failed, Reason: "No video decoder is available."), result);
    }

    [Fact]
    public async Task NoRouteToALocalCameraMayBeTheLocalNetworkPermission()
    {
        // T-097: simulated "no route to host" of FFmpeg; nothing connects.
        _factory.CreateFailure = new CameraFeedException("Opening the stream: No route to host.") { IsHostUnreachable = true };
        FakeLocalNetworkPrivacy privacy = new();

        CameraTestResult local = await new CameraSnapshotTester(_factory, _time, _logger, localNetwork: privacy).TestAsync(StreamUri, Ct).WaitAsync(TestTimeouts.Wait, Ct);
        CameraTestResult routed = await new CameraSnapshotTester(_factory, _time, _logger, localNetwork: privacy).TestAsync(new Uri("rtsp://camera.example.com/live"), Ct).WaitAsync(TestTimeouts.Wait, Ct);
        CameraTestResult elsewhere = await Create().TestAsync(StreamUri, Ct).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(new CameraTestResult(CameraTestOutcome.LocalNetworkAccessMayBeOff, Reason: "Opening the stream: No route to host."), local);
        Assert.Equal(CameraTestOutcome.Failed, routed.Outcome);
        Assert.Equal(CameraTestOutcome.Failed, elsewhere.Outcome);
        Assert.Equal([("192.168.0.227", CameraSnapshotTester.CameraSubject), ("camera.example.com", CameraSnapshotTester.CameraSubject)], privacy.Calls);
    }

    [Fact]
    public async Task OtherCameraFailuresAreNeverTheLocalNetworkPermission()
    {
        _factory.CreateFailure = new CameraFeedException("Opening the stream: Connection refused.");
        FakeLocalNetworkPrivacy privacy = new();

        CameraTestResult result = await new CameraSnapshotTester(_factory, _time, _logger, localNetwork: privacy).TestAsync(StreamUri, Ct).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(CameraTestOutcome.Failed, result.Outcome);
        Assert.Empty(privacy.Calls);
    }

    [Fact]
    public async Task OtherFailuresHideTheirMessage()
    {
        _factory.CreateFailure = new InvalidOperationException("rtsp://user:secret@camera failed");

        CameraTestResult result = await Create().TestAsync(StreamUri, Ct).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(new CameraTestResult(CameraTestOutcome.Failed), result);
    }

    [Fact]
    public async Task EndedStreamIsAFailure()
    {
        Task<CameraTestResult> test = Create().TestAsync(StreamUri, Ct);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        feed.End();
        CameraTestResult result = await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(CameraTestOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task CancellationIsThrownAndClosesTheStream()
    {
        using CancellationTokenSource cancel = new();
        Task<CameraTestResult> test = Create().TestAsync(StreamUri, cancel.Token);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.WaitAsync(TestTimeouts.Wait, Ct));
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, Ct);
    }

    [Fact]
    public async Task LogNeverContainsTheAddressCredentials()
    {
        _factory.CreateFailure = new CameraFeedException("Connection refused.");

        await Create().TestAsync(new Uri("rtsp://admin:secret@192.168.0.227/live"), Ct).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.All(_logger.Entries, entry => Assert.DoesNotContain("secret", entry.Message, StringComparison.Ordinal));
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("rtsp://***@192.168.0.227/live", StringComparison.Ordinal));
    }

    // ---- T-143 decision 3: the saved camera through the hub, an unsaved address on its own ----

    private static readonly Uri UnsavedUri = new("rtsp://192.168.0.228/live");

    private CameraStreamHub CreateHub()
    {
        CameraStreamHub hub = CameraHubs.Immediate(new ReconnectingCameraFeed(_factory, _time, Microsoft.Extensions.Logging.Abstractions.NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default));
        hub.Configure([TestPrinters.Definition("mk4", "MK4") with { Camera = new CameraSettings(enabled: true, StreamUri) }]);
        return hub;
    }

    [Fact]
    public async Task SavedCameraIsTestedOnTheConnectionOfAnOpenWindow()
    {
        await using CameraStreamHub hub = CreateHub();
        using CameraFrameSubscription window = hub.SubscribeFrames("mk4");
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        feed.SendColor(8, 6, 0xFFFF0000);
        await Waiting.UntilAsync(() => feed.Consumed >= 1, "window streaming");

        Task<CameraTestResult> test = new CameraSnapshotTester(_factory, _time, _logger, hub: hub).TestAsync(StreamUri, "MK4", Ct);
        await Task.Delay(50, Ct);
        feed.SendColor(8, 6, 0xFF0000FF);
        CameraTestResult result = await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(CameraTestOutcome.Success, result.Outcome);
        Assert.Equal((8, 6), (result.Width, result.Height));
        // The first image after the test started (blue), as an owned copy.
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, result.Image!.Pixels[..4].ToArray());
        Assert.Single(_factory.Feeds);
        Assert.False(feed.IsDisposed);
        Assert.False(window.IsEnded);
    }

    [Fact]
    public async Task SavedCameraWithoutAWindowIsTestedThroughTheHubAndReleasedAfterwards()
    {
        await using CameraStreamHub hub = CreateHub();

        Task<CameraTestResult> test = new CameraSnapshotTester(_factory, _time, _logger, hub: hub).TestAsync(StreamUri, "mk4", Ct);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        feed.Send(width: 1280, height: 720);
        CameraTestResult result = await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal((CameraTestOutcome.Success, 1280, 720), (result.Outcome, result.Width, result.Height));
        // The lease is released; this hub closes at once (the app's lingers for 10 s).
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.Equal(0, hub.OpenConnections);
    }

    [Fact]
    public async Task UnsavedAddressGetsAConnectionOfItsOwn()
    {
        await using CameraStreamHub hub = CreateHub();
        using CameraFrameSubscription window = hub.SubscribeFrames("mk4");
        FakeCameraFeed windowFeed = await _factory.WaitForFeedAsync(1);

        Task<CameraTestResult> test = new CameraSnapshotTester(_factory, _time, _logger, hub: hub).TestAsync(UnsavedUri, "mk4", Ct);
        FakeCameraFeed own = await _factory.WaitForFeedAsync(2);
        own.Send();
        CameraTestResult result = await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(CameraTestOutcome.Success, result.Outcome);
        Assert.Equal([StreamUri, UnsavedUri], _factory.Uris);
        await own.Disposed.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.False(windowFeed.IsDisposed);
        // Without a printer (or another printer's camera) as well.
        Task<CameraTestResult> other = new CameraSnapshotTester(_factory, _time, _logger, hub: hub).TestAsync(StreamUri, "xl", Ct);
        (await _factory.WaitForFeedAsync(3)).Send();
        Assert.Equal(CameraTestOutcome.Success, (await other.WaitAsync(TestTimeouts.Wait, Ct)).Outcome);
    }

    [Fact]
    public async Task ErrorOfTheSharedConnectionFailsTheTestWithItsReason()
    {
        await using CameraStreamHub hub = CreateHub();
        FakeLocalNetworkPrivacy privacy = new();

        Task<CameraTestResult> test = new CameraSnapshotTester(_factory, _time, _logger, localNetwork: privacy, hub: hub).TestAsync(StreamUri, "mk4", Ct);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        feed.Fail(new CameraFeedException("Opening the stream: No route to host.") { IsHostUnreachable = true });
        CameraTestResult result = await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(CameraTestOutcome.LocalNetworkAccessMayBeOff, result.Outcome);
        Assert.Equal("Opening the stream: No route to host.", result.Reason);
        Assert.Null(result.Image);
    }

    [Fact]
    public async Task StoppedHubFailsTheTestOfTheSavedCamera()
    {
        CameraStreamHub hub = CreateHub();
        await hub.DisposeAsync();

        CameraTestResult result = await new CameraSnapshotTester(_factory, _time, _logger, hub: hub).TestAsync(StreamUri, "mk4", Ct).WaitAsync(TestTimeouts.Wait, Ct);

        // A stopped hub forgets nothing about its configuration, but refuses leases.
        Assert.Equal(CameraTestOutcome.Failed, result.Outcome);
        Assert.Empty(_factory.Feeds);
    }

    [Fact]
    public async Task SharedTestTimesOutAndReleasesTheLease()
    {
        await using CameraStreamHub hub = CreateHub();

        Task<CameraTestResult> test = new CameraSnapshotTester(_factory, _time, _logger, hub: hub).TestAsync(StreamUri, "mk4", Ct);
        FakeCameraFeed feed = await _factory.WaitForFeedAsync(1);
        _time.Advance(CameraSnapshotTester.DefaultTimeout);
        CameraTestResult result = await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(CameraTestOutcome.Timeout, result.Outcome);
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, Ct);
    }
}
