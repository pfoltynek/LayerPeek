using System.Net;

namespace PrusaTrayDiagnostic.PrusaLink.WebProxy;

/// <summary>
/// Streams the browser's request body to the printer block by block, without buffering it (G-code uploads of hundreds
/// of MB). Can be sent once only. Does not dispose the source: it belongs to the server.
/// </summary>
internal sealed class RequestBodyContent : HttpContent
{
    private const int BufferSize = 64 * 1024;

    private readonly Stream _source;
    private readonly long? _length;
    private int _used;

    public RequestBodyContent(Stream source, long? length)
    {
        _source = source;
        _length = length;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _used, 1) != 0)
        {
            throw new InvalidOperationException("A streamed request body cannot be sent twice.");
        }

        return _source.CopyToAsync(stream, BufferSize, cancellationToken);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _length ?? 0;
        return _length.HasValue;
    }
}
