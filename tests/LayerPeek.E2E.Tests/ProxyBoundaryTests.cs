using System.Collections.Concurrent;
using System.Net;

using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;

using LayerPeek.Core.Web;
using LayerPeek.E2E.Tests.Infrastructure;

namespace LayerPeek.E2E.Tests;

/// <summary>What the browser gets outside the logged-in happy path: fallbacks, wrong tokens, foreign hosts, links.</summary>
public sealed class ProxyBoundaryTests(EdgeFixture edge)
{
    private const int Timeout = 120_000;
    private const string WrongToken = "AAAAAAAAAAAAAAAAAAAAAA";

    [Fact(Timeout = Timeout)]
    public async Task RejectedPasswordOpensThePlainAddressWithoutTheProxy()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken, storedPassword: "not-the-printer-password");

        PrinterWebAccess access = await rig.OpenAsync(cancellationToken);

        // Decision T-028 Q3 (a): the plain printer address; the browser asks for the login itself.
        Assert.False(access.IsProxied);
        Assert.Equal(PrinterWebFallback.PasswordRejected, access.Fallback);
        Assert.Equal(rig.Printer.BaseAddress, access.Address);
        IPage page = await browser.NewPageAsync();
        IResponse? response = await page.GotoAsync(access.Address!.ToString());

        // The page is public; its API call gets the printer's own Digest challenge. Edge holds that request for its own
        // login prompt (invisible in headless mode; Playwright does not answer it), so the script neither gets the 401
        // nor loops in setUpAuth.
        Assert.Equal(200, response?.Status);
        Assert.Equal(401, await rig.Printer.AnsweredAsync("/api/v1/info").WaitAsync(cancellationToken));
        // Right after the printer answered, the 401 may still be on its way to the script; without waiting, the assertions
        // below would pass even if the script got the 401 a moment later and looped. That something does NOT happen can
        // only be shown with a bound: first a control request sent after the 401 must make the whole way back to the
        // script (the browser's network and script pipeline is not just slow, e.g. under load), then a grace period for
        // the extra hop of the login handling. A too short bound can only hide a regression, never fail a good run.
        Assert.Equal(200, await page.EvaluateAsync<int>("async () => (await fetch('app.js', { cache: 'no-store' })).status").WaitAsync(cancellationToken));
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        Assert.DoesNotContain(browser.Responses, static r => r.Status == 401);
        Assert.Equal("pending", await BrowserSession.TextAsync(page, "auth"));
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.e2e.setUpAuthCalls"));
        Assert.Empty(browser.Dialogs);
        browser.AssertOnlyLoopback();
    }

    [Fact(Timeout = Timeout)]
    public async Task WrongTokenIsNotFoundInTheBrowser()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        Uri address = await rig.OpenProxyAsync(cancellationToken);
        IPage page = await browser.OpenAsync(address);

        // Script requests of the logged-in page (same origin, so the browser itself sends them) without the right token.
        int wrongToken = await page.EvaluateAsync<int>($"async () => (await fetch('/{WrongToken}/api/v1/status')).status");
        int noToken = await page.EvaluateAsync<int>("async () => (await fetch('/api/v1/status')).status");
        // Navigation: Chromium shows its own error page for the empty 404.
        PlaywrightException navigation = await Assert.ThrowsAsync<PlaywrightException>(() => page.GotoAsync(new Uri(address, $"/{WrongToken}/").ToString()));

        Assert.Equal(404, wrongToken);
        Assert.Equal(404, noToken);
        Assert.Contains("ERR_HTTP_RESPONSE_CODE_FAILURE", navigation.Message, StringComparison.Ordinal);
        Assert.Contains(browser.Responses, static r => r.Url.EndsWith($"/{WrongToken}/", StringComparison.Ordinal) && r.Status == 404);
        // Only the logged-in page's own status request reached the printer.
        Assert.DoesNotContain(rig.Printer.Firmware.Requests, static r => r.PathAndQuery.Contains(WrongToken, StringComparison.Ordinal));
        Assert.Single(rig.Printer.Firmware.Requests, static r => r.PathAndQuery == "/api/v1/status" && r.Status == HttpStatusCode.Continue);
        browser.AssertOnlyLoopback();
    }

    [Fact(Timeout = Timeout)]
    public async Task WrongTokenAndForeignHostAreRejectedForPlainHttpClients()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        Uri address = await rig.OpenProxyAsync(cancellationToken);
        using HttpClient client = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false });

        using HttpResponseMessage valid = await client.GetAsync(address, cancellationToken);
        using HttpResponseMessage wrongToken = await client.GetAsync(new Uri(address, $"/{WrongToken}/api/v1/status"), cancellationToken);
        using HttpRequestMessage rebinding = new(HttpMethod.Get, new Uri(address, "api/v1/status"));
        // DNS rebinding: a foreign page whose host name resolves to 127.0.0.1 sends its own Host.
        rebinding.Headers.Host = $"attacker.example:{address.Port}";
        using HttpResponseMessage foreignHost = await client.SendAsync(rebinding, cancellationToken);
        using HttpRequestMessage localhost = new(HttpMethod.Get, new Uri(address, "api/v1/status"));
        localhost.Headers.Host = $"localhost:{address.Port}";
        using HttpResponseMessage localhostHost = await client.SendAsync(localhost, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongToken.StatusCode);
        Assert.Equal(HttpStatusCode.MisdirectedRequest, foreignHost.StatusCode);
        Assert.Equal(HttpStatusCode.MisdirectedRequest, localhostHost.StatusCode);
        // Only the valid request reached the printer.
        Assert.Single(rig.Printer.Firmware.Requests, static r => r.PathAndQuery == "/" && r.Status == HttpStatusCode.Continue);
        Assert.DoesNotContain(rig.Printer.Firmware.Requests, static r => r.PathAndQuery.Contains("status", StringComparison.Ordinal));
    }

    [Fact(Timeout = Timeout)]
    public async Task ExternalLinkGetsNoTokenInTheReferer()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ConcurrentQueue<(string Path, string? Referer)> requests = new();
        await using LoopbackServer external = await LoopbackServer.StartAsync(
            context =>
            {
                requests.Enqueue((context.Request.Path.Value ?? string.Empty, context.Request.Headers.Referer.Count == 0 ? null : context.Request.Headers.Referer.ToString()));
                context.Response.ContentType = "text/html; charset=utf-8";
                return context.Response.WriteAsync("<html><body>External</body></html>", context.RequestAborted);
            },
            cancellationToken);
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        Uri address = await rig.OpenProxyAsync(cancellationToken);
        IPage page = await browser.OpenAsync(address);
        string token = address.AbsolutePath.Trim('/');
        string landing = new Uri(external.BaseAddress, "landing").ToString();

        // The web's link to prusa3d.com, pointed to a local "external" site (the tests never leave 127.0.0.1).
        await page.EvaluateAsync("href => { document.getElementById('external').href = href; }", landing);
        await page.Locator("#external").ClickAsync();
        await page.WaitForURLAsync(landing);

        Assert.Equal("External", await page.Locator("body").TextContentAsync());
        Assert.Equal(string.Empty, await page.EvaluateAsync<string>("() => document.referrer"));
        // The browser also asks for /favicon.ico, with the external page as referrer.
        string? referer = Assert.Single(requests, static r => r.Path == "/landing").Referer;
        // Without Referrer-Policy: no-referrer Chromium would send at least the origin of the proxy.
        Assert.True(referer is null, $"The external site got a Referer: {referer?.Replace(token, "<token>", StringComparison.Ordinal)}");
        browser.AssertOnlyLoopback();
    }
}
