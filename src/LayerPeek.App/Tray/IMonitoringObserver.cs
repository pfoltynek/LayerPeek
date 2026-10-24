using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tray;

/// <summary>Receives what <see cref="MonitoringSession"/> knows beyond snapshots: the settings of the run and whether monitoring failed.</summary>
internal interface IMonitoringObserver
{
    /// <summary>
    /// A monitoring run with <paramref name="settings"/> (printers in configuration order, notification settings...) starts;
    /// clears a previous failure. Thread-safe.
    /// </summary>
    void ApplySettings(AppSettings settings);

    /// <summary>Monitoring stopped unexpectedly; shown until the next <see cref="ApplySettings"/>. Thread-safe.</summary>
    void ReportMonitoringFailed();

    /// <summary>
    /// Saved settings that keep the printers (id, name, address, connection type) and the polling interval: the running
    /// monitoring continues and only other settings (notifications) change. Thread-safe. Default: nothing to do.
    /// </summary>
    void UpdateSettings(AppSettings settings)
    {
    }
}
