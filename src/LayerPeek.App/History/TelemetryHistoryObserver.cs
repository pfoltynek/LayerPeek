using LayerPeek.App.Tray;
using LayerPeek.Core.History;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.History;

/// <summary>
/// Feeds the <see cref="TelemetryHistory"/> while the app runs: records every published snapshot and hands it the
/// printers and polling interval of every monitoring run.
/// </summary>
internal sealed class TelemetryHistoryObserver : IMonitoringObserver, IDisposable
{
    private readonly IPrinterMonitor _monitor;
    private readonly TelemetryHistory _history;
    private int _disposed;

    public TelemetryHistoryObserver(IPrinterMonitor monitor, TelemetryHistory history)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(history);
        _monitor = monitor;
        _history = history;
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _history.Configure(settings.Printers, settings.PollingInterval);
    }

    /// <summary>The history is kept; the missing samples show as a gap.</summary>
    public void ReportMonitoringFailed()
    {
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        }
    }

    private void OnSnapshotUpdated(object? sender, PrinterSnapshot snapshot)
    {
        if (snapshot is not null)
        {
            _history.Record(snapshot);
        }
    }
}
