namespace LayerPeek.Core.Updates;

/// <summary>Why an update feed was refused (T-181); no update is offered and nothing is downloaded.</summary>
public enum UpdateFeedRejection
{
    /// <summary>The feed has no signature file (<c>releases.&lt;RID&gt;.json.sig</c>).</summary>
    SignatureMissing,

    /// <summary>The signature file is empty, too long or no signature (<see cref="UpdateFeedSignatureCheck.Malformed"/>).</summary>
    SignatureMalformed,

    /// <summary>No trusted key signed this feed for this RID (<see cref="UpdateFeedSignatureCheck.Untrusted"/>).</summary>
    SignatureUntrusted,

    /// <summary>The feed is larger than any feed of the app, so it was not read to the end.</summary>
    FeedTooLarge,
}

/// <summary>
/// A refused update feed (T-181), as <see cref="UpdateCheckService.FeedRejection"/> keeps it for the UI.
/// </summary>
/// <param name="FeedName">The feed file, e.g. <c>releases.osx-arm64.json</c>.</param>
/// <param name="Reason">Why it was refused.</param>
/// <param name="ClaimedVersion">
/// The newest full version the refused feed <b>claims</b> to offer, or <see langword="null"/> when it names none (missing,
/// too large or no feed JSON). <b>Untrusted:</b> whoever made the feed chose it. It only tells one rejected offer from the next
/// (a notification once per offered version, decision 2026-11-10) and may be shown as a semantic version; never install,
/// download or trust anything because of it.
/// </param>
/// <param name="FeedSha256">
/// The SHA-256 of the refused feed bytes (64 uppercase hex characters), or <see langword="null"/> when the feed was not read
/// to the end; the key of an offer that names no version.
/// </param>
public sealed record RejectedUpdateFeed(string FeedName, UpdateFeedRejection Reason, ReleaseVersion? ClaimedVersion, string? FeedSha256);

/// <summary>
/// The update feed failed its signature check (T-181): thrown by an <see cref="IUpdateChecker"/> instead of returning a release,
/// reported by <see cref="UpdateCheckService"/> as <see cref="UpdateCheckOutcome.FeedRejected"/>. Carries no key material and
/// no feed content.
/// </summary>
public sealed class UpdateFeedRejectedException : Exception
{
    public UpdateFeedRejectedException(RejectedUpdateFeed feed)
        : base(MessageOf(feed))
    {
        Feed = feed;
    }

    /// <summary>The refused feed.</summary>
    public RejectedUpdateFeed Feed { get; }

    private static string MessageOf(RejectedUpdateFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        return $"The update feed '{feed.FeedName}' was rejected: {feed.Reason}.";
    }
}
