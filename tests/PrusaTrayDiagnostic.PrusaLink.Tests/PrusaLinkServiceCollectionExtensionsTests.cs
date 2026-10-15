using Microsoft.Extensions.DependencyInjection;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;

namespace PrusaTrayDiagnostic.PrusaLink.Tests;

public class PrusaLinkServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPrusaLinkProvidesWorkingClientFactory()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ISecretStore, InMemorySecretStore>();
        services.AddPrusaLink();
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        PrusaLinkClientFactory factory = provider.GetRequiredService<PrusaLinkClientFactory>();
        IPrinterClient client = ((IPrinterClientFactory)factory).Create(
            new PrinterDefinition { Id = "mk4s", Name = "MK4S", ConnectionType = ConnectionType.PrusaLink, Address = "10.0.0.7" });

        Assert.IsType<PrusaLinkClient>(client);
        Assert.Equal(TimeSpan.FromSeconds(3), provider.GetRequiredService<IHttpClientFactory>().CreateClient(PrusaLinkClientFactory.HttpClientName).Timeout);
    }

    [Fact]
    public void FactoryRejectsOtherConnectionTypes()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ISecretStore, InMemorySecretStore>();
        services.AddPrusaLink();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<ArgumentException>(() => provider.GetRequiredService<PrusaLinkClientFactory>().Create(
            new PrinterDefinition { Id = "xl", Name = "XL", ConnectionType = ConnectionType.PrusaConnect, Address = "uuid" }));
    }
}
