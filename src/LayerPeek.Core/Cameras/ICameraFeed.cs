using System.Runtime.CompilerServices;

namespace LayerPeek.Core.Cameras;

/// <summary>
/// One connection to a camera stream, implemented outside Core (decoder). Created by <see cref="ICameraFeedFactory"/>;
/// disposing it closes the connection. For automatic reconnection use <see cref="ReconnectingCameraFeed"/>, for sharing
/// one connection between several consumers <see cref="CameraStreamHub"/>.
/// </summary>
public interface ICameraFeed : IAsyncDisposable
{
    /// <summary>
    /// Connects (on the first <c>MoveNextAsync</c>) and yields decoded frames. Each frame is borrowed and valid only until
    /// the enumerator advances or is disposed (see <see cref="CameraFrame"/>). Can be enumerated once, and only one of
    /// <see cref="ReadFramesAsync"/> and <see cref="ReadAsync"/> can be used.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Completes when the camera ends the stream.</item>
    /// <item>Throws <see cref="CameraFeedException"/> when connecting or decoding fails; its message must be a short
    /// technical text without the address or credentials, as it is shown to the user.</item>
    /// <item>Must honour <paramref name="cancellationToken"/> promptly, also while connecting or waiting for data
    /// (it is used for timeouts); then it throws <see cref="OperationCanceledException"/>.</item>
    /// <item>The continuation after awaiting <c>MoveNextAsync</c> may run inline on the implementation's decoder thread
    /// (when the awaiter has no synchronization context, e.g. with <c>ConfigureAwait(false)</c>); that keeps streaming
    /// free of allocations. Consumers must therefore never block synchronously inside the loop on anything that waits
    /// for the decoder: in particular not on <c>DisposeAsync</c> (no <c>.Wait()</c> / <c>.Result</c>; await it).
    /// Long work per frame delays the next frame.</item>
    /// </list>
    /// </remarks>
    IAsyncEnumerable<CameraFrame> ReadFramesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="ReadFramesAsync"/>, but yields <see cref="CameraStreamItem"/>s by <paramref name="demand"/>, which
    /// may change during the enumeration: after connecting one <see cref="CameraStreamItemKind.StreamStarted"/>, then every
    /// video packet while <see cref="CameraStreamDemand.Packets"/> is set (before it is decoded) and the decoded frames
    /// while <see cref="CameraStreamDemand.Frames"/> is set. Without frame demand nothing is decoded. Same rules as
    /// <see cref="ReadFramesAsync"/> otherwise (borrowed buffers, failures, cancellation, inline continuations).
    /// </summary>
    /// <remarks>
    /// The default implementation is for feeds without access to packets (e.g. test fakes): it yields the frames of
    /// <see cref="ReadFramesAsync"/>, regardless of <paramref name="demand"/>, and neither stream descriptions nor packets.
    /// </remarks>
    IAsyncEnumerable<CameraStreamItem> ReadAsync(CameraStreamDemand demand, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(demand);
        return CameraFeedItems.FramesOf(this, cancellationToken);
    }
}

/// <summary>The default <see cref="ICameraFeed.ReadAsync"/>: frames only.</summary>
internal static class CameraFeedItems
{
    public static async IAsyncEnumerable<CameraStreamItem> FramesOf(ICameraFeed feed, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (CameraFrame frame in feed.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return CameraStreamItem.FromFrame(frame);
        }
    }
}
