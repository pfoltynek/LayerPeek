using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.PrusaLink;

namespace PrusaTrayDiagnostic.App.Tests.Composition;

public sealed class PrinterClientFactoryTests : IDisposable
{
    private readonly StubHttpClientFactory _httpClientFactory = new();
    private readonly PrinterClientFactory _factory;

    public PrinterClientFactoryTests() =>
        _factory = new(new PrusaLinkClientFactory(_httpClientFactory, new UnsupportedSecretStore(), TimeProvider.System, NullLogger<PrusaLinkClient>.Instance));

    public void Dispose() => _httpClientFactory.Dispose();

    [Fact]
    public void CreatesPrusaLinkClient()
    {
        IPrinterClient client = _factory.Create(Printer(ConnectionType.PrusaLink));

        Assert.IsType<PrusaLinkClient>(client);
    }

    [Fact]
    public void PrusaConnectIsNotSupportedYet()
    {
        Assert.Throws<NotSupportedException>(() => _factory.Create(Printer(ConnectionType.PrusaConnect)));
    }

    [Fact]
    public void RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => _factory.Create(null!));
    }

    private static PrinterDefinition Printer(ConnectionType type) =>
        new() { Id = "p", Name = "P", ConnectionType = type, Address = "192.168.1.10" };

    /// <summary>Hands out one shared client (never used for requests here) and disposes it with the test.</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient _client = new();

        public HttpClient CreateClient(string name) => _client;

        public void Dispose() => _client.Dispose();
    }
}
