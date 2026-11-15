using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras;
using LayerPeek.Simulator.Camera;

using static LayerPeek.Camera.FFmpeg.Tests.HardwareDecodingFallbackTests;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-203 / T-206 with the hardware decoder of this OS (<see cref="HardwareDecodingTestEnvironment.Device"/>: VideoToolbox on
/// macOS, D3D11VA on Windows, where "Automatic" means it here as if the rule chose it): the camera window's frames come
/// from the hardware decoder and are mapped (VideoToolbox) or copied (D3D11VA) only when converted; failures fall back to
/// software for the rest of the connection; the rule against switching back and forth; a changed setting applies at the
/// next key frame; the device lives only during full decoding. Fixture: <see cref="TestFixtures.Buddy3D"/>
/// (<see cref="TestFixtures.Buddy3DKeyFrameCount"/> key frames, the first one first). D3D11VA only:
/// <see cref="HardwareDecodingD3D11VATests"/>.
/// </summary>
public sealed class HardwareDecodingDeviceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string DeviceName => HardwareDecodingTestEnvironment.DeviceName;

    private static bool MapsFrames => HardwareDecodingTestEnvironment.MapsFrames;

    /// <summary>Hardware frames brought into memory for a conversion: mapped (VideoToolbox) or copied (D3D11VA).</summary>
    internal static long ConvertedInHardware(FFmpegCameraFeed feed) => feed.MappedFrames + feed.TransferredFrames;

    [HardwareDecodingFact]
    public async Task TheCameraWindowDecodesInHardwareAndMapsOrCopiesEveryConvertedFrame()
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
                Assert.Equal(frames, ConvertedInHardware(feed));
            }
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.True(heldDevice);
        Assert.Equal(MapsFrames ? TestFixtures.Buddy3DFrameCount : 0, feed.MappedFrames);
        Assert.Equal(MapsFrames ? 0 : TestFixtures.Buddy3DFrameCount, feed.TransferredFrames);
        Assert.Equal(1, factory.Hardware.DevicesCreated);
        Assert.Equal([$"Camera video decoding: {DeviceName}."], log.At(LogLevel.Information));
        Assert.Empty(log.At(LogLevel.Warning));
        // Released with the end of the stream.
        Assert.False(feed.HoldsHardwareDevice);
    }

    [HardwareDecodingFact]
    public async Task HardwareFramesLookLikeSoftwareFrames()
    {
        // The same frames of both paths in full resolution: NV12 from the hardware through the same swscale conversion.
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

            Assert.True(ConvertedInHardware(hardwareFeed) > 0);
        }
        finally
        {
            await fromHardware.DisposeAsync();
            await fromSoftware.DisposeAsync();
        }
    }

    [HardwareDecodingFact]
    public async Task OnlyFramesThatAreDueAreMappedOrCopied()
    {
        // T-185: the mini preview takes a few frames per second; the others are decoded in hardware but never mapped or copied.
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
        Assert.Equal(converted, ConvertedInHardware(feed));
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
        // VideoToolbox tried once and says so; D3D11VA never maps (T-206), so there is nothing to fail.
        Assert.Equal(
            MapsFrames ? 1 : 0,
            log.At(LogLevel.Information).Count(static line => line.StartsWith($"Mapping hardware video frames ({DeviceName}) failed", StringComparison.Ordinal)));
        Assert.Empty(log.At(LogLevel.Warning));
        // All copies of the session go into one buffer (T-206).
        Assert.Equal(1, feed.CopyBuffersAllocated);
    }

    [HardwareDecodingFact]
    public async Task ACopyBufferIsAllocatedAgainOnlyWhenThePictureSizeChanges()
    {
        // T-206: the stream changes its resolution mid-session (the Buddy3D fixture at 1280 × 720, then I_PCM key frames
        // at 320 × 180 with their own SPS): the session's copy buffer is replaced once and reused again afterwards.
        const int SmallWidth = 320;
        const int SmallHeight = 180;
        const int SmallFrames = 6;
        string directory = TestDirectories.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "two-sizes.h264");
            byte[][] small = [.. Enumerable.Range(0, SmallFrames).Select(static i => Gradient(SmallWidth, SmallHeight, i))];
            await File.WriteAllBytesAsync(path, [.. await File.ReadAllBytesAsync(TestFixtures.Buddy3D.LocalPath, Ct), .. PcmH264Writer.Encode(small, SmallWidth, SmallHeight)], Ct);
            (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic);
            // Copy mode on both devices (VideoToolbox would map otherwise).
            factory.Hardware.Faults = new HardwareDecodingFaults { FailMapping = true };
            await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(new Uri(path));
            List<(int Width, int Height)> sizes = [];

            await foreach (CameraStreamItem item in feed.ReadAsync(Window(), Ct))
            {
                if (item.Kind == CameraStreamItemKind.Frame)
                {
                    // The small picture fits the window and is converted in full resolution (T-195).
                    CameraFrame frame = item.DisplayFrame.Width > 0 ? item.DisplayFrame : item.Frame;
                    sizes.Add((frame.Width, frame.Height));
                }
            }

            Assert.Equal(TestFixtures.Buddy3DFrameCount + SmallFrames, sizes.Count);
            Assert.Equal((SmallWidth, SmallHeight), sizes[^1]);
            Assert.Equal(sizes.Count, feed.TransferredFrames);
            Assert.Equal(2, feed.CopyBuffersAllocated);
            Assert.Equal(1, factory.Hardware.DevicesCreated);
            Assert.Empty(log.At(LogLevel.Warning));
        }
        finally
        {
            TestDirectories.Delete(directory);
        }
    }

    [HardwareDecodingFact]
    public async Task ADeviceLostWhileCopyingContinuesInSoftwareFromTheNextKeyFrame()
    {
        // T-206: a lost D3D11 device (driver restart, Win+Ctrl+Shift+B, a GPU switch) shows when its staging texture is
        // mapped for a copy; the camera window goes on in software without an error of the feed.
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic);
        factory.Hardware.Faults = new HardwareDecodingFaults { FailMapping = true, FailCopyAfterFrames = 3 };
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);

        int frames = await ReadDisplayFramesAsync(feed, Window());

        // Three copies from the hardware, the frame of the failed copy and those until the next key frame are lost.
        Assert.InRange(frames, 4, TestFixtures.Buddy3DFrameCount - 2);
        Assert.Equal(3, feed.TransferredFrames);
        Assert.False(feed.HoldsHardwareDevice);
        Assert.Equal(1, factory.Hardware.DevicesCreated);
        string warning = Assert.Single(log.At(LogLevel.Warning));
        Assert.Contains($"Hardware video decoding ({DeviceName}) failed while copying a frame", warning, StringComparison.Ordinal);
        Assert.Contains("Camera video decoding: software (hardware decoding failed on this connection).", log.At(LogLevel.Information));
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
        Assert.Equal(3, ConvertedInHardware(feed));
        // The connection never tried the hardware again.
        Assert.Equal(1, factory.Hardware.DevicesCreated);
        string warning = Assert.Single(log.At(LogLevel.Warning));
        Assert.Contains($"Hardware video decoding ({DeviceName}) failed while decoding", warning, StringComparison.Ordinal);
        Assert.Equal(
            [$"Camera video decoding: {DeviceName}.", "Camera video decoding: software (hardware decoding failed on this connection)."],
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
                    mappedWhenChanged = ConvertedInHardware(feed);
                    Assert.True(feed.HoldsHardwareDevice);
                }
            }
        }

        // The switch waits for the next key frame and loses nothing.
        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.InRange(ConvertedInHardware(feed), mappedWhenChanged + 1, TestFixtures.Buddy3DFrameCount - 1);
        Assert.Equal(
            [$"Camera video decoding: {DeviceName}.", "Camera video decoding: software (turned off in the settings)."],
            log.At(LogLevel.Information));
        Assert.Empty(log.At(LogLevel.Warning));
    }

    [HardwareDecodingFact]
    public async Task AChangedSettingKeepsTheSimulatedCameraRunningWhenItsClipHasTheParameterSetsOnlyOnce()
    {
        // T-205: the simulator played an MP4 clip (SPS and PPS only in avcC) with the parameter sets before the first IDR
        // frame only; the decoder that the switch opens at the next key frame then had none and the window stood still
        // until the clip looped. Now the simulator sends them before every IDR frame, like the Buddy3D.
        using MemoryStream annexB = new();
        for (int f = 0; f < TestFixtures.Buddy3DClip.Frames.Count; f++)
        {
            foreach (byte[] nal in TestFixtures.Buddy3DClip.Frames[f])
            {
                if (f == 0 || (nal[0] & 0x1F) is not (7 or 8))
                {
                    annexB.Write([0, 0, 0, 1]);
                    annexB.Write(nal);
                }
            }
        }

        await using RtspCamera camera = new(new RtspCameraOptions { Clip = H264Clip.Parse(annexB.ToArray()), Loop = false, RealTime = false });
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic);
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(camera.StreamUri);
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(Window(), Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame && ++frames == 5)
            {
                factory.HardwareDecoding = CameraHardwareDecoding.Off;
            }
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.Equal(
            [$"Camera video decoding: {DeviceName}.", "Camera video decoding: software (turned off in the settings)."],
            log.At(LogLevel.Information));
    }

    [HardwareDecodingFact]
    public async Task AFailureAfterTheSettingChangedAndChangedBackCountsInTheCurrentGeneration()
    {
        // The setting goes to Off and back to Automatic within one GOP: the hardware session carries on past the next key
        // frame (frame 51 of the fixture) under the new choice, so a failure after it counts there and ends hardware for
        // the connection, instead of being booked to the old generation and retried at the following key frame.
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic);
        factory.Hardware.Faults = new HardwareDecodingFaults { FailDecodingAfterFrames = 60 };
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(Window(), Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame && ++frames == 5)
            {
                factory.HardwareDecoding = CameraHardwareDecoding.Off;
                factory.HardwareDecoding = CameraHardwareDecoding.Automatic;
            }
        }

        Assert.Equal(1, factory.Hardware.DevicesCreated);
        Assert.Single(log.At(LogLevel.Warning));
        Assert.Equal(1, feed.HardwareHistory.FailuresIn(factory.Hardware.Current.Generation));
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
        Assert.InRange(ConvertedInHardware(feed), 1, TestFixtures.Buddy3DFrameCount - 5);
        Assert.Equal(
            ["Camera video decoding: software (no camera window).", $"Camera video decoding: {DeviceName}."],
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

    /// <summary>A horizontal luma gradient shifted by <paramref name="shift"/>.</summary>
    private static byte[] Gradient(int width, int height, int shift)
    {
        byte[] luma = new byte[width * height];
        for (int i = 0; i < luma.Length; i++)
        {
            luma[i] = (byte)(16 + (((i % width) + (shift * 8)) % 220));
        }

        return luma;
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
