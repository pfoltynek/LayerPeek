using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary><see cref="ReconnectingCameraFeed.ReadAsync"/> and the default <see cref="ICameraFeed.ReadAsync"/> (T-143).</summary>
public sealed class ReconnectingCameraFeedItemsTests
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DemandReachesTheFeedAndTheFirstPacketCountsAsStreaming()
    {
        FakeItemFeedFactory factory = new();
        RecordingProgress status = new();
        ReconnectingCameraFeed camera = new(factory, new FakeTimeProvider(), new ListLogger<ReconnectingCameraFeed>());
        CameraStreamDemand demand = new(frames: false, packets: true);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        // A bug fails the test instead of hanging the run.
        stop.CancelAfter(TestTimeouts.Wait * 3);
        await using IAsyncEnumerator<CameraStreamItem> items = camera.ReadAsync(StreamUri, demand, status, stop.Token).GetAsyncEnumerator(stop.Token);

        Assert.True(await items.MoveNextAsync());
        Assert.Equal(CameraStreamItemKind.StreamStarted, items.Current.Kind);
        FakeItemFeed feed = await factory.FeedAsync(1);
        Assert.Same(demand, await feed.Started.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
        // The description alone is no image yet.
        Assert.Equal([CameraStatus.Connecting], status.All);

        ValueTask<bool> next = items.MoveNextAsync();
        feed.PushPacket(fill: 3, keyFrame: true);
        Assert.True(await next);
        Assert.Equal((CameraStreamItemKind.Packet, true), (items.Current.Kind, items.Current.Packet.IsKeyFrame));
        Assert.Equal([CameraStatus.Connecting, CameraStatus.Streaming], status.All);
        await stop.CancelAsync();
    }

    [Fact]
    public async Task ReadAsyncRejectsBadArguments()
    {
        ReconnectingCameraFeed camera = new(new FakeItemFeedFactory(), new FakeTimeProvider(), new ListLogger<ReconnectingCameraFeed>());

        Assert.Throws<ArgumentNullException>(() => camera.ReadAsync(StreamUri, null!, null, Ct));
        Assert.Throws<ArgumentException>(() => camera.ReadAsync(new Uri("/live", UriKind.Relative), new CameraStreamDemand(true, false), null, Ct));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DefaultReadAsyncYieldsTheFramesOfAFeedWithoutPackets()
    {
        FakeCameraFeed feed = new();
        feed.PushFrame(fill: 5);
        feed.End();
        List<(CameraStreamItemKind Kind, byte First)> items = [];

        await foreach (CameraStreamItem item in ((ICameraFeed)feed).ReadAsync(new CameraStreamDemand(frames: false, packets: true), Ct))
        {
            items.Add((item.Kind, item.Frame.Pixels.Span[0]));
        }

        Assert.Equal([(CameraStreamItemKind.Frame, (byte)5)], items);
    }

    [Fact]
    public void ItemsAndDescriptionsCheckTheirArguments()
    {
        Assert.Throws<ArgumentNullException>(() => CameraStreamItem.Started(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraStreamInfo(CameraVideoCodec.H264, -1, 0, CameraTimeBase.Rtp90kHz, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraStreamInfo(CameraVideoCodec.H264, 0, 0, new CameraTimeBase(0, 1), default));
        CameraStreamDemand demand = new(frames: true, packets: false) { Packets = true, Frames = false };
        Assert.Equal((false, true), (demand.Frames, demand.Packets));
    }
}
