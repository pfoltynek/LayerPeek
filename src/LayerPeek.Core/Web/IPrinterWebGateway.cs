using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Web;

/// <summary>
/// Gives the browser access to the web interface of a printer without handing it the printer's credentials
/// (T-028). For PrusaLink this is a local reverse proxy that answers the printer's login challenges itself.
/// Thread-safe. Disposing stops every running proxy (at application exit).
/// </summary>
public interface IPrinterWebGateway : IAsyncDisposable
{
    /// <summary>
    /// Checks that the printer accepts the stored credentials and returns the address the browser should open: the
    /// address of a running proxy (started on the first call, reused afterwards) or, when the proxy cannot be used,
    /// the plain printer address with the reason. Expected failures (no password, rejected password, offline printer,
    /// proxy cannot start) are results, not exceptions. May block on the secret store: do not await it on a thread
    /// that must stay responsive without <c>ConfigureAwait(false)</c>.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The gateway was disposed.</exception>
    Task<PrinterWebAccess> OpenAsync(PrinterDefinition printer, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the proxy of the printer (if any): its address stops working and requests in progress are cancelled.
    /// Call after the printer was removed or its address, user name or password changed. The next
    /// <see cref="OpenAsync"/> starts a new proxy with a new address. Never throws for an unknown printer.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for a graceful shutdown; the proxy is stopped anyway.</param>
    Task StopAsync(string printerId, CancellationToken cancellationToken);

    /// <summary>Stops the proxies of all printers, e.g. when the whole configuration was replaced.</summary>
    /// <param name="cancellationToken">Stops waiting for a graceful shutdown; the proxies are stopped anyway.</param>
    Task StopAllAsync(CancellationToken cancellationToken);
}
