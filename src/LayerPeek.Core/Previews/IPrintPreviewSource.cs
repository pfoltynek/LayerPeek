using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Previews;

/// <summary>
/// G-code previews of print jobs for the UI (widget "Název tisku", T-085 / T-087). Implemented by
/// <see cref="PrintPreviewCache"/>.
/// </summary>
public interface IPrintPreviewSource
{
    /// <summary>
    /// The preview of <paramref name="job"/> on <paramref name="printer"/>, or <see langword="null"/> when there is none:
    /// the job has no <see cref="PrintJob.PreviewReference"/>, the file contains no such preview, or it could not be
    /// fetched (failures are never thrown). Call it only when the preview is actually shown; fetching starts lazily.
    /// Thread-safe; does not block.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled (only this caller stops waiting).</exception>
    Task<PrintPreview?> GetAsync(PrinterDefinition printer, PrintJob job, PrintPreviewSize size, CancellationToken cancellationToken);
}
