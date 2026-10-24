using LayerPeek.App.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Cooldown;

/// <summary>
/// Hands the printers (with their cool-down thresholds) of every monitoring run and every settings update to the
/// <see cref="CooldownMonitor"/> (T-079). The cool-down widget (T-088) and notification (T-080) read the monitor.
/// </summary>
internal sealed class CooldownObserver : IMonitoringObserver
{
    private readonly CooldownMonitor _monitor;

    public CooldownObserver(CooldownMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        _monitor = monitor;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _monitor.Configure(settings.Printers);
    }

    /// <summary>Same printers, e.g. only a threshold changed: the monitor keeps their state and applies it right away.</summary>
    public void UpdateSettings(AppSettings settings) => ApplySettings(settings);

    /// <summary>The tray already shows the failure; the cool-down states stay as they were.</summary>
    public void ReportMonitoringFailed()
    {
    }
}
