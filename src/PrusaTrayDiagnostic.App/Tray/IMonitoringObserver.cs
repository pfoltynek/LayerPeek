using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Tray;

/// <summary>Receives what <see cref="MonitoringSession"/> knows beyond snapshots: which printers run and whether monitoring failed.</summary>
internal interface IMonitoringObserver
{
    /// <summary>Starts showing <paramref name="printers"/> (configuration order); clears a previous failure. Thread-safe.</summary>
    void SetPrinters(IReadOnlyList<PrinterDefinition> printers);

    /// <summary>Monitoring stopped unexpectedly; shown until the next <see cref="SetPrinters"/>. Thread-safe.</summary>
    void ReportMonitoringFailed();
}
