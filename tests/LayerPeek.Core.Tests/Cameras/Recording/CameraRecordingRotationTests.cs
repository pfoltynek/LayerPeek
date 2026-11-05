using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Tests.Cameras.Recording;

/// <summary>T-145: the rotation deletes only LayerPeek's oldest recordings, in a temporary folder with real files.</summary>
public sealed class CameraRecordingRotationTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly HashSet<string> NoActive = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _root = TestDirectories.NewDirectoryPath();
    private readonly string _printer;

    public CameraRecordingRotationTests()
    {
        _printer = Path.Combine(_root, "MK4");
        CameraRecordingStorage.Instance.PrepareFolder(_printer);
    }

    public void Dispose() => TestDirectories.Delete(_root);

    private static string Write(string folder, string name, int length, DateTime? written = null)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[length]);
        File.SetLastWriteTimeUtc(path, written ?? Now.AddHours(-1));
        return path;
    }

    private static CameraRecordingRotationResult Rotate(string root, long limit, IReadOnlySet<string>? active = null, long activeBytes = 0) =>
        CameraRecordingRotation.Run(CameraRecordingStorage.Instance, root, limit, active ?? NoActive, activeBytes, Now);

    [Fact]
    public void TheOldestRecordingsGoUntilTheRestIsWithinTheLimit()
    {
        string oldest = Write(_printer, "2026-11-01_08-00-00_A.mp4", 100);
        string older = Write(_printer, "2026-11-01_09-00-00_B.mp4", 100);
        string newer = Write(_printer, "2026-11-02_08-00-00_C.mp4", 100);
        string newest = Write(_printer, "2026-11-03_08-00-00.mp4", 100);

        CameraRecordingRotationResult result = Rotate(_root, limit: 250);

        Assert.Equal([oldest, older], result.Deleted);
        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(older));
        Assert.True(File.Exists(newer));
        Assert.True(File.Exists(newest));
        Assert.Equal(200, result.StoredBytes);
        Assert.False(result.OverLimit);
        Assert.Equal(newest, result.NewestByFolder["MK4"]);
    }

    [Fact]
    public void TimelapsesCountAndGoOldestFirstLikeRecordingsAndAreToldApart()
    {
        string oldTimelapse = Write(_printer, "2026-11-01_08-00-00_Benchy_timelapse.mp4", 100);
        string recording = Write(_printer, "2026-11-01_09-00-00_Benchy.mp4", 100);
        string timelapse = Write(_printer, "2026-11-02_08-00-00_Cube_timelapse.mp4", 100);
        string nextFile = Write(_printer, "2026-11-02_08-00-00_Cube_timelapse-2.mp4", 100, Now.AddMinutes(-30));
        string foreign = Write(_printer, "timelapse.mp4", 1000);

        CameraRecordingRotationResult result = Rotate(_root, limit: 300);

        Assert.Equal([oldTimelapse], result.Deleted);
        Assert.True(File.Exists(foreign));
        Assert.Equal(300, result.StoredBytes);
        Assert.Equal(recording, result.NewestByFolder["MK4"]);
        Assert.Equal(nextFile, result.NewestTimelapseByFolder["MK4"]);
        Assert.True(File.Exists(timelapse));
    }

    [Fact]
    public void TheOldestAcrossAllPrintersGoesFirst()
    {
        string xl = Path.Combine(_root, "XL");
        CameraRecordingStorage.Instance.PrepareFolder(xl);
        string mk4 = Write(_printer, "2026-11-02_08-00-00.mp4", 100);
        string oldXl = Write(xl, "2026-11-01_08-00-00.mp4", 100);

        CameraRecordingRotationResult result = Rotate(_root, limit: 150);

        Assert.Equal([oldXl], result.Deleted);
        Assert.True(File.Exists(mk4));
        Assert.False(result.NewestByFolder.ContainsKey("XL"));
    }

    [Fact]
    public void ForeignFilesAreNeverDeleted()
    {
        string ours = Write(_printer, "2026-11-01_08-00-00.mp4", 100);
        string[] foreign =
        [
            Write(_printer, "holiday.mp4", 100),
            Write(_printer, "VID_20261101_080000.mp4", 100),
            Write(_printer, "2026-11-01_08-00-00.mkv", 100),
            Write(_printer, "2026-13-01_08-00-00.mp4", 100),
            Write(_printer, "2026-11-01_08-00-00x.mp4", 100),
            Write(_printer, "notes.txt", 100),
            // Outside the printer folders: the root itself, a folder without the marker, a deeper level.
            Write(_root, "2026-10-01_08-00-00.mp4", 100),
            Write(Path.Combine(_root, "Videos"), "2026-10-01_08-00-00.mp4", 100),
            Write(Path.Combine(_printer, "deeper"), "2026-10-01_08-00-00.mp4", 100),
        ];

        CameraRecordingRotationResult result = Rotate(_root, limit: 1);

        Assert.Equal([ours], result.Deleted);
        Assert.All(foreign, static path => Assert.True(File.Exists(path), path));
        Assert.True(File.Exists(Path.Combine(_printer, CameraRecordingFolders.MarkerFileName)));
        Assert.Equal(0, result.StoredBytes);
        Assert.False(result.OverLimit);
    }

    [Fact]
    public void ARunningRecordingIsNeverDeletedButCounts()
    {
        string old = Write(_printer, "2026-11-01_08-00-00.mp4", 100);
        string running = Write(_printer, "2026-10-01_08-00-00.mp4", 50);
        HashSet<string> active = new([running], StringComparer.OrdinalIgnoreCase);

        CameraRecordingRotationResult result = Rotate(_root, limit: 120, active, activeBytes: 60);

        Assert.Equal([old], result.Deleted);
        Assert.True(File.Exists(running));
        Assert.False(result.OverLimit);

        // The running recording alone above the limit: nothing left to delete.
        result = Rotate(_root, limit: 50, active, activeBytes: 60);
        Assert.Empty(result.Deleted);
        Assert.True(result.OverLimit);
        Assert.True(File.Exists(running));
    }

    [Fact]
    public void EmptyLeftoversOfACrashGoOnceTheyAreOld()
    {
        string leftover = Write(_printer, "2026-11-01_08-00-00.mp4", 0, Now - CameraRecordingRotation.EmptyLeftoverAge);
        string starting = Write(_printer, "2026-11-03_11-59-58.mp4", 0, Now.AddSeconds(-2));
        string kept = Write(_printer, "2026-11-02_08-00-00.mp4", 10);

        CameraRecordingRotationResult result = Rotate(_root, limit: 1_000);

        Assert.Equal([leftover], result.Deleted);
        Assert.True(File.Exists(starting), "A recording that is just starting has an empty file for a moment.");
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void SegmentsOfTheSameStartGoInTheOrderTheyWereWritten()
    {
        string first = Write(_printer, "2026-11-01_08-00-00_Benchy.mp4", 100, Now.AddHours(-3));
        string second = Write(_printer, "2026-11-01_08-00-00_Benchy-2.mp4", 100, Now.AddHours(-2));

        CameraRecordingRotationResult result = Rotate(_root, limit: 150);

        Assert.Equal([first], result.Deleted);
        Assert.True(File.Exists(second));
    }

    [Fact]
    public void AMissingRootHasNoRecordings()
    {
        CameraRecordingRotationResult result = Rotate(Path.Combine(_root, "missing"), limit: 1);

        Assert.Empty(result.Deleted);
        Assert.Equal(0, result.StoredBytes);
        Assert.Empty(result.NewestByFolder);
    }

    [Fact]
    public void AFileThatCannotBeDeletedIsSkipped()
    {
        string oldest = Write(_printer, "2026-11-01_08-00-00.mp4", 100);
        string next = Write(_printer, "2026-11-02_08-00-00.mp4", 100);
        string newest = Write(_printer, "2026-11-03_08-00-00.mp4", 100);
        StubbornStorage storage = new(oldest);

        CameraRecordingRotationResult result = CameraRecordingRotation.Run(storage, _root, 250, NoActive, 0, Now);

        Assert.Equal([next], result.Deleted);
        Assert.Equal(1, result.FailedDeletes);
        Assert.True(File.Exists(oldest));
        Assert.True(File.Exists(newest));
        Assert.Equal(200, result.StoredBytes);
        Assert.False(result.OverLimit);
    }

    [Fact]
    public void FurtherSegmentsOfARunningRecordingAreNeverDeleted()
    {
        // The recorder creates name-2.mp4 a moment before the recording lists it (a new connection).
        string running = Path.Combine(_printer, "2026-11-03_08-00-00_A.mp4");
        string segment = Write(_printer, "2026-11-03_08-00-00_A-2.mp4", 100);
        string other = Write(_printer, "2026-11-03_07-00-00-2.mp4", 100);

        CameraRecordingRotationResult result = Rotate(_root, limit: 0, new HashSet<string>([running], StringComparer.OrdinalIgnoreCase));

        Assert.Equal([other], result.Deleted);
        Assert.True(File.Exists(segment));
    }

    [Fact]
    public void LinksAreNeverFollowed()
    {
        // A marked folder outside the root, linked into the root as a printer folder and as a file of a printer folder.
        string outside = TestDirectories.NewDirectoryPath();
        try
        {
            CameraRecordingStorage.Instance.PrepareFolder(outside);
            string foreign = Write(outside, "2026-11-01_08-00-00.mp4", 100);
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(_root, "Linked"), outside);
                File.CreateSymbolicLink(Path.Combine(_printer, "2026-11-01_09-00-00.mp4"), foreign);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("Creating links needs the developer mode on Windows.");
            }

            CameraRecordingRotationResult result = Rotate(_root, limit: 0);

            Assert.Empty(result.Deleted);
            Assert.True(File.Exists(foreign));
            Assert.Equal(0, result.StoredBytes);
        }
        finally
        {
            TestDirectories.Delete(outside);
        }
    }

    [Fact]
    public void AnExistingFolderOfTheUserIsNeverMarked()
    {
        // A custom root that already holds a folder with the printer's name (e.g. the home folder and "Documents").
        string users = Path.Combine(_root, "Documents");
        string video = Write(users, "2026-11-01_08-00-00.mp4", 100);

        Assert.Throws<ForeignRecordingFolderException>(() => CameraRecordingStorage.Instance.PrepareFolder(users));

        Assert.False(File.Exists(Path.Combine(users, CameraRecordingFolders.MarkerFileName)));
        Assert.Empty(Rotate(_root, limit: 0).Deleted);
        Assert.True(File.Exists(video));
    }

    [Fact]
    public void AnExistingEmptyFolderIsMarked()
    {
        // Only system metadata (Finder, Explorer) in it: nothing of the user's to lose.
        string empty = Path.Combine(_root, "Empty");
        Directory.CreateDirectory(empty);
        File.WriteAllBytes(Path.Combine(empty, ".DS_Store"), [1]);

        CameraRecordingStorage.Instance.PrepareFolder(empty);

        Assert.True(File.Exists(Path.Combine(empty, CameraRecordingFolders.MarkerFileName)));
    }

    [Fact]
    public void PreparingAFolderWritesTheMarkerOnce()
    {
        string marker = Path.Combine(_printer, CameraRecordingFolders.MarkerFileName);
        // On Windows the marker is hidden, and a hidden file cannot be overwritten by File.WriteAllText.
        File.SetAttributes(marker, FileAttributes.Normal);
        File.WriteAllText(marker, "kept");

        CameraRecordingStorage.Instance.PrepareFolder(_printer);
        string fresh = Path.Combine(_root, "a", "b");
        CameraRecordingStorage.Instance.PrepareFolder(fresh);

        Assert.Equal("kept", File.ReadAllText(marker));
        Assert.Contains("LayerPeek", File.ReadAllText(Path.Combine(fresh, CameraRecordingFolders.MarkerFileName)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreparingANewFolderFromSeveralWritersAtOnceNeverTakesItForAForeignOne()
    {
        // T-160: a recording and a timelapse that start with the same print prepare the same new folder together; each
        // writes its file right after (which must not make the other one see a folder of the user's).
        for (int round = 0; round < 50; round++)
        {
            string folder = Path.Combine(_root, $"new-{round}");
            Task[] writers = [.. Enumerable.Range(0, 4).Select(i => Task.Run(() =>
            {
                CameraRecordingStorage.Instance.PrepareFolder(folder);
                File.WriteAllBytes(Path.Combine(folder, $"2026-11-04_08-00-0{i}.mp4"), [1]);
            }, TestContext.Current.CancellationToken))];

            await Task.WhenAll(writers);
        }
    }

    [Fact]
    public void TheFreeSpaceOfAnExistingOrMissingFolderIsKnown()
    {
        Assert.True(CameraRecordingStorage.Instance.GetAvailableFreeSpace(_printer) > 0);
        Assert.True(CameraRecordingStorage.Instance.GetAvailableFreeSpace(Path.Combine(_root, "not", "yet")) > 0);
    }

    /// <summary>The real file system, except that one file cannot be deleted (a player has it open).</summary>
    private sealed class StubbornStorage(string stuck) : ICameraRecordingStorage
    {
        public void PrepareFolder(string folder) => CameraRecordingStorage.Instance.PrepareFolder(folder);

        public long? GetAvailableFreeSpace(string folder) => null;

        public IReadOnlyList<CameraRecordingFile> FindRecordings(string root) => CameraRecordingStorage.Instance.FindRecordings(root);

        public bool TryDelete(string path) => path != stuck && CameraRecordingStorage.Instance.TryDelete(path);
    }
}
