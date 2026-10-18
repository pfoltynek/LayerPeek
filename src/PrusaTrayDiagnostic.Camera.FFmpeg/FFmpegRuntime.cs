using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLinked;

using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Camera.FFmpeg;

/// <summary>
/// Binds and loads the native FFmpeg libraries once per process. They are resolved by the operating system loader
/// next to the application (<c>[DllImport]</c>); build/FFmpeg.targets puts them there.
/// </summary>
internal static class FFmpegRuntime
{
    private static readonly Lazy<LoadResult> Loaded = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Versions and license of the loaded libraries, e.g. for diagnostics and the license notice.</summary>
    /// <exception cref="CameraFeedException">FFmpeg cannot be loaded.</exception>
    public static string Version
    {
        get
        {
            EnsureLoaded();
            return Loaded.Value.Version!;
        }
    }

    /// <exception cref="CameraFeedException">FFmpeg cannot be loaded.</exception>
    public static void EnsureLoaded()
    {
        LoadResult result = Loaded.Value;
        if (result.Failure is { } failure)
        {
            throw new CameraFeedException("The video decoder (FFmpeg) is not available.", failure);
        }
    }

    private static LoadResult Load()
    {
        try
        {
            DynamicallyLinkedBindings.Initialize();
            // FFmpeg writes diagnostics to stderr by default, and some of them contain the stream address. Failures
            // reach the application through return codes instead (see FFmpegDecoder).
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_QUIET);
            // Each call loads one library (and fails if a dependency such as swresample is missing).
            string version = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"avformat {Format(ffmpeg.avformat_version())}, avcodec {Format(ffmpeg.avcodec_version())}, avutil {Format(ffmpeg.avutil_version())}, swscale {Format(ffmpeg.swscale_version())} ({ffmpeg.avcodec_license()})");
            return new LoadResult(version, null);
        }
        // Typically DllNotFoundException, EntryPointNotFoundException or BadImageFormatException; anything else from
        // loading native code (e.g. a crash in an initializer surfacing as an exception) is reported the same way.
        catch (Exception ex)
        {
            return new LoadResult(null, ex);
        }
    }

    private static string Format(uint version) => $"{version >> 16}.{(version >> 8) & 0xFF}.{version & 0xFF}";

    private sealed record LoadResult(string? Version, Exception? Failure);
}
