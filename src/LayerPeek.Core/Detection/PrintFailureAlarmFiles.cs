using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Detection;

/// <summary>
/// The files of the print failure detection (T-153): alarm snapshots in the printers' recording folders (user decision
/// 2026-11-04: next to the recordings of T-145, in their marked folders and counted in their size limit) and the local
/// "false alarm" records for tuning the detectors (user decision 2026-11-04: nothing leaves the machine, the sensitivity is
/// not changed). Every file is written under a temporary name and then renamed, so a crash never leaves a truncated file
/// under a final name; temporary files older than <see cref="LeftoverAge"/> are deleted by the next write into the
/// same folder. Thread-safe; never throws for I/O problems (they are logged without paths, the result says what was saved).
/// </summary>
public sealed partial class PrintFailureAlarmFiles
{
    /// <summary>The newest false alarm records that are kept (with their snapshots); older ones are deleted.</summary>
    public const int MaxFalseAlarmRecords = 100;

    /// <summary>Extension of the temporary file of a write in progress.</summary>
    internal const string PartialExtension = ".partial";

    private const string RecordExtension = ".json";

    /// <summary>A temporary file older than this is a leftover of a crash (a write takes milliseconds).</summary>
    internal static readonly TimeSpan LeftoverAge = TimeSpan.FromMinutes(10);

    private readonly ICameraRecordingStorage _storage;
    private readonly TimeProvider _time;
    private readonly ILogger<PrintFailureAlarmFiles> _logger;
    private readonly Lock _feedbackGate = new();

    /// <param name="falseAlarmFolder">Where the false alarm records go (in the app's data folder, created on first use).</param>
    public PrintFailureAlarmFiles(ICameraRecordingStorage storage, string falseAlarmFolder, TimeProvider timeProvider, ILogger<PrintFailureAlarmFiles> logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentException.ThrowIfNullOrWhiteSpace(falseAlarmFolder);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        if (!Path.IsPathFullyQualified(falseAlarmFolder))
        {
            throw new ArgumentException("The folder must be an absolute path.", nameof(falseAlarmFolder));
        }

        _storage = storage;
        FalseAlarmFolder = falseAlarmFolder;
        _time = timeProvider;
        _logger = logger;
    }

    public string FalseAlarmFolder { get; }

    /// <summary>
    /// Saves <paramref name="jpeg"/> as the snapshot of an alarm of <paramref name="detectorId"/> at <paramref name="time"/>
    /// into <c>&lt;root&gt;/&lt;printerFolder&gt;</c> (prepared like for a recording: created and marked; never into a folder
    /// of the user's own, see <see cref="ICameraRecordingStorage.PrepareFolder"/>). A taken name gets <c>-2</c>, <c>-3</c> ….
    /// </summary>
    /// <returns>The full path, or <see langword="null"/> when it could not be saved.</returns>
    public string? SaveSnapshot(string root, string printerFolder, DateTimeOffset time, string detectorId, ReadOnlySpan<byte> jpeg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(printerFolder);
        string folder = Path.Combine(root, printerFolder);
        try
        {
            _storage.PrepareFolder(folder);
            DeleteLeftovers(folder);
            string name = CameraRecordingFolders.SnapshotFileName(time, detectorId);
            return Write(folder, name, jpeg);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSnapshotFailed(ex.GetType().Name, ex is ForeignRecordingFolderException);
            return null;
        }
    }

    /// <summary>
    /// Saves the local record of a false alarm: its values as JSON and its snapshot (when it has one) next to it, in
    /// <see cref="FalseAlarmFolder"/>; keeps the newest <see cref="MaxFalseAlarmRecords"/>.
    /// </summary>
    /// <returns>The path of the JSON record, or <see langword="null"/> when it could not be saved.</returns>
    public string? SaveFalseAlarm(PrintFailureAlarm alarm)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        lock (_feedbackGate)
        {
            try
            {
                Directory.CreateDirectory(FalseAlarmFolder);
                DeleteLeftovers(FalseAlarmFolder);
                string stem = string.Create(CultureInfo.InvariantCulture, $"{alarm.Timestamp:yyyy-MM-dd_HH-mm-ss}_{alarm.DetectorId}_{Shorten(alarm.Id)}");
                string? snapshot = alarm.SnapshotJpeg.IsEmpty ? null : Path.GetFileName(Write(FalseAlarmFolder, stem + CameraRecordingFolders.SnapshotExtension, alarm.SnapshotJpeg.Span));
                FalseAlarmDocument document = FalseAlarmDocument.From(alarm, _time.GetUtcNow(), snapshot);
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(document, DetectionJsonContext.Default.FalseAlarmDocument);
                string path = Write(FalseAlarmFolder, stem + RecordExtension, json);
                KeepNewestRecords();
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogFalseAlarmFailed(ex.GetType().Name);
                return null;
            }
        }
    }

    /// <summary>Writes <paramref name="data"/> as <paramref name="name"/> (or the next free <c>-2</c>, <c>-3</c> … name) through a temporary file.</summary>
    private static string Write(string folder, string name, ReadOnlySpan<byte> data)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        string partial = Path.Combine(folder, $"{name}.{Guid.NewGuid():N}{PartialExtension}");
        try
        {
            using (FileStream stream = new(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }

            for (int attempt = 1; ; attempt++)
            {
                string target = Path.Combine(folder, attempt == 1 ? name : string.Create(CultureInfo.InvariantCulture, $"{stem}-{attempt}{extension}"));
                try
                {
                    File.Move(partial, target, overwrite: false);
                    return target;
                }
                catch (IOException) when (attempt < 100 && File.Exists(target))
                {
                    // Taken (two alarms within the same second): the next suffix.
                }
            }
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    /// <summary>Deletes temporary files of writes that never completed (the app crashed or the disk was pulled).</summary>
    private void DeleteLeftovers(string folder)
    {
        DateTime now = _time.GetUtcNow().UtcDateTime;
        foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*" + PartialExtension))
        {
            if (now - file.LastWriteTimeUtc >= LeftoverAge)
            {
                TryDelete(file.FullName);
            }
        }
    }

    /// <summary>Under the lock: deletes the oldest records (and their snapshots) beyond <see cref="MaxFalseAlarmRecords"/>.</summary>
    private void KeepNewestRecords()
    {
        string[] records = Directory.GetFiles(FalseAlarmFolder, "*" + RecordExtension);
        if (records.Length <= MaxFalseAlarmRecords)
        {
            return;
        }

        // The names start with the time: sorted as text, oldest first.
        Array.Sort(records, StringComparer.Ordinal);
        foreach (string record in records.AsSpan(0, records.Length - MaxFalseAlarmRecords))
        {
            TryDelete(Path.ChangeExtension(record, CameraRecordingFolders.SnapshotExtension));
            TryDelete(record);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use or read-only: another write tries again.
        }
    }

    /// <summary>Enough of the id to keep two alarms of the same second apart.</summary>
    private static string Shorten(string id) => id.Length <= 8 ? id : id[..8];

    [LoggerMessage(Level = LogLevel.Warning, Message = "The snapshot of a print failure alarm could not be saved ({Error}; folder of another owner: {ForeignFolder}).")]
    private partial void LogSnapshotFailed(string error, bool foreignFolder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The false alarm record could not be saved ({Error}).")]
    private partial void LogFalseAlarmFailed(string error);
}

/// <summary>The JSON of a false alarm record (local only, for tuning the detectors).</summary>
internal sealed class FalseAlarmDocument
{
    public int Version { get; set; } = 1;

    public string? AlarmId { get; set; }

    public string? PrinterId { get; set; }

    public string? PrinterName { get; set; }

    public string? DetectorId { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public DateTimeOffset ReportedAt { get; set; }

    public float Score { get; set; }

    public float Threshold { get; set; }

    public int Sensitivity { get; set; }

    public long? JobId { get; set; }

    public string? FileName { get; set; }

    public double? AxisZ { get; set; }

    public double ProgressPercent { get; set; }

    public double PrintElapsedSeconds { get; set; }

    public double[]? Region { get; set; }

    public float[]? RecentScores { get; set; }

    /// <summary>File name of the snapshot next to the record; <see langword="null"/> when the alarm had none.</summary>
    public string? Snapshot { get; set; }

    public static FalseAlarmDocument From(PrintFailureAlarm alarm, DateTimeOffset reportedAt, string? snapshot) => new()
    {
        AlarmId = alarm.Id,
        PrinterId = alarm.PrinterId,
        PrinterName = alarm.PrinterName,
        DetectorId = alarm.DetectorId,
        Timestamp = alarm.Timestamp,
        ReportedAt = reportedAt,
        Score = alarm.Score,
        Threshold = alarm.Threshold,
        Sensitivity = alarm.Sensitivity,
        JobId = alarm.Context.JobId,
        FileName = alarm.FileName,
        AxisZ = alarm.Context.AxisZ,
        ProgressPercent = alarm.Context.ProgressPercent,
        PrintElapsedSeconds = alarm.Context.PrintElapsed.TotalSeconds,
        Region = [alarm.Context.Region.X, alarm.Context.Region.Y, alarm.Context.Region.Width, alarm.Context.Region.Height],
        RecentScores = [.. alarm.RecentScores],
        Snapshot = snapshot,
    };
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(FalseAlarmDocument))]
internal sealed partial class DetectionJsonContext : JsonSerializerContext;
