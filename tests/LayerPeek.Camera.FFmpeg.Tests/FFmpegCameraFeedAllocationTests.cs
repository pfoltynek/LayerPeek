using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Tests.Allocation;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>Allocation guarantees of the decoder; isolated, see <see cref="AllocationSensitive"/>.</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class FFmpegCameraFeedAllocationTests
{
    private const int WarmUpFrames = 10;
    private const int MeasuredFrames = 50;

    /// <summary>Fewer than <see cref="MeasuredFrames"/>: the hub drops frames the consumer misses, so a pass uses up more of the fixture.</summary>
    private const int HubMeasuredFrames = 30;

    [FFmpegFact]
    public async Task SteadyStateFramesDoNotAllocate()
    {
        // Decoding runs on the feed's own thread, so all threads are counted (GC.GetTotalAllocatedBytes); a stray
        // allocation elsewhere in the process does not repeat on every pass, a per-frame allocation would.
        // Anything per frame would be at least 50 objects; the 1280x720 buffer alone is 3.6 MB.
        Assert.Equal(0, await MinimumOfValidPassesAsync(MeasurePassAsync));
    }

    [FFmpegFact]
    public async Task SteadyStateFramesThroughReconnectingFeedDoNotAllocate()
    {
        // The real path of the camera window: FFmpeg completes every read asynchronously on its decoder thread.
        Assert.Equal(0, await MinimumOfValidPassesAsync(MeasureReconnectingPassAsync));
    }

    [FFmpegFact]
    public async Task SteadyStatePacketsAndFramesDoNotAllocate()
    {
        // T-143: a recording (packets) and a window (frames) on one connection.
        Assert.Equal(0, await MinimumOfValidPassesAsync(MeasureItemsPassAsync));
    }

    [FFmpegFact]
    public async Task SteadyStateFramesThroughTheHubDoNotAllocate()
    {
        // T-143: the camera window's real path, the shared connection with the subscriber's own buffers. The file plays
        // as fast as it decodes, so under load a pass can run out of frames (the stream ends and reconnects); such a pass
        // measures nothing and is repeated.
        Assert.Equal(0, await MinimumOfValidPassesAsync(() => MeasureHubPassAsync(options: null)));
    }

    [FFmpegFact]
    public async Task SteadyStateDisplayFramesDoNotAllocate()
    {
        // T-184: the window's frames scaled down in the decoder, nothing in full resolution.
        Assert.Equal(0, await MinimumOfValidPassesAsync(() => MeasureDisplayPassAsync(switchSizes: false)));
    }

    [FFmpegFact]
    public async Task SwitchingBetweenDisplaySizesUsedBeforeDoesNotAllocate()
    {
        // T-184: a window switched back and forth (mini preview <-> normal) rebuilds its scaler (native) but reuses its
        // buffer, which kept the larger size.
        Assert.Equal(0, await MinimumOfValidPassesAsync(() => MeasureDisplayPassAsync(switchSizes: true)));
    }

    [FFmpegFact]
    public async Task SteadyStateDisplayFramesThroughTheHubDoNotAllocate()
    {
        // T-184: the camera window's path with a display size.
        Assert.Equal(0, await MinimumOfValidPassesAsync(() => MeasureHubPassAsync(new CameraFrameSubscriptionOptions { DisplaySize = new CameraFrameSize(640, 640) })));
    }

    /// <summary>
    /// The smallest allocation of up to three valid passes. All threads are counted, so a pass during which the thread
    /// pool started or retired workers (allocations of the runtime, not of the feed) measures nothing and is repeated.
    /// </summary>
    private static async Task<long> MinimumOfValidPassesAsync(Func<Task<long?>> measure)
    {
        long minimum = long.MaxValue;
        int valid = 0;
        for (int pass = 0; pass < 10 && valid < 3 && minimum > 0; pass++)
        {
            // On the thread pool: no synchronization context, so continuations stay on the decoder thread like in
            // a consumer that uses ConfigureAwait(false) (a context would allocate for every post).
            if (await Task.Run(measure, TestContext.Current.CancellationToken) is { } allocated)
            {
                valid++;
                minimum = Math.Min(minimum, allocated);
            }
        }

        Assert.True(valid > 0, "No pass measured a steady state.");
        return minimum;
    }

    /// <returns>The bytes allocated over <see cref="MeasuredFrames"/> packets and frames; <see langword="null"/> when the thread pool changed its size meanwhile.</returns>
    private static async Task<long?> MeasureItemsPassAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        IAsyncEnumerator<CameraStreamItem> items = feed.ReadAsync(new CameraStreamDemand(frames: true, packets: true), ct).GetAsyncEnumerator(ct);
        try
        {
            // Description, then packet + frame each.
            for (int i = 0; i < 1 + (2 * WarmUpFrames); i++)
            {
                Assert.True(await items.MoveNextAsync());
            }

            int threads = ThreadPool.ThreadCount;
            long before = GC.GetTotalAllocatedBytes(precise: true);
            for (int i = 0; i < 2 * MeasuredFrames; i++)
            {
                Assert.True(await items.MoveNextAsync());
            }

            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            return ThreadPool.ThreadCount == threads ? allocated : null;
        }
        finally
        {
            await items.DisposeAsync();
        }
    }

    /// <returns>The bytes allocated over <see cref="HubMeasuredFrames"/> frames; <see langword="null"/> when the fixture ran out first or the thread pool changed its size meanwhile.</returns>
    private static async Task<long?> MeasureHubPassAsync(CameraFrameSubscriptionOptions? options)
    {
        ReconnectingCameraFeed camera = new(new FFmpegCameraFeedFactory(), TimeProvider.System, NullLogger<ReconnectingCameraFeed>.Instance);
        await using CameraStreamHub hub = new(camera, TimeProvider.System, NullLogger<CameraStreamHub>.Instance, TimeSpan.Zero);
        using CameraFrameSubscription lease = hub.SubscribeFrames("mk4", TestFixtures.Buddy3D, options);
        // The file plays as fast as it decodes; frames the consumer misses are dropped (and counted).
        long Seen(ref int taken)
        {
            if (lease.TakeLatest() is not null)
            {
                taken++;
            }

            return taken + lease.DroppedFrames;
        }

        int taken = 0;
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        // The exchange's third buffer gets its pixels only after the consumer took frames (the third take proves it): a
        // consumer starved by a loaded machine that took none in the warm-up would allocate it inside the measurement.
        while (Seen(ref taken) < WarmUpFrames || taken < 3)
        {
            Assert.True(DateTime.UtcNow < deadline, "No frames.");
            Thread.Yield();
        }

        long start = Seen(ref taken);
        if (start + HubMeasuredFrames > TestFixtures.Buddy3DFrameCount)
        {
            return null;
        }

        int threads = ThreadPool.ThreadCount;
        long before = GC.GetTotalAllocatedBytes(precise: true);
        while (Seen(ref taken) < start + HubMeasuredFrames)
        {
            Thread.Yield();
        }

        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        // The end of the file (and the reconnect) inside the measurement is no steady state.
        return lease.Status.State == CameraConnectionState.Streaming && ThreadPool.ThreadCount == threads ? allocated : null;
    }

    /// <returns>The bytes allocated over <see cref="MeasuredFrames"/> display frames; <see langword="null"/> when the thread pool changed its size meanwhile.</returns>
    private static async Task<long?> MeasureDisplayPassAsync(bool switchSizes)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        CameraFrameSize large = new(640, 640);
        CameraFrameSize small = new(320, 320);
        CameraStreamDemand demand = new(frames: true, packets: false) { DisplaySize = large };
        demand.CancelFullResolution();
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        IAsyncEnumerator<CameraStreamItem> items = feed.ReadAsync(demand, ct).GetAsyncEnumerator(ct);
        try
        {
            // Description, then frames; the warm-up uses both sizes once.
            for (int i = 0; i < 1 + WarmUpFrames; i++)
            {
                demand.DisplaySize = switchSizes && i % 2 == 0 ? small : large;
                Assert.True(await items.MoveNextAsync());
            }

            int threads = ThreadPool.ThreadCount;
            long before = GC.GetTotalAllocatedBytes(precise: true);
            for (int i = 0; i < MeasuredFrames; i++)
            {
                if (switchSizes)
                {
                    // Every fifth frame, like a window that changes its mode now and then.
                    demand.DisplaySize = i / 5 % 2 == 0 ? small : large;
                }

                Assert.True(await items.MoveNextAsync());
                Assert.True(items.Current.DisplayFrame.Width > 0);
            }

            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            return ThreadPool.ThreadCount == threads ? allocated : null;
        }
        finally
        {
            await items.DisposeAsync();
        }
    }

    /// <returns>The bytes allocated over <see cref="MeasuredFrames"/> frames; <see langword="null"/> when the thread pool changed its size meanwhile.</returns>
    private static async Task<long?> MeasureReconnectingPassAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ReconnectingCameraFeed camera = new(new FFmpegCameraFeedFactory(), TimeProvider.System, NullLogger<ReconnectingCameraFeed>.Instance);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IAsyncEnumerator<CameraFrame> frames = camera.ReadFramesAsync(TestFixtures.Buddy3D, status: null, stop.Token).GetAsyncEnumerator(stop.Token);
        try
        {
            for (int i = 0; i < WarmUpFrames; i++)
            {
                Assert.True(await frames.MoveNextAsync());
            }

            int threads = ThreadPool.ThreadCount;
            long before = GC.GetTotalAllocatedBytes(precise: true);
            for (int i = 0; i < MeasuredFrames; i++)
            {
                Assert.True(await frames.MoveNextAsync());
            }

            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            return ThreadPool.ThreadCount == threads ? allocated : null;
        }
        finally
        {
            await stop.CancelAsync();
            await frames.DisposeAsync();
        }
    }

    /// <returns>The bytes allocated over <see cref="MeasuredFrames"/> frames; <see langword="null"/> when the thread pool changed its size meanwhile.</returns>
    private static async Task<long?> MeasurePassAsync()
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

            int threads = ThreadPool.ThreadCount;
            long before = GC.GetTotalAllocatedBytes(precise: true);
            for (int i = 0; i < MeasuredFrames; i++)
            {
                Assert.True(await frames.MoveNextAsync());
            }

            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            return ThreadPool.ThreadCount == threads ? allocated : null;
        }
        finally
        {
            await frames.DisposeAsync();
        }
    }
}
