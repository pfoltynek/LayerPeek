using System.Net;
using System.Net.Sockets;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;

namespace PrusaTrayDiagnostic.PrusaLink.Tests;

public sealed class PrusaLinkClientTests : IDisposable
{
    private const string StatusPath = "/api/v1/status";
    private const string JobPath = "/api/v1/job";

    private readonly FakePrusaLinkServer _server = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly HttpClient _httpClient;
    private readonly PrusaLinkClient _client;

    public PrusaLinkClientTests()
    {
        _httpClient = new HttpClient(_server);
        _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), _server.Password, CancellationToken.None).GetAwaiter().GetResult();
        _client = CreateClient("192.168.1.20");
        _server.RouteFixture(StatusPath, "status-idle");
        _server.Route(JobPath, HttpStatusCode.NoContent);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _httpClient.Dispose();

    [Fact]
    public async Task IdlePrinterMapsStateAndTelemetry()
    {
        PrinterSnapshot snapshot = await _client.GetSnapshotAsync(Ct);

        Assert.Equal("core-one", snapshot.PrinterId);
        Assert.Equal(PrinterState.Idle, snapshot.State);
        Assert.Equal(_time.GetUtcNow(), snapshot.Timestamp);
        Assert.Null(snapshot.Job);
        Assert.Null(snapshot.StatusMessage);
        Telemetry telemetry = Assert.IsType<Telemetry>(snapshot.Telemetry);
        Assert.Equal(new Temperature(24.0, 0.0), telemetry.Nozzle);
        Assert.Equal(new Temperature(23.1, 0.0), telemetry.Bed);
        Assert.Equal(5.0, telemetry.AxisZ);
        Assert.Equal(100, telemetry.SpeedPercent);
        Assert.Equal(100, telemetry.FlowPercent);
        Assert.Equal(0, telemetry.HotendFanRpm);
        Assert.Equal(0, telemetry.PrintFanRpm);
    }

    [Fact]
    public async Task IdlePrinterNeedsOnlyTheStatusRequest()
    {
        await _client.GetSnapshotAsync(Ct);
        await _client.GetSnapshotAsync(Ct);

        Assert.DoesNotContain(_server.Requests, static r => r.PathAndQuery == JobPath);
    }

    [Theory]
    [InlineData("status-busy", PrinterState.Busy)]
    [InlineData("status-printing", PrinterState.Printing)]
    [InlineData("status-paused", PrinterState.Paused)]
    [InlineData("status-attention", PrinterState.Attention)]
    [InlineData("status-finished", PrinterState.Finished)]
    [InlineData("status-stopped", PrinterState.Stopped)]
    [InlineData("status-error", PrinterState.Error)]
    public async Task FirmwareStatesAreMapped(string fixture, PrinterState expected)
    {
        _server.RouteFixture(StatusPath, fixture);
        _server.RouteFixture(JobPath, "job-printing");

        Assert.Equal(expected, (await _client.GetSnapshotAsync(Ct)).State);
    }

    [Theory]
    [InlineData("READY", PrinterState.Idle)]
    [InlineData("printing", PrinterState.Printing)]
    [InlineData("UNKNOWN", PrinterState.Unknown)]
    [InlineData("SOMETHING_NEW", PrinterState.Unknown)]
    [InlineData("", PrinterState.Unknown)]
    public async Task StateNamesAreMappedCaseInsensitivelyAndUnknownValuesTolerated(string state, PrinterState expected)
    {
        _server.Route(StatusPath, HttpStatusCode.OK, $$$"""{"printer":{"state":"{{{state}}}","temp_nozzle":20.0}}""");

        Assert.Equal(expected, (await _client.GetSnapshotAsync(Ct)).State);
    }

    [Fact]
    public async Task PrintingJobIsMappedWithFileNameFromJobEndpoint()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.RouteFixture(JobPath, "job-printing");

        PrinterSnapshot snapshot = await _client.GetSnapshotAsync(Ct);

        PrintJob job = Assert.IsType<PrintJob>(snapshot.Job);
        Assert.Equal("benchy_0.4n_0.2mm_PLA_COREONE.bgcode", job.FileName);
        Assert.Equal(37.0, job.ProgressPercent);
        Assert.Equal(TimeSpan.FromSeconds(3120), job.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(1800), job.Elapsed);
        Assert.Equal(new Temperature(215.1, 215.0), snapshot.Telemetry!.Nozzle);
        Assert.Equal(110, snapshot.Telemetry.SpeedPercent);
        Assert.Equal(95, snapshot.Telemetry.FlowPercent);
        Assert.Equal(7200, snapshot.Telemetry.HotendFanRpm);
        Assert.Equal(4900, snapshot.Telemetry.PrintFanRpm);
    }

    [Fact]
    public async Task JobDetailsAreFetchedOnlyWhenTheJobChanges()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.RouteFixture(JobPath, "job-printing");

        await _client.GetSnapshotAsync(Ct);
        await _client.GetSnapshotAsync(Ct);
        _server.RouteFixture(StatusPath, "status-paused");
        PrinterSnapshot paused = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(1, _server.Requests.Count(static r => r.PathAndQuery == JobPath && r.Status == HttpStatusCode.OK));
        Assert.Equal("benchy_0.4n_0.2mm_PLA_COREONE.bgcode", paused.Job!.FileName);
        Assert.Equal(52.0, paused.Job.ProgressPercent);

        _server.RouteFixture(StatusPath, "status-printing-next-job");
        _server.RouteFixture(JobPath, "job-next");
        PrinterSnapshot next = await _client.GetSnapshotAsync(Ct);

        Assert.Equal("calibration-cube.bgcode", next.Job!.FileName);
        Assert.Equal(2, _server.Requests.Count(static r => r.PathAndQuery == JobPath && r.Status == HttpStatusCode.OK));
    }

    [Fact]
    public async Task MissingTimeRemainingIsUnknown()
    {
        _server.RouteFixture(StatusPath, "status-printing-no-estimate");
        _server.RouteFixture(JobPath, "job-printing");

        PrintJob job = (await _client.GetSnapshotAsync(Ct)).Job!;

        Assert.Null(job.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(15), job.Elapsed);
    }

    [Fact]
    public async Task FinishedPrintKeepsTheLastJobAsComplete()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.RouteFixture(JobPath, "job-printing");
        await _client.GetSnapshotAsync(Ct);

        _server.RouteFixture(StatusPath, "status-finished");
        _server.Route(JobPath, HttpStatusCode.NoContent);
        PrinterSnapshot finished = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Finished, finished.State);
        PrintJob job = Assert.IsType<PrintJob>(finished.Job);
        Assert.Equal("benchy_0.4n_0.2mm_PLA_COREONE.bgcode", job.FileName);
        Assert.Equal(100.0, job.ProgressPercent);
        Assert.Equal(TimeSpan.Zero, job.Remaining);
    }

    [Fact]
    public async Task StoppedPrintKeepsTheLastJobProgress()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.RouteFixture(JobPath, "job-printing");
        await _client.GetSnapshotAsync(Ct);

        _server.RouteFixture(StatusPath, "status-stopped");
        PrinterSnapshot stopped = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Stopped, stopped.State);
        Assert.Equal(37.0, stopped.Job!.ProgressPercent);
    }

    [Fact]
    public async Task LastJobIsForgottenWhenPrinterReturnsToIdle()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.RouteFixture(JobPath, "job-printing");
        await _client.GetSnapshotAsync(Ct);
        _server.RouteFixture(StatusPath, "status-idle");
        await _client.GetSnapshotAsync(Ct);

        _server.RouteFixture(StatusPath, "status-finished");

        Assert.Null((await _client.GetSnapshotAsync(Ct)).Job);
    }

    [Fact]
    public async Task FailingJobEndpointDoesNotFailThePollAndIsRetried()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.Route(JobPath, HttpStatusCode.InternalServerError);

        PrinterSnapshot first = await _client.GetSnapshotAsync(Ct);
        Assert.Equal(PrinterState.Printing, first.State);
        Assert.Null(first.Job!.FileName);
        Assert.Equal(37.0, first.Job.ProgressPercent);

        _server.RouteFixture(JobPath, "job-printing");
        PrinterSnapshot second = await _client.GetSnapshotAsync(Ct);

        Assert.Equal("benchy_0.4n_0.2mm_PLA_COREONE.bgcode", second.Job!.FileName);
    }

    [Fact]
    public async Task JobWithoutDetailsHasNoFileName()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.Route(JobPath, HttpStatusCode.NoContent);

        Assert.Null((await _client.GetSnapshotAsync(Ct)).Job!.FileName);
    }

    [Fact]
    public async Task DigestChallengeIsAnsweredAndNonceIsReused()
    {
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(PrinterState.Idle, (await _client.GetSnapshotAsync(Ct)).State);
        }

        // One challenge for the whole session, not one per request.
        Assert.Equal(1, _server.UnauthorizedCount);
        Assert.Equal(6, _server.Requests.Count);
        Assert.Equal(1, _secrets.ReadCount);
    }

    [Fact]
    public async Task DigestAuthorizationHeaderFollowsTheFirmwareScheme()
    {
        await _client.GetSnapshotAsync(Ct);

        RecordedRequest request = _server.Requests.Last();
        Assert.Equal(HttpStatusCode.OK, request.Status);
        string authorization = request.Authorization!;
        Assert.StartsWith("Digest ", authorization, StringComparison.Ordinal);
        Assert.Contains("username=\"maker\"", authorization, StringComparison.Ordinal);
        Assert.Contains($"realm=\"{FakePrusaLinkServer.Realm}\"", authorization, StringComparison.Ordinal);
        Assert.Contains($"uri=\"{StatusPath}\"", authorization, StringComparison.Ordinal);
        Assert.Contains(
            $"response=\"{FakePrusaLinkServer.ExpectedResponse(_server.Password, _server.CurrentNonce, "GET", StatusPath)}\"",
            authorization,
            StringComparison.Ordinal);
        Assert.DoesNotContain("qop", authorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleNonceIsRenewedTransparently()
    {
        await _client.GetSnapshotAsync(Ct);
        _server.ExpireNonce();

        Assert.Equal(PrinterState.Idle, (await _client.GetSnapshotAsync(Ct)).State);
        Assert.Equal(2, _server.UnauthorizedCount);
        Assert.Equal(1, _secrets.ReadCount);
    }

    [Fact]
    public async Task WrongPasswordIsReportedAsUnauthorizedWithoutRetryStorm()
    {
        await _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), "wrong", Ct);

        PrinterSnapshot snapshot = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Unauthorized, snapshot.State);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.StatusMessage));
        Assert.True(_server.Requests.Count <= 2);
    }

    [Fact]
    public async Task MissingPasswordIsReportedAsUnauthorized()
    {
        await _secrets.DeleteAsync(SecretKeys.ForPrinter("core-one"), Ct);

        PrinterSnapshot snapshot = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Unauthorized, snapshot.State);
        Assert.Contains("password", snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangedPasswordIsPickedUpAfterRejection()
    {
        await _client.GetSnapshotAsync(Ct);
        _server.Password = "NewPass99";

        Assert.Equal(PrinterState.Unauthorized, (await _client.GetSnapshotAsync(Ct)).State);
        await _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), "NewPass99", Ct);

        Assert.Equal(PrinterState.Idle, (await _client.GetSnapshotAsync(Ct)).State);
    }

    [Fact]
    public async Task ApiKeyChallengeIsReportedAsUnauthorized()
    {
        _server.ChallengeScheme = "ApiKey";

        PrinterSnapshot snapshot = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Unauthorized, snapshot.State);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task PasswordNeverLeavesTheProcessInPlainText()
    {
        await _client.GetSnapshotAsync(Ct);
        await _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), "wrong", Ct);
        PrinterSnapshot rejected = await _client.GetSnapshotAsync(Ct);

        Assert.DoesNotContain(_server.Requests, r => r.AllHeaders.Contains(_server.Password, StringComparison.Ordinal));
        Assert.DoesNotContain(_server.Requests, static r => r.AllHeaders.Contains("wrong", StringComparison.Ordinal));
        Assert.DoesNotContain("wrong", rejected.StatusMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleNonceTogetherWithChangedPasswordIsRecovered()
    {
        await _client.GetSnapshotAsync(Ct);
        _server.ExpireNonce();
        _server.StaleIgnoresResponse = true;
        _server.Password = "NewPass99";
        await _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), "NewPass99", Ct);

        Assert.Equal(PrinterState.Idle, (await _client.GetSnapshotAsync(Ct)).State);
    }

    [Theory]
    [InlineData("Digest realm=\"Printer API\", nonce=\"0123456789abcdef\", qop=\"auth\"")]
    [InlineData("Digest realm=\"Printer API\", nonce=\"0123456789abcdef\", algorithm=SHA-256")]
    [InlineData("Digest realm=\"Printer API\"")]
    [InlineData("Digest realm=\"Printer \\\"API\", nonce=\"0123456789abcdef\"")]
    public async Task UnsupportedChallengeIsReportedAsSuch(string challenge)
    {
        _server.ChallengeOverride = challenge;

        PrinterSnapshot snapshot = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Unauthorized, snapshot.State);
        Assert.Contains("unsupported", snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task ExplicitMd5AlgorithmIsAccepted()
    {
        _server.ChallengeOverride = $"Digest realm=\"{FakePrusaLinkServer.Realm}\", nonce=\"{_server.CurrentNonce}\", algorithm=MD5, stale=false";

        await _client.GetSnapshotAsync(Ct);

        Assert.NotNull(_server.Requests.Last().Authorization);
        Assert.Equal(2, _server.Requests.Count);
    }

    [Fact]
    public async Task UnreadableSecretStoreIsReportedNotThrown()
    {
        PrusaLinkClient client = new(
            _httpClient,
            new PrinterDefinition { Id = "core-one", Name = "Core One", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" },
            new ThrowingSecretStore(),
            _time,
            NullLogger<PrusaLinkClient>.Instance);

        PrinterSnapshot snapshot = await client.GetSnapshotAsync(Ct);
        PrusaLinkInfoResult info = await client.GetInfoAsync(Ct);

        Assert.Equal(PrinterState.Unknown, snapshot.State);
        Assert.Contains("password", snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PrinterState.Unknown, info.State);
    }

    [Fact]
    public async Task ConnectionDroppedWhileReadingBodyIsOffline()
    {
        _server.Override = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream()) });

        Assert.Equal(PrinterState.Offline, (await _client.GetSnapshotAsync(Ct)).State);
    }

    [Fact]
    public async Task IoFailureIsOffline()
    {
        _server.Override = static (_, _) => throw new IOException("connection reset");

        Assert.Equal(PrinterState.Offline, (await _client.GetSnapshotAsync(Ct)).State);
    }

    [Fact]
    public async Task OversizedResponseIsUnexpectedAndNotBuffered()
    {
        _httpClient.MaxResponseContentBufferSize = 1024;
        _server.Route(StatusPath, HttpStatusCode.OK, "{\"printer\":{\"state\":\"IDLE\"},\"padding\":\"" + new string('x', 4096) + "\"}");

        PrinterSnapshot snapshot = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Unknown, snapshot.State);
    }

    [Fact]
    public async Task ConnectionDroppedWhileReadingJobKeepsTheStatus()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.RouteFixture(JobPath, "job-printing");
        await _client.GetSnapshotAsync(Ct); // authenticate
        _server.RouteFixture(StatusPath, "status-printing-next-job");
        _server.Override = (request, ct) => request.RequestUri!.AbsolutePath == JobPath
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream()) })
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixtures.Read("status-printing-next-job")) });

        PrinterSnapshot snapshot = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Printing, snapshot.State);
        Assert.Null(snapshot.Job!.FileName);
    }

    [Fact]
    public async Task ConcurrentInfoAndSnapshotAuthenticateTogether()
    {
        _server.RouteFixture("/api/version", "version");
        _server.RouteFixture("/api/v1/info", "info");
        // Hold the first two requests until both arrived, so that both calls are challenged at the same time.
        int arrived = 0;
        TaskCompletionSource bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.BeforeRequest = async () =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                bothArrived.TrySetResult();
            }

            await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };

        Task<PrinterSnapshot> snapshot = Task.Run(() => _client.GetSnapshotAsync(Ct), Ct);
        Task<PrusaLinkInfoResult> info = Task.Run(() => _client.GetInfoAsync(Ct), Ct);
        await Task.WhenAll(snapshot, info);

        Assert.Equal(PrinterState.Idle, (await snapshot).State);
        Assert.Equal(PrinterState.Idle, (await info).State);
        Assert.All(_server.Requests.Where(static r => r.Status == HttpStatusCode.Unauthorized), static r => Assert.Null(r.Authorization));
        Assert.Equal(2, _server.UnauthorizedCount);
        Assert.InRange(_secrets.ReadCount, 1, 2);
    }

    [Fact]
    public async Task DigestUriIncludesThePathPrefix()
    {
        PrusaLinkClient client = CreateClient("http://proxy.local/printers/mk4");
        _server.RouteFixture("/printers/mk4/api/v1/status", "status-idle");

        Assert.Equal(PrinterState.Idle, (await client.GetSnapshotAsync(Ct)).State);
        Assert.Contains("uri=\"/printers/mk4/api/v1/status\"", _server.Requests.Last().Authorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobDetailsOfAnotherJobAreNotUsed()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.RouteFixture(JobPath, "job-next"); // id 43 while the status reports job 42

        Assert.Null((await _client.GetSnapshotAsync(Ct)).Job!.FileName);
        _server.RouteFixture(JobPath, "job-printing");
        Assert.Equal("benchy_0.4n_0.2mm_PLA_COREONE.bgcode", (await _client.GetSnapshotAsync(Ct)).Job!.FileName);
    }

    [Fact]
    public async Task StoppedPrintHasNoRemainingTime()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.RouteFixture(JobPath, "job-printing");
        await _client.GetSnapshotAsync(Ct);
        _server.RouteFixture(StatusPath, "status-stopped");

        PrinterSnapshot stopped = await _client.GetSnapshotAsync(Ct);

        Assert.Null(stopped.Job!.Remaining);
        Assert.Null(stopped.EstimatedCompletion);
    }

    [Fact]
    public async Task FinishedPrintDoesNotAllocateANewJobOnEveryPoll()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.RouteFixture(JobPath, "job-printing");
        await _client.GetSnapshotAsync(Ct);
        _server.RouteFixture(StatusPath, "status-finished");

        PrintJob first = (await _client.GetSnapshotAsync(Ct)).Job!;
        PrintJob second = (await _client.GetSnapshotAsync(Ct)).Job!;

        Assert.Same(first, second);
    }

    [Fact]
    public async Task RequestsAskForJson()
    {
        await _client.GetSnapshotAsync(Ct);

        Assert.All(_server.Requests, static r => Assert.Contains("application/json", r.Accept, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnreachablePrinterIsOffline()
    {
        _server.Override = static (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (192.168.1.20:80)", new SocketException(10061));

        PrinterSnapshot snapshot = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Offline, snapshot.State);
        Assert.DoesNotContain("192.168.1.20", snapshot.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpClientTimeoutIsOffline()
    {
        _httpClient.Timeout = TimeSpan.FromMilliseconds(50);
        _server.Override = static async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        };

        Assert.Equal(PrinterState.Offline, (await _client.GetSnapshotAsync(Ct)).State);
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        _server.Override = static async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        };
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _client.GetSnapshotAsync(cts.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task UnexpectedStatusCodeIsUnknown(HttpStatusCode status)
    {
        _server.Route(StatusPath, status);

        PrinterSnapshot snapshot = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Unknown, snapshot.State);
        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), snapshot.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForbiddenIsUnauthorized()
    {
        _server.Route(StatusPath, HttpStatusCode.Forbidden);

        Assert.Equal(PrinterState.Unauthorized, (await _client.GetSnapshotAsync(Ct)).State);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"printer":{"temp_nozzle":"hot"}}""")]
    public async Task MalformedStatusIsUnknown(string body)
    {
        _server.Route(StatusPath, HttpStatusCode.OK, body);

        PrinterSnapshot snapshot = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Unknown, snapshot.State);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.StatusMessage));
    }

    [Fact]
    public async Task UnknownFieldsAreIgnored()
    {
        _server.Route(StatusPath, HttpStatusCode.OK, """{"future":{"x":1},"printer":{"state":"IDLE","new_sensor":42,"temp_nozzle":21.5}}""");

        PrinterSnapshot snapshot = await _client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Idle, snapshot.State);
        Assert.Equal(21.5, snapshot.Telemetry!.Nozzle!.Value.Actual);
        Assert.Null(snapshot.Telemetry.Bed);
    }

    [Theory]
    [InlineData("http://")]
    [InlineData("ftp://printer")]
    [InlineData("host with spaces")]
    public async Task InvalidAddressIsReportedWithoutNetworkAccess(string address)
    {
        PrusaLinkClient client = CreateClient(address);

        PrinterSnapshot snapshot = await client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Unknown, snapshot.State);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task InfoCombinesVersionAndPrinterInfo()
    {
        _server.RouteFixture("/api/version", "version");
        _server.RouteFixture("/api/v1/info", "info");

        PrusaLinkInfoResult result = await _client.GetInfoAsync(Ct);

        Assert.Equal(PrinterState.Idle, result.State);
        PrusaLinkPrinterInfo info = Assert.IsType<PrusaLinkPrinterInfo>(result.Info);
        Assert.Equal("prusa-core-one", info.Hostname);
        Assert.Equal("6.9.2+10123", info.Firmware);
        Assert.Equal("2.0.0", info.ApiVersion);
        Assert.Equal(0.4, info.NozzleDiameter);
        Assert.True(info.HasMmu);
    }

    [Fact]
    public async Task InfoReportsRejectedPassword()
    {
        await _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), "wrong", Ct);

        PrusaLinkInfoResult result = await _client.GetInfoAsync(Ct);

        Assert.Equal(PrinterState.Unauthorized, result.State);
        Assert.Null(result.Info);
    }

    [Fact]
    public async Task InfoToleratesOlderFirmwareWithoutOptionalFields()
    {
        _server.Route("/api/version", HttpStatusCode.OK, """{"api":"2.0.0","server":"2.1.2","nozzle_diameter":0.4,"text":"PrusaLink","hostname":"prusa-mk4","capabilities":{"upload-by-put":true}}""");
        _server.Route("/api/v1/info", HttpStatusCode.OK, """{"hostname":"prusa-mk4"}""");

        PrusaLinkInfoResult result = await _client.GetInfoAsync(Ct);

        Assert.Equal(PrinterState.Idle, result.State);
        Assert.Null(result.Info!.Firmware);
        Assert.False(result.Info.HasMmu);
    }

    private sealed class ThrowingSecretStore : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken) => throw new SecretStoreException("store locked");

        public Task SetAsync(string key, string secret, CancellationToken cancellationToken) => throw new SecretStoreException("store locked");

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) => throw new SecretStoreException("store locked");
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("connection reset");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("connection reset"));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private PrusaLinkClient CreateClient(string address) =>
        new(
            _httpClient,
            new PrinterDefinition { Id = "core-one", Name = "Core One", ConnectionType = ConnectionType.PrusaLink, Address = address },
            _secrets,
            _time,
            NullLogger<PrusaLinkClient>.Instance);
}