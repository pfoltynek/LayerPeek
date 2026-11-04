using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>
/// T-153: subscriptions of key frames only, downscaled to grayscale (print failure detection): without another frame
/// subscriber only the key frames the subscribers are due for are decoded; the camera window still gets every frame.
/// </summary>
public sealed class CameraStreamHubKeyFrameTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraFeedOptions Options = new()
    {
        ConnectTimeout = CameraFeedOptions.MaxValue,
        FrameTimeout = CameraFeedOptions.MaxValue,
        ReconnectDelay = TimeSpan.FromSeconds(5),
    };

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeItemFeedFactory _factory = new();
    private readonly CameraStreamHub _hub;

    public CameraStreamHubKeyFrameTests()
    {
        _hub = new CameraStreamHub(new ReconnectingCameraFeed(_factory, _time, new ListLogger<ReconnectingCameraFeed>(), Options), _time, new ListLogger<CameraStreamHub>());
    }

    public async ValueTask DisposeAsync() =>
        await _hub.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

    private static CameraFrameSubscriptionOptions Samples(TimeSpan? interval = null) => new()
    {
        KeyFramesOnly = true,
        MinFrameInterval = interval ?? Interval,
        Grayscale = new CameraGrayscaleSize(16, 16),
    };

    private static async Task<CameraFrameBuffer> NextFrameAsync(CameraFrameSubscription subscription)
    {
        CameraFrameBuffer? frame = null;
        await Eventually.TrueAsync(() => (frame = subscription.TakeLatest()) is not null, "a frame");
        return frame!;
    }

    /// <summary>Waits until the feed handled <paramref name="items"/> items after its description (decoded, yielded or skipped).</summary>
    private static Task HandledAsync(FakeItemFeed feed, int items) => Eventually.TrueAsync(() => feed.Consumed >= items + 1, $"{items} items handled");

    [Fact]
    public async Task AloneItDecodesOnlyTheKeyFramesItIsDueFor()
    {
        using CameraFrameSubscription samples = _hub.SubscribeFrames("mk4", StreamUri, Samples());
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await feed.Started;

        // Never every frame; the connection is kept reading (packets) between the samples.
        Assert.False(demand.Frames);
        Assert.True(demand.Packets);
        feed.PushFrame(fill: 10);
        feed.PushFrame(fill: 20, keyFrame: true);
        CameraFrameBuffer first = await NextFrameAsync(samples);
        Assert.Equal((CameraPixelFormat.Gray8, 16, 16, 16), (first.Format, first.Width, first.Height, first.Stride));
        // A gray BGRA pixel keeps its value as luma.
        Assert.Equal(20, first.Pixels[0]);

        // Not due yet: neither the next key frame nor the frames in between are decoded.
        feed.PushFrame(fill: 30, keyFrame: true);
        feed.PushFrame(fill: 40);
        await HandledAsync(feed, 4);
        Assert.Equal(1, feed.DecodedFrames);
        Assert.Null(samples.TakeLatest());

        _time.Advance(Interval);
        feed.PushFrame(fill: 50);
        feed.PushFrame(fill: 60, keyFrame: true);
        Assert.Equal(60, (await NextFrameAsync(samples)).Pixels[0]);
        Assert.Equal(2, feed.DecodedFrames);
        Assert.Equal(1, _hub.OpenConnections);
    }

    [Fact]
    public async Task TheWindowGetsEveryFrameAndTheSamplesOnePerInterval()
    {
        using CameraFrameSubscription samples = _hub.SubscribeFrames("mk4", StreamUri, Samples());
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await feed.Started;
        Assert.True(demand.Frames);

        feed.PushFrame(fill: 1);
        Assert.Equal(1, (await NextFrameAsync(window)).Pixels[0]);
        Assert.Equal(1, (await NextFrameAsync(samples)).Pixels[0]);
        feed.PushFrame(fill: 2);
        Assert.Equal(2, (await NextFrameAsync(window)).Pixels[0]);
        Assert.Null(samples.TakeLatest());

        // While everything is decoded anyway, the sample need not be a key frame.
        _time.Advance(Interval);
        feed.PushFrame(fill: 3);
        CameraFrameBuffer full = await NextFrameAsync(window);
        Assert.Equal((CameraPixelFormat.Bgra32, 4, 2), (full.Format, full.Width, full.Height));
        Assert.Equal(3, (await NextFrameAsync(samples)).Pixels[0]);
        Assert.Equal(3, feed.DecodedFrames);
    }

    [Fact]
    public async Task AfterTheWindowLeftDecodingFallsBackToKeyFrames()
    {
        using CameraFrameSubscription samples = _hub.SubscribeFrames("mk4", StreamUri, Samples());
        CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await feed.Started;
        feed.PushFrame(fill: 1);
        await NextFrameAsync(window);
        await NextFrameAsync(samples);

        window.Dispose();
        // Every frame is still decoded for the linger time (the window may come back), then only key frames.
        Assert.True(demand.Frames);
        _time.Advance(_hub.Linger);
        Assert.False(demand.Frames);

        feed.PushFrame(fill: 2);
        feed.PushFrame(fill: 3, keyFrame: true);
        Assert.Equal(3, (await NextFrameAsync(samples)).Pixels[0]);
        Assert.Equal(2, feed.DecodedFrames);
    }

    [Fact]
    public async Task ANewSubscriberGetsTheNextKeyFrameWithoutWaitingForTheOthers()
    {
        using CameraFrameSubscription samples = _hub.SubscribeFrames("mk4", StreamUri, Samples(TimeSpan.FromHours(1)));
        FakeItemFeed feed = await _factory.FeedAsync(1);
        feed.PushFrame(fill: 1, keyFrame: true);
        await NextFrameAsync(samples);

        // The alarm snapshot of T-153: full size, the next key frame.
        using CameraFrameSubscription snapshot = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions { KeyFramesOnly = true });
        feed.PushFrame(fill: 2);
        feed.PushFrame(fill: 3, keyFrame: true);
        CameraFrameBuffer frame = await NextFrameAsync(snapshot);

        Assert.Equal((CameraPixelFormat.Bgra32, 3), (frame.Format, frame.Pixels[0]));
        Assert.Null(samples.TakeLatest());
        Assert.Equal(2, feed.DecodedFrames);
    }

    [Fact]
    public async Task WithoutDueSubscribersNoKeyFrameIsWanted()
    {
        using CameraFrameSubscription samples = _hub.SubscribeFrames("mk4", StreamUri, Samples(TimeSpan.FromHours(1)));
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await feed.Started;
        Assert.True(demand.KeyFrameDue);
        feed.PushFrame(fill: 1, keyFrame: true);
        await NextFrameAsync(samples);

        Assert.False(demand.KeyFrameDue);
        samples.Dispose();
        Assert.False(demand.KeyFrameDue);
    }

    [Fact]
    public void GrayscaleSizeIsValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraGrayscaleSize(CameraGrayscaleSize.MinSide - 1, 180));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraGrayscaleSize(320, CameraGrayscaleSize.MaxSide + 1));
        Assert.Throws<ArgumentException>(() => new CameraFrameSubscriptionOptions { Grayscale = default(CameraGrayscaleSize) });
    }
}
