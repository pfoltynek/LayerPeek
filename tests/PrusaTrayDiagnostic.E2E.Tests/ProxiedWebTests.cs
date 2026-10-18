using System.Net;

using Microsoft.Extensions.Time.Testing;
using Microsoft.Playwright;

using PrusaTrayDiagnostic.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.E2E.Tests;

/// <summary>
/// The PrusaLink web opened through the local proxy of T-028 in the installed Edge: the web's own JavaScript (fetch and
/// XMLHttpRequest relative to <c>window.location.pathname</c>) works logged in, without a login dialog.
/// </summary>
public sealed class ProxiedWebTests(EdgeFixture edge)
{
    private const int Timeout = 120_000;

    [Fact(Timeout = Timeout)]
    public async Task ProxiedPageShowsPrinterDataWithoutLoginPrompt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        Uri address = await rig.OpenProxyAsync(cancellationToken);

        IPage page = await browser.OpenAsync(address);

        Assert.Equal("logged-in", await BrowserSession.TextAsync(page, "auth"));
        Assert.Equal("prusa-core-one", await BrowserSession.TextAsync(page, "hostname"));
        Assert.Equal("IDLE", await BrowserSession.TextAsync(page, "state"));
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.e2e.setUpAuthCalls"));
        // The page and its script came through the token prefix, the API calls went below it (API_ROOT).
        Assert.Equal(address.AbsolutePath, new Uri(page.Url).AbsolutePath);
        Assert.Contains(browser.Responses, r => r.Url == $"{address}api/v1/info" && r.Status == 200);
        Assert.Contains(browser.Responses, r => r.Url == $"{address}api/v1/status" && r.Status == 200);
        browser.AssertCleanLoggedInTraffic();
        // The proxy answered the printer's Digest challenge itself.
        Assert.Contains(rig.Printer.Firmware.Requests, r => r.PathAndQuery == "/api/v1/info" && r.Authorization?.StartsWith("Digest ", StringComparison.Ordinal) == true);
    }

    [Fact(Timeout = Timeout)]
    public async Task RepeatedOpenReusesTheAddressAndStaysLoggedIn()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        Uri first = await rig.OpenProxyAsync(cancellationToken);
        IPage firstTab = await browser.OpenAsync(first);

        Uri second = await rig.OpenProxyAsync(cancellationToken);
        IPage secondTab = await browser.OpenAsync(second);

        Assert.Equal(first, second);
        Assert.Equal("logged-in", await BrowserSession.TextAsync(secondTab, "auth"));
        // The web keeps polling the status in both tabs.
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal("ok", await firstTab.EvaluateAsync<string>("() => window.refreshStatus()"));
            Assert.Equal("ok", await secondTab.EvaluateAsync<string>("() => window.refreshStatus()"));
        }

        Assert.Equal("IDLE", await BrowserSession.TextAsync(firstTab, "state"));
        browser.AssertCleanLoggedInTraffic();
    }

    [Fact(Timeout = Timeout)]
    public async Task UploadThroughThePageReachesThePrinterIntact()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const int size = 20 * 1024 * 1024;
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        IPage page = await browser.OpenAsync(await rig.OpenProxyAsync(cancellationToken));

        int status = await page.EvaluateAsync<int>("([name, size]) => window.uploadGenerated(name, size)", new object[] { "benchy 0.4n.bgcode", size }).WaitAsync(cancellationToken);

        Assert.Equal(201, status);
        ReceivedUpload upload = Assert.Single(rig.Printer.Uploads);
        Assert.Equal("benchy 0.4n.bgcode", upload.Name);
        Assert.Equal(size, upload.Length);
        Assert.Equal(size, upload.ContentLength);
        Assert.True(upload.ContentMatches, "The printer received different bytes than the page sent.");
        Assert.Equal("?1", upload.Overwrite);
        Assert.Equal("?1", upload.PrintAfterUpload);
        // HA2 of the upload is computed with PUT, like the firmware does.
        Assert.Contains(rig.Printer.Firmware.Requests, r => r.Method == "PUT" && r.Status == HttpStatusCode.Continue && r.Authorization is not null);
        browser.AssertCleanLoggedInTraffic();
    }

    [Fact(Timeout = Timeout)]
    public async Task ExpiredNonceIsRenewedWithoutLoginPrompt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken, time: time);
        IPage page = await browser.OpenAsync(await rig.OpenProxyAsync(cancellationToken));

        // A small request with an expired nonce: the printer answers stale=true and the proxy repeats it once.
        rig.Printer.Firmware.ExpireNonce();
        int unauthorizedBefore = rig.Printer.Firmware.UnauthorizedCount;
        Assert.Equal("ok", await page.EvaluateAsync<string>("() => window.refreshStatus()"));
        Assert.Equal(unauthorizedBefore + 1, rig.Printer.Firmware.UnauthorizedCount);

        // A streamed upload cannot be repeated: the proxy renews a nonce older than 240 s before sending it.
        time.Advance(TimeSpan.FromSeconds(250));
        rig.Printer.Firmware.ExpireNonce();
        int status = await page.EvaluateAsync<int>("([name, size]) => window.uploadGenerated(name, size)", new object[] { "after-expiry.gcode", 1024 * 1024 }).WaitAsync(cancellationToken);

        Assert.Equal(201, status);
        Assert.True(Assert.Single(rig.Printer.Uploads).ContentMatches);
        Assert.Equal("IDLE", await BrowserSession.TextAsync(page, "state"));
        browser.AssertCleanLoggedInTraffic();
    }

    [Fact(Timeout = Timeout)]
    public async Task PasswordChangedOnThePrinterShowsAnErrorInsteadOfALoginLoop()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        IPage page = await browser.OpenAsync(await rig.OpenProxyAsync(cancellationToken));

        // The password changes on the printer while the tab is open (decision T-028 Q3: 403 with a firmware-like error).
        rig.Printer.Firmware.Password = "changed-on-the-printer";
        string result = await page.EvaluateAsync<string>("() => window.refreshStatus()");

        Assert.Equal("error 403", result);
        Assert.Contains("rejected", await BrowserSession.TextAsync(page, "error"), StringComparison.Ordinal);
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.e2e.setUpAuthCalls"));
        Assert.Contains(browser.Responses, r => r.Url.EndsWith("/api/v1/status", StringComparison.Ordinal) && r.Status == 403);
        browser.AssertCleanLoggedInTraffic();
    }
}
