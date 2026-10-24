namespace LayerPeek.Core.Printers;

/// <summary>
/// Reads the state of one printer from a concrete data source.
/// </summary>
public interface IPrinterClient
{
    /// <summary>
    /// Returns the current state of the printer. Expected failures (unreachable printer, rejected credentials,
    /// unexpected response) are reported through <see cref="PrinterSnapshot.State"/>, not as exceptions.
    /// Implementations must honour <paramref name="cancellationToken"/> promptly; callers never issue
    /// overlapping calls on one instance.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<PrinterSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
