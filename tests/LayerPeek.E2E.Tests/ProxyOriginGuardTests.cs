using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;

using LayerPeek.E2E.Tests.Infrastructure;

namespace LayerPeek.E2E.Tests;

/// <summary>
/// T-051 (security finding L1) in the installed Edge: the PrusaLink web opened through the proxy can still change
/// things on the printer, while another page on a different 127.0.0.1 port that knows the proxy address cannot.
/// </summary>
public sealed class ProxyOriginGuardTests(EdgeFixture edge)
{
    private const int Timeout = 120_000;

    [Fact(Timeout = Timeout)]
    public async Task WriteActionsOfTheProxiedPageReachThePrinter()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        IPage page = await browser.OpenAsync(await rig.OpenProxyAsync(cancellationToken));
        string[] actions =
        [
            "POST /api/v1/files/usb/benchy.gcode",
            "PUT /api/v1/job/42/pause",
            "PUT /api/v1/job/42/resume",
            "DELETE /api/v1/job/42",
            "DELETE /api/v1/files/usb/benchy.gcode",
        ];

        foreach (string action in actions)
        {
            int status = await page.EvaluateAsync<int>("([method, endPoint]) => window.apiCall(method, endPoint)", action.Split(' ')).WaitAsync(cancellationToken);
            Assert.True(status == 204, $"{action} answered {status}.");
        }

        int upload = await page.EvaluateAsync<int>("([name, size]) => window.uploadGenerated(name, size)", new object[] { "small.gcode", 256 * 1024 }).WaitAsync(cancellationToken);

        Assert.Equal(actions, rig.Printer.Commands);
        Assert.Equal(201, upload);
        Assert.True(Assert.Single(rig.Printer.Uploads).ContentMatches);
        browser.AssertCleanLoggedInTraffic();
    }

    [Fact(Timeout = Timeout)]
    public async Task ClassicFormPostOfTheProxiedPageReachesThePrinter()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        Uri address = await rig.OpenProxyAsync(cancellationToken);
        IPage page = await browser.OpenAsync(address);
        string startPrint = new Uri(address, "api/v1/files/usb/form.gcode").ToString();

        // Under Referrer-Policy: no-referrer the browser sends Origin: null with Sec-Fetch-Site: same-origin
        // (decision 2026-10-21: allowed). The printer answers 204, so the page stays where it is.
        IResponse form = await page.RunAndWaitForResponseAsync(
            () => page.EvaluateAsync(
                """
                url => {
                  const form = document.createElement("form");
                  form.method = "POST";
                  form.action = url;
                  document.body.appendChild(form);
                  form.submit();
                }
                """,
                startPrint),
            response => response.Url == startPrint && response.Request.Method == "POST");

        Assert.Equal("null", await form.Request.HeaderValueAsync("origin"));
        Assert.Equal("same-origin", await form.Request.HeaderValueAsync("sec-fetch-site"));
        Assert.Equal(204, form.Status);
        Assert.Equal(["POST /api/v1/files/usb/form.gcode"], rig.Printer.Commands);
        // A navigation answered 204 is cancelled by the browser, which reports it as aborted; nothing else failed.
        Assert.Equal($"POST {startPrint}: net::ERR_ABORTED", Assert.Single(browser.FailedRequests));
        Assert.DoesNotContain(browser.Responses, static r => r.Status == 401 || r.WwwAuthenticate is not null);
        Assert.Empty(browser.Dialogs);
        Assert.Empty(browser.ScriptErrors);
        browser.AssertOnlyLoopback();
    }

    [Fact(Timeout = Timeout)]
    public async Task ForeignPageOnAnotherLoopbackPortCannotWriteThroughTheProxy()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        // The attacker's page: another port on 127.0.0.1 (for the browser "same-site", not "same-origin").
        await using LoopbackServer attacker = await LoopbackServer.StartAsync(
            context =>
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                return context.Response.WriteAsync("<!DOCTYPE html><html><body><iframe name=\"sink\"></iframe></body></html>", context.RequestAborted);
            },
            cancellationToken);
        await using BrowserSession browser = await edge.NewSessionAsync();
        await using PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        Uri address = await rig.OpenProxyAsync(cancellationToken);
        // The user has the web open; the attacker is assumed to know its address with the token.
        await browser.OpenAsync(address);
        IPage page = await browser.NewPageAsync();
        await page.GotoAsync(attacker.BaseAddress.ToString());
        string startPrint = new Uri(address, "api/v1/files/usb/benchy.gcode").ToString();
        string formPrint = new Uri(address, "api/v1/files/usb/form.gcode").ToString();
        string stopJob = new Uri(address, "api/v1/job/42").ToString();
        int firmwareRequests = rig.Printer.Firmware.Requests.Count;

        // A "simple" request: sent without a preflight, the page just cannot read the answer. Cross-Origin-Resource-Policy
        // makes the browser drop even the opaque answer; that failure also shows the request reached the proxy.
        TaskCompletionSource<string?> blindFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRequestFailed(object? sender, IRequest request)
        {
            if (request.Url == startPrint && request.Method == "POST")
            {
                blindFailure.TrySetResult(request.Failure);
            }
        }

        page.RequestFailed += OnRequestFailed;
        string blindResult;
        string? blind;
        try
        {
            blindResult = await page.EvaluateAsync<string>("url => fetch(url, { method: 'POST', mode: 'no-cors', body: 'x' }).then(() => 'sent', () => 'failed')", startPrint);
            blind = await blindFailure.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        finally
        {
            page.RequestFailed -= OnRequestFailed;
        }

        // A form posted into a frame (works without any script on the attacker's side); navigations are not subject to CORP.
        IResponse form = await page.RunAndWaitForResponseAsync(
            () => page.EvaluateAsync(
                """
                url => {
                  const form = document.createElement("form");
                  form.method = "POST";
                  form.action = url;
                  form.target = "sink";
                  document.body.appendChild(form);
                  form.submit();
                }
                """,
                formPrint),
            response => response.Url == formPrint && response.Request.Method == "POST");
        // DELETE needs a CORS preflight, which the printer does not allow: the browser never sends the DELETE.
        string delete = await page.EvaluateAsync<string>("url => fetch(url, { method: 'DELETE' }).then(() => 'sent', () => 'blocked')", stopJob);
        // Cross-Origin-Resource-Policy: the page cannot load the proxy's content as no-cors either.
        string embed = await page.EvaluateAsync<string>("url => fetch(url, { mode: 'no-cors' }).then(() => 'loaded', () => 'blocked')", address.ToString());

        Assert.Equal("failed", blindResult);
        Assert.Equal("net::ERR_BLOCKED_BY_RESPONSE.NotSameOrigin", blind);
        Assert.Equal(403, form.Status);
        Assert.Equal("blocked", delete);
        Assert.Equal("blocked", embed);
        Assert.Empty(rig.Printer.Commands);
        Assert.DoesNotContain(rig.Printer.Firmware.Requests.Skip(firmwareRequests), static r => r.Method is "POST" or "PUT" or "DELETE");
        browser.AssertOnlyLoopback();
    }
}
