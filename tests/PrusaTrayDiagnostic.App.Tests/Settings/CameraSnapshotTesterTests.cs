using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Tests.Settings;

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

        Assert.Equal(new CameraTestResult(CameraTestOutcome.Success, 1280, 720), result);
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, Ct);
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
