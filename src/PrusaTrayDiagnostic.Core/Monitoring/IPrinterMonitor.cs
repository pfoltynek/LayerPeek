using System.Diagnostics.CodeAnalysis;

using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Monitoring;

/// <summary>
/// Periodically polls the configured printers and publishes their latest <see cref="PrinterSnapshot"/>.
/// </summary>
public interface IPrinterMonitor
{
    /// <summary>
    /// Raised after every poll of every printer, concurrently from background threads. Handlers must be
    /// thread-safe and fast (marshal to the UI thread themselves); exceptions are logged and ignored.
    /// </summary>
    event EventHandler<PrinterSnapshot>? SnapshotUpdated;

    /// <summary>
    /// Latest snapshot of each printer of the current (or, after it ended, the last) run that was polled at least once,
    /// in configuration order.
    /// </summary>
    IReadOnlyList<PrinterSnapshot> Snapshots { get; }

    bool TryGetSnapshot(string printerId, [NotNullWhen(true)] out PrinterSnapshot? snapshot);

    /// <summary>
    /// Polls <paramref name="printers"/> until <paramref name="cancellationToken"/> is cancelled, then completes
    /// successfully. To apply new settings, cancel the running call, await it and start a new one.
    /// </summary>
    /// <exception cref="ArgumentException">Printer ids are not unique (case-insensitive).</exception>
    /// <exception cref="InvalidOperationException">Another run is in progress.</exception>
    Task RunAsync(IReadOnlyList<PrinterDefinition> printers, TimeSpan pollingInterval, CancellationToken cancellationToken);
}
