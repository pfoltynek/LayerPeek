using System.Buffers;
using System.Net;

using Microsoft.Extensions.Logging;

using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Secrets;
using LayerPeek.PrusaLink.Api;

namespace LayerPeek.PrusaLink;

/// <summary>
/// Downloads G-code previews from Buddy firmware (<c>GET /thumb/s/…</c>, <c>/thumb/l/…</c>, the links of
/// <see cref="PrintJob.PreviewReference"/> set by <see cref="PrusaLinkClient"/>; T-086, docs/api/prusalink.md). Digest
/// login as the monitoring client, but on its own pooled connection, so that a download never delays a poll. Only
/// <c>image/png</c> answers that start like a PNG and stay within <see cref="PrusaLinkPreviewOptions.MaxBytes"/> are
/// accepted; the size is enforced while streaming. Logs carry the printer id and the reason, never the address, path,
/// password or exception text. Thread-safe.
/// </summary>
public sealed partial class PrusaLinkPreviewFetcher : IPrintPreviewFetcher
{
    public const string HttpClientName = "PrusaLink.Preview";

    private const int ReadChunk = 16 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretStore _secretStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrusaLinkPreviewFetcher> _logger;
    private readonly PrusaLinkPreviewOptions _options;

    public PrusaLinkPreviewFetcher(
        IHttpClientFactory httpClientFactory,
        ISecretStore secretStore,
        TimeProvider timeProvider,
        ILogger<PrusaLinkPreviewFetcher> logger,
        PrusaLinkPreviewOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _secretStore = secretStore;
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options ?? PrusaLinkPreviewOptions.Default;
    }

    /// <inheritdoc/>
    public async Task<PrintPreviewFetchResult> FetchAsync(
        PrinterDefinition printer,
        PrintPreviewReference reference,
        PrintPreviewSize size,
        string? etag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        if (printer.ConnectionType != ConnectionType.PrusaLink
            || reference is not PrusaLinkPreviewReference links
            || links.PathFor(size) is not { } path
            || !PrusaLinkAddress.TryCreateBaseUri(printer.Address, out Uri? baseUri))
        {
            return PrintPreviewFetchResult.NotAvailable;
        }

        if (!Uri.TryCreate(baseUri, path, out Uri? uri) || !IsBelow(uri, baseUri))
        {
            // Defence in depth: references are validated when created, but the request must never leave the printer.
            return PrintPreviewFetchResult.NotAvailable;
        }

        using CancellationTokenSource timeout = new(_options.Timeout, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            // The digest session is per download: a preview is fetched about once per job, so the extra 401 round trip
            // is cheaper than keeping authentication state per printer alive (and dropping it on settings changes).
            DigestAuthenticator authenticator = new(_secretStore, SecretKeys.ForPrinter(printer.Id), printer.UserName, _timeProvider, cacheGetAuthorizations: false);
            HttpClient httpClient = _httpClientFactory.CreateClient(HttpClientName);
            (HttpResponseMessage? response, ChallengeResult refusal) = await DigestRequests.GetAsync(
                httpClient, authenticator, uri, "image/png", etag, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (response is null)
            {
                LogLoginFailed(printer.Id, size, refusal.Reason);
                return PrintPreviewFetchResult.Failed;
            }

            using (response)
            {
                return await ReadResponseAsync(response, printer.Id, size, etag, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            LogFailed(printer.Id, size, nameof(PrusaLinkFailure.Timeout));
            return PrintPreviewFetchResult.Failed;
        }
        catch (HttpRequestException ex)
        {
            // Only the category: the exception text may contain the address.
            LogRequestFailed(printer.Id, size, ex.HttpRequestError);
            return PrintPreviewFetchResult.Failed;
        }
        catch (IOException)
        {
            LogFailed(printer.Id, size, "connection dropped");
            return PrintPreviewFetchResult.Failed;
        }
        catch (SecretStoreException)
        {
            LogFailed(printer.Id, size, nameof(PrusaLinkFailure.PasswordUnreadable));
            return PrintPreviewFetchResult.Failed;
        }
    }

    /// <summary>Same scheme, host and port as the printer, and a path below its base path (reverse-proxy prefix).</summary>
    private static bool IsBelow(Uri uri, Uri baseUri) =>
        string.Equals(uri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.IdnHost, baseUri.IdnHost, StringComparison.OrdinalIgnoreCase)
        && uri.Port == baseUri.Port
        && uri.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal)
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0;

    private async Task<PrintPreviewFetchResult> ReadResponseAsync(HttpResponseMessage response, string printerId, PrintPreviewSize size, string? etag, CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                break;
            case HttpStatusCode.NotModified when etag is not null:
                return PrintPreviewFetchResult.NotModified(response.Headers.ETag?.ToString() ?? etag);
            case HttpStatusCode.NotFound:
                // "File doesn't contain preview" (or the file is gone): an expected outcome, not worth a log entry.
                return PrintPreviewFetchResult.NotAvailable;
            default:
                LogUnexpectedStatus(printerId, size, (int)response.StatusCode);
                return PrintPreviewFetchResult.Failed;
        }

        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            LogRejected(printerId, size, "not image/png");
            return PrintPreviewFetchResult.NotAvailable;
        }

        if (response.Content.Headers.ContentLength > _options.MaxBytes)
        {
            LogRejected(printerId, size, "too large");
            return PrintPreviewFetchResult.NotAvailable;
        }

        // One byte more than allowed tells "exactly at the limit" from "too large" without reading further.
        int capacity = _options.MaxBytes + 1;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
        try
        {
            int length = 0;
            Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                while (length < capacity)
                {
                    int read = await stream.ReadAsync(buffer.AsMemory(length, Math.Min(ReadChunk, capacity - length)), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    length += read;
                }
            }

            if (length > _options.MaxBytes)
            {
                LogRejected(printerId, size, "too large");
                return PrintPreviewFetchResult.NotAvailable;
            }

            if (PrintPreview.FromPng(buffer.AsSpan(0, length)) is not { } preview)
            {
                LogRejected(printerId, size, "not a PNG within the size limits");
                return PrintPreviewFetchResult.NotAvailable;
            }

            return PrintPreviewFetchResult.Downloaded(preview, response.Headers.ETag?.ToString());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Debug: an offline printer would otherwise log on every retry. The reason never contains the address or a secret.
    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Size} G-code preview of printer '{PrinterId}' could not be fetched: {Reason}.")]
    private partial void LogFailed(string printerId, PrintPreviewSize size, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Size} G-code preview of printer '{PrinterId}' could not be fetched: login failed ({Reason}).")]
    private partial void LogLoginFailed(string printerId, PrintPreviewSize size, PrusaLinkFailure reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Size} G-code preview of printer '{PrinterId}' could not be fetched: request failed ({Error}).")]
    private partial void LogRequestFailed(string printerId, PrintPreviewSize size, HttpRequestError error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Size} G-code preview of printer '{PrinterId}' could not be fetched: HTTP {StatusCode}.")]
    private partial void LogUnexpectedStatus(string printerId, PrintPreviewSize size, int statusCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Size} G-code preview of printer '{PrinterId}' was refused: {Reason}.")]
    private partial void LogRejected(string printerId, PrintPreviewSize size, string reason);
}
