using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LayerPeek.E2E.Tests.Infrastructure;

/// <summary>
/// A Kestrel server on 127.0.0.1 with a port chosen by the OS, answering every request with one delegate. Disposing
/// stops it and releases the port, also after a failed test.
/// </summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LoopbackServer(WebApplication app, int port)
    {
        _app = app;
        Port = port;
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");
    }

    public int Port { get; }

    public Uri BaseAddress { get; }

    public static async Task<LoopbackServer> StartAsync(RequestDelegate handler, CancellationToken cancellationToken)
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.Logging.ClearProviders();
        // No console lifetime: a test server must not register Ctrl+C handlers.
        builder.Services.AddSingleton<IHostLifetime, ManualHostLifetime>();
        builder.WebHost.UseKestrelCore().ConfigureKestrel(static options =>
        {
            // Uploads of the browser tests are tens of MB.
            options.Limits.MaxRequestBodySize = null;
            options.Listen(IPAddress.Loopback, 0, static listen => listen.Protocols = HttpProtocols.Http1);
        });

        WebApplication app = builder.Build();
        app.Run(handler);
        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        string bound = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        return new LoopbackServer(app, new Uri(bound).Port);
    }

    public async ValueTask DisposeAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        try
        {
            await _app.StopAsync(timeout.Token);
        }
        finally
        {
            await _app.DisposeAsync();
        }
    }

    private sealed class ManualHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
