using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>Whether Do Not Disturb (Windows 11) is on.</summary>
internal sealed record DoNotDisturbState(bool Enabled)
{
    public static DoNotDisturbState Off { get; } = new(false);

    public static DoNotDisturbState On { get; } = new(true);
}

/// <summary>
/// Do Not Disturb of Windows 11 (T-065). There is no public API to set it, so the "Do not disturb" toggle of the
/// notification center (opened from the taskbar clock) is read and switched through UI Automation; the fallback is the Settings page
/// <c>ms-settings:notifications</c>, used only when the Settings app is not running (it would replace a page the user
/// has open) and closed again afterwards. The toggle is found by its automation id, not by its localized name.
/// Automatic Do Not Disturb rules (full screen, duplicated display, games) are never changed.
/// <para>
/// Not yet verified on a real desktop (the self-test is the first use): when the toggle is not found, the failure lists
/// every toggle that was seen, so the automation id can be corrected.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class DoNotDisturbKind() : SystemChangeKind<DoNotDisturbState>("DoNotDisturb", SystemChangeTraits.DoNotDisturb, SystemChangeJson.Default.DoNotDisturbState)
{
    /// <summary>Parts of the automation id of the toggle (notification center and Settings), compared ignoring case.</summary>
    private static readonly string[] ToggleIdParts = ["DoNotDisturb", "QuietHours"];

    private static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(8);

    public override async Task<DoNotDisturbState> ReadStateAsync(string? key, CancellationToken cancellationToken) =>
        new(await WithToggleAsync(static toggle => Task.FromResult(IsOn(toggle)), cancellationToken).ConfigureAwait(false));

    public override string Describe(string? key, DoNotDisturbState original) =>
        $"Do Not Disturb was {(original.Enabled ? "ON" : "OFF")}. By hand: click the clock on the taskbar > the bell button (Do not disturb) at the top of the "
        + "notification center, or Settings > System > Notifications > Do not disturb.";

    /// <summary>
    /// <c>SHQueryUserNotificationState</c> (for the results: whether it follows the toggle is not documented,
    /// https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryusernotificationstate).
    /// </summary>
    public static string QueryUserNotificationState()
    {
        int result = SHQueryUserNotificationState(out int state);
        string name = state switch
        {
            1 => "QUNS_NOT_PRESENT",
            2 => "QUNS_BUSY",
            3 => "QUNS_RUNNING_D3D_FULL_SCREEN",
            4 => "QUNS_PRESENTATION_MODE",
            5 => "QUNS_ACCEPTS_NOTIFICATIONS",
            6 => "QUNS_QUIET_TIME",
            7 => "QUNS_APP",
            _ => "unknown",
        };
        return result == 0 ? $"{name} ({state})" : $"failed 0x{result:X8}";
    }

    protected override async Task ApplyCoreAsync(string? key, DoNotDisturbState state, CancellationToken cancellationToken) =>
        await WithToggleAsync(
            async toggle =>
            {
                if (IsOn(toggle) != state.Enabled)
                {
                    toggle.Patterns.Toggle.Pattern.Toggle();
                    await Wait.UntilAsync(() => IsOn(toggle) == state.Enabled, TimeSpan.FromSeconds(5), $"Do Not Disturb {(state.Enabled ? "on" : "off")}").ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);

    /// <summary>Whether an automation id may be the Do not disturb toggle.</summary>
    internal static bool IsCandidateId(string automationId) =>
        ToggleIdParts.Any(part => automationId.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static bool IsOn(AutomationElement toggle) => toggle.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On;

    /// <summary>Opens the notification center (or the Settings page), runs <paramref name="use"/> on the toggle and closes it.</summary>
    private static async Task<T> WithToggleAsync<T>(Func<AutomationElement, Task<T>> use, CancellationToken cancellationToken)
    {
        using UIA3Automation automation = new();
        StringBuilder seen = new();

        string centerProblem;
        try
        {
            (nint center, nint before) = await NotificationCenter.OpenAsync(automation, cancellationToken).ConfigureAwait(false);
            try
            {
                if (await FindToggleAsync(automation, before, seen, "notification center", cancellationToken).ConfigureAwait(false) is { } toggle)
                {
                    return await use(toggle).ConfigureAwait(false);
                }

                centerProblem = $"The notification center ({NotificationCenter.Describe(center)}) has no Do not disturb toggle";
            }
            finally
            {
                await NotificationCenter.CloseAsync(center).ConfigureAwait(false);
            }
        }
        catch (TimeoutException ex)
        {
            centerProblem = ex.Message;
        }

        if (Process.GetProcessesByName("SystemSettings") is { Length: > 0 } running)
        {
            foreach (Process process in running)
            {
                process.Dispose();
            }

            throw new InvalidOperationException(
                $"{centerProblem}, and the Settings app is open (the fallback would replace its page). "
                + $"Close Settings and run again. Toggles seen:{Environment.NewLine}{seen}");
        }

        nint beforeSettings = Native.GetForegroundWindow();
        using (Process.Start(new ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true }))
        {
        }

        try
        {
            if (await FindToggleAsync(automation, beforeSettings, seen, "Settings", cancellationToken).ConfigureAwait(false) is { } toggle)
            {
                return await use(toggle).ConfigureAwait(false);
            }

            throw new InvalidOperationException($"No Do not disturb toggle (automation id with {string.Join(" or ", ToggleIdParts)}) found. Toggles seen:{Environment.NewLine}{seen}");
        }
        finally
        {
            // Settings did not run before (checked above), so every Settings process is the one opened here; whatever
            // window has the focus now is never touched.
            CloseSettings();
        }
    }

    private static void CloseSettings()
    {
        foreach (Process process in Process.GetProcessesByName("SystemSettings"))
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

    /// <summary>
    /// Searches the window that came to the foreground for the toggle; records every toggle seen. More than one candidate
    /// (e.g. a toggle of an automatic rule) fails instead of guessing: the automatic rules must never change.
    /// </summary>
    private static async Task<AutomationElement?> FindToggleAsync(UIA3Automation automation, nint before, StringBuilder seen, string where, CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + FindTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nint foreground = Native.GetForegroundWindow();
            if (foreground != 0 && foreground != before)
            {
                try
                {
                    List<AutomationElement> candidates = [];
                    foreach (AutomationElement element in automation.FromHandle(foreground).FindAllDescendants())
                    {
                        if (!element.Patterns.Toggle.IsSupported)
                        {
                            continue;
                        }

                        string id = element.Properties.AutomationId.ValueOrDefault ?? string.Empty;
                        string line = $"{where}: '{element.Properties.Name.ValueOrDefault}' id='{id}' class='{element.Properties.ClassName.ValueOrDefault}'";
                        if (!seen.ToString().Contains(line, StringComparison.Ordinal))
                        {
                            seen.AppendLine(line);
                        }

                        if (IsCandidateId(id))
                        {
                            candidates.Add(element);
                        }
                    }

                    if (candidates.Count > 1)
                    {
                        throw new InvalidOperationException($"More than one Do not disturb toggle candidate in the {where}; refusing to guess. Toggles seen:{Environment.NewLine}{seen}");
                    }

                    if (candidates.Count == 1)
                    {
                        return candidates[0];
                    }
                }
                // The flyout is still being built (COMException, FlaUI's ElementNotAvailableException): try again.
                catch (Exception ex) when (ex is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
                {
                }
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);
}
