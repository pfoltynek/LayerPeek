using System.Collections.Concurrent;

using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras.Recording;

/// <summary>
/// T-148: when the recording service runs and saves the dashcam (user decisions 2026-11-04), with fake snapshots, a fake
/// clock, fake dashcams and real files in a temporary folder.
/// </summary>
public sealed class CameraDashcamServiceTests : IAsyncDisposable
{
    private const string Benchy = "benchy_0.4n_0.2mm_PLA_MK4S_1h2m.bgcode";
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 14, 5, 9, TimeSpan.Zero);
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly string _root = TestDirectories.NewDirectoryPath();
    private readonly SnapshotMonitor _monitor = new();
    private readonly FakeCameraRecorder _recorder = new();
    private readonly FakeRecordingStorage _storage = new();
    private readonly CountingTimeProvider _time = new(new FakeTimeProvider(Start) { });
    private readonly ListLogger<CameraRecordingService> _logger = new();
    private readonly CameraRecordingService _service;
    private readonly ConcurrentQueue<string> _changed = new();
    private readonly ConcurrentQueue<CameraRecordingError> _failed = new();

    public CameraDashcamServiceTests()
    {
        _service = new CameraRecordingService(_monitor, _recorder, _storage, _time, _logger, new CameraRecordingServiceOptions { DefaultRoot = _root });
        _service.Changed += (_, printerId) => _changed.Enqueue(printerId);
        _service.Failed += (_, error) => _failed.Enqueue(error);
    }

    private FakeTimeProvider Clock => _time.Fake;

    private string Folder => Path.Combine(_root, "Workshop MK4");

    public async ValueTask DisposeAsync()
    {
        await _service.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        TestDirectories.Delete(_root);
    }

    private static PrinterDefinition Printer(bool dashcam = true, bool recordPrints = false, bool camera = true, string id = "mk4") => new()
    {
        Id = id,
        Name = "Workshop MK4",
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.10",
        Camera = new CameraSettings(camera, StreamUri) { RecordPrints = recordPrints, Dashcam = dashcam },
    };

    private void Configure(PrinterDefinition printer, CameraRecordingSettings? settings = null) =>
        _service.Configure([printer], settings ?? CameraRecordingSettings.Default);

    private void Raise(PrinterState state, string? job = Benchy) =>
        _monitor.Raise(new PrinterSnapshot
        {
            PrinterId = "mk4",
            State = state,
            Timestamp = Clock.GetUtcNow(),
            Job = job is null ? null : new PrintJob { FileName = job, ProgressPercent = 10 },
        });

    private async Task<FakeDashcam> DashcamAsync(int number)
    {
        await Eventually.TrueAsync(() => _recorder.Dashcams.Length >= number, $"dashcam {number} started");
        await DashcamStatusAsync(CameraDashcamActivity.Buffering);
        return _recorder.Dashcams[number - 1];
    }

    private async Task DashcamStatusAsync(CameraDashcamActivity activity) =>
        await Eventually.TrueAsync(() => _service.GetStatus("mk4").Dashcam == activity, $"dashcam {activity}");

    private static async Task SaveAsync(FakeDashcam dashcam, int number = 1) =>
        await Eventually.TrueAsync(() => dashcam.Saves.Length >= number, $"save {number} started");

    /// <summary>Lets the thread pool run what a snapshot or timer may have started (there should be nothing).</summary>
    private static Task SettleAsync() => Task.Delay(50, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ThereIsNoDashcamByDefault()
    {
        Configure(Printer(dashcam: false));
        Raise(PrinterState.Printing);
        Raise(PrinterState.Error);
        await SettleAsync();

        Assert.Empty(_recorder.Dashcams);
        Assert.Equal(CameraDashcamActivity.Off, _service.GetStatus("mk4").Dashcam);
        Assert.False(_service.SaveDashcam("mk4"));
        Assert.False(CameraSettings.Disabled.Dashcam);
    }

    [Fact]
    public async Task TheDashcamBuffersOnlyDuringAPrint()
    {
        Configure(Printer(), CameraRecordingSettings.Default with { DashcamMinutes = 5 });
        Raise(PrinterState.Idle);
        await SettleAsync();
        Assert.Empty(_recorder.Dashcams);
        Assert.Equal(CameraDashcamActivity.Waiting, _service.GetStatus("mk4").Dashcam);
        Assert.Equal(0, _time.ActiveTimers);

        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);
        Assert.Equal(TimeSpan.FromMinutes(5), dashcam.Options.BufferDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), dashcam.Options.PostEventDuration);
        CameraRecordingStatus status = _service.GetStatus("mk4");
        Assert.Equal(5, status.DashcamMinutes);
        Assert.Equal(TimeSpan.FromSeconds(42), status.DashcamBuffered);
        Assert.False(status.IsActive);

        // Paused and waiting for the user are still the print.
        Raise(PrinterState.Paused);
        Raise(PrinterState.Printing);
        await SettleAsync();
        Assert.False(dashcam.EndRequested);

        Raise(PrinterState.Finished);
        await Eventually.TrueAsync(() => dashcam.IsEnded, "dashcam ended with the print");
        await DashcamStatusAsync(CameraDashcamActivity.Waiting);
        Assert.Empty(dashcam.Saves);
        Assert.Single(_recorder.Dashcams);
        Assert.Equal(0, _time.ActiveTimers);
        Assert.Empty(_failed);
        Assert.Contains("mk4", _changed);
    }

    [Theory]
    [InlineData(PrinterState.Error, "error")]
    [InlineData(PrinterState.Stopped, "stopped")]
    public async Task AnErrorOrAStoppedPrintSavesTheLastMinutesAndThenEndsTheDashcam(PrinterState end, string marker)
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);
        Clock.Advance(TimeSpan.FromMinutes(10));

        Raise(end);
        await SaveAsync(dashcam);
        string expected = Path.Combine(Folder, $"2026-11-04_14-15-09_Benchy_dashcam-{marker}.mp4");
        Assert.Equal([expected], dashcam.Saves);
        Assert.True(File.Exists(Path.Combine(Folder, CameraRecordingFolders.MarkerFileName)));
        // The print is over: the save goes on to its end, then the dashcam ends (and holds nothing meanwhile).
        await Eventually.TrueAsync(() => dashcam.EndRequested, "end after the save");
        Assert.False(dashcam.IsEnded);
        await DashcamStatusAsync(CameraDashcamActivity.Saving);

        dashcam.EndSave();
        await Eventually.TrueAsync(() => dashcam.IsEnded, "dashcam ended after its save");
        await DashcamStatusAsync(CameraDashcamActivity.Waiting);
        Assert.Equal(expected, _service.GetStatus("mk4").LastFile);
        Assert.Equal(0, dashcam.StopCalls);
        Assert.Empty(_failed);
    }

    [Fact]
    public async Task APrinterThatWaitsForTheUserSavesAndGoesOnBuffering()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);

        Raise(PrinterState.Attention);
        Raise(PrinterState.Attention);
        await SaveAsync(dashcam);
        dashcam.EndSave();
        await DashcamStatusAsync(CameraDashcamActivity.Buffering);

        Assert.Equal([Path.Combine(Folder, "2026-11-04_14-05-09_Benchy_dashcam-attention.mp4")], dashcam.Saves);
        Assert.False(dashcam.EndRequested);

        // Every new change into the state saves again.
        Raise(PrinterState.Printing);
        Clock.Advance(TimeSpan.FromMinutes(1));
        Raise(PrinterState.Attention);
        await SaveAsync(dashcam, 2);
        Assert.EndsWith("2026-11-04_14-06-09_Benchy_dashcam-attention.mp4", dashcam.Saves[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnotherEventDuringASaveExtendsIt()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);

        Raise(PrinterState.Attention);
        await SaveAsync(dashcam);
        Raise(PrinterState.Error);
        await Eventually.TrueAsync(() => dashcam.Extensions == 1, "save extended");
        Assert.Single(dashcam.Saves);

        Assert.True(_service.SaveDashcam("mk4"));
        await Eventually.TrueAsync(() => dashcam.Extensions == 2, "save extended by the user");
        Assert.Single(dashcam.Saves);
    }

    [Theory]
    [InlineData(PrinterState.Error)]
    [InlineData(PrinterState.Attention)]
    [InlineData(PrinterState.Stopped)]
    public async Task ATriggerThatIsSwitchedOffSavesNothing(PrinterState state)
    {
        CameraRecordingSettings settings = CameraRecordingSettings.Default with
        {
            DashcamOnError = false,
            DashcamOnAttention = false,
            DashcamOnStopped = false,
            DashcamOnFailureDetection = false,
        };
        Configure(Printer(), settings);
        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);

        Raise(state);
        Assert.False(_service.SaveDashcam("mk4", CameraDashcamTrigger.FailureDetected));
        await SettleAsync();
        Assert.Empty(dashcam.Saves);

        // "Save the last minutes" of the user always works while it buffers.
        if (state == PrinterState.Attention)
        {
            Assert.True(_service.SaveDashcam("mk4"));
            await SaveAsync(dashcam);
            Assert.EndsWith("_Benchy_dashcam-manual.mp4", dashcam.Saves[0], StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheFailureDetectionSavesThroughItsHook()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);

        Assert.True(_service.SaveDashcam("mk4", CameraDashcamTrigger.FailureDetected));
        await SaveAsync(dashcam);

        Assert.EndsWith("_Benchy_dashcam-failure.mp4", dashcam.Saves[0], StringComparison.Ordinal);
        Assert.False(dashcam.EndRequested);
    }

    [Fact]
    public async Task NothingIsSavedWithoutABuffer()
    {
        Configure(Printer());
        Raise(PrinterState.Idle);
        Assert.False(_service.SaveDashcam("mk4"));
        Assert.False(_service.SaveDashcam("other"));

        // An app that starts while the printer waits for the user has no last minutes yet: no save, but a buffer from now.
        Raise(PrinterState.Attention);
        FakeDashcam dashcam = await DashcamAsync(1);
        await SettleAsync();
        Assert.Empty(dashcam.Saves);
    }

    [Fact]
    public async Task TheDashcamWaitsWhileTheWholePrintIsRecorded()
    {
        Configure(Printer(recordPrints: true));
        Raise(PrinterState.Printing);
        await Eventually.TrueAsync(() => _recorder.Started.Length == 1, "automatic recording");
        Raise(PrinterState.Error);
        await SettleAsync();

        Assert.Empty(_recorder.Dashcams);
        Assert.Equal(CameraDashcamActivity.Waiting, _service.GetStatus("mk4").Dashcam);
        Assert.False(_service.SaveDashcam("mk4"));
    }

    [Fact]
    public async Task AManualRecordingTakesOverAndTheDashcamReturnsAfterIt()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeDashcam first = await DashcamAsync(1);
        Raise(PrinterState.Attention);
        await SaveAsync(first);

        // The recording starts at once; the running save finishes (nothing lost), then the dashcam ends.
        Assert.True(_service.Start("mk4"));
        await Eventually.TrueAsync(() => _recorder.Started.Length == 1, "manual recording");
        await Eventually.TrueAsync(() => first.EndRequested, "dashcam ends after its save");
        first.EndSave();
        await Eventually.TrueAsync(() => first.IsEnded, "dashcam ended");
        _recorder.Started[0].BeginFile();
        await DashcamStatusAsync(CameraDashcamActivity.Waiting);
        Assert.Single(_recorder.Dashcams);

        // Stopped mid-print: the dashcam holds the last minutes again.
        _service.Stop("mk4");
        await Eventually.TrueAsync(() => _service.GetStatus("mk4").Activity == CameraRecordingActivity.Idle, "recording stopped");
        await DashcamAsync(2);
    }

    [Fact]
    public async Task ADashcamThatFailsIsReportedAndNotRestartedBeforeTheNextPrint()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);

        dashcam.End(CameraRecordingFailure.TooSlow);
        await Eventually.TrueAsync(() => !_failed.IsEmpty, "failure reported");
        Assert.Equal(CameraDashcamActivity.Waiting, _service.GetStatus("mk4").Dashcam);
        CameraRecordingError error = Assert.Single(_failed);
        Assert.Equal(CameraRecordingProblem.TooSlow, error.Problem);
        Assert.Equal(CameraRecordingMode.Dashcam, error.Mode);
        Assert.Equal(error, _service.GetStatus("mk4").LastError);

        Raise(PrinterState.Printing);
        await SettleAsync();
        Assert.Single(_recorder.Dashcams);

        Raise(PrinterState.Finished);
        Raise(PrinterState.Printing);
        await DashcamAsync(2);
    }

    [Fact]
    public async Task ASaveThatFailsIsReportedAndTheDashcamGoesOn()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);
        Assert.True(_service.SaveDashcam("mk4"));
        await SaveAsync(dashcam);

        dashcam.EndSave(CameraRecordingFailure.DiskFull, writeFile: false);

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "failure reported");
        CameraRecordingError error = Assert.Single(_failed);
        Assert.Equal(CameraRecordingProblem.DiskFull, error.Problem);
        Assert.Equal(CameraRecordingMode.Dashcam, error.Mode);
        await DashcamStatusAsync(CameraDashcamActivity.Buffering);
        Assert.False(dashcam.EndRequested);
    }

    [Fact]
    public async Task AFolderThatCannotBeWrittenOrAFullDiskSavesNothing()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);

        _storage.PrepareFailure = new UnauthorizedAccessException("denied");
        Assert.True(_service.SaveDashcam("mk4"));
        await Eventually.TrueAsync(() => _failed.Count == 1, "folder failure");
        Assert.Equal(CameraRecordingProblem.FolderUnavailable, _failed.Single().Problem);

        _storage.PrepareFailure = null;
        _storage.FreeBytes = 1_000_000_000;
        Raise(PrinterState.Attention);
        await Eventually.TrueAsync(() => _failed.Count == 2, "free space failure");
        Assert.Equal(CameraRecordingProblem.DiskSpaceLow, _failed.Last().Problem);

        Assert.Empty(dashcam.Saves);
        await DashcamStatusAsync(CameraDashcamActivity.Buffering);
    }

    [Fact]
    public async Task SwitchingTheDashcamOffEndsItAtOnce()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);
        Raise(PrinterState.Attention);
        await SaveAsync(dashcam);

        Configure(Printer(dashcam: false));

        await Eventually.TrueAsync(() => dashcam.IsEnded, "dashcam stopped");
        Assert.Equal(1, dashcam.StopCalls);
        Assert.Equal(CameraDashcamActivity.Off, _service.GetStatus("mk4").Dashcam);
        Assert.Empty(_failed);

        // Disabling the camera ends it as well; the camera going away at that moment is no error.
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeDashcam second = await DashcamAsync(2);
        Configure(Printer(camera: false));
        second.End(CameraRecordingFailure.CameraUnavailable);
        await Eventually.TrueAsync(() => second.IsEnded, "dashcam stopped");
        await SettleAsync();
        Assert.Empty(_failed);
        Assert.Equal(CameraDashcamActivity.Off, _service.GetStatus("mk4").Dashcam);
    }

    [Fact]
    public async Task AnUnreachablePrinterKeepsTheDashcamForTheGraceTime()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);

        Raise(PrinterState.Offline);
        Clock.Advance(TimeSpan.FromSeconds(59));
        Raise(PrinterState.Printing);
        await SettleAsync();
        Assert.False(dashcam.EndRequested);
        Assert.Equal(0, _time.ActiveTimers);

        // An error right after an outage is still an event of the print.
        Raise(PrinterState.Offline);
        Raise(PrinterState.Error);
        await SaveAsync(dashcam);
        await Eventually.TrueAsync(() => dashcam.EndRequested, "error ended the print");

        // The print goes on during the save: the old dashcam finishes its save, a new one holds the print from then.
        Raise(PrinterState.Printing);
        await SettleAsync();
        Assert.Single(_recorder.Dashcams);
        dashcam.EndSave();
        FakeDashcam second = await DashcamAsync(2);
        Raise(PrinterState.Offline);
        Clock.Advance(TimeSpan.FromMinutes(1));
        await Eventually.TrueAsync(() => second.IsEnded, "dashcam ended after the grace time");
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Fact]
    public async Task QuittingStopsTheDashcamAndCompletesItsSave()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);
        Assert.True(_service.SaveDashcam("mk4"));
        await SaveAsync(dashcam);

        await _service.StopAllAsync().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(dashcam.IsEnded);
        Assert.Equal(1, dashcam.StopCalls);
        Assert.True(File.Exists(dashcam.Saves[0]));
        Assert.False(_service.SaveDashcam("mk4"));
        Raise(PrinterState.Printing);
        await SettleAsync();
        Assert.Single(_recorder.Dashcams);
        Assert.Empty(_failed);
    }

    [Fact]
    public async Task TheRotationNeverDeletesTheFileOfASave()
    {
        Configure(Printer(), CameraRecordingSettings.Default with { SizeLimitGigabytes = 1 });
        Raise(PrinterState.Printing);
        FakeDashcam dashcam = await DashcamAsync(1);
        Assert.True(_service.SaveDashcam("mk4"));
        await SaveAsync(dashcam);
        dashcam.BeginFile();
        dashcam.SaveBytesWritten = 2_000_000_000;

        // Over the limit by the running save alone: older files go, the save stays.
        string older = Path.Combine(Folder, "2026-11-01_10-00-00_Old.mp4");
        await File.WriteAllBytesAsync(older, [1, 2, 3], TestContext.Current.CancellationToken);
        Configure(Printer(), CameraRecordingSettings.Default with { SizeLimitGigabytes = 2 });
        await Eventually.TrueAsync(() => !File.Exists(older), "older recording rotated");

        Assert.True(File.Exists(dashcam.Saves[0]));
        Assert.False(dashcam.IsEnded);
    }

    [Fact]
    public void TheNameOfASaveTellsTheTriggerAndIsARecordingName()
    {
        DateTimeOffset time = new(2026, 11, 4, 14, 5, 9, TimeSpan.Zero);
        Assert.Equal("2026-11-04_14-05-09_Benchy_dashcam-error.mp4", CameraRecordingFolders.DashcamFileName(time, Benchy, CameraDashcamTrigger.Error));
        Assert.Equal("2026-11-04_14-05-09_dashcam-manual.mp4", CameraRecordingFolders.DashcamFileName(time, null, CameraDashcamTrigger.Manual));
        foreach (CameraDashcamTrigger trigger in Enum.GetValues<CameraDashcamTrigger>())
        {
            Assert.True(CameraRecordingFolders.IsRecordingFileName(CameraRecordingFolders.DashcamFileName(time, Benchy, trigger)));
        }
    }
}
