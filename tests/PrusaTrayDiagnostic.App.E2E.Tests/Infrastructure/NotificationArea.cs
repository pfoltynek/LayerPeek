using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// The Windows 11 notification area through UI Automation: the icons on the taskbar (<c>Shell_TrayWnd</c>) and in the
/// overflow flyout ("Show Hidden Icons", <c>TopLevelWindowForOverflowXamlIsland</c>). The test instance's icon is told
/// apart from the user's own by the tooltip prefix <c>[E2E]</c> of the test mode.
/// </summary>
internal static class NotificationArea
{
    public const string TestToolTipPrefix = "[E2E]";

    private const string OverflowClass = "TopLevelWindowForOverflowXamlIsland";

    /// <summary>
    /// The test instance's icon, opening the overflow flyout when it is hidden there. <paramref name="openedOverflow"/>
    /// tells whether the flyout was opened (close it with <see cref="CloseOverflow"/>).
    /// </summary>
    public static async Task<(AutomationElement Icon, bool OpenedOverflow)> FindTestIconAsync(AutomationElement desktop)
    {
        AutomationElement taskbar = desktop.FindFirstChild(cf => cf.ByClassName("Shell_TrayWnd"))
            ?? throw new InvalidOperationException("No taskbar (Shell_TrayWnd).");
        AutomationElement? icon = null;
        bool opened = false;
        await Wait.UntilAsync(
            () =>
            {
                icon = FindIcon(taskbar) ?? (desktop.FindFirstChild(cf => cf.ByClassName(OverflowClass)) is { } overflow ? FindIcon(overflow) : null);
                if (icon is not null)
                {
                    return true;
                }

                if (!opened && taskbar.FindFirstDescendant(cf => cf.ByAutomationId("SystemTrayIcon").And(cf.ByName("Show Hidden Icons"))) is { } chevron)
                {
                    chevron.Patterns.Invoke.Pattern.Invoke();
                    opened = true;
                }

                return false;
            },
            TimeSpan.FromSeconds(10),
            $"the tray icon with '{TestToolTipPrefix}'");
        return (icon!, opened);
    }

    /// <summary>Closes the overflow flyout (Escape) when the tests opened it.</summary>
    public static void CloseOverflow(bool opened)
    {
        if (opened)
        {
            Keyboard.Type(VirtualKeyShort.ESCAPE);
        }
    }

    private static AutomationElement? FindIcon(AutomationElement container) =>
        container.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
            .FirstOrDefault(static button => (button.Properties.Name.ValueOrDefault ?? string.Empty).Contains(TestToolTipPrefix, StringComparison.Ordinal)
                && !button.Properties.IsOffscreen.ValueOrDefault);
}
