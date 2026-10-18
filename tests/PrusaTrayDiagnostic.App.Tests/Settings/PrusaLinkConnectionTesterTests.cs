using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.PrusaLink;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.Tests.Settings;

public sealed class PrusaLinkConnectionTesterTests : IDisposable
{
    private const string VersionJson = """{"api":"2.0.0","hostname":"prusa-core-one","firmware":"6.9.2+10123","printer":"7.0.0","nozzle_diameter":0.4}""";

    private readonly DigestPrinter _printer = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly ListLogger<PrusaLinkClient> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _printer.Dispose();

    [Fact]
    public async Task WorkingConnectionReportsModelAndFirmware()
    {
        _printer.Password = "entered";

        ConnectionTestResult result = await Create().TestAsync(LinkPrinter("new", "New"), "entered", Ct);

        Assert.Equal(ConnectionTestOutcome.Success, result.Outcome);
        Assert.Equal("7.0.0", result.PrinterModel);
        Assert.Equal("6.9.2+10123", result.Firmware);
        Assert.Equal("prusa-core-one", result.Hostname);
    }

    [Fact]
    public async Task EnteredPasswordWinsOverTheSavedOne()
    {
        _secrets.Seed("a", "saved");
        _printer.Password = "entered";

        ConnectionTestResult result = await Create().TestAsync(LinkPrinter("a", "Alpha"), "entered", Ct);

        Assert.Equal(ConnectionTestOutcome.Success, result.Outcome);
        // The unsaved password is used directly; the secret store is neither read nor written.
        Assert.Empty(_secrets.Operations);
    }

    [Fact]
    public async Task NoEnteredPasswordUsesTheSavedOne()
    {
        _secrets.Seed("a", "saved");
        _printer.Password = "saved";

        ConnectionTestResult result = await Create().TestAsync(LinkPrinter("a", "Alpha"), null, Ct);

        Assert.Equal(ConnectionTestOutcome.Success, result.Outcome);
        Assert.Equal(["get printer/a"], _secrets.Operations);
    }

    [Fact]
    public async Task WrongPasswordIsRejected()
    {
        _printer.Password = "right";

        ConnectionTestResult result = await Create().TestAsync(LinkPrinter("a", "Alpha"), "wrong", Ct);

        Assert.Equal(ConnectionTestOutcome.PasswordRejected, result.Outcome);
    }

    [Fact]
    public async Task EmptyPasswordIsMissingWithoutAskingTheStore()
    {
        _secrets.Seed("a", "saved");

        ConnectionTestResult result = await Create().TestAsync(LinkPrinter("a", "Alpha"), string.Empty, Ct);

        Assert.Equal(ConnectionTestOutcome.PasswordMissing, result.Outcome);
        Assert.Empty(_secrets.Operations);
    }

    [Fact]
    public async Task UnreadableSavedPasswordIsReported()
    {
        _secrets.FailGet.Add(SecretKeys.ForPrinter("a"));

        ConnectionTestResult result = await Create().TestAsync(LinkPrinter("a", "Alpha"), null, Ct);

        Assert.Equal(ConnectionTestOutcome.PasswordUnavailable, result.Outcome);
    }

    [Fact]
    public async Task InvalidAddressIsReportedWithoutNetworkAccess()
    {
        ConnectionTestResult result = await Create().TestAsync(LinkPrinter("a", "Alpha", "http://user:pw@printer"), "pw", Ct);

        Assert.Equal(ConnectionTestOutcome.InvalidAddress, result.Outcome);
        Assert.Equal(0, _printer.RequestCount);
    }

    [Fact]
    public async Task PrusaConnectIsNotSupported()
    {
        ConnectionTestResult result = await Create().TestAsync(CloudPrinter("c", "Cloud"), null, Ct);

        Assert.Equal(ConnectionTestOutcome.NotSupported, result.Outcome);
        Assert.Equal(0, _printer.RequestCount);
    }

    [Fact]
    public async Task UnreachablePrinter()
    {
        _printer.Failure = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused", new SocketException(10061));

        ConnectionTestResult result = await Create().TestAsync(LinkPrinter("a", "Alpha"), "Sup3rSecret", Ct);

        Assert.Equal(ConnectionTestOutcome.NotReachable, result.Outcome);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("Sup3rSecret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SecureConnectionFailure()
    {
        _printer.Failure = new HttpRequestException(HttpRequestError.SecureConnectionError, "tls", null);

        ConnectionTestResult result = await Create().TestAsync(LinkPrinter("a", "Alpha", "https://printer"), "pw", Ct);

        Assert.Equal(ConnectionTestOutcome.SecureConnectionFailed, result.Outcome);
    }

    [Fact]
    public async Task AnswerThatIsNotAPrinter()
    {
        _printer.VersionStatus = HttpStatusCode.NotFound;

        ConnectionTestResult result = await Create().TestAsync(LinkPrinter("a", "Alpha"), _printer.Password, Ct);

        Assert.Equal(ConnectionTestOutcome.UnexpectedResponse, result.Outcome);
    }

    [Fact]
    public async Task SlowPrinterTimesOut()
    {
        FakeTimeProvider time = new();
        _printer.Hang = true;
        Task<ConnectionTestResult> test = Create(time).TestAsync(LinkPrinter("a", "Alpha"), "pw", Ct);
        await _printer.FirstRequest.WaitAsync(TestTimeouts.Wait, Ct);

        time.Advance(PrusaLinkConnectionTester.DefaultTimeout);

        ConnectionTestResult result = await test.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.Equal(ConnectionTestOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        _printer.Hang = true;
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        Task<ConnectionTestResult> test = Create().TestAsync(LinkPrinter("a", "Alpha"), "pw", cancel.Token);
        await _printer.FirstRequest.WaitAsync(TestTimeouts.Wait, Ct);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.WaitAsync(TestTimeouts.Wait, Ct));
    }

    private PrusaLinkConnectionTester Create(TimeProvider? time = null) =>
        new(new SingleHandlerFactory(_printer), _secrets, time ?? TimeProvider.System, _logger);

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(PrusaLinkClientFactory.HttpClientName, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    /// <summary>Minimal PrusaLink: RFC 2069 digest (user "maker"), /api/version and /api/v1/info. No network.</summary>
    private sealed class DigestPrinter : HttpMessageHandler
    {
        private const string Realm = "Printer API";
        private const string Nonce = "a1b2c3";
        private readonly TaskCompletionSource _firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public string Password { get; set; } = "Rk7mPq2x";

        public Exception? Failure { get; set; }

        public bool Hang { get; set; }

        public HttpStatusCode VersionStatus { get; set; } = HttpStatusCode.OK;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public Task FirstRequest => _firstRequest.Task;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            _firstRequest.TrySetResult();
            if (Failure is not null)
            {
                throw Failure;
            }

            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            string path = request.RequestUri!.AbsolutePath;
            if (!IsAuthorized(request))
            {
                HttpResponseMessage challenge = new(HttpStatusCode.Unauthorized);
                challenge.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Digest", $"realm=\"{Realm}\", nonce=\"{Nonce}\", stale=false"));
                return challenge;
            }

            return path switch
            {
                "/api/version" when VersionStatus == HttpStatusCode.OK => Json(VersionJson),
                "/api/version" => new HttpResponseMessage(VersionStatus),
                "/api/v1/info" => Json("""{"mmu":false,"hostname":"prusa-core-one","nozzle_diameter":0.4}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private bool IsAuthorized(HttpRequestMessage request)
        {
            if (!request.Headers.TryGetValues("Authorization", out IEnumerable<string>? values))
            {
                return false;
            }

            string header = string.Join(",", values);
            string uri = request.RequestUri!.PathAndQuery;
            string expected = Md5Hex($"{Md5Hex($"maker:{Realm}:{Password}")}:{Nonce}:{Md5Hex($"GET:{uri}")}");
            return header.Contains($"response=\"{expected}\"", StringComparison.Ordinal);
        }

        private static string Md5Hex(string text) =>
#pragma warning disable CA5351 // PrusaLink digest is MD5 by protocol (test imitation of the printer).
            Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text)));
#pragma warning restore CA5351
    }
}
