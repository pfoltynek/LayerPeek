using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Camera.FFmpeg;

/// <summary>
/// Camera streams decoded by FFmpeg (native libraries next to the application, see build/FFmpeg.targets).
/// </summary>
/// <remarks>
/// Supported addresses:
/// <list type="bullet">
/// <item><c>rtsp://</c> – camera stream over RTSP with TCP transport (the camera settings only allow this scheme).</item>
/// <item><c>file://</c> – a local recording (e.g. raw H.264); played as fast as it decodes, without real-time pacing.
/// Meant for tests and diagnostics, not offered in the settings.</item>
/// </list>
/// Frames are 32-bit BGRA (see <see cref="CameraFrame"/>). A stream that stays silent fails after an I/O timeout of
/// 20 s at the latest; callers such as <see cref="ReconnectingCameraFeed"/> normally cancel earlier.
/// </remarks>
public sealed class FFmpegCameraFeedFactory : ICameraFeedFactory
{
    /// <summary>Longer than the default timeouts of <see cref="CameraFeedOptions"/>, so that those decide first.</summary>
    internal static readonly TimeSpan DefaultIOTimeout = TimeSpan.FromSeconds(20);

    private readonly TimeSpan _ioTimeout;

    public FFmpegCameraFeedFactory()
        : this(DefaultIOTimeout)
    {
    }

    /// <param name="ioTimeout">Socket I/O timeout of network streams.</param>
    internal FFmpegCameraFeedFactory(TimeSpan ioTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ioTimeout, TimeSpan.FromMilliseconds(1));
        _ioTimeout = ioTimeout;
    }

    /// <summary>
    /// Versions and license of the loaded FFmpeg libraries, e.g. <c>avformat 63.x.y, …, swscale 10.x.y (LGPL version 3 or later)</c>.
    /// </summary>
    /// <exception cref="CameraFeedException">FFmpeg is not available.</exception>
    public static string LibraryVersion => FFmpegRuntime.Version;

    /// <exception cref="ArgumentNullException"><paramref name="streamUri"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="streamUri"/> is not absolute.</exception>
    /// <exception cref="CameraFeedException">The scheme is not supported, or FFmpeg is not available.</exception>
    public ICameraFeed Create(Uri streamUri)
    {
        ArgumentNullException.ThrowIfNull(streamUri);
        if (!streamUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The stream address must be absolute.", nameof(streamUri));
        }

        string url;
        bool isNetwork;
        if (string.Equals(streamUri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase))
        {
            url = streamUri.AbsoluteUri;
            isNetwork = true;
        }
        else if (streamUri.IsFile)
        {
            // Explicit protocol: FFmpeg must not interpret parts of the path (e.g. a drive letter) as a protocol.
            url = "file:" + streamUri.LocalPath;
            isNetwork = false;
        }
        else
        {
            // The scheme is not secret; the rest of the address is not included.
            throw new CameraFeedException($"Camera addresses of type '{streamUri.Scheme}' are not supported.");
        }

        FFmpegRuntime.EnsureLoaded();
        return new FFmpegCameraFeed(url, isNetwork, _ioTimeout);
    }
}
