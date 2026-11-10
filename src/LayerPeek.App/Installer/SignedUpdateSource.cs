using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

using LayerPeek.Core.Updates;

using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace LayerPeek.App.Installer;

/// <summary>
/// The Velopack update sources of the app with a signed feed (T-181): each reads the exact bytes of <c>releases.&lt;RID&gt;.json</c>
/// and of its signature <c>releases.&lt;RID&gt;.json.sig</c>, verifies them (<see cref="UpdateFeedVerifier"/>) and only then
/// hands the feed to Velopack, which checks every package against its size and SHA-256 in it. A refused feed throws
/// <see cref="UpdateFeedRejectedException"/>: Velopack sees no feed, so it offers nothing and downloads nothing.
/// <list type="bullet">
/// <item><b>Trust by construction:</b> <see cref="ForGitHub"/> verifies with <see cref="UpdateFeedKeys.Production"/> only and
/// <see cref="ForTest"/> with <see cref="UpdateFeedKeys.Test"/> only; the sources are private and take no keys, so no caller
/// can make the GitHub feed trust the test key.</item>
/// <item><b>GitHub:</b> like Velopack's <c>GitBase</c> (the 10 newest releases by publication, pre-releases when asked), but
/// only the feed of the newest release that has this RID's feed is read (a release whose OS is not uploaded yet is skipped,
/// T-113): one feed and one signature instead of up to 10 feeds, and an older release can never stand in for a refused newer
/// one. The newest release normally has the highest version (packed with <c>--delta None</c>, so no older feed is needed).</item>
/// <item><b>Bounded:</b> at most <see cref="MaxFeedBytes"/> of a feed and <see cref="UpdateFeedVerifier.MaxSignatureFileBytes"/>
/// of a signature are read, through the named client <see cref="HttpClientName"/> (<see cref="IHttpClientFactory"/>), within
/// <see cref="Timeout"/> each and cancelled with the check. The release list and the packages still come through Velopack's
/// own downloader.</item>
/// </list>
/// A source is made per check (it holds the check's token) and keeps no state.
/// </summary>
internal static class SignedUpdateSource
{
    /// <summary>The named <see cref="HttpClient"/> of the feed and its signature (registered by the composition root).</summary>
    public const string HttpClientName = "LayerPeek.UpdateFeed";

    /// <summary>A feed of one release is about 8 kB (its release notes included); anything far larger is no feed of the app.</summary>
    public const int MaxFeedBytes = 1024 * 1024;

    /// <summary>One download of the feed or its signature, headers and body, also on a slow connection.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static readonly UpdateFeedVerifier ProductionVerifier = new(UpdateFeedKeys.Production);
    private static readonly UpdateFeedVerifier TestVerifier = new(UpdateFeedKeys.Test);

    /// <summary>The User-Agent (GitHub asks for one) and the media type of a release asset.</summary>
    public static void ConfigureClient(HttpClient client, string productVersion)
    {
        ArgumentNullException.ThrowIfNull(client);
        // The timeout of a whole download is ours (headers and body, FetchAsync); this one only stops a request that hangs.
        client.Timeout = Timeout;
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LayerPeek", string.IsNullOrWhiteSpace(productVersion) ? "0" : productVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
    }

    /// <summary>The releases of <see cref="AppUpdates.RepositoryUrl"/>, trusting only <see cref="UpdateFeedKeys.Production"/>.</summary>
    /// <param name="cancellationToken">The check: cancels the downloads of the feed and its signature.</param>
    public static IUpdateSource ForGitHub(bool prereleases, IHttpClientFactory httpClients, CancellationToken cancellationToken) =>
        ForGitHub(prereleases, httpClients, releaseListDownloader: null, cancellationToken);

    /// <param name="releaseListDownloader">Tests only: answers the GitHub API's release list instead of Velopack's downloader.</param>
    internal static IUpdateSource ForGitHub(bool prereleases, IHttpClientFactory httpClients, IFileDownloader? releaseListDownloader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClients);
        return new GitHub(prereleases, httpClients, releaseListDownloader, cancellationToken);
    }

    /// <summary>The feed of the end-to-end test (Q10 a, T-058), trusting only <see cref="UpdateFeedKeys.Test"/>.</summary>
    /// <param name="source">From <see cref="UpdateSource.TryCreateForTest"/>: the loopback or a local folder.</param>
    /// <param name="cancellationToken">The check: cancels the reads of the feed and its signature.</param>
    public static IUpdateSource ForTest(UpdateSource source, IHttpClientFactory httpClients, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(httpClients);
        return source switch
        {
            { Folder: { } folder } => new Folder(folder, cancellationToken),
            { Address: { } address } => new Web(address, httpClients, cancellationToken),
            _ => throw new ArgumentException("The test source names neither a folder nor an address.", nameof(source)),
        };
    }

    private static string FeedNameOf(string channel) => $"releases.{channel}.json";

    /// <summary>The feed when a trusted key signed it; otherwise throws <see cref="UpdateFeedRejectedException"/>.</summary>
    private static VelopackAssetFeed Verified(UpdateFeedVerifier verifier, string channel, Fetched feed, Fetched signature)
    {
        UpdateFeedRejection? rejection = (feed.Status, signature.Status) switch
        {
            (FetchStatus.TooLarge, _) => UpdateFeedRejection.FeedTooLarge,
            (_, FetchStatus.Missing) => UpdateFeedRejection.SignatureMissing,
            (_, FetchStatus.TooLarge) => UpdateFeedRejection.SignatureMalformed,
            _ => verifier.Verify(feed.Bytes.AsSpan(), channel, signature.Bytes.AsSpan()) switch
            {
                UpdateFeedSignatureCheck.Valid => null,
                UpdateFeedSignatureCheck.Malformed => UpdateFeedRejection.SignatureMalformed,
                _ => UpdateFeedRejection.SignatureUntrusted,
            },
        };
        if (rejection is { } reason)
        {
            bool read = feed.Status == FetchStatus.Ok;
            throw new UpdateFeedRejectedException(new RejectedUpdateFeed(
                FeedNameOf(channel),
                reason,
                read ? ClaimedVersionOf(feed.Bytes.AsSpan()) : null,
                read ? Convert.ToHexString(SHA256.HashData(feed.Bytes.AsSpan())) : null));
        }

        return Parse(feed.Bytes.AsSpan());
    }

    /// <summary>Like Velopack's sources: a UTF-8 byte order mark is skipped, the rest is the JSON of the feed.</summary>
    private static VelopackAssetFeed Parse(ReadOnlySpan<byte> feed)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        return VelopackAssetFeed.FromJson(Encoding.UTF8.GetString(feed.StartsWith(bom) ? feed[bom.Length..] : feed));
    }

    /// <summary>
    /// The newest full version a refused feed names, for <see cref="RejectedUpdateFeed.ClaimedVersion"/> only (untrusted: read
    /// with Velopack's own source-generated parser, bounded by <see cref="MaxFeedBytes"/>, and kept only as a semantic version).
    /// </summary>
    private static ReleaseVersion? ClaimedVersionOf(ReadOnlySpan<byte> feed)
    {
        try
        {
            SemanticVersion? newest = null;
            foreach (VelopackAsset asset in Parse(feed).Assets ?? [])
            {
                if (asset is { Type: VelopackAssetType.Full, Version: { } version } && (newest is null || version > newest))
                {
                    newest = version;
                }
            }

            return newest is not null && ReleaseVersion.TryParse(newest.ToString(), out ReleaseVersion? parsed) ? parsed : null;
        }
        catch (Exception)
        {
            // Not even feed JSON: the rejection is keyed by the hash of the bytes.
            return null;
        }
    }

    /// <summary>Reads <paramref name="uri"/> within <see cref="Timeout"/>; HTTP 404 is <see cref="FetchStatus.Missing"/>, other errors throw.</summary>
    private static async Task<Fetched> FetchAsync(HttpClient client, Uri uri, int maxBytes, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Fetched.Missing;
            }

            response.EnsureSuccessStatusCode();
            long? length = response.Content.Headers.ContentLength;
            if (length > maxBytes)
            {
                return Fetched.TooLarge;
            }

            Stream body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                return await ReadBoundedAsync(body, maxBytes, length, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Downloading {uri} took longer than {Timeout.TotalSeconds:0} s.");
        }
    }

    /// <summary>Reads <paramref name="path"/>; a missing file is <see cref="FetchStatus.Missing"/>.</summary>
    private static async Task<Fetched> ReadFileAsync(string path, int maxBytes, CancellationToken cancellationToken)
    {
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Fetched.Missing;
        }

        await using (file.ConfigureAwait(false))
        {
            return file.Length > maxBytes
                ? Fetched.TooLarge
                : await ReadBoundedAsync(file, maxBytes, file.Length, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The whole of <paramref name="stream"/> when it has at most <paramref name="maxBytes"/>: one buffer of the announced length
    /// (and one byte more to notice a longer body), or one that grows up to the limit.
    /// </summary>
    private static async Task<Fetched> ReadBoundedAsync(Stream stream, int maxBytes, long? announced, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[announced is { } length ? (int)length + 1 : Math.Min(maxBytes + 1, 16 * 1024)];
        int filled = 0;
        while (true)
        {
            if (filled == buffer.Length)
            {
                if (filled > maxBytes)
                {
                    return Fetched.TooLarge;
                }

                Array.Resize(ref buffer, (int)Math.Min(buffer.Length * 2L, maxBytes + 1L));
            }

            int read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return filled > maxBytes ? Fetched.TooLarge : new Fetched(FetchStatus.Ok, new ArraySegment<byte>(buffer, 0, filled));
            }

            filled += read;
        }
    }

    private enum FetchStatus
    {
        Ok,
        Missing,
        TooLarge,
    }

    private readonly record struct Fetched(FetchStatus Status, ArraySegment<byte> Bytes)
    {
        public static Fetched Missing => new(FetchStatus.Missing, default);

        public static Fetched TooLarge => new(FetchStatus.TooLarge, default);
    }

    /// <summary>GitHub Releases of the app; trusts <see cref="UpdateFeedKeys.Production"/> only (no key parameter on purpose).</summary>
    private sealed class GitHub(bool prereleases, IHttpClientFactory httpClients, IFileDownloader? releaseListDownloader, CancellationToken cancellationToken)
        : GithubSource(AppUpdates.RepositoryUrl, accessToken: null, prereleases, releaseListDownloader)
    {
        public override async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            string feedName = FeedNameOf(channel);
            // Newest by publication first, pre-releases only when asked for (GithubSource).
            GithubRelease[] releases = await GetReleases(Prerelease).ConfigureAwait(false);
            foreach (GithubRelease release in releases)
            {
                if (AssetUrl(release, feedName) is not { } feedUrl)
                {
                    // This release has no package for this RID (yet): its OS is uploaded later, or never.
                    continue;
                }

                HttpClient client = httpClients.CreateClient(HttpClientName);
                Fetched feed = await FetchAsync(client, new Uri(feedUrl), MaxFeedBytes, cancellationToken).ConfigureAwait(false);
                Fetched signature = AssetUrl(release, feedName + ".sig") is { } signatureUrl
                    ? await FetchAsync(client, new Uri(signatureUrl), UpdateFeedVerifier.MaxSignatureFileBytes, cancellationToken).ConfigureAwait(false)
                    : Fetched.Missing;
                if (feed.Status == FetchStatus.Missing)
                {
                    throw new HttpRequestException($"The update feed '{feedName}' of release '{release.Name}' is listed but not found.", null, HttpStatusCode.NotFound);
                }

                VelopackAsset[] assets = Verified(ProductionVerifier, channel, feed, signature).Assets ?? [];
                // GitBase's download of a package needs the release it belongs to.
                VelopackAsset[] withRelease = new VelopackAsset[assets.Length];
                for (int i = 0; i < assets.Length; i++)
                {
                    withRelease[i] = new GitBaseAsset(assets[i], release);
                }

                return new VelopackAssetFeed { Assets = withRelease };
            }

            logger.Warn($"No release at '{RepoUri}' has the update feed '{feedName}'.");
            return new VelopackAssetFeed();
        }

        /// <summary>The download address of the asset <paramref name="name"/>, or <see langword="null"/> when the release has none.</summary>
        private string? AssetUrl(GithubRelease release, string name)
        {
            try
            {
                return GetAssetUrlFromName(release, name);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>The feed of a test on the loopback; trusts <see cref="UpdateFeedKeys.Test"/> only.</summary>
    private sealed class Web(Uri address, IHttpClientFactory httpClients, CancellationToken cancellationToken) : SimpleWebSource(address)
    {
        public override async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            string feedName = FeedNameOf(channel);
            Uri folder = BaseUri.AbsolutePath.EndsWith('/') ? BaseUri : new Uri(BaseUri.AbsoluteUri + "/");
            HttpClient client = httpClients.CreateClient(HttpClientName);
            Fetched feed = await FetchAsync(client, new Uri(folder, feedName), MaxFeedBytes, cancellationToken).ConfigureAwait(false);
            if (feed.Status == FetchStatus.Missing)
            {
                // Like SimpleWebSource: a feed that is not there fails the check.
                throw new HttpRequestException($"The update feed '{feedName}' is not found at '{folder}'.", null, HttpStatusCode.NotFound);
            }

            Fetched signature = await FetchAsync(client, new Uri(folder, feedName + ".sig"), UpdateFeedVerifier.MaxSignatureFileBytes, cancellationToken).ConfigureAwait(false);
            return Verified(TestVerifier, channel, feed, signature);
        }
    }

    /// <summary>The feed of a test in a local folder; trusts <see cref="UpdateFeedKeys.Test"/> only.</summary>
    private sealed class Folder(string folder, CancellationToken cancellationToken) : SimpleFileSource(new DirectoryInfo(folder))
    {
        public override async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            string path = Path.Combine(BaseDirectory.FullName, FeedNameOf(channel));
            Fetched feed = await ReadFileAsync(path, MaxFeedBytes, cancellationToken).ConfigureAwait(false);
            if (feed.Status == FetchStatus.Missing)
            {
                // Never SimpleFileSource's fallback (the .nupkg files of the folder, which no signature covers): no update.
                logger.Warn($"No update feed at '{path}'.");
                return new VelopackAssetFeed();
            }

            Fetched signature = await ReadFileAsync(path + ".sig", UpdateFeedVerifier.MaxSignatureFileBytes, cancellationToken).ConfigureAwait(false);
            return Verified(TestVerifier, channel, feed, signature);
        }
    }
}
