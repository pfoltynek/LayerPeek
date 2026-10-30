using System.Net;
using System.Text;
using System.Text.Json;

using LayerPeek.App.Updates;
using LayerPeek.Core.Updates;

namespace LayerPeek.App.Tests.Updates;

/// <summary>
/// The GitHub REST checker of T-058 against a recorded answer of <c>GET /repos/pfoltynek/LayerPeek/releases</c>
/// (Fixtures/github-releases-2026-10-30.json: v0.3.0 with the macOS files only and marked as a pre-release, v0.2.0 and
/// v0.1.0 with Windows files but no checksum file) and a fake <see cref="HttpMessageHandler"/>: no real network. Also
/// 403 (rate limit), 404, a timeout and invalid JSON. T-113: a release counts only for the OS whose
/// <c>SHA256SUMS-&lt;os&gt;.txt</c> it has (Fixtures/github-releases-split-os.json, made from the recorded answer: the
/// OSes of one version published at different times, an interrupted upload).
/// </summary>
public sealed class GitHubReleaseCheckerTests
{
    private static readonly string Recorded = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "github-releases-2026-10-30.json"));

    private static readonly string SplitOs = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "github-releases-split-os.json"));

    [Fact]
    public async Task WithPrereleasesTheNewestVersionWithTheFilesOfThisOsWins()
    {
        FakeHandler handler = new(HttpStatusCode.OK, Recorded);

        UpdateRelease? release = await Checker(handler, os: ReleaseOs.MacOS).CheckAsync(includePrereleases: true, TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseVersion.Parse("0.3.0"), release?.Version);
        Assert.True(release?.IsPrerelease);
        Assert.Equal(new Uri("https://github.com/pfoltynek/LayerPeek/releases/tag/v0.3.0"), release?.ReleasePage);
    }

    [Theory]
    [InlineData("macos")]
    [InlineData("windows")]
    public async Task WithoutPrereleasesOnlyReleasesWithTheChecksumFileOfThisOsCount(string os)
    {
        // v0.2.0 is the only release that is no pre-release, and it has no checksum file (published before T-055 named them).
        UpdateRelease? release = await Checker(new FakeHandler(HttpStatusCode.OK, Recorded), os: Os(os)).CheckAsync(includePrereleases: false, TestContext.Current.CancellationToken);

        Assert.Null(release);
    }

    [Fact]
    public async Task AReleaseWithTheFilesOfTheOtherOsOnlyIsNotOfferedOnWindows()
    {
        // The recorded v0.3.0 was published with the macOS files only.
        UpdateRelease? release = await Checker(new FakeHandler(HttpStatusCode.OK, Recorded), os: ReleaseOs.Windows).CheckAsync(includePrereleases: true, TestContext.Current.CancellationToken);

        Assert.Null(release);
    }

    [Theory]
    // v1.1.0 has the macOS files; its Windows checksum file is still being uploaded (state "starter").
    [InlineData("macos", false, "1.1.0")]
    // v1.0.0 has the Windows files only.
    [InlineData("windows", false, "1.0.0")]
    // v1.2.0-rc.1 has both; v2.0.0 has packages but the checksum file of neither OS.
    [InlineData("macos", true, "1.2.0-rc.1")]
    [InlineData("windows", true, "1.2.0-rc.1")]
    public async Task EachOsIsOfferedTheNewestReleaseThatHasItsChecksumFile(string os, bool includePrereleases, string expected)
    {
        UpdateRelease? release = await Checker(new FakeHandler(HttpStatusCode.OK, SplitOs), os: Os(os)).CheckAsync(includePrereleases, TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseVersion.Parse(expected), release?.Version);
        Assert.Equal(new Uri($"https://github.com/pfoltynek/LayerPeek/releases/tag/v{expected}"), release?.ReleasePage);
    }

    [Fact]
    public void TheChecksumFilesAreTheOnesTheReleaseScriptsUpload()
    {
        // scripts/ReleaseTools/ReleaseTools.psm1, Get-ChecksumFileName: "SHA256SUMS-$Os.txt" with the OS 'windows' / 'macos'.
        Assert.Equal("SHA256SUMS-windows.txt", ReleaseOs.Windows.ChecksumFileName());
        Assert.Equal("SHA256SUMS-macos.txt", ReleaseOs.MacOS.ChecksumFileName());
    }

    [Fact]
    public void TheDefaultOsIsTheOneTheAppRunsOn()
    {
        ReleaseOs? expected = OperatingSystem.IsWindows() ? ReleaseOs.Windows : OperatingSystem.IsMacOS() ? ReleaseOs.MacOS : null;

        Assert.Equal(expected, ReleaseOsExtensions.Current);
    }

    [Fact]
    public void AnOsWithoutReleasesIsOfferedNothing()
    {
        Assert.Null(GitHubReleaseChecker.Newest([Release("v1.0.0", ReleaseOs.Windows, ReleaseOs.MacOS)], includePrereleases: true, os: null));
    }

    [Theory]
    [InlineData("starter", false)]
    [InlineData("uploaded", true)]
    [InlineData(null, true)]
    [InlineData("Uploaded", false)]
    public void OnlyAChecksumFileThatIsCompletelyUploadedCounts(string? state, bool offered)
    {
        GitHubReleaseDocument release = new()
        {
            TagName = "v1.0.0",
            HtmlUrl = "https://github.com/pfoltynek/LayerPeek/releases/tag/v1.0.0",
            Assets = [null, new() { Name = "sha256sums-macos.txt" }, new() { Name = "SHA256SUMS-macos.txt", State = state }],
        };

        Assert.Equal(offered, GitHubReleaseChecker.Newest([release], includePrereleases: false, ReleaseOs.MacOS) is not null);
    }

    [Fact]
    public void AReleaseWithoutAssetsIsSkipped()
    {
        GitHubReleaseDocument release = new() { TagName = "v1.0.0", HtmlUrl = "https://github.com/pfoltynek/LayerPeek/releases/tag/v1.0.0" };

        Assert.Null(GitHubReleaseChecker.Newest([release], includePrereleases: false, ReleaseOs.Windows));
        Assert.Null(GitHubReleaseChecker.Newest([Release("v1.0.0")], includePrereleases: false, ReleaseOs.Windows));
    }

    [Fact]
    public async Task TheRequestIsTheReleaseListWithTheHeadersGitHubAsksFor()
    {
        FakeHandler handler = new(HttpStatusCode.OK, Recorded);
        HttpClient client = new(handler, disposeHandler: false);
        GitHubReleaseChecker.ConfigureClient(client, "0.3.1");

        await new GitHubReleaseChecker(new FakeHttpClientFactory(client), os: ReleaseOs.Windows).CheckAsync(true, TestContext.Current.CancellationToken);

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
        GitHubReleaseDocument? draft = Release("v9.0.0", ReleaseOs.Windows);
        draft!.Draft = true;
        GitHubReleaseDocument?[] releases =
        [
            null,
            draft,
            Release("nightly", ReleaseOs.Windows),
            Release("v1.1.0-rc.1", ReleaseOs.Windows),
            Release("v1.0.0", ReleaseOs.Windows),
            Release("v0.9.0", ReleaseOs.Windows),
        ];

        // A suffix makes a release a pre-release even when GitHub does not mark it.
        Assert.Equal(ReleaseVersion.Parse("1.0.0"), GitHubReleaseChecker.Newest(releases, includePrereleases: false, ReleaseOs.Windows)?.Version);
        Assert.Equal(ReleaseVersion.Parse("1.1.0-rc.1"), GitHubReleaseChecker.Newest(releases, includePrereleases: true, ReleaseOs.Windows)?.Version);
        Assert.Null(GitHubReleaseChecker.Newest([], includePrereleases: true, ReleaseOs.Windows));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://evil.example/pfoltynek/LayerPeek/releases/tag/v1.0.0")]
    [InlineData("http://github.com/pfoltynek/LayerPeek/releases/tag/v1.0.0")]
    [InlineData("javascript:alert(1)")]
    public void APageOffGitHubIsReplacedByTheTagPage(string? htmlUrl)
    {
        GitHubReleaseDocument? document = Release("v1.0.0", ReleaseOs.MacOS);
        document!.HtmlUrl = htmlUrl;
        UpdateRelease? release = GitHubReleaseChecker.Newest([document], includePrereleases: false, ReleaseOs.MacOS);

        Assert.Equal(new Uri("https://github.com/pfoltynek/LayerPeek/releases/tag/v1.0.0"), release?.ReleasePage);
    }

    [Fact]
    public void TheApiBaseMustEndWithASlash()
    {
        Assert.Throws<ArgumentException>(() => new GitHubReleaseChecker(new FakeHttpClientFactory(new HttpClient()), new Uri("http://127.0.0.1:1/api")));
    }

    private static GitHubReleaseChecker Checker(FakeHandler handler, Uri? apiBase = null, ReleaseOs os = ReleaseOs.Windows) =>
        new(new FakeHttpClientFactory(new HttpClient(handler, disposeHandler: false)), apiBase, os);

    /// <summary>The OS of a test case (the OS names of the release scripts; the enum is internal to the app).</summary>
    private static ReleaseOs Os(string name) => name switch
    {
        "windows" => ReleaseOs.Windows,
        "macos" => ReleaseOs.MacOS,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>A published release with the checksum files of <paramref name="oses"/>.</summary>
    private static GitHubReleaseDocument? Release(string tag, params ReleaseOs[] oses) => new()
    {
        TagName = tag,
        HtmlUrl = $"https://github.com/pfoltynek/LayerPeek/releases/tag/{tag}",
        Assets = [.. oses.Select(os => new GitHubReleaseAssetDocument { Name = os.ChecksumFileName(), State = "uploaded" })],
    };

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
