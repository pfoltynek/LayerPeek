using System.Net;
using System.Text;

namespace LayerPeek.PrusaLink.Tests.WebProxy;

/// <summary>Browser -> real loopback proxy -> in-memory printer with firmware Digest.</summary>
public class WebProxyForwardingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApiRequestIsForwardedLoggedIn()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Fixtures.Read("status-idle"), await response.Content.ReadAsStringAsync(Ct));
        Assert.False(response.Headers.Contains("WWW-Authenticate"));
        RecordedRequest forwarded = context.Printer.Requests.Last();
        Assert.Equal("/api/v1/status", forwarded.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, forwarded.Status);
        Assert.StartsWith("Digest ", forwarded.Authorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PageIsForwarded()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();

        using HttpResponseMessage response = await context.Browser.GetAsync(proxy, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("<html>PrusaLink</html>", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("/", context.Printer.Requests.Last().PathAndQuery);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public async Task EveryMethodIsForwardedWithItsOwnDigest(string method)
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        string? receivedBody = null;
        context.Printer.Handler = async (request, ct) =>
        {
            receivedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            HttpResponseMessage answer = new(HttpStatusCode.OK) { Content = new StringContent("done") };
            answer.Headers.TryAddWithoutValidation("X-Method", request.Method.Method);
            return answer;
        };
        bool withBody = method is "POST" or "PUT" or "PATCH";
        using HttpRequestMessage request = new(HttpMethod.Parse(method), new Uri(proxy, "api/v1/files/usb/a.gcode"));
        if (withBody)
        {
            request.Content = new StringContent("G28", Encoding.ASCII, "text/x.gcode");
        }

        using HttpResponseMessage response = await context.Browser.SendAsync(request, Ct);

        // The printer's Digest check uses the real method (HA2), so a 200 proves it was computed with it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(method, Assert.Single(response.Headers.GetValues("X-Method")));
        Assert.Equal(method == "HEAD" ? string.Empty : "done", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(withBody ? "G28" : null, receivedBody);
        RecordedRequest forwarded = context.Printer.Requests.Last();
        Assert.Equal(method, forwarded.Method);
        Assert.Equal(1, context.Printer.UnauthorizedCount);
    }

    [Fact]
    public async Task PercentEncodedPathAndQueryArePassedUnchanged()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        const string Rest = "api/v1/files/usb/My%20Print%23%C3%A9.gcode?a=1&b=%2F&c=%20";

        // The browser side must not canonicalize either (Uri would drop the trailing %20).
        Uri raw = new(proxy.AbsoluteUri + Rest, new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true });

        using HttpResponseMessage response = await context.Browser.GetAsync(raw, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("/" + Rest, context.Printer.Requests.Last().PathAndQuery);
    }

    [Fact]
    public async Task PrinterAddressWithPathPrefixIsKept()
    {
        await using ProxyTestContext context = new();
        context.Printer.RouteFixture("/printers/mk4/api/version", "version");
        context.Printer.RouteFixture("/printers/mk4/api/v1/status", "status-idle");
        Uri proxy = await context.OpenAsync(ProxyTestContext.Definition(address: "http://proxy.lan:8080/printers/mk4"));

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status?x=1"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/printers/mk4/api/v1/status?x=1", context.Printer.Requests.Last().PathAndQuery);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?lang=cs")]
    public async Task TokenWithoutTrailingSlashRedirectsToTheWebRoot(string query)
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        int printerRequests = context.Printer.Requests.Count;

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy.AbsoluteUri.TrimEnd('/') + query), Ct);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(proxy.AbsolutePath + query, response.Headers.Location?.OriginalString);
        Assert.Equal(printerRequests, context.Printer.Requests.Count);
    }

    [Theory]
    [InlineData("/api/v1/files/usb/a.gcode", "{prefix}api/v1/files/usb/a.gcode")]
    [InlineData("http://192.168.1.20/index.html?x=1", "{prefix}index.html?x=1")]
    [InlineData("https://www.prusa3d.com/", "https://www.prusa3d.com/")]
    [InlineData("next", "next")]
    public async Task LocationIsRewrittenIntoTheProxy(string location, string expected)
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = (_, _) =>
        {
            HttpResponseMessage answer = new(HttpStatusCode.Created);
            answer.Headers.TryAddWithoutValidation("Location", location);
            return Task.FromResult(answer);
        };

        using HttpResponseMessage response = await context.Browser.PutAsync(new Uri(proxy, "api/v1/files/usb/a.gcode"), new StringContent("G28"), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expected.Replace("{prefix}", proxy.AbsolutePath, StringComparison.Ordinal), response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task BrowserCredentialsOriginAndHopByHopHeadersDoNotReachThePrinter()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(proxy, "api/v1/status"));
        request.Headers.TryAddWithoutValidation("Connection", "X-Hop");
        request.Headers.TryAddWithoutValidation("X-Hop", "1");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        request.Headers.TryAddWithoutValidation("Proxy-Connection", "keep-alive");
        request.Headers.TryAddWithoutValidation("Cookie", "session=browser");
        request.Headers.TryAddWithoutValidation("Authorization", "Basic YnJvd3Nlcjp4");
        request.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic eA==");
        request.Headers.TryAddWithoutValidation("X-Api-Key", "browser-key");
        request.Headers.TryAddWithoutValidation("Origin", proxy.GetLeftPart(UriPartial.Authority));
        request.Headers.Referrer = proxy;
        request.Headers.TryAddWithoutValidation("X-Kept", "yes");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using HttpResponseMessage response = await context.Browser.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        RecordedRequest forwarded = context.Printer.Requests.Last();
        List<string> names = [.. forwarded.AllHeaders.Split('\n').Select(static line => line.Split(':')[0])];
        foreach (string removed in new[] { "X-Hop", "Keep-Alive", "TE", "Proxy-Connection", "Cookie", "Proxy-Authorization", "X-Api-Key", "Origin", "Referer", "Connection" })
        {
            Assert.DoesNotContain(names, name => string.Equals(name, removed, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Contains("X-Kept", names);
        Assert.Equal("application/json", forwarded.Accept);
        Assert.StartsWith("Digest ", forwarded.Authorization, StringComparison.Ordinal);
        Assert.DoesNotContain(proxy.AbsolutePath.Trim('/'), forwarded.AllHeaders, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChallengesCookiesAndHopByHopHeadersDoNotReachTheBrowser()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = static (_, _) =>
        {
            HttpResponseMessage answer = new(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            answer.Headers.TryAddWithoutValidation("Set-Cookie", "printer=1");
            answer.Headers.TryAddWithoutValidation("WWW-Authenticate", "Digest realm=\"Printer API\", nonce=\"1\"");
            answer.Headers.TryAddWithoutValidation("Connection", "X-Hop-Back");
            answer.Headers.TryAddWithoutValidation("X-Hop-Back", "1");
            answer.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=60");
            answer.Headers.TryAddWithoutValidation("Referrer-Policy", "unsafe-url");
            answer.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
            answer.Headers.TryAddWithoutValidation("ETag", "\"42\"");
            answer.Headers.TryAddWithoutValidation("X-Kept-Back", "yes");
            return Task.FromResult(answer);
        };

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.False(response.Headers.Contains("WWW-Authenticate"));
        Assert.False(response.Headers.Contains("X-Hop-Back"));
        Assert.False(response.Headers.Contains("Keep-Alive"));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("\"42\"", response.Headers.ETag?.Tag);
        Assert.Equal("yes", Assert.Single(response.Headers.GetValues("X-Kept-Back")));
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task WebPagesKeepTheirCacheHeaders()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = static (_, _) =>
        {
            HttpResponseMessage answer = new(HttpStatusCode.OK) { Content = new StringContent("js") };
            answer.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
            return Task.FromResult(answer);
        };

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "main.js"), Ct);

        Assert.Equal("max-age=3600", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task NotModifiedIsPassedWithoutBody()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = static (request, _) =>
        {
            Assert.Equal("\"42\"", request.Headers.IfNoneMatch.Single().Tag);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        };
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(proxy, "api/v1/storage"));
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"42\"");

        using HttpResponseMessage response = await context.Browser.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task PrinterErrorsArePassedThrough()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "api/v1/unknown"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("404: Not Found", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreachablePrinterAnswersBadGatewayInFirmwareFormat()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Override = static (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (192.168.1.20:80)");

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"title\":\"502: Bad Gateway\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
