#if DEBUG
using System.Runtime.CompilerServices;

using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Cameras.Recording;

/// <summary>
/// Debug test mode only (T-213, <see cref="Composition.AppEnvironment.TestFileJobDelayVariable"/>): the feeds of the JPEG
/// export and of the timelapse from a recording, each item handed on <paramref name="delay"/> later, so that a check sees
/// their progress and can cancel them in the middle. The delay is awaited before the item is yielded, while the inner
/// enumerator has not advanced, so a borrowed frame stays valid; cancellation ends the wait at once.
/// </summary>
internal sealed class TestPacedCameraFeedFactory(ICameraFeedFactory inner, TimeSpan delay) : ICameraFeedFactory
{
    public ICameraFeed Create(Uri streamUri) => new Feed(inner.Create(streamUri), delay);

    private sealed class Feed(ICameraFeed inner, TimeSpan delay) : ICameraFeed
    {
        public async IAsyncEnumerable<CameraFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (CameraFrame frame in inner.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                yield return frame;
            }
        }

        public async IAsyncEnumerable<CameraStreamItem> ReadAsync(CameraStreamDemand demand, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (CameraStreamItem item in inner.ReadAsync(demand, cancellationToken).ConfigureAwait(false))
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                yield return item;
            }
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
#endif
