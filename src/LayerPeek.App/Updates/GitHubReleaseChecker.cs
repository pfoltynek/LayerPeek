using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

using LayerPeek.Core.Updates;

namespace LayerPeek.App.Updates;

/// <summary>
/// <see cref="IUpdateChecker"/> over the GitHub REST API (T-058, https://docs.github.com/en/rest/releases/releases): the list
/// of releases (<c>GET /repos/pfoltynek/LayerPeek/releases</c>), because <c>/releases/latest</c> never returns a pre-release
/// and every <c>0.x</c> release is one (T-055). Anonymous (no token in the app, decided 2026-10-21; 60 requests an hour per IP
/// address, the app makes about one a day). The newest release by version wins (not the newest by date): drafts are never
/// visible anonymously and are skipped anyway, tags that are no version (<c>vX.Y.Z[-pre]</c>) are skipped, pre-releases only
/// when asked for. Errors are exceptions for the caller to log: HTTP 403/429 (rate limit), 404, a timeout, invalid JSON.
/// </summary>
internal sealed class GitHubReleaseChecker : IUpdateChecker
{
    /// <summary>The named <see cref="HttpClient"/> (timeout, size limit, headers; registered by the composition root).</summary>
    public const string HttpClientName = "LayerPeek.GitHubReleases";

    /// <summary>The API of github.com.</summary>
    public static readonly Uri DefaultApiBase = new("https://api.github.com/", UriKind.Absolute);

    /// <summary>One request, also on a slow connection; the check runs in the background.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>A page of 30 releases is about 30 kB; anything far larger is not the API.</summary>
    public const long MaxResponseBytes = 1024 * 1024;

    /// <summary>Releases on the page; the newest by version is among the last few dozen published.</summary>
    public const int PageSize = 30;

    private const string ReleasesPath = "repos/pfoltynek/LayerPeek/releases";

    private readonly IHttpClientFactory _httpClients;
    private readonly Uri _apiBase;

    /// <param name="apiBase">
    /// The API to ask; default <see cref="DefaultApiBase"/>. The test mode may point it to a server on the loopback
    /// (Q10 a); never configurable otherwise.
    /// </param>
    public GitHubReleaseChecker(IHttpClientFactory httpClients, Uri? apiBase = null)
    {
        ArgumentNullException.ThrowIfNull(httpClients);
        _httpClients = httpClients;
        _apiBase = apiBase ?? DefaultApiBase;
        if (!_apiBase.IsAbsoluteUri || !_apiBase.AbsolutePath.EndsWith('/'))
        {
            throw new ArgumentException("The API base must be an absolute address ending with '/'.", nameof(apiBase));
        }
    }

    /// <summary>The request the checker makes (tests).</summary>
    internal Uri RequestUri => new(_apiBase, $"{ReleasesPath}?per_page={PageSize}");

    /// <summary>The headers GitHub asks for: a User-Agent (required), the JSON media type and the API version.</summary>
    public static void ConfigureClient(HttpClient client, string productVersion)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.Timeout = Timeout;
        client.MaxResponseContentBufferSize = MaxResponseBytes;
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LayerPeek", string.IsNullOrWhiteSpace(productVersion) ? "0" : productVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<UpdateRelease?> CheckAsync(bool includePrereleases, CancellationToken cancellationToken)
    {
        HttpClient client = _httpClients.CreateClient(HttpClientName);
        using HttpRequestMessage request = new(HttpMethod.Get, RequestUri);
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // 403 / 429: the rate limit of anonymous requests; 404: no such repository (renamed, deleted). No body is logged.
            string reason = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                ? $" (rate limit; remaining {Header(response, "X-RateLimit-Remaining") ?? "?"}, reset {Header(response, "X-RateLimit-Reset") ?? "?"})"
                : string.Empty;
            throw new HttpRequestException($"GitHub answered {(int)response.StatusCode} ({response.StatusCode}){reason}.", null, response.StatusCode);
        }

        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        List<GitHubReleaseDocument?>? releases = JsonSerializer.Deserialize(body, GitHubJsonContext.Default.ListGitHubReleaseDocument)
            ?? throw new JsonException("GitHub answered null instead of a list of releases.");
        return Newest(releases, includePrereleases);
    }

    /// <summary>The newest release by version among <paramref name="releases"/> that may be offered (pure; tests).</summary>
    internal static UpdateRelease? Newest(IEnumerable<GitHubReleaseDocument?> releases, bool includePrereleases)
    {
        ArgumentNullException.ThrowIfNull(releases);
        UpdateRelease? newest = null;
        foreach (GitHubReleaseDocument? release in releases)
        {
            if (release is null || release.Draft || !ReleaseVersion.TryParse(release.TagName, out ReleaseVersion? version))
            {
                continue;
            }

            bool prerelease = release.Prerelease || version.IsPrerelease;
            if (prerelease && !includePrereleases)
            {
                continue;
            }

            if (newest is null || version > newest.Version)
            {
                // Only a release page on github.com is ever opened; otherwise the page of the tag.
                Uri page = GitHubReleasePage.TryCreate(release.HtmlUrl, out Uri? html) ? html : UpdateRelease.PageFor(version);
                newest = new UpdateRelease(version, page, prerelease);
            }
        }

        return newest;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
}

/// <summary>The fields of a GitHub release the checker reads; everything else is ignored.</summary>
internal sealed class GitHubReleaseDocument
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(List<GitHubReleaseDocument?>))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
