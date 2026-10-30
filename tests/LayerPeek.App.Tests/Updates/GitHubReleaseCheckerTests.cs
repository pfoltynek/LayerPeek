using System.Net;
using System.Text;
using System.Text.Json;

using LayerPeek.App.Updates;
using LayerPeek.Core.Updates;

namespace LayerPeek.App.Tests.Updates;

/// <summary>
/// The GitHub REST checker of T-058 against a recorded answer of <c>GET /repos/pfoltynek/LayerPeek/releases</c>
/// (Fixtures/github-releases-2026-10-30.json: v0.3.0 and v0.1.0 marked as pre-releases, v0.2.0 not) and a fake
/// <see cref="HttpMessageHandler"/>: no real network. Also 403 (rate limit), 404, a timeout and invalid JSON.
/// </summary>
public sealed class GitHubReleaseCheckerTests
{
    private static readonly string Recorded = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "github-releases-2026-10-30.json"));

    [Fact]
    public async Task WithPrereleasesTheNewestVersionWins()
    {
        FakeHandler handler = new(HttpStatusCode.OK, Recorded);

        UpdateRelease? release = await Checker(handler).CheckAsync(includePrereleases: true, TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseVersion.Parse("0.3.0"), release?.Version);
        Assert.True(release?.IsPrerelease);
        Assert.Equal(new Uri("https://github.com/pfoltynek/LayerPeek/releases/tag/v0.3.0"), release?.ReleasePage);
    }

    [Fact]
    public async Task WithoutPrereleasesOnlyReleasesCount()
    {
        UpdateRelease? release = await Checker(new FakeHandler(HttpStatusCode.OK, Recorded)).CheckAsync(includePrereleases: false, TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseVersion.Parse("0.2.0"), release?.Version);
        Assert.False(release?.IsPrerelease);
    }

    [Fact]
    public async Task TheRequestIsTheReleaseListWithTheHeadersGitHubAsksFor()
    {
        FakeHandler handler = new(HttpStatusCode.OK, Recorded);
        HttpClient client = new(handler, disposeHandler: false);
        GitHubReleaseChecker.ConfigureClient(client, "0.3.1");

        await new GitHubReleaseChecker(new FakeHttpClientFactory(client)).CheckAsync(true, TestContext.Current.CancellationToken);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://api.github.com/repos/pfoltynek/LayerPeek/releases?per_page=30"), request.RequestUri);
        Assert.Equal("LayerPeek/0.3.1", client.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Equal("application/vnd.github+json", Assert.Single(client.DefaultRequestHeaders.Accept).MediaType);
        Assert.Equal("2022-11-28", Assert.Single(client.DefaultRequestHeaders.GetValues("X-GitHub-Api-Version")));
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.Equal(GitHubReleaseChecker.Timeout, client.Timeout);
        Assert.Equal(GitHubReleaseChecker.MaxResponseBytes, client.MaxResponseContentBufferSize);
    }

    [Fact]
    public async Task ATestAddressReplacesTheApi()
    {
        FakeHandler handler = new(HttpStatusCode.OK, "[]");

        UpdateRelease? release = await Checker(handler, new Uri("http://127.0.0.1:41234/")).CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Null(release);
        Assert.Equal(new Uri("http://127.0.0.1:41234/repos/pfoltynek/LayerPeek/releases?per_page=30"), Assert.Single(handler.Requests).RequestUri);
    }

    [Fact]
    public async Task TheRateLimitIsAnErrorWithItsStatus()
    {
        FakeHandler handler = new(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded"}""");
        handler.Headers["X-RateLimit-Remaining"] = "0";
        handler.Headers["X-RateLimit-Reset"] = "1793350000";

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => Checker(handler).CheckAsync(true, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Contains("rate limit", error.Message, StringComparison.Ordinal);
        Assert.Contains("remaining 0", error.Message, StringComparison.Ordinal);
        // The body is never part of the message.
        Assert.DoesNotContain("exceeded", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task AnUnsuccessfulAnswerIsAnError(HttpStatusCode status)
    {
        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => Checker(new FakeHandler(status, "{}")).CheckAsync(true, TestContext.Current.CancellationToken));

        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task ATimeoutIsAnError()
    {
        FakeHandler handler = new(HttpStatusCode.OK, Recorded) { Delay = Timeout.InfiniteTimeSpan };
        HttpClient client = new(handler, disposeHandler: false) { Timeout = TimeSpan.FromMilliseconds(50) };

        await Assert.ThrowsAnyAsync<TaskCanceledException>(() => new GitHubReleaseChecker(new FakeHttpClientFactory(client)).CheckAsync(true, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"message":"Not a list"}""")]
    [InlineData("null")]
    [InlineData("""[{"tag_name": 5}]""")]
    public async Task InvalidJsonIsAnError(string body)
    {
        await Assert.ThrowsAsync<JsonException>(() => Checker(new FakeHandler(HttpStatusCode.OK, body)).CheckAsync(true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DraftsInvalidTagsAndPrereleasesAreSkippedAsAskedFor()
    {
        GitHubReleaseDocument?[] releases =
        [
            null,
            new() { TagName = "v9.0.0", Draft = true, HtmlUrl = "https://github.com/pfoltynek/LayerPeek/releases/tag/v9.0.0" },
            new() { TagName = "nightly", HtmlUrl = "https://github.com/pfoltynek/LayerPeek/releases/tag/nightly" },
            new() { TagName = "v1.1.0-rc.1", HtmlUrl = "https://github.com/pfoltynek/LayerPeek/releases/tag/v1.1.0-rc.1" },
            new() { TagName = "v1.0.0", HtmlUrl = "https://github.com/pfoltynek/LayerPeek/releases/tag/v1.0.0" },
            new() { TagName = "v0.9.0", HtmlUrl = "https://github.com/pfoltynek/LayerPeek/releases/tag/v0.9.0" },
        ];

        // A suffix makes a release a pre-release even when GitHub does not mark it.
        Assert.Equal(ReleaseVersion.Parse("1.0.0"), GitHubReleaseChecker.Newest(releases, includePrereleases: false)?.Version);
        Assert.Equal(ReleaseVersion.Parse("1.1.0-rc.1"), GitHubReleaseChecker.Newest(releases, includePrereleases: true)?.Version);
        Assert.Null(GitHubReleaseChecker.Newest([], includePrereleases: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://evil.example/pfoltynek/LayerPeek/releases/tag/v1.0.0")]
    [InlineData("http://github.com/pfoltynek/LayerPeek/releases/tag/v1.0.0")]
    [InlineData("javascript:alert(1)")]
    public void APageOffGitHubIsReplacedByTheTagPage(string? htmlUrl)
    {
        UpdateRelease? release = GitHubReleaseChecker.Newest([new GitHubReleaseDocument { TagName = "v1.0.0", HtmlUrl = htmlUrl }], includePrereleases: false);

        Assert.Equal(new Uri("https://github.com/pfoltynek/LayerPeek/releases/tag/v1.0.0"), release?.ReleasePage);
    }

    [Fact]
    public void TheApiBaseMustEndWithASlash()
    {
        Assert.Throws<ArgumentException>(() => new GitHubReleaseChecker(new FakeHttpClientFactory(new HttpClient()), new Uri("http://127.0.0.1:1/api")));
    }

    private static GitHubReleaseChecker Checker(FakeHandler handler, Uri? apiBase = null) =>
        new(new FakeHttpClientFactory(new HttpClient(handler, disposeHandler: false)), apiBase);

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(GitHubReleaseChecker.HttpClientName, name);
            return client;
        }
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public Dictionary<string, string> Headers { get; } = [];

        public TimeSpan Delay { get; init; } = TimeSpan.Zero;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Delay != TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            HttpResponseMessage response = new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json"), RequestMessage = request };
            foreach ((string name, string value) in Headers)
            {
                response.Headers.Add(name, value);
            }

            return response;
        }
    }
}
