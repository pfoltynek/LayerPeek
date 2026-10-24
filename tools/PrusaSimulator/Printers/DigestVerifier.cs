using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LayerPeek.Simulator.Printers;

/// <summary>
/// Server side of the PrusaLink login as in Buddy firmware 6.x (docs/api/prusalink.md): HTTP Digest per RFC 2069 (MD5, no
/// qop), realm "Printer API"; only <c>nonce</c> and <c>response</c> of the Authorization header are read. A nonce is valid
/// for 300 s; a correct answer with an expired nonce gets a new challenge with <c>stale=true</c>. <c>X-Api-Key</c> with
/// the password is accepted as well (the firmware still does). Thread-safe.
/// </summary>
public sealed partial class DigestVerifier(string userName, string password, TimeProvider timeProvider)
{
    public const string Realm = "Printer API";

    public static readonly TimeSpan NonceLifetime = TimeSpan.FromSeconds(300);

    // Issued nonces and when; forgotten after twice their lifetime (an unknown nonce gets a fresh, non-stale challenge).
    private readonly ConcurrentDictionary<string, long> _nonces = new(StringComparer.Ordinal);

    public string UserName => userName;

    /// <returns>Null when the request is authorized, otherwise the <c>WWW-Authenticate</c> value of the 401 response.</returns>
    /// <param name="requestTarget">Path and query exactly as sent on the request line.</param>
    public string? Check(string method, string requestTarget, string? authorization, string? apiKey)
    {
        if (apiKey is not null && FixedTimeEquals(apiKey, password))
        {
            return null;
        }

        if (authorization is not null && authorization.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase))
        {
            string? nonce = Parameter(authorization, "nonce");
            string? response = Parameter(authorization, "response");
            if (nonce is not null && response is not null && _nonces.TryGetValue(nonce, out long issued))
            {
                bool correct = FixedTimeEquals(response, ExpectedResponse(userName, password, nonce, method, requestTarget));
                if (correct && timeProvider.GetElapsedTime(issued) <= NonceLifetime)
                {
                    return null;
                }

                return Challenge(stale: correct);
            }
        }

        return Challenge(stale: false);
    }

    /// <summary>The digest a client must send (independent of the app's implementation).</summary>
    public static string ExpectedResponse(string userName, string password, string nonce, string method, string uri) =>
        Md5Hex($"{Md5Hex($"{userName}:{Realm}:{password}")}:{nonce}:{Md5Hex($"{method}:{uri}")}");

    private string Challenge(bool stale)
    {
        long now = timeProvider.GetTimestamp();
        foreach ((string old, long issued) in _nonces)
        {
            if (timeProvider.GetElapsedTime(issued, now) > NonceLifetime * 2)
            {
                _nonces.TryRemove(old, out _);
            }
        }

        string nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        _nonces[nonce] = now;
        return $"Digest realm=\"{Realm}\", nonce=\"{nonce}\", stale={(stale ? "true" : "false")}";
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static string? Parameter(string header, string name)
    {
        Match match = ParameterRegex().Match(header);
        while (match.Success)
        {
            if (string.Equals(match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["token"].Value;
            }

            match = match.NextMatch();
        }

        return null;
    }

    [GeneratedRegex("""(?<name>[A-Za-z]+)\s*=\s*(?:"(?<quoted>[^"]*)"|(?<token>[^\s,]+))""")]
    private static partial Regex ParameterRegex();

#pragma warning disable CA5351 // The printer firmware mandates MD5 digest authentication.
    private static string Md5Hex(string value) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
#pragma warning restore CA5351
}
