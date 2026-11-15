using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Detection;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Tests.Cameras;
using LayerPeek.Core.Tests.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;
using LayerPeek.Tests.Allocation;

namespace LayerPeek.Core.Tests.Detection;

/// <summary>
/// T-153: a sample of a watched print allocates nothing once warm, from the frame through the hub (key frame lease, gray
/// downscale, key frame scheduling) and the thread-pool work item to the detector and the confirmation. Isolated, see
/// <see cref="AllocationSensitive"/>; the fake camera allocates nothing per frame, the clock is stepped by the test.
/// </summary>
[Collection(nameof(AllocationSensitive))]
public sealed class PrintFailureDetectionAllocationTests
{
    private const int WarmUp = 32;
    private const int Measured = 128;

    [Fact]
    public async Task ASampleDoesNotAllocate()
    {
        long allocated = await Task.Run(MeasureAsync, TestContext.Current.CancellationToken);

        AllocationProbe.AssertNoneAllocated(allocated);
    }

    private static async Task<long> MeasureAsync()
    {
        AllocationProbe.WaitForQuietBeforeTest();
        SteppingClock clock = new();
        CameraStreamHubAllocationTests.LockstepFeed feed = new(packets: false);
        await using CameraStreamHub hub = new(
            new ReconnectingCameraFeed(new CameraStreamHubAllocationTests.SingleFeedFactory(feed), clock, NullLogger<ReconnectingCameraFeed>.Instance),
            clock,
            NullLogger<CameraStreamHub>.Instance,
            TimeSpan.Zero);
        using CameraStreamHubAllocationTests.Stopper stopper = new(feed);
        SnapshotMonitor monitor = new();
        DetectorScript script = new() { Score = 0.2f };
        string folder = TestDirectories.NewDirectoryPath();
        await using PrintFailureDetectionService detection = new(
            monitor,
            hub,
            [script.Registration()],
            new FakeRecordingsFolder(folder),
            new PrintFailureAlarmFiles(CameraRecordingStorage.Instance, folder, clock, NullLogger<PrintFailureAlarmFiles>.Instance),
            new FakeJpegEncoder(),
            new AlarmNotifications(),
            clock,
            NullLogger<PrintFailureDetectionService>.Instance,
            // The fake's frames are black.
            new PrintFailureDetectionOptions { DarkBrightness = 0 });
        AppSettings settings = new()
        {
            Printers =
            [
                new PrinterDefinition
                {
                    Id = "mk4",
                    Name = "MK4",
                    ConnectionType = ConnectionType.PrusaLink,
                    Address = "192.168.0.10",
                    Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")),
                    Detection = new PrinterDetectionSettings { Enabled = true },
                },
            ],
            Detection = new PrintFailureDetectionSettings { Enabled = true },
        };
        hub.Configure(settings.Printers);
        detection.Configure(settings);
        monitor.Raise(new PrinterSnapshot { PrinterId = "mk4", State = PrinterState.Printing, Timestamp = DateTimeOffset.UnixEpoch, Job = new PrintJob { Id = 1 } });
        await feed.Started.WaitAsync(TestContext.Current.CancellationToken);

        // Created with the run, before its first sample.
        ScriptedDetector detector = script.Latest;
        int samples = 0;
        void Step()
        {
            clock.Advance(PrintFailureDetectionSettings.DefaultSampleInterval);
            feed.Produce();
            samples++;
            // The sample crosses the hub thread and a thread-pool worker; on a loaded machine each of them waits for a CPU.
            // SpinOnce yields it to them (Sleep(0) / Sleep(1) once it spun a few times); the timeout catches a sample that
            // never arrives.
            DateTime deadline = DateTime.UtcNow + TestTimeouts.Wait;
            SpinWait spin = default;
            while (detector.Samples < samples)
            {
                Assert.True(DateTime.UtcNow < deadline, "Sample not analyzed.");
                spin.SpinOnce();
            }
        }

        for (int i = 0; i < WarmUp; i++)
        {
            Step();
        }

        // All threads are counted (the sample crosses the hub thread and the thread pool). Each pass starts once the rest
        // of the process is quiet (T-220: the test platform sends the results of the earlier tests for many seconds on a
        // loaded machine, ~200 KB per pass). The thread pool starting or retiring a worker meanwhile allocates a few
        // hundred bytes in the runtime, not per sample: such a pass proves nothing and is repeated. A real allocation per
        // sample shows on every pass.
        long minimum = long.MaxValue;
        int valid = 0;
        for (int pass = 0; pass < 12 && valid < 4 && minimum > 0; pass++)
        {
            AllocationProbe.WaitForQuietProcess(AllocationProbe.QuietBeforePass);
            int threads = ThreadPool.ThreadCount;
            long before = GC.GetTotalAllocatedBytes(precise: true);
            for (int i = 0; i < Measured; i++)
            {
                Step();
            }

            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            if (ThreadPool.ThreadCount == threads)
            {
                valid++;
                minimum = Math.Min(minimum, allocated);
            }
        }

        TestDirectories.Delete(folder);
        Assert.True(valid > 0, "The thread pool changed its size during every pass.");
        return minimum;
    }

    /// <summary>Timestamps stepped by the test (allocation free); timers run on the system clock.</summary>
    private sealed class SteppingClock : TimeProvider
    {
        private long _ticks = TimeSpan.TicksPerDay;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }
}
