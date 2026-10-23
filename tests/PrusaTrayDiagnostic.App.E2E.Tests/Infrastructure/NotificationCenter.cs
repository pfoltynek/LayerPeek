using System.Diagnostics;
using System.Runtime.Versioning;

using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// Opens and closes the notification center of Windows 11 (Win+N) for the Do Not Disturb toggle (T-065) and the toasts
/// (T-068). Win+N pressed while a shell flyout is still closing is swallowed (seen on build 26300: the next test opened
/// nothing and the toggle search saw no window), so opening waits for the previous flyout to go and presses Win+N a
/// second time when the first press brought nothing up. Only window classes and process names are ever reported.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NotificationCenter
{
    /// <summary>Processes of the Windows 11 shell that host toast banners and the notification center.</summary>
    public static IReadOnlyList<string> ShellProcesses { get; } = ["ShellExperienceHost", "ShellHost"];

    /// <summary>The window class of the shell's XAML flyouts.</summary>
    public const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

    /// <summary>How long one Win+N may take to bring the center to the foreground before it is pressed again.</summary>
    private static readonly TimeSpan PressTimeout = TimeSpan.FromSeconds(4);

    /// <summary>How long a shell flyout in the foreground (the center closing) is waited for before Win+N.</summary>
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Opens the center and returns its window and the window that had the foreground before; throws (with the foreground
    /// window's process and class) when two presses brought no window of the shell to the foreground.
    /// </summary>
    public static async Task<(nint Center, nint Before)> OpenAsync(CancellationToken cancellationToken = default)
    {
        await WaitWhileShellIsInForegroundAsync(cancellationToken).ConfigureAwait(false);
        nint before = Native.GetForegroundWindow();
        for (int press = 1; press <= 2; press++)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.KEY_N);
            DateTime deadline = DateTime.UtcNow + PressTimeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nint foreground = Native.GetForegroundWindow();
                if (foreground != 0 && foreground != before && IsShellWindow(foreground))
                {
                    return (foreground, before);
                }

                await Task.Delay(Poll, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException(
            $"The notification center (Win+N) did not come to the foreground after two presses; the foreground is {Describe(Native.GetForegroundWindow())}.");
    }

    /// <summary>
    /// Closes the center with Escape while it still has the foreground (a click on a toast closes it by itself; Escape
    /// to another window would reach whatever has the focus), then waits until it is gone.
    /// </summary>
    public static async Task CloseAsync(nint center)
    {
        if (center == 0 || Native.GetForegroundWindow() != center)
        {
            return;
        }

        Keyboard.Type(VirtualKeyShort.ESCAPE);
        await WaitWhileShellIsInForegroundAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>A window of the shell processes, or a XAML flyout window outside Explorer and this process.</summary>
    public static bool IsShell(string process, string className, int processId) =>
        ShellProcesses.Contains(process, StringComparer.OrdinalIgnoreCase)
        || (className == CoreWindowClass && !string.Equals(process, "explorer", StringComparison.OrdinalIgnoreCase) && processId != Environment.ProcessId);

    /// <summary>Whether a window belongs to the shell (see <see cref="IsShell"/>).</summary>
    public static bool IsShellWindow(nint handle) =>
        ProcessOf(handle) is { } owner && IsShell(owner.Name, Native.ClassName(handle), owner.Id);

    /// <summary>Process name and class of a window, for failure messages (never its title).</summary>
    public static string Describe(nint handle) =>
        handle == 0 ? "(none)" : $"{ProcessOf(handle)?.Name ?? "(gone)"} class='{Native.ClassName(handle)}'";

    private static async Task WaitWhileShellIsInForegroundAsync(CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + SettleTimeout;
        while (DateTime.UtcNow < deadline && IsShellWindow(Native.GetForegroundWindow()))
        {
            await Task.Delay(Poll, cancellationToken).ConfigureAwait(false);
        }

        // The closing animation of a flyout outlasts its foreground a little.
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
    }

    private static (string Name, int Id)? ProcessOf(nint handle)
    {
        if (handle == 0)
        {
            return null;
        }

        Native.GetWindowThreadProcessId(handle, out uint id);
        try
        {
            using Process owner = Process.GetProcessById((int)id);
            return (owner.ProcessName, (int)id);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
