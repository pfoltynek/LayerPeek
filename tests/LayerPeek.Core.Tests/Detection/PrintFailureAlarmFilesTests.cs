using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Detection;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Detection;

/// <summary>
/// T-153: alarm snapshots next to the recordings (marked folder, counted and rotated with them, crash leftovers removed)
/// and the false alarm records (newest kept).
/// </summary>
public sealed class PrintFailureAlarmFilesTests : IDisposable
{
    private static readonly DateTimeOffset Time = new(2026, 11, 4, 14, 5, 9, TimeSpan.FromHours(1));

    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 13, 5, 9, TimeSpan.Zero));
    private readonly PrintFailureAlarmFiles _files;

    public PrintFailureAlarmFilesTests()
    {
        _files = new PrintFailureAlarmFiles(CameraRecordingStorage.Instance, Path.Combine(_directory, "FalseAlarms"), _time, new ListLogger<PrintFailureAlarmFiles>());
    }

    private string Root => Path.Combine(_directory, "Recordings");

    public void Dispose() => TestDirectories.Delete(_directory);

    private static PrintFailureAlarm Alarm(string id, DateTimeOffset time, byte[]? jpeg = null) => new()
    {
        Id = id,
        PrinterId = "core-one",
        PrinterName = "Core One",
        DetectorId = "bed-change",
        Timestamp = time,
        Score = 0.2f,
        Threshold = 0.04f,
        Sensitivity = 50,
        Context = new PrintFailureContext(7, 3.2, 40, TimeSpan.FromMinutes(30), DetectionRegion.CoreOneBed),
        RecentScores = [0.1f, 0.2f],
        SnapshotJpeg = jpeg ?? [],
    };

    [Fact]
    public void ASnapshotGoesIntoTheMarkedPrinterFolderUnderARecordingStyleName()
    {
        string? path = _files.SaveSnapshot(Root, "Core One", Time, "bed-change", [1, 2, 3]);
        string? second = _files.SaveSnapshot(Root, "Core One", Time, "bed-change", [4]);

        Assert.Equal(Path.Combine(Root, "Core One", "2026-11-04_14-05-09_alarm-bed-change.jpg"), path);
        Assert.Equal(Path.Combine(Root, "Core One", "2026-11-04_14-05-09_alarm-bed-change-2.jpg"), second);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path!));
        Assert.True(File.Exists(Path.Combine(Root, "Core One", CameraRecordingFolders.MarkerFileName)));
        Assert.Empty(Directory.GetFiles(Path.Combine(Root, "Core One"), "*" + PrintFailureAlarmFiles.PartialExtension));
        Assert.True(CameraRecordingFolders.IsSnapshotFileName(Path.GetFileName(path)));
        Assert.False(CameraRecordingFolders.IsRecordingFileName(Path.GetFileName(path)));
    }

    [Fact]
    public void AFolderOfTheUsersOwnIsNeverWrittenTo()
    {
        string folder = Path.Combine(Root, "Core One");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "holiday.jpg"), "mine");

        Assert.Null(_files.SaveSnapshot(Root, "Core One", Time, "bed-change", [1]));
        Assert.Single(Directory.GetFiles(folder));
    }

    [Fact]
    public void LeftoversOfACrashAreRemovedByTheNextWrite()
    {
        string folder = Path.Combine(Root, "Core One");
        Assert.NotNull(_files.SaveSnapshot(Root, "Core One", Time, "bed-change", [1]));
        string old = Path.Combine(folder, "2026-11-04_13-00-00_alarm-bed-change.jpg.0123.partial");
        string fresh = Path.Combine(folder, "2026-11-04_14-05-00_alarm-bed-change.jpg.4567.partial");
        File.WriteAllBytes(old, [1, 2]);
        File.WriteAllBytes(fresh, [1, 2]);
        File.SetLastWriteTimeUtc(old, _time.GetUtcNow().UtcDateTime - PrintFailureAlarmFiles.LeftoverAge);
        File.SetLastWriteTimeUtc(fresh, _time.GetUtcNow().UtcDateTime);

        Assert.NotNull(_files.SaveSnapshot(Root, "Core One", Time.AddMinutes(1), "bed-change", [2]));

        Assert.False(File.Exists(old));
        // Possibly a write still in progress.
        Assert.True(File.Exists(fresh));
        // Never counted as a snapshot by the rotation.
        Assert.All(CameraRecordingStorage.Instance.FindRecordings(Root), static f => Assert.EndsWith(".jpg", f.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void SnapshotsCountInTheSizeLimitAndGoOldestFirstButAreNoRecordingToPlay()
    {
        string folder = Path.Combine(Root, "Core One");
        string oldSnapshot = _files.SaveSnapshot(Root, "Core One", Time.AddDays(-2), "bed-change", new byte[600])!;
        string recording = Path.Combine(folder, "2026-11-03_10-00-00_benchy.mp4");
        File.WriteAllBytes(recording, new byte[500]);
        string newSnapshot = _files.SaveSnapshot(Root, "Core One", Time, "bed-change", new byte[300])!;

        CameraRecordingRotationResult result = CameraRecordingRotation.Run(
            CameraRecordingStorage.Instance, Root, limitBytes: 1000, new HashSet<string>(), activeBytes: 0, _time.GetUtcNow().UtcDateTime);

        Assert.Equal([oldSnapshot], result.Deleted);
        Assert.Equal(800, result.StoredBytes);
        Assert.True(File.Exists(newSnapshot));
        Assert.Equal(recording, result.NewestByFolder["Core One"]);
    }

    [Fact]
    public void AFalseAlarmRecordKeepsItsValuesAndSnapshot()
    {
        string? record = _files.SaveFalseAlarm(Alarm("0123456789abcdef", Time, jpeg: [9, 9]));

        Assert.NotNull(record);
        Assert.Equal("2026-11-04_14-05-09_bed-change_01234567.json", Path.GetFileName(record));
        string json = File.ReadAllText(record);
        Assert.Contains("\"printerName\": \"Core One\"", json, StringComparison.Ordinal);
        Assert.Contains("\"snapshot\": \"2026-11-04_14-05-09_bed-change_01234567.jpg\"", json, StringComparison.Ordinal);
        Assert.Contains("\"printElapsedSeconds\": 1800", json, StringComparison.Ordinal);
        Assert.Equal([9, 9], File.ReadAllBytes(Path.ChangeExtension(record, ".jpg")));

        string? withoutSnapshot = _files.SaveFalseAlarm(Alarm("fedcba9876543210", Time));
        Assert.Contains("\"snapshot\": null", File.ReadAllText(withoutSnapshot!), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheNewestFalseAlarmRecordsAreKept()
    {
        for (int i = 0; i <= PrintFailureAlarmFiles.MaxFalseAlarmRecords + 1; i++)
        {
            Assert.NotNull(_files.SaveFalseAlarm(Alarm($"{i:x8}", Time.AddMinutes(i), jpeg: [1])));
        }

        string[] records = Directory.GetFiles(_files.FalseAlarmFolder, "*.json");
        Assert.Equal(PrintFailureAlarmFiles.MaxFalseAlarmRecords, records.Length);
        Assert.Equal(PrintFailureAlarmFiles.MaxFalseAlarmRecords, Directory.GetFiles(_files.FalseAlarmFolder, "*.jpg").Length);
        Assert.DoesNotContain(records, static r => Path.GetFileName(r).StartsWith("2026-11-04_14-05-09", StringComparison.Ordinal));
    }
}
