using System.Collections.Concurrent;

using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Tests.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>
/// T-160: when the recording service takes timelapses (user decisions 2026-11-04), with fake snapshots, a fake clock that
/// drives the time trigger, fake recorders and real files in a temporary folder.
/// </summary>
public sealed class CameraTimelapseServiceTests : IAsyncDisposable
{
    private const string Benchy = "benchy_0.4n_0.2mm_PLA_MK4S_1h2m.bgcode";
    private const string Cube = "cube_0.4n_0.2mm_PLA_MK4S_30m.bgcode";
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 14, 5, 9, TimeSpan.Zero);
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly string _root = TestDirectories.NewDirectoryPath();
    private readonly SnapshotMonitor _monitor = new();
    private readonly FakeCameraRecorder _recorder = new();
    private readonly FakeCameraTimelapseRecorder _timelapses = new();
    private readonly FakeRecordingStorage _storage = new();
    private readonly CountingTimeProvider _time = new(new FakeTimeProvider(Start));
    private readonly ListLogger<CameraRecordingService> _logger = new();
    private readonly CameraRecordingService _service;
    private readonly ConcurrentQueue<string> _changed = new();
    private readonly ConcurrentQueue<CameraRecordingError> _failed = new();

    public CameraTimelapseServiceTests()
    {
        _service = CreateService();
    }

    private FakeTimeProvider Clock => _time.Fake;

    private string Folder => Path.Combine(_root, "Workshop MK4");

    public async ValueTask DisposeAsync()
    {
        await _service.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        TestDirectories.Delete(_root);
    }

    private CameraRecordingService CreateService(ICameraTimelapseRecorder? timelapses = null, Func<ICameraTimelapse, ICameraTimelapseTrigger>? trigger = null)
    {
        CameraRecordingService service = new(_monitor, _recorder, _storage, _time, _logger, new CameraRecordingServiceOptions { DefaultRoot = _root }, timelapses ?? _timelapses)
        {
            TriggerOverride = trigger,
        };
        service.Changed += (_, printerId) => _changed.Enqueue(printerId);
        service.Failed += (_, error) => _failed.Enqueue(error);
        return service;
    }

    private static PrinterDefinition Printer(
        string id = "mk4", string name = "Workshop MK4", bool timelapsePrints = true, bool recordPrints = false, bool camera = true, string address = "192.168.0.10") => new()
    {
        Id = id,
        Name = name,
        ConnectionType = ConnectionType.PrusaLink,
        Address = address,
        Camera = new CameraSettings(camera, StreamUri) { TimelapsePrints = timelapsePrints, RecordPrints = recordPrints },
    };

    private void Configure(params PrinterDefinition[] printers) => _service.Configure(printers, CameraRecordingSettings.Default);

    private void Raise(PrinterState state, string id = "mk4", string? job = Benchy, long? jobId = null) =>
        _monitor.Raise(new PrinterSnapshot
        {
            PrinterId = id,
            State = state,
            Timestamp = Clock.GetUtcNow(),
            Job = job is null && jobId is null ? null : new PrintJob { Id = jobId, FileName = job, ProgressPercent = 10 },
        });

    private CameraTimelapseStatus Status(string printerId = "mk4") => _service.GetStatus(printerId).Timelapse;

    /// <summary>Waits for timelapse <paramref name="number"/>, its first frame request, and lets its first key frame arrive.</summary>
    private async Task<FakeTimelapse> TimelapseAsync(int number, bool capturing = true)
    {
        await Eventually.TrueAsync(() => _timelapses.Started.Length >= number, $"timelapse {number} started");
        FakeTimelapse timelapse = _timelapses.Started[number - 1];
        await StatusAsync(timelapse.PrinterId, CameraRecordingActivity.Starting);
        if (capturing)
        {
            // The trigger exists once the first frame is asked for (both under the service's lock).
            await Eventually.TrueAsync(() => timelapse.Requests == 1, "first frame asked for");
        }

        timelapse.BeginFile();
        await StatusAsync(timelapse.PrinterId, CameraRecordingActivity.Recording);
        return timelapse;
    }

    private async Task StatusAsync(string printerId, CameraRecordingActivity activity) =>
        await Eventually.TrueAsync(() => _service.GetStatus(printerId).Timelapse.Activity == activity, $"{printerId} timelapse {activity}");

    /// <summary>Lets the thread pool run what a snapshot or timer may have started (there should be nothing).</summary>
    private static Task SettleAsync() => Task.Delay(50, TestContext.Current.CancellationToken);

    [Fact]
    public async Task AnAutomaticTimelapseTakesAFrameEveryThirtySecondsOfThePrintAndEndsAMinuteAfterIt()
    {
        Configure(Printer());
        Raise(PrinterState.Idle, job: null);
        await SettleAsync();
        Assert.Empty(_timelapses.Started);

        Raise(PrinterState.Printing);
        FakeTimelapse timelapse = await TimelapseAsync(1);

        Assert.Equal(Path.Combine(Folder, "2026-11-04_14-05-09_Benchy_timelapse.mp4"), timelapse.Path);
        Assert.True(File.Exists(Path.Combine(Folder, CameraRecordingFolders.MarkerFileName)));
        CameraTimelapseStatus status = Status();
        Assert.Equal(CameraRecordingMode.Automatic, status.Mode);
        Assert.True(status.IsActive);
        Assert.True(status.IsCapturing);
        Assert.True(status.TimelapsePrints);
        Assert.Equal(TimeSpan.FromSeconds(30), status.Interval);
        Assert.Equal(Start, status.StartedAt);
        Assert.Equal(timelapse.Path, status.CurrentFile);
        Assert.Equal(1, status.FrameCount);

        Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(1, timelapse.Requests);
        Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, timelapse.Requests);
        Clock.Advance(TimeSpan.FromSeconds(90));
        Assert.Equal(5, timelapse.Requests);

        // The end of the print: the same minute as a recording, with frames meanwhile.
        Raise(PrinterState.Finished);
        Clock.Advance(TimeSpan.FromSeconds(59));
        await SettleAsync();
        Assert.False(timelapse.IsEnded);
        Clock.Advance(TimeSpan.FromSeconds(1));

        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(timelapse.IsEnded);
        Assert.Equal(1, timelapse.StopCalls);
        Assert.Equal(timelapse.Path, Status().LastFile);
        Assert.Null(_service.GetStatus("mk4").LastFile);
        Assert.Empty(_recorder.Started);
        Assert.Empty(_failed);
        Assert.Contains("mk4", _changed);
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Theory]
    [InlineData(PrinterState.Paused)]
    [InlineData(PrinterState.Attention)]
    public async Task APausedOrWaitingPrintTakesNoFramesAndResumesInTheSameFile(PrinterState pause)
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeTimelapse timelapse = await TimelapseAsync(1);
        Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(2, timelapse.Requests);

        _changed.Clear();
        Raise(pause);
        Assert.False(Status().IsCapturing);
        Assert.Contains("mk4", _changed);
        Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(2, timelapse.Requests);
        Assert.False(timelapse.IsEnded);

        _changed.Clear();
        Raise(PrinterState.Printing);
        Assert.True(Status().IsCapturing);
        Assert.Contains("mk4", _changed);
        Assert.Equal(3, timelapse.Requests);
        Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(4, timelapse.Requests);
        await SettleAsync();
        Assert.Single(_timelapses.Started);
        Assert.Equal(CameraRecordingActivity.Recording, Status().Activity);
    }

    [Fact]
    public async Task APrintThatStartsPausedTakesItsFirstFrameWhenItResumes()
    {
        Configure(Printer());
        Raise(PrinterState.Paused);
        FakeTimelapse timelapse = await TimelapseAsync(1, capturing: false);

        await SettleAsync();
        Assert.Equal(0, timelapse.Requests);
        Assert.False(Status().IsCapturing);

        Raise(PrinterState.Printing);
        Assert.Equal(1, timelapse.Requests);
    }

    [Theory]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Unauthorized)]
    public async Task AnUnreachablePrinterKeepsTakingFramesAndEndsTheTimelapseAfterTheTolerance(PrinterState unreachable)
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeTimelapse timelapse = await TimelapseAsync(1);

        Raise(unreachable);
        Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(2, timelapse.Requests);
        Clock.Advance(TimeSpan.FromSeconds(30));

        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(timelapse.IsEnded);
        Assert.Empty(_failed);
    }

    [Fact]
    public async Task TheTimelapseIsOffByDefaultAndIdleCostsNoTimers()
    {
        Configure(Printer(timelapsePrints: false), Printer("xl", "XL", address: "192.168.0.11"));
        Assert.False(new CameraSettings(true, StreamUri).TimelapsePrints);
        Raise(PrinterState.Printing);
        Raise(PrinterState.Finished);
        Raise(PrinterState.Idle, "xl");
        await SettleAsync();
        Assert.Empty(_timelapses.Started);
        Assert.Equal(0, _time.ActiveTimers);
        Assert.False(Status().TimelapsePrints);

        Raise(PrinterState.Printing, "xl");
        FakeTimelapse timelapse = await TimelapseAsync(1);
        Assert.Equal(2, _time.ActiveTimers); // the trigger and the disk check

        Raise(PrinterState.Finished, "xl");
        Assert.Equal(3, _time.ActiveTimers); // and the minute after the print
        Clock.Advance(TimeSpan.FromMinutes(1));
        await StatusAsync("xl", CameraRecordingActivity.Idle);

        Assert.True(timelapse.IsEnded);
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Fact]
    public async Task AManualTimelapseWithoutAPrintRunsUntilItIsStopped()
    {
        Configure(Printer(timelapsePrints: false));
        Raise(PrinterState.Idle, job: null);

        Assert.True(_service.StartTimelapse("mk4"));
        FakeTimelapse timelapse = await TimelapseAsync(1);
        Assert.Equal(CameraRecordingMode.Manual, Status().Mode);
        Assert.Equal(Path.Combine(Folder, "2026-11-04_14-05-09_timelapse.mp4"), timelapse.Path);

        Clock.Advance(TimeSpan.FromHours(1));
        await SettleAsync();
        Assert.False(timelapse.IsEnded);
        Assert.Equal(121, timelapse.Requests);

        _service.StopTimelapse("mk4");
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(timelapse.IsEnded);
        Assert.Equal(timelapse.Path, Status().LastFile);
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Fact]
    public async Task AManualTimelapseOfAPrintEndsWithThePrint()
    {
        Configure(Printer(timelapsePrints: false));
        Raise(PrinterState.Printing);

        _service.StartTimelapse("mk4");
        FakeTimelapse timelapse = await TimelapseAsync(1);
        Assert.EndsWith("_Benchy_timelapse.mp4", timelapse.Path, StringComparison.Ordinal);

        // Paused in between: still the same print.
        Raise(PrinterState.Paused);
        Raise(PrinterState.Printing);
        Raise(PrinterState.Finished);
        Clock.Advance(TimeSpan.FromSeconds(59));
        await SettleAsync();
        Assert.False(timelapse.IsEnded);
        Clock.Advance(TimeSpan.FromSeconds(1));

        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(timelapse.IsEnded);
        Assert.Empty(_failed);
        Assert.Contains(_logger.Entries, e => e.Message.Contains("so does its manual timelapse", StringComparison.Ordinal));

        // The next print is not taken (no automatic timelapse), and nothing follows the prints any more.
        Raise(PrinterState.Printing, job: Cube);
        Raise(PrinterState.Finished, job: Cube);
        await SettleAsync();
        Assert.Single(_timelapses.Started);
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Fact]
    public async Task AManualTimelapseStartedBeforeAPrintEndsWithThatPrint()
    {
        Configure(Printer(timelapsePrints: false));
        Raise(PrinterState.Idle, job: null);
        _service.StartTimelapse("mk4");
        FakeTimelapse timelapse = await TimelapseAsync(1);

        Raise(PrinterState.Printing);
        Raise(PrinterState.Stopped);
        Clock.Advance(TimeSpan.FromMinutes(1));

        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(timelapse.IsEnded);
    }

    [Fact]
    public async Task AManualTimelapseStartedAfterAPrintEndedIsNotEndedByThatPrint()
    {
        // The automatic recording follows the print: its minute after the print runs when the timelapse is started.
        Configure(Printer(timelapsePrints: false, recordPrints: true));
        Raise(PrinterState.Printing);
        Raise(PrinterState.Finished);
        Clock.Advance(TimeSpan.FromSeconds(20));

        _service.StartTimelapse("mk4");
        FakeTimelapse timelapse = await TimelapseAsync(1);
        Clock.Advance(TimeSpan.FromMinutes(5));
        await SettleAsync();

        Assert.False(timelapse.IsEnded);
        Assert.Equal(CameraRecordingActivity.Recording, Status().Activity);
    }

    [Fact]
    public async Task AnotherJobEndsAManualTimelapseOfThePreviousPrint()
    {
        Configure(Printer(timelapsePrints: false));
        Raise(PrinterState.Printing, jobId: 1);
        _service.StartTimelapse("mk4");
        FakeTimelapse timelapse = await TimelapseAsync(1);

        Raise(PrinterState.Finished, jobId: 1);
        Raise(PrinterState.Printing, job: Cube, jobId: 2);

        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(timelapse.IsEnded);
        await SettleAsync();
        Assert.Single(_timelapses.Started);
    }

    [Fact]
    public async Task AnotherJobTakesAnAutomaticTimelapseIntoItsOwnFile()
    {
        Configure(Printer());
        Raise(PrinterState.Printing, jobId: 1);
        FakeTimelapse first = await TimelapseAsync(1);
        Raise(PrinterState.Finished, jobId: 1);
        Clock.Advance(TimeSpan.FromSeconds(20));

        Raise(PrinterState.Printing, job: Cube, jobId: 2);
        FakeTimelapse second = await TimelapseAsync(2);

        Assert.True(first.IsEnded);
        Assert.Equal(Path.Combine(Folder, "2026-11-04_14-05-29_Cube_timelapse.mp4"), second.Path);
        Assert.Equal(CameraRecordingMode.Automatic, Status().Mode);
        Assert.Contains(_logger.Entries, e => e.Message.Contains("its timelapse continues in a new file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReprintOfTheSameFileTakesItsOwnTimelapse()
    {
        Configure(Printer());
        Raise(PrinterState.Printing, jobId: 1);
        FakeTimelapse first = await TimelapseAsync(1);
        Raise(PrinterState.Finished, jobId: 1);

        Raise(PrinterState.Printing, jobId: 2);
        await TimelapseAsync(2);

        Assert.True(first.IsEnded);
    }

    [Fact]
    public async Task AManualTimelapseThroughAnAutomaticPrintStaysManualAndStartDuringOneAddsNothing()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeTimelapse timelapse = await TimelapseAsync(1);

        Assert.True(_service.StartTimelapse("mk4"));
        await SettleAsync();
        Assert.Single(_timelapses.Started);
        Assert.Equal(CameraRecordingMode.Automatic, Status().Mode);

        Raise(PrinterState.Finished);
        Clock.Advance(TimeSpan.FromMinutes(1));
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(timelapse.IsEnded);
    }

    [Fact]
    public async Task StopEndsTheAutomaticTimelapseUntilThePrintEnded()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeTimelapse timelapse = await TimelapseAsync(1);

        _service.StopTimelapse("mk4");
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(timelapse.IsEnded);
        Assert.Equal(0, _time.ActiveTimers);

        Raise(PrinterState.Printing);
        Raise(PrinterState.Paused);
        Raise(PrinterState.Printing);
        await SettleAsync();
        Assert.Single(_timelapses.Started);

        // The user may start it again during the print.
        _service.StartTimelapse("mk4");
        FakeTimelapse manual = await TimelapseAsync(2);
        Assert.Equal(CameraRecordingMode.Manual, Status().Mode);
        _service.StopTimelapse("mk4");
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(manual.IsEnded);

        // The next print is taken again.
        Raise(PrinterState.Finished);
        Clock.Advance(TimeSpan.FromMinutes(1));
        Raise(PrinterState.Printing, job: Cube);
        await TimelapseAsync(3);
    }

    [Fact]
    public async Task RecordingAndTimelapseRunTogetherAndStoppingOneKeepsTheOther()
    {
        Configure(Printer(recordPrints: true));
        Raise(PrinterState.Printing);
        FakeTimelapse timelapse = await TimelapseAsync(1);
        await Eventually.TrueAsync(() => _recorder.Started.Length == 1, "recording started");
        FakeRecording recording = _recorder.Started[0];
        recording.BeginFile();
        await Eventually.TrueAsync(() => _service.GetStatus("mk4").Activity == CameraRecordingActivity.Recording, "recording");

        Assert.Equal(Path.Combine(Folder, "2026-11-04_14-05-09_Benchy.mp4"), recording.Path);
        Assert.NotEqual(recording.Path, timelapse.Path);
        Assert.Equal(CameraDashcamActivity.Off, _service.GetStatus("mk4").Dashcam);

        _service.Stop("mk4");
        await Eventually.TrueAsync(() => recording.IsEnded, "recording stopped");
        await SettleAsync();
        Assert.False(timelapse.IsEnded);
        Assert.Equal(CameraRecordingActivity.Recording, Status().Activity);
        Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(2, timelapse.Requests);

        _service.Start("mk4");
        FakeRecording manual = await RecordingStartedAsync(2);
        _service.StopTimelapse("mk4");
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        await SettleAsync();
        Assert.False(manual.IsEnded);
        Assert.Equal(recording.Path, _service.GetStatus("mk4").LastFile);
        Assert.Equal(timelapse.Path, Status().LastFile);
    }

    private async Task<FakeRecording> RecordingStartedAsync(int number)
    {
        await Eventually.TrueAsync(() => _recorder.Started.Length >= number, $"recording {number} started");
        return _recorder.Started[number - 1];
    }

    [Fact]
    public async Task TheIntervalOfTheSettingsAppliesAlsoToARunningTimelapse()
    {
        _service.Configure([Printer()], new CameraRecordingSettings { TimelapseIntervalSeconds = 5 });
        Raise(PrinterState.Printing);
        FakeTimelapse timelapse = await TimelapseAsync(1);
        Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(3, timelapse.Requests);
        Assert.Equal(TimeSpan.FromSeconds(5), Status().Interval);

        _changed.Clear();
        _service.Configure([Printer()], new CameraRecordingSettings { TimelapseIntervalSeconds = 600 });
        Assert.Contains("mk4", _changed);
        Assert.Equal(TimeSpan.FromMinutes(10), Status().Interval);
        // The new trigger starts with a frame, then every ten minutes.
        Assert.Equal(4, timelapse.Requests);
        Clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(4, timelapse.Requests);
        Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(5, timelapse.Requests);
        Assert.Equal(2, _time.ActiveTimers); // still one trigger (and the disk check)
    }

    [Fact]
    public async Task TheTriggerGetsEverySnapshotAndIsDisposedWithTheTimelapse()
    {
        ConcurrentQueue<RecordingTrigger> triggers = new();
        await using CameraRecordingService service = CreateService(trigger: _ =>
        {
            RecordingTrigger trigger = new();
            triggers.Enqueue(trigger);
            return trigger;
        });
        service.Configure([Printer()], CameraRecordingSettings.Default);
        Raise(PrinterState.Printing);
        await Eventually.TrueAsync(() => !triggers.IsEmpty, "trigger created");
        RecordingTrigger created = Assert.Single(triggers);

        Raise(PrinterState.Attention);
        Raise(PrinterState.Printing);

        Assert.Equal([PrinterState.Attention, PrinterState.Printing], created.Snapshots);
        Assert.Equal([true, false, true], created.Capturing);
        service.StopTimelapse("mk4");
        await Eventually.TrueAsync(() => created.IsDisposed, "trigger disposed");
        await Eventually.TrueAsync(() => service.GetStatus("mk4").Timelapse.Activity == CameraRecordingActivity.Idle, "idle");
    }

    [Fact]
    public void StartTimelapseNeedsAConfiguredPrinterWithAnEnabledCameraAndARecorder()
    {
        Configure(Printer(camera: false), Printer("xl", "XL", address: "192.168.0.11"));

        Assert.False(_service.StartTimelapse("mk4"));
        Assert.False(_service.StartTimelapse("unknown"));
        Assert.True(_service.TimelapseAvailability.IsAvailable);
        Assert.Same(CameraTimelapseStatus.None, _service.GetStatus("unknown").Timelapse);

        using CameraRecordingService without = new(_monitor, _recorder, _storage, _time, _logger, new CameraRecordingServiceOptions { DefaultRoot = _root });
        without.Configure([Printer("xl", "XL", address: "192.168.0.11")], CameraRecordingSettings.Default);
        Assert.False(without.TimelapseAvailability.IsAvailable);
        Assert.False(without.StartTimelapse("xl"));
    }

    [Fact]
    public async Task WithoutAMuxerTheTimelapseIsUnavailable()
    {
        _timelapses.Availability = CameraRecorderAvailability.Unavailable("No MP4 muxer.");
        Configure(Printer());

        Assert.False(_service.TimelapseAvailability.IsAvailable);
        Raise(PrinterState.Printing);

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        CameraRecordingError error = Assert.Single(_failed);
        Assert.True(error.IsTimelapse);
        Assert.Equal(CameraRecordingProblem.Unavailable, error.Problem);
        Assert.Equal(CameraRecordingMode.Automatic, error.Mode);
        Assert.Equal(error, Status().LastError);
        Assert.Null(_service.GetStatus("mk4").LastError);
        Assert.Empty(_timelapses.Started);
        Assert.Equal(0, _time.ActiveTimers);

        // Not again during this print.
        Raise(PrinterState.Printing);
        await SettleAsync();
        Assert.Single(_failed);
    }

    [Fact]
    public async Task AFolderThatCannotBeCreatedIsAnErrorWithoutRetries()
    {
        _storage.PrepareFailure = new UnauthorizedAccessException("denied");
        Configure(Printer(timelapsePrints: false));

        _service.StartTimelapse("mk4");

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        CameraRecordingError error = Assert.Single(_failed);
        Assert.Equal(CameraRecordingProblem.FolderUnavailable, error.Problem);
        Assert.Equal(CameraRecordingMode.Manual, error.Mode);
        Assert.True(error.IsTimelapse);
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.Empty(_timelapses.Started);
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Fact]
    public async Task TooLittleFreeSpaceDoesNotStart()
    {
        _storage.FreeBytes = 4_999_999_999;
        Configure(Printer());

        Raise(PrinterState.Printing);

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        Assert.Equal(CameraRecordingProblem.DiskSpaceLow, Assert.Single(_failed).Problem);
        Assert.Empty(_timelapses.Started);
    }

    [Theory]
    [InlineData(CameraRecordingFailure.DiskFull, CameraRecordingProblem.DiskFull)]
    [InlineData(CameraRecordingFailure.WriteFailed, CameraRecordingProblem.WriteFailed)]
    [InlineData(CameraRecordingFailure.TooSlow, CameraRecordingProblem.TooSlow)]
    [InlineData(CameraRecordingFailure.StreamFailed, CameraRecordingProblem.StreamFailed)]
    public async Task AFailedTimelapseIsReportedAndNotRestartedDuringThePrint(CameraRecordingFailure failure, CameraRecordingProblem problem)
    {
        Configure(Printer(recordPrints: true));
        Raise(PrinterState.Printing);
        FakeTimelapse timelapse = await TimelapseAsync(1);
        FakeRecording recording = await RecordingStartedAsync(1);

        timelapse.End(failure, "Broken (test).");

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        CameraRecordingError error = Assert.Single(_failed);
        Assert.True(error.IsTimelapse);
        Assert.Equal(problem, error.Problem);
        Assert.Equal("Broken (test).", error.Message);
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.Equal(error, Status().LastError);
        Assert.Equal(timelapse.Path, Status().LastFile);

        Raise(PrinterState.Printing);
        await SettleAsync();
        Assert.Single(_timelapses.Started);
        Assert.False(recording.IsEnded, "The recording goes on.");

        // The next print starts again, and its first frame clears the error.
        Raise(PrinterState.Finished);
        Clock.Advance(TimeSpan.FromMinutes(1));
        Raise(PrinterState.Printing, job: Cube);
        await TimelapseAsync(2);
        Assert.Null(Status().LastError);
    }

    [Fact]
    public async Task RunningLowOnSpaceStopsTheTimelapseWithAnError()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeTimelapse timelapse = await TimelapseAsync(1);

        _storage.FreeBytes = 4_000_000_000;
        Clock.Advance(TimeSpan.FromSeconds(30));

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        CameraRecordingError error = Assert.Single(_failed);
        Assert.Equal(CameraRecordingProblem.DiskSpaceLow, error.Problem);
        Assert.True(error.IsTimelapse);
        Assert.True(timelapse.IsEnded);
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
    }

    [Fact]
    public async Task ATimelapseCountsInTheSizeLimit()
    {
        _service.Configure([Printer()], new CameraRecordingSettings { SizeLimitGigabytes = 1 });
        Raise(PrinterState.Printing);
        FakeTimelapse timelapse = await TimelapseAsync(1);

        timelapse.BytesWritten = 1_000_000_001;
        _service.CheckDisk();

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        CameraRecordingError error = Assert.Single(_failed);
        Assert.Equal(CameraRecordingProblem.SizeLimitReached, error.Problem);
        Assert.True(error.IsTimelapse);
        Assert.True(File.Exists(timelapse.Path), "A running timelapse is never deleted.");
    }

    [Fact]
    public async Task TheLastTimelapseOfAnEarlierRunIsKnownApartFromTheRecordings()
    {
        CameraRecordingStorage.Instance.PrepareFolder(Folder);
        string timelapse = Path.Combine(Folder, "2026-11-01_08-00-00_Benchy_timelapse.mp4");
        string newerTimelapse = Path.Combine(Folder, "2026-11-02_08-00-00_Cube_timelapse-2.mp4");
        string recording = Path.Combine(Folder, "2026-11-01_09-00-00_Benchy.mp4");
        foreach (string file in new[] { timelapse, newerTimelapse, recording })
        {
            await File.WriteAllBytesAsync(file, [1], TestContext.Current.CancellationToken);
        }

        Configure(Printer());

        await Eventually.TrueAsync(() => Status().LastFile == newerTimelapse, "newest timelapse found");
        Assert.Equal(recording, _service.GetStatus("mk4").LastFile);
    }

    [Fact]
    public async Task TheRotationDeletesAnOldTimelapseWhenItsTurnComes()
    {
        CameraRecordingStorage.Instance.PrepareFolder(Folder);
        string old = Path.Combine(Folder, "2026-11-01_08-00-00_Benchy_timelapse.mp4");
        await File.WriteAllBytesAsync(old, [1], TestContext.Current.CancellationToken);
        Configure(Printer());
        await Eventually.TrueAsync(() => Status().LastFile == old, "timelapse found");

        // A running timelapse alone above the limit: everything older goes first, then it is stopped.
        _service.Configure([Printer()], new CameraRecordingSettings { SizeLimitGigabytes = 1 });
        Raise(PrinterState.Printing);
        FakeTimelapse running = await TimelapseAsync(1);
        running.BytesWritten = 1_000_000_001;
        _service.CheckDisk();

        await Eventually.TrueAsync(() => !File.Exists(old), "old timelapse deleted");
        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        Assert.True(File.Exists(running.Path));
    }

    [Fact]
    public async Task SwitchingTheCameraOffStopsTheTimelapseWithoutAnError()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeTimelapse timelapse = await TimelapseAsync(1);

        // The hub ends the lease of a switched-off camera at the same moment.
        Configure(Printer(camera: false));
        timelapse.End(CameraRecordingFailure.CameraUnavailable);

        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        await SettleAsync();
        Assert.Empty(_failed);
        Assert.False(_service.StartTimelapse("mk4"));
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Fact]
    public async Task SwitchingTheAutomaticTimelapseOffStopsIt()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeTimelapse timelapse = await TimelapseAsync(1);

        Configure(Printer(timelapsePrints: false));

        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(timelapse.IsEnded);
        Assert.Empty(_failed);
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Fact]
    public async Task RemovingThePrinterStopsItsTimelapse()
    {
        Configure(Printer(), Printer("xl", "XL", address: "192.168.0.11"));
        _service.StartTimelapse("xl");
        FakeTimelapse timelapse = await TimelapseAsync(1);

        Configure(Printer());

        await Eventually.TrueAsync(() => timelapse.IsEnded, "timelapse stopped");
        Assert.Same(CameraRecordingStatus.None, _service.GetStatus("xl"));
        Assert.Empty(_failed);
        await Eventually.TrueAsync(() => _time.ActiveTimers == 0, "no timers");
    }

    [Fact]
    public async Task QuittingCompletesEveryTimelapseAndStartsNoneAfterwards()
    {
        Configure(Printer(), Printer("xl", "XL", timelapsePrints: false, address: "192.168.0.11"));
        Raise(PrinterState.Printing);
        FakeTimelapse automatic = await TimelapseAsync(1);
        _service.StartTimelapse("xl");
        FakeTimelapse manual = await TimelapseAsync(2);

        await _service.StopAllAsync().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(automatic.IsEnded);
        Assert.True(manual.IsEnded);
        Assert.Equal(1, automatic.StopCalls);
        Assert.Equal(0, _time.ActiveTimers);
        Assert.False(_service.StartTimelapse("mk4"));
        Raise(PrinterState.Printing, "xl");
        await SettleAsync();
        Assert.Equal(2, _timelapses.Started.Length);
        Assert.Empty(_failed);
        Assert.Contains(_logger.Entries, e => e.Message.Contains("timelapse of printer 'mk4' because the application quits", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATimelapseThatIsStillStartingWhenQuittingIsStoppedToo()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        await Eventually.TrueAsync(() => _timelapses.Started.Length == 1, "started");

        // No key frame yet.
        await _service.StopAllAsync().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(_timelapses.Started[0].IsEnded);
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Fact]
    public async Task LogsNeverContainTheFolder()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        await TimelapseAsync(1);
        _service.StopTimelapse("mk4");
        await StatusAsync("mk4", CameraRecordingActivity.Idle);

        Assert.Contains(_logger.Entries, e => e.Message.Contains("Timelapse of printer 'mk4' started (Automatic)", StringComparison.Ordinal));
        Assert.DoesNotContain(_logger.Entries, e => e.Message.Contains(_root, StringComparison.Ordinal));
    }
}
