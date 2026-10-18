using System.Net;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.PrusaLink;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Diagnostics;

public sealed class PrinterInfoSourceTests : IDisposable
{
    private readonly FakeHandler _handler = new();
    private readonly PrinterInfoSource _source;

    public PrinterInfoSourceTests() =>
        _source = new PrinterInfoSource(new PrusaLinkClientFactory(new FakeHttpClientFactory(_handler), new EmptySecretStore(), TimeProvider.System, NullLogger<PrusaLinkClient>.Instance));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _handler.Dispose();

    [Fact]
    public async Task PrusaLinkPrinterIsAskedForVersionAndInfo()
    {
        _handler.Responses["/api/version"] = """{"api":"2.0.0","hostname":"prusa-core-one","firmware":"6.9.2+10123","printer":"7.0.0","nozzle_diameter":0.4}""";
        _handler.Responses["/api/v1/info"] = """{"mmu":true,"hostname":"prusa-core-one","nozzle_diameter":0.4}""";

        PrusaLinkInfoResult? result = await _source.GetInfoAsync(Definition(), Ct);

        Assert.NotNull(result);
        Assert.Equal(PrinterState.Idle, result.State);
        Assert.Equal("6.9.2+10123", result.Info?.Firmware);
        Assert.Equal("7.0.0", result.Info?.PrinterModel);
        Assert.True(result.Info?.HasMmu);
        Assert.Equal(["/api/version", "/api/v1/info"], _handler.Paths);
    }

    [Fact]
    public async Task UnreachablePrusaLinkPrinterIsReportedNotThrown()
    {
        PrusaLinkInfoResult? result = await _source.GetInfoAsync(Definition(), Ct);

        Assert.Equal(PrinterState.Offline, result?.State);
        Assert.Null(result?.Info);
    }

    [Fact]
    public async Task PrusaConnectHasNoInfo()
    {
        PrusaLinkInfoResult? result = await _source.GetInfoAsync(Definition() with { ConnectionType = ConnectionType.PrusaConnect, Address = "uuid" }, Ct);

        Assert.Null(result);
        Assert.Empty(_handler.Paths);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Lock _lock = new();
        private readonly List<string> _paths = [];

        public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Paths
        {
            get
            {
                lock (_lock)
                {
                    return [.. _paths];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            lock (_lock)
            {
                _paths.Add(path);
            }

            if (!Responses.TryGetValue(path, out string? body))
            {
                // No network in tests: behave like a printer that is switched off.
                throw new HttpRequestException("connection refused");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class EmptySecretStore : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task SetAsync(string key, string secret, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
