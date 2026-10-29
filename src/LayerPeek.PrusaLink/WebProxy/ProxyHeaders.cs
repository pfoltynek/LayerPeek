using System.Net.Http.Headers;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace LayerPeek.PrusaLink.WebProxy;

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
    /// The only printer headers that reach the browser (T-107, security finding N1). An allow-list, so that headers with
    /// browser semantics never do: <c>Service-Worker-Allowed</c>, <c>Set-Cookie</c>, <c>WWW-Authenticate</c> (the
    /// browser would show its own login dialog and the web loops on 401), <c>Access-Control-*</c>,
    /// <c>Content-Security-Policy</c>, <c>Refresh</c>, <c>Link</c>, <c>Clear-Site-Data</c> and anything unknown.
    /// What the Buddy firmware sends and Prusa-Link-Web reads (details in docs/api/prusalink.md, "Lokální proxy"):
    /// <list type="bullet">
    /// <item><c>Content-Type</c>: every resource; required with <c>X-Content-Type-Options: nosniff</c>.</item>
    /// <item><c>Content-Encoding</c>: the firmware serves the web's files gzip-compressed (<c>static_file.cpp</c>).</item>
    /// <item><c>Content-Disposition</c>: <c>attachment</c> for file downloads (<c>usb_files.cpp</c>).</item>
    /// <item><c>Cache-Control</c>, <c>ETag</c>: caching of the web's files and API answers; the web reads <c>etag</c>
    /// (<c>auth.js</c>) and <c>cache-control</c> (<c>cameras.js</c>).</item>
    /// <item><c>Last-Modified</c>, <c>Expires</c>: read by the web for camera snapshots (<c>cameras.js</c>).</item>
    /// <item><c>Read-Only</c>, <c>Currently-Printing</c>: file information of <c>HEAD /api/v1/files/...</c>
    /// (<c>file_info.cpp</c>); plain data without browser semantics.</item>
    /// </list>
    /// <c>Content-Length</c> and <c>Location</c> are set separately; the proxy's own headers are never taken over.
    /// </summary>
    private static readonly HashSet<string> ResponseAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", "Content-Encoding", "Content-Disposition", "Cache-Control", "ETag", "Last-Modified", "Expires",
        "Read-Only", "Currently-Printing",
    };

    public const string ReferrerPolicy = "Referrer-Policy";

    public const string ContentTypeOptions = "X-Content-Type-Options";

    public const string ResourcePolicy = "Cross-Origin-Resource-Policy";

    public const string OpenerPolicy = "Cross-Origin-Opener-Policy";

    public const string FrameOptions = "X-Frame-Options";

    public const string ContentSecurityPolicy = "Content-Security-Policy";

    /// <summary>
    /// The proxy's own policy (T-107): no page may frame the proxy (clickjacking, also from other ports on 127.0.0.1),
    /// and the printer's content may start no worker, in particular no Service Worker that would outlive the proxy.
    /// Prusa-Link-Web uses neither frames nor workers. Other directives stay unrestricted (scripts, styles, images of
    /// the web are not changed). A policy of the content itself (<c>&lt;meta&gt;</c>) can only add restrictions.
    /// </summary>
    public const string ContentSecurityPolicyValue = "frame-ancestors 'none'; worker-src 'none'";

    /// <summary>Request header browsers send with every fetch of a Service Worker script (registration and update).</summary>
    public const string ServiceWorker = "Service-Worker";

    /// <summary>Fetch metadata: what the response is for (<c>serviceworker</c> for a Service Worker script).</summary>
    public const string SecFetchDest = "Sec-Fetch-Dest";

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
    /// Copies the allowed printer headers (<see cref="ResponseAllowed"/>), rewrites <c>Location</c> into the proxy and adds the
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
            if (!ResponseAllowed.Contains(name) || HopByHop.Contains(name) || (connectionTokens?.Contains(name) ?? false))
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
