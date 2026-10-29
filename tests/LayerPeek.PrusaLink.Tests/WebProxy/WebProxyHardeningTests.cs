using System.Net;
using System.Text;

namespace LayerPeek.PrusaLink.Tests.WebProxy;

/// <summary>
/// T-107 (security finding N1): the printer's content runs in the proxy's origin <c>http://127.0.0.1:&lt;port&gt;</c>. It
/// may register no Service Worker, gets only an allow-list of the printer's response headers, and every answer carries
/// the proxy's hardening headers.
/// </summary>
public class WebProxyHardeningTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Service-Worker", "script")]
    [InlineData("Service-Worker", "other")]
    [InlineData("Sec-Fetch-Dest", "serviceworker")]
    [InlineData("Sec-Fetch-Dest", "ServiceWorker")]
    public async Task ServiceWorkerScriptRequestsAreRejectedBeforeThePrinter(string header, string value)
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        int printerRequests = context.Printer.Requests.Count;
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(proxy, "sw.js"));
        request.Headers.TryAddWithoutValidation(header, value);
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

        using HttpResponseMessage response = await context.Browser.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Service Worker", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(printerRequests, context.Printer.Requests.Count);
        string logs = context.Logger.AllText;
        Assert.Contains("Service Worker", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(context.Proxy().TokenForTests, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("sw.js", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceWorkerRequestWithAWrongTokenStaysNotFound()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri($"http://127.0.0.1:{proxy.Port}/wrong-token/sw.js"));
        request.Headers.TryAddWithoutValidation("Service-Worker", "script");

        using HttpResponseMessage response = await context.Browser.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Theory]
    [InlineData("script")]
    [InlineData("document")]
    [InlineData("empty")]
    [InlineData("image")]
    public async Task OtherFetchDestinationsAreForwarded(string destination)
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("js") });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(proxy, "main.js"));
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", destination);

        using HttpResponseMessage response = await context.Browser.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("js", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("/main.js", context.Printer.Requests.Last().PathAndQuery);
    }

    [Theory]
    [InlineData("Service-Worker-Allowed", "/")]
    [InlineData("Access-Control-Allow-Origin", "*")]
    [InlineData("Access-Control-Allow-Credentials", "true")]
    [InlineData("Content-Security-Policy-Report-Only", "default-src 'none'")]
    [InlineData("Link", "</sw.js>; rel=serviceworker")]
    [InlineData("Refresh", "0; url=http://evil.example/")]
    [InlineData("Clear-Site-Data", "\"cookies\"")]
    [InlineData("Strict-Transport-Security", "max-age=31536000")]
    [InlineData("Set-Cookie", "printer=1; Path=/")]
    [InlineData("WWW-Authenticate", "Digest realm=\"Printer API\", nonce=\"1\"")]
    [InlineData("Content-Location", "http://192.168.1.20/api/v1/files/usb/a.gcode")]
    [InlineData("Server", "Buddy")]
    [InlineData("Origin-Agent-Cluster", "?0")]
    [InlineData("X-Unknown", "yes")]
    public async Task HeadersOutsideTheAllowListDoNotReachTheBrowser(string header, string value)
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = (_, _) =>
        {
            HttpResponseMessage answer = new(HttpStatusCode.OK) { Content = new StringContent("<html></html>", Encoding.UTF8, "text/html") };
            if (!answer.Headers.TryAddWithoutValidation(header, value))
            {
                Assert.True(answer.Content.Headers.TryAddWithoutValidation(header, value));
            }

            return Task.FromResult(answer);
        };

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "index.html"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(header), $"{header} reached the browser.");
        Assert.False(response.Content.Headers.Contains(header), $"{header} reached the browser.");
        Assert.Equal("<html></html>", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task HeadersTheWebNeedsAreKept()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        DateTimeOffset modified = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
        context.Printer.Handler = (_, _) =>
        {
            HttpResponseMessage answer = new(HttpStatusCode.OK) { Content = new ByteArrayContent([0x1F, 0x8B, 0x08]) };
            answer.Content.Headers.TryAddWithoutValidation("Content-Type", "application/javascript");
            answer.Content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
            answer.Content.Headers.TryAddWithoutValidation("Content-Disposition", "attachment");
            answer.Content.Headers.LastModified = modified;
            answer.Content.Headers.Expires = modified.AddMinutes(1);
            answer.Headers.TryAddWithoutValidation("Cache-Control", "private, max-age=86400");
            answer.Headers.TryAddWithoutValidation("ETag", "\"1234\"");
            answer.Headers.TryAddWithoutValidation("Read-Only", "false");
            answer.Headers.TryAddWithoutValidation("Currently-Printing", "true");
            return Task.FromResult(answer);
        };

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "main.js"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("gzip", Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(modified, response.Content.Headers.LastModified);
        Assert.Equal(modified.AddMinutes(1), response.Content.Headers.Expires);
        Assert.Equal("private, max-age=86400", string.Join(", ", response.Headers.NonValidated["Cache-Control"]));
        Assert.Equal("\"1234\"", response.Headers.ETag?.Tag);
        Assert.Equal("false", Assert.Single(response.Headers.GetValues("Read-Only")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Currently-Printing")));
        Assert.Equal(3, response.Content.Headers.ContentLength);
        Assert.Equal([0x1F, 0x8B, 0x08], await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task AllowedHeaderNamedInConnectionIsDropped()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = static (_, _) =>
        {
            HttpResponseMessage answer = new(HttpStatusCode.OK) { Content = new StringContent("{}") };
            answer.Headers.TryAddWithoutValidation("Connection", "ETag");
            answer.Headers.TryAddWithoutValidation("ETag", "\"1\"");
            return Task.FromResult(answer);
        };

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.ETag);
    }

    [Fact]
    public async Task EveryResponseCarriesTheHardeningHeaders()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = static (_, _) =>
        {
            // The printer (or whoever controls its traffic) cannot weaken the policies.
            HttpResponseMessage answer = new(HttpStatusCode.OK) { Content = new StringContent("<html></html>", Encoding.UTF8, "text/html") };
            answer.Headers.TryAddWithoutValidation("Cross-Origin-Opener-Policy", "unsafe-none");
            answer.Headers.TryAddWithoutValidation("X-Frame-Options", "ALLOWALL");
            answer.Headers.TryAddWithoutValidation("Content-Security-Policy", "frame-ancestors *; worker-src *");
            return Task.FromResult(answer);
        };
        using HttpRequestMessage serviceWorker = new(HttpMethod.Get, new Uri(proxy, "sw.js"));
        serviceWorker.Headers.TryAddWithoutValidation("Service-Worker", "script");
        using HttpRequestMessage otherHost = new(HttpMethod.Get, new Uri(proxy, "index.html"));
        otherHost.Headers.Host = "evil.example";

        using HttpResponseMessage page = await context.Browser.GetAsync(new Uri(proxy, "index.html"), Ct);
        using HttpResponseMessage rejected = await context.Browser.SendAsync(serviceWorker, Ct);
        using HttpResponseMessage misdirected = await context.Browser.SendAsync(otherHost, Ct);
        using HttpResponseMessage notFound = await context.Browser.GetAsync(new Uri($"http://127.0.0.1:{proxy.Port}/favicon.ico"), Ct);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.MisdirectedRequest, misdirected.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        foreach (HttpResponseMessage response in new[] { page, rejected, misdirected, notFound })
        {
            Assert.Equal("same-origin", Single(response, "Cross-Origin-Opener-Policy"));
            Assert.Equal("DENY", Single(response, "X-Frame-Options"));
            Assert.Equal("frame-ancestors 'none'; worker-src 'none'", Single(response, "Content-Security-Policy"));
            Assert.Equal("same-origin", Single(response, "Cross-Origin-Resource-Policy"));
            Assert.Equal("no-referrer", Single(response, "Referrer-Policy"));
            Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
            Assert.False(response.Headers.Contains("Service-Worker-Allowed"));
        }
    }

    private static string Single(HttpResponseMessage response, string header)
    {
        Assert.True(response.Headers.TryGetValues(header, out IEnumerable<string>? values), $"No {header} on {response.StatusCode}.");
        return Assert.Single(values);
    }
}
