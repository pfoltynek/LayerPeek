using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Tests.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;
using LayerPeek.Tests.Allocation;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>T-158: a running timelapse allocates nothing per packet or frame; isolated, see <see cref="AllocationSensitive"/>.</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class CameraTimelapseAllocationTests : IDisposable
{
    private const int MeasuredKeyFrames = 200;

    private readonly string _folder = TestDirectories.NewDirectoryPath();

    public CameraTimelapseAllocationTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => TestDirectories.Delete(_folder);

    [Fact]
    public async Task SteadyStateTimelapseDoesNotAllocate()
    {
        // The timelapse reads on the thread pool: all threads are counted. Stray allocations elsewhere in the process (seen
        // on Windows: 56, 784, 4176 B in three passes in a row) differ from pass to pass and some pass sees none; a
        // per-packet allocation (200 key frames and as many P-frames) would show in every pass, so it never reaches 0.
        // A pass during which the thread pool started or retired workers (allocations of the runtime) measures nothing.
        List<string> passes = [];
        long minimum = long.MaxValue;
        for (int pass = 0; pass < 10 && minimum > 0; pass++)
        {
            long? measured = await MeasurePassAsync(pass);
            passes.Add(measured?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "thread pool changed");
            if (measured is { } allocated)
            {
                minimum = Math.Min(minimum, allocated);
            }
        }

        Assert.True(minimum == 0, $"Every pass allocated (bytes per pass): {string.Join(", ", passes)}.");
    }

    /// <returns>The bytes allocated over the measured key frames; <see langword="null"/> when the thread pool changed its size meanwhile.</returns>
    private async Task<long?> MeasurePassAsync(int pass)
    {
        TestPackets packets = new(capacity: 16);
        FakeRecordingWriterFactory writers = new();
        CameraTimelapse timelapse = CameraTimelapse.Start("mk4", Path.Combine(_folder, $"pass{pass}.mp4"), packets.Subscription, writers, null, NullLogger.Instance);
        packets.Started();
        // Warm-up like the measurement, so that the subscription's buffer pool holds what a key frame and a P-frame in
        // flight need.
        for (int i = 0; i < 4; i++)
        {
            timelapse.RequestFrame();
            packets.Packet(1, keyFrame: true);
            packets.Packet(2);
            long items = 1 + (2 * (i + 1));
            await Eventually.TrueAsync(() => timelapse.ItemsRead == items, "warm-up read");
        }

        // Paced like a camera: every other key frame is asked for, the others (and the P-frames) are dropped.
        int threads = ThreadPool.ThreadCount;
        long before = GC.GetTotalAllocatedBytes(precise: true);
        DateTime deadline = DateTime.UtcNow + TestTimeouts.Wait;
        long read = timelapse.ItemsRead;
        for (int i = 0; i < MeasuredKeyFrames; i++)
        {
            if (i % 2 == 0)
            {
                timelapse.RequestFrame();
            }

            packets.Packet((byte)i, keyFrame: true);
            packets.Packet((byte)i);
            read += 2;
            SpinWait spin = default;
            while (timelapse.ItemsRead < read)
            {
                Assert.True(DateTime.UtcNow < deadline, "Packets not read.");
                spin.SpinOnce();
            }
        }

        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        bool steady = ThreadPool.ThreadCount == threads;
        Assert.Equal(4 + (MeasuredKeyFrames / 2), timelapse.FrameCount);
        await timelapse.StopAsync().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        return steady ? allocated : null;
    }
}
