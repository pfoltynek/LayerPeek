using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Tests.Cameras;

public class CameraSnapshotTests
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReturnsCopyOfFirstFrameAndClosesStream()
    {
        FakeCameraFeedFactory factory = new();
        FakeCameraFeed stream = new();
        factory.Behavior = _ => stream;
        stream.PushFrame(fill: 7, width: 3, height: 2);
        stream.PushFrame(fill: 8);

        CameraImage image = await CameraSnapshot.CaptureAsync(factory, StreamUri, Ct);

        Assert.Equal(StreamUri, factory.LastUri);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.All(image.Pixels.ToArray(), static b => Assert.Equal(7, b));
        Assert.Equal(1, stream.FramesRead);
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task ImageStaysValidAfterTheStreamReusesItsBuffer()
    {
        FakeCameraFeedFactory factory = new();
        FakeCameraFeed stream = new();
        factory.Behavior = _ => stream;
        stream.PushFrame(fill: 7);

        CameraImage image = await CameraSnapshot.CaptureAsync(factory, StreamUri, Ct);
        // A decoder reuses its buffer for the next frame; that must not change the snapshot.
        stream.OverwriteBuffer(9);

        Assert.All(image.Pixels.ToArray(), static b => Assert.Equal(7, b));
    }

    [Fact]
    public async Task StreamEndingBeforeFirstFrameThrowsCameraFeedException()
    {
        FakeCameraFeedFactory factory = new();
        FakeCameraFeed stream = new();
        factory.Behavior = _ => stream;
        stream.End();

        await Assert.ThrowsAsync<CameraFeedException>(() => CameraSnapshot.CaptureAsync(factory, StreamUri, Ct));

        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task StreamFailurePropagatesAndClosesStream()
    {
        FakeCameraFeedFactory factory = new();
        FakeCameraFeed stream = new();
        factory.Behavior = _ => stream;
        stream.Fail(new CameraFeedException("Connection refused."));

        CameraFeedException error = await Assert.ThrowsAsync<CameraFeedException>(() => CameraSnapshot.CaptureAsync(factory, StreamUri, Ct));

        Assert.Equal("Connection refused.", error.Message);
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task CancellationStopsWaitingAndClosesStream()
    {
        FakeCameraFeedFactory factory = new();
        FakeCameraFeed stream = new();
        factory.Behavior = _ => stream;
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        Task<CameraImage> capture = CameraSnapshot.CaptureAsync(factory, StreamUri, cancellation.Token);
        await stream.NextReadAsync();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task InvalidArgumentsAreRejected()
    {
        FakeCameraFeedFactory factory = new();

        await Assert.ThrowsAsync<ArgumentNullException>(() => CameraSnapshot.CaptureAsync(null!, StreamUri, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => CameraSnapshot.CaptureAsync(factory, null!, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => CameraSnapshot.CaptureAsync(factory, new Uri("live", UriKind.Relative), Ct));
        Assert.Equal(0, factory.CreateCount);
    }
}
