namespace LayerPeek.Core.Cameras.Recording;

/// <summary>One recording file found by <see cref="ICameraRecordingStorage.FindRecordings"/>.</summary>
/// <param name="Path">Full path.</param>
/// <param name="FolderName">Name of its printer folder (directly under the root).</param>
/// <param name="Length">Size in bytes.</param>
/// <param name="LastWriteUtc">When it was last written.</param>
public readonly record struct CameraRecordingFile(string Path, string FolderName, long Length, DateTime LastWriteUtc);

/// <summary>
/// The file system as the recording service (T-145) needs it; a seam for tests (a simulated full disk). Implementations are
/// thread-safe. Paths are never logged by them.
/// </summary>
public interface ICameraRecordingStorage
{
    /// <summary>
    /// Creates <paramref name="folder"/> (with its parents) when missing and writes its
    /// <see cref="CameraRecordingFolders.MarkerFileName"/> when missing, which also proves that it can be written to. An
    /// existing folder without the marker is only taken over while it is empty (apart from system metadata files): the
    /// marker lets the rotation delete files, so it never goes into a folder of the user's own (a custom root that holds a
    /// folder with the printer's name).
    /// </summary>
    /// <exception cref="ForeignRecordingFolderException">The folder exists, has files and no marker.</exception>
    /// <exception cref="IOException">The folder cannot be created or written to (also a full disk).</exception>
    /// <exception cref="UnauthorizedAccessException">No permission.</exception>
    void PrepareFolder(string folder);

    /// <summary>Free bytes for the user on the volume of <paramref name="folder"/>; <see langword="null"/> when unknown (never throws).</summary>
    long? GetAvailableFreeSpace(string folder);

    /// <summary>
    /// The recordings in <paramref name="root"/>: files named by <see cref="CameraRecordingFolders.IsRecordingFileName"/>
    /// or <see cref="CameraRecordingFolders.IsSnapshotFileName"/> (alarm snapshots, T-153) directly in subfolders that carry
    /// the marker; nothing else (no files of the root itself, no deeper levels, no unmarked folders). Missing or unreadable
    /// folders give no files (never throws).
    /// </summary>
    IReadOnlyList<CameraRecordingFile> FindRecordings(string root);

    /// <summary>Deletes the file; <see langword="false"/> when that failed (it is in use, no permission…; never throws).</summary>
    bool TryDelete(string path);
}

/// <summary><see cref="ICameraRecordingStorage"/> over the real file system.</summary>
public sealed class CameraRecordingStorage : ICameraRecordingStorage
{
    private const string MarkerText =
        "LayerPeek stores the camera recordings of one printer in this folder.\n"
        + "When all recordings together exceed the size limit set in LayerPeek, the oldest recordings in this folder\n"
        + "(files named yyyy-MM-dd_HH-mm-ss….mp4, and alarm snapshots yyyy-MM-dd_HH-mm-ss_alarm-….jpg) are deleted.\n"
        + "Other files are never touched.\n";

    public static CameraRecordingStorage Instance { get; } = new();

    /// <summary>Files that the system puts into any folder that was opened; they do not make a folder the user's.</summary>
    private static readonly string[] SystemMetadataFiles = [".DS_Store", "Thumbs.db", "desktop.ini"];

    public void PrepareFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        bool existed = Directory.Exists(folder);
        Directory.CreateDirectory(folder);
        string marker = Path.Combine(folder, CameraRecordingFolders.MarkerFileName);
        if (File.Exists(marker))
        {
            return;
        }

        // Another writer (a recording and a timelapse that start together) may have created the folder and its marker in
        // between: the marker found now makes the folder ours after all.
        if (existed && HasUserEntries(folder) && !File.Exists(marker))
        {
            throw new ForeignRecordingFolderException();
        }

        try
        {
            using FileStream stream = new(marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(System.Text.Encoding.UTF8.GetBytes(MarkerText));
        }
        catch (IOException) when (File.Exists(marker))
        {
            // Written by another recording at the same moment.
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            // A dot does not hide it in Explorer; best effort.
            try
            {
                File.SetAttributes(marker, File.GetAttributes(marker) | FileAttributes.Hidden);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public long? GetAvailableFreeSpace(string folder)
    {
        try
        {
            // Windows needs the drive root (a UNC share gives no DriveInfo: unknown); on Unix any existing path names its volume.
            string? volume = OperatingSystem.IsWindows() ? Path.GetPathRoot(folder) : NearestExisting(folder);
            return string.IsNullOrEmpty(volume) ? null : new DriveInfo(volume).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public IReadOnlyList<CameraRecordingFile> FindRecordings(string root)
    {
        List<CameraRecordingFile> files = [];
        DirectoryInfo rootDirectory = new(root);
        EnumerationOptions options = new() { RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try
        {
            if (!rootDirectory.Exists)
            {
                return files;
            }

            // Links are skipped: a link to another folder must never let the rotation delete files outside the root.
            foreach (DirectoryInfo folder in rootDirectory.EnumerateDirectories("*", options))
            {
                try
                {
                    if (!File.Exists(Path.Combine(folder.FullName, CameraRecordingFolders.MarkerFileName)))
                    {
                        continue;
                    }

                    foreach (FileInfo file in folder.EnumerateFiles("*", options))
                    {
                        if (CameraRecordingFolders.IsRecordingFileName(file.Name) || CameraRecordingFolders.IsSnapshotFileName(file.Name))
                        {
                            files.Add(new CameraRecordingFile(file.FullName, folder.Name, file.Length, file.LastWriteTimeUtc));
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // That folder is skipped; the others still count.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return files;
    }

    public bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasUserEntries(string folder)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
        {
            string name = Path.GetFileName(entry);
            if (!SystemMetadataFiles.Contains(name, StringComparer.OrdinalIgnoreCase)
                && !string.Equals(name, CameraRecordingFolders.MarkerFileName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? NearestExisting(string folder)
    {
        string? path = Path.GetFullPath(folder);
        while (path is not null && !Directory.Exists(path))
        {
            path = Path.GetDirectoryName(path);
        }

        return path;
    }
}

/// <summary>
/// <see cref="ICameraRecordingStorage.PrepareFolder"/>: the printer's folder exists with files and was not created by
/// LayerPeek (no marker), so LayerPeek does not record into it (its rotation would treat matching files as its own).
/// </summary>
public sealed class ForeignRecordingFolderException : IOException
{
    public ForeignRecordingFolderException()
        : base("The printer's recording folder already exists with files that LayerPeek did not create.")
    {
    }

    public ForeignRecordingFolderException(string message)
        : base(message)
    {
    }

    public ForeignRecordingFolderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
