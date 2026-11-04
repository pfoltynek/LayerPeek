using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Detection;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Tests.Cameras;
using LayerPeek.Core.Tests.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Detection;

/// <summary>
/// T-153: the orchestration of print failure detection over the camera hub (fake feeds, fake time, scripted detectors):
/// when it watches, sampling, confirmation, alarms with snapshots and notifications, false alarms.
/// </summary>
public sealed class PrintFailureDetectionServiceTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraFeedOptions FeedOptions = new()
    {
        ConnectTimeout = CameraFeedOptions.MaxValue,
        FrameTimeout = CameraFeedOptions.MaxValue,
        ReconnectDelay = TimeSpan.FromSeconds(5),
    };

    private static readonly TimeSpan Interval = PrintFailureDetectionSettings.DefaultSampleInterval;

    private readonly FakeTimeProvider _time = new();
    private readonly FakeItemFeedFactory _factory = new();
    private readonly SnapshotMonitor _monitor = new();
    private readonly DetectorScript _script = new();
    private readonly AlarmNotifications _notifications = new();
    private readonly FakeJpegEncoder _encoder = new();
    private readonly ListLogger<PrintFailureDetectionService> _logger = new();
    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly FakeRecordingsFolder _recordings;
    private readonly PrintFailureAlarmFiles _files;
    private readonly CameraStreamHub _hub;
    private PrintFailureDetectionService? _service;

    public PrintFailureDetectionServiceTests()
    {
        _recordings = new FakeRecordingsFolder(Path.Combine(_directory, "Recordings"));
        _files = new PrintFailureAlarmFiles(CameraRecordingStorage.Instance, Path.Combine(_directory, "FalseAlarms"), _time, new ListLogger<PrintFailureAlarmFiles>());
        // Closes at once after the last lease: a stopped watch shows as a closed connection.
        _hub = new CameraStreamHub(new ReconnectingCameraFeed(_factory, _time, new ListLogger<ReconnectingCameraFeed>(), FeedOptions), _time, new ListLogger<CameraStreamHub>(), TimeSpan.Zero);
    }

    private PrintFailureDetectionService Service => _service ??= Create([_script.Registration()]);

    /// <summary>Like the monitoring session: the hub learns the cameras first, then detection.</summary>
    private void Apply(AppSettings settings)
    {
        _hub.Configure(settings.Printers);
        Service.Configure(settings);
    }

    private string[] Messages => [.. _logger.Entries.Select(static e => e.Message)];

    private int Suspected => Messages.Count(static m => m.Contains("suspected", StringComparison.Ordinal));

    public async ValueTask DisposeAsync()
    {
        if (_service is not null)
        {
            await _service.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }

        await _hub.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        TestDirectories.Delete(_directory);
    }

    private PrintFailureDetectionService Create(IReadOnlyList<PrintFailureDetectorRegistration> detectors, PrintFailureDetectionOptions? options = null) =>
        _service = new PrintFailureDetectionService(_monitor, _hub, detectors, _recordings, _files, _encoder, _notifications, _time, _logger, options);

    private static PrinterDefinition Printer(bool detection = true, bool camera = true, DetectionRegion? region = null, DetectorSettings? detector = null) => new()
    {
        Id = "core-one",
        Name = "Core One",
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.10",
        Camera = camera ? new CameraSettings(enabled: true, StreamUri) : CameraSettings.Disabled,
        Detection = new PrinterDetectionSettings
        {
            Enabled = detection,
            Region = region,
            Detectors = detector is null ? new Dictionary<string, DetectorSettings>() : new Dictionary<string, DetectorSettings> { ["scripted"] = detector },
        },
    };

    private static AppSettings Settings(PrinterDefinition? printer = null, bool enabled = true, PrintFailureAlarmRepeat repeat = PrintFailureAlarmRepeat.OncePerPrint, bool saveSnapshots = true) => new()
    {
        Printers = [printer ?? Printer()],
        Detection = new PrintFailureDetectionSettings { Enabled = enabled, AlarmRepeat = repeat, SaveSnapshots = saveSnapshots },
    };

    private PrinterSnapshot Snapshot(PrinterState state, long? job = 7) => new()
    {
        PrinterId = "core-one",
        State = state,
        Timestamp = _time.GetUtcNow(),
        Job = job is null ? null : new PrintJob { Id = job, FileName = "benchy_0.4n_PLA_COREONE_1h2m.gcode", ProgressPercent = 12 },
        Telemetry = new Telemetry { AxisZ = 0.2 },
    };

    private async Task<FakeItemFeed> StartPrintingAsync(AppSettings? settings = null, long? job = 7)
    {
        Apply(settings ?? Settings());
        _monitor.Raise(Snapshot(PrinterState.Printing, job));
        Assert.True(Service.IsWatching("core-one"));
        return await _factory.FeedAsync(_factory.CreateCount == 0 ? 1 : _factory.CreateCount);
    }

    /// <summary>One sample: the next key frame after the interval, analysed by the latest detector.</summary>
    private async Task SampleAsync(FakeItemFeed feed, byte fill = 100, bool advance = true)
    {
        ScriptedDetector? detector = _script.Created.Length > 0 ? _script.Latest : null;
        int before = detector?.Samples ?? 0;
        if (advance)
        {
            _time.Advance(Interval);
        }

        feed.PushFrame(fill, keyFrame: true);
        await Eventually.TrueAsync(() => _script.Created.Length > 0 && _script.Latest.Samples > (ReferenceEquals(_script.Latest, detector) ? before : 0), "a sample analysed");
    }

    /// <summary>Feeds key frames (without advancing time: only the alarm's snapshot lease is due) until <paramref name="condition"/> holds.</summary>
    private static async Task PumpAsync(FakeItemFeed feed, Func<bool> condition, string description)
    {
        DateTime deadline = DateTime.UtcNow + TestTimeouts.Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for: {description}");
            feed.PushFrame(fill: 120, keyFrame: true);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Samples failing scores until the <paramref name="count"/>-th alarm is confirmed, then lets its snapshot come.</summary>
    private async Task<PrintFailureNotification> ConfirmAlarmAsync(FakeItemFeed feed, int count = 1)
    {
        _script.Score = 0.9f;
        for (int i = 0; Suspected < count; i++)
        {
            Assert.True(i < 20, "No alarm was confirmed.");
            await SampleAsync(feed);
        }

        await PumpAsync(feed, () => _notifications.Shown.Count >= count, "the alarm notification");
        return _notifications.Shown.ToArray()[count - 1];
    }

    // ---- When it watches ----

    [Theory]
    [InlineData(PrinterState.Idle)]
    [InlineData(PrinterState.Busy)]
    [InlineData(PrinterState.Paused)]
    [InlineData(PrinterState.Attention)]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Error)]
    public void OutsidePrintingNothingConnectsDecodesOrAnalyses(PrinterState state)
    {
        Apply(Settings());

        _monitor.Raise(Snapshot(state));

        Assert.False(Service.IsWatching("core-one"));
        Assert.Equal(0, _factory.CreateCount);
        Assert.Equal(0, _hub.OpenConnections);
        Assert.Empty(_script.Created);
    }

    public static TheoryData<string> SwitchedOff => ["master", "printer", "camera", "detector", "none registered"];

    [Theory]
    [MemberData(nameof(SwitchedOff))]
    public void ASwitchedOffDetectionNeverTakesALease(string what)
    {
        if (what == "none registered")
        {
            Create([]);
        }

        Apply(what switch
        {
            "master" => Settings(enabled: false),
            "printer" => Settings(Printer(detection: false)),
            "camera" => Settings(Printer(camera: false)),
            "detector" => Settings(Printer(detector: new DetectorSettings { Enabled = false })),
            _ => Settings(),
        });

        _monitor.Raise(Snapshot(PrinterState.Printing));

        Assert.False(Service.IsWatching("core-one"));
        Assert.Equal(0, _factory.CreateCount);
        Assert.Empty(_script.Created);
    }

    [Fact]
    public void DetectionIsOffByDefault()
    {
        Assert.False(AppSettings.Default.Detection.Enabled);
        Assert.Equal(PrinterDetectionSettings.Default, new PrinterDefinition { Id = "a", Name = "a", ConnectionType = ConnectionType.PrusaLink, Address = "a" }.Detection);
    }

    [Fact]
    public async Task WhilePrintingItSamplesGrayscaleKeyFramesAtTheInterval()
    {
        FakeItemFeed feed = await StartPrintingAsync();
        CameraStreamDemand demand = await feed.Started;

        await SampleAsync(feed, advance: false);
        ScriptedDetector detector = _script.Latest;
        Assert.Equal((320, 180), detector.LastSize);
        Assert.Equal(new DetectionPixelRegion(0, 0, 320, 180), detector.LastRegion);
        Assert.Equal(1, detector.Resets);
        Assert.Equal(7, detector.LastContext.JobId);
        Assert.Equal(0.2, detector.LastContext.AxisZ);
        Assert.Equal(12, detector.LastContext.ProgressPercent);

        // The key frames in between are not decoded, nor any other frame: only one sample per interval.
        feed.PushFrame(fill: 100, keyFrame: true);
        feed.PushFrame(fill: 100);
        _time.Advance(Interval / 2);
        feed.PushFrame(fill: 100, keyFrame: true);
        await Eventually.TrueAsync(() => feed.Consumed >= 5, "frames handled");
        Assert.Equal(1, feed.DecodedFrames);
        Assert.False(demand.Frames);

        _time.Advance(Interval / 2);
        await SampleAsync(feed, advance: false);
        Assert.Equal(2, detector.Samples);
        Assert.Equal(Interval, detector.LastContext.PrintElapsed);
        Assert.Equal(2, feed.DecodedFrames);
    }

    [Fact]
    public async Task PausingStopsTheWatchAndResumingStartsOverWithFreshDetectors()
    {
        FakeItemFeed feed = await StartPrintingAsync();
        await SampleAsync(feed, advance: false);

        _monitor.Raise(Snapshot(PrinterState.Paused));

        Assert.False(Service.IsWatching("core-one"));
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        Assert.Equal(0, _hub.OpenConnections);

        _monitor.Raise(Snapshot(PrinterState.Printing));
        FakeItemFeed second = await _factory.FeedAsync(2);
        await SampleAsync(second, advance: false);
        Assert.Equal(2, _script.Created.Length);
        Assert.Equal(1, _script.Latest.Resets);
        Assert.Equal(TimeSpan.Zero, _script.Latest.LastContext.PrintElapsed);
    }

    [Fact]
    public async Task OfflineStopsTheWatch()
    {
        FakeItemFeed feed = await StartPrintingAsync();

        _monitor.Raise(PrinterSnapshot.Unavailable("core-one", PrinterState.Offline, _time.GetUtcNow()));

        Assert.False(Service.IsWatching("core-one"));
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SwitchingDetectionOffOrRemovingThePrinterStopsTheWatch()
    {
        await StartPrintingAsync();

        Apply(Settings(enabled: false));
        Assert.False(Service.IsWatching("core-one"));

        Apply(Settings());
        Assert.True(Service.IsWatching("core-one"));

        Apply(new AppSettings { Detection = new PrintFailureDetectionSettings { Enabled = true } });
        Assert.False(Service.IsWatching("core-one"));
    }

    [Fact]
    public async Task MonitoringFailureStopsEveryWatch()
    {
        await StartPrintingAsync();

        Service.ForgetSnapshots();

        Assert.False(Service.IsWatching("core-one"));
    }

    [Fact]
    public async Task ACameraReconnectResetsTheDetectors()
    {
        FakeItemFeed feed = await StartPrintingAsync();
        await SampleAsync(feed, advance: false);

        feed.Fail(new CameraFeedException("Connection reset by the camera."));
        await Eventually.TrueAsync(
            () =>
            {
                _time.Advance(TimeSpan.FromSeconds(1));
                return _factory.CreateCount >= 2;
            },
            "the reconnect");
        FakeItemFeed second = await _factory.FeedAsync(2);
        await SampleAsync(second);

        Assert.Single(_script.Created);
        Assert.Equal(2, _script.Latest.Resets);
    }

    [Fact]
    public async Task ADarkChamberPausesTheAnalysis()
    {
        FakeItemFeed feed = await StartPrintingAsync();

        feed.PushFrame(fill: 5, keyFrame: true);
        await Eventually.TrueAsync(() => Messages.Any(static m => m.Contains("dark", StringComparison.Ordinal)), "darkness noticed");
        Assert.Equal(0, _script.Latest.Samples);

        await SampleAsync(feed, fill: 100);
        Assert.Equal(1, _script.Latest.Samples);
    }

    // ---- Region ----

    [Fact]
    public async Task TheDetectorScoresOnlyInsideTheRegion()
    {
        // Left half white, right half gray: the region is the right half.
        const int Width = 64;
        const int Height = 36;
        byte[] pixels = new byte[Width * Height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte value = (i / 4 % Width) < Width / 2 ? (byte)255 : (byte)60;
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (value, value, value, 255);
        }

        _script.ScoreFromRegion = true;
        FakeItemFeed feed = await StartPrintingAsync(Settings(Printer(region: new DetectionRegion(0.5, 0, 0.5, 1))));

        feed.PushImage(pixels, Width, Height);
        await Eventually.TrueAsync(() => _script.Created.Length > 0 && _script.Latest.Samples > 0, "a sample");

        ScriptedDetector detector = _script.Latest;
        Assert.Equal(new DetectionPixelRegion(160, 0, 160, 180), detector.LastRegion);
        Assert.Equal(60, detector.LastRegionMean, precision: 0);
        Assert.Equal(new DetectionRegion(0.5, 0, 0.5, 1), detector.LastContext.Region);
    }

    [Fact]
    public async Task ACoreOneWithoutARegionUsesTheCoreOneBed()
    {
        PrinterDefinition printer = Printer() with { DetectedModel = new PrinterModelCode(7, 1, 0) };
        FakeItemFeed feed = await StartPrintingAsync(Settings(printer));

        await SampleAsync(feed, advance: false);

        Assert.Equal(DetectionRegion.CoreOneBed, _script.Latest.LastContext.Region);
        Assert.Equal(new DetectionPixelRegion(48, 99, 224, 81), _script.Latest.LastRegion);
    }

    // ---- Confirmation and alarms ----

    [Fact]
    public async Task AnAlarmNeedsConfirmationAndComesOncePerPrint()
    {
        FakeItemFeed feed = await StartPrintingAsync();
        _script.Score = 0.9f;

        // Two samples above the threshold are not enough (at least three in the window).
        await SampleAsync(feed, advance: false);
        await SampleAsync(feed);
        Assert.Equal(0, Suspected);

        await SampleAsync(feed);
        await PumpAsync(feed, () => !_notifications.Shown.IsEmpty, "the alarm");
        PrintFailureNotification shown = Assert.Single(_notifications.Shown);
        PrintFailureAlarm alarm = shown.Alarm;
        Assert.Equal(("core-one", "Core One", "scripted"), (alarm.PrinterId, alarm.PrinterName, alarm.DetectorId));
        Assert.Equal((0.9f, 0.3f, DetectorSettings.DefaultSensitivity), (alarm.Score, alarm.Threshold, alarm.Sensitivity));
        Assert.Equal([0.9f, 0.9f, 0.9f], alarm.RecentScores);
        Assert.Equal("benchy_0.4n_PLA_COREONE_1h2m.gcode", alarm.FileName);
        Assert.True(shown.PlaySound);

        // Snapshot: full size from the next key frame, saved into the printer's recording folder and counted.
        Assert.Equal([0xFF, 0xD8, 4, 2, 120, 0xFF, 0xD9], alarm.SnapshotJpeg.ToArray());
        Assert.NotNull(alarm.SnapshotPath);
        Assert.Equal(Path.Combine(_recordings.RecordingsFolder, "Core One"), Path.GetDirectoryName(alarm.SnapshotPath));
        Assert.True(CameraRecordingFolders.IsSnapshotFileName(Path.GetFileName(alarm.SnapshotPath)));
        Assert.Equal(alarm.SnapshotJpeg.ToArray(), await File.ReadAllBytesAsync(alarm.SnapshotPath, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(_recordings.RecordingsFolder, "Core One", CameraRecordingFolders.MarkerFileName)));
        Assert.Equal(1, _recordings.Rotations);

        // T-155: the dashcam keeps the minutes around the failure (T-148 trigger).
        Assert.Equal([("core-one", CameraDashcamTrigger.FailureDetected)], _recordings.DashcamSaves);

        // Still failing: no second alarm of this print.
        for (int i = 0; i < 4; i++)
        {
            await SampleAsync(feed);
        }

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(_notifications.Shown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADashcamThatSavesNothingOrFailsDoesNotCostTheAlarm(bool fail)
    {
        _recordings.DashcamResult = false;
        _recordings.ThrowOnDashcam = fail;
        FakeItemFeed feed = await StartPrintingAsync();

        await ConfirmAlarmAsync(feed);

        Assert.Single(_recordings.DashcamSaves);
        Assert.Single(_notifications.Shown);
        Assert.Equal(fail, Messages.Any(static m => m.Contains("dashcam", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ScoresBelowTheThresholdOrBeforeWarmUpRaiseNothing()
    {
        FakeItemFeed feed = await StartPrintingAsync();
        _script.Score = 0.29f;
        for (int i = 0; i < 5; i++)
        {
            await SampleAsync(feed, advance: i > 0);
        }

        _script.Score = 0.9f;
        _script.WarmedUp = false;
        for (int i = 0; i < 5; i++)
        {
            await SampleAsync(feed);
        }

        Assert.Equal(0, Suspected);
        Assert.Empty(_notifications.Shown);
    }

    [Fact]
    public async Task TheSensitivitySetsTheThreshold()
    {
        FakeItemFeed feed = await StartPrintingAsync(Settings(Printer(detector: new DetectorSettings { Sensitivity = 100 })));

        PrintFailureNotification shown = await ConfirmAlarmAsync(feed);

        Assert.Equal((0.1f, 100), (shown.Alarm.Threshold, shown.Alarm.Sensitivity));
    }

    [Fact]
    public async Task OldSamplesOutsideTheWindowDoNotConfirm()
    {
        FakeItemFeed feed = await StartPrintingAsync();
        _script.Score = 0.9f;
        await SampleAsync(feed, advance: false);
        await SampleAsync(feed);

        // A long gap (e.g. the camera was gone): the earlier samples are out of the window.
        _time.Advance(PrintFailureDetectionOptions.Default.ConfirmationWindow);
        await SampleAsync(feed);

        Assert.Equal(0, Suspected);
    }

    [Fact]
    public async Task ResumingTheSamePrintKeepsItsAlarmAndANewPrintAlarmsAgain()
    {
        FakeItemFeed feed = await StartPrintingAsync();
        await ConfirmAlarmAsync(feed);

        _monitor.Raise(Snapshot(PrinterState.Paused));
        _monitor.Raise(Snapshot(PrinterState.Printing));
        FakeItemFeed resumed = await _factory.FeedAsync(2);
        for (int i = 0; i < 4; i++)
        {
            await SampleAsync(resumed, advance: i > 0);
        }

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(_notifications.Shown);

        _monitor.Raise(Snapshot(PrinterState.Finished));
        _monitor.Raise(Snapshot(PrinterState.Printing, job: 8));
        FakeItemFeed next = await _factory.FeedAsync(3);
        await ConfirmAlarmAsync(next, count: 2);
        Assert.Equal(2, _notifications.Shown.Count);
    }

    [Fact]
    public async Task RepeatedAlarmsComeAgainAfterTheRepeatInterval()
    {
        Create([_script.Registration()], new PrintFailureDetectionOptions { RepeatInterval = TimeSpan.FromMinutes(1) });
        FakeItemFeed feed = await StartPrintingAsync(Settings(repeat: PrintFailureAlarmRepeat.WhilePersisting));
        await ConfirmAlarmAsync(feed);

        // Within the interval nothing more, after it the lasting failure is announced again.
        for (int i = 0; i < 4; i++)
        {
            await SampleAsync(feed);
        }

        Assert.Single(_notifications.Shown);
        _time.Advance(TimeSpan.FromMinutes(1));
        await ConfirmAlarmAsync(feed, count: 2);
    }

    [Fact]
    public async Task WithoutSavingTheSnapshotStaysInMemoryOnly()
    {
        FakeItemFeed feed = await StartPrintingAsync(Settings(saveSnapshots: false));

        PrintFailureNotification shown = await ConfirmAlarmAsync(feed);

        Assert.Null(shown.Alarm.SnapshotPath);
        Assert.False(shown.Alarm.SnapshotJpeg.IsEmpty);
        Assert.False(Directory.Exists(_recordings.RecordingsFolder));
        Assert.Equal(0, _recordings.Rotations);
    }

    [Fact]
    public async Task WithoutASnapshotInTimeTheAlarmComesWithoutOne()
    {
        FakeItemFeed feed = await StartPrintingAsync();
        _script.Score = 0.9f;
        await SampleAsync(feed, advance: false);
        await SampleAsync(feed);
        await SampleAsync(feed);
        await Eventually.TrueAsync(() => Messages.Any(static m => m.Contains("suspected", StringComparison.Ordinal)), "the confirmation");

        // No key frame comes any more.
        await Eventually.TrueAsync(
            () =>
            {
                _time.Advance(TimeSpan.FromSeconds(1));
                return !_notifications.Shown.IsEmpty;
            },
            "the alarm after the timeout");

        PrintFailureAlarm alarm = Assert.Single(_notifications.Shown).Alarm;
        Assert.True(alarm.SnapshotJpeg.IsEmpty);
        Assert.Null(alarm.SnapshotPath);
    }

    [Fact]
    public async Task TurnedOffNotificationsStillRaiseTheEvent()
    {
        AppSettings settings = Settings() with { Notifications = new NotificationSettings { Enabled = false } };
        PrintFailureAlarm? raised = null;
        Service.AlarmRaised += (_, alarm) => raised = alarm;
        FakeItemFeed feed = await StartPrintingAsync(settings);
        _script.Score = 0.9f;
        for (int i = 0; i < 3; i++)
        {
            await SampleAsync(feed, advance: i > 0);
        }

        await PumpAsync(feed, () => raised is not null, "the alarm event");
        Assert.Empty(_notifications.Shown);
    }

    [Fact]
    public async Task AFailingDetectorIsOffForThisPrintAndTheOthersGoOn()
    {
        DetectorScript failing = new() { ThrowOnAnalyze = true };
        Create([failing.Registration("failing"), _script.Registration()]);
        FakeItemFeed feed = await StartPrintingAsync();

        await SampleAsync(feed, advance: false);
        await SampleAsync(feed);

        Assert.Equal(2, _script.Latest.Samples);
        Assert.Single(Messages, static m => m.Contains("'failing'", StringComparison.Ordinal));
    }

    // ---- False alarm ----

    [Fact]
    public async Task AFalseAlarmIsRecordedLocally()
    {
        FakeItemFeed feed = await StartPrintingAsync();
        PrintFailureAlarm alarm = (await ConfirmAlarmAsync(feed)).Alarm;

        // The notification's action carries only the id.
        Assert.True(Service.TryGetAlarm(alarm.Id, out PrintFailureAlarm? found));
        Assert.Same(alarm, found);
        Assert.False(Service.TryGetAlarm("unknown", out _));

        string? record = await Service.ReportFalseAlarmAsync(found, TestContext.Current.CancellationToken);

        Assert.NotNull(record);
        Assert.Equal(_files.FalseAlarmFolder, Path.GetDirectoryName(record));
        using JsonDocument json = JsonDocument.Parse(await File.ReadAllBytesAsync(record, TestContext.Current.CancellationToken));
        JsonElement root = json.RootElement;
        Assert.Equal("scripted", root.GetProperty("detectorId").GetString());
        Assert.Equal(alarm.Id, root.GetProperty("alarmId").GetString());
        Assert.Equal(0.9f, root.GetProperty("score").GetSingle());
        Assert.Equal(3, root.GetProperty("recentScores").GetArrayLength());
        Assert.Equal(7, root.GetProperty("jobId").GetInt64());
        string snapshot = Path.Combine(_files.FalseAlarmFolder, root.GetProperty("snapshot").GetString()!);
        Assert.Equal(alarm.SnapshotJpeg.ToArray(), await File.ReadAllBytesAsync(snapshot, TestContext.Current.CancellationToken));
    }

    // ---- Lifetime ----

    [Fact]
    public async Task DisposingEndsTheLeaseAndStopsListening()
    {
        FakeItemFeed feed = await StartPrintingAsync();

        await Service.DisposeAsync();

        Assert.Equal(0, _monitor.SubscriberCount);
        Assert.False(Service.IsWatching("core-one"));
        await feed.Disposed.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        Apply(Settings());
        _monitor.Raise(Snapshot(PrinterState.Printing));
        Assert.Equal(1, _factory.CreateCount);
    }

    [Fact]
    public void DetectorsMustShareOneFrameSpecAndHaveUniqueIds()
    {
        PrintFailureDetectorRegistration other = new("other", new DetectionFrameSpec(640, 360), new DetectionThresholds(0.5f, 0.1f), () => new ScriptedDetector(_script));

        Assert.Throws<ArgumentException>(() => Create([_script.Registration(), other]));
        Assert.Throws<ArgumentException>(() => Create([_script.Registration(), _script.Registration()]));
        Assert.Throws<ArgumentException>(() => new PrintFailureDetectorRegistration("Bed Change", DetectionFrameSpec.Default, new DetectionThresholds(0.5f, 0.1f), () => new ScriptedDetector(_script)));
        _service = null;
    }
}
