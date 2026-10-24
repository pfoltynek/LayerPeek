using Microsoft.Extensions.Logging;

using LayerPeek.App.Platform;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Web;

namespace LayerPeek.App.Tray.Menu;

/// <summary>
/// Opens the web interface of a printer from the tray menu (T-028). For a known printer it asks
/// <see cref="IPrinterWebGateway"/>: a PrusaLink printer whose stored password the printer accepts is opened through the
/// local proxy, logged in, without the password ever reaching the browser. Otherwise (no password, rejected password,
/// offline printer, proxy unavailable, Prusa Connect, unknown printer) the plain address is opened, as the user decided
/// (no extra notification). The proxy address contains a secret token: it is never logged, only the fallback reason is.
/// </summary>
internal sealed partial class PrinterWebOpener
{
    private readonly IPrinterWebGateway _gateway;
    private readonly IUrlLauncher _launcher;
    private readonly ILogger _logger;

    public PrinterWebOpener(IPrinterWebGateway gateway, IUrlLauncher launcher, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(logger);
        _gateway = gateway;
        _launcher = launcher;
        _logger = logger;
    }

    /// <summary>The gateway asked for every known printer (composition tests).</summary>
    internal IPrinterWebGateway Gateway => _gateway;

    /// <summary>Opens the web interface; never throws. Cancelled before launching: opens nothing.</summary>
    /// <param name="webUri">The validated plain address of the menu entry (<see cref="PrinterWebAddress.For"/>).</param>
    /// <param name="printer">
    /// The printer the address belongs to, or <see langword="null"/> when it is not known (then the plain address is
    /// opened and the gateway is not asked).
    /// </param>
    /// <returns><see langword="true"/> when the browser was started.</returns>
    public async Task<bool> OpenAsync(Uri webUri, PrinterDefinition? printer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webUri);
        Uri plain = webUri;
        if (printer is not null)
        {
            PrinterWebAccess access;
            try
            {
                // Called from the menu click on the UI thread: the gateway may block on the OS credential store
                // (CredRead, Keychain) before its first await, and checks the login with the printer.
                access = await Task.Run(() => _gateway.OpenAsync(printer, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                // The app is quitting (the container disposed the gateway): opening a browser now would be surprising.
                return false;
            }
            catch (Exception ex)
            {
                // The gateway reports expected failures as results; anything else must not break the menu. Neither the
                // exception nor its message is logged: they could carry an address with the access token.
                LogGatewayFailed(printer.Id, ex.GetType().Name);
                return await OpenPlainAsync(webUri, cancellationToken).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (access is { IsProxied: true, Address: { } proxyAddress })
            {
                return await _launcher.OpenSecretAsync(proxyAddress, cancellationToken).ConfigureAwait(false);
            }

            if (access.Fallback != PrinterWebFallback.NotSupported)
            {
                // Prusa Connect simply has no proxy; every other reason means the browser asks for the login itself.
                LogOpeningPlain(printer.Id, access.Fallback);
            }

            plain = access.Address ?? webUri;
        }

        return await OpenPlainAsync(plain, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> OpenPlainAsync(Uri plain, CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested && await _launcher.OpenAsync(plain).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening printer '{PrinterId}' logged in failed unexpectedly ({ExceptionType}); opening its plain address.")]
    private partial void LogGatewayFailed(string printerId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Printer '{PrinterId}' is opened without logging in ({Reason}).")]
    private partial void LogOpeningPlain(string printerId, PrinterWebFallback reason);
}
