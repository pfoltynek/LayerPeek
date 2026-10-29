using System.Collections.Concurrent;

using Microsoft.Playwright;

namespace LayerPeek.E2E.Tests.Infrastructure;

/// <summary>
/// An isolated browser context (own storage, like a fresh browser profile) that records what the browser saw: every
/// response (status, <c>WWW-Authenticate</c>), failed requests, dialogs, script errors and any request that left
/// 127.0.0.1. Disposing closes the context and its pages, also after a failed test.
/// </summary>
internal sealed class BrowserSession : IAsyncDisposable
{
    /// <summary>Playwright actions (navigation, waiting for an element) fail after this instead of hanging.</summary>
    private const float ActionTimeoutMs = 20_000;

    /// <summary>How often <see cref="WaitUntilReadyAsync"/> evaluates its condition.</summary>
    private const float ReadyPollingIntervalMs = 50;

    private readonly IBrowserContext _context;

    private BrowserSession(IBrowserContext context)
    {
        _context = context;
        context.SetDefaultTimeout(ActionTimeoutMs);
        context.SetDefaultNavigationTimeout(ActionTimeoutMs);
        context.Request += (_, request) =>
        {
            if (Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? url) && url.Host != "127.0.0.1")
            {
                NonLoopbackRequests.Enqueue(request.Url);
            }
        };
        context.Response += (_, response) =>
            Responses.Enqueue(new SeenResponse(response.Url, response.Status, response.Headers.GetValueOrDefault("www-authenticate")));
        context.RequestFailed += (_, request) => FailedRequests.Enqueue($"{request.Method} {request.Url}: {request.Failure}");
        context.Dialog += (_, dialog) =>
        {
            Dialogs.Enqueue($"{dialog.Type}: {dialog.Message}");
            _ = dialog.DismissAsync();
        };
        context.Page += (_, page) => page.PageError += (_, error) => ScriptErrors.Enqueue(error);
    }

    public ConcurrentQueue<SeenResponse> Responses { get; } = new();

    public ConcurrentQueue<string> FailedRequests { get; } = new();

    public ConcurrentQueue<string> Dialogs { get; } = new();

    public ConcurrentQueue<string> ScriptErrors { get; } = new();

    public ConcurrentQueue<string> NonLoopbackRequests { get; } = new();

    public static async Task<BrowserSession> StartAsync(IBrowser browser) => new(await browser.NewContextAsync());

    public Task<IPage> NewPageAsync() => _context.NewPageAsync();

    /// <summary>Opens <paramref name="address"/> in a new tab and waits until the fake web finished its start.</summary>
    public async Task<IPage> OpenAsync(Uri address)
    {
        IPage page = await NewPageAsync();
        IResponse? response = await page.GotoAsync(address.ToString());
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        await WaitUntilReadyAsync(page);
        return page;
    }

    /// <summary>
    /// Polls by interval, not by <c>requestAnimationFrame</c> (Playwright's default): headless Edge 156 on macOS was seen
    /// to stop calling rAF on a page that was already ready (T-095, T-021), and the readiness of the fake web does not
    /// depend on rendering. Same condition and timeout as before.
    /// </summary>
    public static async Task WaitUntilReadyAsync(IPage page) =>
        await page.WaitForFunctionAsync("() => window.e2e !== undefined && window.e2e.ready === true", null, new PageWaitForFunctionOptions { PollingInterval = ReadyPollingIntervalMs });

    public static async Task<string> TextAsync(IPage page, string id) => (await page.Locator($"#{id}").TextContentAsync()) ?? string.Empty;

    /// <summary>
    /// The browser never saw a login challenge (it would show its own login dialog and the web would loop in setUpAuth),
    /// no request failed, no dialog or script error appeared and nothing left 127.0.0.1.
    /// </summary>
    public void AssertCleanLoggedInTraffic()
    {
        Assert.DoesNotContain(Responses, static r => r.Status == 401 || r.WwwAuthenticate is not null);
        Assert.Empty(FailedRequests);
        Assert.Empty(Dialogs);
        Assert.Empty(ScriptErrors);
        AssertOnlyLoopback();
    }

    public void AssertOnlyLoopback() => Assert.Empty(NonLoopbackRequests);

    public async ValueTask DisposeAsync() => await _context.CloseAsync();
}

/// <summary>A response as the browser received it.</summary>
internal sealed record SeenResponse(string Url, int Status, string? WwwAuthenticate);
