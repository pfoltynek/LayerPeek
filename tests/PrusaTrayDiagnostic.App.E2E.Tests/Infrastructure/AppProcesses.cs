using System.Diagnostics;
using System.Management;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// App processes started by these tests: <c>PrusaTrayDiagnostic.exe</c> whose command line carries
/// <see cref="AppSwitches.MarkerArgument"/> with a folder under the reserved folder. The user's own instance never has
/// the marker, so it is never touched.
/// </summary>
internal static class AppProcesses
{
    /// <summary>Process ids of marked app processes whose marker folder lies in <paramref name="directory"/>.</summary>
    public static List<int> FindMarked(string directory)
    {
        string prefix = AppSwitches.MarkerArgument + Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        List<int> found = [];
        using ManagementObjectSearcher searcher = new("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'PrusaTrayDiagnostic.exe'");
        using ManagementObjectCollection processes = searcher.Get();
        foreach (ManagementBaseObject process in processes)
        {
            using (process)
            {
                if (process["CommandLine"] is string commandLine && IsMarked(commandLine, prefix))
                {
                    found.Add(Convert.ToInt32(process["ProcessId"], System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Kills every marked app process of <paramref name="directory"/> (a crashed or hung run) and returns their
    /// descriptions; anything returned at the end of a run is a leak.
    /// </summary>
    public static async Task<List<string>> KillMarkedAsync(string directory)
    {
        List<string> killed = [];
        foreach (int id in FindMarked(directory))
        {
            try
            {
                using Process process = Process.GetProcessById(id);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                killed.Add($"PrusaTrayDiagnostic ({id})");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
        }

        return killed;
    }

    /// <summary>The marker followed by the folder itself or a sub folder (never a sibling that merely starts alike).</summary>
    internal static bool IsMarked(string commandLine, string prefix)
    {
        int index = commandLine.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        int end = index + prefix.Length;
        return end == commandLine.Length || commandLine[end] is '\\' or '"' or ' ';
    }
}
