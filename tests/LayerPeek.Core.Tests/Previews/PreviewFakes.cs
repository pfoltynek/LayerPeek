using System.Collections.Concurrent;

using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Shared;

namespace LayerPeek.Core.Tests.Previews;

/// <summary>A reference type of an imaginary client.</summary>
internal sealed record TestPreviewReference(string File) : PrintPreviewReference;

internal sealed record FetchCall(PrinterDefinition Printer, PrintPreviewReference Reference, PrintPreviewSize Size, string? ETag, CancellationToken CancellationToken);

/// <summary>Records every fetch; by default answers with a fresh 16 × 16 PNG and ETag "\"1\"".</summary>
internal sealed class FakePreviewFetcher : IPrintPreviewFetcher
{
    public ConcurrentQueue<FetchCall> Calls { get; } = new();

    public Func<FetchCall, Task<PrintPreviewFetchResult>> Handler { get; set; } =
        static _ => Task.FromResult(PrintPreviewFetchResult.Downloaded(NewPreview(), "\"1\""));

    public int Count => Calls.Count;

    public static PrintPreview NewPreview(int width = 16, int height = 16) => PrintPreview.FromPng(TestPng.Create(width, height))!;

    public Task<PrintPreviewFetchResult> FetchAsync(PrinterDefinition printer, PrintPreviewReference reference, PrintPreviewSize size, string? etag, CancellationToken cancellationToken)
    {
        FetchCall call = new(printer, reference, size, etag, cancellationToken);
        Calls.Enqueue(call);
        return Handler(call);
    }
}
