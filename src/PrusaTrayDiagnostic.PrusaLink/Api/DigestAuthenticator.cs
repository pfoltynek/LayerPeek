using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

using PrusaTrayDiagnostic.Core.Secrets;

namespace PrusaTrayDiagnostic.PrusaLink.Api;

/// <summary>
/// HTTP Digest authentication as implemented by Buddy firmware: RFC 2069 (MD5, no qop).
/// A nonce stays valid for 300 s and may be reused, so one challenge round-trip is needed only every few minutes
/// (the built-in .NET handler would pay it on every request). Authorization values are cached per URI, so a
/// steady-state request allocates nothing for authentication. Thread-safe.
/// <paramref name="userName"/> must be valid per <see cref="Core.Printers.PrinterUserName"/> (it is echoed in a quoted header).
/// </summary>
internal sealed class DigestAuthenticator(ISecretStore secretStore, string secretKey, string userName)
{
    private Session? _session;

    /// <summary>Authorization header value for a GET of <paramref name="pathAndQuery"/>, or null before the first challenge.</summary>
    public string? GetAuthorization(string pathAndQuery) =>
        Volatile.Read(ref _session) is { } session
            ? session.Authorizations.GetOrAdd(pathAndQuery, static (uri, s) => s.CreateAuthorization(uri), session)
            : null;

    /// <summary>Processes a 401 response: on success the request should be retried with the new credentials.</summary>
    /// <exception cref="SecretStoreException">The password could not be read.</exception>
    public async Task<ChallengeResult> AcceptChallengeAsync(HttpHeaderValueCollection<AuthenticationHeaderValue> challenges, CancellationToken cancellationToken)
    {
        Challenge? challenge = FindDigestChallenge(challenges);
        if (challenge is null)
        {
            return new ChallengeResult("The printer requires an unsupported authentication method.", PasswordReloaded: false, PrusaLinkFailure.UnsupportedAuthentication);
        }

        Session? current = Volatile.Read(ref _session);
        if (challenge.Value.Stale && current is not null && current.Realm == challenge.Value.Realm)
        {
            // Only the nonce expired; the password was accepted.
            Volatile.Write(ref _session, new Session(userName, challenge.Value.Realm, challenge.Value.Nonce, current.Ha1));
            return new ChallengeResult(null, PasswordReloaded: false);
        }

        // First challenge or rejected password: read it again, it may have been changed in the settings.
        string? password = await secretStore.GetAsync(secretKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(password))
        {
            Volatile.Write(ref _session, null);
            return new ChallengeResult("No PrusaLink password is configured for this printer.", PasswordReloaded: true, PrusaLinkFailure.PasswordMissing);
        }

        string ha1 = Md5Hex($"{userName}:{challenge.Value.Realm}:{password}");
        Volatile.Write(ref _session, new Session(userName, challenge.Value.Realm, challenge.Value.Nonce, ha1));
        return new ChallengeResult(null, PasswordReloaded: true);
    }

    private static Challenge? FindDigestChallenge(HttpHeaderValueCollection<AuthenticationHeaderValue> challenges)
    {
        foreach (AuthenticationHeaderValue value in challenges)
        {
            if (string.Equals(value.Scheme, "Digest", StringComparison.OrdinalIgnoreCase) && value.Parameter is { } parameter)
            {
                string? realm = null;
                string? nonce = null;
                bool stale = false;
                bool supported = true;
                foreach ((string name, string paramValue) in DigestParameters.Parse(parameter))
                {
                    if (name.Equals("realm", StringComparison.OrdinalIgnoreCase))
                    {
                        realm = paramValue;
                    }
                    else if (name.Equals("nonce", StringComparison.OrdinalIgnoreCase))
                    {
                        nonce = paramValue;
                    }
                    else if (name.Equals("stale", StringComparison.OrdinalIgnoreCase))
                    {
                        stale = paramValue.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (name.Equals("qop", StringComparison.OrdinalIgnoreCase)
                        || (name.Equals("algorithm", StringComparison.OrdinalIgnoreCase) && !paramValue.Equals("MD5", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Only the firmware's RFC 2069 variant is implemented; answering anything else would look like a wrong password.
                        supported = false;
                    }
                }

                // Values are echoed into a quoted header; refuse anything that could break out of the quotes.
                if (supported && realm is not null && nonce is { Length: > 0 } && IsSafe(realm) && IsSafe(nonce))
                {
                    return new Challenge(realm, nonce, stale);
                }
            }
        }

        return null;
    }

    private static bool IsSafe(string value)
    {
        foreach (char c in value)
        {
            if (c is '"' or '\\' || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

#pragma warning disable CA5351 // MD5 is mandated by the printer's digest authentication; it is not used for any other purpose.
    private static string Md5Hex(string value)
    {
        Span<byte> hash = stackalloc byte[MD5.HashSizeInBytes];
        int byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> buffer = stackalloc byte[256];
        Span<byte> bytes = byteCount <= buffer.Length ? buffer[..byteCount] : new byte[byteCount];
        try
        {
            Encoding.UTF8.GetBytes(value, bytes);
            MD5.HashData(bytes, hash);
            return Convert.ToHexStringLower(hash);
        }
        finally
        {
            // The input of HA1 contains the password.
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
#pragma warning restore CA5351

    private readonly record struct Challenge(string Realm, string Nonce, bool Stale);

    private sealed class Session(string userName, string realm, string nonce, string ha1)
    {
        public string Realm { get; } = realm;

        public string Ha1 { get; } = ha1;

        /// <summary>Per-URI header values for this nonce; only a handful of API paths are ever used.</summary>
        public ConcurrentDictionary<string, string> Authorizations { get; } = new(StringComparer.Ordinal);

        public string CreateAuthorization(string uri)
        {
            string response = Md5Hex($"{Ha1}:{nonce}:{Md5Hex($"GET:{uri}")}");
            return $"Digest username=\"{userName}\", realm=\"{Realm}\", nonce=\"{nonce}\", uri=\"{uri}\", response=\"{response}\"";
        }
    }
}

/// <param name="Error">Why the challenge cannot be answered; null when the request should be retried.</param>
/// <param name="PasswordReloaded">The password was read from the secret store (false when only the nonce was renewed).</param>
/// <param name="Reason">Machine-readable <paramref name="Error"/>; <see cref="PrusaLinkFailure.None"/> when there is none.</param>
internal readonly record struct ChallengeResult(string? Error, bool PasswordReloaded, PrusaLinkFailure Reason = PrusaLinkFailure.None);

/// <summary>Parser of the comma-separated <c>name=value</c> / <c>name="value"</c> list of a Digest challenge.</summary>
internal static class DigestParameters
{
    public static List<(string Name, string Value)> Parse(string parameters)
    {
        List<(string, string)> result = [];
        ReadOnlySpan<char> rest = parameters;
        while (true)
        {
            rest = rest.TrimStart(" ,\t");
            int equals = rest.IndexOf('=');
            if (equals <= 0)
            {
                return result;
            }

            string name = rest[..equals].Trim().ToString();
            rest = rest[(equals + 1)..].TrimStart();
            string value;
            if (rest.StartsWith('"'))
            {
                int end = rest[1..].IndexOf('"');
                if (end < 0)
                {
                    return result;
                }

                value = rest.Slice(1, end).ToString();
                rest = rest[(end + 2)..];
            }
            else
            {
                int comma = rest.IndexOf(',');
                ReadOnlySpan<char> token = comma < 0 ? rest : rest[..comma];
                value = token.Trim().ToString();
                rest = comma < 0 ? [] : rest[comma..];
            }

            result.Add((name, value));
        }
    }
}
