using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Cameras;
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
}
