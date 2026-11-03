namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// Keeps the recordings of a root within the size limit (T-145, user decision 2026-11-03: delete the oldest). Only files
/// found by <see cref="ICameraRecordingStorage.FindRecordings"/> (LayerPeek's names in LayerPeek's marked folders) are
/// considered, and never a file of a running recording. Empty recordings left by a crash before their first fragment are
/// removed as well once they are old enough not to belong to a recording that is just starting.
/// </summary>
internal static class CameraRecordingRotation
{
    /// <summary>An empty recording older than this is a leftover (a starting recording writes its header within seconds).</summary>
    internal static readonly TimeSpan EmptyLeftoverAge = TimeSpan.FromMinutes(10);

    /// <param name="activeFiles">
    /// Files of starting and running recordings (full paths, case-insensitive): never deleted, nor their further segments
    /// (<c>name-2.mp4</c> …, which exist on disk a moment before the recording lists them).
    /// </param>
    /// <param name="activeBytes">Bytes written by the running recordings in this root so far.</param>
    public static CameraRecordingRotationResult Run(
        ICameraRecordingStorage storage,
        string root,
        long limitBytes,
        IReadOnlySet<string> activeFiles,
        long activeBytes,
        DateTime utcNow)
    {
        IReadOnlyList<CameraRecordingFile> found = storage.FindRecordings(root);
        List<CameraRecordingFile> stored = new(found.Count);
        List<string> deleted = [];
        int failed = 0;
        foreach (CameraRecordingFile file in found)
        {
            if (IsActive(file.Path, activeFiles))
            {
                continue;
            }

            if (file.Length == 0 && utcNow - file.LastWriteUtc >= EmptyLeftoverAge)
            {
                if (storage.TryDelete(file.Path))
                {
                    deleted.Add(file.Path);
                }
                else
                {
                    failed++;
                }

                continue;
            }

            stored.Add(file);
        }

        // Oldest first: by the start time at the start of the name (sorts as text), then in order of writing (the
        // recorder's -2, -3 … of the same start).
        stored.Sort(static (a, b) =>
        {
            int byStart = Path.GetFileName(a.Path.AsSpan())[..CameraRecordingFolders.TimestampLength]
                .CompareTo(Path.GetFileName(b.Path.AsSpan())[..CameraRecordingFolders.TimestampLength], StringComparison.Ordinal);
            return byStart != 0 ? byStart : a.LastWriteUtc.CompareTo(b.LastWriteUtc);
        });

        long storedBytes = 0;
        foreach (CameraRecordingFile file in stored)
        {
            storedBytes += file.Length;
        }

        int next = 0;
        while (storedBytes + activeBytes > limitBytes && next < stored.Count)
        {
            CameraRecordingFile oldest = stored[next++];
            if (storage.TryDelete(oldest.Path))
            {
                deleted.Add(oldest.Path);
                storedBytes -= oldest.Length;
            }
            else
            {
                // In use (a player has it open) or read-only: the next one goes instead.
                failed++;
            }
        }

        Dictionary<string, string> newest = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> gone = new(deleted, StringComparer.OrdinalIgnoreCase);
        foreach (CameraRecordingFile file in stored)
        {
            if (!gone.Contains(file.Path))
            {
                // Sorted oldest first: the last one per folder wins.
                newest[file.FolderName] = file.Path;
            }
        }

        return new CameraRecordingRotationResult(storedBytes, storedBytes + activeBytes > limitBytes, deleted, failed, newest);
    }

    private static bool IsActive(string path, IReadOnlySet<string> activeFiles)
    {
        if (activeFiles.Contains(path))
        {
            return true;
        }

        ReadOnlySpan<char> name = Path.GetFileNameWithoutExtension(path.AsSpan());
        int dash = name.LastIndexOf('-');
        if (dash < 0 || dash == name.Length - 1 || name[(dash + 1)..].ContainsAnyExceptInRange('0', '9'))
        {
            return false;
        }

        string first = Path.Join(Path.GetDirectoryName(path.AsSpan()), string.Concat(name[..dash], Path.GetExtension(path.AsSpan())));
        return activeFiles.Contains(first);
    }
}

/// <param name="StoredBytes">Bytes of the remaining recordings that are not running.</param>
/// <param name="OverLimit">Still above the limit: everything that could go is gone, the running recordings alone are too big.</param>
/// <param name="Deleted">Deleted files (full paths).</param>
/// <param name="FailedDeletes">Files that should have gone but could not be deleted.</param>
/// <param name="NewestByFolder">The newest remaining recording of every printer folder (by folder name, case-insensitive).</param>
internal sealed record CameraRecordingRotationResult(
    long StoredBytes,
    bool OverLimit,
    IReadOnlyList<string> Deleted,
    int FailedDeletes,
    IReadOnlyDictionary<string, string> NewestByFolder);
