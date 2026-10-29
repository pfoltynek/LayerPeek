#if DEBUG
using LayerPeek.App.Composition;

namespace LayerPeek.App.Platform;

/// <summary>
/// Debug builds only (T-019, user decision 2026-10-28 "full cleanup"): <see cref="AppEnvironment.ClearAutostartArgument"/>
/// removes the stand-ins of the test mode's autostart after a crashed test run: the Login Item stand-in of macOS
/// (<see cref="AppEnvironment.TestLoginItemPathIn"/>) and the Startup shortcut stand-in of Windows, both inside the test
/// data folder of <see cref="AppEnvironment.DataDirectoryVariable"/>. It never touches <c>SMAppService</c>, the user's
/// Login Items, <c>~/Library/LaunchAgents</c> or the user's Startup folder: it only deletes files in the test folder.
/// </summary>
internal static class TestAutostartCleanup
{
    /// <summary>Runs the cleanup mode; returns the exit code (0, or 1 with the reason on <paramref name="error"/>).</summary>
    public static int RunFromCommandLine(Func<string, string?> read, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(error);
        string? dataDirectory = read(AppEnvironment.DataDirectoryVariable)?.Trim();
        if (string.IsNullOrEmpty(dataDirectory) || !Path.IsPathFullyQualified(dataDirectory))
        {
            error.WriteLine($"{AppEnvironment.ClearAutostartArgument} needs the absolute test data folder in {AppEnvironment.DataDirectoryVariable}; nothing was removed.");
            return 1;
        }

        try
        {
            foreach (string standIn in StandIns(Path.GetFullPath(dataDirectory)))
            {
                if (File.Exists(standIn))
                {
                    File.Delete(standIn);
                }
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"The autostart stand-ins of the test mode could not be removed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Every stand-in the test mode may have written into <paramref name="dataDirectory"/> (missing ones included).</summary>
    internal static IReadOnlyList<string> StandIns(string dataDirectory) =>
    [
        AppEnvironment.TestLoginItemPathIn(dataDirectory),
        AppEnvironment.ForTests(dataDirectory).AutostartShortcutPath(),
    ];
}
#endif
