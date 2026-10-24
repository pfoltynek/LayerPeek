namespace LayerPeek.Core.Web;

/// <summary>
/// Result of <see cref="IPrinterWebGateway.OpenAsync"/>: the address to open in the browser and whether it goes through
/// the proxy. A proxy address contains a secret access token: never log it (<see cref="ToString"/> omits it).
/// </summary>
public sealed class PrinterWebAccess
{
    private PrinterWebAccess(Uri? address, PrinterWebFallback fallback)
    {
        Address = address;
        Fallback = fallback;
    }

    /// <summary>
    /// The proxy address (<see cref="IsProxied"/>), the plain printer address, or <see langword="null"/> when the gateway
    /// has none (<see cref="PrinterWebFallback.NotSupported"/>, <see cref="PrinterWebFallback.InvalidAddress"/>).
    /// </summary>
    public Uri? Address { get; }

    /// <summary>Why the plain address is returned; <see cref="PrinterWebFallback.None"/> for the proxy address.</summary>
    public PrinterWebFallback Fallback { get; }

    /// <summary>The browser opens the printer through the proxy, logged in.</summary>
    public bool IsProxied => Fallback == PrinterWebFallback.None;

    /// <summary>Access through the proxy at <paramref name="proxyAddress"/>.</summary>
    public static PrinterWebAccess Proxied(Uri proxyAddress)
    {
        ArgumentNullException.ThrowIfNull(proxyAddress);
        if (!proxyAddress.IsAbsoluteUri)
        {
            throw new ArgumentException("The proxy address must be absolute.", nameof(proxyAddress));
        }

        return new PrinterWebAccess(proxyAddress, PrinterWebFallback.None);
    }

    /// <summary>The plain printer address (if known) to open instead of the proxy, and why.</summary>
    public static PrinterWebAccess Plain(Uri? printerAddress, PrinterWebFallback reason)
    {
        if (reason == PrinterWebFallback.None || !Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "A fallback needs a reason.");
        }

        if (printerAddress is { IsAbsoluteUri: false })
        {
            throw new ArgumentException("The printer address must be absolute.", nameof(printerAddress));
        }

        return new PrinterWebAccess(printerAddress, reason);
    }

    /// <summary>Never contains the address: a proxy address carries the access token.</summary>
    public override string ToString() => $"{nameof(PrinterWebAccess)} {{ IsProxied = {IsProxied}, Fallback = {Fallback} }}";
}
