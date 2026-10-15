using System.Diagnostics.CodeAnalysis;

namespace PrusaTrayDiagnostic.PrusaLink;

/// <summary>
/// Turns a user-entered printer address ("192.168.1.20", "prusa-core-one.local", "http://host:8080/prefix")
/// into the base URI of the PrusaLink API.
/// </summary>
public static class PrusaLinkAddress
{
    /// <returns>
    /// <see langword="false"/> for anything that is not a plain http(s) host address; credentials, query and
    /// fragment are rejected so that nothing sensitive or surprising is ever sent.
    /// </returns>
    public static bool TryCreateBaseUri(string? address, [NotNullWhen(true)] out Uri? baseUri)
    {
        baseUri = null;
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        string trimmed = address.Trim();
        string candidate = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "http://" + trimmed;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0)
        {
            return false;
        }

        // A trailing slash makes relative API paths resolve below an optional path prefix (reverse proxy).
        string path = uri.AbsolutePath.EndsWith('/') ? uri.AbsolutePath : uri.AbsolutePath + "/";
        baseUri = new UriBuilder(uri.Scheme, uri.Host, uri.Port, path).Uri;
        return true;
    }
}
