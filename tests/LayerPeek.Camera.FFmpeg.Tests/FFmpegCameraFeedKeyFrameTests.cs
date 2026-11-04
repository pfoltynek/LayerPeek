using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-153: without frame demand, a due key frame is decoded on its own (print failure detection samples) and nothing else
/// is decoded (fixture file with <see cref="TestFixtures.Buddy3DKeyFrameCount"/> key frames among its frames).
/// </summary>
public sealed class FFmpegCameraFeedKeyFrameTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [FFmpegFact]
    public async Task DueKeyFramesAreDecodedAloneAndNothingElse()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: false, packets: false);
        demand.RequestKeyFrame(long.MinValue);
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame)
            {
                frames++;
                Assert.Equal((TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (item.Frame.Width, item.Frame.Height));
                // A real picture, not an empty (green or black) buffer.
                Assert.InRange(MeanGreen(item.Frame), 20, 235);
            }
        }

        Assert.Equal(TestFixtures.Buddy3DKeyFrameCount, frames);
        Assert.Equal(TestFixtures.Buddy3DKeyFrameCount, feed.DecodedFrames);
    }

    [FFmpegFact]
    public async Task WithPacketsEveryPacketComesAndTheKeyFrameAfterItsPacket()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: false, packets: true);
        demand.RequestKeyFrame(long.MinValue);
        int packets = 0;
        int frames = 0;
        bool previousWasKey = false;

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind == CameraStreamItemKind.Packet)
            {
                packets++;
                previousWasKey = item.Packet.IsKeyFrame;
            }
            else if (item.Kind == CameraStreamItemKind.Frame)
            {
                Assert.True(previousWasKey, "The frame follows its key frame packet.");
                frames++;
            }
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, packets);
        Assert.Equal(TestFixtures.Buddy3DKeyFrameCount, frames);
    }

    [FFmpegFact]
    public async Task AKeyFrameThatIsNotDueIsNotDecoded()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: false, packets: true);
        demand.RequestKeyFrame(long.MinValue);
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame)
            {
                frames++;
                // The hub schedules the next sample after the interval (here: never).
                demand.CancelKeyFrame();
            }
        }

        Assert.Equal(1, frames);
        Assert.Equal(1, feed.DecodedFrames);
    }

    [FFmpegFact]
    public async Task WithoutADueKeyFrameTheDecoderIsNeverOpened()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: false, packets: true);
        demand.RequestKeyFrame(long.MaxValue - 1);

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            Assert.NotEqual(CameraStreamItemKind.Frame, item.Kind);
        }

        Assert.Equal(0, feed.DecodedFrames);
        Assert.False(feed.DecoderOpened);
    }

    [FFmpegFact]
    public async Task FullDecodingAfterAKeyFrameSampleStillWorks()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: false, packets: false);
        demand.RequestKeyFrame(long.MinValue);
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame && ++frames == 1)
            {
                // The camera window opens after the first sample: from the next key frame every frame is decoded.
                demand.CancelKeyFrame();
                demand.Frames = true;
            }
        }

        // The second key frame and all frames after it; the first sample alone before.
        Assert.True(frames > TestFixtures.Buddy3DKeyFrameCount, $"Only {frames} frames.");
        Assert.True(frames < TestFixtures.Buddy3DFrameCount, $"{frames} frames: the frames before the next key frame cannot be decoded.");
    }

    private static double MeanGreen(in CameraFrame frame)
    {
        ReadOnlySpan<byte> pixels = frame.Pixels.Span;
        long sum = 0;
        int count = 0;
        for (int y = 0; y < frame.Height; y += 16)
        {
            for (int x = 0; x < frame.Width; x += 16)
            {
                sum += pixels[(y * frame.Stride) + (x * 4) + 1];
                count++;
            }
        }

        return (double)sum / count;
    }
}
