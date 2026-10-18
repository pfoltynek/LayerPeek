using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.PrusaLink;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

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
    /// <paramref name="baseUri"/> with <c>user:password@</c> in front of the host, both percent-encoded as RFC 3986
    /// userinfo (everything except unreserved characters), so that <c>@ : / # % ?</c>, spaces or non-ASCII characters
    /// cannot change the host or path. Used only with credentials from the app's own storage: the base address must
    /// pass <see cref="IsSafeToLaunch"/>, so it never carries userinfo of its own (e.g. typed into the settings).
    /// </summary>
    /// <returns>
    /// A string, never a <see cref="Uri"/>: <see cref="Uri.ToString"/> would turn escaped non-ASCII characters and
    /// spaces back into raw characters. The result contains the password: never log it.
    /// </returns>
    /// <exception cref="ArgumentException">The base address is not safe to launch, or a credential is empty.</exception>
    public static string WithCredentials(Uri baseUri, string userName, string password)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentException.ThrowIfNullOrEmpty(userName);
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (!IsSafeToLaunch(baseUri))
        {
            throw new ArgumentException("Credentials can only be added to a plain http(s) address without credentials.", nameof(baseUri));
        }

        // Safe http(s) addresses always start with "scheme://" and have no userinfo, so the authority starts right after.
        string address = baseUri.AbsoluteUri;
        int authority = baseUri.Scheme.Length + "://".Length;
        return string.Concat(
            address.AsSpan(0, authority),
            Uri.EscapeDataString(userName) + ":" + Uri.EscapeDataString(password) + "@",
            address.AsSpan(authority));
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
    /// <see cref="Uri.UserInfo"/> is empty for <c>http://@host/</c>, yet the address keeps the <c>@</c>: adding
    /// credentials in front of it would produce <c>user:pw@@host</c>, so such an address is not plain either.
    /// </summary>
    private static bool HasUserInfoDelimiter(Uri uri)
    {
        // Only called for http(s), whose absolute form always starts with "scheme://".
        ReadOnlySpan<char> afterScheme = uri.AbsoluteUri.AsSpan(uri.Scheme.Length + "://".Length);
        int end = afterScheme.IndexOfAny('/', '?', '#');
        return (end < 0 ? afterScheme : afterScheme[..end]).Contains('@');
    }
}
