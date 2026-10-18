using System.Net;

using Microsoft.Extensions.DependencyInjection;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.Core.Web;
using PrusaTrayDiagnostic.PrusaLink.WebProxy;

namespace PrusaTrayDiagnostic.PrusaLink.Tests.WebProxy;

/// <summary>The login check before the browser is opened and the plain-address fallbacks (T-028 Q3).</summary>
public class PrusaLinkWebGatewayTests
{
    private static readonly Uri PlainAddress = new("http://192.168.1.20/");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PrusaConnectPrinterIsNotSupported()
    {
        await using ProxyTestContext context = new();

        PrinterWebAccess access = await context.Gateway.OpenAsync(
            new PrinterDefinition { Id = "xl", Name = "XL", ConnectionType = ConnectionType.PrusaConnect, Address = "c0ffee" }, Ct);

        Assert.Equal(PrinterWebFallback.NotSupported, access.Fallback);
        Assert.Null(access.Address);
        Assert.Empty(context.Printer.Requests);
    }

    [Theory]
    [InlineData("http://user:pw@192.168.1.20/")]
    [InlineData("ftp://192.168.1.20/")]
    [InlineData("http://192.168.1.20/?q=1")]
    public async Task InvalidAddressIsNotOpened(string address)
    {
        await using ProxyTestContext context = new();

        PrinterWebAccess access = await context.Gateway.OpenAsync(ProxyTestContext.Definition(address: address), Ct);

        Assert.Equal(PrinterWebFallback.InvalidAddress, access.Fallback);
        Assert.Null(access.Address);
        Assert.Empty(context.Printer.Requests);
    }

    [Fact]
    public async Task MissingPasswordOpensThePlainAddress()
    {
        await using ProxyTestContext context = new();
        await context.Secrets.DeleteAsync(SecretKeys.ForPrinter("core-one"), Ct);

        await AssertFallbackAsync(context, PrinterWebFallback.PasswordMissing);
    }

    [Fact]
    public async Task RejectedPasswordOpensThePlainAddress()
    {
        await using ProxyTestContext context = new();
        context.Printer.Password = "changed-on-printer";

        await AssertFallbackAsync(context, PrinterWebFallback.PasswordRejected);
    }

    [Fact]
    public async Task UnreadablePasswordOpensThePlainAddress()
    {
        await using ProxyTestContext context = new(secrets: new ThrowingSecretStore());

        await AssertFallbackAsync(context, PrinterWebFallback.PasswordUnreadable);
    }

    [Fact]
    public async Task OfflinePrinterOpensThePlainAddress()
    {
        await using ProxyTestContext context = new();
        context.Printer.Override = static (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (192.168.1.20:80)");

        await AssertFallbackAsync(context, PrinterWebFallback.NotReachable);
    }

    [Fact]
    public async Task SilentPrinterOpensThePlainAddressAfterTheCheckTimeout()
    {
        await using ProxyTestContext context = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Printer.Override = async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Unreachable.");
        };

        Task<PrinterWebAccess> open = context.Gateway.OpenAsync(ProxyTestContext.Definition(), Ct);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        context.Time.Advance(PrusaLinkWebProxyOptions.Default.CheckTimeout);
        PrinterWebAccess access = await open.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Equal(PrinterWebFallback.NotReachable, access.Fallback);
        Assert.Equal(PlainAddress, access.Address);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, PrinterWebFallback.AccessDenied)]
    [InlineData(HttpStatusCode.NotFound, PrinterWebFallback.UnexpectedResponse)]
    [InlineData(HttpStatusCode.InternalServerError, PrinterWebFallback.UnexpectedResponse)]
    public async Task UnexpectedVersionAnswerOpensThePlainAddress(HttpStatusCode status, PrinterWebFallback expected)
    {
        await using ProxyTestContext context = new();
        context.Printer.Route("/api/version", status, "{}");

        await AssertFallbackAsync(context, expected);
    }

    [Fact]
    public async Task UnsupportedAuthenticationOpensThePlainAddress()
    {
        await using ProxyTestContext context = new();
        context.Printer.ChallengeScheme = "ApiKey";

        await AssertFallbackAsync(context, PrinterWebFallback.UnsupportedAuthentication);
    }

    [Fact]
    public async Task ProxyThatCannotStartOpensThePlainAddress()
    {
        // 192.0.2.1 (TEST-NET-1) is never an address of this machine, so binding fails locally.
        await using ProxyTestContext context = new(new PrusaLinkWebProxyOptions { ListenAddress = IPAddress.Parse("192.0.2.1") });

        await AssertFallbackAsync(context, PrinterWebFallback.ProxyUnavailable);
        Assert.Contains(context.Logger.Entries, static e => e.StartsWith("Warning", StringComparison.Ordinal) && e.Contains("could not be started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PrinterWithoutLoginIsProxiedWithoutReadingThePassword()
    {
        await using ProxyTestContext context = new();
        context.Printer.IsPublicPath = static _ => true;

        Uri proxy = await context.OpenAsync();

        Assert.Equal("127.0.0.1", proxy.Host);
        Assert.Equal(0, context.Secrets.ReadCount);
    }

    [Fact]
    public async Task FailedCheckKeepsARunningProxy()
    {
        await using ProxyTestContext context = new();
        Uri first = await context.OpenAsync();
        context.Printer.Override = static (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");

        PrinterWebAccess access = await context.Gateway.OpenAsync(ProxyTestContext.Definition(), Ct);
        context.Printer.Override = null;

        Assert.Equal(PrinterWebFallback.NotReachable, access.Fallback);
        Assert.Equal(first, await context.OpenAsync());
    }

    [Fact]
    public async Task CancelledOpenThrows()
    {
        await using ProxyTestContext context = new();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Gateway.OpenAsync(ProxyTestContext.Definition(), cancelled.Token));
        Assert.Null(context.Gateway.GetProxyForTests("core-one"));
    }

    [Fact]
    public async Task ProxyUsesItsOwnHttpClientConfiguration()
    {
        FakePrusaLinkServer printer = new();
        FakeHttpClientFactory factory = new(printer);
        InMemorySecretStore secrets = new();
        await secrets.SetAsync(SecretKeys.ForPrinter("core-one"), printer.Password, Ct);
        printer.RouteFixture("/api/version", "version");
        await using PrusaLinkWebGateway gateway = new(factory, secrets, TimeProvider.System, new CapturingLoggerFactory());

        PrinterWebAccess access = await gateway.OpenAsync(ProxyTestContext.Definition(), Ct);

        Assert.True(access.IsProxied);
        Assert.Equal([PrusaLinkWebGateway.HttpClientName], factory.Names);
        printer.Dispose();
    }

    [Fact]
    public async Task AddPrusaLinkRegistersTheGatewayWithLimitedConnections()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ISecretStore, InMemorySecretStore>();
        services.AddPrusaLink();
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        IPrinterWebGateway gateway = provider.GetRequiredService<IPrinterWebGateway>();

        Assert.Same(provider.GetRequiredService<PrusaLinkWebGateway>(), gateway);
        Assert.Equal(Timeout.InfiniteTimeSpan, provider.GetRequiredService<IHttpClientFactory>().CreateClient(PrusaLinkWebGateway.HttpClientName).Timeout);
        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(PrusaLinkWebGateway.HttpClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler;
        }

        SocketsHttpHandler sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.Equal(2, sockets.MaxConnectionsPerServer);
        Assert.True(sockets.PooledConnectionIdleTimeout < TimeSpan.FromSeconds(60));
        Assert.False(sockets.AllowAutoRedirect);
        Assert.False(sockets.UseCookies);
        Assert.False(sockets.UseProxy);
    }

    [Fact]
    public async Task SynchronousContainerDisposeStopsTheGateway()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ISecretStore, InMemorySecretStore>();
        services.AddPrusaLink();
        ServiceProvider provider = services.BuildServiceProvider();
        IPrinterWebGateway gateway = provider.GetRequiredService<IPrinterWebGateway>();

        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => gateway.OpenAsync(ProxyTestContext.Definition(), Ct));
    }

    private static async Task AssertFallbackAsync(ProxyTestContext context, PrinterWebFallback expected)
    {
        PrinterWebAccess access = await context.Gateway.OpenAsync(ProxyTestContext.Definition(), Ct);

        Assert.Equal(expected, access.Fallback);
        Assert.False(access.IsProxied);
        Assert.Equal(PlainAddress, access.Address);
        Assert.Null(context.Gateway.GetProxyForTests("core-one"));
    }

    private sealed class ThrowingSecretStore : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken) => throw new SecretStoreException("The credential store is locked.");

        public Task SetAsync(string key, string secret, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
