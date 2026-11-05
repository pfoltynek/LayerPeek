using Microsoft.Extensions.Logging;

using LayerPeek.Core.Detection;

namespace LayerPeek.App.Notifications;

/// <summary>
/// The image file of a print failure notification (T-154, user decision 2026-11-04): Windows toasts and macOS notification
/// attachments take an image only from a file.
/// <list type="bullet">
/// <item>Windows: the saved alarm snapshot in the recording folder when there is one (<see cref="PrintFailureAlarm.SnapshotPath"/>,
/// <see cref="PrintFailureDetectionSettings.SaveSnapshots"/> on); otherwise the JPEG in memory is written into
/// <see cref="Folder"/>.</item>
/// <item>macOS: always a copy in <see cref="Folder"/>, because the system moves an attached file into its own store (the
/// saved snapshot must stay with the recordings).</item>
/// </list>
/// The folder keeps only the newest <see cref="KeptFiles"/> images (the notification center shows older notifications without
/// their image once it is deleted); every write deletes the older ones and leftovers of an interrupted write, so the folder
/// never grows. Files are named by the alarm id only (no printer or job name). Thread-safe; never throws for file problems
/// (the notification is then shown without an image).
/// </summary>
internal sealed partial class AlarmNotificationImages
{
    /// <summary>Images kept in <see cref="Folder"/>: enough for the notifications on screen at once.</summary>
    public const int KeptFiles = 5;

    /// <summary>Larger images are not attached: Windows refuses toast images above 3 MB (a 1280×720 JPEG is ~0.1–0.3 MB).</summary>
    public const long MaxImageBytes = 3 * 1024 * 1024;

    private const string Prefix = "alarm-";
    private const string Extension = ".jpg";
    private const string PartialExtension = ".partial";

    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    /// <param name="folder">Where images are written (the cache folder of the app, <c>notification-images</c>).</param>
    public AlarmNotificationImages(string folder, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(logger);
        Folder = folder;
        _logger = logger;
    }

    public string Folder { get; }

    /// <summary>The image of <paramref name="alarm"/> as an absolute file path; <see langword="null"/>: no image.</summary>
    /// <param name="useSavedSnapshot">Use the saved snapshot in place (Windows); <see langword="false"/>: always a copy (macOS).</param>
    public string? Prepare(PrintFailureAlarm alarm, bool useSavedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        if (useSavedSnapshot && UsableSavedSnapshot(alarm.SnapshotPath) is { } saved)
        {
            return saved;
        }

        ReadOnlyMemory<byte> jpeg = alarm.SnapshotJpeg;
        if (jpeg.IsEmpty || jpeg.Length > MaxImageBytes || !ToastLaunchArgument.IsValidAlarmId(alarm.Id))
        {
            return null;
        }

        lock (_gate)
        {
            string path = Path.Combine(Folder, Prefix + alarm.Id + Extension);
            string partial = path + PartialExtension;
            try
            {
                Directory.CreateDirectory(Folder);
                using (FileStream stream = new(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(jpeg.Span);
                }

                File.Move(partial, path, overwrite: true);
                Prune(path);
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
            {
                LogWriteFailed(ex);
                TryDelete(partial);
                return null;
            }
        }
    }

    /// <summary>An absolute path of an existing, not too large file; <see langword="null"/> otherwise.</summary>
    private static string? UsableSavedSnapshot(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        try
        {
            FileInfo file = new(path);
            return file.Exists && file.Length is > 0 and <= MaxImageBytes ? file.FullName : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Under the lock: only the newest <see cref="KeptFiles"/> images stay; leftovers of interrupted writes go.</summary>
    private void Prune(string keep)
    {
        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(Folder).GetFiles(Prefix + "*");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            LogPruneFailed(ex);
            return;
        }

        List<FileInfo> images = new(files.Length);
        foreach (FileInfo file in files)
        {
            if (file.Name.EndsWith(PartialExtension, StringComparison.Ordinal))
            {
                // Only this thread writes, under the lock: any partial file is a leftover.
                TryDelete(file.FullName);
            }
            else if (file.Name.EndsWith(Extension, StringComparison.Ordinal) && !string.Equals(file.FullName, Path.GetFullPath(keep), StringComparison.Ordinal))
            {
                images.Add(file);
            }
        }

        // Newest first; the image just written is always kept.
        images.Sort(static (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
        for (int i = KeptFiles - 1; i < images.Count; i++)
        {
            TryDelete(images[i].FullName);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            LogDeleteFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The image of a print failure notification could not be written; the notification is shown without it.")]
    private partial void LogWriteFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The old images of print failure notifications could not be listed.")]
    private partial void LogPruneFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "An old image of a print failure notification could not be deleted.")]
    private partial void LogDeleteFailed(Exception exception);
}
