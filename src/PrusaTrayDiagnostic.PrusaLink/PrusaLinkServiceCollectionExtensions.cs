using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PrusaTrayDiagnostic.PrusaLink;

public static class PrusaLinkServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PrusaLinkClientFactory"/> and its HTTP configuration. Requires an
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

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PrusaLinkClientFactory>();
        return services;
    }
}
