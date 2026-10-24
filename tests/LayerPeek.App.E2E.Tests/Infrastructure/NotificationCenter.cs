using System.Diagnostics;
using System.Runtime.Versioning;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// Opens and closes the notification center of Windows 11 for the Do Not Disturb toggle (T-065) and the toasts (T-068).
/// It is opened by invoking the clock button of the taskbar through UI Automation (what a click on the clock does), never
/// with Win+N: another program can register that hotkey for itself (found 2026-10-24: Office's "Send to OneNote" tool
/// ONENOTEM.EXE takes Win+N for a new quick note, so the run opened OneNote instead, T-073). An invoke that comes while a
/// shell flyout is still closing can be swallowed, so opening waits for the previous flyout to go and invokes a second
/// time when the first brought nothing up. A window of another program is never touched and nothing is typed into it
/// (Escape only while the center itself has the foreground). Only window classes and process names are ever reported.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NotificationCenter
{
    /// <summary>Processes of the Windows 11 shell that host toast banners and the notification center.</summary>
    public static IReadOnlyList<string> ShellProcesses { get; } = ["ShellExperienceHost", "ShellHost"];

    /// <summary>The window class of the shell's XAML flyouts.</summary>
    public const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

    /// <summary>Automation id of the system buttons of the taskbar (clock, volume, network, show hidden icons…).</summary>
    public const string SystemButtonId = "SystemTrayIcon";

    /// <summary>The class of the clock button (date and time) that opens the notification center (build 26300).</summary>
    public const string ClockButtonClass = "SystemTray.OmniButton";

    /// <summary>How long one invoke may take to bring the center to the foreground before it is invoked again.</summary>
    private static readonly TimeSpan PressTimeout = TimeSpan.FromSeconds(4);

    /// <summary>How long a shell flyout in the foreground (the center closing) is waited for before the invoke.</summary>
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Opens the center and returns its window and the window that had the foreground before; throws (with the foreground
    /// window's process and class) when two invokes brought no window of the shell to the foreground. No key is pressed, so
    /// no other program can take the call; a window of another program that gets the foreground meanwhile (e.g. the one
    /// behind a flyout that just closed, T-073: Edge) is only waited past, never touched.
    /// </summary>
    public static async Task<(nint Center, nint Before)> OpenAsync(UIA3Automation automation, CancellationToken cancellationToken = default)
    {
        await WaitWhileShellIsInForegroundAsync(cancellationToken).ConfigureAwait(false);
        nint before = Native.GetForegroundWindow();
        for (int press = 1; press <= 2; press++)
        {
            InvokeClock(automation);
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
            $"The notification center (the taskbar clock) did not come to the foreground after two invokes; the foreground is {Describe(Native.GetForegroundWindow())}.");
    }

    /// <summary>Whether a taskbar button is the clock (date and time) that opens the notification center.</summary>
    public static bool IsClockButton(string? automationId, string? className) =>
        automationId == SystemButtonId && className == ClockButtonClass;

    /// <summary>
    /// A window of another program: not the shell, not Explorer (taskbar, flyouts), not this test process and not the app
    /// under test (a window of it may come up on its own meanwhile, e.g. after a toast).
    /// </summary>
    public static bool IsForeign(string process, string className, int processId) =>
        !IsShell(process, className, processId)
        && !string.Equals(process, "explorer", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(process, AppProcessName, StringComparison.OrdinalIgnoreCase)
        && processId != Environment.ProcessId;

    /// <summary>The process name of the app under test (Debug and Native AOT exe alike).</summary>
    private const string AppProcessName = "LayerPeek";

    /// <summary>Invokes the clock button of the taskbar (found by automation id and class, both language-independent).</summary>
    private static void InvokeClock(UIA3Automation automation)
    {
        nint taskbar = Native.FindWindow(NotificationArea.TaskbarClassName, null);
        if (taskbar == 0)
        {
            throw new InvalidOperationException($"No taskbar ({NotificationArea.TaskbarClassName}).");
        }

        AutomationElement clock = automation.FromHandle(taskbar)
            .FindAllDescendants(cf => cf.ByAutomationId(SystemButtonId))
            .FirstOrDefault(static button => IsClockButton(button.Properties.AutomationId.ValueOrDefault, button.Properties.ClassName.ValueOrDefault))
            ?? throw new InvalidOperationException($"The taskbar has no clock button ({SystemButtonId}, class {ClockButtonClass}).");
        if (clock.Patterns.Invoke.TryGetPattern(out FlaUI.Core.Patterns.IInvokePattern? invoke))
        {
            invoke.Invoke();
        }
        else if (clock.Patterns.Toggle.TryGetPattern(out FlaUI.Core.Patterns.ITogglePattern? toggle))
        {
            toggle.Toggle();
        }
        else
        {
            throw new InvalidOperationException("The clock button of the taskbar can be neither invoked nor toggled.");
        }
    }

    /// <summary>Whether a window belongs to another program (see <see cref="IsForeign(string, string, int)"/>).</summary>
    public static bool IsForeignWindow(nint handle) =>
        ProcessOf(handle) is { } owner && IsForeign(owner.Name, Native.ClassName(handle), owner.Id);

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

    /// <summary>The process name of a window, for failure messages (never its title).</summary>
    public static string ProcessNameOf(nint handle) => ProcessOf(handle)?.Name ?? "(gone)";

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
