using System.Net;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Printers;
using LayerPeek.Core.Secrets;

namespace LayerPeek.PrusaLink.Tests;

/// <summary><see cref="PrusaLinkClient.DetectModelAsync"/> and <see cref="PrusaLinkPrinterInfo.ModelCode"/> (T-039).</summary>
public sealed class PrusaLinkModelDetectionTests : IDisposable
{
    private const string VersionPath = "/api/version";

    private readonly FakePrusaLinkServer _server = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly HttpClient _httpClient;

    public PrusaLinkModelDetectionTests()
    {
        _httpClient = new HttpClient(_server);
        _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), _server.Password, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _httpClient.Dispose();

    [Fact]
    public async Task FirmwareFixtureReportsCoreOne()
    {
        _server.RouteFixture(VersionPath, "version");

        PrinterModelDetection detection = await CreateClient().DetectModelAsync(Ct);

        Assert.Equal(PrinterModelDetectionStatus.Detected, detection.Status);
        Assert.Equal(new PrinterModelCode(7, 1, 0), detection.Code);
        Assert.Equal(PrinterModel.CoreOne, PrinterModels.FromCode(detection.Code!.Value));
    }

    [Fact]
    public async Task DetectionAsksOnlyTheVersionEndpoint()
    {
        _server.RouteFixture(VersionPath, "version");

        await CreateClient().DetectModelAsync(Ct);

        // The first request is answered by the digest challenge, the second one is the authorized request.
        Assert.All(_server.Requests, static r => Assert.Equal(VersionPath, r.PathAndQuery));
        Assert.Single(_server.Requests, static r => r.Status == HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("1.4.1", 1, 4, 1)]
    [InlineData("1.3.10", 1, 3, 10)]
    [InlineData("3.1.1", 3, 1, 1)]
    public async Task ReportedCodeIsParsed(string printer, int type, int version, int subversion)
    {
        _server.Route(VersionPath, HttpStatusCode.OK, $$"""{"api":"2.0.0","firmware":"6.10.3+1","printer":"{{printer}}"}""");

        PrinterModelDetection detection = await CreateClient().DetectModelAsync(Ct);

        Assert.Equal(PrinterModelDetection.Detected(new PrinterModelCode(type, version, subversion)), detection);
    }

    [Fact]
    public async Task FirmwareBefore65ReportsNoModel()
    {
        _server.Route(VersionPath, HttpStatusCode.OK, """{"api":"2.0.0","server":"2.1.2","nozzle_diameter":0.4,"text":"PrusaLink","hostname":"prusa-mk4","capabilities":{"upload-by-put":true}}""");

        PrinterModelDetection detection = await CreateClient().DetectModelAsync(Ct);

        Assert.Equal(PrinterModelDetection.NotReported, detection);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"MK4\"")]
    [InlineData("\"1.4\"")]
    [InlineData("\"1.4.0.0\"")]
    [InlineData("null")]
    public async Task MalformedCodeCountsAsNotReported(string printer)
    {
        _server.Route(VersionPath, HttpStatusCode.OK, $$"""{"api":"2.0.0","printer":{{printer}}}""");

        PrinterModelDetection detection = await CreateClient().DetectModelAsync(Ct);

        Assert.Equal(PrinterModelDetection.NotReported, detection);
    }

    [Fact]
    public async Task RejectedPasswordFails()
    {
        _server.RouteFixture(VersionPath, "version");
        await _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), "wrong", Ct);

        PrinterModelDetection detection = await CreateClient().DetectModelAsync(Ct);

        Assert.Equal(PrinterModelDetection.Failed, detection);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ErrorStatusFails(HttpStatusCode status)
    {
        _server.Route(VersionPath, status);

        PrinterModelDetection detection = await CreateClient().DetectModelAsync(Ct);

        Assert.Equal(PrinterModelDetection.Failed, detection);
    }

    [Fact]
    public async Task UnreachablePrinterFails()
    {
        _server.Override = static (_, _) => throw new HttpRequestException("connection to 192.168.1.20 refused");

        PrinterModelDetection detection = await CreateClient().DetectModelAsync(Ct);

        Assert.Equal(PrinterModelDetection.Failed, detection);
    }

    [Fact]
    public async Task MalformedJsonFails()
    {
        _server.Route(VersionPath, HttpStatusCode.OK, "{not json");

        PrinterModelDetection detection = await CreateClient().DetectModelAsync(Ct);

        Assert.Equal(PrinterModelDetection.Failed, detection);
    }

    [Fact]
    public async Task InvalidAddressFailsWithoutNetworkAccess()
    {
        PrinterModelDetection detection = await CreateClient("ftp://printer").DetectModelAsync(Ct);

        Assert.Equal(PrinterModelDetection.Failed, detection);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        _server.RouteFixture(VersionPath, "version");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient().DetectModelAsync(new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task ConnectionTestInfoExposesParsedCode()
    {
        _server.RouteFixture(VersionPath, "version");
        _server.RouteFixture("/api/v1/info", "info");

        PrusaLinkInfoResult result = await CreateClient().GetInfoAsync(Ct);

        Assert.Equal(new PrinterModelCode(7, 1, 0), result.Info!.ModelCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    public void InfoWithoutValidCodeHasNoModelCode(string? printerModel)
    {
        PrusaLinkPrinterInfo info = new("host", null, "2.0.0", printerModel, 0.4, HasMmu: false);

        Assert.Null(info.ModelCode);
    }

    private PrusaLinkClient CreateClient(string address = "192.168.1.20") =>
        new(
            _httpClient,
            new PrinterDefinition { Id = "core-one", Name = "Core One", ConnectionType = ConnectionType.PrusaLink, Address = address },
            _secrets,
            _time,
            NullLogger<PrusaLinkClient>.Instance);
}
