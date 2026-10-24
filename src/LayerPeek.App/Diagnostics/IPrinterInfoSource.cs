using LayerPeek.Core.Printers;
using LayerPeek.PrusaLink;

namespace LayerPeek.App.Diagnostics;

/// <summary>Reads printer and firmware information (model, firmware version...) once for the diagnostics window.</summary>
internal interface IPrinterInfoSource
{
    /// <summary>
    /// Expected failures (unreachable printer, rejected password) come back in the result, not as exceptions.
    /// </summary>
    /// <returns><see langword="null"/> when the connection type offers no such information (Prusa Connect).</returns>
    Task<PrusaLinkInfoResult?> GetInfoAsync(PrinterDefinition printer, CancellationToken cancellationToken);
}

/// <summary>
/// PrusaLink: <c>/api/version</c> and <c>/api/v1/info</c> through a short-lived <see cref="PrusaLinkClient"/> on the shared
/// HTTP configuration (one extra digest challenge per request; the window asks once per printer selection).
/// Prusa Connect has no client yet (T-005), so it has no information.
/// </summary>
internal sealed class PrinterInfoSource(PrusaLinkClientFactory prusaLink) : IPrinterInfoSource
{
    public async Task<PrusaLinkInfoResult?> GetInfoAsync(PrinterDefinition printer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(printer);
        if (printer.ConnectionType != ConnectionType.PrusaLink)
        {
            return null;
        }

        return await prusaLink.Create(printer).GetInfoAsync(cancellationToken).ConfigureAwait(false);
    }
}
