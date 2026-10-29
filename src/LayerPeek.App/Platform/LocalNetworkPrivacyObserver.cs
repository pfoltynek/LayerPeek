using LayerPeek.App.Tray;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.PrusaLink;

namespace LayerPeek.App.Platform;

/// <summary>
/// T-097: tells <see cref="ILocalNetworkPrivacy"/> about every PrusaLink printer whose connection fails with "no route to
/// host", so that the log says where to allow the local network access also while no window shows the printer.
/// </summary>
internal sealed class LocalNetworkPrivacyObserver : IMonitoringObserver, IDisposable
{
    private readonly IPrinterMonitor _monitor;
    private readonly ILocalNetworkPrivacy _privacy;
    private volatile Dictionary<string, string> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private int _disposed;

    public LocalNetworkPrivacyObserver(IPrinterMonitor monitor, ILocalNetworkPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(privacy);
        _monitor = monitor;
        _privacy = privacy;
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
    }

    /// <summary>The host of a PrusaLink printer's address, as the client connects to it; <see langword="null"/> for others.</summary>
    public static string? HostOf(PrinterDefinition? printer) =>
        printer is { ConnectionType: ConnectionType.PrusaLink } && PrusaLinkAddress.TryCreateBaseUri(printer.Address, out Uri? uri) ? uri.Host : null;

    /// <summary>The subject of the log line: the printer id only, never its address.</summary>
    public static string PrinterSubject(string printerId) => $"printer '{printerId}'";

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Dictionary<string, string> hosts = new(StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in settings.Printers ?? [])
        {
            if (HostOf(printer) is { } host)
            {
                hosts[printer.Id] = host;
            }
        }

        _hosts = hosts;
    }

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
        if (snapshot is { Problem: PrinterProblem.HostUnreachable } && _hosts.TryGetValue(snapshot.PrinterId, out string? host))
        {
            _ = _privacy.MayBeDenied(host, PrinterSubject(snapshot.PrinterId));
        }
    }
}
