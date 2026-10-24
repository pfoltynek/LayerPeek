using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Printers;
using LayerPeek.Core.Secrets;

namespace LayerPeek.PrusaLink.Tests;

/// <summary><see cref="PrusaLinkInfoResult.Failure"/>: why "test connection" failed, so that the UI can explain it.</summary>
public sealed class PrusaLinkInfoFailureTests : IDisposable
{
    private const string VersionPath = "/api/version";

    private readonly FakePrusaLinkServer _server = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly HttpClient _httpClient;

    public PrusaLinkInfoFailureTests()
    {
        _httpClient = new HttpClient(_server);
        _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), _server.Password, CancellationToken.None).GetAwaiter().GetResult();
        _server.RouteFixture(VersionPath, "version");
        _server.RouteFixture("/api/v1/info", "info");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _httpClient.Dispose();

    [Fact]
    public async Task SuccessHasNoFailure()
    {
        PrusaLinkInfoResult result = await CreateClient().GetInfoAsync(Ct);

        Assert.Equal(PrinterState.Idle, result.State);
        Assert.Equal(PrusaLinkFailure.None, result.Failure);
    }

    [Fact]
    public async Task InvalidAddress()
    {
        PrusaLinkInfoResult result = await CreateClient("ftp://printer").GetInfoAsync(Ct);

        Assert.Equal(PrusaLinkFailure.InvalidAddress, result.Failure);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task RejectedPassword()
    {
        await _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), "wrong", Ct);

        PrusaLinkInfoResult result = await CreateClient().GetInfoAsync(Ct);

        Assert.Equal(PrinterState.Unauthorized, result.State);
        Assert.Equal(PrusaLinkFailure.PasswordRejected, result.Failure);
    }

    [Fact]
    public async Task MissingPassword()
    {
        await _secrets.DeleteAsync(SecretKeys.ForPrinter("core-one"), Ct);

        PrusaLinkInfoResult result = await CreateClient().GetInfoAsync(Ct);

        Assert.Equal(PrinterState.Unauthorized, result.State);
        Assert.Equal(PrusaLinkFailure.PasswordMissing, result.Failure);
    }

    [Fact]
    public async Task AccessDenied()
    {
        _server.Route(VersionPath, HttpStatusCode.Forbidden);

        PrusaLinkInfoResult result = await CreateClient().GetInfoAsync(Ct);

        Assert.Equal(PrusaLinkFailure.AccessDenied, result.Failure);
    }

    [Fact]
    public async Task UnsupportedAuthentication()
    {
        _server.ChallengeScheme = "ApiKey";

        PrusaLinkInfoResult result = await CreateClient().GetInfoAsync(Ct);

        Assert.Equal(PrusaLinkFailure.UnsupportedAuthentication, result.Failure);
    }

    [Fact]
    public async Task UnreadablePassword()
    {
        PrusaLinkInfoResult result = await CreateClient(secrets: new ThrowingSecretStore()).GetInfoAsync(Ct);

        Assert.Equal(PrusaLinkFailure.PasswordUnreadable, result.Failure);
    }

    [Fact]
    public async Task NotReachable()
    {
        _server.Override = static (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused", new SocketException(10061));

        PrusaLinkInfoResult result = await CreateClient().GetInfoAsync(Ct);

        Assert.Equal(PrinterState.Offline, result.State);
        Assert.Equal(PrusaLinkFailure.NotReachable, result.Failure);
    }

    [Fact]
    public async Task SecureConnectionFailed()
    {
        _server.Override = static (_, _) => throw new HttpRequestException(
            HttpRequestError.SecureConnectionError,
            "The SSL connection could not be established.",
            new AuthenticationException("The remote certificate is invalid."));

        PrusaLinkInfoResult result = await CreateClient("https://192.168.1.20").GetInfoAsync(Ct);

        Assert.Equal(PrinterState.Offline, result.State);
        Assert.Equal(PrusaLinkFailure.SecureConnectionFailed, result.Failure);
    }

    [Fact]
    public async Task SecureConnectionFailureIsNamedInSnapshot()
    {
        _server.Override = static (_, _) => throw new HttpRequestException(HttpRequestError.SecureConnectionError, "tls", null);

        PrinterSnapshot snapshot = await CreateClient("https://192.168.1.20").GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Offline, snapshot.State);
        Assert.Contains("secure", snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeout()
    {
        _httpClient.Timeout = TimeSpan.FromMilliseconds(50);
        _server.Override = static async (_, ct) =>
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        };

        PrusaLinkInfoResult result = await CreateClient().GetInfoAsync(Ct);

        Assert.Equal(PrusaLinkFailure.Timeout, result.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, null)]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "null")]
    public async Task UnexpectedResponse(HttpStatusCode status, string? body)
    {
        _server.Route(VersionPath, status, body);

        PrusaLinkInfoResult result = await CreateClient().GetInfoAsync(Ct);

        Assert.Equal(PrinterState.Unknown, result.State);
        Assert.Equal(PrusaLinkFailure.UnexpectedResponse, result.Failure);
    }

    private PrusaLinkClient CreateClient(string address = "192.168.1.20", ISecretStore? secrets = null) =>
        new(
            _httpClient,
            new PrinterDefinition { Id = "core-one", Name = "Core One", ConnectionType = ConnectionType.PrusaLink, Address = address },
            secrets ?? _secrets,
            TimeProvider.System,
            NullLogger<PrusaLinkClient>.Instance);

    private sealed class ThrowingSecretStore : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken) => throw new SecretStoreException("store locked");

        public Task SetAsync(string key, string secret, CancellationToken cancellationToken) => throw new SecretStoreException("store locked");

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) => throw new SecretStoreException("store locked");
    }
}
