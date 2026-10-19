using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PrusaTrayDiagnostic.PrusaLink.Tests;

/// <summary>
/// In-memory imitation of the PrusaLink server in Buddy firmware 6.x: RFC 2069 digest (MD5, no qop) with
/// realm "Printer API" and user <see cref="UserName"/> ("maker" by default), nonces valid until replaced (stale=true for a replaced nonce),
/// 204 for /api/v1/job without a job. Digest is verified independently of the production implementation.
/// </summary>
internal sealed partial class FakePrusaLinkServer : HttpMessageHandler
{
    public const string Realm = "Printer API";

    private readonly ConcurrentDictionary<string, (HttpStatusCode Status, string? Body)> _routes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _staleNonces = [];
    private int _nonceCounter;

    public FakePrusaLinkServer() => CurrentNonce = NextNonce();

    public string Password { get; set; } = "Rk7mPq2x";

    /// <summary>The only user the server accepts.</summary>
    public string UserName { get; set; } = "maker";

    /// <summary>Scheme of the challenge: "Digest" (firmware default) or e.g. "ApiKey".</summary>
    public string ChallengeScheme { get; set; } = "Digest";

    public string CurrentNonce { get; private set; }

    /// <summary>Answer a replaced nonce with stale=true without checking the digest (worst case for the client).</summary>
    public bool StaleIgnoresResponse { get; set; }

    /// <summary>Overrides the parameters of the Digest challenge, e.g. to add qop.</summary>
    public string? ChallengeOverride { get; set; }

    /// <summary>Awaited at the start of every request, e.g. to make requests overlap.</summary>
    public Func<Task>? BeforeRequest { get; set; }

    /// <summary>
    /// Paths served without login (the firmware asks for Digest only on the API, not on the web pages and scripts).
    /// </summary>
    public Func<string, bool>? IsPublicPath { get; set; }

    /// <summary>
    /// Answers authorized requests (after the digest check) instead of the static routes, e.g. to stream bodies. Such
    /// requests are recorded with status 100 (Continue) before the handler runs.
    /// </summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Handler { get; set; }

    /// <summary>Replaces the whole response, e.g. to throw or delay.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Override { get; set; }

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    public int UnauthorizedCount => Requests.Count(static r => r.Status == HttpStatusCode.Unauthorized);

    public void Route(string path, HttpStatusCode status, string? body = null) => _routes[path] = (status, body);

    public void RouteFixture(string path, string fixture) => Route(path, HttpStatusCode.OK, Fixtures.Read(fixture));

    /// <summary>Simulates nonce expiry (300 s on the printer): the old nonce becomes stale.</summary>
    public void ExpireNonce()
    {
        _staleNonces.Add(CurrentNonce);
        CurrentNonce = NextNonce();
    }

    public static string ExpectedResponse(string password, string nonce, string method, string uri, string userName = "maker") =>
        Md5Hex($"{Md5Hex($"{userName}:{Realm}:{password}")}:{nonce}:{Md5Hex($"{method}:{uri}")}");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (BeforeRequest is not null)
        {
            await BeforeRequest();
        }

        if (Override is not null)
        {
            HttpResponseMessage overridden = await Override(request, cancellationToken);
            Record(request, overridden.StatusCode);
            return overridden;
        }

        string uri = request.RequestUri!.PathAndQuery;
        string? authorization = request.Headers.Authorization?.ToString();
        bool isPublic = IsPublicPath?.Invoke(request.RequestUri.AbsolutePath) ?? false;
        if (!isPublic && Authenticate(request.Method.Method, uri, authorization) is { } challenge)
        {
            Record(request, HttpStatusCode.Unauthorized);
            HttpResponseMessage unauthorized = new(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"title\": \"401: Unauthorized\",\"message\":\"\"}", Encoding.UTF8, "application/json"),
            };
            unauthorized.Headers.TryAddWithoutValidation("WWW-Authenticate", challenge);
            return unauthorized;
        }

        if (Handler is not null)
        {
            Record(request, HttpStatusCode.Continue);
            return await Handler(request, cancellationToken);
        }

        (HttpStatusCode status, string? body) = _routes.TryGetValue(request.RequestUri.AbsolutePath, out var route)
            ? route
            : (HttpStatusCode.NotFound, "{\"title\": \"404: Not Found\",\"message\":\"\"}");
        Record(request, status);
        HttpResponseMessage response = new(status);
        if (body is not null)
        {
            response.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return response;
    }

    /// <returns><see langword="null"/> when authorized, otherwise the WWW-Authenticate challenge.</returns>
    private string? Authenticate(string method, string uri, string? authorization)
    {
        if (ChallengeScheme != "Digest")
        {
            return $"{ChallengeScheme} realm=\"{Realm}\"";
        }

        if (authorization is not null && authorization.StartsWith("Digest ", StringComparison.Ordinal))
        {
            string? nonce = Parameter(authorization, "nonce");
            string? response = Parameter(authorization, "response");
            if (nonce is not null && response is not null && Parameter(authorization, "username") == UserName)
            {
                bool correctForNonce = response == ExpectedResponse(Password, nonce, method, uri, UserName);
                if (correctForNonce && nonce == CurrentNonce)
                {
                    return null;
                }

                bool stale = (correctForNonce || StaleIgnoresResponse) && _staleNonces.Contains(nonce);
                return Challenge(stale);
            }
        }

        return Challenge(stale: false);
    }

    private string Challenge(bool stale) =>
        ChallengeOverride ?? $"Digest realm=\"{Realm}\", nonce=\"{CurrentNonce}\", stale={(stale ? "true" : "false")}";

    private static string? Parameter(string header, string name)
    {
        Match match = Regex.Match(header, $@"(?:^|[\s,]){name}=""([^""]*)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    private string NextNonce() => $"{0x3f2a9c1d:x8}{Interlocked.Increment(ref _nonceCounter):x8}";

    private void Record(HttpRequestMessage request, HttpStatusCode status) =>
        Requests.Enqueue(new RecordedRequest(
            request.Method.Method,
            request.RequestUri!.PathAndQuery,
            request.Headers.Authorization?.ToString(),
            request.Headers.Accept.ToString(),
            string.Join("\n", request.Headers.Select(static h => $"{h.Key}: {string.Join(",", h.Value)}")),
            status));

    private static string Md5Hex(string value) =>
#pragma warning disable CA5351 // The printer firmware mandates MD5 digest authentication.
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
#pragma warning restore CA5351
}

internal sealed record RecordedRequest(string Method, string PathAndQuery, string? Authorization, string Accept, string AllHeaders, HttpStatusCode Status);

internal static class Fixtures
{
    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string Read(string name) => File.ReadAllText(Path.Combine(Directory, "firmware", name + ".json"));
}

internal sealed class InMemorySecretStore : Core.Secrets.ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public int ReadCount;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref ReadCount);
        return Task.FromResult(_secrets.TryGetValue(key, out string? secret) ? secret : null);
    }

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken)
    {
        _secrets[key] = secret;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) => Task.FromResult(_secrets.TryRemove(key, out _));
}
