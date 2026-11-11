using System.Collections.Concurrent;

using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>
/// T-185: the mini preview (a display subscriber with <see cref="CameraFrameSubscriptionOptions.MaxFrameRate"/>) takes only
/// its rate; the frames in between are decoded but not converted, a new limit (or none: the normal window, full screen)
/// applies to the next frame, and detection, the alarm snapshot, recording and timelapse beside it keep their own pace.
/// The camera sends 25 fps on a fake clock.
/// </summary>
public sealed class CameraStreamHubFrameRateTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraFeedOptions Options = new()
    {
        ConnectTimeout = CameraFeedOptions.MaxValue,
        FrameTimeout = CameraFeedOptions.MaxValue,
        ReconnectDelay = TimeSpan.FromSeconds(5),
    };

    private static readonly TimeSpan CameraPeriod = TimeSpan.FromMilliseconds(40);

    private const int SourceWidth = 64;
    private const int SourceHeight = 36;
    private static readonly CameraFrameSize Mini = new(32, 32);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeItemFeedFactory _factory = new();
    private readonly CameraStreamHub _hub;
    private readonly ConcurrentQueue<(int Width, byte Fill)> _shown = new();
    private int _pushed;

    public CameraStreamHubFrameRateTests()
    {
        _hub = new CameraStreamHub(new ReconnectingCameraFeed(_factory, _time, new ListLogger<ReconnectingCameraFeed>(), Options), _time, new ListLogger<CameraStreamHub>());
    }

    public async ValueTask DisposeAsync() =>
        await _hub.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

    /// <summary>The camera window in the mini preview: frames drawn on the streaming thread, like the app's window.</summary>
    private CameraFrameSubscription SubscribeMini(int? rate = 10) =>
        _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions
        {
            DisplaySize = Mini,
            MaxFrameRate = rate,
            FrameReceived = (in CameraFrame frame) => _shown.Enqueue((frame.Width, frame.Pixels.Span[0])),
        });

    /// <summary>One second of the camera: 25 frames, each handled by the hub before the clock moves on.</summary>
    private async Task SecondAsync(FakeItemFeed feed)
    {
        for (int i = 0; i < 25; i++)
        {
            feed.PushFrame((byte)(++_pushed % 100), SourceWidth, SourceHeight);
            int pushed = _pushed;
            await Eventually.TrueAsync(() => feed.Consumed >= pushed + 1, $"frame {pushed} handled");
            _time.Advance(CameraPeriod);
        }
    }

    [Fact]
    public async Task TheMiniPreviewGetsItsRateAndTheSkippedFramesAreDecodedButNotConverted()
    {
        using CameraFrameSubscription mini = SubscribeMini(rate: 10);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await feed.Started;

        Assert.True(demand.DisplayDue);
        feed.PushFrame(1, SourceWidth, SourceHeight);
        await Eventually.TrueAsync(() => feed.Consumed >= 2, "the first frame handled");
        // Right after a frame was taken, the next one is not due yet: the decoder converts nothing.
        Assert.Single(_shown);
        Assert.False(demand.DisplayDue);
        _pushed = 1;
        _time.Advance(CameraPeriod);

        await SecondAsync(feed);
        await SecondAsync(feed);

        // 10 fps from 25 fps; the first frame of the grid may make one more.
        Assert.InRange(_shown.Count, 20, 21);
        Assert.All(_shown, frame => Assert.Equal(32, frame.Width));
        Assert.Equal(51, feed.DecodedFrames);
        Assert.Equal((0, _shown.Count), (feed.FullConversions, feed.DisplayConversions));
    }

    [Fact]
    public async Task SwitchingToTheNormalWindowOrFullScreenGivesEveryFrameAtOnceAndBack()
    {
        using CameraFrameSubscription window = SubscribeMini(rate: 5);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        CameraStreamDemand demand = await feed.Started;
        await SecondAsync(feed);
        Assert.InRange(_shown.Count, 5, 6);

        // The normal window (or full screen): the next frame and every one after it.
        window.SetMaxFrameRate(null);
        Assert.True(demand.DisplayDue);
        _shown.Clear();
        await SecondAsync(feed);
        Assert.Equal(25, _shown.Count);

        // The mini preview again: the next frame at once, then its rate (so one more in this second).
        window.SetMaxFrameRate(5);
        _shown.Clear();
        await SecondAsync(feed);
        Assert.InRange(_shown.Count, 5, 6);
        Assert.Equal(1, _factory.CreateCount);
    }

    [Fact]
    public async Task ANewRateFromTheSettingsAppliesToTheOpenMiniPreview()
    {
        using CameraFrameSubscription mini = SubscribeMini(rate: 10);
        FakeItemFeed feed = await _factory.FeedAsync(1);
        await SecondAsync(feed);

        mini.SetMaxFrameRate(5);
        _shown.Clear();
        await SecondAsync(feed);
        await SecondAsync(feed);

        Assert.InRange(_shown.Count, 10, 11);
        Assert.Equal(5, mini.MaxFrameRate);
    }

    [Fact]
    public async Task AFullResolutionSubscriberBesideTheMiniPreviewGetsEveryFrameAndTheMiniPreviewNeverAFullOne()
    {
        using CameraFrameSubscription mini = SubscribeMini(rate: 5);
        // E.g. the camera test of the settings: every frame in the camera's resolution.
        int full = 0;
        using CameraFrameSubscription fullResolution = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions
        {
            FrameReceived = (in CameraFrame frame) =>
            {
                Assert.Equal(SourceWidth, frame.Width);
                Interlocked.Increment(ref full);
            },
        });
        FakeItemFeed feed = await _factory.FeedAsync(1);

        await SecondAsync(feed);

        Assert.Equal(25, Volatile.Read(ref full));
        Assert.InRange(_shown.Count, 5, 6);
        // Every frame of the mini preview is scaled: a frame converted in full anyway is converted for display too.
        Assert.All(_shown, frame => Assert.Equal(32, frame.Width));
    }

    [Fact]
    public async Task DetectionAndTheAlarmSnapshotBesideTheMiniPreviewKeepTheirOwnPace()
    {
        TimeSpan interval = TimeSpan.FromMilliseconds(500);
        using CameraFrameSubscription mini = SubscribeMini(rate: 5);
        using CameraFrameSubscription samples = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions
        {
            KeyFramesOnly = true,
            MinFrameInterval = interval,
            Grayscale = new CameraGrayscaleSize(16, 16),
        });
        int snapshots = 0;
        using CameraFrameSubscription snapshot = _hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions
        {
            KeyFramesOnly = true,
            MinFrameInterval = interval,
            FrameReceived = (in CameraFrame frame) =>
            {
                Assert.Equal(SourceWidth, frame.Width);
                Interlocked.Increment(ref snapshots);
            },
        });
        FakeItemFeed feed = await _factory.FeedAsync(1);

        int grayscale = 0;
        for (int second = 0; second < 2; second++)
        {
            for (int i = 0; i < 25; i++)
            {
                feed.PushFrame((byte)(++_pushed % 100), SourceWidth, SourceHeight);
                int pushed = _pushed;
                await Eventually.TrueAsync(() => feed.Consumed >= pushed + 1, $"frame {pushed} handled");
                grayscale += samples.TakeLatest() is { Format: CameraPixelFormat.Gray8 } ? 1 : 0;
                _time.Advance(CameraPeriod);
            }
        }

        // One sample and one snapshot per 0.5 s (the first frame of each 0.5 s that is not closer than the interval), as
        // without the mini preview; the mini preview its 5 fps.
        Assert.Equal(feed.GrayscaleConversions, grayscale);
        Assert.InRange(grayscale, 3, 4);
        Assert.InRange(Volatile.Read(ref snapshots), 3, 4);
        Assert.InRange(_shown.Count, 10, 11);
    }

    [Fact]
    public async Task RecordingAndTimelapseGetEveryPacketBesideTheMiniPreview()
    {
        using CameraFrameSubscription mini = SubscribeMini(rate: 1);
        await using CameraPacketSubscription recording = _hub.SubscribePackets("mk4", StreamUri);
        await using CameraPacketSubscription timelapse = _hub.SubscribePackets("mk4", StreamUri, new CameraPacketSubscriptionOptions { KeyFramesOnly = true });
        FakeItemFeed feed = await _factory.FeedAsync(1);

        for (int i = 0; i < 10; i++)
        {
            feed.PushPacket(fill: (byte)i, keyFrame: i % 5 == 0, length: 16 + i);
            feed.PushFrame((byte)i, SourceWidth, SourceHeight, keyFrame: i % 5 == 0);
            _time.Advance(CameraPeriod);
        }

        Assert.Equal(10, (await PacketsAsync(recording, 10)).Count);
        Assert.Equal([0, 5], (await PacketsAsync(timelapse, 2)).ConvertAll(static packet => packet.Fill));
        await Eventually.TrueAsync(() => feed.Consumed >= 21, "every item handled");
        Assert.Single(_shown);
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
