using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// The Windows 11 notification area through UI Automation: the icons on the taskbar (<c>Shell_TrayWnd</c>) and in the
/// overflow flyout (<c>TopLevelWindowForOverflowXamlIsland</c>). The test instance's icon is told apart from the user's own
/// by the tooltip prefix <c>[E2E]</c> of the test mode. Nothing depends on the language of Windows.
/// </summary>
internal static class NotificationArea
{
    public const string TestToolTipPrefix = "[E2E]";

    private const string OverflowClass = "TopLevelWindowForOverflowXamlIsland";

    /// <summary>Automation id of the system buttons of the tray (the overflow chevron, input indicator, clock…).</summary>
    private const string SystemButtonId = "SystemTrayIcon";

    /// <summary>Automation id of the icons of applications.</summary>
    private const string NotifyIconId = "NotifyItemIcon";

    /// <summary>
    /// Every icon with the test prefix (usually one; icons of killed instances may linger until the mouse passes over them),
    /// opening the overflow flyout when none is on the taskbar. <c>OpenedOverflow</c> tells whether the flyout was opened
    /// (close it with <see cref="CloseOverflow"/>).
    /// </summary>
    public static async Task<(IReadOnlyList<AutomationElement> Icons, bool OpenedOverflow)> FindTestIconsAsync(AutomationElement desktop)
    {
        AutomationElement taskbar = desktop.FindFirstChild(cf => cf.ByClassName("Shell_TrayWnd"))
            ?? throw new InvalidOperationException("No taskbar (Shell_TrayWnd).");
        List<AutomationElement> icons = [];
        bool opened = false;
        await Wait.UntilAsync(
            () =>
            {
                icons = [.. FindIcons(taskbar), .. desktop.FindFirstChild(cf => cf.ByClassName(OverflowClass)) is { } overflow ? FindIcons(overflow) : []];
                if (icons.Count > 0)
                {
                    return true;
                }

                if (!opened && OverflowChevron(taskbar) is { } chevron)
                {
                    chevron.Patterns.Invoke.Pattern.Invoke();
                    opened = true;
                }

                return false;
            },
            TimeSpan.FromSeconds(10),
            $"the tray icon with '{TestToolTipPrefix}'");
        return (icons, opened);
    }

    /// <summary>Closes the overflow flyout (Escape) when the tests opened it.</summary>
    public static void CloseOverflow(bool opened)
    {
        if (opened)
        {
            Keyboard.Type(VirtualKeyShort.ESCAPE);
        }
    }

    /// <summary>
    /// "Show hidden icons": the system tray button that comes before every application icon (the others – input
    /// indicator, network, volume, clock – come after them). Found by automation id and order, not by its localized name.
    /// </summary>
    private static AutomationElement? OverflowChevron(AutomationElement taskbar)
    {
        foreach (AutomationElement button in taskbar.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)))
        {
            string id = button.Properties.AutomationId.ValueOrDefault ?? string.Empty;
            if (id == NotifyIconId)
            {
                return null;
            }

            if (id == SystemButtonId)
            {
                return button;
            }
        }

        return null;
    }

    private static IEnumerable<AutomationElement> FindIcons(AutomationElement container) =>
        container.FindAllDescendants(cf => cf.ByAutomationId(NotifyIconId))
            .Where(static button => (button.Properties.Name.ValueOrDefault ?? string.Empty).Contains(TestToolTipPrefix, StringComparison.Ordinal)
                && !button.Properties.IsOffscreen.ValueOrDefault);
}
