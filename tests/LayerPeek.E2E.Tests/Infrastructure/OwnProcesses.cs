using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32.SafeHandles;

namespace LayerPeek.E2E.Tests.Infrastructure;

/// <summary>
/// Finds processes started by the browser tests: their <c>--user-data-dir</c> lies in a directory reserved for the
/// tests. The user's own Edge never matches, so it is never touched.
/// </summary>
internal static partial class OwnProcesses
{
    private const string UserDataDirSwitch = "--user-data-dir=";

    /// <summary>Processes whose <c>--user-data-dir</c> starts with <paramref name="marker"/> (a directory path).</summary>
    public static List<Process> Find(string marker)
    {
        List<Process> found = [];
        int self = Environment.ProcessId;
        // macOS has no /proc: one ps call for all processes instead of one per process (decided 2026-10-18, T-030 Q4).
        Dictionary<int, string>? snapshot = OperatingSystem.IsWindows() || Directory.Exists("/proc") ? null : PsCommandLines();
        foreach (Process process in Process.GetProcesses())
        {
            string? commandLine = process.Id == self ? null
                : snapshot is not null ? snapshot.GetValueOrDefault(process.Id)
                : CommandLine(process.Id);
            if (commandLine is not null && IsOwnCommandLine(commandLine, marker))
            {
                found.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }

        return found;
    }

    /// <summary>
    /// Waits (bounded) until no process with <paramref name="marker"/> runs, then kills what is left. Returns the
    /// descriptions of the killed processes: anything returned here is a leak of the code under test.
    /// </summary>
    public static async Task<List<string>> WaitForExitOrKillAsync(string marker, TimeSpan timeout)
    {
        List<string> killed = [];
        using CancellationTokenSource deadline = new(timeout);
        foreach (Process process in Find(marker))
        {
            using (process)
            {
                try
                {
                    await process.WaitForExitAsync(deadline.Token);
                }
                catch (OperationCanceledException)
                {
                    killed.Add($"{process.ProcessName} ({process.Id})");
                    Kill(process);
                }
                catch (InvalidOperationException)
                {
                    // Exited before it could be watched.
                }
            }
        }

        return killed;
    }

    /// <summary>
    /// Only the browser's profile switch counts (every Edge process of a test run carries it, quoted or not): a mere
    /// mention of the path, e.g. the user's own Edge opening a file that lies under the reserved directory, or a shell
    /// command deleting it, never matches. The switch must be an argument of its own (not the tail of another switch
    /// such as <c>--x--user-data-dir=</c>). Switch names and paths are case-insensitive on Windows only, like Chromium
    /// and the file system there.
    /// </summary>
    internal static bool IsOwnCommandLine(string commandLine, string marker)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int index = 0;
        while ((index = commandLine.IndexOf(UserDataDirSwitch, index, comparison)) >= 0)
        {
            bool startsArgument = index == 0 || char.IsWhiteSpace(commandLine[index - 1]) || commandLine[index - 1] == '"';
            // Either --user-data-dir="<path>" or "--user-data-dir=<path>" or unquoted: at most one quote before the path.
            ReadOnlySpan<char> value = commandLine.AsSpan(index + UserDataDirSwitch.Length);
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                value = value[1..];
            }

            if (startsArgument && value.StartsWith(marker, comparison))
            {
                return true;
            }

            index += UserDataDirSwitch.Length;
        }

        return false;
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    private static string? CommandLine(int processId)
    {
        try
        {
            return OperatingSystem.IsWindows() ? WindowsCommandLine(processId) : UnixCommandLine(processId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Another user's or an exited process: never one of ours.
            return null;
        }
    }

    /// <summary>Linux: <c>/proc/&lt;pid&gt;/cmdline</c> (macOS uses <see cref="PsCommandLines"/>).</summary>
    private static string? UnixCommandLine(int processId)
    {
        string procFile = $"/proc/{processId}/cmdline";
        return File.Exists(procFile) ? File.ReadAllText(procFile).Replace('\0', ' ') : null;
    }

    /// <summary>
    /// The command lines of all processes (also of other users and without a terminal) by one
    /// <c>ps -axww -o pid=,command=</c>: process id -> command line. A process that started after the call is missing,
    /// like one that exited before a per-process query.
    /// </summary>
    internal static Dictionary<int, string> PsCommandLines()
    {
        using Process ps = Process.Start(new ProcessStartInfo("/bin/ps", ["-axww", "-o", "pid=,command="])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        string output = ps.StandardOutput.ReadToEnd();
        ps.WaitForExit();
        return ps.ExitCode == 0 ? ParsePsOutput(output) : throw new InvalidOperationException($"ps failed with exit code {ps.ExitCode}.");
    }

    /// <summary>Lines of <c>ps -o pid=,command=</c>: the right-aligned id, spaces, the command line (kept as it is).</summary>
    internal static Dictionary<int, string> ParsePsOutput(string output)
    {
        Dictionary<int, string> commandLines = [];
        foreach (string line in output.Split('\n'))
        {
            ReadOnlySpan<char> rest = line.AsSpan().TrimStart(' ').TrimEnd('\r');
            int space = rest.IndexOf(' ');
            if (space > 0 && int.TryParse(rest[..space], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int processId))
            {
                commandLines[processId] = rest[(space + 1)..].TrimStart(' ').ToString();
            }
        }

        return commandLines;
    }

    [SupportedOSPlatform("windows")]
    private static unsafe string? WindowsCommandLine(int processId)
    {
        const uint processQueryLimitedInformation = 0x1000;
        const int processCommandLineInformation = 60;
        const int statusInfoLengthMismatch = unchecked((int)0xC0000004);

        using SafeProcessHandle handle = OpenProcess(processQueryLimitedInformation, false, (uint)processId);
        if (handle.IsInvalid)
        {
            return null;
        }

        int status = NtQueryInformationProcess(handle, processCommandLineInformation, null, 0, out int length);
        if (status != statusInfoLengthMismatch || length <= 0)
        {
            return null;
        }

        byte[] buffer = new byte[length];
        fixed (byte* start = buffer)
        {
            if (NtQueryInformationProcess(handle, processCommandLineInformation, start, length, out _) != 0)
            {
                return null;
            }

            // UNICODE_STRING { USHORT Length; USHORT MaximumLength; PWSTR Buffer; } followed by the characters.
            ushort bytes = *(ushort*)start;
            char* text = *(char**)(start + IntPtr.Size);
            return new string(text, 0, bytes / sizeof(char));
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static partial SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("ntdll.dll")]
    [SupportedOSPlatform("windows")]
    private static unsafe partial int NtQueryInformationProcess(SafeProcessHandle processHandle, int processInformationClass, byte* processInformation, int processInformationLength, out int returnLength);
}
