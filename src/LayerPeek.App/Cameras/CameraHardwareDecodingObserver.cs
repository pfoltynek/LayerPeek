using LayerPeek.App.Tray;
using LayerPeek.Camera.FFmpeg;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Cameras;

/// <summary>
/// "Hardware decoding" of the camera video (T-203): the loaded settings and every saved change set it on the FFmpeg decoder
/// without a restart; running camera windows switch at their next key frame (T-204, question 2). Nothing for another video
/// decoder (tests, a settings window without one).
/// </summary>
internal sealed class CameraHardwareDecodingObserver : IMonitoringObserver
{
    private readonly FFmpegCameraFeedFactory? _decoder;

    public CameraHardwareDecodingObserver(ICameraFeedFactory feeds)
    {
        ArgumentNullException.ThrowIfNull(feeds);
        _decoder = feeds as FFmpegCameraFeedFactory;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _decoder?.HardwareDecoding = settings.CameraHardwareDecoding;
    }

    public void UpdateSettings(AppSettings settings) => ApplySettings(settings);

    /// <summary>The decoder does not depend on the printers' connection.</summary>
    public void ReportMonitoringFailed()
    {
    }
}
