using System.Reflection;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLinked;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg;

/// <summary>
/// Binds and loads the native FFmpeg libraries once per process. They are resolved next to the application
/// (<c>[DllImport]</c>); build/FFmpeg.targets puts them there. Windows: by the operating system loader under the
/// binding's names (<c>avformat-63.dll</c>). macOS (T-026): the libraries carry their install names
/// (<c>libavformat.63.dylib</c>), which a <see cref="NativeLibrary.SetDllImportResolver"/> maps to, loading only from the
/// application folder (never from Homebrew or other system locations).
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
            if (OperatingSystem.IsMacOS())
            {
                // Once per process (Lazy): a second resolver for the same assembly would throw.
                NativeLibrary.SetDllImportResolver(typeof(DynamicallyLinkedBindings).Assembly, ResolveFromApplicationFolder);
                NativeLibrary.SetDllImportResolver(typeof(FFmpegRuntime).Assembly, ResolveFromApplicationFolder);
            }

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

    /// <summary>
    /// The macOS file name of an FFmpeg library named as in the binding and in <see cref="NativeMethods"/>
    /// (<c>avformat-63</c> becomes <c>libavformat.63.dylib</c>, the name of an FFmpeg build); <see langword="null"/> for
    /// any other library name.
    /// </summary>
    internal static string? MacOSFileName(string libraryName)
    {
        int dash = libraryName.LastIndexOf('-');
        if (dash <= 0 || dash == libraryName.Length - 1)
        {
            return null;
        }

        ReadOnlySpan<char> name = libraryName.AsSpan(0, dash);
        ReadOnlySpan<char> major = libraryName.AsSpan(dash + 1);
        bool isFFmpeg = name is "avformat" or "avcodec" or "avutil" or "swscale" or "swresample" or "avfilter" or "avdevice" or "postproc";
        foreach (char digit in major)
        {
            isFFmpeg &= char.IsAsciiDigit(digit);
        }

        return isFFmpeg ? $"lib{name}.{major}.dylib" : null;
    }

    /// <summary>
    /// Loads an FFmpeg library only from the application folder. A missing one throws instead of falling back to the
    /// default search, which on macOS would also try <c>/usr/local/lib</c> (Homebrew).
    /// </summary>
    private static IntPtr ResolveFromApplicationFolder(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (MacOSFileName(libraryName) is not { } fileName)
        {
            return IntPtr.Zero;
        }

        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        return NativeLibrary.TryLoad(path, out IntPtr handle)
            ? handle
            : throw new DllNotFoundException($"The FFmpeg library {fileName} was not found or could not be loaded from {AppContext.BaseDirectory}.");
    }

    private static string Format(uint version) => $"{version >> 16}.{(version >> 8) & 0xFF}.{version & 0xFF}";

    private sealed record LoadResult(string? Version, Exception? Failure);
}
