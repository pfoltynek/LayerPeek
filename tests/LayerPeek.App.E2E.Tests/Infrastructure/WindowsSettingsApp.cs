using System.Diagnostics;
using System.Runtime.Versioning;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The Settings app of Windows (<c>SystemSettings.exe</c>, shown in a window of <c>ApplicationFrameHost.exe</c>) opened by
/// a test at an <c>ms-settings:</c> address (T-128: what the button of the settings window opens outside the test mode).
/// Only when Settings is not running: opening an address in a running Settings would replace the page the user has open,
/// so the test then leaves it alone. Closing ends only the Settings this test started (it did not run before). The
/// address goes through the shell (a URI, like the app does), never through a keyboard shortcut.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsSettingsApp : IDisposable
{
    public const string ProcessName = "SystemSettings";

    /// <summary>The process that hosts the windows of Settings (and of other packaged apps).</summary>
    public const string FrameHostName = "ApplicationFrameHost";

    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(20);

    private WindowsSettingsApp(nint foreground, string foregroundProcess)
    {
        Foreground = foreground;
        ForegroundProcess = foregroundProcess;
    }

    /// <summary>The window that came to the foreground for Settings.</summary>
    public nint Foreground { get; }

    /// <summary>Its process: <see cref="ProcessName"/> or <see cref="FrameHostName"/>.</summary>
    public string ForegroundProcess { get; }

    /// <summary>Whether Settings runs (opened by the user or by anyone else).</summary>
    public static bool IsRunning()
    {
        Process[] running = Process.GetProcessesByName(ProcessName);
        foreach (Process process in running)
        {
            process.Dispose();
        }

        return running.Length > 0;
    }

    /// <summary>
    /// Opens <paramref name="address"/> and waits until Settings runs and a window of it (or of its frame host) is in the
    /// foreground, other than the one before. Refuses when Settings runs already (see the class).
    /// </summary>
    public static async Task<WindowsSettingsApp> OpenAsync(string address, CancellationToken cancellationToken)
    {
        if (!address.StartsWith("ms-settings:", StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{address}' is not an address of Settings.", nameof(address));
        }

        if (IsRunning())
        {
            throw new InvalidOperationException("Settings runs already; opening an address would replace the user's page.");
        }

        nint before = Native.GetForegroundWindow();
        using (Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }))
        {
        }

        try
        {
            nint foreground = 0;
            string process = string.Empty;
            await Wait.UntilAsync(
                () =>
                {
                    foreground = Native.GetForegroundWindow();
                    process = ProcessOf(foreground);
                    return IsRunning() && foreground != 0 && foreground != before && process is ProcessName or FrameHostName;
                },
                OpenTimeout,
                () => $"Settings ({address}) in the foreground (last foreground process: '{process}')").WaitAsync(cancellationToken);
            return new WindowsSettingsApp(foreground, process);
        }
        catch
        {
            Close();
            throw;
        }
    }

    /// <summary>The name of the process of a window ("" when it is gone).</summary>
    public static string ProcessOf(nint window)
    {
        if (window == 0)
        {
            return string.Empty;
        }

        _ = Native.GetWindowThreadProcessId(window, out uint id);
        try
        {
            using Process owner = Process.GetProcessById((int)id);
            return owner.ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>Ends Settings (it did not run before the test opened it).</summary>
    public void Dispose() => Close();

    private static void Close()
    {
        foreach (Process process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }
            }
        }
    }
}
