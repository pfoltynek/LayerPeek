using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>T-165: the JPEG export of a finished timelapse, over a scripted decoder and a fake encoder, in a temporary folder.</summary>
public sealed class CameraTimelapseJpegExporterTests : IDisposable
{
    private const string TimelapseName = "2026-11-04_14-05-09_Benchy_timelapse.mp4";
    private const string ExportName = "2026-11-04_14-05-09_Benchy_timelapse_jpg";

    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly string _timelapse;
    private readonly ScriptedVideoFiles _files = new();
    private readonly FakeJpegSequenceEncoders _encoders = new();
    private readonly RecordingFileGuard _guard = new();
    private readonly CameraTimelapseJpegExporter _exporter;
    private readonly List<CameraTimelapseExportResult> _results = [];
    private readonly List<CameraTimelapseExportStatus> _statuses = [];
    private readonly TaskCompletionSource<CameraTimelapseExportResult> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CameraTimelapseJpegExporterTests()
    {
        Directory.CreateDirectory(_folder);
        _timelapse = Path.Combine(_folder, TimelapseName);
        File.WriteAllBytes(_timelapse, [1, 2, 3]);
        _exporter = new CameraTimelapseJpegExporter(_files, _encoders, NullLogger<CameraTimelapseJpegExporter>.Instance, _guard);
        _exporter.Changed += (_, _) =>
        {
            lock (_statuses)
            {
                _statuses.Add(_exporter.Status);
            }
        };
        _exporter.Completed += (_, result) =>
        {
            lock (_results)
            {
                _results.Add(result);
            }

            // The status is idle again when Completed comes.
            Assert.False(_exporter.Status.IsRunning);
            _completed.TrySetResult(result);
        };
    }

    public void Dispose()
    {
        _exporter.Dispose();
        TestDirectories.Delete(_folder);
    }

    private async Task<CameraTimelapseExportResult> ExportAsync(string? file = null, string? printerId = "mk4")
    {
        Assert.True(_exporter.Start(file ?? _timelapse, printerId));
        return await _completed.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
    }

    private string[] Entries() => [.. Directory.EnumerateFileSystemEntries(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    [Fact]
    public async Task EveryFrameBecomesANumberedJpegInOrderAndInItsSizeNextToTheTimelapse()
    {
        _files.Frames = 12;
        _files.Width = 1280;
        _files.Height = 720;

        CameraTimelapseExportResult result = await ExportAsync();

        Assert.True(result.Succeeded, result.Problem.ToString());
        string output = Path.Combine(_folder, ExportName);
        Assert.Equal(output, result.OutputFolder);
        Assert.Equal(12, result.Frames);
        Assert.Equal("mk4", result.PrinterId);
        Assert.Equal(_timelapse, result.SourceFile);
        string[] frames = [.. Directory.GetFiles(output).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
        Assert.Equal([.. Enumerable.Range(1, 12).Select(static n => $"{n:0000}.jpg")], frames);
        for (int n = 1; n <= 12; n++)
        {
            Assert.Equal((1280, 720, n), FakeJpegSequenceEncoders.Read(Path.Combine(output, $"{n:0000}.jpg")));
        }

        // Only the timelapse and the export; no temporary folder is left.
        Assert.Equal([TimelapseName, ExportName], Entries());
        Assert.Equal(CameraTimelapseJpegExporter.DefaultQuality, _encoders.LastQuality);
        Assert.Equal(1, _encoders.Created);
        Assert.Equal(1, _encoders.Disposed);
        // One count pass, one decoding pass, both on the file's own address.
        Assert.Equal(2, _files.CreateCount);
        Assert.Equal(_timelapse, _files.LastUri!.LocalPath);
        Assert.Equal(CameraTimelapseExportStatus.Idle, _exporter.Status);
        Assert.Null(_exporter.Running);
    }

    [Fact]
    public async Task ProgressGoesUpOncePerPercentWithTheTotalCountedFirst()
    {
        _files.Frames = 400;

        await ExportAsync();

        CameraTimelapseExportStatus[] statuses;
        lock (_statuses)
        {
            statuses = [.. _statuses];
        }

        // Started (frames not counted yet), counted, then one per whole percent, then idle.
        Assert.True(statuses[0].IsRunning);
        Assert.Equal(0, statuses[0].FramesTotal);
        Assert.Equal("mk4", statuses[0].PrinterId);
        Assert.Equal(_timelapse, statuses[0].SourceFile);
        Assert.Equal(400, statuses[1].FramesTotal);
        Assert.Equal(0, statuses[1].Percent);
        int[] percents = [.. statuses.Skip(2).SkipLast(1).Select(static s => s.Percent)];
        Assert.Equal([.. Enumerable.Range(1, 100)], percents);
        Assert.False(statuses[^1].IsRunning);
    }

    [Fact]
    public async Task ManyFramesGetMoreDigitsSoThatTheNamesStillSort()
    {
        _files.Frames = 10_001;
        _files.Width = 1;
        _files.Height = 1;

        CameraTimelapseExportResult result = await ExportAsync();

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(result.OutputFolder!, "00001.jpg")));
        Assert.True(File.Exists(Path.Combine(result.OutputFolder!, "10001.jpg")));
    }

    [Fact]
    public async Task ASecondExportOfTheSameTimelapseGetsAFolderOfItsOwn()
    {
        string earlier = Directory.CreateDirectory(Path.Combine(_folder, ExportName)).FullName;
        File.WriteAllBytes(Path.Combine(earlier, "0001.jpg"), [9]);

        CameraTimelapseExportResult result = await ExportAsync();

        Assert.Equal(Path.Combine(_folder, ExportName + "-2"), result.OutputFolder);
        Assert.Equal([9], File.ReadAllBytes(Path.Combine(earlier, "0001.jpg")));
        Assert.True(CameraRecordingFolders.IsJpegExportFolderName(Path.GetFileName(result.OutputFolder)));
    }

    [Fact]
    public async Task OnlyOneExportRunsAtATime()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _files.Hold = (2, gate.Task);
        string other = Path.Combine(_folder, "2026-11-05_08-00-00_timelapse.mp4");
        File.WriteAllBytes(other, [1]);

        Assert.True(_exporter.Start(_timelapse, "mk4"));
        Assert.False(_exporter.Start(other, "xl"));
        Assert.True(_exporter.Status.IsRunning);
        Assert.Equal(_timelapse, _exporter.Status.SourceFile);
        gate.SetResult();
        CameraTimelapseExportResult result = await _completed.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Single(_results);
        Assert.False(Directory.Exists(Path.Combine(_folder, "2026-11-05_08-00-00_timelapse_jpg")));
        // Afterwards the next one can start.
        Assert.True(_exporter.Start(other, "xl"));
        await _exporter.Running!.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CancellingLeavesNoFolderAndNoHalfWrittenFile()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _files.Frames = 10;
        _files.Hold = (4, gate.Task);
        Assert.True(_exporter.Start(_timelapse, "mk4"));
        // Three frames are written into the temporary folder, the fourth waits.
        string partial = Path.Combine(_folder, CameraRecordingFolders.UnfinishedJpegExportFolderName(ExportName));
        await WaitUntilAsync(() => _exporter.Status.FramesDone == 3);
        Assert.Equal(3, Directory.GetFiles(partial).Length);
        Assert.Contains(partial, _guard.Held);
        Assert.Contains(_timelapse, _guard.Held);

        _exporter.Cancel();
        CameraTimelapseExportResult result = await _completed.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(CameraTimelapseExportProblem.Cancelled, result.Problem);
        Assert.Null(result.OutputFolder);
        Assert.Equal([TimelapseName], Entries());
        Assert.Empty(_guard.Held);
        Assert.Equal(1, _encoders.Disposed);
    }

    [Fact]
    public async Task DisposingCancelsAndWaitsForTheCleanUp()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _files.Hold = (2, gate.Task);
        Assert.True(_exporter.Start(_timelapse));
        await WaitUntilAsync(() => _exporter.Status.FramesDone == 1);

        await _exporter.DisposeAsync();

        Assert.Equal(CameraTimelapseExportProblem.Cancelled, (await _completed.Task).Problem);
        Assert.Equal([TimelapseName], Entries());
        Assert.False(_exporter.Start(_timelapse));
    }

    [Fact]
    public async Task ADamagedEndExportsTheFramesBeforeIt()
    {
        // A timelapse cut off by a crash: both passes stop at the same place.
        _files.Frames = 10;
        _files.FailAfterFrames = 6;
        _files.FailAfterPackets = 6;

        CameraTimelapseExportResult result = await ExportAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(6, result.Frames);
        Assert.Equal(6, Directory.GetFiles(result.OutputFolder!).Length);
    }

    [Fact]
    public async Task AFileWithoutFramesFailsAndLeavesNothing()
    {
        _files.Frames = 0;

        CameraTimelapseExportResult result = await ExportAsync();

        Assert.Equal(CameraTimelapseExportProblem.Unreadable, result.Problem);
        Assert.Equal([TimelapseName], Entries());
        Assert.Empty(_guard.Held);
    }

    [Fact]
    public async Task AFileTheDecoderCannotReadFailsAndLeavesNothing()
    {
        _files.FailAfterPackets = 0;
        _files.FailAfterFrames = 0;

        CameraTimelapseExportResult result = await ExportAsync();

        Assert.Equal(CameraTimelapseExportProblem.Unreadable, result.Problem);
        Assert.Equal([TimelapseName], Entries());
    }

    [Fact]
    public async Task OnlyTimelapsesAreExported()
    {
        string recording = Path.Combine(_folder, "2026-11-04_14-05-09_Benchy.mp4");
        File.WriteAllBytes(recording, [1]);

        CameraTimelapseExportResult result = await ExportAsync(recording);

        Assert.Equal(CameraTimelapseExportProblem.NotATimelapse, result.Problem);
        Assert.Equal(0, _files.CreateCount);
    }

    [Fact]
    public async Task AMissingFileIsUnreadable()
    {
        CameraTimelapseExportResult result = await ExportAsync(Path.Combine(_folder, "2026-11-01_08-00-00_timelapse.mp4"));

        Assert.Equal(CameraTimelapseExportProblem.Unreadable, result.Problem);
        Assert.Equal(0, _files.CreateCount);
    }

    [Fact]
    public async Task WithoutTheDecoderTheExportIsUnavailable()
    {
        _files.CreateFailure = new CameraFeedException("FFmpeg is not available.");

        CameraTimelapseExportResult result = await ExportAsync();

        Assert.Equal(CameraTimelapseExportProblem.Unavailable, result.Problem);
        Assert.Equal([TimelapseName], Entries());
    }

    [Fact]
    public async Task AFailingEncoderEndsTheExportWithoutAFolder()
    {
        _encoders.FailAtFrame = 3;

        CameraTimelapseExportResult result = await ExportAsync();

        Assert.Equal(CameraTimelapseExportProblem.Failed, result.Problem);
        Assert.Equal(2, result.Frames);
        Assert.Equal([TimelapseName], Entries());
        Assert.Equal(1, _encoders.Disposed);
    }

    [Fact]
    public async Task AFolderThatCannotBeWrittenFailsTheExport()
    {
        // A file where the temporary folder would go.
        File.WriteAllBytes(Path.Combine(_folder, CameraRecordingFolders.UnfinishedJpegExportFolderName(ExportName)), [1]);

        CameraTimelapseExportResult result = await ExportAsync();

        Assert.Equal(CameraTimelapseExportProblem.WriteFailed, result.Problem);
        Assert.False(Directory.Exists(Path.Combine(_folder, ExportName)));
    }

    [Fact]
    public async Task ATemporaryFolderLeftByACrashIsReplaced()
    {
        string partial = Directory.CreateDirectory(Path.Combine(_folder, CameraRecordingFolders.UnfinishedJpegExportFolderName(ExportName))).FullName;
        File.WriteAllBytes(Path.Combine(partial, "0001.jpg"), [9, 9]);
        File.WriteAllBytes(Path.Combine(partial, "0099.jpg"), [9, 9]);

        CameraTimelapseExportResult result = await ExportAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(5, Directory.GetFiles(result.OutputFolder!).Length);
        Assert.False(Directory.Exists(partial));
    }

    [Fact]
    public async Task ASubscriberThatThrowsDoesNotBreakTheExport()
    {
        _exporter.Changed += static (_, _) => throw new InvalidOperationException("subscriber");
        _exporter.Completed += static (_, _) => throw new InvalidOperationException("subscriber");

        CameraTimelapseExportResult result = await ExportAsync();

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task TheFramesAllocateLittleBeyondTheEncoder()
    {
        // The scripted feed and the fake encoder allocate nothing per frame; what is left is the export's own steady state:
        // the frame's file name and path and the file handle (the JPEG buffer, the decoder buffer and the encoder are reused).
        _files.Frames = 300;
        _files.Width = 64;
        _files.Height = 36;

        await ExportAsync();

        IReadOnlyList<long> calls = _encoders.AllocatedAtCalls;
        long[] perFrame = [.. calls.Skip(100).Zip(calls.Skip(101), static (a, b) => b - a)];
        long median = perFrame.Order().ElementAt(perFrame.Length / 2);
        TestContext.Current.TestOutputHelper?.WriteLine($"{median} B per frame (median).");
        Assert.InRange(median, 0, 1024);
    }

    [Theory]
    [InlineData("2026-11-04_14-05-09_Benchy #2_timelapse.mp4")]
    [InlineData("2026-11-04_14-05-09_50%20off_timelapse.mp4")]
    [InlineData("2026-11-04_14-05-09_Kostka ž_timelapse.mp4")]
    public async Task FileNamesWithSpecialCharactersReachTheDecoderUnchanged(string name)
    {
        string file = Path.Combine(_folder, name);
        File.WriteAllBytes(file, [1]);

        CameraTimelapseExportResult result = await ExportAsync(file);

        Assert.True(result.Succeeded);
        Assert.Equal(file, _files.LastUri!.LocalPath);
        Assert.Equal(Path.Combine(_folder, CameraRecordingFolders.JpegExportFolderName(name)), result.OutputFolder);
    }

    [Theory]
    [InlineData("Benchy #2", "a.mp4")]
    [InlineData("50%20off", "a.mp4")]
    [InlineData("Tiskárna #1 %20", "a.mp4")]
    [InlineData("MK4", "Benchy #2 %41 ž.mp4")]
    [InlineData("100% #", "x%2541#.mp4")]
    public void TheFileAddressGivesThePathBackAlsoWithAHashAndAnEscapeSequence(string folderName, string name)
    {
        string path = Path.Combine(_folder, folderName, name);

        Uri? uri = CameraTimelapseJpegExporter.FileUri(path);

        Assert.NotNull(uri);
        Assert.True(uri.IsFile);
        Assert.Equal(path, uri.LocalPath);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TestTimeouts.Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
