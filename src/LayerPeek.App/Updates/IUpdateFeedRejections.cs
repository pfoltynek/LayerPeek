using LayerPeek.Core.Updates;

namespace LayerPeek.App.Updates;

/// <summary>
/// The refused update feed of the current run (T-181, <see cref="UpdateCheckService.FeedRejection"/>), as the Startup and
/// updates tab shows it; implemented by <see cref="UpdateOffer"/>.
/// </summary>
internal interface IUpdateFeedRejections
{
    /// <summary>The refused feed while the refusal is active, or <see langword="null"/>.</summary>
    RejectedUpdateFeed? FeedRejection { get; }

    /// <summary><see cref="FeedRejection"/> changed (any thread).</summary>
    event EventHandler? FeedRejectionChanged;
}
