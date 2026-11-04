using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Tests.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;
using LayerPeek.Tests.Allocation;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>T-164: the packet loop of a timelapse from a recording allocates nothing per packet; isolated, see <see cref="AllocationSensitive"/>.</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class TimelapseFromRecordingAllocationTests : IDisposable
{
    private readonly string _folder = TestDirectories.NewDirectoryPath();

    public TimelapseFromRecordingAllocationTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => TestDirectories.Delete(_folder);

    [Fact]
    public async Task ReadingAndWritingFramesAllocatesNothingPerPacket()
    {
        long minimum = long.MaxValue;
        for (int pass = 0; pass < 3 && minimum > 0; pass++)
        {
            minimum = Math.Min(minimum, await MeasurePassAsync(pass));
        }

        // 10 min of packets at 25 fps after the first frame, 300 frames written: nothing.
        Assert.Equal(0, minimum);
    }

    private async Task<long> MeasurePassAsync(int pass)
    {
        long allocated = -1;
        ScriptedRecordings files = new();
        string recording = files.Add(
            Path.Combine(_folder, $"2026-11-04_14-05-{pass:00}.mp4"),
            new RecordingScript { Frames = 25 * 600, KeyFrameEvery = 25, MeasureFrom = 100, Allocations = (before, after) => allocated = after - before },
            fileBytes: 25 * 600 * 16);
        await using CameraTimelapseFromRecordingMaker maker = new(files, new FakeRecordingWriterFactory(), NullLogger<CameraTimelapseFromRecordingMaker>.Instance);
        TaskCompletionSource<CameraTimelapseFromRecordingResult> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        maker.Completed += (_, result) => completed.TrySetResult(result);
        Assert.True(maker.Start(recording, TimeSpan.FromSeconds(2)));
        CameraTimelapseFromRecordingResult result = await completed.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Problem.ToString());
        Assert.Equal(300, result.Frames);
        Assert.True(allocated >= 0);
        return allocated;
    }
}
