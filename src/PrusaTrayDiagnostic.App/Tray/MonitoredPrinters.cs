using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Tray;

/// <param name="Printers">Printers of the current monitoring run in configuration order; <see langword="null"/> before the first run.</param>
/// <param name="MonitoringFailed">Monitoring stopped unexpectedly (until the next run).</param>
internal sealed record MonitoredPrintersState(IReadOnlyList<PrinterDefinition>? Printers, bool MonitoringFailed)
{
    public static MonitoredPrintersState Starting { get; } = new(null, MonitoringFailed: false);
}

/// <summary>
/// The printers and the health of the current monitoring run, for windows opened later (diagnostics) that need the
/// definitions (names, addresses) and not only the snapshots of <see cref="Core.Monitoring.IPrinterMonitor"/>.
/// </summary>
internal sealed class MonitoredPrinters : IMonitoringObserver
{
    private readonly Lock _gate = new();

    // Written under _gate (so a failure report never overwrites newer settings), read without it.
    private MonitoredPrintersState _current = MonitoredPrintersState.Starting;

    /// <summary>
    /// <see cref="Current"/> changed. Raised on the thread of the monitoring session (a background thread); handlers
    /// must be fast and marshal to the UI thread themselves.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Immutable; read it again after <see cref="Changed"/>.</summary>
    public MonitoredPrintersState Current => Volatile.Read(ref _current);

    void IMonitoringObserver.ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // A copy: the state must not change under a reader if the caller reuses its list.
        Publish(new MonitoredPrintersState([.. settings.Printers], MonitoringFailed: false));
    }

    public void ReportMonitoringFailed()
    {
        lock (_gate)
        {
            Volatile.Write(ref _current, _current with { MonitoringFailed = true });
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Publish(MonitoredPrintersState state)
    {
        lock (_gate)
        {
            Volatile.Write(ref _current, state);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
