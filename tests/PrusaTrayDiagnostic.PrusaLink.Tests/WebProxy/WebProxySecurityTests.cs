using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.Core.Web;

namespace PrusaTrayDiagnostic.PrusaLink.Tests.WebProxy;

public class WebProxySecurityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProxyAddressIsLoopbackWithTokenPath()
    {
        await using ProxyTestContext context = new();

        Uri proxy = await context.OpenAsync();

        Assert.Equal("http", proxy.Scheme);
        Assert.Equal("127.0.0.1", proxy.Host);
        Assert.Equal(context.Proxy().Port, proxy.Port);
        Assert.Equal("/" + context.Proxy().TokenForTests + "/", proxy.AbsolutePath);
        Assert.Empty(proxy.UserInfo);
        Assert.Empty(proxy.Query);
    }

    [Fact]
    public async Task MissingOrWrongTokenAnswersTheSameNotFound()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        string token = context.Proxy().TokenForTests;
        string origin = proxy.GetLeftPart(UriPartial.Authority);
        char changed = token[^1] == 'A' ? 'B' : 'A';
        int printerRequests = context.Printer.Requests.Count;
        string[] paths =
        [
            "/",
            "/api/v1/status",
            "/favicon.ico",
            "/" + token[..^1] + changed + "/api/v1/status",
            "/" + token[..^1] + "/",
            "/" + token + "x/",
            "/x" + token + "/",
            "/%2F" + token + "/",
            "/" + token.ToUpperInvariant() + "/",
        ];

        foreach (string path in paths)
        {
            using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(origin + path), Ct);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
            Assert.False(response.Headers.Contains("Location"));
        }

        Assert.Equal(printerRequests, context.Printer.Requests.Count);
    }

    [Fact]
    public async Task TokenOfAnotherPrinterIsRejected()
    {
        await using ProxyTestContext context = new();
        await context.Secrets.SetAsync(SecretKeys.ForPrinter("mk4"), context.Printer.Password, Ct);
        Uri coreOne = await context.OpenAsync();
        Uri mk4 = await context.OpenAsync(ProxyTestContext.Definition("mk4", "192.168.1.21"));
        Assert.NotEqual(coreOne.Port, mk4.Port);

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri($"http://127.0.0.1:{mk4.Port}{coreOne.AbsolutePath}api/v1/status"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("localhost:{port}")]
    [InlineData("evil.example")]
    [InlineData("evil.example:{port}")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:1")]
    [InlineData("[::1]:{port}")]
    [InlineData("127.000.000.001:{port}")]
    public async Task OtherHostHeadersAreRejected(string host)
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        int printerRequests = context.Printer.Requests.Count;
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(proxy, "api/v1/status"));
        request.Headers.Host = host.Replace("{port}", proxy.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        using HttpResponseMessage response = await context.Browser.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.MisdirectedRequest, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(printerRequests, context.Printer.Requests.Count);
    }

    [Theory]
    [InlineData("GET {path} HTTP/1.1\r\nHost: {host}\r\nHost: {host}\r\n\r\n")]
    [InlineData("GET {path} HTTP/1.1\r\n\r\n")]
    [InlineData("GET {path} HTTP/1.1\r\nHost: \r\n\r\n")]
    [InlineData("GET http://evil.example{path} HTTP/1.1\r\nHost: {host}\r\n\r\n")]
    public async Task MalformedHostIsRejected(string template)
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        int printerRequests = context.Printer.Requests.Count;
        string request = template.Replace("{path}", proxy.AbsolutePath + "api/v1/status", StringComparison.Ordinal)
            .Replace("{host}", $"127.0.0.1:{proxy.Port}", StringComparison.Ordinal);

        int status = await ProxyTestContext.SendRawAsync(proxy.Port, request);

        Assert.True(status is 400 or 404 or 421, $"Unexpected status {status}.");
        Assert.Equal(printerRequests, context.Printer.Requests.Count);
    }

    [Fact]
    public async Task DotSegmentsCannotLeaveThePrinterBasePath()
    {
        await using ProxyTestContext context = new();
        context.Printer.RouteFixture("/printers/mk4/api/version", "version");
        Uri proxy = await context.OpenAsync(ProxyTestContext.Definition(address: "http://proxy.lan/printers/mk4"));
        int printerRequests = context.Printer.Requests.Count;

        int status = await ProxyTestContext.SendRawAsync(
            proxy.Port,
            $"GET {proxy.AbsolutePath}../../other/api/v1/status HTTP/1.1\r\nHost: 127.0.0.1:{proxy.Port}\r\n\r\n");

        Assert.Equal(404, status);
        Assert.Equal(printerRequests, context.Printer.Requests.Count);
    }

    [Fact]
    public async Task ConnectAndTraceAreRejected()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        int printerRequests = context.Printer.Requests.Count;

        using HttpResponseMessage trace = await context.Browser.SendAsync(new HttpRequestMessage(HttpMethod.Trace, new Uri(proxy, "api/v1/status")), Ct);
        int connect = await ProxyTestContext.SendRawAsync(proxy.Port, $"CONNECT 192.168.1.20:80 HTTP/1.1\r\nHost: 127.0.0.1:{proxy.Port}\r\n\r\n");
        int connectWithToken = await ProxyTestContext.SendRawAsync(proxy.Port, $"CONNECT {proxy.AbsolutePath} HTTP/1.1\r\nHost: 127.0.0.1:{proxy.Port}\r\n\r\n");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, trace.StatusCode);
        Assert.True(connect is 400 or 404 or 405, $"Unexpected CONNECT status {connect}.");
        Assert.True(connectWithToken is 400 or 404 or 405, $"Unexpected CONNECT status {connectWithToken}.");
        Assert.Equal(printerRequests, context.Printer.Requests.Count);
    }

    [Fact]
    public async Task UpgradeIsRejected()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        int printerRequests = context.Printer.Requests.Count;

        int status = await ProxyTestContext.SendRawAsync(
            proxy.Port,
            $"GET {proxy.AbsolutePath}ws HTTP/1.1\r\nHost: 127.0.0.1:{proxy.Port}\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n\r\n");

        Assert.Equal(501, status);
        Assert.Equal(printerRequests, context.Printer.Requests.Count);
    }

    [Fact]
    public async Task ListenerIsNotReachableOnOtherAddresses()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();

        Assert.True(await ProxyTestContext.IsListeningAsync(IPAddress.Loopback, proxy.Port));

        // The OS listener table: bound to 127.0.0.1 only (not 0.0.0.0, ::, ::1 or a LAN address).
        IPEndPoint listener = Assert.Single(ProxyTestContext.Listeners(proxy.Port));
        Assert.Equal(IPAddress.Loopback, listener.Address);

        // And a real connect to another address of this machine (nothing leaves the computer) is refused.
        IPAddress? other = NetworkInterface.GetAllNetworkInterfaces()
            .Where(static n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(static n => n.GetIPProperties().UnicastAddresses)
            .Select(static a => a.Address)
            .FirstOrDefault(static a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        if (other is not null)
        {
            Assert.False(await ProxyTestContext.IsListeningAsync(other, proxy.Port), $"The proxy accepts connections on {other}.");
        }
    }

    [Fact]
    public async Task LogsContainNoSecrets()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        string token = context.Proxy().TokenForTests;
        using (HttpResponseMessage page = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status?secret=query"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        }

        // Rejected password during the session, an unreachable printer and an idle stop all log something.
        string password = context.Printer.Password;
        context.Printer.Password = "changed-on-printer";
        using (HttpResponseMessage rejected = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        }

        context.Printer.Override = static (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (192.168.1.20:80)");
        using (HttpResponseMessage offline = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct))
        {
            Assert.Equal(HttpStatusCode.BadGateway, offline.StatusCode);
        }

        PrinterWebAccess fallback = await context.Gateway.OpenAsync(ProxyTestContext.Definition("mk4", "192.168.1.21"), Ct);
        Assert.Equal(PrinterWebFallback.NotReachable, fallback.Fallback);
        context.Time.Advance(TimeSpan.FromMinutes(10));

        string logs = context.Logger.AllText;
        Assert.NotEmpty(context.Logger.Entries);
        Assert.Contains("core-one", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(token, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(proxy.AbsoluteUri, logs, StringComparison.Ordinal);
        Assert.DoesNotContain($"127.0.0.1:{proxy.Port}", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(password, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(context.Printer.Password, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(Md5Ha1(password), logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Digest", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("secret=query", logs, StringComparison.Ordinal);
    }

    private static string Md5Ha1(string password) =>
#pragma warning disable CA5351 // The printer firmware mandates MD5 digest authentication.
        Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"maker:{FakePrusaLinkServer.Realm}:{password}")));
#pragma warning restore CA5351
}
