using FFmpeg.AutoGen.Abstractions;

using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-203 on every OS with FFmpeg: a hardware device that cannot be used falls back to software without an error of the
/// feed and with one warning; without a camera window (a display reader) no device is ever created; "Off" never creates
/// one. Fixture: <see cref="TestFixtures.Buddy3D"/>.
/// </summary>
public sealed class HardwareDecodingFallbackTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The device the build of this OS has for H.264 (macOS: VideoToolbox, Windows: D3D11VA, T-206).</summary>
    private static AVHWDeviceType DeviceOfThisOs => OperatingSystem.IsWindows() ? AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA : AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX;

    internal static CameraStreamDemand Window(int width = 640, int height = 360)
    {
        CameraStreamDemand demand = new(frames: true, packets: false) { DisplaySize = new CameraFrameSize(width, height) };
        demand.CancelFullResolution();
        return demand;
    }

    internal static (FFmpegCameraFeedFactory Factory, ListLogger Log) CreateFactory(CameraHardwareDecoding mode, AVHWDeviceType? device = null)
    {
        ListLogger log = new();
        FFmpegCameraFeedFactory factory = new(FFmpegCameraFeedFactory.DefaultIOTimeout, log) { HardwareDecoding = mode };
        if (device is { } forced)
        {
            factory.Hardware.SetOverride(forced);
        }

        return (factory, log);
    }

    /// <summary>Reads the feed to its end; returns the frames with a display frame.</summary>
    internal static async Task<int> ReadDisplayFramesAsync(FFmpegCameraFeed feed, CameraStreamDemand demand)
    {
        int frames = 0;
        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame && item.DisplayFrame.Width > 0)
            {
                Assert.Equal((640, 360), (item.DisplayFrame.Width, item.DisplayFrame.Height));
                frames++;
            }
        }

        return frames;
    }

    [FFmpegFact]
    public async Task ADeviceTypeTheBuildLacksDecodesInSoftwareWithOneWarning()
    {
        // VA-API (Linux) is in neither the macOS nor the Windows build.
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic, AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI);
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);

        Assert.Equal(TestFixtures.Buddy3DFrameCount, await ReadDisplayFramesAsync(feed, Window()));

        Assert.Equal(0, factory.Hardware.DevicesCreated);
        string warning = Assert.Single(log.At(LogLevel.Warning));
        Assert.Contains("Hardware video decoding (device type", warning, StringComparison.Ordinal);
        Assert.Contains("1 of 2 failures", warning, StringComparison.Ordinal);
        Assert.Contains("Camera video decoding: software (the hardware decoder is not available).", log.At(LogLevel.Information));
    }

    [FFmpegFact]
    public async Task ADeviceThatCannotBeCreatedDecodesInSoftwareWithOneWarning()
    {
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic, DeviceOfThisOs);
        factory.Hardware.Faults = new HardwareDecodingFaults { FailDeviceCreation = true };
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);

        Assert.Equal(TestFixtures.Buddy3DFrameCount, await ReadDisplayFramesAsync(feed, Window()));

        Assert.Equal(0, factory.Hardware.DevicesCreated);
        Assert.False(feed.HoldsHardwareDevice);
        string warning = Assert.Single(log.At(LogLevel.Warning));
        Assert.Contains("failed while starting", warning, StringComparison.Ordinal);
        Assert.Equal(1, feed.HardwareHistory.FailuresIn(factory.Hardware.Current.Generation));
    }

    [FFmpegFact]
    public async Task OffNeverCreatesADevice()
    {
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Off);
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D);

        Assert.Equal(TestFixtures.Buddy3DFrameCount, await ReadDisplayFramesAsync(feed, Window()));

        Assert.Equal(0, factory.Hardware.DevicesCreated);
        Assert.Empty(log.At(LogLevel.Warning));
        Assert.Equal(["Camera video decoding: software (turned off in the settings)."], log.At(LogLevel.Information));
    }

    [FFmpegFact]
    public async Task WithoutACameraWindowNoDeviceIsCreated()
    {
        // The device of this OS forced, as if the rule chose it: still nothing for detection, the snapshot or the camera test.
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic, DeviceOfThisOs);

        // The snapshot and the camera test of an unsaved address (CameraSnapshot): frames without a display size.
        CameraImage image = await CameraSnapshot.CaptureAsync(factory, TestFixtures.Buddy3D, Ct);
        Assert.Equal(TestFixtures.Buddy3DWidth, image.Width);

        // Print failure detection: key frames decoded on their own.
        await using (FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D))
        {
            CameraStreamDemand demand = new(frames: false, packets: false);
            demand.RequestKeyFrame(long.MinValue);
            int keyFrames = 0;
            await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
            {
                keyFrames += item.Kind == CameraStreamItemKind.Frame ? 1 : 0;
            }

            Assert.Equal(TestFixtures.Buddy3DKeyFrameCount, keyFrames);
        }

        // Detection with full decoding but no window (grayscale samples only), e.g. while an alarm snapshot is taken.
        await using (FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(TestFixtures.Buddy3D))
        {
            CameraStreamDemand demand = new(frames: true, packets: false);
            demand.CancelFullResolution();
            demand.RequestGrayscale(new CameraGrayscaleSize(320, 180), long.MinValue);
            int samples = 0;
            await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
            {
                samples += item.Kind == CameraStreamItemKind.Frame && item.GrayscaleFrame.Width == 320 ? 1 : 0;
            }

            Assert.Equal(TestFixtures.Buddy3DFrameCount, samples);
        }

        Assert.Equal(0, factory.Hardware.DevicesCreated);
        Assert.Empty(log.At(LogLevel.Warning));
        Assert.All(log.At(LogLevel.Information), static line => Assert.Equal("Camera video decoding: software (no camera window).", line));
    }

    [FFmpegFact]
    public void TheMacOSBuildHasVideoToolboxForH264AndStaysLgpl()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The macOS build of FFmpeg (build/macos/build-ffmpeg.sh); Windows: T-206.");

        // --enable-videotoolbox --enable-hwaccel=h264_videotoolbox (T-203), still LGPL 3.
        Assert.True(FFmpegRuntime.HasHardwareDecoder(AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX));
        Assert.False(FFmpegRuntime.HasHardwareDecoder(AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI));
        Assert.EndsWith("(LGPL version 3 or later)", FFmpegCameraFeedFactory.LibraryVersion, StringComparison.Ordinal);
    }

    [FFmpegFact]
    public void TheFailuresOfACameraAreKeptByItsAddressWithoutCredentials()
    {
        FFmpegCameraFeedFactory factory = new();

        FFmpegCameraFeed withCredentials = (FFmpegCameraFeed)factory.Create(new Uri("rtsp://user:Pw7Secret@10.0.0.5:554/live#x"));
        FFmpegCameraFeed without = (FFmpegCameraFeed)factory.Create(new Uri("rtsp://10.0.0.5:554/live"));
        FFmpegCameraFeed other = (FFmpegCameraFeed)factory.Create(new Uri("rtsp://10.0.0.6:554/live"));

        Assert.Same(withCredentials.HardwareHistory, without.HardwareHistory);
        Assert.Same(factory.Hardware.HistoryOf("rtsp://10.0.0.5:554/live"), without.HardwareHistory);
        Assert.NotSame(without.HardwareHistory, other.HardwareHistory);
    }

    [FFmpegFact]
    public void TheSettingIsKeptByTheFactory()
    {
        FFmpegCameraFeedFactory factory = new() { HardwareDecoding = CameraHardwareDecoding.Off };

        Assert.Equal(CameraHardwareDecoding.Off, factory.HardwareDecoding);
        Assert.Throws<ArgumentOutOfRangeException>(() => factory.HardwareDecoding = (CameraHardwareDecoding)5);
    }

#if DEBUG
    [FFmpegFact]
    public void TheTestSwitchOverridesTheSettingAndAnUnknownValueIsSoftware()
    {
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Off);

        factory.UseTestHardwareDecoder("videotoolbox");
        Assert.Equal(AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX, factory.Hardware.Current.Device);
        factory.UseTestHardwareDecoder("cuda");
        Assert.Equal(AVHWDeviceType.AV_HWDEVICE_TYPE_NONE, factory.Hardware.Current.Device);

        Assert.Contains("Hardware decoder test switch: videotoolbox for every camera window.", log.At(LogLevel.Information));
        Assert.Contains("'cuda'", Assert.Single(log.At(LogLevel.Warning)), StringComparison.Ordinal);
    }
#endif
}
