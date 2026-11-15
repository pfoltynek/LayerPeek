using FFmpeg.AutoGen.Abstractions;

using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras;

using static LayerPeek.Camera.FFmpeg.Tests.HardwareDecodingFallbackTests;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-206, D3D11VA on Windows only: "Automatic" and "Off" decode in software until T-125 measured it and the user decided;
/// the Debug test switch (<c>LAYERPEEK_TEST_HW_DECODER=d3d11va</c>) decodes the camera window and the mini preview in
/// D3D11VA; its frames are copied (FFmpeg cannot map D3D11 textures) into one reused buffer per session. The tests of
/// <see cref="HardwareDecodingDeviceTests"/> cover the rest with D3D11VA on Windows. Skipped without a graphics adapter that
/// decodes H.264 (a VM or CI runner with the Microsoft Basic Render Driver).
/// </summary>
public sealed class HardwareDecodingD3D11VATests
{
    private const AVHWDeviceType D3D11VA = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;

    /// <summary>AVERROR(ENOSYS) of the Windows FFmpeg build: ENOSYS is 40 in the Microsoft C runtime (errno.h).</summary>
    private static readonly int ErrorNotImplemented = ffmpeg.AVERROR(40);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [FFmpegFact]
    public async Task OnWindowsAutomaticAndOffDecodeInSoftware()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The rule of Windows (T-206); macOS: HardwareDecodingDeviceTests.");

        foreach (CameraHardwareDecoding mode in new[] { CameraHardwareDecoding.Automatic, CameraHardwareDecoding.Off })
        {
            // The factory of the application: the rule of the OS, no test seam.
            ListLogger log = new();
            FFmpegCameraFeedFactory factory = new(FFmpegCameraFeedFactory.DefaultIOTimeout, log) { HardwareDecoding = mode };
            await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);

            Assert.Equal(TestFixtures.Buddy3DFrameCount, await ReadDisplayFramesAsync(feed, Window()));

            Assert.Equal(0, factory.Hardware.DevicesCreated);
            Assert.Equal(0, HardwareDecodingDeviceTests.ConvertedInHardware(feed));
            string reason = mode == CameraHardwareDecoding.Off ? "turned off in the settings" : "not used on this OS yet";
            Assert.Equal([$"Camera video decoding: software ({reason})."], log.At(LogLevel.Information));
        }
    }

#if DEBUG
    [HardwareDecodingFact]
    public async Task TheTestSwitchDecodesTheCameraWindowAndTheMiniPreviewInD3D11VA()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D3D11VA is Windows only.");

        // The factory of the application with "Automatic" (software on Windows) and LAYERPEEK_TEST_HW_DECODER=d3d11va.
        ListLogger log = new();
        FFmpegCameraFeedFactory factory = new(FFmpegCameraFeedFactory.DefaultIOTimeout, log) { HardwareDecoding = CameraHardwareDecoding.Automatic };
        factory.UseTestHardwareDecoder("d3d11va");

        // The normal camera window: every frame copied for display.
        await using (FFmpegCameraFeed window = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D))
        {
            Assert.Equal(TestFixtures.Buddy3DFrameCount, await ReadDisplayFramesAsync(window, Window()));
            Assert.Equal(TestFixtures.Buddy3DFrameCount, window.TransferredFrames);
        }

        // The mini preview (T-185, a few frames per second): only the frames that are due are copied.
        CameraStreamDemand preview = Window();
        await using (FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D))
        {
            int frames = 0;
            int converted = 0;
            await foreach (CameraStreamItem item in feed.ReadAsync(preview, Ct))
            {
                if (item.Kind == CameraStreamItemKind.Frame)
                {
                    frames++;
                    converted += item.DisplayFrame.Width > 0 ? 1 : 0;
                    // Every tenth frame is due (the clock of the demand never reaches the next due time otherwise).
                    preview.RequestDisplay(frames % 10 == 9 ? long.MinValue : long.MaxValue);
                }
            }

            Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
            Assert.Equal(1 + ((TestFixtures.Buddy3DFrameCount - 1) / 10), converted);
            Assert.Equal(converted, feed.TransferredFrames);
            Assert.Equal(0, feed.MappedFrames);
        }

        Assert.Equal(2, factory.Hardware.DevicesCreated);
        Assert.Equal(
            ["Hardware decoder test switch: d3d11va for every camera window.", "Camera video decoding: d3d11va.", "Camera video decoding: d3d11va."],
            log.At(LogLevel.Information));
        Assert.Empty(log.At(LogLevel.Warning));
    }
#endif

    [HardwareDecodingFact]
    public async Task D3D11VAFramesAreCopiedIntoOneBufferPerSessionAndNeverMapped()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D3D11VA is Windows only.");

        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic);
        Assert.Equal(D3D11VA, factory.Hardware.Current.Device);
        FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);
        CameraStreamDemand demand = Window();
        demand.Packets = true;
        IAsyncEnumerator<CameraStreamItem> items = feed.ReadAsync(demand, Ct).GetAsyncEnumerator(Ct);
        try
        {
            for (int frames = 0; frames < 20;)
            {
                Assert.True(await items.MoveNextAsync());
                frames += items.Current.Kind == CameraStreamItemKind.Frame ? 1 : 0;
            }

            Assert.Equal(20, feed.TransferredFrames);
            Assert.Equal(1, feed.CopyBuffersAllocated);

            // Full decoding stops (the window closed): the session and its buffer go; the next one gets a new buffer.
            demand.Frames = false;
            Assert.True(await items.MoveNextAsync());
            Assert.False(feed.HoldsHardwareDevice);
            demand.Frames = true;
            while (await items.MoveNextAsync() && items.Current.Kind != CameraStreamItemKind.Frame)
            {
            }

            Assert.True(feed.HoldsHardwareDevice);
            Assert.Equal(2, feed.CopyBuffersAllocated);
        }
        finally
        {
            await items.DisposeAsync();
            await feed.DisposeAsync();
        }

        Assert.Equal(0, feed.MappedFrames);
        Assert.DoesNotContain(log.At(LogLevel.Information), static line => line.StartsWith("Mapping", StringComparison.Ordinal));
        Assert.Empty(log.At(LogLevel.Warning));
        Assert.False(feed.HoldsNativeResources);
    }

    [HardwareDecodingFact]
    public unsafe void FFmpegCannotMapD3D11Frames()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D3D11VA is Windows only.");

        // HardwareDecoderSelection.MapsFrames(D3D11VA) is false because FFmpeg's hwcontext d3d11va has no mapping of the
        // decoder's textures. Once a newer FFmpeg maps them, this fails: measure map against copy (T-125) before using it.
        Assert.Equal(ErrorNotImplemented, HardwareDecodingTestEnvironment.InspectFirstKeyFrame(D3D11VA, &MapForReading));
        Assert.False(HardwareDecoderSelection.MapsFrames(D3D11VA));
    }

    [HardwareDecodingFact]
    public async Task D3D11VAFramesLookLikeSoftwareFramesInTheMiniPreviewSize()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "D3D11VA is Windows only.");

        // Scaled down from the copied NV12 like the software picture (the full size is in HardwareDecodingDeviceTests).
        (FFmpegCameraFeedFactory hardware, _) = CreateFactory(CameraHardwareDecoding.Automatic);
        (FFmpegCameraFeedFactory software, _) = CreateFactory(CameraHardwareDecoding.Off);
        await using FFmpegCameraFeed hardwareFeed = (FFmpegCameraFeed)hardware.Create(TestFixtures.Buddy3D);
        await using FFmpegCameraFeed softwareFeed = (FFmpegCameraFeed)software.Create(TestFixtures.Buddy3D);
        IAsyncEnumerator<CameraStreamItem> fromHardware = hardwareFeed.ReadAsync(Window(), Ct).GetAsyncEnumerator(Ct);
        IAsyncEnumerator<CameraStreamItem> fromSoftware = softwareFeed.ReadAsync(Window(), Ct).GetAsyncEnumerator(Ct);
        try
        {
            for (int i = 0; i < 10; i++)
            {
                CameraFrame a = await NextDisplayFrameAsync(fromHardware);
                CameraFrame b = await NextDisplayFrameAsync(fromSoftware);
                Assert.Equal((b.Width, b.Height), (a.Width, a.Height));
                Assert.InRange(MeanAbsoluteDifference(a, b), 0, 2.0);
            }

            Assert.Equal(10, hardwareFeed.TransferredFrames);
        }
        finally
        {
            await fromHardware.DisposeAsync();
            await fromSoftware.DisposeAsync();
        }
    }

    private static unsafe int MapForReading(AVFrame* frame)
    {
        AVFrame* mapped = ffmpeg.av_frame_alloc();
        try
        {
            return frame->format == (int)AVPixelFormat.AV_PIX_FMT_D3D11
                ? ffmpeg.av_hwframe_map(mapped, frame, (int)AvHwframeMap.AV_HWFRAME_MAP_READ)
                : HardwareDecodingTestEnvironment.NoFrame;
        }
        finally
        {
            ffmpeg.av_frame_free(&mapped);
        }
    }

    private static async Task<CameraFrame> NextDisplayFrameAsync(IAsyncEnumerator<CameraStreamItem> items)
    {
        while (await items.MoveNextAsync())
        {
            if (items.Current.Kind == CameraStreamItemKind.Frame)
            {
                return items.Current.DisplayFrame;
            }
        }

        throw new InvalidOperationException("The stream ended.");
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
}
