using Microsoft.Extensions.DependencyInjection;

using LayerPeek.Core.Printers;
using LayerPeek.Core.Secrets;
using LayerPeek.Core.Web;
using LayerPeek.PrusaLink;
using LayerPeek.PrusaLink.Tests;

namespace LayerPeek.E2E.Tests.Infrastructure;

/// <summary>
/// One fake printer and the real web gateway of the app, composed like the app does it (<c>AddPrusaLink()</c> with the
/// real named <see cref="HttpClient"/> towards the printer) with an in-memory <see cref="ISecretStore"/>. Disposing
/// stops the proxies and the printer and releases every port, also after a failed test.
/// </summary>
internal sealed class PrinterRig : IAsyncDisposable
{
    public const string PrinterId = "e2e-core-one";

    private readonly ServiceProvider _services;

    private PrinterRig(FakePrinterSite printer, ServiceProvider services)
    {
        Printer = printer;
        _services = services;
        Gateway = services.GetRequiredService<IPrinterWebGateway>();
    }

    public FakePrinterSite Printer { get; }

    public IPrinterWebGateway Gateway { get; }

    public PrinterDefinition Definition => new()
    {
        Id = PrinterId,
        Name = "Core One (E2E)",
        ConnectionType = ConnectionType.PrusaLink,
        Address = Printer.Address,
    };

    /// <param name="cancellationToken">Cancels the start (the test's token).</param>
    /// <param name="storedPassword">Password in the secret store; the printer's own password when omitted.</param>
    /// <param name="time">Replaces <see cref="TimeProvider.System"/> of the gateway (nonce age, idle timeout).</param>
    public static async Task<PrinterRig> StartAsync(CancellationToken cancellationToken, string? storedPassword = null, TimeProvider? time = null)
    {
        FakePrinterSite printer = await FakePrinterSite.StartAsync(cancellationToken);
        try
        {
            InMemorySecretStore secrets = new();
            await secrets.SetAsync(SecretKeys.ForPrinter(PrinterId), storedPassword ?? printer.Firmware.Password, cancellationToken);
            ServiceCollection services = new();
            services.AddLogging();
            services.AddSingleton<ISecretStore>(secrets);
            if (time is not null)
            {
                // AddPrusaLink adds TimeProvider.System only when no TimeProvider is registered.
                services.AddSingleton(time);
            }

            services.AddPrusaLink();
            return new PrinterRig(printer, services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        }
        catch
        {
            await printer.DisposeAsync();
            throw;
        }
    }

    public Task<PrinterWebAccess> OpenAsync(CancellationToken cancellationToken) => Gateway.OpenAsync(Definition, cancellationToken);

    /// <summary>Opens the proxy and asserts that it is used.</summary>
    public async Task<Uri> OpenProxyAsync(CancellationToken cancellationToken)
    {
        PrinterWebAccess access = await OpenAsync(cancellationToken);
        Assert.True(access.IsProxied, $"Expected a proxy address, got fallback {access.Fallback}.");
        return access.Address!;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Disposes the gateway, which stops every proxy (ports, HTTP clients).
            await _services.DisposeAsync();
        }
        finally
        {
            await Printer.DisposeAsync();
        }
    }
}
