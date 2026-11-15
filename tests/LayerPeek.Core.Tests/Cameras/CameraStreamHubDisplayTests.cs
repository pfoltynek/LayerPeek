using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>
/// T-184: display subscribers (the camera window) get frames scaled down to the size they draw; every other consumer
/// (the alarm snapshot, a one-off full frame, recording and timelapse) keeps the camera's resolution, and a frame is
/// converted in full resolution only when such a consumer takes it; print failure detection gets grayscale scaled from
/// the camera's resolution by the feed (T-189). The fake feed scales like
/// the decoder and fills a scaled frame with <see cref="FakeItemFeed.DisplayFill"/>.
/// </summary>
public sealed class CameraStreamHubDisplayTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraFeedOptions Options = new()
    {
        ConnectTimeout = CameraFeedOptions.MaxValue,
        FrameTimeout = CameraFeedOptions.MaxValue,
        ReconnectDelay = TimeSpan.FromSeconds(5),
    };

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    // The "camera": 64 × 36 (16:9), the fake's largest frame; the window draws it at 32 × 32 device pixels.
    private const int SourceWidth = 64;
    private const int SourceHeight = 36;
    private static readonly CameraFrameSize Window = new(32, 32);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeItemFeedFactory _factory = new();
    private readonly CameraStreamHub _hub;

    public CameraStreamHubDisplayTests()
    {
        _hub = new CameraStreamHub(new ReconnectingCameraFeed(_factory, _time, new ListLogger<ReconnectingCameraFeed>(), Options), _time, new ListLogger<CameraStreamHub>());
    }

    public async ValueTask DisposeAsync() =>
        await _hub.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

    private static CameraFrameSubscriptionOptions Display(CameraFrameSize? size = null) => new() { DisplaySize = size ?? Window };

    private static async Task<CameraFrameBuffer> NextFrameAsync(CameraFrameSubscription subscription)
    {
        CameraFrameBuffer? frame = null;
        await Eventually.TrueAsync(() => (frame = subscription.TakeLatest()) is not null, "a frame");
        return frame!;
    }

    private static void Push(FakeItemFeed feed, byte fill, bool keyFrame = false) => feed.PushFrame(fill, SourceWidth, SourceHeight, keyFrame);

    /// <summary>Waits until the feed handled <paramref name="items"/> items after its description.</summary>
    private static Task HandledAsync(FakeItemFeed feed, int items) => Eventually.TrueAsync(() => feed.Consumed >= items + 1, $"{items} items handled");

    [Fact]
    public async Task AWindowAloneGetsOnlyScaledFramesAndNothingIsConvertedInFullResolution()
    {
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, Display());
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await feed.Started;

        Assert.True(demand.Frames);
        Assert.Equal(Window, demand.DisplaySize);
        Assert.False(demand.FullResolutionDue);
        Push(feed, fill: 1);
        CameraFrameBuffer frame = await NextFrameAsync(window);

        Assert.Equal((32, 18, SourceWidth, SourceHeight), (frame.Width, frame.Height, frame.SourceWidth, frame.SourceHeight));
        Assert.Equal(FakeItemFeed.DisplayFill(1), frame.Pixels[0]);
        Assert.Equal((0, 1), (feed.FullConversions, feed.DisplayConversions));
    }

    [Fact]
    public async Task AFullResolutionSubscriberBesideTheWindowGetsTheCamerasResolution()
    {
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, Display());
        // E.g. the camera test of the settings, or a full-resolution snapshot taken from the window (a short lease of its own).
        using CameraFrameSubscription full = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);

        Push(feed, fill: 1);
        CameraFrameBuffer shown = await NextFrameAsync(window);
        CameraFrameBuffer copy = await NextFrameAsync(full);

        Assert.Equal((32, 18, FakeItemFeed.DisplayFill(1)), (shown.Width, shown.Height, shown.Pixels[0]));
        Assert.Equal((SourceWidth, SourceHeight, (byte)1), (copy.Width, copy.Height, copy.Pixels[0]));
        Assert.Equal((1, 1), (feed.FullConversions, feed.DisplayConversions));
    }

    [Fact]
    public async Task AShortFullResolutionLeaseCostsFullConversionsOnlyWhileItLasts()
    {
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, Display());
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await feed.Started;
        Push(feed, fill: 1);
        await NextFrameAsync(window);

        CameraFrameBuffer snapshot;
        using (CameraFrameSubscription full = _hub.SubscribeFrames("mk4", StreamUri))
        {
            Assert.True(demand.FullResolutionDue);
            Push(feed, fill: 2);
            snapshot = await NextFrameAsync(full);
            Assert.Equal((SourceWidth, SourceHeight, (byte)2), (snapshot.Width, snapshot.Height, snapshot.Pixels[0]));
        }

        Assert.False(demand.FullResolutionDue);
        Push(feed, fill: 3);
        await HandledAsync(feed, 3);
        Assert.Equal(FakeItemFeed.DisplayFill(3), (await NextFrameAsync(window)).Pixels[0]);
        Assert.Equal((1, 3), (feed.FullConversions, feed.DisplayConversions));
        // No reconnect for any of it.
        Assert.Equal(1, _factory.CreateCount);
    }

    [Fact]
    public async Task DetectionBesideTheWindowGetsGrayscaleFramesAndNothingIsConvertedInFull()
    {
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, Display());
        // Print failure detection (T-153): grayscale samples, one per interval, scaled by the feed (T-189).
        using CameraFrameSubscription samples = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions
        {
            KeyFramesOnly = true,
            MinFrameInterval = Interval,
            Grayscale = new CameraGrayscaleSize(16, 16),
        });
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await feed.Started;
        Assert.False(demand.FullResolutionDue);
        Assert.Equal(new CameraGrayscaleSize(16, 16), demand.GrayscaleSize);

        Push(feed, fill: 10);
        // Handled: the hub scheduled the next sample before it read on.
        await HandledAsync(feed, 1);
        // A gray BGRA pixel keeps its value as luma: the sample was made from the camera's frame, not the scaled one.
        CameraFrameBuffer sample = await NextFrameAsync(samples);
        Assert.Equal((CameraPixelFormat.Gray8, 16, 16, (byte)10), (sample.Format, sample.Width, sample.Height, sample.Pixels[0]));
        Assert.Equal((SourceWidth, SourceHeight), (sample.SourceWidth, sample.SourceHeight));
        Assert.Equal(FakeItemFeed.DisplayFill(10), (await NextFrameAsync(window)).Pixels[0]);
        Assert.False(demand.GrayscaleDue);

        // Until the next sample is due, the window's frames are the only conversions.
        Push(feed, fill: 20);
        Push(feed, fill: 30);
        await HandledAsync(feed, 3);
        Assert.Equal(FakeItemFeed.DisplayFill(30), (await NextFrameAsync(window)).Pixels[0]);
        Assert.Null(samples.TakeLatest());
        Assert.Equal((0, 1, 3), (feed.FullConversions, feed.GrayscaleConversions, feed.DisplayConversions));

        _time.Advance(Interval);
        Assert.True(demand.GrayscaleDue);
        Push(feed, fill: 40);
        await HandledAsync(feed, 4);
        Assert.Equal(40, (await NextFrameAsync(samples)).Pixels[0]);
        Assert.Equal(FakeItemFeed.DisplayFill(40), (await NextFrameAsync(window)).Pixels[0]);
        // Never a BGRA frame in full resolution for detection.
        Assert.Equal((0, 2, 4), (feed.FullConversions, feed.GrayscaleConversions, feed.DisplayConversions));
        Assert.False(demand.GrayscaleDue);
    }

    [Fact]
    public async Task TheAlarmSnapshotBesideTheWindowIsFullResolution()
    {
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, Display());
        FakeItemFeed feed = await _factory.FeedAsync(1);
        Push(feed, fill: 1);
        await NextFrameAsync(window);

        // T-153: the snapshot of an alarm, BGRA, the next frame.
        using CameraFrameSubscription snapshot = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions { KeyFramesOnly = true });
        Push(feed, fill: 2);
        CameraFrameBuffer frame = await NextFrameAsync(snapshot);

        Assert.Equal((CameraPixelFormat.Bgra32, SourceWidth, SourceHeight, (byte)2), (frame.Format, frame.Width, frame.Height, frame.Pixels[0]));
    }

    [Fact]
    public async Task RecordingAndTimelapseGetEveryPacketUnchangedBesideTheWindow()
    {
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, Display());
        await using CameraPacketSubscription recording = _hub.SubscribePackets("mk4", StreamUri);
        await using CameraPacketSubscription timelapse = _hub.SubscribePackets("mk4", StreamUri, new CameraPacketSubscriptionOptions { KeyFramesOnly = true });
        FakeItemFeed feed = await _factory.FeedAsync(1);

        feed.PushPacket(fill: 1, keyFrame: true, length: 48);
        Push(feed, fill: 1, keyFrame: true);
        feed.PushPacket(fill: 2, length: 32);
        Push(feed, fill: 2);
        await NextFrameAsync(window);

        Assert.Equal([(1, 48), (2, 32)], await PacketsAsync(recording, 2));
        Assert.Equal([(1, 48)], await PacketsAsync(timelapse, 1));
    }

    [Fact]
    public async Task ANewDisplaySizeAppliesToTheNextFrameWithoutReconnecting()
    {
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, Display());
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await feed.Started;
        Push(feed, fill: 1);
        Assert.Equal(32, (await NextFrameAsync(window)).Width);

        // The mini preview.
        window.SetDisplaySize(new CameraFrameSize(16, 16));
        Assert.Equal(new CameraFrameSize(16, 16), window.DisplaySize);
        Assert.Equal(new CameraFrameSize(16, 16), demand.DisplaySize);
        Push(feed, fill: 2);
        CameraFrameBuffer mini = await NextFrameAsync(window);
        Assert.Equal((16, 9, (byte)FakeItemFeed.DisplayFill(2)), (mini.Width, mini.Height, mini.Pixels[0]));

        // Full screen on a large monitor: larger than the camera, so its own resolution (never scaled up).
        window.SetDisplaySize(new CameraFrameSize(3840, 2160));
        Push(feed, fill: 3);
        CameraFrameBuffer fullScreen = await NextFrameAsync(window);
        Assert.Equal((SourceWidth, SourceHeight, (byte)3), (fullScreen.Width, fullScreen.Height, fullScreen.Pixels[0]));

        // Without a size: full resolution.
        window.SetDisplaySize(null);
        Assert.True(demand.FullResolutionDue);
        Assert.Null(demand.DisplaySize);
        Push(feed, fill: 4);
        Assert.Equal(SourceWidth, (await NextFrameAsync(window)).Width);

        Assert.Equal(1, _factory.CreateCount);
        Assert.Equal(4, feed.DecodedFrames);
    }

    [Fact]
    public async Task SeveralDisplaySubscribersShareTheSizeThatFitsAll()
    {
        using CameraFrameSubscription wide = _hub.SubscribeFrames("mk4", StreamUri, Display(new CameraFrameSize(48, 8)));
        using CameraFrameSubscription tall = _hub.SubscribeFrames("mk4", StreamUri, Display(new CameraFrameSize(16, 18)));
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await feed.Started;

        Assert.Equal(new CameraFrameSize(48, 18), demand.DisplaySize);
        Push(feed, fill: 1);
        Assert.Equal((32, 18), Size(await NextFrameAsync(wide)));
        Assert.Equal((32, 18), Size(await NextFrameAsync(tall)));
        Assert.Equal(1, feed.DisplayConversions);

        wide.Dispose();
        Assert.Equal(new CameraFrameSize(16, 18), demand.DisplaySize);

        static (int, int) Size(CameraFrameBuffer frame) => (frame.Width, frame.Height);
    }

    [Fact]
    public async Task AfterTheWindowLeftTheLingeringConnectionDecodesWithoutConverting()
    {
        CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, Display());
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await feed.Started;
        Push(feed, fill: 1);
        await NextFrameAsync(window);

        window.Dispose();
        // Decoding goes on for the linger time (the window may come back without waiting for a key frame), but nobody
        // wants the pixels.
        Assert.True(demand.Frames);
        Assert.Null(demand.DisplaySize);
        Assert.False(demand.FullResolutionDue);
        Push(feed, fill: 2);
        await HandledAsync(feed, 2);
        Assert.Equal((2, 0, 1), (feed.DecodedFrames, feed.FullConversions, feed.DisplayConversions));

        using CameraFrameSubscription again = _hub.SubscribeFrames("mk4", StreamUri, Display());
        Push(feed, fill: 3);
        Assert.Equal(FakeItemFeed.DisplayFill(3), (await NextFrameAsync(again)).Pixels[0]);
    }

    [Fact]
    public async Task AFrameReceiverGetsTheBorrowedFrameOnTheStreamingThreadInsteadOfACopy()
    {
        List<(int Width, int Height, int SourceWidth, byte First)> received = [];
        using CameraFrameSubscription window = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions
        {
            DisplaySize = Window,
            FrameReceived = (in CameraFrame frame) =>
            {
                lock (received)
                {
                    received.Add((frame.Width, frame.Height, frame.SourceWidth, frame.Pixels.Span[0]));
                }
            },
        });
        FakeItemFeed feed = await _factory.FeedAsync(1);

        Push(feed, fill: 1);
        await HandledAsync(feed, 1);
        await Eventually.TrueAsync(() => window.Status.State == CameraConnectionState.Streaming, "streaming");

        lock (received)
        {
            Assert.Equal([(32, 18, SourceWidth, FakeItemFeed.DisplayFill(1))], received);
        }

        Assert.Null(window.TakeLatest());
        Assert.Equal(0, window.DroppedFrames);
    }

    [Fact]
    public async Task AFailingFrameReceiverEndsOnlyItsSubscription()
    {
        FrameProbe probe = new();
        using CameraFrameSubscription failing = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions
        {
            FrameReceived = (in CameraFrame _) => throw new InvalidOperationException("Drawing failed."),
            Ended = probe.Ended.Enqueue,
        });
        using CameraFrameSubscription other = _hub.SubscribeFrames("mk4", StreamUri);
        FakeItemFeed feed = await _factory.FeedAsync(1);

        Push(feed, fill: 1);
        Assert.Equal(SourceWidth, (await NextFrameAsync(other)).Width);
        await Eventually.TrueAsync(() => failing.IsEnded, "the failing subscription ended");
        Assert.Equal(CameraStreamEndReason.Failed, failing.EndReason);
    }

    [Fact]
    public void AFrameReceiverCannotConvertToGrayscale()
    {
        Assert.Throws<ArgumentException>(() => _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions
        {
            FrameReceived = (in CameraFrame _) => { },
            Grayscale = new CameraGrayscaleSize(16, 16),
        }));
    }

    [Fact]
    public void ADisplaySizeOfAnEndedSubscriptionIsKeptButChangesNothing()
    {
        // No camera configured: the lease has ended at once.
        CameraFrameSubscription window = _hub.SubscribeFrames("unknown", options: Display());

        window.SetDisplaySize(new CameraFrameSize(10, 10));

        Assert.True(window.IsEnded);
        Assert.Equal(new CameraFrameSize(10, 10), window.DisplaySize);
    }

    private static async Task<List<(int Fill, int Length)>> PacketsAsync(CameraPacketSubscription subscription, int count)
    {
        List<(int, int)> packets = [];
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TestTimeouts.Wait);
        await foreach (CameraStreamItem item in subscription.ReadAsync(timeout.Token))
        {
            if (item.Kind == CameraStreamItemKind.Packet)
            {
                packets.Add((item.Packet.Data.Span[0], item.Packet.Data.Length));
                if (packets.Count == count)
                {
                    break;
                }
            }
        }

        return packets;
    }
}
