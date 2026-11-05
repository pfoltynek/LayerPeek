using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Tests.Cameras.Recording;
using LayerPeek.Core.Tests.Monitoring;
using LayerPeek.Tests.Allocation;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>
/// T-164: the names of a timelapse from a recording, and how the storage and the rotation treat it and its temporary file
/// (in a temporary folder with real files): the output counts like any timelapse, the file being written never counts and
/// goes only as an old leftover, the recording being read is held.
/// </summary>
public sealed class TimelapseFromRecordingFolderTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = TestDirectories.NewDirectoryPath();
    private readonly string _printer;

    public TimelapseFromRecordingFolderTests()
    {
        _printer = Path.Combine(_root, "MK4");
        CameraRecordingStorage.Instance.PrepareFolder(_printer);
    }

    public void Dispose() => TestDirectories.Delete(_root);

    private string Write(string name, int length, DateTime written)
    {
        string path = Path.Combine(_printer, name);
        File.WriteAllBytes(path, new byte[length]);
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    [Fact]
    public void TheTimelapseIsNamedAfterTheRecordingAndIsATimelapseForTheOtherFeatures()
    {
        string name = CameraRecordingFolders.RecordingTimelapseFileName("2026-11-04_14-05-09_Benchy");

        Assert.Equal("2026-11-04_14-05-09_Benchy_recording_timelapse.mp4", name);
        // "Play last timelapse", the JPEG export (T-165) and the rotation know it.
        Assert.True(CameraRecordingFolders.IsTimelapseFileName(name));
        Assert.True(CameraRecordingFolders.IsTimelapseFileName("2026-11-04_14-05-09_Benchy_recording_timelapse-2.mp4"));
        Assert.Equal("2026-11-04_14-05-09_Benchy_recording_timelapse_jpg", CameraRecordingFolders.JpegExportFolderName(name));
        // Never the name of the live timelapse of the same print.
        Assert.NotEqual(CameraRecordingFolders.TimelapseFileName(new DateTimeOffset(2026, 11, 4, 14, 5, 9, TimeSpan.Zero), "Benchy.bgcode"), name);
        Assert.Equal(".2026-11-04_14-05-09_Benchy_recording_timelapse.mp4.partial", CameraRecordingFolders.UnfinishedFileName(name));
    }

    [Theory]
    [InlineData(".2026-11-04_14-05-09_Benchy_recording_timelapse.mp4.partial", true)]
    [InlineData(".2026-11-04_14-05-09_Benchy_recording_timelapse-2.mp4.PARTIAL", true)]
    [InlineData("2026-11-04_14-05-09_Benchy_recording_timelapse.mp4.partial", false)]
    [InlineData(".2026-11-04_14-05-09_Benchy.mp4.partial", false)]
    [InlineData(".2026-11-04_14-05-09_timelapse_jpg.partial", false)]
    [InlineData(".partial", false)]
    [InlineData(null, false)]
    public void UnfinishedTimelapsesAreRecognized(string? name, bool expected) =>
        Assert.Equal(expected, CameraRecordingFolders.IsUnfinishedTimelapseFileName(name));

    [Fact]
    public void TheTimelapseCountsAndGoesOldestFirstButTheFileBeingWrittenOnlyGoesAsAnOldLeftover()
    {
        string recording = Write("2026-11-02_08-00-00_Benchy.mp4", 100, Now.AddDays(-2));
        string timelapse = Write("2026-11-02_08-00-00_Benchy_recording_timelapse.mp4", 100, Now.AddHours(-1));
        string newer = Write("2026-11-03_08-00-00.mp4", 100, Now.AddDays(-1));
        string running = Write(".2026-11-03_08-00-00_recording_timelapse.mp4.partial", 500, Now.AddMinutes(-1));
        string leftover = Write(".2026-11-01_08-00-00_recording_timelapse.mp4.partial", 500, Now - CameraRecordingRotation.EmptyLeftoverAge);

        CameraRecordingFile[] found = [.. CameraRecordingStorage.Instance.FindRecordings(_root).OrderBy(static f => f.Path, StringComparer.Ordinal)];
        Assert.Equal([leftover, running], found.Where(static f => f.IsUnfinished).Select(static f => f.Path));
        CameraRecordingRotationResult result = CameraRecordingRotation.Run(CameraRecordingStorage.Instance, _root, 150, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, Now);

        // 300 B over 150 B: the recording and its timelapse go (sorted right after it), the unfinished files never count.
        Assert.Equal([leftover, recording, timelapse], result.Deleted);
        Assert.True(File.Exists(running));
        Assert.True(File.Exists(newer));
        Assert.Equal(100, result.StoredBytes);
        Assert.Equal(newer, result.NewestByFolder["MK4"]);
    }

    [Fact]
    public void TheNewestTimelapseOfAFolderMayBeOneFromARecording()
    {
        Write("2026-11-02_08-00-00_timelapse.mp4", 100, Now.AddDays(-2));
        string fromRecording = Write("2026-11-03_08-00-00_Benchy_recording_timelapse.mp4", 100, Now.AddHours(-1));

        CameraRecordingRotationResult result = CameraRecordingRotation.Run(CameraRecordingStorage.Instance, _root, long.MaxValue, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, Now);

        Assert.Equal(fromRecording, result.NewestTimelapseByFolder["MK4"]);
    }

    [Fact]
    public async Task TheRecordingBeingReadIsNotRotatedAwayAndTheTimelapseCountsAfterwards()
    {
        string recording = Write("2026-11-02_08-00-00_Benchy.mp4", 100, Now.AddDays(-2));
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedRecordings files = new();
        files.Add(recording, new RecordingScript { Hold = (25 * 31, gate.Task) }, fileBytes: 100);
        // The recording looks 2 GB large, over the smallest limit of 1 GB: the rotation wants it gone.
        CountingStorage storage = new(recording, 2_000_000_000);
        await using CameraRecordingService service = new(
            new SnapshotMonitor(),
            new FakeCameraRecorder(),
            storage,
            TimeProvider.System,
            NullLogger<CameraRecordingService>.Instance,
            new CameraRecordingServiceOptions { DefaultRoot = _root });
        await using CameraTimelapseFromRecordingMaker maker = new(files, new FakeRecordingWriterFactory(), NullLogger<CameraTimelapseFromRecordingMaker>.Instance, service);
        TaskCompletionSource<CameraTimelapseFromRecordingResult> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        maker.Completed += (_, result) => completed.TrySetResult(result);
        Assert.True(maker.Start(recording, TimeSpan.FromSeconds(30)));
        await Eventually.TrueAsync(() => maker.Status.Frames == 2, "two frames written");

        service.Configure([], new CameraRecordingSettings { SizeLimitGigabytes = CameraRecordingSettings.MinSizeLimitGigabytes });
        await Eventually.TrueAsync(() => storage.Finds >= 1, "a rotation");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        // Held while it is read; the file being written is neither counted nor deleted.
        Assert.True(File.Exists(recording));
        Assert.True(File.Exists(Path.Combine(_printer, ".2026-11-02_08-00-00_Benchy_recording_timelapse.mp4.partial")));

        gate.SetResult();
        CameraTimelapseFromRecordingResult made = await completed.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        // Released: the next rotation runs at once, counts the new timelapse and deletes the oversized recording.
        Assert.True(made.Succeeded, made.Problem.ToString());
        await Eventually.TrueAsync(() => !File.Exists(recording), "the recording rotated away after the job");
        Assert.True(File.Exists(made.OutputFiles[0]));
        Assert.Contains(storage.LastFound, f => f.Path == made.OutputFiles[0] && !f.IsUnfinished);
    }

    /// <summary>The real file system, except that one file looks much larger; counts the rotations.</summary>
    private sealed class CountingStorage(string big, long length) : ICameraRecordingStorage
    {
        private int _finds;
        private IReadOnlyList<CameraRecordingFile> _last = [];

        public int Finds => Volatile.Read(ref _finds);

        public IReadOnlyList<CameraRecordingFile> LastFound => Volatile.Read(ref _last);

        public void PrepareFolder(string folder) => CameraRecordingStorage.Instance.PrepareFolder(folder);

        public long? GetAvailableFreeSpace(string folder) => null;

        public IReadOnlyList<CameraRecordingFile> FindRecordings(string root)
        {
            CameraRecordingFile[] found = [.. CameraRecordingStorage.Instance.FindRecordings(root).Select(f => f.Path == big ? f with { Length = length } : f)];
            Volatile.Write(ref _last, found);
            Interlocked.Increment(ref _finds);
            return found;
        }

        public bool TryDelete(string path) => CameraRecordingStorage.Instance.TryDelete(path);
    }
}
