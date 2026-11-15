using LayerPeek.App.Updates;
using LayerPeek.Core.Updates;

namespace LayerPeek.App.Tests.Updates;

/// <summary>
/// The refused update feed of a run (T-181) as a test sets it: <see cref="Set"/> raises the event on the calling thread, like
/// <see cref="UpdateCheckService"/> on the thread of its check. Shared with the UI tests.
/// </summary>
internal sealed class FakeFeedRejections : IUpdateFeedRejections
{
    private EventHandler? _changed;

    public FakeFeedRejections(RejectedUpdateFeed? current = null) => Current = current;

    /// <summary>The refusal read by <see cref="FeedRejection"/>; set without the event (before a window opens).</summary>
    public RejectedUpdateFeed? Current { get; set; }

    public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

    public RejectedUpdateFeed? FeedRejection => Current;

    public event EventHandler? FeedRejectionChanged
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    /// <summary>A refusal of a feed that names <paramref name="version"/> (or none).</summary>
    public static RejectedUpdateFeed Refused(string? version) =>
        new("releases.osx-arm64.json", UpdateFeedRejection.SignatureUntrusted, version is null ? null : ReleaseVersion.Parse(version), null);

    public void Set(RejectedUpdateFeed? rejection)
    {
        Current = rejection;
        _changed?.Invoke(this, EventArgs.Empty);
    }
}
