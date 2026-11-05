using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Detection;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Simulator.Camera;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-153 end to end over the real camera stack: print failure detection (with a counting test detector) on the hub, the
/// FFmpeg feed and the simulated live Buddy3D camera (tools/PrusaSimulator, loopback). Only while the printer prints is
/// there a connection; then only the sampled key frames are decoded; pausing closes the connection again.
/// </summary>
public sealed class PrintFailureDetectionSimulatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [FFmpegFact]
    public async Task OnlyAPrintingPrinterIsSampledAndOnlyItsSampleKeyFramesAreDecoded()
    {
        await using RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true);
        CapturingFactory factory = new();
        await using CameraStreamHub hub = new(
            new ReconnectingCameraFeed(factory, TimeProvider.System, NullLogger<ReconnectingCameraFeed>.Instance),
            TimeProvider.System,
            NullLogger<CameraStreamHub>.Instance,
            TimeSpan.Zero);
        Monitor monitor = new();
        CountingDetector detector = new();
        string folder = Path.Combine(Path.GetTempPath(), "LayerPeek.Camera.FFmpeg.Tests", Guid.NewGuid().ToString("N"));
        await using PrintFailureDetectionService detection = new(
            monitor,
            hub,
            [new PrintFailureDetectorRegistration("counting", DetectionFrameSpec.Default, new DetectionThresholds(1, 1), () => detector)],
            new Folder(folder),
            new PrintFailureAlarmFiles(CameraRecordingStorage.Instance, folder, TimeProvider.System, NullLogger<PrintFailureAlarmFiles>.Instance),
            new NoEncoder(),
            NullNotificationService.Instance,
            TimeProvider.System,
            NullLogger<PrintFailureDetectionService>.Instance);
        AppSettings settings = new()
        {
            Printers =
            [
                new PrinterDefinition
                {
                    Id = "core-one",
                    Name = "Core One",
                    ConnectionType = ConnectionType.PrusaLink,
                    Address = "127.0.0.1",
                    Camera = new CameraSettings(enabled: true, camera.StreamUri),
                    Detection = new PrinterDetectionSettings { Enabled = true },
                },
            ],
            Detection = new PrintFailureDetectionSettings { Enabled = true },
        };
        hub.Configure(settings.Printers);
        detection.Configure(settings);

        // Idle, paused, offline: no connection at all.
        foreach (PrinterState state in (PrinterState[])[PrinterState.Idle, PrinterState.Paused, PrinterState.Offline])
        {
            monitor.Raise(state);
        }

        await Task.Delay(500, Ct);
        Assert.Equal(0, camera.Connections);
        Assert.Equal(0, detector.Samples);

        monitor.Raise(PrinterState.Printing);
        await WaitAsync(() => detector.Samples >= 2, "two samples");

        // One sample every 2 s; the camera sends 25 frames per second, a key frame about every 1.4 s.
        FFmpegCameraFeed feed = Assert.Single(factory.Feeds);
        long decoded = feed.DecodedFrames;
        long sent = camera.FramesSent;
        Assert.Equal((320, 180), detector.Size);
        Assert.True(decoded <= detector.Samples + 1, $"{decoded} frames decoded for {detector.Samples} samples ({sent} sent).");
        Assert.True(sent > 3 * decoded, $"{decoded} of {sent} frames decoded.");

        monitor.Raise(PrinterState.Paused);
        Assert.False(detection.IsWatching("core-one"));
        await WaitAsync(() => camera.ActiveSessions == 0 && hub.OpenConnections == 0, "the connection closed");
        int samples = detector.Samples;
        await Task.Delay(2500, Ct);
        Assert.Equal(samples, detector.Samples);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task WaitAsync(Func<bool> condition, string description)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for: {description}");
            await Task.Delay(20, Ct);
        }
    }

    /// <summary>The FFmpeg feeds the hub creates, for their decoded frame counters.</summary>
    private sealed class CapturingFactory : ICameraFeedFactory
    {
        private readonly FFmpegCameraFeedFactory _inner = new();
        private readonly ConcurrentQueue<FFmpegCameraFeed> _feeds = new();

        public FFmpegCameraFeed[] Feeds => [.. _feeds];

        public ICameraFeed Create(Uri streamUri)
        {
            FFmpegCameraFeed feed = (FFmpegCameraFeed)_inner.Create(streamUri);
            _feeds.Enqueue(feed);
            return feed;
        }
    }

    private sealed class CountingDetector : IPrintFailureDetector
    {
        private int _samples;

        public int Samples => Volatile.Read(ref _samples);

        public (int Width, int Height) Size { get; private set; }

        public void Reset(in PrintFailureContext context)
        {
        }

        public DetectionResult Analyze(in DetectionSample sample)
        {
            Size = (sample.Width, sample.Height);
            Interlocked.Increment(ref _samples);
            return new DetectionResult(IsWarmedUp: false, 0);
        }
    }

    private sealed class Monitor : IPrinterMonitor
    {
        public event EventHandler<PrinterSnapshot>? SnapshotUpdated;

        public IReadOnlyList<PrinterSnapshot> Snapshots => [];

        public bool TryGetSnapshot(string printerId, [NotNullWhen(true)] out PrinterSnapshot? snapshot)
        {
            snapshot = null;
            return false;
        }

        public Task RunAsync(IReadOnlyList<PrinterDefinition> printers, TimeSpan pollingInterval, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Raise(PrinterState state) => SnapshotUpdated?.Invoke(this, new PrinterSnapshot
        {
            PrinterId = "core-one",
            State = state,
            Timestamp = DateTimeOffset.UtcNow,
            Job = state is PrinterState.Printing or PrinterState.Paused ? new PrintJob { Id = 1 } : null,
        });
    }

    private sealed class Folder(string root) : ICameraRecordingsFolder
    {
        public string RecordingsFolder => root;

        public void RequestRotation()
        {
        }

        public bool SaveDashcam(string printerId, CameraDashcamTrigger trigger) => false;
    }

    private sealed class NoEncoder : ICameraImageEncoder
    {
        public byte[] EncodeJpeg(ReadOnlySpan<byte> pixels, int width, int height, int stride, int quality) => [];
    }
}
