using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-189: the samples of print failure detection are scaled into grayscale straight from the decoded picture's luma; no
/// BGRA frame in full resolution is converted for them (fixture: 1280×720, <see cref="TestFixtures.Buddy3DFrameCount"/>
/// frames, <see cref="TestFixtures.Buddy3DKeyFrameCount"/> key frames).
/// </summary>
public sealed class FFmpegCameraFeedGrayscaleTests
{
    private static readonly CameraGrayscaleSize SampleSize = new(320, 180);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The demand of the hub for a lease of grayscale key frames only, alone on the connection.</summary>
    private static CameraStreamDemand KeyFrameSamples()
    {
        CameraStreamDemand demand = new(frames: false, packets: false);
        demand.CancelFullResolution();
        demand.RequestGrayscale(SampleSize, long.MinValue);
        demand.RequestKeyFrame(long.MinValue);
        return demand;
    }

    [FFmpegFact]
    public async Task KeyFrameSamplesAreGrayscaleOnly()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(KeyFrameSamples(), Ct))
        {
            if (item.Kind != CameraStreamItemKind.Frame)
            {
                continue;
            }

            frames++;
            CameraGrayscaleFrame gray = item.GrayscaleFrame;
            Assert.Equal((320, 180), (gray.Width, gray.Height));
            Assert.Equal((TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (gray.SourceWidth, gray.SourceHeight));
            Assert.Equal(0, gray.Stride % 64);
            // Nothing in BGRA: neither in full resolution nor for display.
            Assert.Equal((0, 0), (item.Frame.Width, item.DisplayFrame.Width));
            // A real picture, not an empty buffer.
            Assert.InRange(Mean(gray), 20, 235);
        }

        Assert.Equal(TestFixtures.Buddy3DKeyFrameCount, frames);
    }

    [FFmpegFact]
    public async Task BesideTheWindowOnlyTheDueFramesAreConvertedToGrayscale()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: true, packets: false) { DisplaySize = new CameraFrameSize(640, 640) };
        demand.CancelFullResolution();
        demand.RequestGrayscale(SampleSize, long.MinValue);
        int frames = 0;
        int samples = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind != CameraStreamItemKind.Frame)
            {
                continue;
            }

            frames++;
            Assert.Equal((640, 360), (item.DisplayFrame.Width, item.DisplayFrame.Height));
            Assert.Equal(0, item.Frame.Width);
            if (item.GrayscaleFrame.Width > 0)
            {
                samples++;
                // The hub schedules the next sample after the interval (here: never).
                demand.RequestGrayscale(SampleSize, long.MaxValue);
            }
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.Equal(1, samples);
    }

    [FFmpegFact]
    public async Task TheHubHandsDetectionGrayscaleFromTheDecoderAndTheSnapshotAFullFrame()
    {
        // The real path of print failure detection (T-153) on a live camera: its lease of grayscale key frames and the
        // short lease of the alarm snapshot.
        await using Simulator.Camera.RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true);
        ReconnectingCameraFeed feed = new(new FFmpegCameraFeedFactory(), TimeProvider.System, NullLogger<ReconnectingCameraFeed>.Instance);
        await using CameraStreamHub hub = new(feed, TimeProvider.System, NullLogger<CameraStreamHub>.Instance, TimeSpan.Zero);
        using SemaphoreSlim available = new(0);
        using CameraFrameSubscription samples = hub.SubscribeFrames("core-one", camera.StreamUri, new CameraFrameSubscriptionOptions
        {
            KeyFramesOnly = true,
            MinFrameInterval = TimeSpan.FromSeconds(1),
            Grayscale = SampleSize,
            FrameAvailable = () => available.Release(),
        });

        Assert.True(await available.WaitAsync(TimeSpan.FromSeconds(30), Ct), "No sample.");
        CameraFrameBuffer sample = samples.TakeLatest()!;
        Assert.Equal((CameraPixelFormat.Gray8, 320, 180, 320), (sample.Format, sample.Width, sample.Height, sample.Stride));
        Assert.Equal((TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (sample.SourceWidth, sample.SourceHeight));

        using SemaphoreSlim snapshotAvailable = new(0);
        using CameraFrameSubscription snapshot = hub.SubscribeFrames("core-one", camera.StreamUri, new CameraFrameSubscriptionOptions
        {
            KeyFramesOnly = true,
            FrameAvailable = () => snapshotAvailable.Release(),
        });
        Assert.True(await snapshotAvailable.WaitAsync(TimeSpan.FromSeconds(30), Ct), "No snapshot.");
        CameraFrameBuffer full = snapshot.TakeLatest()!;
        Assert.Equal((CameraPixelFormat.Bgra32, TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (full.Format, full.Width, full.Height));
    }

    private static double Mean(in CameraGrayscaleFrame frame)
    {
        ReadOnlySpan<byte> pixels = frame.Pixels.Span;
        long sum = 0;
        for (int y = 0; y < frame.Height; y++)
        {
            foreach (byte value in pixels.Slice(y * frame.Stride, frame.Width))
            {
                sum += value;
            }
        }

        return (double)sum / (frame.Width * frame.Height);
    }
}
