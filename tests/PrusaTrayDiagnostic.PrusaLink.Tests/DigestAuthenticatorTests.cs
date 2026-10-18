using System.Net;
using System.Net.Http.Headers;

using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.PrusaLink.Api;

namespace PrusaTrayDiagnostic.PrusaLink.Tests;

public sealed class DigestAuthenticatorTests : IDisposable
{
    private const string Password = "Rk7mPq2x";
    private const string Nonce = "3f2a9c1d00000001";

    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeTimeProvider _time = new();
    private readonly HttpResponseMessage _challenge = new(HttpStatusCode.Unauthorized);

    public DigestAuthenticatorTests()
    {
        _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), Password, CancellationToken.None).GetAwaiter().GetResult();
        SetChallenge(Nonce, stale: false);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _challenge.Dispose();

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public async Task ResponseHashesTheRealMethod(string method)
    {
        foreach (bool cache in new[] { true, false })
        {
            DigestAuthenticator authenticator = Create(cache);
            await authenticator.AcceptChallengeAsync(_challenge.Headers.WwwAuthenticate, Ct);

            string? authorization = authenticator.GetAuthorization(HttpMethod.Parse(method), "/api/v1/files/usb/a%20b.gcode");

            Assert.NotNull(authorization);
            Assert.Contains("uri=\"/api/v1/files/usb/a%20b.gcode\"", authorization, StringComparison.Ordinal);
            Assert.Contains($"response=\"{FakePrusaLinkServer.ExpectedResponse(Password, Nonce, method, "/api/v1/files/usb/a%20b.gcode")}\"", authorization, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetAuthorizationsAreCachedOnlyWhenEnabled()
    {
        DigestAuthenticator cached = Create(cache: true);
        DigestAuthenticator uncached = Create(cache: false);
        await cached.AcceptChallengeAsync(_challenge.Headers.WwwAuthenticate, Ct);
        await uncached.AcceptChallengeAsync(_challenge.Headers.WwwAuthenticate, Ct);

        Assert.Same(cached.GetAuthorization(HttpMethod.Get, "/api/v1/status"), cached.GetAuthorization(HttpMethod.Get, "/api/v1/status"));
        string? first = uncached.GetAuthorization(HttpMethod.Get, "/api/v1/status");
        string? second = uncached.GetAuthorization(HttpMethod.Get, "/api/v1/status");
        Assert.Equal(first, second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void NoAuthorizationBeforeTheFirstChallenge()
    {
        DigestAuthenticator authenticator = Create(cache: true);

        Assert.Null(authenticator.GetAuthorization(HttpMethod.Put, "/api/v1/files/usb/a.gcode"));
        Assert.Null(authenticator.GetNonceAge());
        Assert.Equal(0, _secrets.ReadCount);
    }

    [Fact]
    public async Task NonceAgeIsMeasuredFromTheLastChallenge()
    {
        DigestAuthenticator authenticator = Create(cache: false);
        await authenticator.AcceptChallengeAsync(_challenge.Headers.WwwAuthenticate, Ct);
        _time.Advance(TimeSpan.FromSeconds(250));

        Assert.Equal(TimeSpan.FromSeconds(250), authenticator.GetNonceAge());

        SetChallenge("3f2a9c1d00000002", stale: true);
        await authenticator.AcceptChallengeAsync(_challenge.Headers.WwwAuthenticate, Ct);

        Assert.Equal(TimeSpan.Zero, authenticator.GetNonceAge());
        // A stale nonce does not re-read the password.
        Assert.Equal(1, _secrets.ReadCount);
    }

    private DigestAuthenticator Create(bool cache) =>
        new(_secrets, SecretKeys.ForPrinter("core-one"), "maker", _time, cache);

    private void SetChallenge(string nonce, bool stale)
    {
        _challenge.Headers.WwwAuthenticate.Clear();
        _challenge.Headers.TryAddWithoutValidation(
            "WWW-Authenticate",
            $"Digest realm=\"{FakePrusaLinkServer.Realm}\", nonce=\"{nonce}\", stale={(stale ? "true" : "false")}");
        Assert.Single(_challenge.Headers.WwwAuthenticate, static (AuthenticationHeaderValue v) => v.Scheme == "Digest");
    }
}
