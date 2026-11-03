using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Cameras;

/// <summary>
/// Hands the printers and recording settings of every monitoring run and every settings update to the
/// <see cref="CameraRecordingService"/> (T-145). Registered before the <see cref="CameraStreamHubObserver"/>: a camera that
/// is switched off or a removed printer stops its recording on the service's account before the hub ends its lease.
/// </summary>
internal sealed class CameraRecordingObserver : IMonitoringObserver
{
    private readonly CameraRecordingService _recordings;

    public CameraRecordingObserver(CameraRecordingService recordings)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        _recordings = recordings;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _recordings.Configure(settings.Printers, settings.Recording);
    }

    /// <summary>Same printers with new definitions (e.g. "record prints" switched): the service keeps their state.</summary>
    public void UpdateSettings(AppSettings settings) => ApplySettings(settings);

    /// <summary>Recordings do not depend on the printers' connection; running ones go on (an automatic one ends by its rules).</summary>
    public void ReportMonitoringFailed()
    {
    }
}
