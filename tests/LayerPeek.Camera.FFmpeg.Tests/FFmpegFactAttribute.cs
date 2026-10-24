using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// Whether tests that need the native FFmpeg libraries can run (T-014 decision): always on Windows x64, where the build
/// provides them, so that a missing library fails the tests there; elsewhere only if FFmpeg can be loaded (macOS
/// libraries come with T-026), otherwise the tests are skipped.
/// </summary>
public static class FFmpegTestEnvironment
{
    public const string SkipReason =
        "The native FFmpeg libraries are not available on this platform; the build provides them for win-x64 only (macOS: T-026).";

    public static bool CanRun { get; } =
        (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64) || IsLoadable();

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
