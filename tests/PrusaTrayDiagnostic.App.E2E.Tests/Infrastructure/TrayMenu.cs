using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>The open tray menu: Avalonia's <c>TrayPopupRoot</c> window of the app and its UI Automation element.</summary>
internal sealed record TrayMenuWindow(nint Handle, AutomationElement Element)
{
    /// <summary>The items of the top level, in order (separators are not items).</summary>
    public AutomationElement[] Items() => TrayMenu.ItemsOf(Element);

    /// <summary>The top-level item named <paramref name="name"/>.</summary>
    public AutomationElement Item(string name) =>
        Items().FirstOrDefault(item => item.Name == name) ?? throw new InvalidOperationException($"No menu item '{name}' in: {string.Join(" | ", Items().Select(static i => TrayMenu.CodePoints(i.Name)))}");

    public bool IsOpen => Native.IsWindowVisible(Handle);
}

/// <summary>
/// The real tray menu of the test instance (T-042, T-069): opened with the real mouse (right click on the tray icon) or
/// only with the keyboard (Win+B, arrows to the icon, Shift+F10), its items and submenus through UI Automation.
/// <para>
/// Failure reports hold only what belongs to the test (user decision 2026-10-22): screenshots of the test's tray button,
/// the overflow flyout and the app's own windows, never the desktop; titles only of the app's own windows; other
/// programs' tray icons only by position.
/// </para>
/// </summary>
internal static class TrayMenu
{
    /// <summary>U+2060 WORD JOINER: the app's invisible access key marker in printer names with "_" (T-042).</summary>
    public const char WordJoiner = '⁠';

    private static readonly TimeSpan MenuTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Right-clicks the test instance's tray icon and returns the menu.</summary>
    public static Task<TrayMenuWindow> OpenWithMouseAsync(AppInstance app) =>
        WithFailureReportAsync(app, "tray-menu-mouse", trace => OpenWithMouseCoreAsync(app, trace));

    /// <summary>
    /// Opens the menu without the mouse: Win+B puts the focus into the notification area, the arrow keys move it onto the
    /// test icon (through the overflow flyout when the icon is not on the taskbar), Shift+F10 opens its menu.
    /// </summary>
    /// <param name="requireTaskbar">The icon must be on the taskbar itself (promoted, T-065), not in the overflow flyout.</param>
    public static Task<TrayMenuWindow> OpenWithKeyboardAsync(AppInstance app, bool requireTaskbar = false) =>
        WithFailureReportAsync(app, "tray-menu-keyboard", async trace =>
        {
            List<nint> before = app.Windows();
            bool overflow = await FocusTestIconAsync(app, requireTaskbar, trace);
            trace($"Shift+F10 (icon {(overflow ? "in the overflow flyout" : "on the taskbar")})");
            Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.F10);
            return await WaitForMenuAsync(app, before, MenuTimeout, trace);
        });

    /// <summary>
    /// Closes the menu (and an open submenu): Escape while the menu has the keyboard, otherwise (or when Escape does not
    /// help) the window pattern's close of the app's own menu window. Escape is never pressed into another program.
    /// </summary>
    public static async Task CloseAsync(TrayMenuWindow menu)
    {
        for (int i = 0; i < 3 && menu.IsOpen && Native.GetForegroundWindow() == menu.Handle; i++)
        {
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            await Task.Delay(300);
        }

        if (menu.IsOpen)
        {
            menu.Element.Patterns.Window.Pattern.Close();
            await Wait.UntilAsync(() => !menu.IsOpen, TimeSpan.FromSeconds(5), "the tray menu to close");
        }
    }

    /// <summary>The menu items directly in the menu's list (not those of an expanded submenu).</summary>
    public static AutomationElement[] ItemsOf(AutomationElement menu)
    {
        AutomationElement? list = menu.FindFirstChild(cf => cf.ByControlType(ControlType.List));
        return list?.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem)) ?? [];
    }

    /// <summary>Expands <paramref name="row"/> and returns the items of its submenu and the submenu's window.</summary>
    public static async Task<(AutomationElement[] Items, nint Window)> ExpandAsync(AppInstance app, TrayMenuWindow menu, AutomationElement row)
    {
        List<nint> before = app.Windows();
        Assert.True(row.Patterns.ExpandCollapse.IsSupported, $"'{CodePoints(row.Name)}' is not a submenu.");
        row.Patterns.ExpandCollapse.Pattern.Expand();
        return await SubmenuAsync(app, menu, row, before);
    }

    /// <summary>The submenu of <paramref name="row"/> once it is open (opened by any means after <paramref name="before"/>).</summary>
    public static async Task<(AutomationElement[] Items, nint Window)> SubmenuAsync(AppInstance app, TrayMenuWindow menu, AutomationElement row, IReadOnlyCollection<nint> before)
    {
        AutomationElement[] items = [];
        nint window = 0;
        await Wait.UntilAsync(
            () =>
            {
                // The items of the open submenu are children of its row in UI Automation; its popup is another window.
                items = row.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem));
                window = app.Windows().FirstOrDefault(hwnd => hwnd != menu.Handle && !before.Contains(hwnd));
                return items.Length > 0 && window != 0;
            },
            MenuTimeout,
            $"the submenu of '{CodePoints(row.Name)}'");
        return (items, window);
    }

    /// <summary>The access key as UI Automation reports it, without the "Alt+" some providers add, upper case (Avalonia reports the letter as written: "Show _camera" → "c").</summary>
    public static string AccessKeyOf(AutomationElement item) =>
        (item.Properties.AccessKey.ValueOrDefault ?? string.Empty).Replace("Alt+", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant();

    /// <summary>The text with every invisible or non-ASCII-printable character as its code point, e.g. <c>MK4&lt;U+2060&gt;_dílna</c>.</summary>
    public static string CodePoints(string? text)
    {
        StringBuilder shown = new();
        foreach (char c in text ?? string.Empty)
        {
            if (IsHidden(c))
            {
                shown.Append(CultureInfo.InvariantCulture, $"<U+{(int)c:X4}>");
            }
            else
            {
                shown.Append(c);
            }
        }

        return shown.ToString();
    }

    /// <summary>The text without U+2060 (user decision 2026-10-22: the name is compared after removing it).</summary>
    public static string WithoutWordJoiner(string text) => text.Replace(WordJoiner.ToString(), string.Empty, StringComparison.Ordinal);

    /// <summary>Characters a reader cannot see: control and format characters (U+2060, U+200B, soft hyphen…).</summary>
    public static bool IsHidden(char c) => char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format;

    /// <summary>The letter after the access key marker of a resource text (<c>Add _printer…</c> → <c>P</c>), or <see langword="null"/>.</summary>
    public static string? AccessKeyLetter(string resourceText)
    {
        int marker = resourceText.IndexOf('_', StringComparison.Ordinal);
        return marker < 0 || marker == resourceText.Length - 1 ? null : char.ToUpperInvariant(resourceText[marker + 1]).ToString();
    }

    /// <summary>The text shown for a resource text: the access key marker removed (<c>_Settings…</c> → <c>Settings…</c>).</summary>
    public static string ShownText(string resourceText)
    {
        int marker = resourceText.IndexOf('_', StringComparison.Ordinal);
        return marker < 0 ? resourceText : resourceText.Remove(marker, 1);
    }

    /// <summary>A text of the app's resources in the repository (<c>Strings[.language].resx</c>, read as XML, not from the app).</summary>
    public static string Resource(string key, string language = "en")
    {
        string file = Path.Combine(TestWorktree.Root, "src", "PrusaTrayDiagnostic.App", "Resources", language == "en" ? "Strings.resx" : $"Strings.{language}.resx");
        XElement? data = XDocument.Load(file).Root?.Elements("data").FirstOrDefault(element => (string?)element.Attribute("name") == key);
        return data?.Element("value")?.Value ?? throw new InvalidOperationException($"No resource '{key}' in {file}.");
    }

    /// <summary>The key of an access key letter (A–Z, 0–9).</summary>
    public static VirtualKeyShort KeyOf(string letter) => (VirtualKeyShort)char.ToUpperInvariant(letter.Single());

    /// <summary>
    /// A window for a report: the title only of the app's and the test's own windows, other programs' windows by process
    /// id and class only (user decision 2026-10-22).
    /// </summary>
    public static string DescribeWindow(AppInstance app, nint hwnd)
    {
        if (hwnd == 0)
        {
            return "(none)";
        }

        Native.GetWindowRect(hwnd, out Native.Rect rect);
        Native.GetWindowThreadProcessId(hwnd, out uint process);
        bool own = process == (uint)app.Process.Id || process == (uint)Environment.ProcessId;
        return $"0x{hwnd:X} pid={process} class={Native.ClassName(hwnd)}" + (own ? $" title='{Native.Title(hwnd)}'" : " (another program)") + $" rect={rect} visible={Native.IsWindowVisible(hwnd)}";
    }

    private static async Task<TrayMenuWindow> OpenWithMouseCoreAsync(AppInstance app, Action<string> trace)
    {
        List<nint> before = app.Windows();
        foreach (nint hwnd in before)
        {
            trace($"App window before: {DescribeWindow(app, hwnd)}");
        }

        (IReadOnlyList<AutomationElement> icons, bool openedOverflow) = await NotificationArea.FindTestIconsAsync(app.Automation, trace);
        TrayMenuWindow? menu = null;
        try
        {
            // Several matches: icons of killed test instances can linger; only the live one opens a menu of this app.
            for (int i = 0; i < icons.Count; i++)
            {
                trace($"Right-click #{i}: {NotificationArea.Describe(icons[i])}");
                NotificationArea.RightClick(icons[i]);
                try
                {
                    menu = await WaitForMenuAsync(app, before, TimeSpan.FromSeconds(icons.Count == 1 ? 10 : 3), trace);
                    break;
                }
                catch (TimeoutException) when (i < icons.Count - 1)
                {
                    // A ghost icon: try the next one.
                }
            }
        }
        finally
        {
            if (menu is null)
            {
                NotificationArea.CloseOverflow(openedOverflow);
            }
        }

        return menu!;
    }

    private static async Task<TrayMenuWindow> WaitForMenuAsync(AppInstance app, IReadOnlyCollection<nint> before, TimeSpan timeout, Action<string> trace)
    {
        TrayMenuWindow? menu = null;
        await Wait.UntilAsync(
            () =>
            {
                foreach (nint hwnd in app.Windows().Where(hwnd => !before.Contains(hwnd)))
                {
                    AutomationElement element = app.Element(hwnd);
                    if (ItemsOf(element).Length > 0)
                    {
                        menu = new TrayMenuWindow(hwnd, element);
                        return true;
                    }
                }

                return false;
            },
            timeout,
            "the tray menu");
        trace($"Menu: {DescribeWindow(app, menu!.Handle)}, foreground {DescribeWindow(app, Native.GetForegroundWindow())}");
        return menu;
    }

    /// <summary>
    /// Moves the keyboard focus onto the test icon; returns whether it is in the overflow flyout. Win+B gives the focus to the
    /// notification area where it was last (not always the first button), so the walk starts from the left end.
    /// </summary>
    private static async Task<bool> FocusTestIconAsync(AppInstance app, bool requireTaskbar, Action<string> trace)
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.KEY_B);
        await Task.Delay(600);
        await WalkAsync(app, VirtualKeyShort.LEFT, 40, IsChevron, trace);
        if (await WalkAsync(app, VirtualKeyShort.RIGHT, 40, IsTestIcon, trace))
        {
            return false;
        }

        if (requireTaskbar)
        {
            throw new InvalidOperationException("The test icon is not on the taskbar (Win+B and the arrow keys did not reach it).");
        }

        // Not on the taskbar: back to "Show hidden icons" (left of the application icons), Enter opens the overflow flyout.
        if (!await WalkAsync(app, VirtualKeyShort.LEFT, 40, IsChevron, trace))
        {
            throw new InvalidOperationException($"The arrow keys did not reach the 'Show hidden icons' button; the focus is on {DescribeFocus(Focused(app))}.");
        }

        // Enter toggles the flyout: one still open (e.g. after a right click on an icon in it) is closed first, so that the
        // focus moves into the flyout when it opens.
        if (NotificationArea.Overflow(app.Automation) is not null)
        {
            trace("The overflow flyout is open: Enter closes it first.");
            Keyboard.Type(VirtualKeyShort.RETURN);
            await Wait.UntilAsync(() => NotificationArea.Overflow(app.Automation) is null, TimeSpan.FromSeconds(5), "the overflow flyout to close");
            await Task.Delay(300);
        }

        Keyboard.Type(VirtualKeyShort.RETURN);
        await Wait.UntilAsync(() => NotificationArea.Overflow(app.Automation) is not null, TimeSpan.FromSeconds(5), "the overflow flyout");
        await Task.Delay(400);
        foreach (VirtualKeyShort key in new[] { VirtualKeyShort.RIGHT, VirtualKeyShort.LEFT, VirtualKeyShort.DOWN, VirtualKeyShort.UP })
        {
            if (await WalkAsync(app, key, 30, IsTestIcon, trace))
            {
                return true;
            }
        }

        throw new InvalidOperationException("The arrow keys did not reach the test icon in the overflow flyout.");
    }

    /// <summary>
    /// Presses <paramref name="key"/> until the focused element is <paramref name="wanted"/> (<see langword="true"/>) or the
    /// focus stops moving or comes back (<see langword="false"/>).
    /// </summary>
    private static async Task<bool> WalkAsync(AppInstance app, VirtualKeyShort key, int steps, Func<AutomationElement?, bool> wanted, Action<string> trace)
    {
        HashSet<string> seen = [];
        for (int i = 0; ; i++)
        {
            AutomationElement? focused = Focused(app);
            trace($"Focus ({key}): {DescribeFocus(focused)}");
            if (wanted(focused))
            {
                return true;
            }

            string id = focused is null ? "(none)" : string.Join(".", focused.Properties.RuntimeId.ValueOrDefault ?? []);
            if (i >= steps || !seen.Add(id))
            {
                return false;
            }

            Keyboard.Type(key);
            await Task.Delay(250);
        }
    }

    private static bool IsChevron(AutomationElement? element) => element?.Properties.AutomationId.ValueOrDefault == "SystemTrayIcon"
        && element.Properties.ClassName.ValueOrDefault != null
        && IsFirstSystemButton(element);

    /// <summary>"Show hidden icons" is the system tray button left of every application icon (the others come after them).</summary>
    private static bool IsFirstSystemButton(AutomationElement element)
    {
        Rectangle bounds = element.BoundingRectangle;
        AutomationElement taskbar = NotificationArea.Taskbar(element.Automation);
        return taskbar.FindAllDescendants(cf => cf.ByAutomationId("NotifyItemIcon")).All(icon => icon.BoundingRectangle.Left >= bounds.Left);
    }

    private static AutomationElement? Focused(AppInstance app)
    {
        try
        {
            return app.Automation.FocusedElement();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
        {
            return null;
        }
    }

    private static bool IsTestIcon(AutomationElement? element) =>
        element is not null && element.Properties.AutomationId.ValueOrDefault == "NotifyItemIcon"
        && (element.Properties.Name.ValueOrDefault ?? string.Empty).Contains(NotificationArea.TestToolTipPrefix, StringComparison.Ordinal);

    /// <summary>The focused element for the trace: the name only of the test icon and of the system's own buttons.</summary>
    private static string DescribeFocus(AutomationElement? element)
    {
        if (element is null)
        {
            return "(none)";
        }

        try
        {
            string id = element.Properties.AutomationId.ValueOrDefault ?? string.Empty;
            string name = IsTestIcon(element) ? $" '{element.Properties.Name.ValueOrDefault}'" : string.Empty;
            return $"{element.Properties.ControlType.ValueOrDefault} id='{id}' class='{element.Properties.ClassName.ValueOrDefault}'{name}";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
        {
            return $"(not readable: 0x{ex.HResult:X8})";
        }
    }

    private static async Task<TrayMenuWindow> WithFailureReportAsync(AppInstance app, string name, Func<Action<string>, Task<TrayMenuWindow>> open)
    {
        StringBuilder report = new();
        Stopwatch clock = Stopwatch.StartNew();
        void Trace(string line) => report.AppendLine(CultureInfo.InvariantCulture, $"{clock.ElapsedMilliseconds,6} ms  {line}");
        try
        {
            return await open(Trace);
        }
        catch (Exception ex)
        {
            Trace($"Failed: {ex}");
            throw new InvalidOperationException($"Opening the tray menu failed ({ex.Message}); diagnostics: {SaveFailureReport(app, name, report, Trace)}", ex);
        }
    }

    /// <summary>
    /// What the next person needs to analyse a failed menu opening without reproducing it: the trace, the app's windows,
    /// the tray icons (others only by position), screenshots of the test icons, the overflow flyout and the app's windows
    /// only (never the desktop), and the app's log.
    /// </summary>
    public static string SaveFailureReport(AppInstance app, string name, StringBuilder report, Action<string> trace)
    {
        try
        {
            Native.GetCursorPos(out Native.Point cursor);
            trace($"Now: cursor=({cursor.X},{cursor.Y}) foreground={DescribeWindow(app, Native.GetForegroundWindow())}");
            foreach (nint hwnd in app.Windows())
            {
                trace($"App window: {DescribeWindow(app, hwnd)}");
                trace($"  screenshot: {Screenshots.Save(hwnd, $"{name}-failure-app-window")}");
            }

            foreach (string icon in NotificationArea.DescribeAll(app.Automation))
            {
                trace($"Icon now: {icon}");
            }

            foreach (AutomationElement icon in NotificationArea.TestIconsOnTaskbar(app.Automation))
            {
                trace($"Test icon on the taskbar: {Screenshots.SaveScreenArea(icon.BoundingRectangle, 8, $"{name}-failure-tray-button")}");
            }

            if (NotificationArea.Overflow(app.Automation) is { } overflow)
            {
                trace($"Overflow flyout: {Screenshots.SaveScreenArea(overflow.BoundingRectangle, 4, $"{name}-failure-overflow")}");
            }
        }
        catch (Exception ex)
        {
            trace($"Collecting the diagnostics failed: {ex}");
        }

        // Called while the menu failure is being reported: a failure here must not replace that one.
        try
        {
            report.AppendLine().AppendLine("App log:").AppendLine(app.ReadLog());
            return Screenshots.SaveText(report.ToString(), $"{name}-failure");
        }
        catch (Exception ex)
        {
            return $"(not saved: {ex.Message})";
        }
    }
}
