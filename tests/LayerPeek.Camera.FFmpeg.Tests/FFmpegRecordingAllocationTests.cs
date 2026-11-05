using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Tests.Allocation;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>T-144: writing a recording allocates nothing per packet; isolated, see <see cref="AllocationSensitive"/>.</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class FFmpegRecordingAllocationTests : IDisposable
{
    private const int WarmUpPackets = 10;
    private const int MeasuredPackets = 50;

    private readonly string _folder = TestDirectories.CreateDirectory();

    public void Dispose() => TestDirectories.Delete(_folder);

    [FFmpegFact]
    public async Task SteadyStateWritesDoNotAllocate()
    {
        (CameraStreamInfo info, byte[][] packets, bool[] keys) = await ReadFixtureAsync();
        FFmpegCameraRecordingWriterFactory writers = new();
        int pass = 0;

        long minimum = AllocationProbe.MinAllocated(() =>
        {
            using ICameraRecordingWriter writer = writers.Create(Path.Combine(_folder, $"pass{pass++}.mp4"), info, packets[0]);
            for (int i = 0; i < WarmUpPackets; i++)
            {
                writer.Write(packets[i], i * 3600, i * 3600, 3600, keys[i]);
            }

            long allocated = AllocationProbe.Measure(() =>
            {
                for (int i = WarmUpPackets; i < WarmUpPackets + MeasuredPackets; i++)
                {
                    writer.Write(packets[i], i * 3600, i * 3600, 3600, keys[i]);
                }
            });
            writer.Complete();
            return allocated;
        });

        Assert.Equal(0, minimum);
    }

    private static async Task<(CameraStreamInfo Info, byte[][] Packets, bool[] Keys)> ReadFixtureAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        CameraStreamInfo? info = null;
        List<byte[]> packets = [];
        List<bool> keys = [];
        await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: false, packets: true), ct))
        {
            if (item.Kind == CameraStreamItemKind.StreamStarted)
            {
                info = item.Info;
            }
            else
            {
                packets.Add(item.Packet.Data.ToArray());
                keys.Add(item.Packet.IsKeyFrame);
            }
        }

        return (info!, [.. packets], [.. keys]);
    }
}
