using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Previews;

/// <summary>
/// Downloads one G-code preview from a data source (e.g. PrusaLink). No caching: <see cref="PrintPreviewCache"/>
/// decides when to fetch. Implementations are thread-safe.
/// </summary>
public interface IPrintPreviewFetcher
{
    /// <summary>
    /// Fetches the <paramref name="size"/> preview behind <paramref name="reference"/>. With <paramref name="etag"/> (from an
    /// earlier <see cref="PrintPreviewFetchResult.ETag"/> of the same reference) the source may answer
    /// <see cref="PrintPreviewFetchStatus.NotModified"/>. Every failure is a result, never an exception; references the
    /// fetcher does not understand are <see cref="PrintPreviewFetchStatus.NotAvailable"/>.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<PrintPreviewFetchResult> FetchAsync(
        PrinterDefinition printer,
        PrintPreviewReference reference,
        PrintPreviewSize size,
        string? etag,
        CancellationToken cancellationToken);
}

/// <summary>Outcome of <see cref="IPrintPreviewFetcher.FetchAsync"/>.</summary>
public enum PrintPreviewFetchStatus
{
    /// <summary>Downloaded; <see cref="PrintPreviewFetchResult.Preview"/> is set.</summary>
    Downloaded = 0,

    /// <summary>The preview did not change since the given ETag; keep the earlier one.</summary>
    NotModified,

    /// <summary>
    /// There is no usable preview and asking again will not help: the file has none (HTTP 404), the answer is not a PNG
    /// or too large, or the reference / printer is not the fetcher's.
    /// </summary>
    NotAvailable,

    /// <summary>A transient failure (offline, timeout, rejected password...); a later attempt may succeed.</summary>
    Failed,
}

/// <param name="Status">What happened.</param>
/// <param name="Preview">The preview when <paramref name="Status"/> is <see cref="PrintPreviewFetchStatus.Downloaded"/>.</param>
/// <param name="ETag">Validator of the downloaded preview for a later conditional fetch; null when the source sent none.</param>
public readonly record struct PrintPreviewFetchResult(PrintPreviewFetchStatus Status, PrintPreview? Preview = null, string? ETag = null)
{
    public static PrintPreviewFetchResult NotAvailable => new(PrintPreviewFetchStatus.NotAvailable);

    public static PrintPreviewFetchResult Failed => new(PrintPreviewFetchStatus.Failed);

    public static PrintPreviewFetchResult NotModified(string? etag) => new(PrintPreviewFetchStatus.NotModified, ETag: etag);

    public static PrintPreviewFetchResult Downloaded(PrintPreview preview, string? etag)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new(PrintPreviewFetchStatus.Downloaded, preview, etag);
    }
}
