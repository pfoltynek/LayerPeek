using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
