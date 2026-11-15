using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen.Abstractions;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// Whether tests that need the native FFmpeg libraries can run (T-014 decision, extended by T-026): always on Windows x64,
/// where the build provides them, so that a missing library fails the tests there. On macOS arm64 the build compiles them
/// (build/FFmpeg.targets), so they run whenever the libraries are next to the tests, and a library that is there but
/// does not load fails them; they are skipped only when the build left them out (<c>LayerPeekFFmpegEnabled=false</c>).
/// The same on Windows ARM64 (T-225): the ARM64 libraries come with a win-arm64 build (dotnet test -r win-arm64, T-227);
/// a build there without a RID has none (not supported) and skips the tests. Elsewhere they run only if FFmpeg can be
/// loaded, otherwise they are skipped.
/// </summary>
public static class FFmpegTestEnvironment
{
    public const string SkipReason =
        "The native FFmpeg libraries are not available: the build provides them for win-x64, win-arm64 (with -r win-arm64) and osx-arm64 only, and not with LayerPeekFFmpegEnabled=false.";

    public static bool CanRun { get; } =
        (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        || (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && IsProvidedOnMacOS())
        || (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && IsProvidedOnWindows())
        || IsLoadable();

    /// <summary>Whether the build put the Windows libraries next to the tests (see build/FFmpeg.targets).</summary>
    public static bool IsProvidedOnWindows() =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "avformat-63.dll"));

    /// <summary>Whether the build put the macOS libraries next to the tests (see build/FFmpeg.targets).</summary>
    public static bool IsProvidedOnMacOS() =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "libavformat.63.dylib"));

    private static bool IsLoadable()
    {
        try
        {
            _ = FFmpegCameraFeedFactory.LibraryVersion;
            return true;
        }
        catch (CameraFeedException)
        {
            return false;
        }
    }
}

/// <summary>A fact that needs FFmpeg; skipped where it is not available (see <see cref="FFmpegTestEnvironment"/>).</summary>
public sealed class FFmpegFactAttribute : FactAttribute
{
    public FFmpegFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = FFmpegTestEnvironment.SkipReason;
        SkipType = typeof(FFmpegTestEnvironment);
        SkipUnless = nameof(FFmpegTestEnvironment.CanRun);
    }
}

/// <summary>A theory that needs FFmpeg; skipped where it is not available (see <see cref="FFmpegTestEnvironment"/>).</summary>
public sealed class FFmpegTheoryAttribute : TheoryAttribute
{
    public FFmpegTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = FFmpegTestEnvironment.SkipReason;
        SkipType = typeof(FFmpegTestEnvironment);
        SkipUnless = nameof(FFmpegTestEnvironment.CanRun);
    }
}

/// <summary>
/// Whether the hardware decoding tests of T-203 / T-206 can run with the hardware decoder of this OS
/// (<see cref="Device"/>): FFmpeg is available, its build has the device for H.264 and this computer can use it. macOS:
/// a VideoToolbox device can be created. Windows: a D3D11VA device can be created on the default adapter and decodes the
/// first key frame of <see cref="TestFixtures.Buddy3D"/> into a D3D11 texture (a VM or CI runner without a GPU has only the
/// Microsoft Basic Render Driver, which creates the device but cannot decode H.264; the tests are skipped there).
/// </summary>
public static class HardwareDecodingTestEnvironment
{
    public const string SkipReason =
        "Hardware decoding is not available: it needs macOS with VideoToolbox or Windows with a graphics adapter that decodes H.264 through D3D11VA (T-203, T-206).";

    /// <summary>The hardware decoder of this OS: VideoToolbox on macOS, D3D11VA on Windows (elsewhere none).</summary>
    public static AVHWDeviceType Device { get; } =
        OperatingSystem.IsMacOS() ? AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX
        : OperatingSystem.IsWindows() ? AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA
        : AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

    /// <summary>The name of <see cref="Device"/> in the log (<c>videotoolbox</c>, <c>d3d11va</c>).</summary>
    public static string DeviceName => HardwareDecoderSelection.Name(Device);

    /// <summary>Whether the frames of <see cref="Device"/> are mapped (VideoToolbox) or copied (D3D11VA).</summary>
    public static bool MapsFrames => HardwareDecoderSelection.MapsFrames(Device);

    public static bool CanRun { get; } = FFmpegTestEnvironment.CanRun && Device != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE && CanUseDevice();

    /// <summary>What <see cref="InspectFirstKeyFrame"/> returns when the decoder gave no frame.</summary>
    public const int NoFrame = int.MinValue;

    private static unsafe bool CanUseDevice()
    {
        try
        {
            if (!FFmpegRuntime.HasHardwareDecoder(Device))
            {
                return false;
            }

            if (Device == AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA)
            {
                return InspectFirstKeyFrame(Device, &IsD3D11Frame) == 1;
            }

            AVBufferRef* device = null;
            int created = ffmpeg.av_hwdevice_ctx_create(&device, Device, null, null, 0);
            ffmpeg.av_buffer_unref(&device);
            return created >= 0;
        }
        catch (CameraFeedException)
        {
            return false;
        }
    }

    private static unsafe int IsD3D11Frame(AVFrame* frame) => frame->format == (int)AVPixelFormat.AV_PIX_FMT_D3D11 ? 1 : 0;

    /// <summary>
    /// Decodes the first key frame of <see cref="TestFixtures.Buddy3D"/> with FFmpeg's H.264 decoder on a new device of
    /// <paramref name="deviceType"/> (default adapter) and returns what <paramref name="inspect"/> says about the frame
    /// (FFmpeg hands out a software frame when the hardware cannot decode it); the error of <c>av_hwdevice_ctx_create</c>
    /// when no device can be created, <see cref="NoFrame"/> when nothing was decoded. Plain FFmpeg calls, independent of
    /// the decoder under test; frees everything it allocates.
    /// </summary>
    internal static unsafe int InspectFirstKeyFrame(AVHWDeviceType deviceType, delegate*<AVFrame*, int> inspect)
    {
        AVBufferRef* device = null;
        int created = ffmpeg.av_hwdevice_ctx_create(&device, deviceType, null, null, 0);
        if (created < 0)
        {
            return created;
        }

        AVFormatContext* format = null;
        AVCodecContext* codec = null;
        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        try
        {
            if (packet is null || frame is null
                || ffmpeg.avformat_open_input(&format, "file:" + TestFixtures.Buddy3D.LocalPath, null, null) < 0
                || ffmpeg.avformat_find_stream_info(format, null) < 0)
            {
                return NoFrame;
            }

            AVCodec* decoder = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            codec = ffmpeg.avcodec_alloc_context3(decoder);
            if (codec is null || ffmpeg.avcodec_parameters_to_context(codec, format->streams[0]->codecpar) < 0)
            {
                return NoFrame;
            }

            codec->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            codec->thread_count = 1;
            codec->hw_device_ctx = ffmpeg.av_buffer_ref(device);
            if (ffmpeg.avcodec_open2(codec, decoder, null) < 0)
            {
                return NoFrame;
            }

            for (int i = 0; i < 10 && ffmpeg.av_read_frame(format, packet) >= 0; i++)
            {
                int sent = ffmpeg.avcodec_send_packet(codec, packet);
                ffmpeg.av_packet_unref(packet);
                if (sent >= 0 && ffmpeg.avcodec_receive_frame(codec, frame) == 0)
                {
                    return inspect(frame);
                }
            }

            return NoFrame;
        }
        finally
        {
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avcodec_free_context(&codec);
            ffmpeg.avformat_close_input(&format);
            ffmpeg.av_buffer_unref(&device);
        }
    }
}

/// <summary>
/// A fact that needs the hardware decoder of this OS (VideoToolbox on macOS, D3D11VA on Windows); skipped where it is not
/// available (see <see cref="HardwareDecodingTestEnvironment"/>).
/// </summary>
public sealed class HardwareDecodingFactAttribute : FactAttribute
{
    public HardwareDecodingFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = HardwareDecodingTestEnvironment.SkipReason;
        SkipType = typeof(HardwareDecodingTestEnvironment);
        SkipUnless = nameof(HardwareDecodingTestEnvironment.CanRun);
    }
}
