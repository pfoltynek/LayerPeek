using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Tests.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>
/// T-164: a timelapse from a finished recording, over a scripted demuxer (packets only) and a fake writer, in a temporary
/// folder: which key frames are taken, the timestamps, segments, damaged files, cancelling, names, holds and failures.
/// </summary>
public sealed class CameraTimelapseFromRecordingMakerTests : IDisposable
{
    private const string RecordingName = "2026-11-04_14-05-09_Benchy.mp4";
    private const string OutputName = "2026-11-04_14-05-09_Benchy_recording_timelapse.mp4";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly ScriptedRecordings _files = new();
    private readonly RecordingFileGuard _guard = new();
    private readonly List<CameraTimelapseFromRecordingStatus> _statuses = [];

    public CameraTimelapseFromRecordingMakerTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => TestDirectories.Delete(_folder);

    private string Path_(string name) => Path.Combine(_folder, name);

    private string[] Entries() => [.. Directory.EnumerateFileSystemEntries(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    private CameraTimelapseFromRecordingMaker Maker(FakeRecordingWriterFactory writers, ILogger<CameraTimelapseFromRecordingMaker>? logger = null)
    {
        CameraTimelapseFromRecordingMaker maker = new(_files, writers, logger ?? NullLogger<CameraTimelapseFromRecordingMaker>.Instance, _guard);
        maker.Changed += (_, _) =>
        {
            lock (_statuses)
            {
                _statuses.Add(maker.Status);
            }
        };
        return maker;
    }

    private static async Task<CameraTimelapseFromRecordingResult> MakeAsync(CameraTimelapseFromRecordingMaker maker, string file, TimeSpan? interval = null, string? printerId = "mk4")
    {
        TaskCompletionSource<CameraTimelapseFromRecordingResult> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        maker.Completed += (_, result) =>
        {
            // The status is idle again when Completed comes.
            Assert.False(maker.Status.IsRunning);
            completed.TrySetResult(result);
        };
        Assert.True(maker.Start(file, interval ?? Interval, printerId));
        return await completed.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheFirstKeyFrameOfEveryIntervalIsWrittenAt30FpsUnchangedNextToTheRecording()
    {
        // 2 min at 25 fps, a key frame every 2 s (like the Buddy3D's ~2.2 s).
        string recording = _files.Add(Path_(RecordingName), new RecordingScript { Frames = 25 * 120 });
        FakeRecordingWriterFactory writers = new();
        using CameraTimelapseFromRecordingMaker maker = Maker(writers);

        CameraTimelapseFromRecordingResult result = await MakeAsync(maker, recording);

        Assert.True(result.Succeeded, result.Problem.ToString());
        Assert.Equal((4, "mk4", recording), (result.Frames, result.PrinterId, result.SourceFile));
        Assert.Equal([Path_(OutputName)], result.OutputFiles);
        Assert.Equal([RecordingName, OutputName], Entries());
        FakeRecordingWriter writer = Assert.Single(writers.Writers);
        Assert.True(writer.IsCompleted);
        Assert.True(writer.IsDisposed);
        Assert.Equal(RecordingScript.DefaultInfo, writer.Info);
        // The key frames at 0, 30, 60 and 90 s (key frames 0, 15, 30, 45), as they are, one frame of 30 fps each.
        Assert.Equal(
            [
                new WrittenPacket(0, 16, 0, 0, 3000, true),
                new WrittenPacket(15, 16, 3000, 3000, 3000, true),
                new WrittenPacket(30, 16, 6000, 6000, 3000, true),
                new WrittenPacket(45, 16, 9000, 9000, 3000, true),
            ],
            writer.Packets);
        Assert.Equal(Enumerable.Repeat((byte)0, 16), writer.FirstKeyFrame);
        Assert.Empty(_guard.Held);
    }

    [Fact]
    public async Task KeyFramesSparserThanTheIntervalGiveOneFrameEachAndNeverTwoForOneStep()
    {
        // A key frame every 40 s, every 30 s wanted: 0, 40, 80, 120 s (the effective interval is the key frame spacing).
        string recording = _files.Add(Path_(RecordingName), new RecordingScript { Frames = 25 * 130, KeyFrameEvery = 25 * 40 });
        FakeRecordingWriterFactory writers = new();
        using CameraTimelapseFromRecordingMaker maker = Maker(writers);

        CameraTimelapseFromRecordingResult result = await MakeAsync(maker, recording);

        Assert.Equal(4, result.Frames);
        Assert.Equal([0, 1, 2, 3], Assert.Single(writers.Writers).Packets.Select(static p => (int)p.Fill));
    }

    [Fact]
    public async Task TheStepsFollowTheRecordingsOwnTimeFromItsFirstPacket()
    {
        // Timestamps that do not start at 0, 5 s between the steps.
        string recording = _files.Add(Path_(RecordingName), new RecordingScript { Frames = 25 * 20, FirstDts = 123_456_789 });
        FakeRecordingWriterFactory writers = new();
        using CameraTimelapseFromRecordingMaker maker = Maker(writers);

        CameraTimelapseFromRecordingResult result = await MakeAsync(maker, recording, TimeSpan.FromSeconds(5));

        // 0, 6 (first key frame at or after 5 s), 10, 16 s.
        Assert.Equal([0, 3, 5, 8], Assert.Single(writers.Writers).Packets.Select(static p => (int)p.Fill));
        Assert.Equal(4, result.Frames);
    }

    [Fact]
    public async Task TheSegmentsOfARecordingAreOneWholeAndOtherStreamParametersStartTheNextFile()
    {
        // Three segments of 60 s (T-144: -2, -3 after outages); the third one has other parameters (another resolution).
        CameraStreamInfo other = new(CameraVideoCodec.H264, 1920, 1080, CameraTimeBase.Rtp90kHz, new byte[] { 1, 0x64, 0, 0x28 });
        _files.Add(Path_("2026-11-04_14-05-09_Benchy.mp4"), new RecordingScript { FirstFill = 0 });
        string second = _files.Add(Path_("2026-11-04_14-05-09_Benchy-2.mp4"), new RecordingScript { FirstFill = 100, FirstDts = 900_000 });
        _files.Add(Path_("2026-11-04_14-05-09_Benchy-3.mp4"), new RecordingScript { FirstFill = 200, Info = other });
        // Neither another recording nor a timelapse of it.
        _files.Add(Path_("2026-11-04_14-05-09_Benchy_timelapse.mp4"), new RecordingScript());
        _files.Add(Path_("2026-11-04_15-00-00_Benchy.mp4"), new RecordingScript());
        FakeRecordingWriterFactory writers = new();
        using CameraTimelapseFromRecordingMaker maker = Maker(writers);

        // Any segment may be chosen.
        CameraTimelapseFromRecordingResult result = await MakeAsync(maker, second, TimeSpan.FromSeconds(25));

        Assert.True(result.Succeeded, result.Problem.ToString());
        Assert.Equal(
            [Path_("2026-11-04_14-05-09_Benchy.mp4"), Path_("2026-11-04_14-05-09_Benchy-2.mp4"), Path_("2026-11-04_14-05-09_Benchy-3.mp4")],
            _files.Opened);
        // Recording time 0–180 s, every 25 s: 0, 26, 50 | 76, 100 (segment 2 from 60 s) | 126, 150, 176 (segment 3).
        Assert.Equal(8, result.Frames);
        Assert.Equal([Path_("2026-11-04_14-05-09_Benchy_recording_timelapse.mp4"), Path_("2026-11-04_14-05-09_Benchy_recording_timelapse-2.mp4")], result.OutputFiles);
        FakeRecordingWriter[] files = writers.Writers;
        Assert.Equal(2, files.Length);
        Assert.Equal([0, 13, 25, 108, 120], files[0].Packets.Select(static p => (int)p.Fill));
        Assert.Equal([0L, 3000, 6000, 9000, 12000], files[0].Packets.Select(static p => p.Dts));
        Assert.Equal([203, 215, 228], files[1].Packets.Select(static p => (int)p.Fill));
        // Every file starts at 0.
        Assert.Equal([0L, 3000, 6000], files[1].Packets.Select(static p => p.Dts));
        Assert.Equal(other, files[1].Info);
        Assert.All(files, static f => Assert.True(f.IsCompleted));
        Assert.DoesNotContain(Entries(), static e => e.StartsWith('.'));
    }

    [Theory]
    [InlineData("2026-11-04_14-05-09_Benchy.mp4", "2026-11-04_14-05-09_Benchy")]
    [InlineData("2026-11-04_14-05-09_Benchy-2.mp4", "2026-11-04_14-05-09_Benchy")]
    [InlineData("2026-11-04_14-05-09_Benchy-4.mp4", "2026-11-04_14-05-09_Benchy")]
    public void TheSegmentsAreFoundFromAnyOfThemAndInOrderAlsoWithAGap(string chosen, string stem)
    {
        foreach (string name in (string[])["2026-11-04_14-05-09_Benchy-4.mp4", "2026-11-04_14-05-09_Benchy.mp4", "2026-11-04_14-05-09_Benchy-2.mp4", "2026-11-04_14-05-09_Benchy-10.mp4", "2026-11-04_14-05-09_Benchy-x.mp4", "2026-11-04_14-05-09_Benchy_timelapse.mp4", "2026-11-04_14-05-09_Benchy-2_timelapse.mp4"])
        {
            File.WriteAllBytes(Path_(name), [1]);
        }

        (string found, IReadOnlyList<string> files) = CameraTimelapseFromRecordingMaker.FindSegments(Path_(chosen));

        Assert.Equal(stem, found);
        Assert.Equal(
            [Path_("2026-11-04_14-05-09_Benchy.mp4"), Path_("2026-11-04_14-05-09_Benchy-2.mp4"), Path_("2026-11-04_14-05-09_Benchy-4.mp4"), Path_("2026-11-04_14-05-09_Benchy-10.mp4")],
            files);
    }

    [Fact]
    public void ATitleThatEndsWithANumberKeepsItsOwnSegments()
    {
        // A print titled "Part-2" with an outage: its segments are Part-2-2 …, not those of "Part".
        foreach (string name in (string[])["2026-11-04_14-05-09_Part-2.mp4", "2026-11-04_14-05-09_Part-2-2.mp4"])
        {
            File.WriteAllBytes(Path_(name), [1]);
        }

        AssertSegments("2026-11-04_14-05-09_Part-2", [Path_("2026-11-04_14-05-09_Part-2.mp4"), Path_("2026-11-04_14-05-09_Part-2-2.mp4")], Path_("2026-11-04_14-05-09_Part-2.mp4"));
        // Without an earlier part, a lone "-2" file is a segment whose first file is gone (the output is named without it).
        File.WriteAllBytes(Path_("2026-11-04_16-00-00-3.mp4"), [1]);
        AssertSegments("2026-11-04_16-00-00", [Path_("2026-11-04_16-00-00-3.mp4")], Path_("2026-11-04_16-00-00-3.mp4"));

        // The seconds of an untitled recording are no segment number: recordings two seconds apart stay apart.
        File.WriteAllBytes(Path_("2026-11-04_17-00-05.mp4"), [1]);
        File.WriteAllBytes(Path_("2026-11-04_17-00-07.mp4"), [1]);
        AssertSegments("2026-11-04_17-00-05", [Path_("2026-11-04_17-00-05.mp4")], Path_("2026-11-04_17-00-05.mp4"));

        static void AssertSegments(string stem, string[] files, string chosen)
        {
            (string foundStem, IReadOnlyList<string> found) = CameraTimelapseFromRecordingMaker.FindSegments(chosen);
            Assert.Equal(stem, foundStem);
            Assert.Equal(files, found);
        }
    }

    [Fact]
    public async Task ARecordingCutOffByACrashGivesTheFramesBeforeTheDamageAndTheNextSegmentGoesOn()
    {
        // The first segment breaks after 70 s (a crash leftover), the second one is whole.
        _files.Add(Path_(RecordingName), new RecordingScript { Frames = 25 * 120, FailAfterPackets = 25 * 70 });
        _files.Add(Path_("2026-11-04_14-05-09_Benchy-2.mp4"), new RecordingScript { FirstFill = 100 });
        FakeRecordingWriterFactory writers = new();
        ListLogger<CameraTimelapseFromRecordingMaker> logger = new();
        using CameraTimelapseFromRecordingMaker maker = Maker(writers, logger);

        CameraTimelapseFromRecordingResult result = await MakeAsync(maker, Path_(RecordingName));

        Assert.True(result.Succeeded, result.Problem.ToString());
        // 0, 30, 60 s; the second segment from 70 s: 90, 120 s.
        Assert.Equal([0, 15, 30, 110, 125], Assert.Single(writers.Writers).Packets.Select(static p => (int)p.Fill));
        Assert.Contains(logger.Entries, static e => e.Level == LogLevel.Warning && e.Message.Contains("damaged", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, static e => e.Message.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFileThatIsNoVideoOrHasNoKeyFrameFailsWithoutLeftovers()
    {
        string garbage = Path_(RecordingName);
        File.WriteAllBytes(garbage, [1, 2, 3]);
        FakeRecordingWriterFactory writers = new();
        using (CameraTimelapseFromRecordingMaker maker = Maker(writers))
        {
            Assert.Equal(CameraTimelapseExportProblem.Unreadable, (await MakeAsync(maker, garbage)).Problem);
        }

        string noKeys = _files.Add(Path_("2026-11-04_15-00-00.mp4"), new RecordingScript { FailAfterPackets = 0 });
        string noTimes = _files.Add(Path_("2026-11-04_16-00-00.mp4"), new RecordingScript { NoTimestamps = true });
        using (CameraTimelapseFromRecordingMaker maker = Maker(writers))
        {
            Assert.Equal(CameraTimelapseExportProblem.Unreadable, (await MakeAsync(maker, noKeys)).Problem);
        }

        using (CameraTimelapseFromRecordingMaker maker = Maker(writers))
        {
            Assert.Equal(CameraTimelapseExportProblem.Unreadable, (await MakeAsync(maker, noTimes)).Problem);
        }

        Assert.Empty(writers.Writers);
        Assert.Equal([RecordingName, "2026-11-04_15-00-00.mp4", "2026-11-04_16-00-00.mp4"], Entries());
    }

    [Theory]
    [InlineData("2026-11-04_14-05-09_Benchy_timelapse.mp4")]
    [InlineData("2026-11-04_14-05-09_Benchy_recording_timelapse-2.mp4")]
    [InlineData("holiday.mp4")]
    [InlineData(".2026-11-04_14-05-09_Benchy_recording_timelapse.mp4.partial")]
    [InlineData("2026-11-04_14-05-09_alarm-bed-change.jpg")]
    public async Task AFileThatIsNoRecordingIsRefusedWithoutReadingIt(string name)
    {
        string file = _files.Add(Path_(name), new RecordingScript());
        using CameraTimelapseFromRecordingMaker maker = Maker(new FakeRecordingWriterFactory());

        CameraTimelapseFromRecordingResult result = await MakeAsync(maker, file);

        Assert.Equal((CameraTimelapseExportProblem.NotARecording, 0), (result.Problem, result.Frames));
        Assert.Empty(result.OutputFiles);
        Assert.Empty(_files.Opened);
        Assert.Equal(0, _guard.Holds);
    }

    [Fact]
    public async Task AMissingFileOrAMissingLibraryFailsCleanly()
    {
        using (CameraTimelapseFromRecordingMaker maker = Maker(new FakeRecordingWriterFactory()))
        {
            Assert.Equal(CameraTimelapseExportProblem.Unreadable, (await MakeAsync(maker, Path_(RecordingName))).Problem);
        }

        string recording = _files.Add(Path_(RecordingName), new RecordingScript());
        using (CameraTimelapseFromRecordingMaker maker = Maker(new FakeRecordingWriterFactory { Availability = CameraRecorderAvailability.Unavailable("No muxer.") }))
        {
            Assert.False(maker.Availability.IsAvailable);
            Assert.Equal(CameraTimelapseExportProblem.Unavailable, (await MakeAsync(maker, recording)).Problem);
        }

        _files.CreateFailure = new CameraFeedException("The video library (FFmpeg) is not available.");
        using (CameraTimelapseFromRecordingMaker maker = Maker(new FakeRecordingWriterFactory()))
        {
            Assert.Equal(CameraTimelapseExportProblem.Unavailable, (await MakeAsync(maker, recording)).Problem);
        }

        Assert.Equal([RecordingName], Entries());
        Assert.Empty(_guard.Held);
    }

    [Fact]
    public async Task CancellingLeavesNoFileAndOneJobRunsAtATime()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        string recording = _files.Add(Path_(RecordingName), new RecordingScript { Frames = 25 * 120, Hold = (25 * 61, gate.Task) }, fileBytes: 25 * 120 * 16);
        FakeRecordingWriterFactory writers = new();
        using CameraTimelapseFromRecordingMaker maker = Maker(writers);
        TaskCompletionSource<CameraTimelapseFromRecordingResult> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        maker.Completed += (_, result) => completed.TrySetResult(result);

        Assert.True(maker.Start(recording, Interval, "mk4"));
        await Eventually.TrueAsync(() => maker.Status.Frames == 3, "three frames written");
        // The temporary file and the recording are held from the rotation meanwhile; nothing has its final name yet.
        string temporary = Path_("." + OutputName + ".partial");
        Assert.True(File.Exists(temporary));
        Assert.Contains(temporary, _guard.Held);
        Assert.Contains(recording, _guard.Held);
        Assert.DoesNotContain(OutputName, Entries());
        CameraTimelapseFromRecordingStatus running = maker.Status;
        Assert.Equal((true, "mk4", recording), (running.IsRunning, running.PrinterId, running.SourceFile));
        Assert.InRange(running.Percent, 45, 55);
        Assert.False(maker.Start(recording, Interval));

        maker.Cancel();
        CameraTimelapseFromRecordingResult cancelled = await completed.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal((CameraTimelapseExportProblem.Cancelled, 3), (cancelled.Problem, cancelled.Frames));
        Assert.Empty(cancelled.OutputFiles);
        Assert.Equal([RecordingName], Entries());
        Assert.True(Assert.Single(writers.Writers).IsDisposed);
        Assert.Empty(_guard.Held);
        Assert.Equal(CameraTimelapseFromRecordingStatus.Idle, maker.Status);
        Assert.Null(maker.Running);
        // The next job may start.
        gate.SetResult();
        Assert.True((await MakeAsync(maker, recording)).Succeeded);
    }

    [Fact]
    public async Task DisposingWaitsForTheCleanUpAndRefusesNewJobs()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        string recording = _files.Add(Path_(RecordingName), new RecordingScript { Frames = 25 * 120, Hold = (25 * 40, gate.Task) });
        CameraTimelapseFromRecordingMaker maker = Maker(new FakeRecordingWriterFactory());
        Assert.True(maker.Start(recording, Interval));
        await Eventually.TrueAsync(() => maker.Status.Frames == 2, "two frames written");

        await maker.DisposeAsync();

        Assert.Equal([RecordingName], Entries());
        Assert.False(maker.Start(recording, Interval));
        maker.Dispose();
    }

    [Fact]
    public async Task ATakenNameGetsTheNextNumberAndALeftoverOfACrashIsReplaced()
    {
        string recording = _files.Add(Path_(RecordingName), new RecordingScript());
        File.WriteAllBytes(Path_(OutputName), [42]);
        File.WriteAllBytes(Path_("." + OutputName + ".partial"), [1, 2, 3]);
        using CameraTimelapseFromRecordingMaker maker = Maker(new FakeRecordingWriterFactory());

        CameraTimelapseFromRecordingResult result = await MakeAsync(maker, recording);

        Assert.Equal([Path_("2026-11-04_14-05-09_Benchy_recording_timelapse-2.mp4")], result.OutputFiles);
        Assert.Equal([42], File.ReadAllBytes(Path_(OutputName)));
        Assert.Equal([RecordingName, "2026-11-04_14-05-09_Benchy_recording_timelapse-2.mp4", OutputName], Entries());
    }

    [Theory]
    [InlineData(CameraRecordingFailure.DiskFull, CameraTimelapseExportProblem.DiskFull)]
    [InlineData(CameraRecordingFailure.WriteFailed, CameraTimelapseExportProblem.WriteFailed)]
    [InlineData(CameraRecordingFailure.StreamFailed, CameraTimelapseExportProblem.Unreadable)]
    public async Task AFailingWriterLeavesNoFile(CameraRecordingFailure failure, CameraTimelapseExportProblem problem)
    {
        string recording = _files.Add(Path_(RecordingName), new RecordingScript { Frames = 25 * 120 });
        FakeRecordingWriterFactory writers = new() { WriteFailure = (3, new CameraRecordingException(failure, "Writing the recording failed.")) };
        using CameraTimelapseFromRecordingMaker maker = Maker(writers);

        CameraTimelapseFromRecordingResult result = await MakeAsync(maker, recording);

        Assert.Equal((problem, 2), (result.Problem, result.Frames));
        Assert.Equal([RecordingName], Entries());
        Assert.Empty(_guard.Held);
    }

    [Fact]
    public async Task AFailureToCompleteOrToCreateLeavesNoFile()
    {
        string recording = _files.Add(Path_(RecordingName), new RecordingScript());
        using (CameraTimelapseFromRecordingMaker maker = Maker(new FakeRecordingWriterFactory { CompleteFailure = new CameraRecordingException(CameraRecordingFailure.DiskFull, "Completing the recording failed.") }))
        {
            Assert.Equal(CameraTimelapseExportProblem.DiskFull, (await MakeAsync(maker, recording)).Problem);
        }

        using (CameraTimelapseFromRecordingMaker maker = Maker(new FakeRecordingWriterFactory { CreateFailure = new CameraRecordingException(CameraRecordingFailure.WriteFailed, "Creating the recording file failed.") }))
        {
            Assert.Equal(CameraTimelapseExportProblem.WriteFailed, (await MakeAsync(maker, recording)).Problem);
        }

        Assert.Equal([RecordingName], Entries());
    }

    [Fact]
    public async Task AnUnwritableFolderFailsAsWriteFailed()
    {
        if (OperatingSystem.IsWindows())
        {
            // Unix permissions; Windows is covered by the failing writer.
            return;
        }

        string recording = _files.Add(Path_(RecordingName), new RecordingScript());
        File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            using CameraTimelapseFromRecordingMaker maker = Maker(new FakeRecordingWriterFactory());
            Assert.Equal(CameraTimelapseExportProblem.WriteFailed, (await MakeAsync(maker, recording)).Problem);
        }
        finally
        {
            File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Assert.Equal([RecordingName], Entries());
    }

    [Fact]
    public async Task ProgressIsAnnouncedOncePerPercentAndAFailingSubscriberIsOnlyLogged()
    {
        string recording = _files.Add(Path_(RecordingName), new RecordingScript { Frames = 25 * 600, PacketLength = 100 }, fileBytes: 25 * 600 * 100);
        ListLogger<CameraTimelapseFromRecordingMaker> logger = new();
        using CameraTimelapseFromRecordingMaker maker = Maker(new FakeRecordingWriterFactory(), logger);
        maker.Changed += static (_, _) => throw new InvalidOperationException("A subscriber failed.");

        CameraTimelapseFromRecordingResult result = await MakeAsync(maker, recording);

        Assert.True(result.Succeeded);
        CameraTimelapseFromRecordingStatus[] statuses;
        lock (_statuses)
        {
            statuses = [.. _statuses];
        }

        // Started, 1 … 99 %, ended: never more often, and in order.
        Assert.Equal(101, statuses.Length);
        Assert.Equal([.. Enumerable.Range(0, 100)], statuses[..^1].Select(static s => s.Percent));
        Assert.All(statuses[..^1], static s => Assert.True(s.IsRunning));
        Assert.Equal(CameraTimelapseFromRecordingStatus.Idle, statuses[^1]);
        Assert.Contains(logger.Entries, static e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Theory]
    [InlineData("Tiskárna #1", "2026-11-04_14-05-09_Benchy #2.mp4")]
    [InlineData("MK4", "2026-11-04_14-05-09_50%20off.mp4")]
    [InlineData("MK4", "2026-11-04_14-05-09_Kostka ž.mp4")]
    [InlineData("MK4", "2026-11-04_14-05-09_50%.mp4")]
    [InlineData("Tiskárna #1 %20", "2026-11-04_14-05-09_Benchy #2 %41.mp4")]
    public async Task AFileWithSpecialCharactersInItsPathIsRead(string folderName, string name)
    {
        string folder = Path.Combine(_folder, folderName);
        string recording = _files.Add(Path.Combine(folder, name), new RecordingScript());
        using CameraTimelapseFromRecordingMaker maker = Maker(new FakeRecordingWriterFactory());

        CameraTimelapseFromRecordingResult result = await MakeAsync(maker, recording);

        Assert.True(result.Succeeded, result.Problem.ToString());
        Assert.Equal([recording], _files.Opened);
        Assert.Equal([Path.Combine(folder, CameraRecordingFolders.RecordingTimelapseFileName(Path.GetFileNameWithoutExtension(name)))], result.OutputFiles);
    }

    [Fact]
    public void StartChecksItsArguments()
    {
        using CameraTimelapseFromRecordingMaker maker = Maker(new FakeRecordingWriterFactory());

        Assert.Throws<ArgumentOutOfRangeException>(() => maker.Start(Path_(RecordingName), TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => maker.Start(" ", Interval));
        Assert.Equal(CameraTimelapseFromRecordingStatus.Idle, maker.Status);
    }
}
