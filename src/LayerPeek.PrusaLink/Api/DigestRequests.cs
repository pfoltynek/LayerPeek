using System.Net;

namespace LayerPeek.PrusaLink.Api;

/// <summary>GET requests with the firmware's Digest login, shared by the monitoring client and the preview fetcher.</summary>
internal static class DigestRequests
{
    /// <summary>
    /// Sends a GET with cached digest credentials and answers 401 challenges: at most twice, and the second time only when
    /// the first one reused the cached password (stale nonce), so that a password changed at the same moment is re-read.
    /// A final 401 is returned as is. When a challenge cannot be answered, the response is null and
    /// <c>Refusal</c> says why (it is only meaningful then).
    /// </summary>
    /// <param name="accept">Value of the Accept header.</param>
    /// <param name="ifNoneMatch">Optional entity tag for If-None-Match (sent as is).</param>
    /// <exception cref="Core.Secrets.SecretStoreException">The password could not be read.</exception>
    public static async Task<(HttpResponseMessage? Response, ChallengeResult Refusal)> GetAsync(
        HttpClient httpClient,
        DigestAuthenticator authenticator,
        Uri uri,
        string accept,
        string? ifNoneMatch,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await SendOnceAsync(httpClient, authenticator, uri, accept, ifNoneMatch, completionOption, cancellationToken).ConfigureAwait(false);
        for (int round = 0; round < 2 && response.StatusCode == HttpStatusCode.Unauthorized; round++)
        {
            ChallengeResult challenge;
            try
            {
                challenge = await authenticator.AcceptChallengeAsync(response.Headers.WwwAuthenticate, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                response.Dispose();
            }

            if (challenge.Error is not null)
            {
                return (null, challenge);
            }

            response = await SendOnceAsync(httpClient, authenticator, uri, accept, ifNoneMatch, completionOption, cancellationToken).ConfigureAwait(false);
            if (challenge.PasswordReloaded)
            {
                break;
            }
        }

        return (response, default);
    }

    private static async Task<HttpResponseMessage> SendOnceAsync(
        HttpClient httpClient,
        DigestAuthenticator authenticator,
        Uri uri,
        string accept,
        string? ifNoneMatch,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        if (authenticator.GetAuthorization(HttpMethod.Get, uri.PathAndQuery) is { } authorization)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return await httpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
    }
}
