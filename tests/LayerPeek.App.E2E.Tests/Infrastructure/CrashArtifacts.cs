namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// What Windows may keep of a deliberate crash of the app (T-046), depending on the machine's settings: a dump in
/// <c>%LOCALAPPDATA%\CrashDumps</c> (LocalDumps) and a Windows Error Reporting report. Removed again, so that the crash
/// tests leave nothing behind; only the ".NET Runtime" event in the Application event log stays (accepted 2026-10-22).
/// </summary>
internal static class CrashArtifacts
{
    private static readonly string[] ReportFolders =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "WER", "ReportArchive"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "WER", "ReportQueue"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportQueue"),
    ];

    /// <summary>
    /// Removes the dump of process <paramref name="processId"/> and the error reports of the app created since
    /// <paramref name="since"/>. Windows writes them right after the process ended, so a few seconds are watched.
    /// </summary>
    public static async Task RemoveAsync(int processId, DateTime since)
    {
        string dump = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps", $"LayerPeek.exe.{processId}.dmp");
        for (int attempt = 0; attempt < 10; attempt++)
        {
            TryDelete(dump, static path => File.Delete(path));
            foreach (string folder in ReportFolders.Where(Directory.Exists))
            {
                foreach (string report in Directory.GetDirectories(folder, "AppCrash_LayerPeek*"))
                {
                    if (Directory.GetCreationTime(report) >= since)
                    {
                        TryDelete(report, static path => Directory.Delete(path, recursive: true));
                    }
                }
            }

            await Task.Delay(500);
        }
    }

    private static void TryDelete(string path, Action<string> delete)
    {
        try
        {
            if (Path.Exists(path))
            {
                delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still being written, or a machine-wide report folder this user may not change: named in the output.
            TestContext.Current.SendDiagnosticMessage($"A crash report of this test could not be removed (yet): {path}");
        }
    }
}
