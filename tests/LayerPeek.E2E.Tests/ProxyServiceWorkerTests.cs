using Microsoft.Playwright;

using LayerPeek.E2E.Tests.Infrastructure;

namespace LayerPeek.E2E.Tests;

/// <summary>
/// T-107 (security finding N1) in the installed Edge: content of the printer (here a tampered <c>sw.js</c> with
/// <c>Service-Worker-Allowed: /</c>) cannot register a Service Worker on the proxy's origin, which would outlive the
/// proxy and answer for whatever listens on the port later. The web itself keeps working under the hardening headers.
/// </summary>
public sealed class ProxyServiceWorkerTests(EdgeFixture edge)
{
    private const int Timeout = 120_000;

    [Fact(Timeout = Timeout)]
    public async Task PrinterContentCannotRegisterAServiceWorker()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        IPage page = await browser.OpenAsync(await rig.OpenProxyAsync(cancellationToken));

        // 127.0.0.1 is a secure context: the API exists, so a rejection comes from the proxy's policy.
        Assert.True(await page.EvaluateAsync<bool>("() => 'serviceWorker' in navigator"));
        string registration = await page.EvaluateAsync<string>(
            "() => navigator.serviceWorker.register('sw.js', { scope: './' }).then(() => 'registered', e => 'rejected: ' + e.name)")
            .WaitAsync(cancellationToken);
        int registrations = await page.EvaluateAsync<int>("async () => (await navigator.serviceWorker.getRegistrations()).length");
        // A plain fetch of the same script passes, without the printer's scope header but with the proxy's policies.
        string[] headers = await page.EvaluateAsync<string[]>(
            """
            async () => {
              const r = await fetch('sw.js', { cache: 'no-store' });
              return [String(r.status), String(r.headers.get('service-worker-allowed')), String(r.headers.get('content-security-policy')),
                String(r.headers.get('x-frame-options')), String(r.headers.get('cross-origin-opener-policy'))];
            }
            """);

        Assert.StartsWith("rejected: ", registration, StringComparison.Ordinal);
        Assert.Equal(0, registrations);
        Assert.Equal(["200", "null", "frame-ancestors 'none'; worker-src 'none'", "DENY", "same-origin"], headers);
        Assert.DoesNotContain(rig.Printer.Firmware.Requests, static r => r.AllHeaders.Contains("Service-Worker:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ready", await page.EvaluateAsync<string>("() => window.e2e.ready ? 'ready' : 'not ready'"));
        Assert.Empty(browser.Dialogs);
        Assert.Empty(browser.ScriptErrors);
        browser.AssertOnlyLoopback();
    }
}
