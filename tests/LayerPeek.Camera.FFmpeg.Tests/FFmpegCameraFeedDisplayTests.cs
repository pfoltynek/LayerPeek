using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-184: the decoder converts each frame only in the sizes the demand wants: scaled down to the display size in the same
/// swscale pass (aspect ratio kept, never scaled up), in full resolution while that is due, and the size can change from
/// one frame to the next without interrupting the stream (fixture: 1280×720, <see cref="TestFixtures.Buddy3DFrameCount"/> frames).
/// </summary>
public sealed class FFmpegCameraFeedDisplayTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CameraStreamDemand DisplayOnly(int width, int height)
    {
        CameraStreamDemand demand = new(frames: true, packets: false) { DisplaySize = new CameraFrameSize(width, height) };
        demand.CancelFullResolution();
        return demand;
    }

    [FFmpegFact]
    public async Task DisplayFramesAreScaledToFitKeepingTheAspectRatio()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(DisplayOnly(640, 640), Ct))
        {
            if (item.Kind != CameraStreamItemKind.Frame)
            {
                continue;
            }

            frames++;
            CameraFrame frame = item.DisplayFrame;
            Assert.Equal((640, 360), (frame.Width, frame.Height));
            Assert.Equal((TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (frame.SourceWidth, frame.SourceHeight));
            // Rows stay aligned for SIMD; nothing was converted in full resolution.
            Assert.Equal(0, frame.Stride % 64);
            Assert.Equal(0, item.Frame.Width);
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
    }

    [FFmpegTheory]
    [InlineData(320, 180, 320, 180)]
    // Odd sizes: the second pass converts whole SIMD blocks, the extra columns and row must stay outside the frame.
    [InlineData(1000, 301, 535, 301)]
    public async Task BothSizesShowTheSamePicture(int maxWidth, int maxHeight, int width, int height)
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: true, packets: false) { DisplaySize = new CameraFrameSize(maxWidth, maxHeight) };
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind != CameraStreamItemKind.Frame)
            {
                continue;
            }

            frames++;
            Assert.Equal((TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (item.Frame.Width, item.Frame.Height));
            Assert.Equal((width, height), (item.DisplayFrame.Width, item.DisplayFrame.Height));
            // Same colours (same YUV -> RGB coefficients and range) and a real picture, not an empty buffer.
            (double b, double g, double r) full = MeanColour(item.Frame);
            (double b, double g, double r) display = MeanColour(item.DisplayFrame);
            Assert.InRange(display.b - full.b, -2, 2);
            Assert.InRange(display.g - full.g, -2, 2);
            Assert.InRange(display.r - full.r, -2, 2);
            Assert.InRange(display.g, 20, 235);
            // The last column and row are picture like their neighbours, not leftovers of the buffer.
            Assert.InRange(MeanGreen(item.DisplayFrame, column: width - 1) - MeanGreen(item.DisplayFrame, column: width - 2), -12, 12);
            Assert.InRange(MeanGreen(item.DisplayFrame, row: height - 1) - MeanGreen(item.DisplayFrame, row: height - 2), -12, 12);
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
    }

    [FFmpegFact]
    public async Task ADisplaySizeLargerThanTheCameraIsNotScaledUp()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);

        await foreach (CameraStreamItem item in feed.ReadAsync(DisplayOnly(1920, 1080), Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame)
            {
                // Converted once, in the camera's resolution, for display too.
                Assert.Equal(0, item.DisplayFrame.Width);
                Assert.Equal((TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (item.Frame.Width, item.Frame.Height));
            }
        }
    }

    [FFmpegFact]
    public async Task ChangingTheDisplaySizeReconfiguresWithoutInterruptingTheStream()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamDemand demand = DisplayOnly(640, 640);
        List<(int, int)> sizes = [];

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind != CameraStreamItemKind.Frame)
            {
                continue;
            }

            CameraFrame frame = item.DisplayFrame.Width > 0 ? item.DisplayFrame : item.Frame;
            sizes.Add((frame.Width, frame.Height));
            switch (sizes.Count)
            {
                case 10:
                    // The mini preview.
                    demand.DisplaySize = new CameraFrameSize(400, 400);
                    break;
                case 20:
                    // A window wider than the image.
                    demand.DisplaySize = new CameraFrameSize(1000, 300);
                    break;
                case 30:
                    // Back to the first size (its scaler and buffer are rebuilt, the stream goes on).
                    demand.DisplaySize = new CameraFrameSize(640, 640);
                    break;
                case 40:
                    demand.DisplaySize = null;
                    demand.RequestFullResolution(long.MinValue);
                    break;
            }
        }

        // Every frame of the file in one connection: the change applied from the next frame on, no restart at a key frame.
        Assert.Equal(TestFixtures.Buddy3DFrameCount, sizes.Count);
        Assert.Equal(TestFixtures.Buddy3DFrameCount, feed.DecodedFrames);
        Assert.Equal(
            [.. Repeat((640, 360), 10), .. Repeat((400, 225), 10), .. Repeat((533, 300), 10), .. Repeat((640, 360), 10), .. Repeat((1280, 720), 31)],
            sizes);

        static IEnumerable<(int, int)> Repeat((int, int) size, int count) => Enumerable.Repeat(size, count);
    }

    [FFmpegFact]
    public async Task WithoutAnyConversionWantedFramesAreDecodedButCarryNoPixels()
    {
        // A lingering connection: decoding goes on (the next reader needs no key frame), nobody wants the pixels.
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: true, packets: false);
        demand.CancelFullResolution();
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame)
            {
                frames++;
                Assert.Equal((0, 0), (item.Frame.Width, item.DisplayFrame.Width));
            }
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.Equal(TestFixtures.Buddy3DFrameCount, feed.DecodedFrames);
    }

    [FFmpegTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AKeyFrameDecodedAloneIsConvertedOnlyInTheSizesThatAreDue(bool fullResolution)
    {
        // T-189 (before: always in full resolution, T-153): a key frame lease for display, and the alarm snapshot.
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: false, packets: false) { DisplaySize = new CameraFrameSize(320, 320) };
        if (!fullResolution)
        {
            demand.CancelFullResolution();
        }

        demand.RequestKeyFrame(long.MinValue);
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame)
            {
                frames++;
                Assert.Equal((320, 180), (item.DisplayFrame.Width, item.DisplayFrame.Height));
                Assert.Equal(fullResolution ? (TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight) : (0, 0), (item.Frame.Width, item.Frame.Height));
                Assert.Equal(0, item.GrayscaleFrame.Width);
            }
        }

        Assert.Equal(TestFixtures.Buddy3DKeyFrameCount, frames);
    }

    [FFmpegFact]
    public async Task ThroughTheHubTheWindowGetsItsSizeAndAFullResolutionLeaseTheCamerasResolution()
    {
        ReconnectingCameraFeed camera = new(new FFmpegCameraFeedFactory(), TimeProvider.System, NullLogger<ReconnectingCameraFeed>.Instance);
        await using CameraStreamHub hub = new(camera, TimeProvider.System, NullLogger<CameraStreamHub>.Instance, TimeSpan.Zero);
        using CameraFrameSubscription window = hub.SubscribeFrames("mk4", TestFixtures.Buddy3D, new CameraFrameSubscriptionOptions
        {
            DisplaySize = new CameraFrameSize(480, 480),
        });
        using CameraFrameSubscription full = hub.SubscribeFrames("mk4", TestFixtures.Buddy3D);

        CameraFrameBuffer shown = await NextFrameAsync(window);
        CameraFrameBuffer copy = await NextFrameAsync(full);

        Assert.Equal((480, 270, TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (shown.Width, shown.Height, shown.SourceWidth, shown.SourceHeight));
        Assert.Equal((TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (copy.Width, copy.Height));
    }

    private static async Task<CameraFrameBuffer> NextFrameAsync(CameraFrameSubscription subscription)
    {
        DateTime deadline = DateTime.UtcNow + TestFixtures.Prompt;
        while (true)
        {
            if (subscription.TakeLatest() is { } frame)
            {
                return frame;
            }

            Assert.True(DateTime.UtcNow < deadline, "No frame.");
            await Task.Delay(5, Ct);
        }
    }

    /// <summary>Mean green of one <paramref name="column"/> or one <paramref name="row"/>.</summary>
    private static double MeanGreen(in CameraFrame frame, int column = -1, int row = -1)
    {
        ReadOnlySpan<byte> pixels = frame.Pixels.Span;
        long sum = 0;
        int count = column >= 0 ? frame.Height : frame.Width;
        for (int i = 0; i < count; i++)
        {
            int offset = column >= 0
                ? (i * frame.Stride) + (column * CameraFrame.BytesPerPixel)
                : (row * frame.Stride) + (i * CameraFrame.BytesPerPixel);
            sum += pixels[offset + 1];
        }

        return (double)sum / count;
    }

    /// <summary>Mean blue, green and red over every pixel.</summary>
    private static (double B, double G, double R) MeanColour(in CameraFrame frame)
    {
        ReadOnlySpan<byte> pixels = frame.Pixels.Span;
        long b = 0, g = 0, r = 0;
        for (int y = 0; y < frame.Height; y++)
        {
            ReadOnlySpan<byte> row = pixels.Slice(y * frame.Stride, frame.Width * CameraFrame.BytesPerPixel);
            for (int x = 0; x < row.Length; x += CameraFrame.BytesPerPixel)
            {
                b += row[x];
                g += row[x + 1];
                r += row[x + 2];
            }
        }

        double count = (double)frame.Width * frame.Height;
        return (b / count, g / count, r / count);
    }
}
