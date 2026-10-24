using Microsoft.Extensions.Logging;

using LayerPeek.Core.Printers;
using LayerPeek.Core.Secrets;

namespace LayerPeek.PrusaLink;

/// <summary>
/// Creates <see cref="PrusaLinkClient"/> instances on the shared, pooled "PrusaLink" <see cref="HttpClient"/> configuration.
/// </summary>
public sealed class PrusaLinkClientFactory(
    IHttpClientFactory httpClientFactory,
    ISecretStore secretStore,
    TimeProvider timeProvider,
    ILogger<PrusaLinkClient> logger) : IPrinterClientFactory
{
    public const string HttpClientName = "PrusaLink";

    IPrinterClient IPrinterClientFactory.Create(PrinterDefinition printer) => Create(printer);

    /// <exception cref="ArgumentException"><paramref name="printer"/> is not a PrusaLink printer.</exception>
    public PrusaLinkClient Create(PrinterDefinition printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        if (printer.ConnectionType != ConnectionType.PrusaLink)
        {
            throw new ArgumentException($"Printer '{printer.Id}' does not use PrusaLink.", nameof(printer));
        }

        return new PrusaLinkClient(httpClientFactory.CreateClient(HttpClientName), printer, secretStore, timeProvider, logger);
    }
}
