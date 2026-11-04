using LayerPeek.App.Tray;
using LayerPeek.Core.Detection;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Detection;

/// <summary>
/// Hands the printers and detection settings of every monitoring run and every settings update to the
/// <see cref="PrintFailureDetectionService"/> (T-153). Registered after the <see cref="Cameras.CameraStreamHubObserver"/>:
/// the hub knows a new camera address before detection takes a lease on it.
/// </summary>
internal sealed class PrintFailureDetectionObserver : IMonitoringObserver
{
    private readonly PrintFailureDetectionService _detection;

    public PrintFailureDetectionObserver(PrintFailureDetectionService detection)
    {
        ArgumentNullException.ThrowIfNull(detection);
        _detection = detection;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _detection.Configure(settings);
    }

    /// <summary>Same printers with new definitions: printers whose detection settings changed start over.</summary>
    public void UpdateSettings(AppSettings settings) => ApplySettings(settings);

    /// <summary>No more snapshots: whether a printer still prints is unknown, so nothing is watched until the next run.</summary>
    public void ReportMonitoringFailed() => _detection.ForgetSnapshots();
}
