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
using LayerPeek.Detection;
using LayerPeek.Simulator;
using LayerPeek.Tests.Detection;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-155 end to end: the simulator plays a custom clip (<c>--camera-clip</c>) of the Core One bed in which a lump of
/// filament appears; the real bed-change detector, the print failure detection of T-153, the hub and the FFmpeg feed see it
/// and the alarm goes the whole way: dashcam save, snapshot file, notification and the "false alarm" record. The clip is
/// made here from the committed fixtures (uncompressed H.264, <see cref="PcmH264Writer"/>); only the detector's time
/// constants are shortened so that the test takes seconds, not minutes.
/// </summary>
public sealed class BedChangeAlarmSimulatorTests : IDisposable
{
    private const int FramesPerSecond = 2;
    private static readonly TimeSpan CleanPart = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FailurePart = TimeSpan.FromSeconds(40);

    private readonly string _folder = TestDirectories.CreateDirectory();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_folder);

    [FFmpegFact]
    public async Task ALumpOnTheBedOfAPlayedClipRaisesTheWholeAlarm()
    {
        string clip = Path.Combine(_folder, "failure.h264");
        await File.WriteAllBytesAsync(clip, FailureClip(), Ct);
        Assert.True(SimulatorOptions.TryParse(["--camera-clip", clip, "--fps", $"{FramesPerSecond}", "--port", "0", "--camera-port", "0"], out SimulatorOptions options, out string? error), error);
        await using SimulatorHost simulator = await SimulatorHost.StartAsync(options, Ct);

        await using CameraStreamHub hub = new(
            new ReconnectingCameraFeed(new FFmpegCameraFeedFactory(), TimeProvider.System, NullLogger<ReconnectingCameraFeed>.Instance),
            TimeProvider.System,
            NullLogger<CameraStreamHub>.Instance,
            TimeSpan.Zero);
        Monitor monitor = new();
        Recordings recordings = new(Path.Combine(_folder, "Recordings"));
        Notifications notifications = new();
        ConcurrentQueue<string> scores = new();
        BedChangeOptions fast = BedChangeOptions.Default with
        {
            WarmUp = TimeSpan.FromSeconds(3),
            FastLearning = TimeSpan.FromSeconds(3),
            WarmUpRate = 100,
            // The lump is learned within a few samples and stays in the comparison for 8–16 s (several samples).
            Rate = 20,
            ReferenceSpacing = TimeSpan.FromSeconds(8),
            ReferenceCount = 2,
            Quiet = TimeSpan.FromSeconds(2),
        };
        PrintFailureAlarmFiles files = new(CameraRecordingStorage.Instance, Path.Combine(_folder, "FalseAlarms"), TimeProvider.System, NullLogger<PrintFailureAlarmFiles>.Instance);
        await using PrintFailureDetectionService detection = new(
            monitor,
            hub,
            [new PrintFailureDetectorRegistration(BedChangeDetector.DetectorId, DetectionFrameSpec.Default, BedChangeDetector.Registration.Thresholds, () => new ScoreLog(new BedChangeDetector(fast), scores))],
            recordings,
            files,
            new StandInJpegEncoder(),
            notifications,
            TimeProvider.System,
            NullLogger<PrintFailureDetectionService>.Instance,
            new PrintFailureDetectionOptions { ConfirmationWindow = TimeSpan.FromSeconds(7), MinConfirmationSamples = 2 });
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
                    Camera = new CameraSettings(enabled: true, simulator.Camera!.StreamUri),
                    Detection = new PrinterDetectionSettings { Enabled = true, Region = DetectionRegion.CoreOneBed },
                },
            ],
            Detection = new PrintFailureDetectionSettings { Enabled = true },
        };
        hub.Configure(settings.Printers);
        detection.Configure(settings);
        TaskCompletionSource<PrintFailureAlarm> raised = new(TaskCreationOptions.RunContinuationsAsynchronously);
        detection.AlarmRaised += (_, alarm) => raised.TrySetResult(alarm);

        monitor.RaisePrinting();
        Task done = await Task.WhenAny(raised.Task, Task.Delay(CleanPart + FailurePart + TimeSpan.FromSeconds(20), Ct));
        Assert.True(done == raised.Task, $"No alarm; samples (time, warm, score): {string.Join(", ", scores)}");
        PrintFailureAlarm alarm = await raised.Task;

        // Not before the lump appeared in the clip (the camera started it from its first frame for this connection).
        Assert.Equal(BedChangeDetector.DetectorId, alarm.DetectorId);
        Assert.True(alarm.Context.PrintElapsed >= CleanPart, $"Alarm after {alarm.Context.PrintElapsed} of printing.");
        Assert.True(alarm.Score >= alarm.Threshold, $"Score {alarm.Score} below {alarm.Threshold}.");
        Assert.Equal(BedChangeDetector.Registration.Thresholds.ThresholdFor(DetectorSettings.DefaultSensitivity), alarm.Threshold);
        Assert.Equal([("core-one", CameraDashcamTrigger.FailureDetected)], recordings.DashcamSaves);

        // The snapshot is a full frame of the camera, saved next to the recordings.
        Assert.Equal((320, 180), StandInJpegEncoder.SizeOf(alarm.SnapshotJpeg.Span));
        Assert.NotNull(alarm.SnapshotPath);
        Assert.True(File.Exists(alarm.SnapshotPath));
        // The notification follows the event on the alarm's own thread.
        PrintFailureNotification shown = await notifications.First.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(alarm.Id, shown.Alarm.Id);
        Assert.Single(notifications.Shown);

        // "False alarm" (T-154's toast action) keeps a local record.
        Assert.True(detection.TryGetAlarm(alarm.Id, out PrintFailureAlarm? found));
        string? record = await detection.ReportFalseAlarmAsync(found, Ct);
        Assert.NotNull(record);
        Assert.True(File.Exists(record));
        Assert.Single(Directory.GetFiles(files.FalseAlarmFolder, "*.jpg"));
    }

    /// <summary>The still Core One bed (median of the fixture key frames), then the same with a lump of filament in front of the part.</summary>
    private static byte[] FailureClip()
    {
        Random random = new(5);
        Lump lump = new(Seed: 3, CenterX: 160, CenterY: 150, Radius: 14, Luma: 225, Growth: TimeSpan.Zero);
        IEnumerable<byte[]> Frames()
        {
            int clean = (int)(CleanPart.TotalSeconds * FramesPerSecond);
            int total = clean + (int)(FailurePart.TotalSeconds * FramesPerSecond);
            for (int i = 0; i < total; i++)
            {
                byte[] frame = [.. SyntheticScene.Median];
                if (i >= clean)
                {
                    lump.Apply(frame, TimeSpan.Zero);
                }

                SyntheticScene.AddCompressionNoise(frame, 1.5, random);
                yield return frame;
            }
        }

        return PcmH264Writer.Encode(Frames(), SyntheticPrint.Width, SyntheticPrint.Height);
    }

    /// <summary>The real detector, with every result noted for the message of a failed test.</summary>
    private sealed class ScoreLog(IPrintFailureDetector inner, ConcurrentQueue<string> log) : IPrintFailureDetector
    {
        public void Reset(in PrintFailureContext context) => inner.Reset(context);

        public DetectionResult Analyze(in DetectionSample sample)
        {
            DetectionResult result = inner.Analyze(sample);
            log.Enqueue(FormattableString.Invariant($"{sample.Time.TotalSeconds:0.0} s {(result.IsWarmedUp ? "warm" : "cold")} {result.Score:P2}"));
            return result;
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

        /// <summary>High above the first layers, so that the detector judges after its warm-up.</summary>
        public void RaisePrinting() => SnapshotUpdated?.Invoke(this, new PrinterSnapshot
        {
            PrinterId = "core-one",
            State = PrinterState.Printing,
            Timestamp = DateTimeOffset.UtcNow,
            Job = new PrintJob { Id = 1, FileName = "box.bgcode" },
            Telemetry = new Telemetry { AxisZ = 12.4 },
        });
    }

    private sealed class Recordings(string root) : ICameraRecordingsFolder
    {
        public ConcurrentQueue<(string PrinterId, CameraDashcamTrigger Trigger)> DashcamSaves { get; } = new();

        public string RecordingsFolder => root;

        public void RequestRotation()
        {
        }

        public bool SaveDashcam(string printerId, CameraDashcamTrigger trigger)
        {
            DashcamSaves.Enqueue((printerId, trigger));
            return true;
        }
    }

    private sealed class Notifications : INotificationService
    {
        public ConcurrentQueue<PrintFailureNotification> Shown { get; } = new();

        public TaskCompletionSource<PrintFailureNotification> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Show(PrintNotification notification)
        {
        }

        public void ShowPrintFailure(PrintFailureNotification notification)
        {
            Shown.Enqueue(notification);
            First.TrySetResult(notification);
        }
    }

    /// <summary>Not a JPEG (the app encodes with Avalonia): SOI, the frame's size, EOI.</summary>
    private sealed class StandInJpegEncoder : ICameraImageEncoder
    {
        public byte[] EncodeJpeg(ReadOnlySpan<byte> pixels, int width, int height, int stride, int quality) =>
            [0xFF, 0xD8, (byte)(width >> 8), (byte)width, (byte)(height >> 8), (byte)height, 0xFF, 0xD9];

        public static (int Width, int Height) SizeOf(ReadOnlySpan<byte> jpeg) => ((jpeg[2] << 8) | jpeg[3], (jpeg[4] << 8) | jpeg[5]);
    }
}
