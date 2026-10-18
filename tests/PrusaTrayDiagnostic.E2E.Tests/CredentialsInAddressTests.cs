using Microsoft.Playwright;

using PrusaTrayDiagnostic.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.E2E.Tests;

/// <summary>
/// Regression documentation for T-028: the first version opened <c>http://user:password@printer/</c>. Chromium loads the
/// page (it is public), but refuses every <c>fetch</c> relative to a page address with credentials, so the web stays
/// blank. This is why the app opens the web through the local proxy instead.
/// </summary>
public sealed class CredentialsInAddressTests(EdgeFixture edge)
{
    [Fact(Timeout = 120_000)]
    public async Task CredentialsInTheAddressBreakTheWebsFetch()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        UriBuilder withCredentials = new(rig.Printer.BaseAddress)
        {
            UserName = rig.Printer.Firmware.UserName,
            Password = rig.Printer.Firmware.Password,
        };
        IPage page = await browser.NewPageAsync();

        IResponse? response = await page.GotoAsync(withCredentials.Uri.ToString());
        await BrowserSession.WaitUntilReadyAsync(page);

        Assert.Equal(200, response?.Status);
        Assert.Equal("failed", await BrowserSession.TextAsync(page, "auth"));
        Assert.Contains("credentials", await BrowserSession.TextAsync(page, "error"), StringComparison.OrdinalIgnoreCase);
        // The API was never asked: the request could not even be constructed.
        Assert.DoesNotContain(rig.Printer.Firmware.Requests, r => r.PathAndQuery.StartsWith("/api/", StringComparison.Ordinal));
        browser.AssertOnlyLoopback();
    }
}
