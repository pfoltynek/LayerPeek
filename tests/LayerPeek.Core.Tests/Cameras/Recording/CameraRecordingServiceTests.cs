using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras.Recording;

/// <summary>
/// T-145: when the recording service records (user decisions 2026-11-03), with fake snapshots, a fake clock, a fake
/// recorder and real files in a temporary folder.
/// </summary>
public sealed class CameraRecordingServiceTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 14, 5, 9, TimeSpan.Zero);
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly string _root = TestDirectories.NewDirectoryPath();
    private readonly SnapshotMonitor _monitor = new();
    private readonly FakeCameraRecorder _recorder = new();
    private readonly FakeRecordingStorage _storage = new();
    private readonly CountingTimeProvider _time = new(new FakeTimeProvider(Start));
    private readonly ListLogger<CameraRecordingService> _logger = new();
    private readonly CameraRecordingService _service;
    private readonly ConcurrentQueue<string> _changed = new();
    private readonly ConcurrentQueue<CameraRecordingError> _failed = new();

    public CameraRecordingServiceTests()
    {
        _service = new CameraRecordingService(_monitor, _recorder, _storage, _time, _logger, new CameraRecordingServiceOptions { DefaultRoot = _root });
        _service.Changed += (_, printerId) => _changed.Enqueue(printerId);
        _service.Failed += (_, error) => _failed.Enqueue(error);
    }

    private FakeTimeProvider Clock => _time.Fake;

    public async ValueTask DisposeAsync()
    {
        await _service.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        TestDirectories.Delete(_root);
    }

    private static PrinterDefinition Printer(string id = "mk4", string name = "Workshop MK4", bool recordPrints = true, bool camera = true, string address = "192.168.0.10") => new()
    {
        Id = id,
        Name = name,
        ConnectionType = ConnectionType.PrusaLink,
        Address = address,
        Camera = new CameraSettings(camera, StreamUri) { RecordPrints = recordPrints },
    };

    private void Configure(params PrinterDefinition[] printers) => _service.Configure(printers, CameraRecordingSettings.Default);

    private void Raise(PrinterState state, string id = "mk4", string? job = "benchy_0.4n_0.2mm_PLA_MK4S_1h2m.bgcode") =>
        _monitor.Raise(new PrinterSnapshot
        {
            PrinterId = id,
            State = state,
            Timestamp = Clock.GetUtcNow(),
            Job = job is null ? null : new PrintJob { FileName = job, ProgressPercent = 10 },
        });

    private async Task<FakeRecording> RecordingAsync(int number)
    {
        await Eventually.TrueAsync(() => _recorder.Started.Length >= number, $"recording {number} started");
        FakeRecording recording = _recorder.Started[number - 1];
        recording.BeginFile();
        await StatusAsync(recording.PrinterId, CameraRecordingActivity.Recording);
        return recording;
    }

    private async Task StatusAsync(string printerId, CameraRecordingActivity activity) =>
        await Eventually.TrueAsync(() => _service.GetStatus(printerId).Activity == activity, $"{printerId} {activity}");

    /// <summary>Lets the thread pool run what a snapshot or timer may have started (there should be nothing).</summary>
    private static Task SettleAsync() => Task.Delay(50, TestContext.Current.CancellationToken);

    [Fact]
    public async Task AnAutomaticRecordingStartsWithThePrintAndEndsAMinuteAfterIt()
    {
        Configure(Printer());
        Raise(PrinterState.Idle);
        await SettleAsync();
        Assert.Empty(_recorder.Started);

        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        CameraRecordingStatus status = _service.GetStatus("mk4");
        Assert.Equal(CameraRecordingMode.Automatic, status.Mode);
        Assert.Equal(Start, status.StartedAt);
        Assert.Equal(recording.Path, status.CurrentFile);
        Assert.Equal(Path.Combine(_root, "Workshop MK4", "2026-11-03_14-05-09_Benchy.mp4"), recording.Path);
        Assert.True(File.Exists(Path.Combine(_root, "Workshop MK4", CameraRecordingFolders.MarkerFileName)));

        Raise(PrinterState.Finished);
        Clock.Advance(TimeSpan.FromSeconds(59));
        Raise(PrinterState.Finished);
        await SettleAsync();
        Assert.False(recording.IsEnded, "Recording goes on for a minute after the print.");

        Clock.Advance(TimeSpan.FromSeconds(1));
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.Equal(1, recording.StopCalls);
        Assert.Equal(recording.Path, _service.GetStatus("mk4").LastFile);
        Assert.Empty(_failed);
        Assert.Contains("mk4", _changed);
    }

    [Theory]
    [InlineData(PrinterState.Stopped)]
    [InlineData(PrinterState.Error)]
    [InlineData(PrinterState.Idle)]
    [InlineData(PrinterState.Busy)]
    public async Task EveryEndOfThePrintEndsTheRecordingAMinuteLater(PrinterState end)
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        Raise(end);
        Clock.Advance(TimeSpan.FromSeconds(30));
        await SettleAsync();
        Assert.False(recording.IsEnded);
        Clock.Advance(TimeSpan.FromSeconds(30));

        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(recording.IsEnded);
    }

    [Theory]
    [InlineData(PrinterState.Paused)]
    [InlineData(PrinterState.Attention)]
    public async Task APauseOrAWaitingPrinterKeepsRecording(PrinterState state)
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        Raise(state);
        Clock.Advance(TimeSpan.FromMinutes(30));
        Raise(state);
        Raise(PrinterState.Printing);
        await SettleAsync();

        Assert.False(recording.IsEnded);
        Assert.Single(_recorder.Started);
        Assert.Equal(CameraRecordingActivity.Recording, _service.GetStatus("mk4").Activity);
    }

    [Fact]
    public async Task APrintThatGoesOnWithinTheMinuteKeepsItsRecording()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        Raise(PrinterState.Busy);
        Clock.Advance(TimeSpan.FromSeconds(40));
        Raise(PrinterState.Printing);
        Clock.Advance(TimeSpan.FromMinutes(5));
        await SettleAsync();

        Assert.False(recording.IsEnded);
        Assert.Single(_recorder.Started);
    }

    [Theory]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Unknown)]
    [InlineData(PrinterState.Unauthorized)]
    public async Task AnUnreachablePrinterEndsTheRecordingAfterTheTolerance(PrinterState unreachable)
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        Raise(unreachable);
        Clock.Advance(TimeSpan.FromSeconds(59));
        Raise(unreachable);
        await SettleAsync();
        Assert.False(recording.IsEnded, "A short outage is tolerated (like T-079).");

        Clock.Advance(TimeSpan.FromSeconds(1));
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(recording.IsEnded);
        Assert.Empty(_failed);

        // Back and still printing: a new recording (a new file) for the rest of the print.
        Raise(PrinterState.Printing);
        await RecordingAsync(2);
    }

    [Fact]
    public async Task AShortOutageKeepsTheRecording()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        Raise(PrinterState.Offline);
        Clock.Advance(TimeSpan.FromSeconds(45));
        Raise(PrinterState.Printing);
        Clock.Advance(TimeSpan.FromMinutes(5));
        await SettleAsync();

        Assert.False(recording.IsEnded);
    }

    [Fact]
    public async Task AnOutageAfterThePrintEndedDoesNotExtendTheMinute()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        Raise(PrinterState.Finished);
        Clock.Advance(TimeSpan.FromSeconds(30));
        Raise(PrinterState.Offline);
        Clock.Advance(TimeSpan.FromSeconds(30));

        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(recording.IsEnded);
    }

    [Fact]
    public async Task AutomaticRecordingIsOffByDefault()
    {
        Assert.False(CameraSettings.Disabled.RecordPrints);
        Assert.False(new CameraSettings(true, StreamUri).RecordPrints);
        Configure(Printer(recordPrints: false));

        Raise(PrinterState.Printing);
        Raise(PrinterState.Finished);
        await SettleAsync();

        Assert.Empty(_recorder.Started);
        Assert.Equal(0, _time.ActiveTimers);
        CameraRecordingStatus status = _service.GetStatus("mk4");
        Assert.False(status.RecordsPrints);
        Assert.True(status.CanRecord);
    }

    [Fact]
    public async Task IdleCostsNoTimers()
    {
        Configure(Printer(), Printer("xl", "XL", recordPrints: false, address: "192.168.0.11"));
        Raise(PrinterState.Idle);
        Raise(PrinterState.Printing, "xl");
        Raise(PrinterState.Finished, "xl");
        await SettleAsync();
        Assert.Equal(0, _time.ActiveTimers);

        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);
        Assert.Equal(1, _time.ActiveTimers); // the disk check while recording

        Raise(PrinterState.Finished);
        Assert.Equal(2, _time.ActiveTimers); // and the minute after the print
        Clock.Advance(TimeSpan.FromMinutes(1));
        await StatusAsync("mk4", CameraRecordingActivity.Idle);

        Assert.True(recording.IsEnded);
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Fact]
    public async Task ARestartOfTheMonitoringKeepsTheRecording()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        // Settings saved: same machine (also renamed), a new monitoring run whose first snapshot is still printing.
        Configure(Printer(name: "Renamed MK4"));
        Raise(PrinterState.Printing);
        await SettleAsync();

        Assert.False(recording.IsEnded);
        Assert.Single(_recorder.Started);
    }

    [Fact]
    public async Task SwitchingAutomaticRecordingOffStopsTheAutomaticRecording()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        Configure(Printer(recordPrints: false));

        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(recording.IsEnded);
        Assert.Empty(_failed);
        Assert.Equal(0, _time.ActiveTimers);
    }

    [Fact]
    public async Task SwitchingAutomaticRecordingOnDuringAPrintRecordsFromTheNextSnapshot()
    {
        Configure(Printer(recordPrints: false));
        Raise(PrinterState.Printing);

        Configure(Printer());
        Raise(PrinterState.Printing);

        await RecordingAsync(1);
    }

    [Fact]
    public async Task AManualRecordingRunsUntilItIsStopped()
    {
        Configure(Printer(recordPrints: false));
        Raise(PrinterState.Idle, job: null);

        Assert.True(_service.Start("mk4"));
        FakeRecording recording = await RecordingAsync(1);
        Assert.Equal(CameraRecordingMode.Manual, _service.GetStatus("mk4").Mode);
        Assert.Equal(Path.Combine(_root, "Workshop MK4", "2026-11-03_14-05-09.mp4"), recording.Path);

        Raise(PrinterState.Finished);
        Clock.Advance(TimeSpan.FromHours(2));
        await SettleAsync();
        Assert.False(recording.IsEnded);

        _service.Stop("mk4");
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(recording.IsEnded);
        Assert.Equal(recording.Path, _service.GetStatus("mk4").LastFile);
    }

    [Fact]
    public async Task AManualRecordingOfAPrintIsNamedAfterThePrint()
    {
        Configure(Printer(recordPrints: false));
        Raise(PrinterState.Printing);

        _service.Start("mk4");
        FakeRecording recording = await RecordingAsync(1);

        Assert.EndsWith("_Benchy.mp4", recording.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartDuringAnAutomaticRecordingAddsNothing()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        Assert.True(_service.Start("mk4"));
        await SettleAsync();
        Assert.Single(_recorder.Started);
        Assert.Equal(CameraRecordingMode.Automatic, _service.GetStatus("mk4").Mode);

        // Still the automatic recording: it ends with the print.
        Raise(PrinterState.Finished);
        Clock.Advance(TimeSpan.FromMinutes(1));
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(recording.IsEnded);
    }

    [Fact]
    public async Task StopEndsTheAutomaticRecordingUntilThePrintEnded()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording first = await RecordingAsync(1);

        _service.Stop("mk4");
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.True(first.IsEnded);

        Raise(PrinterState.Printing);
        Raise(PrinterState.Paused);
        Raise(PrinterState.Printing);
        Raise(PrinterState.Offline);
        Clock.Advance(TimeSpan.FromMinutes(2));
        Raise(PrinterState.Printing);
        await SettleAsync();
        Assert.Single(_recorder.Started);

        // The print ended (a minute later): the next print records again.
        Raise(PrinterState.Finished);
        Clock.Advance(TimeSpan.FromMinutes(1));
        Raise(PrinterState.Idle);
        Raise(PrinterState.Printing);
        await RecordingAsync(2);
        Assert.Empty(_failed);
    }

    [Fact]
    public async Task AManualRecordingStaysManualThroughAnAutomaticPrint()
    {
        Configure(Printer());
        Raise(PrinterState.Idle);
        _service.Start("mk4");
        FakeRecording recording = await RecordingAsync(1);

        Raise(PrinterState.Printing);
        Raise(PrinterState.Finished);
        Clock.Advance(TimeSpan.FromMinutes(5));
        await SettleAsync();

        Assert.False(recording.IsEnded);
        Assert.Single(_recorder.Started);
        Assert.Equal(CameraRecordingMode.Manual, _service.GetStatus("mk4").Mode);
    }

    [Fact]
    public void StartNeedsAConfiguredPrinterWithAnEnabledCamera()
    {
        Configure(Printer(camera: false));

        Assert.False(_service.Start("mk4"));
        Assert.False(_service.Start("unknown"));
        Assert.False(_service.GetStatus("mk4").CanRecord);
        Assert.Same(CameraRecordingStatus.None, _service.GetStatus("unknown"));
        _service.Stop("unknown");
    }

    [Fact]
    public async Task AFolderThatCannotBeCreatedIsAnErrorWithoutRetries()
    {
        // The root is a file: no folder can be created in it.
        Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
        await File.WriteAllTextAsync(_root, "not a folder", TestContext.Current.CancellationToken);
        try
        {
            Configure(Printer());
            Raise(PrinterState.Printing);

            await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
            CameraRecordingError error = Assert.Single(_failed);
            Assert.Equal(CameraRecordingProblem.FolderUnavailable, error.Problem);
            Assert.Equal(CameraRecordingMode.Automatic, error.Mode);
            Assert.DoesNotContain(_root, error.Message, StringComparison.Ordinal);
            await StatusAsync("mk4", CameraRecordingActivity.Idle);
            Assert.Equal(error, _service.GetStatus("mk4").LastError);
            Assert.Empty(_recorder.Started);

            // Not again for this print (no error loop), every snapshot.
            Raise(PrinterState.Printing);
            Raise(PrinterState.Printing);
            await SettleAsync();
            Assert.Single(_failed);
        }
        finally
        {
            File.Delete(_root);
        }
    }

    [Fact]
    public async Task AFolderWithoutPermissionIsAnError()
    {
        _storage.PrepareFailure = new UnauthorizedAccessException("denied");
        Configure(Printer());

        _service.Start("mk4");

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        Assert.Equal(CameraRecordingProblem.FolderUnavailable, Assert.Single(_failed).Problem);
        Assert.Equal(CameraRecordingActivity.Idle, _service.GetStatus("mk4").Activity);
    }

    [Fact]
    public async Task TooLittleFreeSpaceDoesNotStart()
    {
        _storage.FreeBytes = 4_999_999_999;
        Configure(Printer());

        Raise(PrinterState.Printing);

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        CameraRecordingError error = Assert.Single(_failed);
        Assert.Equal(CameraRecordingProblem.DiskSpaceLow, error.Problem);
        Assert.Contains("5 GB", error.Message, StringComparison.Ordinal);
        Assert.Empty(_recorder.Started);
    }

    [Fact]
    public async Task RunningLowOnSpaceStopsTheRecordingWithAnError()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        _storage.FreeBytes = 4_000_000_000;
        Clock.Advance(TimeSpan.FromSeconds(30));

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        Assert.Equal(CameraRecordingProblem.DiskSpaceLow, Assert.Single(_failed).Problem);
        Assert.True(recording.IsEnded);
        await StatusAsync("mk4", CameraRecordingActivity.Idle);

        // Not restarted during this print.
        _storage.FreeBytes = 100_000_000_000;
        Raise(PrinterState.Printing);
        await SettleAsync();
        Assert.Single(_recorder.Started);
    }

    [Fact]
    public async Task ARecordingAloneAboveTheLimitIsStoppedWithAnError()
    {
        _service.Configure([Printer()], new CameraRecordingSettings { SizeLimitGigabytes = 1 });
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        recording.BytesWritten = 1_000_000_001;
        Clock.Advance(TimeSpan.FromSeconds(30));

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        Assert.Equal(CameraRecordingProblem.SizeLimitReached, Assert.Single(_failed).Problem);
        Assert.True(recording.IsEnded);
    }

    [Fact]
    public async Task TheRotationNeverDeletesTheRunningRecordingOfAFolderWrittenWithDots()
    {
        // A hand-edited settings.json: the rotation enumerates normalized paths, the running file must compare equal.
        _service.Configure([Printer()], new CameraRecordingSettings { Folder = Path.Combine(_root, "x", ".."), SizeLimitGigabytes = 1 });
        Assert.Equal(_root, _service.RecordingsFolder);
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        recording.BytesWritten = 1_000_000_001;
        _service.CheckDisk();

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        Assert.Equal(CameraRecordingProblem.SizeLimitReached, Assert.Single(_failed).Problem);
        Assert.True(File.Exists(recording.Path));
    }

    [Fact]
    public async Task TheFileOfAStartingRecordingIsNeverDeleted()
    {
        _service.Configure([Printer(recordPrints: false)], new CameraRecordingSettings { SizeLimitGigabytes = 1 });
        _service.Start("mk4");
        await Eventually.TrueAsync(() => _recorder.Started.Length == 1, "recording started");
        FakeRecording starting = _recorder.Started[0];
        // The recorder created its file, but the recording lists it only once the first key frame is written.
        await File.WriteAllBytesAsync(starting.Path, [1, 2, 3], TestContext.Current.CancellationToken);
        await StatusAsync("mk4", CameraRecordingActivity.Starting);

        starting.BytesWritten = 1_000_000_001;
        _service.CheckDisk();

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        Assert.Equal(CameraRecordingProblem.SizeLimitReached, Assert.Single(_failed).Problem);
        Assert.True(File.Exists(starting.Path));
    }

    [Fact]
    public async Task AFolderOfTheUserWithThePrintersNameIsNeverTakenOver()
    {
        string folder = Path.Combine(_root, "Workshop MK4");
        Directory.CreateDirectory(folder);
        string video = Path.Combine(folder, "2026-11-01_08-00-00.mp4");
        await File.WriteAllBytesAsync(video, [1], TestContext.Current.CancellationToken);
        _service.Configure([Printer(recordPrints: false)], new CameraRecordingSettings { SizeLimitGigabytes = 1 });

        _service.Start("mk4");

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        CameraRecordingError error = Assert.Single(_failed);
        Assert.Equal(CameraRecordingProblem.FolderUnavailable, error.Problem);
        Assert.DoesNotContain(_root, error.Message, StringComparison.Ordinal);
        Assert.Empty(_recorder.Started);
        Assert.False(File.Exists(Path.Combine(folder, CameraRecordingFolders.MarkerFileName)));
        Assert.True(File.Exists(video));
    }

    [Fact]
    public async Task DisposingSynchronouslyStopsTheRecordings()
    {
        // A container disposed after a failed start (ServiceHost.DisposeAfterFailedStart) disposes synchronously.
        Configure(Printer(recordPrints: false));
        _service.Start("mk4");
        FakeRecording recording = await RecordingAsync(1);

        _service.Dispose();

        await Eventually.TrueAsync(() => recording.IsEnded, "recording stopped");
        Assert.Equal(0, _monitor.SubscriberCount);
        Assert.False(_service.Start("mk4"));
    }

    [Fact]
    public async Task WithoutAMuxerRecordingIsUnavailable()
    {
        _recorder.Availability = CameraRecorderAvailability.Unavailable("No MP4 muxer.");
        Configure(Printer());

        Assert.False(_service.Availability.IsAvailable);
        _service.Start("mk4");

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        CameraRecordingError error = Assert.Single(_failed);
        Assert.Equal(CameraRecordingProblem.Unavailable, error.Problem);
        Assert.Equal("No MP4 muxer.", error.Message);
        Assert.Equal(CameraRecordingMode.Manual, error.Mode);
    }

    [Theory]
    [InlineData(CameraRecordingFailure.DiskFull, CameraRecordingProblem.DiskFull)]
    [InlineData(CameraRecordingFailure.WriteFailed, CameraRecordingProblem.WriteFailed)]
    [InlineData(CameraRecordingFailure.TooSlow, CameraRecordingProblem.TooSlow)]
    [InlineData(CameraRecordingFailure.StreamFailed, CameraRecordingProblem.StreamFailed)]
    [InlineData(CameraRecordingFailure.CameraUnavailable, CameraRecordingProblem.CameraUnavailable)]
    public async Task AFailedRecordingIsReportedAndNotRestarted(CameraRecordingFailure failure, CameraRecordingProblem problem)
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        recording.End(failure, "Broken (test).");

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        CameraRecordingError error = Assert.Single(_failed);
        Assert.Equal(problem, error.Problem);
        Assert.Equal("Broken (test).", error.Message);
        Assert.Equal("mk4", error.PrinterId);
        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        Assert.Equal(recording.Path, _service.GetStatus("mk4").LastFile);
        Raise(PrinterState.Printing);
        await SettleAsync();
        Assert.Single(_recorder.Started);
    }

    [Fact]
    public async Task TheNextGoodRecordingClearsTheLastError()
    {
        Configure(Printer(recordPrints: false));
        _service.Start("mk4");
        FakeRecording first = await RecordingAsync(1);
        first.End(CameraRecordingFailure.TooSlow);
        await Eventually.TrueAsync(() => _service.GetStatus("mk4").LastError is not null, "error kept");

        _service.Start("mk4");
        await RecordingAsync(2);

        Assert.Null(_service.GetStatus("mk4").LastError);
    }

    [Fact]
    public async Task SwitchingTheCameraOffStopsTheRecordingWithoutAnError()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        FakeRecording recording = await RecordingAsync(1);

        // The hub ends the lease of a switched-off camera at the same moment.
        Configure(Printer(camera: false));
        recording.End(CameraRecordingFailure.CameraUnavailable);

        await StatusAsync("mk4", CameraRecordingActivity.Idle);
        await SettleAsync();
        Assert.Empty(_failed);
        Assert.False(_service.Start("mk4"));
    }

    [Fact]
    public async Task RemovingThePrinterStopsItsRecording()
    {
        Configure(Printer(), Printer("xl", "XL", address: "192.168.0.11"));
        _service.Start("mk4");
        FakeRecording recording = await RecordingAsync(1);

        Configure(Printer("xl", "XL", address: "192.168.0.11"));

        await Eventually.TrueAsync(() => recording.IsEnded, "recording stopped");
        Assert.Same(CameraRecordingStatus.None, _service.GetStatus("mk4"));
        await SettleAsync();
        Assert.Empty(_failed);
        Raise(PrinterState.Printing);
        await SettleAsync();
        Assert.Single(_recorder.Started);
    }

    [Fact]
    public async Task AnotherMachineUnderTheSameIdStartsOver()
    {
        Configure(Printer());
        _service.Start("mk4");
        FakeRecording recording = await RecordingAsync(1);

        Configure(Printer(address: "192.168.0.99"));

        await Eventually.TrueAsync(() => recording.IsEnded, "recording stopped");
        Assert.Equal(CameraRecordingActivity.Idle, _service.GetStatus("mk4").Activity);
    }

    [Fact]
    public async Task PrintersWithTheSameNameRecordIntoTheirOwnFolders()
    {
        Configure(Printer("a", "MK4"), Printer("b", "MK4", address: "192.168.0.11"));

        _service.Start("a");
        _service.Start("b");
        await Eventually.TrueAsync(() => _recorder.Started.Length == 2, "both started");

        string[] folders = [.. _recorder.Started.Select(static r => Path.GetFileName(Path.GetDirectoryName(r.Path)!)).Order(StringComparer.Ordinal)];
        Assert.Equal(2, folders.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(folders, static folder => Assert.StartsWith("MK4 (", folder, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThePrinterFolderIsTheOneItRecordsInto()
    {
        string other = Path.Combine(_root, "other");
        Configure(Printer("a", "MK4"), Printer("b", "MK4", address: "192.168.0.11"));
        Assert.Null(_service.GetPrinterFolder("unknown"));
        Assert.NotEqual(_service.GetPrinterFolder("a"), _service.GetPrinterFolder("b"));
        Assert.Equal(_root, Path.GetDirectoryName(_service.GetPrinterFolder("A")));

        _service.Start("a");
        FakeRecording recording = await RecordingAsync(1);
        Assert.Equal(_service.GetPrinterFolder("a"), Path.GetDirectoryName(recording.Path));

        // A new root applies at once (to the next recording, and to "Open recordings folder").
        _service.Configure([Printer("a", "MK4"), Printer("b", "MK4", address: "192.168.0.11")], new CameraRecordingSettings { Folder = other });
        Assert.Equal(other, Path.GetDirectoryName(_service.GetPrinterFolder("a")));
    }

    [Fact]
    public async Task ANewFolderAppliesToTheNextRecording()
    {
        string other = Path.Combine(_root, "other");
        Configure(Printer(recordPrints: false));
        _service.Start("mk4");
        FakeRecording first = await RecordingAsync(1);

        _service.Configure([Printer(recordPrints: false)], new CameraRecordingSettings { Folder = other });
        await SettleAsync();
        Assert.False(first.IsEnded);
        Assert.Equal(other, _service.RecordingsFolder);
        _service.Stop("mk4");
        await StatusAsync("mk4", CameraRecordingActivity.Idle);

        _service.Start("mk4");
        FakeRecording second = await RecordingAsync(2);

        Assert.StartsWith(_root + Path.DirectorySeparatorChar + "Workshop MK4", first.Path, StringComparison.Ordinal);
        Assert.StartsWith(other + Path.DirectorySeparatorChar, second.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRotationRunsAtStartAndAfterEveryRecording()
    {
        Configure(Printer(recordPrints: false));
        await Eventually.TrueAsync(() => _storage.FindCalls == 1, "rotation at start");
        Configure(Printer(recordPrints: false));
        await SettleAsync();
        Assert.Equal(1, _storage.FindCalls);

        _service.Start("mk4");
        await RecordingAsync(1);
        _service.Stop("mk4");

        await Eventually.TrueAsync(() => _storage.FindCalls == 2, "rotation after the recording");
    }

    [Fact]
    public async Task TheLastRecordingOfAnEarlierRunIsKnown()
    {
        string folder = Path.Combine(_root, "Workshop MK4");
        CameraRecordingStorage.Instance.PrepareFolder(folder);
        string older = Path.Combine(folder, "2026-11-01_10-00-00_A.mp4");
        string newer = Path.Combine(folder, "2026-11-02_10-00-00_B.mp4");
        await File.WriteAllBytesAsync(older, [1], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(newer, [1], TestContext.Current.CancellationToken);

        Configure(Printer());

        await Eventually.TrueAsync(() => _service.GetStatus("mk4").LastFile == newer, "newest recording found");
    }

    [Fact]
    public async Task QuittingStopsEveryRecordingAndWaitsForThem()
    {
        Configure(Printer(), Printer("xl", "XL", address: "192.168.0.11"));
        Raise(PrinterState.Printing);
        FakeRecording automatic = await RecordingAsync(1);
        _service.Start("xl");
        FakeRecording manual = await RecordingAsync(2);

        await _service.StopAllAsync().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(automatic.IsEnded);
        Assert.True(manual.IsEnded);
        Assert.Equal(0, _time.ActiveTimers);
        Assert.False(_service.Start("mk4"));
        Raise(PrinterState.Printing, "xl");
        await SettleAsync();
        Assert.Equal(2, _recorder.Started.Length);
        Assert.Empty(_failed);
        Assert.Same(_service.StopAllAsync(), _service.StopAllAsync());
    }

    [Fact]
    public async Task ARecordingThatIsStillStartingWhenQuittingIsStoppedToo()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        await Eventually.TrueAsync(() => _recorder.Started.Length == 1, "started");

        // No key frame yet.
        await _service.StopAllAsync().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(_recorder.Started[0].IsEnded);
    }

    [Fact]
    public async Task DisposingUnsubscribesFromTheMonitor()
    {
        Assert.Equal(1, _monitor.SubscriberCount);

        await _service.DisposeAsync();

        Assert.Equal(0, _monitor.SubscriberCount);
    }

    [Fact]
    public async Task AFailingSubscriberIsLoggedAndDoesNotStopTheOthers()
    {
        _service.Changed += static (_, _) => throw new InvalidOperationException("subscriber failed");
        _service.Failed += static (_, _) => throw new InvalidOperationException("subscriber failed");
        _recorder.Availability = CameraRecorderAvailability.Unavailable("No MP4 muxer.");
        Configure(Printer());

        _service.Start("mk4");

        await Eventually.TrueAsync(() => !_failed.IsEmpty, "error reported");
        Assert.Contains("mk4", _changed);
        Assert.Contains(_logger.Entries, static e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task SnapshotsBeforeConfigureAreIgnored()
    {
        Raise(PrinterState.Printing);
        await SettleAsync();

        Assert.Empty(_recorder.Started);
        Assert.Same(CameraRecordingStatus.None, _service.GetStatus("mk4"));
    }

    [Fact]
    public async Task LogsNeverContainTheFolder()
    {
        Configure(Printer());
        Raise(PrinterState.Printing);
        await RecordingAsync(1);
        _service.Stop("mk4");
        await StatusAsync("mk4", CameraRecordingActivity.Idle);

        Assert.NotEmpty(_logger.Entries);
        Assert.DoesNotContain(_logger.Entries, e => e.Message.Contains(_root, StringComparison.Ordinal));
    }
}
