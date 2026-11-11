using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Installer;
using LayerPeek.Core.Updates;
using LayerPeek.Tests.Shared;

using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace LayerPeek.App.Tests.Updates;

/// <summary>
/// The signed update sources (T-181) on the shared test vector (<see cref="TestUpdateFeed"/>), with no network: the folder feed
/// of the end-to-end tests in a temporary folder, the loopback feed and GitHub through a fake <see cref="HttpMessageHandler"/>
/// (GitHub's release list through a fake Velopack downloader). A feed is handed to Velopack only with a valid signature of a
/// key trusted by that source; the test key never counts for GitHub.
/// </summary>
public sealed class SignedUpdateSourceTests : IDisposable
{
    private const string Rid = TestUpdateFeed.Rid;
    private const string FeedName = $"releases.{Rid}.json";
    private const string Package = "LayerPeek-0.3.2-osx-arm64-full.nupkg";

    private static readonly byte[] Feed = File.ReadAllBytes(TestUpdateFeed.FeedPath);
    private static readonly byte[] Signature = File.ReadAllBytes(TestUpdateFeed.SignaturePath);

    private readonly string _folder = TestDirectories.NewDirectoryPath();

    public SignedUpdateSourceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => TestDirectories.Delete(_folder);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

#if DEBUG
    [Fact]
    public async Task TheTestFolderFeedWithAValidSignatureIsHandedToVelopack()
    {
        WriteFeed(Feed, Signature);

        VelopackAssetFeed feed = await FolderSource().GetReleaseFeed(NullVelopackLogger.Instance, "LayerPeek", Rid);

        VelopackAsset asset = Assert.Single(feed.Assets);
        Assert.Equal("0.3.2", asset.Version.ToString());
        Assert.Equal(Package, asset.FileName);
        Assert.Equal("41401C9184A1CABEC40E319558BF398D08694DC31AD691D637340480ED3BD68A", asset.SHA256);
    }

    [Fact]
    public async Task AFeedWithAByteOrderMarkIsReadWhenItsSignatureCoversIt()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Feed];
        WriteFeed(withBom, TestUpdateFeed.Sign(withBom));

        VelopackAssetFeed feed = await FolderSource().GetReleaseFeed(NullVelopackLogger.Instance, "LayerPeek", Rid);

        Assert.Equal("0.3.2", Assert.Single(feed.Assets).Version.ToString());
    }

    [Fact]
    public void TheTestKeyOfTheAppIsTheKeyOfTheVector()
    {
        Assert.Equal(TestUpdateFeed.PublicKeyPem.Trim(), Assert.Single(UpdateFeedKeys.Test).Trim());
    }
#else
    [Fact]
    public void AReleaseBuildTrustsNoTestKey()
    {
        Assert.Empty(UpdateFeedKeys.Test);
    }
#endif

    [Fact]
    public async Task AFeedWithoutSignatureIsRejectedWithItsClaimedVersionAndHash()
    {
        WriteFeed(Feed, signature: null);

        RejectedUpdateFeed rejected = await RejectedAsync(FolderSource());

        Assert.Equal(new RejectedUpdateFeed(FeedName, UpdateFeedRejection.SignatureMissing, ReleaseVersion.Parse("0.3.2"), Convert.ToHexString(SHA256.HashData(Feed))), rejected);
    }

    [Fact]
    public async Task AFeedChangedInOneByteIsRejected()
    {
        byte[] changed = [.. Feed];
        changed[^3] ^= 0x01;
        WriteFeed(changed, Signature);

        Assert.Equal(UpdateFeedRejection.SignatureUntrusted, (await RejectedAsync(FolderSource())).Reason);
    }

    [Fact]
    public async Task AFeedSignedByAnotherKeyIsRejected()
    {
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        WriteFeed(Feed, TestUpdateFeed.Sign(Feed, key: other));

        Assert.Equal(UpdateFeedRejection.SignatureUntrusted, (await RejectedAsync(FolderSource())).Reason);
    }

    [Fact]
    public async Task TheSignatureOfAnotherRidsFeedIsRejected()
    {
        // The osx-arm64 feed and its signature, offered as the feed of win-x64.
        File.WriteAllBytes(Path.Combine(_folder, "releases.win-x64.json"), Feed);
        File.WriteAllBytes(Path.Combine(_folder, "releases.win-x64.json.sig"), Signature);

        RejectedUpdateFeed rejected = await RejectedAsync(FolderSource(), "win-x64");

        Assert.Equal("releases.win-x64.json", rejected.FeedName);
        Assert.Equal(UpdateFeedRejection.SignatureUntrusted, rejected.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(UpdateFeedVerifier.MaxSignatureFileBytes + 1)]
    [InlineData(4 * 1024 * 1024)]
    public async Task AnEmptyCorruptOrOversizedSignatureIsRejected(int length)
    {
        WriteFeed(Feed, Enumerable.Repeat((byte)'A', length).ToArray());

        Assert.Equal(UpdateFeedRejection.SignatureMalformed, (await RejectedAsync(FolderSource())).Reason);
    }

    [Fact]
    public async Task AnOversizedFeedIsRejectedUnread()
    {
        byte[] huge = new byte[SignedUpdateSource.MaxFeedBytes + 1];
        WriteFeed(huge, TestUpdateFeed.Sign(huge));

        Assert.Equal(new RejectedUpdateFeed(FeedName, UpdateFeedRejection.FeedTooLarge, null, null), await RejectedAsync(FolderSource()));
    }

    [Fact]
    public async Task AFeedThatIsNoJsonNamesNoVersion()
    {
        byte[] garbage = Encoding.UTF8.GetBytes("{ not json");
        WriteFeed(garbage, Signature);

        RejectedUpdateFeed rejected = await RejectedAsync(FolderSource());

        Assert.Null(rejected.ClaimedVersion);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(garbage)), rejected.FeedSha256);
    }

    [Fact]
    public async Task AFolderWithoutFeedOffersNothingAndNeverItsUnsignedPackages()
    {
        // SimpleFileSource would read the .nupkg files of the folder, which no signature covers.
        File.WriteAllBytes(Path.Combine(_folder, Package), [1, 2, 3]);

        VelopackAssetFeed feed = await FolderSource().GetReleaseFeed(NullVelopackLogger.Instance, "LayerPeek", Rid);

        Assert.Empty(feed.Assets);
    }

    [Fact]
    public async Task TheLoopbackFeedReadsTheFeedAndItsSignatureOnly()
    {
        FakeHttp http = new();
        http.Serve(FeedName, Feed);
        http.Serve(FeedName + ".sig", Signature);

        IUpdateSource source = SignedUpdateSource.ForTest(new UpdateSource(new Uri("http://127.0.0.1:41234/feed"), null), http, Token);
        Exception? failure = await Record.ExceptionAsync(() => source.GetReleaseFeed(NullVelopackLogger.Instance, "LayerPeek", Rid));

#if DEBUG
        Assert.Null(failure);
#else
        Assert.IsType<UpdateFeedRejectedException>(failure);
#endif
        Assert.Equal(["http://127.0.0.1:41234/feed/releases.osx-arm64.json", "http://127.0.0.1:41234/feed/releases.osx-arm64.json.sig"], http.Requested);
        Assert.All(http.Clients, name => Assert.Equal(SignedUpdateSource.HttpClientName, name));
    }

    [Fact]
    public async Task AMissingSignatureOnTheLoopbackIsRejected()
    {
        FakeHttp http = new();
        http.Serve(FeedName, Feed);

        IUpdateSource source = SignedUpdateSource.ForTest(new UpdateSource(new Uri("http://127.0.0.1:41234/"), null), http, Token);

        Assert.Equal(UpdateFeedRejection.SignatureMissing, (await RejectedAsync(source)).Reason);
    }

    [Fact]
    public async Task AnAnnouncedOversizedFeedIsNotDownloaded()
    {
        FakeHttp http = new();
        http.Serve(FeedName, Feed, announcedLength: SignedUpdateSource.MaxFeedBytes + 1L);
        http.Serve(FeedName + ".sig", Signature);

        IUpdateSource source = SignedUpdateSource.ForTest(new UpdateSource(new Uri("http://127.0.0.1:41234/"), null), http, Token);

        Assert.Equal(UpdateFeedRejection.FeedTooLarge, (await RejectedAsync(source)).Reason);
    }

    [Fact]
    public async Task AnUnannouncedOversizedSignatureIsReadOnlyUpToItsLimit()
    {
        FakeHttp http = new();
        http.Serve(FeedName, Feed);
        http.Serve(FeedName + ".sig", new byte[1024 * 1024], announcedLength: null);

        IUpdateSource source = SignedUpdateSource.ForTest(new UpdateSource(new Uri("http://127.0.0.1:41234/"), null), http, Token);

        Assert.Equal(UpdateFeedRejection.SignatureMalformed, (await RejectedAsync(source)).Reason);
        Assert.True(http.BytesRead(FeedName + ".sig") <= 32 * 1024, $"read {http.BytesRead(FeedName + ".sig")} bytes");
    }

    [Fact]
    public async Task AFailedDownloadOfTheSignatureFailsTheCheckButIsNoRejection()
    {
        FakeHttp http = new();
        http.Serve(FeedName, Feed);
        http.Fail(FeedName + ".sig", HttpStatusCode.ServiceUnavailable);

        IUpdateSource source = SignedUpdateSource.ForTest(new UpdateSource(new Uri("http://127.0.0.1:41234/"), null), http, Token);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetReleaseFeed(NullVelopackLogger.Instance, "LayerPeek", Rid));
    }

    [Fact]
    public async Task TheGitHubFeedNeverTrustsTheTestKey()
    {
        // A feed with a valid signature of the test key, which the folder source of a Debug build accepts (above).
        FakeHttp http = new();
        http.Serve("v0.3.2/" + FeedName, Feed);
        http.Serve("v0.3.2/" + FeedName + ".sig", Signature);
        FakeReleaseList releases = new(Release("v0.3.2", FeedName, FeedName + ".sig", Package));

        RejectedUpdateFeed rejected = await RejectedAsync(SignedUpdateSource.ForGitHub(prereleases: true, http, releases, Token));

        Assert.Equal(UpdateFeedRejection.SignatureUntrusted, rejected.Reason);
        Assert.Equal(ReleaseVersion.Parse("0.3.2"), rejected.ClaimedVersion);
        Assert.Empty(releases.Downloads);
        Assert.DoesNotContain(TestUpdateFeed.PublicKeyPem.Trim(), UpdateFeedKeys.Production.Select(static key => key.Trim()));
    }

    [Fact]
    public async Task TheGitHubFeedReadsOnlyTheNewestReleaseWithThisRidsFeed()
    {
        FakeHttp http = new();
        http.Serve("v0.3.2/" + FeedName, Feed);
        // 0.3.3 has only its Windows files yet; 0.3.1 is older and never read.
        FakeReleaseList releases = new(
            Release("v0.3.3", "releases.win-x64.json", "releases.win-x64.json.sig"),
            Release("v0.3.2", FeedName, Package),
            Release("v0.3.1", FeedName, FeedName + ".sig"));

        RejectedUpdateFeed rejected = await RejectedAsync(SignedUpdateSource.ForGitHub(prereleases: true, http, releases, Token));

        Assert.Equal(UpdateFeedRejection.SignatureMissing, rejected.Reason);
        Assert.Equal([$"https://github.com/pfoltynek/LayerPeek/releases/download/v0.3.2/{FeedName}"], http.Requested);
        Assert.Empty(releases.Downloads);
    }

    [Fact]
    public void EveryProductionKeyIsAP256PublicKey()
    {
        // Empty until the user adds the real key (placeholder, T-181): then every GitHub feed is rejected.
        Assert.Equal(UpdateFeedKeys.Production.Count, new UpdateFeedVerifier(UpdateFeedKeys.Production).TrustedKeyCount);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    public async Task TheEngineOffersNothingFromAnUnsignedFeedAndDownloadsNothing()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS() || OperatingSystem.IsWindows(), "Velopack's engine runs on Windows and macOS only.");
        string packages = Path.Combine(_folder, "packages");
        Directory.CreateDirectory(packages);
        WriteFeed(Feed, signature: null);
        File.WriteAllBytes(Path.Combine(_folder, Package), [1, 2, 3]);
        VelopackUpdateEngine engine = Engine(packages);

        UpdateFeedRejectedException rejected = await Assert.ThrowsAsync<UpdateFeedRejectedException>(() => engine.CheckAsync(prereleases: true, Token));
        Assert.Equal(UpdateFeedRejection.SignatureMissing, rejected.Feed.Reason);

        // The tray's "install" after a remembered offer checks the feed again first: it refuses, nothing is downloaded.
        AppUpdates updates = new(engine, NullLogger<AppUpdates>.Instance);
        bool quit = false;
        ReleaseVersion next = ReleaseVersion.Parse("0.3.2");
        Assert.False(await updates.ApplyAsync(new UpdateRelease(next, UpdateRelease.PageFor(next), false), true, () => { quit = true; return Task.CompletedTask; }, Token));
        Assert.False(quit);
        Assert.True(updates.IsFeedRejected);
        Assert.Empty(Directory.EnumerateFiles(packages, "*.nupkg*"));
    }

#if DEBUG
    [Fact]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    public async Task TheEngineOffersTheUpdateOfASignedFeed()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS() || OperatingSystem.IsWindows(), "Velopack's engine runs on Windows and macOS only.");
        string packages = Path.Combine(_folder, "packages");
        Directory.CreateDirectory(packages);
        WriteFeed(Feed, Signature);

        AvailableUpdate? update = await Engine(packages).CheckAsync(prereleases: true, Token);

        Assert.Equal("0.3.2", update?.Version);
    }
#endif

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    private VelopackUpdateEngine Engine(string packages) =>
        new(new FakeHttp(), new UpdateSource(null, _folder), new TestVelopackLocator("LayerPeek", "0.3.1", packages, appDir: null, rootDir: null, updateExe: null, channel: Rid));

    private IUpdateSource FolderSource() => SignedUpdateSource.ForTest(new UpdateSource(null, _folder), new FakeHttp(), Token);

    private void WriteFeed(byte[] feed, byte[]? signature)
    {
        File.WriteAllBytes(Path.Combine(_folder, FeedName), feed);
        if (signature is not null)
        {
            File.WriteAllBytes(Path.Combine(_folder, FeedName + ".sig"), signature);
        }
    }

    private static async Task<RejectedUpdateFeed> RejectedAsync(IUpdateSource source, string channel = Rid) =>
        (await Assert.ThrowsAsync<UpdateFeedRejectedException>(() => source.GetReleaseFeed(NullVelopackLogger.Instance, "LayerPeek", channel))).Feed;

    /// <summary>A release of the GitHub API with its assets, downloaded from <c>…/releases/download/&lt;tag&gt;/&lt;name&gt;</c>.</summary>
    private static JsonObject Release(string tag, params string[] assets) => new()
    {
        ["name"] = tag,
        ["prerelease"] = true,
        ["published_at"] = $"2026-11-{20 + int.Parse(tag[^1..], System.Globalization.CultureInfo.InvariantCulture)}T08:00:00Z",
        ["assets"] = new JsonArray([.. assets.Select(name => (JsonNode)new JsonObject
        {
            ["name"] = name,
            ["url"] = $"https://api.github.com/repos/pfoltynek/LayerPeek/releases/assets/{name.Length}",
            ["browser_download_url"] = $"https://github.com/pfoltynek/LayerPeek/releases/download/{tag}/{name}",
        })]),
    };

    /// <summary>Velopack's downloader for GitHub's release list; records any other download (a package must never come).</summary>
    private sealed class FakeReleaseList(params JsonObject[] releases) : IFileDownloader
    {
        public List<string> Downloads { get; } = [];

        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Assert.Equal("https://api.github.com/repos/pfoltynek/LayerPeek/releases?per_page=10&page=1", url);
            return Task.FromResult(new JsonArray([.. releases]).ToJsonString());
        }

        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Downloads.Add(url);
            throw new InvalidOperationException("Velopack's downloader must not read a feed.");
        }

        public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
        {
            Downloads.Add(url);
            throw new InvalidOperationException("No package may be downloaded.");
        }
    }

    /// <summary>The named clients of the feed over a fake handler; files are served by the end of their path.</summary>
    private sealed class FakeHttp : HttpMessageHandler, IHttpClientFactory
    {
        private readonly Dictionary<string, (byte[] Body, long? Length)> _files = [];
        private readonly Dictionary<string, HttpStatusCode> _failures = [];
        private readonly Dictionary<string, CountingStream> _streams = [];

        public List<string> Requested { get; } = [];

        public List<string> Clients { get; } = [];

        public void Serve(string pathEnd, byte[] body, long? announcedLength = -1) =>
            _files[pathEnd] = (body, announcedLength == -1 ? body.Length : announcedLength);

        public void Fail(string pathEnd, HttpStatusCode status) => _failures[pathEnd] = status;

        public long BytesRead(string pathEnd) => _streams[pathEnd].BytesRead;

        public HttpClient CreateClient(string name)
        {
            Clients.Add(name);
            return new HttpClient(this, disposeHandler: false);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string uri = request.RequestUri!.AbsoluteUri;
            Requested.Add(uri);
            if (_failures.FirstOrDefault(failure => uri.EndsWith("/" + failure.Key, StringComparison.Ordinal)) is { Key: not null } failed)
            {
                return Task.FromResult(new HttpResponseMessage(failed.Value));
            }

            if (_files.FirstOrDefault(file => uri.EndsWith("/" + file.Key, StringComparison.Ordinal)) is not { Key: not null } served)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            CountingStream stream = new(served.Value.Body);
            _streams[served.Key] = stream;
            StreamContent content = new(stream);
            content.Headers.ContentLength = served.Value.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    /// <summary>A body that counts what was read of it and announces no length of its own.</summary>
    private sealed class CountingStream(byte[] body) : MemoryStream(body)
    {
        public long BytesRead { get; private set; }

        public override bool CanSeek => false;

        public override int Read(byte[] buffer, int offset, int count) => Count(base.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(base.Read(buffer));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await base.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private int Count(int read)
        {
            BytesRead += read;
            return read;
        }
    }
}
