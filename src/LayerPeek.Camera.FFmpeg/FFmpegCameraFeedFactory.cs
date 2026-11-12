using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg;

/// <summary>
/// Camera streams decoded by FFmpeg (native libraries next to the application, see build/FFmpeg.targets).
/// </summary>
/// <remarks>
/// Supported addresses:
/// <list type="bullet">
/// <item><c>rtsp://</c> – camera stream over RTSP with TCP transport (the camera settings only allow this scheme).</item>
/// <item><c>file://</c> – a local file: raw H.264 (tests and diagnostics) or H.264 in MP4 (the app's recordings and
/// timelapses, T-165: the JPEG export); played as fast as it decodes, without real-time pacing. Not offered in the
/// camera settings.</item>
/// </list>
/// Frames are 32-bit BGRA (see <see cref="CameraFrame"/>). A stream that stays silent fails after an I/O timeout of
/// 20 s at the latest; callers such as <see cref="ReconnectingCameraFeed"/> normally cancel earlier.
/// <para>
/// T-203: the camera window's video is decoded by the graphics hardware as <see cref="HardwareDecoding"/> says (on macOS
/// VideoToolbox, on Windows software for now); key frames of detection, snapshots and the camera test always in software.
/// </para>
/// </remarks>
public sealed class FFmpegCameraFeedFactory : ICameraFeedFactory
{
    /// <summary>Longer than the default timeouts of <see cref="CameraFeedOptions"/>, so that those decide first.</summary>
    internal static readonly TimeSpan DefaultIOTimeout = TimeSpan.FromSeconds(20);

    private readonly TimeSpan _ioTimeout;
    private readonly HardwareDecodingPolicy _hardware;

    public FFmpegCameraFeedFactory()
        : this(DefaultIOTimeout)
    {
    }

    /// <param name="logger">The log of hardware decoding (T-203): which path full decoding takes and fallbacks to software.</param>
    public FFmpegCameraFeedFactory(ILogger<FFmpegCameraFeedFactory> logger)
        : this(DefaultIOTimeout, logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
    }

    /// <param name="ioTimeout">Socket I/O timeout of network streams.</param>
    /// <param name="logger">See <see cref="FFmpegCameraFeedFactory(ILogger{FFmpegCameraFeedFactory})"/>; none by default.</param>
    internal FFmpegCameraFeedFactory(TimeSpan ioTimeout, ILogger? logger = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ioTimeout, TimeSpan.FromMilliseconds(1));
        _ioTimeout = ioTimeout;
        _hardware = new HardwareDecodingPolicy(DefaultHardwareDecoding, logger);
    }

    /// <summary>
    /// The initial <see cref="HardwareDecoding"/> of new factories (<see cref="CameraHardwareDecoding.Automatic"/>, the
    /// default of the setting); the test suite runs once more with <see cref="CameraHardwareDecoding.Off"/> through it.
    /// </summary>
    internal static CameraHardwareDecoding DefaultHardwareDecoding { get; set; } = CameraHardwareDecoding.Automatic;

    /// <summary>
    /// "Hardware decoding" (T-203, <see cref="Core.Settings.AppSettings.CameraHardwareDecoding"/>), set by the app after the
    /// settings are loaded and after every save. Read without locks by the decoders when full decoding starts and at each of
    /// its key frames: a change applies to running camera windows from their next key frame on, without reconnecting.
    /// A change also forgets earlier hardware failures. Thread-safe.
    /// </summary>
    public CameraHardwareDecoding HardwareDecoding
    {
        get => _hardware.Mode;
        set => _hardware.Mode = value;
    }

    /// <summary>Hardware decoding of the feeds of this factory (for tests).</summary>
    internal HardwareDecodingPolicy Hardware => _hardware;

#if DEBUG
    /// <summary>
    /// Debug builds only, the test mode's switch <c>LAYERPEEK_TEST_HW_DECODER</c> (T-203, for the measurements of T-205 and
    /// T-125): <c>off</c>, <c>videotoolbox</c> or <c>d3d11va</c> for every camera window whatever the setting and the OS
    /// rule say; any other value is logged and means software.
    /// </summary>
    public void UseTestHardwareDecoder(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        bool known = HardwareDecoderSelection.TryParseTestOverride(value, out global::FFmpeg.AutoGen.Abstractions.AVHWDeviceType device);
        HardwareDecodingLog.TestOverride(_hardware.Logger, value, known, device);
        _hardware.SetOverride(device);
    }
#endif

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
        string camera;
        if (string.Equals(streamUri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase))
        {
            // T-203: the hardware failures of a camera are remembered by its address without credentials.
            camera = streamUri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
            // Without the fragment: it is not part of the request and FFmpeg would send it to the camera.
            url = streamUri.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.Fragment, UriFormat.UriEscaped);
            isNetwork = true;
        }
        else if (streamUri.IsFile)
        {
            // Explicit protocol: FFmpeg must not interpret parts of the path (e.g. a drive letter) as a protocol.
            url = "file:" + streamUri.LocalPath;
            isNetwork = false;
            camera = url;
        }
        else
        {
            // The scheme is not secret; the rest of the address is not included.
            throw new CameraFeedException($"Camera addresses of type '{streamUri.Scheme}' are not supported.");
        }

        FFmpegRuntime.EnsureLoaded();
        return new FFmpegCameraFeed(url, isNetwork, _ioTimeout, _hardware, _hardware.HistoryOf(camera));
    }
}
