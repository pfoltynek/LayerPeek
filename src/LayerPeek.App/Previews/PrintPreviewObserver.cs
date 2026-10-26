using LayerPeek.App.Tray;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Previews;

/// <summary>
/// Keeps the in-memory <see cref="PrintPreviewCache"/> (T-086) in step with monitoring (T-087): every published
/// snapshot goes to <see cref="PrintPreviewCache.Observe"/> in the order the monitor raises them (the preview of a
/// finished job goes as soon as the next job starts, and an older job is never fetched again), and the printers of
/// every saved settings go to <see cref="PrintPreviewCache.RetainOnly"/> (a removed printer or a changed address drops
/// its previews). The cache itself is disposed with the app's services.
/// </summary>
internal sealed class PrintPreviewObserver : IMonitoringObserver, IDisposable
{
    private readonly IPrinterMonitor _monitor;
    private readonly PrintPreviewCache _cache;
    private int _disposed;

    public PrintPreviewObserver(IPrinterMonitor monitor, PrintPreviewCache cache)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(cache);
        _monitor = monitor;
        _cache = cache;
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cache.RetainOnly(settings.Printers);
    }

    /// <summary>The previews are kept: monitoring may come back with the same jobs.</summary>
    public void ReportMonitoringFailed()
    {
    }

    public void UpdateSettings(AppSettings settings) => ApplySettings(settings);

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
            _cache.Observe(snapshot);
        }
    }
}
