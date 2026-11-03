using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;
using LayerPeek.Tests.Allocation;

namespace LayerPeek.Core.Tests.Cameras.Recording;

/// <summary>T-144: a running recording allocates nothing per packet; isolated, see <see cref="AllocationSensitive"/>.</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class CameraRecordingAllocationTests : IDisposable
{
    private const int WarmUpPackets = 20;
    private const int MeasuredPackets = 200;

    private readonly string _folder = TestDirectories.NewDirectoryPath();

    public CameraRecordingAllocationTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => TestDirectories.Delete(_folder);

    [Fact]
    public async Task SteadyStateRecordingDoesNotAllocate()
    {
        long minimum = long.MaxValue;
        for (int pass = 0; pass < 3 && minimum > 0; pass++)
        {
            // The recording reads on the thread pool: all threads are counted; a stray allocation elsewhere does not
            // repeat on every pass, a per-packet one would.
            minimum = Math.Min(minimum, await MeasurePassAsync(pass));
        }

        Assert.Equal(0, minimum);
    }

    private async Task<long> MeasurePassAsync(int pass)
    {
        TestPackets packets = new(capacity: 512);
        FakeRecordingWriterFactory writers = new();
        CameraRecording recording = CameraRecording.Start("mk4", Path.Combine(_folder, $"pass{pass}.mp4"), packets.Subscription, writers, null, NullLogger.Instance);
        packets.Started();
        packets.Packet(1, keyFrame: true);
        for (int i = 1; i < WarmUpPackets; i++)
        {
            packets.Packet(2);
        }

        await Eventually.TrueAsync(() => writers.Writers is [{ Count: WarmUpPackets }], "warm-up written");
        FakeRecordingWriter writer = writers.Writers[0];

        // Paced like a camera (the writer keeps up): a backlog beyond the subscription's buffer pool would allocate
        // new buffers by design, which a real-time stream never builds up.
        long before = GC.GetTotalAllocatedBytes(precise: true);
        DateTime deadline = DateTime.UtcNow + TestTimeouts.Wait;
        for (int i = 0; i < MeasuredPackets; i++)
        {
            packets.Packet((byte)i, keyFrame: i % 50 == 0);
            SpinWait spin = default;
            while (writer.Count < WarmUpPackets + i + 1)
            {
                Assert.True(DateTime.UtcNow < deadline, "Packets not written.");
                spin.SpinOnce();
            }
        }

        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        await recording.StopAsync();
        return allocated;
    }
}
