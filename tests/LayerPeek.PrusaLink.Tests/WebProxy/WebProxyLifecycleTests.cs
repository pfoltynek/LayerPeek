using System.Net;
using System.Net.Sockets;

using LayerPeek.Core.Secrets;
using LayerPeek.PrusaLink.WebProxy;

namespace LayerPeek.PrusaLink.Tests.WebProxy;

public class WebProxyLifecycleTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProxyStartsOnlyOnOpen()
    {
        await using ProxyTestContext context = new();

        Assert.Null(context.Gateway.GetProxyForTests("core-one"));
        Assert.Empty(context.Printer.Requests);

        Uri proxy = await context.OpenAsync();

        Assert.True(await ProxyTestContext.IsListeningAsync(IPAddress.Loopback, proxy.Port));
    }

    [Fact]
    public async Task RepeatedOpenReusesTheProxyAndToken()
    {
        await using ProxyTestContext context = new();

        Uri first = await context.OpenAsync();
        PrusaLinkWebProxy proxy = context.Proxy();
        Uri second = await context.OpenAsync();

        Assert.Equal(first, second);
        Assert.Same(proxy, context.Proxy());
    }

    [Fact]
    public async Task ConcurrentOpensStartOneProxy()
    {
        await using ProxyTestContext context = new();

        Uri[] addresses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => context.OpenAsync()));

        Assert.Single(addresses.Distinct());
    }

    [Fact]
    public async Task IdleProxyStopsAndReleasesItsPort()
    {
        await using ProxyTestContext context = new();
        Uri first = await context.OpenAsync();
        PrusaLinkWebProxy proxy = context.Proxy();

        context.Time.Advance(TimeSpan.FromMinutes(10));

        await proxy.Stopped.WaitAsync(Wait, Ct);
        Assert.Null(context.Gateway.GetProxyForTests("core-one"));
        Assert.False(ProxyTestContext.IsListening(first.Port));
        Uri second = await context.OpenAsync();
        Assert.NotEqual(first.AbsolutePath, second.AbsolutePath);
    }

    [Fact]
    public async Task RequestsPostponeTheIdleStop()
    {
        await using ProxyTestContext context = new();
        Uri address = await context.OpenAsync();
        PrusaLinkWebProxy proxy = context.Proxy();

        context.Time.Advance(TimeSpan.FromMinutes(9));
        using (HttpResponseMessage response = await context.Browser.GetAsync(new Uri(address, "api/v1/status"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await WaitUntilRequestsFinishedAsync(proxy);
        context.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Same(proxy, context.Gateway.GetProxyForTests("core-one"));
        Assert.False(proxy.IsStopping);

        context.Time.Advance(TimeSpan.FromMinutes(8));
        await proxy.Stopped.WaitAsync(Wait, Ct);
        Assert.Null(context.Gateway.GetProxyForTests("core-one"));
    }

    [Fact]
    public async Task ReopeningPostponesTheIdleStop()
    {
        await using ProxyTestContext context = new();
        await context.OpenAsync();
        PrusaLinkWebProxy proxy = context.Proxy();

        context.Time.Advance(TimeSpan.FromMinutes(9));
        await context.OpenAsync();
        context.Time.Advance(TimeSpan.FromMinutes(2));

        Assert.Same(proxy, context.Gateway.GetProxyForTests("core-one"));
    }

    [Fact]
    public async Task RunningRequestPreventsTheIdleStop()
    {
        await using ProxyTestContext context = new();
        Uri address = await context.OpenAsync();
        PrusaLinkWebProxy proxy = context.Proxy();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Printer.Handler = async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        Task<HttpResponseMessage> upload = context.Browser.GetAsync(new Uri(address, "api/v1/files/usb/slow.gcode"), Ct);
        await entered.Task.WaitAsync(Wait, Ct);

        context.Time.Advance(TimeSpan.FromMinutes(11));
        Assert.Same(proxy, context.Gateway.GetProxyForTests("core-one"));

        release.SetResult();
        using (HttpResponseMessage response = await upload.WaitAsync(Wait, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await WaitUntilRequestsFinishedAsync(proxy);
        context.Time.Advance(TimeSpan.FromMinutes(10));
        await proxy.Stopped.WaitAsync(Wait, Ct);
    }

    /// <summary>
    /// The browser gets the response before the proxy's request handler finishes (it marks the activity and leaves the
    /// running-request count in its <c>finally</c>). Moving the fake clock before that would make the idle check see a
    /// running request and postpone the stop by a whole idle timeout, so wait for the handler first.
    /// </summary>
    private static async Task WaitUntilRequestsFinishedAsync(PrusaLinkWebProxy proxy)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(Wait);
        while (proxy.ActiveRequestsForTests != 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }
    }

    [Fact]
    public async Task StopAsyncStopsTheProxyAndTheNextOpenGetsANewAddress()
    {
        await using ProxyTestContext context = new();
        Uri first = await context.OpenAsync();
        PrusaLinkWebProxy proxy = context.Proxy();

        await context.Gateway.StopAsync("CORE-ONE", Ct);

        Assert.True(proxy.Stopped.IsCompleted);
        Assert.False(ProxyTestContext.IsListening(first.Port));
        Uri second = await context.OpenAsync();
        Assert.NotEqual(first.AbsolutePath, second.AbsolutePath);
    }

    [Fact]
    public async Task StopAsyncOfAnUnknownPrinterDoesNothing()
    {
        await using ProxyTestContext context = new();

        await context.Gateway.StopAsync("unknown", Ct);
        await context.Gateway.StopAllAsync(Ct);
    }

    [Theory]
    [InlineData("192.168.1.21", "maker")]
    [InlineData(ProxyTestContext.PrinterAddress, "admin")]
    public async Task ChangedPrinterSettingsReplaceTheProxy(string address, string userName)
    {
        await using ProxyTestContext context = new();
        Uri first = await context.OpenAsync();
        PrusaLinkWebProxy old = context.Proxy();
        context.Printer.UserName = userName;

        Uri second = await context.OpenAsync(ProxyTestContext.Definition(address: address) with { UserName = userName });

        Assert.True(old.Stopped.IsCompleted);
        Assert.NotEqual(first.AbsolutePath, second.AbsolutePath);
        Assert.False(ProxyTestContext.IsListening(first.Port));
    }

    [Fact]
    public async Task StopAllAsyncStopsEveryProxy()
    {
        await using ProxyTestContext context = new();
        await context.Secrets.SetAsync(SecretKeys.ForPrinter("mk4"), context.Printer.Password, Ct);
        Uri coreOne = await context.OpenAsync();
        Uri mk4 = await context.OpenAsync(ProxyTestContext.Definition("mk4", "192.168.1.21"));

        await context.Gateway.StopAllAsync(Ct);

        Assert.False(ProxyTestContext.IsListening(coreOne.Port));
        Assert.False(ProxyTestContext.IsListening(mk4.Port));
    }

    [Fact]
    public async Task DisposeCancelsRunningRequestsAndClosesPorts()
    {
        ProxyTestContext context = new();
        try
        {
            Uri address = await context.OpenAsync();
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Printer.Handler = async (_, ct) =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    throw;
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            };
            Task<HttpResponseMessage> request = context.Browser.GetAsync(new Uri(address, "api/v1/status"), Ct);
            await entered.Task.WaitAsync(Wait, Ct);

            await context.Gateway.DisposeAsync();

            await cancelled.Task.WaitAsync(Wait, Ct);
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => request.WaitAsync(Wait, Ct));
            Assert.False(ProxyTestContext.IsListening(address.Port));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => context.Gateway.OpenAsync(ProxyTestContext.Definition(), Ct));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task SynchronousDisposeClosesPorts()
    {
        await using ProxyTestContext context = new();
        Uri address = await context.OpenAsync();
        PrusaLinkWebProxy proxy = context.Proxy();

        context.Gateway.Dispose();

        Assert.True(proxy.Stopped.IsCompleted);
        Assert.False(ProxyTestContext.IsListening(address.Port));
        await context.Gateway.DisposeAsync();
    }

    [Fact]
    public async Task BrowserDisconnectCancelsThePrinterRequest()
    {
        await using ProxyTestContext context = new();
        Uri address = await context.OpenAsync();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Printer.Handler = async (_, ct) =>
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        using CancellationTokenSource browserGivesUp = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        Task<HttpResponseMessage> request = context.Browser.GetAsync(new Uri(address, "api/v1/status"), browserGivesUp.Token);
        await entered.Task.WaitAsync(Wait, Ct);

        await browserGivesUp.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await cancelled.Task.WaitAsync(Wait, Ct);
        // The proxy keeps serving other requests.
        context.Printer.Handler = null;
        using HttpResponseMessage next = await context.Browser.GetAsync(new Uri(address, "api/v1/status"), Ct);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRequestsDuringStopEnd()
    {
        await using ProxyTestContext context = new();
        Uri address = await context.OpenAsync();
        PrusaLinkWebProxy proxy = context.Proxy();
        int inPrinter = 0;
        int cancelledInPrinter = 0;
        TaskCompletionSource busy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Printer.Handler = async (_, ct) =>
        {
            if (Interlocked.Increment(ref inPrinter) == 3)
            {
                busy.TrySetResult();
            }

            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancelledInPrinter);
                throw;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        Task[] requests = [.. Enumerable.Range(0, 20).Select(async _ =>
        {
            // A connection that the OS queues while the listener closes may stay unanswered (it never reaches the proxy):
            // the client-side timeout ends it, like a browser's would.
            using CancellationTokenSource clientTimeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            clientTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(address, "api/v1/status"), clientTimeout.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or SocketException || (ex is OperationCanceledException && !Ct.IsCancellationRequested))
            {
                // Refused, dropped by the stopping proxy, or timed out. On macOS a connection the closing listener resets right
                // after connect fails in HttpClient with a bare SocketException (EINVAL from getpeername), not wrapped.
            }
        })];
        await busy.Task.WaitAsync(Wait, Ct);

        await context.Gateway.StopAsync("core-one", Ct).WaitAsync(Wait, Ct);

        Assert.True(proxy.Stopped.IsCompleted);
        Assert.False(ProxyTestContext.IsListening(address.Port));
        await Task.WhenAll(requests).WaitAsync(Wait, Ct);
        // Every request that reached the printer was cancelled by the stop.
        Assert.Equal(Volatile.Read(ref inPrinter), Volatile.Read(ref cancelledInPrinter));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopDuringTheOpenOfANewProxyDiscardsIt(bool stopAll)
    {
        await using ProxyTestContext context = new();
        TaskCompletionSource checking = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Printer.BeforeRequest = async () =>
        {
            checking.TrySetResult();
            await release.Task;
        };

        // The settings are saved (StopAsync) while the first click still checks the old login.
        Task<Core.Web.PrinterWebAccess> open = context.Gateway.OpenAsync(ProxyTestContext.Definition(), Ct);
        await checking.Task.WaitAsync(Wait, Ct);
        await (stopAll ? context.Gateway.StopAllAsync(Ct) : context.Gateway.StopAsync("core-one", Ct)).WaitAsync(Wait, Ct);
        release.TrySetResult();
        Core.Web.PrinterWebAccess access = await open.WaitAsync(Wait, Ct);

        Assert.False(access.IsProxied);
        Assert.Equal(Core.Web.PrinterWebFallback.ProxyUnavailable, access.Fallback);
        Assert.Null(context.Gateway.GetProxyForTests("core-one"));
        // The discarded proxy was started and stopped again (its port is closed).
        Assert.Single(context.Logger.Entries, static e => e.Contains("'core-one' started", StringComparison.Ordinal));
        Assert.Single(context.Logger.Entries, static e => e.Contains("'core-one' stopped", StringComparison.Ordinal));

        // The next click starts a fresh proxy with the current settings.
        context.Printer.BeforeRequest = null;
        Uri next = await context.OpenAsync();
        Assert.True(await ProxyTestContext.IsListeningAsync(IPAddress.Loopback, next.Port));
    }

    [Fact]
    public async Task StopDuringTheCheckOfARunningProxyOpensThePlainAddress()
    {
        await using ProxyTestContext context = new();
        Uri first = await context.OpenAsync();
        PrusaLinkWebProxy running = context.Proxy();
        TaskCompletionSource checking = new(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Printer.Override = async (_, ct) =>
        {
            checking.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Not reached.");
        };

        Task<Core.Web.PrinterWebAccess> open = context.Gateway.OpenAsync(ProxyTestContext.Definition(), Ct);
        await checking.Task.WaitAsync(Wait, Ct);
        await context.Gateway.StopAsync("core-one", Ct).WaitAsync(Wait, Ct);
        Core.Web.PrinterWebAccess access = await open.WaitAsync(Wait, Ct);

        Assert.Equal(Core.Web.PrinterWebFallback.ProxyUnavailable, access.Fallback);
        Assert.True(running.Stopped.IsCompleted);
        Assert.Null(context.Gateway.GetProxyForTests("core-one"));
        Assert.False(ProxyTestContext.IsListening(first.Port));
    }

    [Fact]
    public async Task RequestOutlivingTheStopTimeoutIsNotLoggedAsAnError()
    {
        await using ProxyTestContext context = new(PrusaLinkWebProxyOptions.Default with { StopTimeout = TimeSpan.FromMilliseconds(100) });
        Uri address = await context.OpenAsync();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // A printer request that ignores cancellation and then asks for a new nonce: the proxy's retry runs after the
        // stop released its HTTP client.
        context.Printer.Override = async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            HttpResponseMessage stale = new(HttpStatusCode.Unauthorized);
            stale.Headers.TryAddWithoutValidation("WWW-Authenticate", $"Digest realm=\"{FakePrusaLinkServer.Realm}\", nonce=\"renewed\", stale=true");
            return stale;
        };
        Task browser = Task.Run(async () =>
        {
            try
            {
                using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(address, "api/v1/status"), Ct);
            }
            catch (HttpRequestException)
            {
                // Dropped by the stopping proxy.
            }
        }, Ct);
        await entered.Task.WaitAsync(Wait, Ct);

        await context.Gateway.StopAsync("core-one", Ct).WaitAsync(Wait, Ct);
        context.Printer.Override = null;
        release.TrySetResult();
        await browser.WaitAsync(Wait, Ct);
        await WaitForLogAsync(context, "aborted because the proxy stopped");

        Assert.DoesNotContain(context.Logger.Entries, static e => e.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProxyLogsUnderItsOwnCategory()
    {
        await using ProxyTestContext context = new();
        Uri address = await context.OpenAsync();

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(address, "api/v1/status"), Ct);

        // Start and request logs come from the proxy.
        Assert.NotEmpty(context.Logger.Categories);
        Assert.All(context.Logger.Categories, static c => Assert.Equal(typeof(PrusaLinkWebProxy).FullName, c));

        // A fallback (no password stored for this printer) is the gateway's.
        Core.Web.PrinterWebAccess fallback = await context.Gateway.OpenAsync(ProxyTestContext.Definition("mk4", "192.168.1.21"), Ct);
        Assert.Equal(Core.Web.PrinterWebFallback.PasswordMissing, fallback.Fallback);
        Assert.Contains(typeof(PrusaLinkWebGateway).FullName!, context.Logger.Categories);
    }

    [Fact]
    public async Task CancelledOpenOfANewProxyLeavesNothingRunning()
    {
        await using ProxyTestContext context = new();
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        context.Printer.BeforeRequest = async () =>
        {
            await cancel.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancel.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Gateway.OpenAsync(ProxyTestContext.Definition(), cancel.Token));

        Assert.Null(context.Gateway.GetProxyForTests("core-one"));
        Assert.DoesNotContain(context.Logger.Entries, static e => e.Contains("'core-one' started", StringComparison.Ordinal));
        context.Printer.BeforeRequest = null;
        Assert.True((await context.Gateway.OpenAsync(ProxyTestContext.Definition(), Ct)).IsProxied);
    }

    /// <summary>The request handler of a stopped proxy finishes on its own; waits until it logged <paramref name="text"/>.</summary>
    private static async Task WaitForLogAsync(ProxyTestContext context, string text)
    {
        System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
        while (!context.Logger.Entries.Any(e => e.Contains(text, StringComparison.Ordinal)))
        {
            Assert.True(waited.Elapsed < Wait, $"No log entry \"{text}\":\n{context.Logger.AllText}");
            await Task.Delay(10, Ct);
        }
    }
}
