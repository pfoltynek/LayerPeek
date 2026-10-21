using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

using PrusaTrayDiagnostic.PrusaLink.WebProxy;

namespace PrusaTrayDiagnostic.PrusaLink.Tests.WebProxy;

/// <summary>
/// T-051 (security finding L1): a foreign web page that knows the token must not change anything on the printer. Write
/// requests whose <c>Sec-Fetch-Site</c> or <c>Origin</c> reveal another page are answered 403 by the proxy itself.
/// </summary>
public class WebProxyOriginGuardTests
{
    private const string OwnOrigin = "{own}";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("POST", "cross-site", null)]
    [InlineData("PUT", "same-site", null)]
    [InlineData("DELETE", "cross-site", null)]
    [InlineData("PATCH", "same-site", null)]
    [InlineData("POST", "", null)]
    [InlineData("POST", "SAME-ORIGIN", null)]
    [InlineData("POST", "same-origin, cross-site", null)]
    [InlineData("POST", null, "http://evil.example")]
    [InlineData("PUT", null, "null")]
    [InlineData("DELETE", null, "")]
    [InlineData("POST", null, "http://127.0.0.1:1")]
    [InlineData("POST", null, "http://localhost:{port}")]
    [InlineData("POST", null, "https://127.0.0.1:{port}")]
    [InlineData("POST", null, "http://127.0.0.1:{port}/")]
    [InlineData("POST", null, "HTTP://127.0.0.1:{port}")]
    [InlineData("POST", "same-origin", "http://evil.example")]
    [InlineData("DELETE", "none", "null")]
    [InlineData("PUT", "cross-site", OwnOrigin)]
    [InlineData("POST", "same-site", OwnOrigin)]
    public async Task ForeignWriteRequestsAreRejectedBeforeThePrinter(string method, string? fetchSite, string? origin)
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        int printerRequests = context.Printer.Requests.Count;
        using HttpRequestMessage request = WriteRequest(method, new Uri(proxy, "api/v1/files/usb/benchy.gcode"), proxy.Port, fetchSite, origin);

        using HttpResponseMessage response = await context.Browser.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("403: Forbidden", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(printerRequests, context.Printer.Requests.Count);
    }

    [Theory]
    [InlineData("POST", "same-origin", OwnOrigin)]
    [InlineData("PUT", "same-origin", OwnOrigin)]
    [InlineData("DELETE", "same-origin", OwnOrigin)]
    [InlineData("PATCH", "same-origin", OwnOrigin)]
    [InlineData("POST", "none", null)]
    [InlineData("POST", "same-origin", null)]
    [InlineData("DELETE", null, OwnOrigin)]
    [InlineData("POST", "none", OwnOrigin)]
    [InlineData("POST", null, null)]
    [InlineData("PUT", null, null)]
    [InlineData("DELETE", null, null)]
    public async Task OwnAndNonBrowserWriteRequestsAreForwarded(string method, string? fetchSite, string? origin)
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        using HttpRequestMessage request = WriteRequest(method, new Uri(proxy, "api/v1/files/usb/benchy.gcode"), proxy.Port, fetchSite, origin);

        using HttpResponseMessage response = await context.Browser.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        RecordedRequest forwarded = Assert.Single(context.Printer.Requests, static r => r.PathAndQuery == "/api/v1/files/usb/benchy.gcode" && r.Status == HttpStatusCode.Continue);
        Assert.Equal(method, forwarded.Method);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task SafeMethodsFromForeignPagesAreForwardedUnchanged(string method)
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        using HttpRequestMessage request = new(new HttpMethod(method), new Uri(proxy, "api/v1/status"));
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        request.Headers.TryAddWithoutValidation("Origin", "http://evil.example");

        using HttpResponseMessage response = await context.Browser.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        RecordedRequest forwarded = Assert.Single(context.Printer.Requests, static r => r.PathAndQuery == "/api/v1/status" && r.Status == HttpStatusCode.Continue);
        Assert.Equal(method, forwarded.Method);
    }

    [Theory]
    [InlineData("Sec-Fetch-Site: same-origin\r\nSec-Fetch-Site: same-origin\r\n")]
    [InlineData("Origin: {own}\r\nOrigin: {own}\r\n")]
    [InlineData("Sec-Fetch-Site: same-origin\r\nOrigin: {own}\r\nOrigin: http://evil.example\r\n")]
    public async Task RepeatedFetchHeadersAreRejected(string headers)
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        int printerRequests = context.Printer.Requests.Count;
        string own = OwnOriginOf(proxy.Port);

        int status = await ProxyTestContext.SendRawAsync(
            proxy.Port,
            $"POST {proxy.AbsolutePath}api/v1/files/usb/benchy.gcode HTTP/1.1\r\nHost: 127.0.0.1:{proxy.Port}\r\n"
            + headers.Replace(OwnOrigin, own, StringComparison.Ordinal)
            + "Content-Length: 0\r\nConnection: close\r\n\r\n");

        Assert.Equal(403, status);
        Assert.Equal(printerRequests, context.Printer.Requests.Count);
    }

    [Fact]
    public async Task WrongTokenOrHostAnswerAsBeforeForForeignWrites()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        int printerRequests = context.Printer.Requests.Count;
        string token = context.Proxy().TokenForTests;
        char changed = token[^1] == 'A' ? 'B' : 'A';
        Uri wrongTokenUri = new($"http://127.0.0.1:{proxy.Port}/{token[..^1]}{changed}/api/v1/files/usb/benchy.gcode");
        using HttpRequestMessage wrongToken = WriteRequest("POST", wrongTokenUri, proxy.Port, "cross-site", "http://evil.example");
        using HttpRequestMessage foreignHost = WriteRequest("POST", new Uri(proxy, "api/v1/files/usb/benchy.gcode"), proxy.Port, "cross-site", "http://evil.example");
        foreignHost.Headers.Host = $"evil.example:{proxy.Port}";

        // A rejection must not tell a foreign page whether its token is valid: an unknown token stays a plain 404.
        using HttpResponseMessage wrongTokenResponse = await context.Browser.SendAsync(wrongToken, Ct);
        using HttpResponseMessage foreignHostResponse = await context.Browser.SendAsync(foreignHost, Ct);

        Assert.Equal(HttpStatusCode.NotFound, wrongTokenResponse.StatusCode);
        Assert.Empty(await wrongTokenResponse.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(HttpStatusCode.MisdirectedRequest, foreignHostResponse.StatusCode);
        Assert.Equal(printerRequests, context.Printer.Requests.Count);
    }

    [Fact]
    public async Task ForeignStreamedUploadIsRejectedAndTheProxyKeepsWorking()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        int printerRequests = context.Printer.Requests.Count;
        using HttpRequestMessage upload = WriteRequest("PUT", new Uri(proxy, "api/v1/files/usb/big.bgcode"), proxy.Port, "same-site", null);
        // Chunked (no Content-Length) and larger than the buffered limit: the proxy answers without reading it all.
        upload.Content = new StreamContent(new GeneratedStream(8L * 1024 * 1024));
        upload.Headers.TransferEncodingChunked = true;

        using HttpResponseMessage rejected = await context.Browser.SendAsync(upload, Ct);
        using HttpResponseMessage page = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.DoesNotContain(context.Printer.Requests.Skip(printerRequests), static r => r.Method == "PUT");
    }

    [Fact]
    public async Task RejectionIsLoggedWithoutTokenPathOrOrigin()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        string token = context.Proxy().TokenForTests;
        using HttpRequestMessage bySite = WriteRequest("DELETE", new Uri(proxy, "api/v1/files/usb/secret-name.gcode?q=secret-query"), proxy.Port, "cross-site", null);
        using HttpRequestMessage byOrigin = WriteRequest("POST", new Uri(proxy, "api/v1/files/usb/secret-name.gcode"), proxy.Port, null, "http://evil-origin.example");

        using (HttpResponseMessage response = await context.Browser.SendAsync(bySite, Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (HttpResponseMessage response = await context.Browser.SendAsync(byOrigin, Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        string[] rejections = [.. context.Logger.Entries.Where(static e => e.Contains("rejected", StringComparison.Ordinal))];
        Assert.Equal(2, rejections.Length);
        Assert.Contains(rejections, static e => e.Contains("DELETE", StringComparison.Ordinal) && e.Contains("Sec-Fetch-Site", StringComparison.Ordinal));
        Assert.Contains(rejections, static e => e.Contains("POST", StringComparison.Ordinal) && e.Contains("Origin", StringComparison.Ordinal));
        string logs = context.Logger.AllText;
        Assert.DoesNotContain(token, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-name", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-query", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("evil-origin", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("cross-site", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedRequestsDoNotPostponeTheIdleStop()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        PrusaLinkWebProxy proxyInstance = context.Proxy();

        context.Time.Advance(TimeSpan.FromMinutes(9));
        using (HttpRequestMessage request = WriteRequest("POST", new Uri(proxy, "api/v1/files/usb/benchy.gcode"), proxy.Port, "cross-site", null))
        using (HttpResponseMessage response = await context.Browser.SendAsync(request, Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        context.Time.Advance(TimeSpan.FromMinutes(1));

        await proxyInstance.Stopped.WaitAsync(Wait, Ct);
    }

    [Fact]
    public async Task EveryResponseForbidsCrossOriginEmbedding()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = static (_, _) =>
        {
            // A printer (or whoever controls it) cannot widen the policy.
            HttpResponseMessage answer = new(HttpStatusCode.OK) { Content = new StringContent("<html></html>", Encoding.UTF8, "text/html") };
            answer.Headers.TryAddWithoutValidation("Cross-Origin-Resource-Policy", "cross-origin");
            return Task.FromResult(answer);
        };
        using HttpRequestMessage rejected = WriteRequest("POST", new Uri(proxy, "api/v1/files/usb/benchy.gcode"), proxy.Port, "cross-site", null);

        using HttpResponseMessage page = await context.Browser.GetAsync(new Uri(proxy, "index.html"), Ct);
        using HttpResponseMessage forbidden = await context.Browser.SendAsync(rejected, Ct);
        using HttpResponseMessage notFound = await context.Browser.GetAsync(new Uri($"http://127.0.0.1:{proxy.Port}/favicon.ico"), Ct);

        foreach (HttpResponseMessage response in new[] { page, forbidden, notFound })
        {
            Assert.True(response.Headers.TryGetValues("Cross-Origin-Resource-Policy", out IEnumerable<string>? values), $"No CORP on {response.StatusCode}.");
            Assert.Equal("same-origin", Assert.Single(values));
        }
    }

    private static string OwnOriginOf(int port) => string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}");

    private static HttpRequestMessage WriteRequest(string method, Uri uri, int port, string? fetchSite, string? origin)
    {
        HttpRequestMessage request = new(new HttpMethod(method), uri)
        {
            Content = new ByteArrayContent("{\"command\":\"x\"}"u8.ToArray()),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (fetchSite is not null)
        {
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", fetchSite);
        }

        if (origin is not null)
        {
            string value = origin == OwnOrigin
                ? OwnOriginOf(port)
                : origin.Replace("{port}", port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            request.Headers.TryAddWithoutValidation("Origin", value);
        }

        return request;
    }
}
