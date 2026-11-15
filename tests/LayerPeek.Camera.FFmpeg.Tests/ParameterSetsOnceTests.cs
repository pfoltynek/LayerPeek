using FFmpeg.AutoGen.Abstractions;

using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras;
using LayerPeek.Simulator.Camera;

using static LayerPeek.Camera.FFmpeg.Tests.HardwareDecodingFallbackTests;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-207: a camera that sends SPS and PPS only at the start of the connection and not in the session description (the
/// simulator plays the fixture with the parameter sets of its first frame only; its SDP never has
/// <c>sprop-parameter-sets</c>). A codec context opened later at a key frame - a switch between hardware and software
/// decoding, the return to software after a hardware failure, the key frames of detection after the hardware session
/// (T-203) - decodes on with the parameter sets the decoder remembered from the packets.
/// </summary>
public sealed class ParameterSetsOnceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The fixture with SPS and PPS only in its first frame.</summary>
    private static readonly H264Clip Clip = ParameterSetsOnce();

    /// <summary>Index of the fixture's second key frame, where a switch takes effect.</summary>
    private static readonly int SecondKeyFrame = FindSecondKeyFrame();

    private static H264Clip ParameterSetsOnce()
    {
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

        H264Clip clip = H264Clip.Parse(annexB.ToArray(), repeatParameterSets: false);
        Assert.Equal(TestFixtures.Buddy3DFrameCount, clip.Frames.Count);
        return clip;
    }

    private static int FindSecondKeyFrame()
    {
        int index = -1;
        for (int f = 1; f < Clip.Frames.Count && index < 0; f++)
        {
            index = Clip.Frames[f].Any(static n => (n[0] & 0x1F) == 5) ? f : -1;
        }

        // Without parameter sets: the decoder that the switch opens there has only what it remembered.
        Assert.All(Clip.Frames[index], static n => Assert.Equal(5, n[0] & 0x1F));
        return index;
    }

    private static RtspCamera StartCamera() => new(new RtspCameraOptions { Clip = Clip, Loop = false, RealTime = false });

    [FFmpegFact]
    public async Task ADecoderOpenedAtALaterKeyFrameDecodesWithTheRememberedParameterSets()
    {
        // On every OS: a device the build lacks (VA-API) leaves the first context in software; the changed setting opens
        // a new context at the next key frame, again in software because the device is still missing.
        await using RtspCamera camera = StartCamera();
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic, AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI);
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(camera.StreamUri);
        int frames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(Window(), Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame && item.DisplayFrame.Width > 0 && ++frames == 5)
            {
                factory.HardwareDecoding = CameraHardwareDecoding.Off;
                factory.HardwareDecoding = CameraHardwareDecoding.Automatic;
            }
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        // Two contexts: the first one and the one of the changed setting.
        Assert.Equal(2, log.At(LogLevel.Warning).Count);
    }

    [HardwareDecodingFact]
    public async Task SwitchingFromHardwareToSoftwareKeepsThePictureGoing()
    {
        await using RtspCamera camera = StartCamera();
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Automatic);
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(camera.StreamUri);

        int frames = await ReadSwitchingAsync(feed, () => factory.HardwareDecoding = CameraHardwareDecoding.Off);

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.Equal(
            ["Camera video decoding: videotoolbox.", "Camera video decoding: software (turned off in the settings)."],
            log.At(LogLevel.Information));
    }

    [HardwareDecodingFact]
    public async Task SwitchingFromSoftwareToHardwareKeepsThePictureGoing()
    {
        await using RtspCamera camera = StartCamera();
        (FFmpegCameraFeedFactory factory, ListLogger log) = CreateFactory(CameraHardwareDecoding.Off);
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(camera.StreamUri);

        int frames = await ReadSwitchingAsync(feed, () => factory.HardwareDecoding = CameraHardwareDecoding.Automatic);

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.Equal(TestFixtures.Buddy3DFrameCount - SecondKeyFrame, feed.MappedFrames);
        Assert.Equal(
            ["Camera video decoding: software (turned off in the settings).", "Camera video decoding: videotoolbox."],
            log.At(LogLevel.Information));
        Assert.Empty(log.At(LogLevel.Warning));
    }

    [HardwareDecodingFact]
    public async Task AfterAHardwareFailureSoftwareTakesOverAtTheNextKeyFrame()
    {
        await using RtspCamera camera = StartCamera();
        (FFmpegCameraFeedFactory factory, _) = CreateFactory(CameraHardwareDecoding.Automatic);
        factory.Hardware.Faults = new HardwareDecodingFaults { FailDecodingAfterFrames = 3 };
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(camera.StreamUri);

        int frames = await ReadDisplayFramesAsync(feed, Window());

        // Three from the hardware, the rest from software from the next key frame on.
        Assert.Equal(3 + TestFixtures.Buddy3DFrameCount - SecondKeyFrame, frames);
    }

    [HardwareDecodingFact]
    public async Task DetectionDecodesTheNextKeyFrameAfterTheHardwareSessionEnded()
    {
        await using RtspCamera camera = StartCamera();
        (FFmpegCameraFeedFactory factory, _) = CreateFactory(CameraHardwareDecoding.Automatic);
        await using FFmpegCameraFeed feed = (FFmpegCameraFeed)factory.Create(camera.StreamUri);
        CameraStreamDemand demand = Window();
        int windowFrames = 0;
        int keyFrames = 0;

        await foreach (CameraStreamItem item in feed.ReadAsync(demand, Ct))
        {
            if (item.Kind != CameraStreamItemKind.Frame)
            {
                continue;
            }

            if (demand.Frames && ++windowFrames == 5)
            {
                // The window closed (the hardware session ends); detection wants the next key frame, decoded on its own
                // by a new software context.
                Assert.True(feed.HoldsHardwareDevice);
                demand.Frames = false;
                demand.RequestKeyFrame(long.MinValue);
            }
            else if (!demand.Frames)
            {
                keyFrames++;
                Assert.False(feed.HoldsHardwareDevice);
            }
        }

        Assert.Equal(1, keyFrames);
    }

    /// <summary>Reads the camera window's frames to the end; <paramref name="change"/> is applied after the fifth.</summary>
    private static async Task<int> ReadSwitchingAsync(FFmpegCameraFeed feed, Action change)
    {
        int frames = 0;
        await foreach (CameraStreamItem item in feed.ReadAsync(Window(), Ct))
        {
            if (item.Kind == CameraStreamItemKind.Frame && item.DisplayFrame.Width > 0 && ++frames == 5)
            {
                change();
            }
        }

        return frames;
    }
}
