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

    /// <summary>
    /// Right-clicks the test instance's tray icon and returns the menu once it has stayed open for a moment. A menu that
    /// closes by itself right after it opened (seen once when the overflow flyout hid at the same moment) is opened once
    /// more; that is written to the test output and the results, a second such close fails.
    /// </summary>
    public static Task<TrayMenuWindow> OpenWithMouseAsync(AppInstance app) =>
        WithFailureReportAsync(app, "tray-menu-mouse", async trace =>
        {
            for (int attempt = 1; ; attempt++)
            {
                TrayMenuWindow menu = await OpenWithMouseCoreAsync(app, trace);
                await Task.Delay(500);
                if (menu.IsOpen)
                {
                    return menu;
                }

                string closed = $"Attempt {attempt}: the tray menu closed by itself within 0.5 s of opening (foreground now {DescribeWindow(app, Native.GetForegroundWindow())}).";
                trace(closed);
                if (attempt == 2)
                {
                    throw new InvalidOperationException("The tray menu closed by itself right after it opened, twice.");
                }

                TestContext.Current.TestOutputHelper?.WriteLine(closed);
                Screenshots.SaveText(closed, "tray-menu-mouse-reopened");
            }
        });

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
            if (!IsTestIcon(Focused(app)))
            {
                throw new InvalidOperationException($"Not pressing Shift+F10: the focus left the test icon ({DescribeFocus(Focused(app))}).");
            }

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

        // A menu opened from the overflow flyout leaves the flyout open; the next test (and the user) must not find it so.
        await NotificationArea.CloseOverflowAsync(menu.Element.Automation);
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

        // An overflow flyout left shown by an earlier step (e.g. a menu item that opened a window, which took the foreground)
        // is stale: Explorer ignored a right-click on its icon there (T-073, the menu never opened). Hidden first, it is
        // opened afresh, with the foreground, below.
        if (NotificationArea.Overflow(app.Automation) is not null && !NotificationArea.HasKeyboard())
        {
            trace($"The overflow flyout is still shown without the keyboard (foreground {DescribeWindow(app, Native.GetForegroundWindow())}): hiding it first.");
            await NotificationArea.CloseOverflowAsync(app.Automation);
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
    /// Moves the keyboard focus onto the test icon; returns whether it is in the overflow flyout. A failed attempt (Explorer
    /// slow to move the focus, Win+B not taken) is started once more from Win+B; both attempts are in the trace.
    /// </summary>
    private static async Task<bool> FocusTestIconAsync(AppInstance app, bool requireTaskbar, Action<string> trace)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await FocusTestIconOnceAsync(app, requireTaskbar, trace);
            }
            catch (Exception ex) when (attempt < 2 && ex is InvalidOperationException or TimeoutException)
            {
                trace($"Attempt {attempt} failed ({ex.Message}); again from Win+B.");
                await NotificationArea.CloseOverflowAsync(app.Automation);
                await Task.Delay(500);
            }
        }
    }

    /// <summary>
    /// One attempt of <see cref="FocusTestIconAsync"/>. Win+B gives the focus to the notification area where it was last
    /// (not always the first button), so the walk starts from the left end.
    /// </summary>
    private static async Task<bool> FocusTestIconOnceAsync(AppInstance app, bool requireTaskbar, Action<string> trace)
    {
        await EnterNotificationAreaAsync(app, trace);
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
            PressInNotificationArea(app, VirtualKeyShort.RETURN);
            await Wait.UntilAsync(() => NotificationArea.Overflow(app.Automation) is null, TimeSpan.FromSeconds(5), "the overflow flyout to close");
            await Task.Delay(300);
        }

        PressInNotificationArea(app, VirtualKeyShort.RETURN);
        await Wait.UntilAsync(() => NotificationArea.Overflow(app.Automation) is not null, TimeSpan.FromSeconds(5), "the overflow flyout");
        await Wait.UntilAsync(() => IsTrayButton(Focused(app)), TimeSpan.FromSeconds(3), "the focus in the overflow flyout");
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
    /// Win+B until the keyboard focus is on a button of the notification area (up to three presses, 2 s each): the arrow
    /// keys that follow must never go to another window.
    /// </summary>
    private static async Task EnterNotificationAreaAsync(AppInstance app, Action<string> trace)
    {
        // An overflow flyout still open (e.g. from a right click on an icon in it) keeps the focus of Win+B inside it.
        if (NotificationArea.Overflow(app.Automation) is not null)
        {
            trace("The overflow flyout is open: closing it before Win+B.");
            await NotificationArea.CloseOverflowAsync(app.Automation);
        }

        for (int press = 1; press <= 3; press++)
        {
            nint before = Native.GetForegroundWindow();
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.KEY_B);
            Stopwatch waited = Stopwatch.StartNew();
            while (waited.ElapsedMilliseconds < 2000)
            {
                await Task.Delay(100);
                // Another program may have registered Win+B for itself (T-073: Win+N was taken by OneNote): its window is
                // left alone, and no further key is pressed (none would reach the notification area anyway).
                nint now = Native.GetForegroundWindow();
                if (now != before && NotificationCenter.IsForeignWindow(now))
                {
                    throw new InvalidOperationException(
                        $"Win+B brought a window of another program to the foreground ({NotificationCenter.ProcessNameOf(now)}); it is left alone and no more keys are pressed.");
                }

                AutomationElement? focused = Focused(app);
                string foreground = Native.ClassName(Native.GetForegroundWindow());
                if (IsTrayButton(focused) && foreground != NotificationArea.OverflowClass)
                {
                    trace($"Win+B #{press}: focus on {DescribeFocus(focused)} after {waited.ElapsedMilliseconds} ms, foreground class '{foreground}'");
                    return;
                }

                if (foreground == NotificationArea.OverflowClass)
                {
                    // Found 2026-10-23: after a menu opened from the overflow flyout, the flyout window (even hidden) kept the
                    // foreground and Win+B put the focus on an icon inside it, so the walk never reached the taskbar.
                    // Escape goes to Explorer's flyout only (it has the foreground) and hands the focus back; Win+B again.
                    trace($"Win+B #{press}: the focus is in the overflow flyout ({DescribeFocus(focused)}); Escape.");
                    Keyboard.Type(VirtualKeyShort.ESCAPE);
                    await Task.Delay(400);
                    break;
                }
            }

            trace($"Win+B #{press}: the focus did not reach the notification area (focus {DescribeFocus(Focused(app))}, foreground {DescribeWindow(app, Native.GetForegroundWindow())}).");

            // A window of another program that kept the foreground (it had it before the press, so the check above did not
            // fire) may be the one that takes Win+B: it gets no second press.
            nint stayed = Native.GetForegroundWindow();
            if (NotificationCenter.IsForeignWindow(stayed))
            {
                throw new InvalidOperationException(
                    $"Win+B left a window of another program in the foreground ({NotificationCenter.ProcessNameOf(stayed)}); it is left alone and no more keys are pressed.");
            }
        }

        throw new InvalidOperationException("Win+B did not put the keyboard focus into the notification area.");
    }

    /// <summary>
    /// Presses <paramref name="key"/> until the focused element is <paramref name="wanted"/> (<see langword="true"/>) or the
    /// focus stops moving, comes back or leaves the notification area (<see langword="false"/>). After each press it waits
    /// for the focus to move (Explorer is sometimes slow, a fixed pause read the old element and ended the walk early).
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

            string id = IdOf(focused);
            if (i >= steps || !seen.Add(id))
            {
                return false;
            }

            if (!IsTrayButton(focused))
            {
                trace($"The focus left the notification area: no {key}.");
                return false;
            }

            PressInNotificationArea(app, key);
            Stopwatch moving = Stopwatch.StartNew();
            while (moving.ElapsedMilliseconds < 1000 && IdOf(Focused(app)) == id)
            {
                await Task.Delay(50);
            }

            await Task.Delay(100);
        }
    }

    /// <summary>A key for the notification area: only while the focus is on one of its buttons, never into another window.</summary>
    private static void PressInNotificationArea(AppInstance app, VirtualKeyShort key)
    {
        AutomationElement? focused = Focused(app);
        if (!IsTrayButton(focused))
        {
            throw new InvalidOperationException($"Not pressing {key}: the keyboard focus is not in the notification area ({DescribeFocus(focused)}, foreground {DescribeWindow(app, Native.GetForegroundWindow())}).");
        }

        Keyboard.Type(key);
    }

    /// <summary>
    /// Presses <paramref name="key"/> for the app (the open tray menu, its submenu): only while a window of the app is in the
    /// foreground, so a menu that closed meanwhile fails the test instead of the key going to another program.
    /// </summary>
    public static void Press(AppInstance app, VirtualKeyShort key)
    {
        nint foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out uint process);
        if (process != (uint)app.Process.Id)
        {
            throw new InvalidOperationException($"Not pressing {key}: the app does not have the keyboard (foreground {DescribeWindow(app, foreground)}).");
        }

        Keyboard.Type(key);
    }

    /// <summary>A button of the notification area (an application icon or a system button such as "Show hidden icons").</summary>
    private static bool IsTrayButton(AutomationElement? element) =>
        Readable(() => element?.Properties.AutomationId.ValueOrDefault is "SystemTrayIcon" or "NotifyItemIcon");

    private static string IdOf(AutomationElement? element)
    {
        try
        {
            return element is null ? "(none)" : string.Join(".", element.Properties.RuntimeId.ValueOrDefault ?? []);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
        {
            return "(gone)";
        }
    }

    private static bool IsChevron(AutomationElement? element) => Readable(() => element?.Properties.AutomationId.ValueOrDefault == "SystemTrayIcon"
        && element.Properties.ClassName.ValueOrDefault != null
        && IsFirstSystemButton(element));

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

    private static bool IsTestIcon(AutomationElement? element) => Readable(() =>
        element is not null && element.Properties.AutomationId.ValueOrDefault == "NotifyItemIcon"
        && (element.Properties.Name.ValueOrDefault ?? string.Empty).Contains(NotificationArea.TestToolTipPrefix, StringComparison.Ordinal));

    /// <summary>A check of a focused element that may vanish while it is read (Explorer rebuilds its buttons): then false.</summary>
    private static bool Readable(Func<bool> check)
    {
        try
        {
            return check();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
        {
            return false;
        }
    }

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
                trace($"Test icon on the taskbar: {ScreenArea.Save(icon.BoundingRectangle, 8, $"{name}-failure-tray-button")}");
            }

            if (NotificationArea.Overflow(app.Automation) is { } overflow)
            {
                trace($"Overflow flyout: {ScreenArea.Save(overflow.BoundingRectangle, 4, $"{name}-failure-overflow")}");
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
