using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras;

using static LayerPeek.Camera.FFmpeg.Tests.HardwareDecodingFallbackTests;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-203 with VideoToolbox (macOS): the camera window's frames come from the hardware decoder, mapped without a copy and
/// only when converted; failures fall back to software for the rest of the connection; the rule against switching back
/// and forth; a changed setting applies at the next key frame; the device lives only during full decoding. Fixture:
/// <see cref="TestFixtures.Buddy3D"/> (<see cref="TestFixtures.Buddy3DKeyFrameCount"/> key frames, the first one first).
/// </summary>
public sealed class HardwareDecodingVideoToolboxTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [HardwareDecodingFact]
    public async Task TheCameraWindowDecodesInHardwareAndMapsEveryConvertedFrame()
    {
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic);
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);
        int frames = 0;
        bool heldDevice = true;

        await foreach (CameraStreamItem item in feed.ReadAsync(Window(), Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame)
            {
                frames++;
                heldDevice &= feed.HoldsHardwareDevice;
                Assert.Equal((640, 360), (item.DisplayFrame.Width, item.DisplayFrame.Height));
                Assert.InRange(MeanGreen(item.DisplayFrame), 20, 235);
                Assert.Equal(frames, feed.MappedFrames);
            }
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.True(heldDevice);
        Assert.Equal(0, feed.TransferredFrames);
        Assert.Equal(1, factory.Hardware.DevicesCreated);
        Assert.Equal(["Camera video decoding: videotoolbox."], log.At(LogLevel.Information));
        Assert.Empty(log.At(LogLevel.Warning));
        // Released with the end of the stream.
        Assert.False(feed.HoldsHardwareDevice);
    }

    [HardwareDecodingFact]
    public async Task HardwareFramesLookLikeSoftwareFrames()
    {
        // The same frames of both paths in full resolution: NV12 from VideoToolbox through the same swscale conversion.
        (FFmpegCameraFeedFactory hardware, _) = CreateFactory(CameraHardwareDecoding.Automatic);
        (FFmpegCameraFeedFactory software, _) = CreateFactory(CameraHardwareDecoding.Off);
        await using FFmpegCameraFeed hardwareFeed = (FFmpegCameraFeed)hardware.Create(TestFixtures.Buddy3D);
        await using FFmpegCameraFeed softwareFeed = (FFmpegCameraFeed)software.Create(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: true, packets: false) { DisplaySize = new CameraFrameSize(1280, 720) };
        IAsyncEnumerator<CameraStreamItem> fromHardware = hardwareFeed.ReadAsync(demand, Ct).GetAsyncEnumerator(Ct);
        IAsyncEnumerator<CameraStreamItem> fromSoftware = softwareFeed.ReadAsync(new CameraStreamDemand(frames: true, packets: false), Ct).GetAsyncEnumerator(Ct);
        try
        {
            for (int i = 0; i < 10; i++)
            {
                CameraFrame a = await NextFrameAsync(fromHardware);
                CameraFrame b = await NextFrameAsync(fromSoftware);
                Assert.Equal((b.Width, b.Height), (a.Width, a.Height));
                Assert.InRange(MeanAbsoluteDifference(a, b), 0, 2.0);
            }

            Assert.True(hardwareFeed.MappedFrames > 0);
        }
        finally
        {
            await fromHardware.DisposeAsync();
            await fromSoftware.DisposeAsync();
        }
    }

    [HardwareDecodingFact]
    public async Task OnlyFramesThatAreDueAreMapped()
    {
        // T-185: the mini preview takes a few frames per second; the others are decoded in hardware but never mapped.
        ManualClock clock = new();
        CameraStreamDemand demand = new(frames: true, packets: false, clock) { DisplaySize = new CameraFrameSize(640, 360) };
        demand.CancelFullResolution();
        (FFmpegCameraFeedFactory factory, _) = CreateFactory(CameraHardwareDecoding.Automatic);
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);
        int frames = 0;
        int converted = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind != CameraStreamItemKind.Frame)
            {
                continue;
            }

            frames++;
            converted += item.DisplayFrame.Width > 0 ? 1 : 0;
            // Every tenth frame is due.
            demand.RequestDisplay(frames % 10 == 9 ? long.MinValue : clock.Now + 1);
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.Equal(TestFixtures.Buddy3DFrameCount, feed.DecodedFrames);
        // The first frame and every tenth after it.
        Assert.Equal(1 + ((TestFixtures.Buddy3DFrameCount - 1) / 10), converted);
        Assert.Equal(converted, feed.MappedFrames);
    }

    [HardwareDecodingFact]
    public async Task FramesThatCannotBeMappedAreCopiedForTheRestOfTheSession()
    {
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic);
        factory.Hardware.Faults = new HardwareDecodingFaults { FailMapping = true };
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);

        Assert.Equal(TestFixtures.Buddy3DFrameCount, await ReadDisplayFramesAsync(feed, Window()));

        Assert.Equal(0, feed.MappedFrames);
        Assert.Equal(TestFixtures.Buddy3DFrameCount, feed.TransferredFrames);
        Assert.Single(log.At(LogLevel.Information), static line => line.StartsWith("Mapping hardware video frames (videotoolbox) failed", StringComparison.Ordinal));
        Assert.Empty(log.At(LogLevel.Warning));
    }

    [HardwareDecodingFact]
    public async Task AFailureWhileDecodingContinuesInSoftwareFromTheNextKeyFrame()
    {
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic);
        factory.Hardware.Faults = new HardwareDecodingFaults { FailDecodingAfterFrames = 3 };
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);

        // No error of the feed: the frames until the next key frame are lost, the rest comes from software.
        int frames = 0;
        bool releasedAfterTheFailure = true;
        await foreach (CameraStreamItem item in feed.ReadAsync(Window(), Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame && item.DisplayFrame.Width > 0 && ++frames > 3)
            {
                // The device went with the failure, before the software decoder took over.
                releasedAfterTheFailure &= !feed.HoldsHardwareDevice;
            }
        }

        Assert.True(releasedAfterTheFailure);
        Assert.InRange(frames, 4, TestFixtures.Buddy3DFrameCount - 2);
        Assert.Equal(3, feed.MappedFrames);
        // The connection never tried the hardware again.
        Assert.Equal(1, factory.Hardware.DevicesCreated);
        string warning = Assert.Single(log.At(LogLevel.Warning));
        Assert.Contains("Hardware video decoding (videotoolbox) failed while decoding", warning, StringComparison.Ordinal);
        Assert.Equal(
            ["Camera video decoding: videotoolbox.", "Camera video decoding: software (hardware decoding failed on this connection)."],
            log.At(LogLevel.Information));
        Assert.Equal(1, feed.HardwareHistory.FailuresIn(factory.Hardware.Current.Generation));
    }

    [HardwareDecodingFact]
    public async Task AfterTwoFailedConnectionsInARowTheCameraStaysInSoftwareUntilTheSettingChanges()
    {
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic);
        factory.Hardware.Faults = new HardwareDecodingFaults { FailDecodingAfterFrames = 2 };

        // Each connection fails once and tries again on the next one.
        await ReadToEndAsync(factory);
        await ReadToEndAsync(factory);
        Assert.Equal(2, factory.Hardware.DevicesCreated);

        // The third connection does not try any more.
        await ReadToEndAsync(factory);
        Assert.Equal(2, factory.Hardware.DevicesCreated);
        Assert.Contains("Camera video decoding: software (hardware decoding failed on several connections in a row).", log.At(LogLevel.Information));
        Assert.Equal(2, log.At(LogLevel.Warning).Count);

        // A changed setting forgets the failures.
        factory.HardwareDecoding = CameraHardwareDecoding.Off;
        factory.HardwareDecoding = CameraHardwareDecoding.Automatic;
        factory.Hardware.Faults = null;
        await ReadToEndAsync(factory);
        Assert.Equal(3, factory.Hardware.DevicesCreated);
    }

    [HardwareDecodingFact]
    public async Task AConnectionWithoutFailureBetweenTwoFailuresStartsTheCountAgain()
    {
        (FFmpegCameraFeedFactory factory, _) = CreateFactory(CameraHardwareDecoding.Automatic);
        HardwareDecodingFaults failing = new() { FailDecodingAfterFrames = 2 };

        factory.Hardware.Faults = failing;
        await ReadToEndAsync(factory);
        factory.Hardware.Faults = null;
        await ReadToEndAsync(factory);
        factory.Hardware.Faults = failing;
        await ReadToEndAsync(factory);
        factory.Hardware.Faults = null;
        await ReadToEndAsync(factory);

        Assert.Equal(4, factory.Hardware.DevicesCreated);
    }

    [HardwareDecodingFact]
    public async Task TheDeviceIsReleasedWhenFullDecodingStopsAndWhenTheFeedIsDisposed()
    {
        (FFmpegCameraFeedFactory factory, _) = CreateFactory(CameraHardwareDecoding.Automatic);
        FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);
        CameraStreamDemand demand = Window();
        demand.Packets = true;
        IAsyncEnumerator<CameraStreamItem> items = feed.ReadAsync(demand, Ct).GetAsyncEnumerator(Ct);
        try
        {
            await NextFrameAsync(items, display: true);
            Assert.True(feed.HoldsHardwareDevice);

            // The window closed (and the hub's linger ended): the recording reads on, nothing is decoded any more.
            demand.Frames = false;
            Assert.True(await items.MoveNextAsync());
            Assert.False(feed.HoldsHardwareDevice);

            // The window again: a new device from the next key frame on.
            demand.Frames = true;
            await NextFrameAsync(items, display: true);
            Assert.True(feed.HoldsHardwareDevice);
            Assert.Equal(2, factory.Hardware.DevicesCreated);
        }
        finally
        {
            await items.DisposeAsync();
            await feed.DisposeAsync();
        }

        Assert.False(feed.HoldsHardwareDevice);
        Assert.False(feed.HoldsNativeResources);
    }

    [HardwareDecodingFact]
    public async Task AChangedSettingAppliesAtTheNextKeyFrameWithoutLosingFrames()
    {
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic);
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);
        int frames = 0;
        long mappedWhenChanged = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(Window(), Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame)
            {
                Assert.Equal(640, item.DisplayFrame.Width);
                if (++frames == 5)
                {
                    // "Off" saved in the settings while the window shows the camera.
                    factory.HardwareDecoding = CameraHardwareDecoding.Off;
                    mappedWhenChanged = feed.MappedFrames;
                    Assert.True(feed.HoldsHardwareDevice);
                }
            }
        }

        // The switch waits for the next key frame and loses nothing.
        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.InRange(feed.MappedFrames, mappedWhenChanged + 1, TestFixtures.Buddy3DFrameCount - 1);
        Assert.Equal(
            ["Camera video decoding: videotoolbox.", "Camera video decoding: software (turned off in the settings)."],
            log.At(LogLevel.Information));
        Assert.Empty(log.At(LogLevel.Warning));
    }

    [HardwareDecodingFact]
    public async Task AWindowThatGetsItsSizeLaterSwitchesToHardwareAtTheNextKeyFrame()
    {
        // A camera window starts in full resolution until it is laid out (T-184): software first, then hardware.
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic);
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);
        CameraStreamDemand demand = new(frames: true, packets: false);
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame && ++frames == 5)
            {
                demand.DisplaySize = new CameraFrameSize(640, 360);
            }
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.InRange(feed.MappedFrames, 1, TestFixtures.Buddy3DFrameCount - 5);
        Assert.Equal(
            ["Camera video decoding: software (no camera window).", "Camera video decoding: videotoolbox."],
            log.At(LogLevel.Information));
    }

    private static async Task ReadToEndAsync(FFmpegCameraFeedFactory factory)
    {
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);
        Assert.True(await ReadDisplayFramesAsync(feed, Window()) > 0);
    }

    private static async Task<CameraFrame> NextFrameAsync(IAsyncEnumerator<CameraStreamItem> items, bool display = false)
    {
        while (await items.MoveNextAsync())
        {
            if (items.Current.Kind == CameraStreamItemKind.Frame)
            {
                return display ? items.Current.DisplayFrame : items.Current.Frame;
            }
        }

        throw new InvalidOperationException("The stream ended.");
    }

    private static double MeanGreen(CameraFrame frame)
    {
        ReadOnlySpan<byte> pixels = frame.Pixels.Span;
        long sum = 0;
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                sum += pixels[(y * frame.Stride) + (x * CameraFrame.BytesPerPixel) + 1];
            }
        }

        return (double)sum / (frame.Width * frame.Height);
    }

    private static double MeanAbsoluteDifference(CameraFrame a, CameraFrame b)
    {
        ReadOnlySpan<byte> first = a.Pixels.Span;
        ReadOnlySpan<byte> second = b.Pixels.Span;
        long sum = 0;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width * CameraFrame.BytesPerPixel; x++)
            {
                sum += Math.Abs(first[(y * a.Stride) + x] - second[(y * b.Stride) + x]);
            }
        }

        return (double)sum / (a.Width * a.Height * CameraFrame.BytesPerPixel);
    }

    /// <summary>A clock that stands still unless the test moves it.</summary>
    private sealed class ManualClock : TimeProvider
    {
        public long Now { get; set; } = 1_000;

        public override long GetTimestamp() => Now;
    }
}
