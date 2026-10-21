using System.Net;
using System.Text.RegularExpressions;

using PrusaTrayDiagnostic.Simulator.Printers;

namespace PrusaTrayDiagnostic.Simulator.Tests;

/// <summary>The simulated PrusaLink server over real HTTP on the loopback interface (ephemeral ports).</summary>
public sealed class SimulatedPrinterHttpTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly SimulationClock _clock = new();

    [Theory]
    [InlineData("api/version")]
    [InlineData("api/v1/info")]
    [InlineData("api/v1/status")]
    [InlineData("api/v1/job")]
    public async Task ApiAsksForDigestLikeTheFirmware(string path)
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(printer.BaseUri, path));
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using HttpResponseMessage response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string challenge = Assert.Single(response.Headers.WwwAuthenticate).ToString();
        Assert.Matches("^Digest realm=\"Printer API\", nonce=\"[0-9a-f]{16}\", stale=false$", challenge);
        Assert.Equal("{\"title\": \"401: Unauthorized\",\"message\":\"\"}", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task CorrectDigestIsAccepted()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();

        using HttpResponseMessage response = await TestPrinters.GetWithDigestAsync(client, new Uri(printer.BaseUri, "api/version"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DigestCoversTheQueryAndTheMethod()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();
        Uri uri = new(printer.BaseUri, "api/v1/status");
        using HttpResponseMessage challenge = await client.GetAsync(uri, Ct);
        string nonce = TestPrinters.Nonce(challenge);
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(printer.BaseUri, "api/v1/status?x=1"));
        // Signed for a different request target.
        request.Headers.TryAddWithoutValidation("Authorization", TestPrinters.DigestHeader("maker", TestPrinters.Password, nonce, "GET", "/api/v1/status"));

        using HttpResponseMessage response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongPasswordIsRejectedWithoutStale()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();

        using HttpResponseMessage response = await TestPrinters.GetWithDigestAsync(client, new Uri(printer.BaseUri, "api/version"), password: "wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.EndsWith("stale=false", Assert.Single(response.Headers.WwwAuthenticate).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongUserIsRejected()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock, userName: "operator");
        using HttpClient client = TestPrinters.CreateHttpClient();

        using HttpResponseMessage wrong = await TestPrinters.GetWithDigestAsync(client, new Uri(printer.BaseUri, "api/version"), userName: "maker");
        using HttpResponseMessage right = await TestPrinters.GetWithDigestAsync(client, new Uri(printer.BaseUri, "api/version"), userName: "operator");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
    }

    [Fact]
    public async Task ExpiredNonceIsAnsweredWithStale()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();
        Uri uri = new(printer.BaseUri, "api/v1/status");
        using HttpResponseMessage challenge = await client.GetAsync(uri, Ct);
        string authorization = TestPrinters.DigestHeader("maker", TestPrinters.Password, TestPrinters.Nonce(challenge), "GET", uri.PathAndQuery);

        HttpStatusCode fresh = await SendAsync(client, uri, authorization);
        _clock.Advance(DigestVerifier.NonceLifetime + TimeSpan.FromSeconds(1));
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        using HttpResponseMessage expired = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, fresh);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.EndsWith("stale=true", Assert.Single(expired.Headers.WwwAuthenticate).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiKeyWithThePasswordIsAccepted()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(printer.BaseUri, "api/v1/status"));
        request.Headers.Add("X-Api-Key", TestPrinters.Password);

        using HttpResponseMessage response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WebPageIsServedWithoutLogin()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock, model: SimulatedModel.Mk4S);
        using HttpClient client = TestPrinters.CreateHttpClient();

        using HttpResponseMessage response = await client.GetAsync(printer.BaseUri, Ct);
        string html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("MK4S", html, StringComparison.Ordinal);
        Assert.Contains("fetch('api/v1/status'", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownApiPathIsNotFoundAsJson()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        using HttpResponseMessage response = await TestPrinters.GetWithDigestAsync(client, new Uri(printer.BaseUri, "api/v1/nothing"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("{\"title\": \"404: Not Found\",\"message\":\"\"}", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task JobIsNoContentWithoutAJob()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock, PrinterScenario.Idle);
        using HttpClient client = TestPrinters.CreateHttpClient();

        using HttpResponseMessage response = await TestPrinters.GetWithDigestAsync(client, new Uri(printer.BaseUri, "api/v1/job"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task StatusAndJobDescribeTheSamePrint()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        _clock.Advance(TimeSpan.FromMinutes(10));
        using HttpClient client = TestPrinters.CreateHttpClient();

        using HttpResponseMessage status = await TestPrinters.GetWithDigestAsync(client, new Uri(printer.BaseUri, "api/v1/status"));
        using HttpResponseMessage job = await TestPrinters.GetWithDigestAsync(client, new Uri(printer.BaseUri, "api/v1/job"));
        string statusJson = await status.Content.ReadAsStringAsync(Ct);
        string jobJson = await job.Content.ReadAsStringAsync(Ct);

        Assert.Contains("\"job\":{\"id\":42,", statusJson, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"PRINTING\"", statusJson, StringComparison.Ordinal);
        Assert.StartsWith("{\"id\":42,\"state\":\"PRINTING\",", jobJson, StringComparison.Ordinal);
        Assert.Contains("\"display_name\":\"benchy_0.4n_0.2mm_PLA.bgcode\"", jobJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfflinePrinterDropsConnectionsAndRecovers()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();
        Uri uri = new(printer.BaseUri, "api/version");

        printer.Simulation.SetOffline(true);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(uri, Ct));
        printer.Simulation.SetOffline(false);
        using HttpResponseMessage recovered = await client.GetAsync(uri, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, recovered.StatusCode);
    }

    [Fact]
    public async Task OfflinePrinterInHangModeNeverAnswers()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock, offlineMode: OfflineMode.Hang);
        using HttpClient client = TestPrinters.CreateHttpClient(TimeSpan.FromMilliseconds(500));
        printer.Simulation.SetOffline(true);

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.GetAsync(new Uri(printer.BaseUri, "api/version"), Ct));
    }

    [Fact]
    public async Task HangingRequestIsAnsweredWhenThePrinterComesBack()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock, offlineMode: OfflineMode.Hang);
        using HttpClient client = TestPrinters.CreateHttpClient();
        printer.Simulation.SetOffline(true);

        Task<HttpResponseMessage> pending = client.GetAsync(new Uri(printer.BaseUri, "api/version"), Ct);
        await Task.Delay(300, Ct);
        Assert.False(pending.IsCompleted);
        printer.Simulation.SetOffline(false);
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ControlEndpointsSwitchScenarioAndOnlineState()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();

        using HttpResponseMessage paused = await client.PostAsync(new Uri(printer.BaseUri, "_simulator/scenario/paused"), null, Ct);
        using HttpResponseMessage offline = await client.PostAsync(new Uri(printer.BaseUri, "_simulator/offline"), null, Ct);
        // Answered also while the printer is offline.
        string state = await client.GetStringAsync(new Uri(printer.BaseUri, "_simulator/state"), Ct);
        using HttpResponseMessage online = await client.PostAsync(new Uri(printer.BaseUri, "_simulator/online"), null, Ct);
        using HttpResponseMessage unknown = await client.PostAsync(new Uri(printer.BaseUri, "_simulator/scenario/sleeping"), null, Ct);

        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, offline.StatusCode);
        Assert.Equal("{\"model\":\"7.1.0\",\"scenario\":\"paused\",\"online\":false,\"forced_offline\":true}", state);
        Assert.Equal(HttpStatusCode.OK, online.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(PrinterScenario.Paused, printer.Simulation.Scenario);
        Assert.True(printer.Simulation.IsOnline);
    }

    [Fact]
    public async Task OtherMethodsAreNotAllowed()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestPrinters.Password);

        using HttpResponseMessage response = await client.DeleteAsync(new Uri(printer.BaseUri, "api/v1/job/42"), Ct);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task DisposingReleasesThePort()
    {
        SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        int port = printer.Port;
        await printer.DisposeAsync();

        using System.Net.Sockets.TcpListener listener = new(IPAddress.Loopback, port);
        listener.Start();
        listener.Stop();
    }

    [Theory]
    [InlineData(PrinterScenario.Printing, "status-printing", 10)]
    [InlineData(PrinterScenario.Printing, "status-printing-no-estimate", 0)]
    [InlineData(PrinterScenario.Idle, "status-idle", 0)]
    [InlineData(PrinterScenario.Paused, "status-paused", 0)]
    [InlineData(PrinterScenario.Finished, "status-finished", 0)]
    [InlineData(PrinterScenario.Error, "status-error", 0)]
    [InlineData(PrinterScenario.Attention, "status-attention", 0)]
    public void StatusHasTheShapeOfTheFirmwareFixture(PrinterScenario scenario, string fixture, int minutes)
    {
        PrinterSimulation simulation = new(SimulatedModel.CoreOne, scenario, _clock, timing: TestPrinters.Timing);
        _clock.Advance(TimeSpan.FromMinutes(minutes));

        string json = PrusaLinkJson.Status(simulation.GetStatus());

        SortedSet<string> expected = TestPrinters.Shape(TestPrinters.Fixture(fixture));
        // Only the MMU / filament change estimate of the fixture is not simulated.
        expected.Remove("job.filament_change_in");
        Assert.Equal(expected, TestPrinters.Shape(json));
        Assert.Matches("\"temp_bed\":-?\\d+\\.\\d,", json);
        if (simulation.GetStatus().Job is not null)
        {
            Assert.Matches("\"progress\":\\d+\\.\\d\\d,", json);
        }
    }

    [Fact]
    public void VersionInfoAndJobHaveTheShapeOfTheFirmwareFixtures()
    {
        PrinterSimulation simulation = new(SimulatedModel.CoreOne, PrinterScenario.Printing, _clock, timing: TestPrinters.Timing);
        _clock.Advance(TimeSpan.FromMinutes(10));
        SimulatedStatus status = simulation.GetStatus();

        Assert.Equal(TestPrinters.Shape(TestPrinters.Fixture("version")), TestPrinters.Shape(PrusaLinkJson.Version(SimulatedModel.CoreOne, "prusa-core-one")));
        Assert.Equal(TestPrinters.Shape(TestPrinters.Fixture("info")), TestPrinters.Shape(PrusaLinkJson.Info("prusa-core-one", "SN")));
        Assert.Equal(TestPrinters.Shape(TestPrinters.Fixture("job-printing")), TestPrinters.Shape(PrusaLinkJson.Job(status.Job!, status.State)));
        Assert.Matches(new Regex("\"printer\":\"7\\.1\\.0\""), PrusaLinkJson.Version(SimulatedModel.CoreOne, "prusa-core-one"));
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, Uri uri, string authorization)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        using HttpResponseMessage response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }
}
