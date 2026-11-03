using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Simulator.Camera;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-143: packets before decoding, decoding only on demand (fixture file), and the shared connection of
/// <see cref="CameraStreamHub"/> against the simulated camera (loopback).
/// </summary>
public sealed class FFmpegCameraFeedPacketTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [FFmpegFact]
    public async Task PacketsOnlyNeverOpensTheDecoder()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        List<CameraStreamItemKind> kinds = [];
        CameraStreamInfo? info = null;
        bool firstPacketIsKey = false;
        long previousDts = long.MinValue;

        await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: false, packets: true), Ct))
        {
            kinds.Add(item.Kind);
            if (item.Kind == CameraStreamItemKind.StreamStarted)
            {
                info = item.Info;
            }
            else if (item.Kind == CameraStreamItemKind.Packet)
            {
                if (kinds.Count == 2)
                {
                    firstPacketIsKey = item.Packet.IsKeyFrame;
                }

                Assert.True(item.Packet.Data.Length > 0);
                if (item.Packet.Dts is { } dts)
                {
                    Assert.True(dts > previousDts, "Timestamps must increase.");
                    previousDts = dts;
                }
            }
        }

        Assert.Equal(CameraStreamItemKind.StreamStarted, kinds[0]);
        Assert.Equal(TestFixtures.Buddy3DFrameCount, kinds.Count(static k => k == CameraStreamItemKind.Packet));
        Assert.DoesNotContain(CameraStreamItemKind.Frame, kinds);
        Assert.NotNull(info);
        Assert.Equal(CameraVideoCodec.H264, info.Codec);
        Assert.Equal((TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (info.Width, info.Height));
        Assert.True(firstPacketIsKey);
        Assert.Equal(0, feed.DecodedFrames);
        Assert.False(feed.DecoderOpened);
    }

    [FFmpegFact]
    public async Task PacketsAndFramesComeTogetherEachPacketBeforeItsFrame()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        int packets = 0;
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: true, packets: true), Ct))
        {
            if (item.Kind == CameraStreamItemKind.Packet)
            {
                packets++;
            }
            else if (item.Kind == CameraStreamItemKind.Frame)
            {
                frames++;
                Assert.True(frames <= packets, "A frame never comes before its packet.");
                Assert.Equal(TestFixtures.Buddy3DWidth, item.Frame.Width);
            }
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, packets);
        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.Equal(TestFixtures.Buddy3DFrameCount, feed.DecodedFrames);
    }

    [FFmpegFact]
    public async Task FramesSwitchedOnLaterStartAtAKeyFrame()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: false, packets: true);
        int packets = 0;
        int keyPacketWhenSwitched = -1;
        int firstFramePacket = -1;
        List<int> keyPackets = [];

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind == CameraStreamItemKind.Packet)
            {
                packets++;
                if (item.Packet.IsKeyFrame)
                {
                    keyPackets.Add(packets);
                }

                if (packets == 5)
                {
                    // A window opens while only a recording reads the stream.
                    demand.Frames = true;
                    keyPacketWhenSwitched = keyPackets.Count;
                }
            }
            else if (item.Kind == CameraStreamItemKind.Frame && firstFramePacket < 0)
            {
                firstFramePacket = packets;
            }
        }

        Assert.True(feed.DecoderOpened);
        Assert.True(firstFramePacket > 5, $"First frame after packet {firstFramePacket}.");
        // The first decoded frame belongs to the first key frame after the switch.
        int nextKey = keyPackets.First(static p => p > 5);
        Assert.Equal(nextKey, firstFramePacket);
        Assert.True(feed.DecodedFrames > 0);
        Assert.True(feed.DecodedFrames <= TestFixtures.Buddy3DFrameCount - nextKey + 1);
        Assert.True(keyPacketWhenSwitched >= 1);
    }

    [FFmpegFact]
    public async Task FramesSwitchedOffStopDecoding()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: true, packets: true);
        int frames = 0;
        long decodedAtSwitch = -1;

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame && ++frames == 10)
            {
                demand.Frames = false;
                decodedAtSwitch = feed.DecodedFrames;
            }
        }

        Assert.Equal(10, frames);
        Assert.Equal(decodedAtSwitch, feed.DecodedFrames);
    }

    [FFmpegFact]
    public async Task SubscribersOfTheHubShareOneRtspConnection()
    {
        await using RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true);
        await using CameraStreamHub hub = new(CreateReconnectingFeed(), TimeProvider.System, NullLogger<CameraStreamHub>.Instance, TimeSpan.Zero);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        stop.CancelAfter(TimeSpan.FromSeconds(30));
        int maxSessions = 0;

        CameraFrameSubscription window = hub.SubscribeFrames("mk4", camera.StreamUri);
        CameraFrameSubscription detection = hub.SubscribeFrames("mk4", camera.StreamUri, new CameraFrameSubscriptionOptions { MinFrameInterval = TimeSpan.FromMilliseconds(200) });
        CameraPacketSubscription recording = hub.SubscribePackets("mk4", camera.StreamUri);
        int[] counts = await Task.WhenAll(CountFramesAsync(window, 30), CountFramesAsync(detection, 3), CountPacketsAsync(recording, 30));

        Assert.Equal([30, 3, 30], counts);
        Assert.Equal(1, camera.Connections);
        Assert.Equal(1, maxSessions);

        window.Dispose();
        detection.Dispose();
        recording.Dispose();
        // Without linger: the connection closes with the last lease, no session is left open.
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while ((camera.ActiveSessions > 0 || hub.OpenConnections > 0) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, Ct);
        }

        Assert.Equal(0, camera.ActiveSessions);
        Assert.Equal(0, hub.OpenConnections);

        async Task<int> CountFramesAsync(CameraFrameSubscription subscription, int wanted)
        {
            int count = 0;
            while (count < wanted)
            {
                stop.Token.ThrowIfCancellationRequested();
                if (subscription.TakeLatest() is { } frame)
                {
                    Assert.Equal(TestFixtures.Buddy3DWidth, frame.Width);
                    count++;
                    maxSessions = Math.Max(maxSessions, camera.ActiveSessions);
                }
                else
                {
                    await Task.Delay(5, stop.Token);
                }
            }

            return count;
        }

        async Task<int> CountPacketsAsync(CameraPacketSubscription subscription, int wanted)
        {
            int count = 0;
            await foreach (CameraStreamItem item in subscription.ReadAsync(stop.Token))
            {
                if (item.Kind == CameraStreamItemKind.Packet && ++count == wanted)
                {
                    break;
                }
            }

            return count;
        }
    }

    [FFmpegFact]
    public async Task PacketSubscriberAloneOnTheSimulatorDecodesNothing()
    {
        await using RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true);
        FeedCapture capture = new(new FFmpegCameraFeedFactory());
        ReconnectingCameraFeed feed = new(capture, TimeProvider.System, NullLogger<ReconnectingCameraFeed>.Instance);
        await using CameraStreamHub hub = new(feed, TimeProvider.System, NullLogger<CameraStreamHub>.Instance, TimeSpan.Zero);
        using CameraPacketSubscription recording = hub.SubscribePackets("mk4", camera.StreamUri);
        CameraStreamInfo? info = null;
        int packets = 0;

        await foreach (CameraStreamItem item in recording.ReadAsync(Ct))
        {
            if (item.Kind == CameraStreamItemKind.StreamStarted)
            {
                info = item.Info;
            }
            else if (++packets == 20)
            {
                break;
            }
        }

        FFmpegCameraFeed connection = Assert.Single(capture.Feeds);
        Assert.False(connection.DecoderOpened);
        Assert.Equal(0, connection.DecodedFrames);
        Assert.NotNull(info);
        Assert.Equal(CameraTimeBase.Rtp90kHz, info.TimeBase);
    }

    private static ReconnectingCameraFeed CreateReconnectingFeed() => new(
        new FFmpegCameraFeedFactory(),
        TimeProvider.System,
        NullLogger<ReconnectingCameraFeed>.Instance,
        new CameraFeedOptions { ReconnectDelay = TimeSpan.FromMilliseconds(200), FrameTimeout = TimeSpan.FromSeconds(2) });

    /// <summary>Keeps the FFmpeg feeds it creates, so that the test can inspect them.</summary>
    private sealed class FeedCapture(FFmpegCameraFeedFactory inner) : ICameraFeedFactory
    {
        private readonly List<FFmpegCameraFeed> _feeds = [];

        public IReadOnlyList<FFmpegCameraFeed> Feeds
        {
            get
            {
                lock (_feeds)
                {
                    return [.. _feeds];
                }
            }
        }

        public ICameraFeed Create(Uri streamUri)
        {
            FFmpegCameraFeed feed = (FFmpegCameraFeed)inner.Create(streamUri);
            lock (_feeds)
            {
                _feeds.Add(feed);
            }

            return feed;
        }
    }
}
