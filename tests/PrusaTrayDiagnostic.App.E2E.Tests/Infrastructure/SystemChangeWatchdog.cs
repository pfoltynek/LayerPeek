using System.Globalization;
using System.Management;
using System.Runtime.Versioning;

using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// Layer 4 of the restore (T-065): a separate process <c>pwsh -File tools/run-e2e.ps1 -RestoreOnly -AfterProcess
/// &lt;pid&gt;</c> that waits for the test process to end – normally or killed – and then restores what is still in the
/// journal (nothing, normally).
/// <para>
/// It is created through WMI (<c>Win32_Process.Create</c>), so it is neither a child in the test process tree ("end
/// process tree" or <c>Kill(entireProcessTree)</c> does not reach it), nor in a job object of the test runner, nor on
/// its console (Ctrl+C does not reach it). Its window is hidden; its output goes to
/// <c>system-changes-watchdog.log</c> in the results folder.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SystemChangeWatchdog
{
    /// <summary>
    /// <c>0</c> switches the watchdog off in a child or restore process (the crash self-test checks the start cleanup
    /// alone; the restore makes no change); a test run itself always has one, whatever its environment says.
    /// </summary>
    public const string Variable = "PTD_E2E_WATCHDOG";

    public static bool IsEnabled => !(E2EFixture.IsAuxiliaryProcess && Environment.GetEnvironmentVariable(Variable) == "0");

    /// <summary>Starts the watchdog of the current process; returns its process id.</summary>
    public static int StartForCurrentProcess() => Start(Environment.ProcessId);

    public static int Start(int processId)
    {
        string pwsh = FindOnPath("pwsh.exe")
            ?? throw new InvalidOperationException("PowerShell 7 (pwsh.exe) is not on PATH: no watchdog could restore the system changes after a crash, so none are made.");
        string script = Path.Combine(TestWorktree.Root, "tools", "run-e2e.ps1");
        if (!File.Exists(script))
        {
            throw new InvalidOperationException($"'{script}' is missing: no watchdog, so no system changes.");
        }

        string commandLine = string.Create(
            CultureInfo.InvariantCulture,
            $"\"{pwsh}\" -NoProfile -NonInteractive -File \"{script}\" -RestoreOnly -AfterProcess {processId}");

        using ManagementClass startupClass = new("Win32_ProcessStartup");
        using ManagementObject startup = startupClass.CreateInstance();
        startup["ShowWindow"] = (ushort)0; // SW_HIDE
        using ManagementClass processClass = new("Win32_Process");
        using ManagementBaseObject parameters = processClass.GetMethodParameters("Create");
        parameters["CommandLine"] = commandLine;
        parameters["CurrentDirectory"] = TestWorktree.Root;
        parameters["ProcessStartupInformation"] = startup;
        using ManagementBaseObject result = processClass.InvokeMethod("Create", parameters, null);
        uint code = Convert.ToUInt32(result["ReturnValue"], CultureInfo.InvariantCulture);
        if (code != 0)
        {
            throw new InvalidOperationException($"The watchdog did not start (Win32_Process.Create returned {code}): {commandLine}");
        }

        return Convert.ToInt32(result["ProcessId"], CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Waits until no watchdog of the process <paramref name="processId"/> runs any more. Its restore takes every entry of
    /// a process that is gone, not only its own (T-065), so a test that kills its next child right after the first
    /// watchdog removed its own entry would lose that child's entry to it (T-073: the start cleanup test of a switched-off
    /// monitor found nothing left to restore).
    /// </summary>
    public static async Task WaitUntilGoneAsync(int processId, TimeSpan timeout)
    {
        string query = string.Create(
            CultureInfo.InvariantCulture,
            $"SELECT ProcessId FROM Win32_Process WHERE Name = 'pwsh.exe' AND CommandLine LIKE '%-RestoreOnly -AfterProcess {processId}'");
        await Wait.UntilAsync(
            () =>
            {
                using ManagementObjectSearcher searcher = new(query);
                using ManagementObjectCollection found = searcher.Get();
                return found.Count == 0;
            },
            timeout,
            $"the watchdog of the process {processId} to end");
    }

    internal static string? FindOnPath(string file)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(folder.Trim('"'), file);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }

        return null;
    }
}
