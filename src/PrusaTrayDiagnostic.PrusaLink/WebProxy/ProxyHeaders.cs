using System.Net.Http.Headers;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace PrusaTrayDiagnostic.PrusaLink.WebProxy;

/// <summary>Which headers cross the proxy in each direction, and how <c>Location</c> is mapped back to the proxy.</summary>
internal static class ProxyHeaders
{
    /// <summary>Hop-by-hop headers (RFC 9110 §7.6.1); in addition every header named in <c>Connection</c>.</summary>
    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Proxy-Connection",
    };

    /// <summary>
    /// Never sent to the printer: credentials of the browser (the proxy authenticates itself, and cookies on 127.0.0.1
    /// are shared by every local port), the proxy address (Origin, Referer carry the token), headers the HTTP client
    /// sets itself (Host, Content-Length) and Expect (the proxy's server answers 100-continue itself).
    /// </summary>
    private static readonly HashSet<string> RequestExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Authorization", "Proxy-Authorization", "Cookie", "X-Api-Key", Origin, "Referer", "Expect", "Content-Length",
    };

    /// <summary>
    /// Never sent to the browser: login challenges (the browser would show its own login dialog and the web loops on
    /// 401), cookies, and headers the proxy sets itself.
    /// </summary>
    private static readonly HashSet<string> ResponseExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "WWW-Authenticate", "Proxy-Authenticate", "Set-Cookie", "Set-Cookie2", "Content-Length", "Location",
        ReferrerPolicy, ContentTypeOptions, ResourcePolicy,
    };

    public const string ReferrerPolicy = "Referrer-Policy";

    public const string ContentTypeOptions = "X-Content-Type-Options";

    public const string ResourcePolicy = "Cross-Origin-Resource-Policy";

    /// <summary>Fetch metadata of the browser: where the request comes from relative to the proxy page.</summary>
    public const string SecFetchSite = "Sec-Fetch-Site";

    public const string Origin = "Origin";

    /// <summary>Copies the browser's headers that may reach the printer (content headers onto the content, if any).</summary>
    public static void CopyRequestHeaders(IHeaderDictionary source, HttpRequestMessage target)
    {
        HashSet<string>? connectionTokens = ConnectionTokens(source.Connection);
        foreach (KeyValuePair<string, StringValues> header in source)
        {
            string name = header.Key;
            if (HopByHop.Contains(name) || RequestExcluded.Contains(name) || (connectionTokens?.Contains(name) ?? false))
            {
                continue;
            }

            if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                target.Content?.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)header.Value);
            }
            else
            {
                target.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)header.Value);
            }
        }
    }

    /// <summary>
    /// Copies the printer's headers that may reach the browser, rewrites <c>Location</c> into the proxy and adds the
    /// proxy's own headers. Content-Length is set separately (it must match what is actually written).
    /// </summary>
    public static void CopyResponseHeaders(HttpResponseMessage source, HttpResponse target, ProxyLocation location)
    {
        HashSet<string>? connectionTokens = source.Headers.NonValidated.TryGetValues("Connection", out HeaderStringValues connection)
            ? ConnectionTokens(new StringValues([.. connection]))
            : null;
        Copy(source.Headers.NonValidated, target, connectionTokens);
        Copy(source.Content.Headers.NonValidated, target, connectionTokens);

        if (source.Headers.NonValidated.TryGetValues("Location", out HeaderStringValues locations) && locations.Count == 1)
        {
            target.Headers.Location = location.Rewrite(locations.ToString());
        }
    }

    private static void Copy(HttpHeadersNonValidated headers, HttpResponse target, HashSet<string>? connectionTokens)
    {
        foreach (KeyValuePair<string, HeaderStringValues> header in headers)
        {
            string name = header.Key;
            if (HopByHop.Contains(name) || ResponseExcluded.Contains(name) || (connectionTokens?.Contains(name) ?? false))
            {
                continue;
            }

            StringValues values = header.Value.Count == 1 ? new StringValues(header.Value.ToString()) : new StringValues([.. header.Value]);
            try
            {
                target.Headers.Append(name, values);
            }
            catch (InvalidOperationException)
            {
                // The server refuses the value (e.g. non-ASCII characters); the header is dropped rather than the response.
            }
        }
    }

    private static HashSet<string>? ConnectionTokens(StringValues connection)
    {
        if (StringValues.IsNullOrEmpty(connection))
        {
            return null;
        }

        HashSet<string> tokens = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? value in connection)
        {
            foreach (string token in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }
}

/// <summary>Maps redirect targets on the printer to the proxy path <c>/&lt;token&gt;/</c>; other targets stay unchanged.</summary>
/// <param name="printerBase">Base address of the printer (ends with '/').</param>
/// <param name="proxyPrefix">Path prefix of the proxy: <c>/&lt;token&gt;/</c>.</param>
internal sealed class ProxyLocation(Uri printerBase, string proxyPrefix)
{
    public string Rewrite(string location)
    {
        string basePath = printerBase.AbsolutePath;
        if (Uri.TryCreate(location, UriKind.Absolute, out Uri? absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            bool samePrinter = absolute.Scheme == printerBase.Scheme
                && string.Equals(absolute.Host, printerBase.Host, StringComparison.OrdinalIgnoreCase)
                && absolute.Port == printerBase.Port
                && absolute.AbsolutePath.StartsWith(basePath, StringComparison.Ordinal);
            return samePrinter
                ? proxyPrefix + absolute.PathAndQuery[basePath.Length..] + absolute.Fragment
                : location;
        }

        // An absolute path on the printer; "//host/..." is a reference to another host and stays unchanged.
        if (location.StartsWith('/') && !location.StartsWith("//", StringComparison.Ordinal)
            && location.StartsWith(basePath, StringComparison.Ordinal))
        {
            return proxyPrefix + location[basePath.Length..];
        }

        // Relative references resolve against the proxied request address and stay correct.
        return location;
    }
}
