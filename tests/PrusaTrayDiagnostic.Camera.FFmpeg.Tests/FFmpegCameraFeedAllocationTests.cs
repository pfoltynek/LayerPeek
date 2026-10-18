using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Tests.Allocation;

namespace PrusaTrayDiagnostic.Camera.FFmpeg.Tests;

/// <summary>Allocation guarantees of the decoder; isolated, see <see cref="AllocationSensitive"/>.</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class FFmpegCameraFeedAllocationTests
{
    private const int WarmUpFrames = 10;
    private const int MeasuredFrames = 50;

    [Fact]
    public async Task SteadyStateFramesDoNotAllocate()
    {
        long minimum = long.MaxValue;

        // Decoding runs on the feed's own thread, so all threads are counted (GC.GetTotalAllocatedBytes); a stray
        // allocation elsewhere in the process does not repeat on every pass, a per-frame allocation would.
        for (int pass = 0; pass < 3 && minimum > 0; pass++)
        {
            // On the thread pool: no synchronization context, so continuations stay on the decoder thread like in
            // a consumer that uses ConfigureAwait(false) (a context would allocate for every post).
            minimum = Math.Min(minimum, await Task.Run(MeasurePassAsync, TestContext.Current.CancellationToken));
        }

        // Anything per frame would be at least 50 objects; the 1280x720 buffer alone is 3.6 MB.
        Assert.Equal(0, minimum);
    }

    private static async Task<long> MeasurePassAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        IAsyncEnumerator<CameraFrame> frames = feed.ReadFramesAsync(ct).GetAsyncEnumerator(ct);
        try
        {
            for (int i = 0; i < WarmUpFrames; i++)
            {
                Assert.True(await frames.MoveNextAsync());
            }

            long before = GC.GetTotalAllocatedBytes(precise: true);
            for (int i = 0; i < MeasuredFrames; i++)
            {
                Assert.True(await frames.MoveNextAsync());
            }

            return GC.GetTotalAllocatedBytes(precise: true) - before;
        }
        finally
        {
            await frames.DisposeAsync();
        }
    }
}
