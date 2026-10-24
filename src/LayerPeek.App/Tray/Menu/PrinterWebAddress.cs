using LayerPeek.Core.Printers;
using LayerPeek.PrusaLink;

namespace LayerPeek.App.Tray.Menu;

/// <summary>Where the web interface of a printer is.</summary>
internal static class PrinterWebAddress
{
    /// <summary>
    /// Prusa Connect home page. A printer-specific page is not used: its URL format is not documented (T-008 open question).
    /// </summary>
    public static readonly Uri PrusaConnectHome = new("https://connect.prusa3d.com/");

    /// <summary>
    /// The PrusaLink web interface on the printer itself (same address rules as the API client) or Prusa Connect;
    /// <see langword="null"/> when the address cannot be opened safely.
    /// </summary>
    public static Uri? For(PrinterDefinition printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        Uri? uri = printer.ConnectionType switch
        {
            ConnectionType.PrusaLink => PrusaLinkAddress.TryCreateBaseUri(printer.Address, out Uri? baseUri) ? baseUri : null,
            ConnectionType.PrusaConnect => PrusaConnectHome,
            _ => null,
        };

        return uri is not null && IsSafeToLaunch(uri) ? uri : null;
    }

    /// <summary>
    /// Only absolute http(s) addresses without credentials are handed to the OS: anything else (file:, custom
    /// schemes...) could start an arbitrary program.
    /// </summary>
    public static bool IsSafeToLaunch(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host)
            && uri.UserInfo.Length == 0
            && !HasUserInfoDelimiter(uri);
    }

    /// <summary>
    /// <see cref="Uri.UserInfo"/> is empty for <c>http://@host/</c>, yet the address keeps the <c>@</c>: a browser
    /// may still treat it as an address with (empty) credentials, so such an address is not plain either.
    /// </summary>
    private static bool HasUserInfoDelimiter(Uri uri)
    {
        // Only called for http(s), whose absolute form always starts with "scheme://".
        ReadOnlySpan<char> afterScheme = uri.AbsoluteUri.AsSpan(uri.Scheme.Length + "://".Length);
        int end = afterScheme.IndexOfAny('/', '?', '#');
        return (end < 0 ? afterScheme : afterScheme[..end]).Contains('@');
    }
}
