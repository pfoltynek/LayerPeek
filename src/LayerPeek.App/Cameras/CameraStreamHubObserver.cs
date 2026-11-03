using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Cameras;

/// <summary>
/// Hands the printers of every monitoring run and every settings update to the <see cref="CameraStreamHub"/> (T-143): a
/// changed camera address restarts the shared connection, a camera switched off or a removed printer ends its leases.
/// </summary>
internal sealed class CameraStreamHubObserver : IMonitoringObserver
{
    private readonly CameraStreamHub _hub;

    public CameraStreamHubObserver(CameraStreamHub hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _hub.Configure(settings.Printers);
    }

    /// <summary>Same printers with new definitions: only a changed camera acts.</summary>
    public void UpdateSettings(AppSettings settings) => ApplySettings(settings);

    /// <summary>The cameras do not depend on the printers' connection; open streams go on.</summary>
    public void ReportMonitoringFailed()
    {
    }
}
