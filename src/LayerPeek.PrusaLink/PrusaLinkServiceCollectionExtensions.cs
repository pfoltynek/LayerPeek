using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using LayerPeek.Core.Secrets;
using LayerPeek.Core.Web;
using LayerPeek.PrusaLink.WebProxy;

namespace LayerPeek.PrusaLink;

public static class PrusaLinkServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PrusaLinkClientFactory"/>, the web gateway (<see cref="IPrinterWebGateway"/>, T-028), the
    /// G-code preview fetcher (<see cref="PrusaLinkPreviewFetcher"/>, T-086; the app puts a
    /// <see cref="Core.Previews.PrintPreviewCache"/> in front of it) and their HTTP configurations. Requires an
    /// <see cref="Core.Secrets.ISecretStore"/> registration (platform specific, done by the app).
    /// </summary>
    public static IServiceCollection AddPrusaLink(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(PrusaLinkClientFactory.HttpClientName, static client =>
            {
                // Per request. A poll is usually one request; challenge renewal and a new job add a few more, and the
                // monitor's own poll timeout cuts off the rare worst case. A healthy printer answers within milliseconds.
                client.Timeout = TimeSpan.FromSeconds(3);
                // Real bodies are below 2 kB; a wrong address must not make the app buffer large responses.
                client.MaxResponseContentBufferSize = 64 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                // The printer serves at most 3 requests at once; one keep-alive connection per printer is plenty.
                // (Printers behind one reverse proxy share this connection; see docs/api/prusalink.md.)
                MaxConnectionsPerServer = 1,
                // Below the firmware's 60 s idle timeout, so a connection closed by the printer is never reused.
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(50),
                // Picks up a changed IP of a host name (e.g. mDNS) without recreating the handler.
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                ConnectTimeout = TimeSpan.FromSeconds(3),
                AllowAutoRedirect = false,
                UseCookies = false,
                // Printers live in the local network; a system proxy would only add latency or break access.
                UseProxy = false,
                AutomaticDecompression = System.Net.DecompressionMethods.None,
            })
            // Connection lifetime is handled above; rotating handlers would only drop warm connections.
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            // Per-request logging is not needed and costs allocations on every poll.
            .RemoveAllLoggers();

        // Web proxy towards the printer (T-028): bodies are streamed (uploads of hundreds of MB), so there is no total
        // timeout; a browser that gives up cancels the request.
        services.AddHttpClient(PrusaLinkWebGateway.HttpClientName, static client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                // The firmware serves at most 3 requests at once and monitoring uses one; further browser requests queue.
                MaxConnectionsPerServer = 2,
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(50),
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                ConnectTimeout = TimeSpan.FromSeconds(5),
                // Redirects, cookies and compression are the browser's business: everything passes through unchanged.
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                AutomaticDecompression = System.Net.DecompressionMethods.None,
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers();

        // G-code previews (T-086): a separate pool, so that a download never queues a monitoring poll behind it. The
        // fetcher enforces its own total timeout (headers and streamed body), hence none here.
        services.AddHttpClient(PrusaLinkPreviewFetcher.HttpClientName, static client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                // Previews are rare (about one per job and size); one connection keeps the printer's 3-request budget.
                MaxConnectionsPerServer = 1,
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(50),
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                ConnectTimeout = TimeSpan.FromSeconds(3),
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                AutomaticDecompression = System.Net.DecompressionMethods.None,
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PrusaLinkClientFactory>();
        services.TryAddSingleton(static sp => new PrusaLinkPreviewFetcher(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<PrusaLinkPreviewFetcher>>()));
        services.TryAddSingleton(static sp => new PrusaLinkWebGateway(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILoggerFactory>()));
        services.TryAddSingleton<IPrinterWebGateway>(static sp => sp.GetRequiredService<PrusaLinkWebGateway>());
        return services;
    }
}
