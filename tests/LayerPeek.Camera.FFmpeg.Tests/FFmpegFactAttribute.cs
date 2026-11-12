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
/// Elsewhere they run only if FFmpeg can be loaded, otherwise they are skipped.
/// </summary>
public static class FFmpegTestEnvironment
{
    public const string SkipReason =
        "The native FFmpeg libraries are not available: the build provides them for win-x64 and osx-arm64 only, and not with LayerPeekFFmpegEnabled=false.";

    public static bool CanRun { get; } =
        (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        || (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && IsProvidedOnMacOS())
        || IsLoadable();

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
/// Whether the hardware decoding tests of T-203 can run: FFmpeg is available, the build has VideoToolbox for H.264 and this
/// Mac can create a VideoToolbox device. Windows (D3D11VA) has its own tests in T-206.
/// </summary>
public static class HardwareDecodingTestEnvironment
{
    public const string SkipReason =
        "Hardware decoding (VideoToolbox) is not available: it needs macOS with the FFmpeg build of T-203 and a VideoToolbox device.";

    public static bool CanRun { get; } = FFmpegTestEnvironment.CanRun && OperatingSystem.IsMacOS() && CanCreateVideoToolbox();

    private static unsafe bool CanCreateVideoToolbox()
    {
        try
        {
            if (!FFmpegRuntime.HasHardwareDecoder(AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX))
            {
                return false;
            }

            AVBufferRef* device = null;
            int created = ffmpeg.av_hwdevice_ctx_create(&device, AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX, null, null, 0);
            ffmpeg.av_buffer_unref(&device);
            return created >= 0;
        }
        catch (CameraFeedException)
        {
            return false;
        }
    }
}

/// <summary>A fact that needs hardware decoding (VideoToolbox); skipped where it is not available (see <see cref="HardwareDecodingTestEnvironment"/>).</summary>
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
