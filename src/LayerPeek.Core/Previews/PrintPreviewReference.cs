namespace LayerPeek.Core.Previews;

/// <summary>
/// Where the data source can find the G-code preview of a job (<see cref="Printers.PrintJob.PreviewReference"/>).
/// Opaque: only the <see cref="IPrintPreviewFetcher"/> of the client that created it understands it (each client
/// derives its own type). Equality is by value, so the same file of the same printer yields equal references.
/// </summary>
public abstract record PrintPreviewReference;
