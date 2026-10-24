using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The Windows 11 notification area through UI Automation: the icons on the taskbar (<c>Shell_TrayWnd</c>) and in the
/// overflow flyout (<c>TopLevelWindowForOverflowXamlIsland</c>). The test instance's icon is told apart from the user's own
/// by the tooltip prefix <c>[E2E]</c> of the test mode. Nothing depends on the language of Windows.
/// </summary>
internal static class NotificationArea
{
    public const string TestToolTipPrefix = "[E2E]";

    public const string TaskbarClassName = "Shell_TrayWnd";

    public const string OverflowClass = "TopLevelWindowForOverflowXamlIsland";

    /// <summary>Automation id of the system buttons of the tray (the overflow chevron, input indicator, clock…).</summary>
    private const string SystemButtonId = "SystemTrayIcon";

    /// <summary>Automation id of the icons of applications.</summary>
    private const string NotifyIconId = "NotifyItemIcon";

    /// <summary>
    /// Every icon with the test prefix (usually one; icons of killed instances may linger until the mouse passes over them),
    /// opening the overflow flyout when none is on the taskbar. <c>OpenedOverflow</c> tells whether the flyout was opened
    /// (close it with <see cref="CloseOverflow"/>).
    /// </summary>
    public static async Task<(IReadOnlyList<AutomationElement> Icons, bool OpenedOverflow)> FindTestIconsAsync(AutomationBase automation, Action<string>? trace = null)
    {
        AutomationElement taskbar = Taskbar(automation);
        List<AutomationElement> icons = [];
        bool opened = false;
        DateTime openedAt = DateTime.MinValue;
        await Wait.UntilAsync(
            () =>
            {
                icons = [.. FindIcons(taskbar), .. Overflow(automation) is { } overflow ? FindIcons(overflow) : []];
                if (icons.Count > 0)
                {
                    return true;
                }

                // The flyout may not open (or close again right away, e.g. while focus changes): opened again after 2 s.
                if ((!opened || (Overflow(automation) is null && DateTime.UtcNow - openedAt > TimeSpan.FromSeconds(2))) && OverflowChevron(taskbar) is { } chevron)
                {
                    trace?.Invoke(opened ? "The overflow flyout is not shown: opening it again." : "No test icon on the taskbar: opening the overflow flyout.");
                    chevron.Patterns.Invoke.Pattern.Invoke();
                    opened = true;
                    openedAt = DateTime.UtcNow;
                }

                return false;
            },
            TimeSpan.FromSeconds(10),
            $"the tray icon with '{TestToolTipPrefix}'");
        if (trace is not null)
        {
            foreach (AutomationElement icon in icons)
            {
                trace($"Test icon: {Describe(icon)}");
            }
        }

        return (icons, opened);
    }

    /// <summary>Test icons shown directly on the taskbar (not in the overflow flyout), e.g. after a promotion (T-065).</summary>
    public static IReadOnlyList<AutomationElement> TestIconsOnTaskbar(AutomationBase automation) => [.. FindIcons(Taskbar(automation))];

    /// <summary>
    /// The taskbar. Top-level windows are found through Win32, not as children of the UI Automation desktop: listing those
    /// asks the provider of every top-level window of every process, and one busy window (a loaded machine, the app's own
    /// camera window) fails the whole search with <c>UIA_E_TIMEOUT</c> (0x80131505, T-062).
    /// </summary>
    public static AutomationElement Taskbar(AutomationBase automation)
    {
        nint hwnd = Native.FindWindow(TaskbarClassName, null);
        return hwnd != 0 ? automation.FromHandle(hwnd) : throw new InvalidOperationException($"No taskbar ({TaskbarClassName}).");
    }

    /// <summary>The overflow flyout while it is shown, or <see langword="null"/> (see <see cref="Taskbar"/>).</summary>
    public static AutomationElement? Overflow(AutomationBase automation)
    {
        nint hwnd = Native.FindWindow(OverflowClass, null);
        return hwnd != 0 && Native.IsWindowVisible(hwnd) ? automation.FromHandle(hwnd) : null;
    }

    /// <summary>
    /// Every application icon of the taskbar and the shown overflow flyout, for a failure report: the tooltip only of the
    /// test icons (<see cref="TestToolTipPrefix"/>); icons of other programs only by position (T-069: nothing of the user's
    /// other programs in the results).
    /// </summary>
    public static IEnumerable<string> DescribeAll(AutomationBase automation)
    {
        foreach (AutomationElement icon in Taskbar(automation).FindAllDescendants(cf => cf.ByAutomationId(NotifyIconId)))
        {
            yield return $"taskbar: {DescribeAny(icon)}";
        }

        if (Overflow(automation) is { } overflow)
        {
            foreach (AutomationElement icon in overflow.FindAllDescendants(cf => cf.ByAutomationId(NotifyIconId)))
            {
                yield return $"overflow: {DescribeAny(icon)}";
            }
        }
    }

    /// <summary>A test icon in full (<see cref="Describe"/>), another program's icon without its tooltip.</summary>
    public static string DescribeAny(AutomationElement icon)
    {
        try
        {
            return (icon.Properties.Name.ValueOrDefault ?? string.Empty).Contains(TestToolTipPrefix, StringComparison.Ordinal)
                ? Describe(icon)
                : $"(another program's icon) bounds={icon.Properties.BoundingRectangle.ValueOrDefault} offscreen={icon.Properties.IsOffscreen.ValueOrDefault}";
        }
        // Report only: a vanished icon must not fail the caller.
        catch (Exception ex)
        {
            return $"(not readable: {ex.GetType().Name} 0x{ex.HResult:X8})";
        }
    }

    /// <summary>
    /// Right-clicks the icon with the real mouse. The pointer jumps onto the icon instead of FlaUI's animated move: no
    /// other icon is hovered on the way, and the pointer is ours for milliseconds instead of a few hundred. The real
    /// mouse is shared with the person at the computer, so a pointer moved meanwhile fails with that reason, not with a
    /// menu timeout later (T-062).
    /// </summary>
    public static void RightClick(AutomationElement icon)
    {
        System.Drawing.Rectangle bounds = icon.BoundingRectangle;
        System.Drawing.Point center = new(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
        Mouse.Position = center;
        Mouse.Click(MouseButton.Right);
        Native.GetCursorPos(out Native.Point cursor);
        if (!bounds.Contains(cursor.X, cursor.Y))
        {
            throw new InvalidOperationException(
                $"The mouse pointer left the tray icon {bounds} right after the click (it is at {cursor.X},{cursor.Y}): someone used the mouse during the end-to-end run.");
        }
    }

    /// <summary>One icon for a failure report: tooltip, screen rectangle, off-screen flag and its window.</summary>
    public static string Describe(AutomationElement icon)
    {
        try
        {
            return $"'{icon.Properties.Name.ValueOrDefault}' bounds={icon.Properties.BoundingRectangle.ValueOrDefault} "
                + $"offscreen={icon.Properties.IsOffscreen.ValueOrDefault} hwnd=0x{icon.Properties.NativeWindowHandle.ValueOrDefault:X}";
        }
        // Report only: a vanished icon (COMException, FlaUI's ElementNotAvailableException) must not fail the caller.
        catch (Exception ex)
        {
            return $"(not readable: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message})";
        }
    }

    /// <summary>
    /// Closes the overflow flyout (Escape) when the tests opened it, but only while it has the keyboard: Escape is never
    /// pressed into another window (an app window, another program). Use <see cref="CloseOverflowAsync"/> to close it
    /// whoever has the keyboard.
    /// </summary>
    public static void CloseOverflow(bool opened)
    {
        if (opened && OverflowHasKeyboard())
        {
            Keyboard.Type(VirtualKeyShort.ESCAPE);
        }
    }

    /// <summary>
    /// Closes the overflow flyout when it is shown: Escape while it has the keyboard, otherwise "Show hidden icons" through
    /// UI Automation (it toggles the flyout; no key goes to another window). Waits up to 2 s for it to hide.
    /// </summary>
    public static async Task CloseOverflowAsync(AutomationBase automation)
    {
        if (Overflow(automation) is null)
        {
            return;
        }

        if (OverflowHasKeyboard())
        {
            Keyboard.Type(VirtualKeyShort.ESCAPE);
        }
        else
        {
            OverflowChevron(Taskbar(automation))?.Patterns.Invoke.Pattern.Invoke();
        }

        for (int i = 0; i < 20 && Overflow(automation) is not null; i++)
        {
            await Task.Delay(100);
        }
    }

    /// <summary>Whether the keyboard goes to the notification area: the taskbar or the overflow flyout is in the foreground.</summary>
    public static bool HasKeyboard() => Native.ClassName(Native.GetForegroundWindow()) is TaskbarClassName or OverflowClass;

    private static bool OverflowHasKeyboard() => Native.ClassName(Native.GetForegroundWindow()) == OverflowClass;

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
