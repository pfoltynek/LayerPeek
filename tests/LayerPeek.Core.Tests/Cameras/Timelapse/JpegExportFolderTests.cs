using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>
/// T-165: the names of a JPEG export of a timelapse, and how the storage and the rotation count and delete an export folder as
/// a whole (in a temporary folder with real files).
/// </summary>
public sealed class JpegExportFolderTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly HashSet<string> NoActive = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _root = TestDirectories.NewDirectoryPath();
    private readonly string _printer;

    public JpegExportFolderTests()
    {
        _printer = Path.Combine(_root, "MK4");
        CameraRecordingStorage.Instance.PrepareFolder(_printer);
    }

    public void Dispose() => TestDirectories.Delete(_root);

    [Fact]
    public void TheExportFolderIsNamedAfterTheTimelapse()
    {
        Assert.Equal("2026-11-04_14-05-09_Benchy_timelapse_jpg", CameraRecordingFolders.JpegExportFolderName("2026-11-04_14-05-09_Benchy_timelapse.mp4"));
        Assert.Equal("2026-11-04_14-05-09_timelapse-2_jpg", CameraRecordingFolders.JpegExportFolderName("2026-11-04_14-05-09_timelapse-2.mp4"));
        Assert.Equal(".2026-11-04_14-05-09_timelapse_jpg.partial", CameraRecordingFolders.UnfinishedJpegExportFolderName("2026-11-04_14-05-09_timelapse_jpg"));
    }

    [Theory]
    [InlineData("2026-11-04_14-05-09_Benchy_timelapse_jpg", true)]
    [InlineData("2026-11-04_14-05-09_Benchy_timelapse_JPG", true)]
    [InlineData("2026-11-04_14-05-09_Benchy_timelapse_jpg-2", true)]
    [InlineData("2026-11-04_14-05-09_jpg", true)]
    [InlineData("2026-11-04_14-05-09_Benchy_timelapse_jpg-", false)]
    [InlineData("2026-11-04_14-05-09_Benchy_timelapse_jpg-x", false)]
    [InlineData("2026-11-04_14-05-09_Benchy_timelapse", false)]
    [InlineData("2026-13-04_14-05-09_Benchy_timelapse_jpg", false)]
    [InlineData("Holiday_jpg", false)]
    [InlineData("_jpg", false)]
    [InlineData(".2026-11-04_14-05-09_timelapse_jpg.partial", false)]
    [InlineData(null, false)]
    public void ExportFoldersAreRecognized(string? name, bool expected) =>
        Assert.Equal(expected, CameraRecordingFolders.IsJpegExportFolderName(name));

    [Theory]
    [InlineData(".2026-11-04_14-05-09_timelapse_jpg.partial", true)]
    [InlineData(".2026-11-04_14-05-09_timelapse_jpg-3.partial", true)]
    [InlineData("2026-11-04_14-05-09_timelapse_jpg.partial", false)]
    [InlineData(".2026-11-04_14-05-09_timelapse.partial", false)]
    [InlineData(".partial", false)]
    [InlineData(null, false)]
    public void UnfinishedExportFoldersAreRecognized(string? name, bool expected) =>
        Assert.Equal(expected, CameraRecordingFolders.IsUnfinishedJpegExportFolderName(name));

    [Theory]
    [InlineData(1, 5, "0001.jpg")]
    [InlineData(42, 1200, "0042.jpg")]
    [InlineData(9999, 9999, "9999.jpg")]
    [InlineData(7, 10_000, "00007.jpg")]
    [InlineData(123_456, 100, "123456.jpg")]
    public void FramesAreNumberedSoThatTheySort(int number, int frames, string expected)
    {
        Assert.Equal(expected, CameraRecordingFolders.JpegExportFrameFileName(number, frames));
        Assert.True(CameraRecordingFolders.IsJpegExportFrameFileName(expected));
    }

    [Theory]
    [InlineData("0001.JPG", true)]
    [InlineData("notes.jpg", false)]
    [InlineData("0001.jpeg", false)]
    [InlineData(".jpg", false)]
    [InlineData("2026-11-04_14-05-09_alarm-x.jpg", false)]
    public void OnlyNumberedJpegsAreFrames(string name, bool expected) =>
        Assert.Equal(expected, CameraRecordingFolders.IsJpegExportFrameFileName(name));

    private string Export(string name, int frames, int frameBytes, DateTime written, string? parent = null)
    {
        string folder = Directory.CreateDirectory(Path.Combine(parent ?? _printer, name)).FullName;
        for (int n = 1; n <= frames; n++)
        {
            string file = Path.Combine(folder, CameraRecordingFolders.JpegExportFrameFileName(n, frames));
            File.WriteAllBytes(file, new byte[frameBytes]);
            File.SetLastWriteTimeUtc(file, written);
        }

        Directory.SetLastWriteTimeUtc(folder, written);
        return folder;
    }

    private string Write(string name, int length, DateTime written)
    {
        string path = Path.Combine(_printer, name);
        File.WriteAllBytes(path, new byte[length]);
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    private CameraRecordingRotationResult Rotate(long limit, IReadOnlySet<string>? active = null) =>
        CameraRecordingRotation.Run(CameraRecordingStorage.Instance, _root, limit, active ?? NoActive, 0, Now);

    [Fact]
    public void AnExportFolderIsFoundAsOneEntryWithTheSizeOfItsFrames()
    {
        string export = Export("2026-11-02_08-00-00_timelapse_jpg", frames: 3, frameBytes: 100, Now.AddHours(-2));
        File.WriteAllBytes(Path.Combine(export, "notes.txt"), new byte[5000]);
        Directory.CreateDirectory(Path.Combine(export, "deeper"));
        Directory.SetLastWriteTimeUtc(export, Now.AddHours(-3));
        Export("Holiday_jpg", frames: 2, frameBytes: 100, Now);
        // Not in a marked printer folder: never LayerPeek's.
        Export("2026-11-02_08-00-00_timelapse_jpg", frames: 2, frameBytes: 100, Now, Directory.CreateDirectory(Path.Combine(_root, "Other")).FullName);

        CameraRecordingFile found = Assert.Single(CameraRecordingStorage.Instance.FindRecordings(_root));

        Assert.Equal(export, found.Path);
        Assert.Equal("MK4", found.FolderName);
        Assert.Equal(300, found.Length);
        Assert.True(found.IsFolder);
        Assert.False(found.IsUnfinished);
        Assert.Equal(Now.AddHours(-2), found.LastWriteUtc, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ExportFoldersCountInTheLimitAndGoOldestFirstWithTheRecordings()
    {
        string oldTimelapse = Write("2026-11-01_08-00-00_timelapse.mp4", 100, Now.AddDays(-3));
        string oldExport = Export("2026-11-01_08-00-00_timelapse_jpg", frames: 4, frameBytes: 50, Now.AddDays(-2));
        string recording = Write("2026-11-02_08-00-00.mp4", 100, Now.AddDays(-1));
        string newExport = Export("2026-11-03_08-00-00_timelapse_jpg", frames: 2, frameBytes: 50, Now.AddHours(-1));

        CameraRecordingRotationResult result = Rotate(limit: 250);

        // 100 + 200 + 100 + 100 = 500 B: the oldest timelapse and its export go (the export after its timelapse).
        Assert.Equal([oldTimelapse, oldExport], result.Deleted);
        Assert.False(Directory.Exists(oldExport));
        Assert.True(File.Exists(recording));
        Assert.True(Directory.Exists(newExport));
        Assert.Equal(200, result.StoredBytes);
        Assert.False(result.OverLimit);
        // An export is no recording to play.
        Assert.Equal(recording, result.NewestByFolder["MK4"]);
        Assert.False(result.NewestTimelapseByFolder.ContainsKey("MK4"));
    }

    [Fact]
    public void DeletingAnExportKeepsFilesOfTheUserAndThenTheFolder()
    {
        string export = Export("2026-11-01_08-00-00_timelapse_jpg", frames: 3, frameBytes: 100, Now.AddDays(-2));
        File.WriteAllBytes(Path.Combine(export, "my-edit.psd"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(export, ".DS_Store"), [1]);

        CameraRecordingRotationResult result = Rotate(limit: 0);

        Assert.Equal([export], result.Deleted);
        Assert.Equal(["my-edit.psd"], Directory.GetFiles(export).Select(Path.GetFileName));
        Assert.Equal(0, result.StoredBytes);
    }

    [Fact]
    public void AnExportFolderWithOnlyFilesOfTheUserLeftIsNotDeletedAgainEveryRotation()
    {
        string export = Export("2026-11-01_08-00-00_timelapse_jpg", frames: 3, frameBytes: 100, Now.AddDays(-2));
        File.WriteAllBytes(Path.Combine(export, "my-edit.psd"), [1, 2, 3]);
        string leftover = Export(".2026-11-02_08-00-00_timelapse_jpg.partial", frames: 0, frameBytes: 0, Now.AddDays(-1));
        File.WriteAllBytes(Path.Combine(leftover, "notes.txt"), [1]);
        Directory.CreateDirectory(Path.Combine(export, "deeper"));

        CameraRecordingRotationResult first = Rotate(limit: 0);
        CameraRecordingRotationResult second = Rotate(limit: 0);

        // The frames went once; then nothing of LayerPeek's is left to count, delete or report as failed.
        Assert.Equal([export], first.Deleted);
        Assert.Empty(second.Deleted);
        Assert.Equal(0, second.FailedDeletes);
        Assert.Empty(CameraRecordingStorage.Instance.FindRecordings(_root));
        Assert.False(CameraRecordingStorage.Instance.TryDelete(export));
        Assert.Equal(["my-edit.psd"], Directory.GetFiles(export).Select(Path.GetFileName));
        Assert.Equal(["notes.txt"], Directory.GetFiles(leftover).Select(Path.GetFileName));
    }

    [Fact]
    public void EmptyFramesNextToFilesOfTheUserAreStillRemoved()
    {
        // A crash can leave a frame of zero bytes: it is LayerPeek's and goes, the file of the user stays.
        string export = Export(".2026-11-02_08-00-00_timelapse_jpg.partial", frames: 2, frameBytes: 0, Now.AddDays(-1));
        File.WriteAllBytes(Path.Combine(export, "notes.txt"), [1]);
        Directory.SetLastWriteTimeUtc(export, Now.AddDays(-1));

        CameraRecordingFile found = Assert.Single(CameraRecordingStorage.Instance.FindRecordings(_root));
        CameraRecordingRotationResult result = Rotate(limit: 0);

        Assert.Equal(0, found.Length);
        Assert.Equal([export], result.Deleted);
        Assert.Equal(["notes.txt"], Directory.GetFiles(export).Select(Path.GetFileName));
        Assert.Empty(CameraRecordingStorage.Instance.FindRecordings(_root));
    }

    [Fact]
    public void AnExportFolderWithOnlySystemMetadataIsStillAnEmptyLeftover()
    {
        string export = Export("2026-11-01_08-00-00_timelapse_jpg", frames: 0, frameBytes: 0, Now.AddDays(-2));
        File.WriteAllBytes(Path.Combine(export, ".DS_Store"), [1]);
        Directory.SetLastWriteTimeUtc(export, Now.AddDays(-2));

        CameraRecordingRotationResult result = Rotate(limit: long.MaxValue);

        Assert.Equal([export], result.Deleted);
        Assert.False(Directory.Exists(export));
    }

    [Fact]
    public void AnEmptiedExportFolderGoesCompletely()
    {
        string export = Export("2026-11-01_08-00-00_timelapse_jpg", frames: 3, frameBytes: 100, Now.AddDays(-2));
        File.WriteAllBytes(Path.Combine(export, ".DS_Store"), [1]);

        Rotate(limit: 0);

        Assert.False(Directory.Exists(export));
    }

    [Fact]
    public void ARunningExportIsNeitherCountedNorDeletedButAnOldLeftoverGoes()
    {
        string running = Export(".2026-11-03_08-00-00_timelapse_jpg.partial", frames: 3, frameBytes: 100, Now.AddHours(-1));
        string fresh = Export(".2026-11-03_09-00-00_timelapse_jpg.partial", frames: 3, frameBytes: 100, Now.AddMinutes(-1));
        string leftover = Export(".2026-11-02_08-00-00_timelapse_jpg.partial", frames: 3, frameBytes: 100, Now - CameraRecordingRotation.EmptyLeftoverAge);
        string timelapse = Write("2026-11-03_08-00-00_timelapse.mp4", 100, Now.AddHours(-2));
        HashSet<string> active = new([running, timelapse], StringComparer.OrdinalIgnoreCase);

        CameraRecordingRotationResult result = Rotate(limit: 0, active);

        Assert.Equal([leftover], result.Deleted);
        Assert.False(Directory.Exists(leftover));
        Assert.True(Directory.Exists(running));
        Assert.True(Directory.Exists(fresh));
        // The timelapse being exported is held, the unfinished folders never count.
        Assert.True(File.Exists(timelapse));
        Assert.Equal(0, result.StoredBytes);
    }

    [Fact]
    public void AFolderThatIsNoExportIsNeverDeleted()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_printer, "Holiday")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "0001.jpg"), [1]);

        Assert.False(CameraRecordingStorage.Instance.TryDelete(folder));
        Assert.True(File.Exists(Path.Combine(folder, "0001.jpg")));
    }

    [Fact]
    public async Task TheRecordingServiceKeepsHeldFilesUntilTheyAreReleasedAndThenRotates()
    {
        string timelapse = Write("2026-11-01_08-00-00_timelapse.mp4", 1000, Now.AddDays(-3));
        // The timelapse looks 2 GB large, over the smallest limit of 1 GB.
        BigFileStorage storage = new(timelapse, 2_000_000_000);
        await using CameraRecordingService service = new(
            new Recording.SnapshotMonitor(),
            new Recording.FakeCameraRecorder(),
            storage,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CameraRecordingService>.Instance,
            new CameraRecordingServiceOptions { DefaultRoot = _root });
        IDisposable hold = service.Hold([timelapse]);
        service.Configure([], new CameraRecordingSettings { SizeLimitGigabytes = CameraRecordingSettings.MinSizeLimitGigabytes });
        await WaitUntilAsync(() => storage.Finds >= 1);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(timelapse));

        hold.Dispose();
        hold.Dispose();
        await WaitUntilAsync(() => !File.Exists(timelapse));
        Assert.Equal(2, storage.Finds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AHeldPathInAnotherEquivalentFormIsKeptToo(bool folder)
    {
        string big = folder
            ? Export("2026-11-01_08-00-00_timelapse_jpg", frames: 2, frameBytes: 100, Now.AddDays(-3))
            : Write("2026-11-01_08-00-00_timelapse.mp4", 1000, Now.AddDays(-3));
        string name = Path.GetFileName(big);
        string sep = Path.DirectorySeparatorChar.ToString();
        // ".." and "." parts, a doubled separator, and a trailing one for the folder.
        string held = folder
            ? Path.Combine(_root, "MK4", "..", "MK4", name) + sep
            : _root + sep + "." + sep + "MK4" + sep + sep + name;
        BigFileStorage storage = new(big, 2_000_000_000);
        await using CameraRecordingService service = new(
            new Recording.SnapshotMonitor(),
            new Recording.FakeCameraRecorder(),
            storage,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CameraRecordingService>.Instance,
            new CameraRecordingServiceOptions { DefaultRoot = _root });
        IDisposable hold = service.Hold([held]);
        service.Configure([], new CameraRecordingSettings { SizeLimitGigabytes = CameraRecordingSettings.MinSizeLimitGigabytes });
        await WaitUntilAsync(() => storage.Finds >= 1);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.True(Path.Exists(big));

        hold.Dispose();
        await WaitUntilAsync(() => !Path.Exists(big));
    }

    /// <summary>The real file system, except that one file looks much larger.</summary>
    private sealed class BigFileStorage(string big, long length) : ICameraRecordingStorage
    {
        private int _finds;

        public int Finds => Volatile.Read(ref _finds);

        public void PrepareFolder(string folder) => CameraRecordingStorage.Instance.PrepareFolder(folder);

        public long? GetAvailableFreeSpace(string folder) => null;

        public IReadOnlyList<CameraRecordingFile> FindRecordings(string root)
        {
            Interlocked.Increment(ref _finds);
            return [.. CameraRecordingStorage.Instance.FindRecordings(root).Select(f => f.Path == big ? f with { Length = length } : f)];
        }

        public bool TryDelete(string path) => CameraRecordingStorage.Instance.TryDelete(path);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + Monitoring.TestTimeouts.Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
