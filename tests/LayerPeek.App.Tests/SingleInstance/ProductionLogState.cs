#if DEBUG
using LayerPeek.App.Logging;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.SingleInstance;

/// <summary>
/// The user's log folder of the app (read only, never created or changed): tests that start the real exe compare it before
/// and after, so a start that fell back to production mode shows as a failure (T-074).
/// </summary>
internal readonly record struct ProductionLogState(bool FolderExists, long LogLength, DateTime LogWrittenUtc)
{
    public static string Folder => AppDataPaths.GetDefaultLogDirectory();

    public static ProductionLogState Now()
    {
        string folder = Folder;
        FileInfo log = new(Path.Combine(folder, LogFile.FileName));
        return log.Exists
            ? new ProductionLogState(true, log.Length, log.LastWriteTimeUtc)
            : new ProductionLogState(Directory.Exists(folder), -1, default);
    }
}
#endif
