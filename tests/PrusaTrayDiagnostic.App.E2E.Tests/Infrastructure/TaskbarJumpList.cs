using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// The app's button on the Windows 11 taskbar and its jump list (T-072), through UI Automation. The taskbar groups the
/// windows of one AppUserModelID into one button whose automation id is <c>Appid: &lt;AUMID&gt;</c>: the test app
/// (<see cref="AppSwitches.AppUserModelId"/>) has a button of its own, apart from the user's instance
/// (<see cref="ProductionButtonId"/>). The jump list opens on a right-click with the real mouse; it is a
/// <c>Windows.UI.Core.CoreWindow</c> of the shell whose entries are list items. Nothing depends on the language of
/// Windows: buttons and entries are told apart by automation ids; texts are only reported.
/// </summary>
internal static class TaskbarJumpList
{
    /// <summary>Automation id of the test app's taskbar button.</summary>
    public const string TestButtonId = "Appid: " + AppSwitches.AppUserModelId;

    /// <summary>Automation id of the taskbar button of the user's own instance (production AUMID); never clicked.</summary>
    public const string ProductionButtonId = "Appid: PrusaTrayDiagnostic";

    /// <summary>Automation id of the jump list entry that closes the window(s) of the button.</summary>
    public const string CloseEntryId = "Close";

    private const string JumpListClass = "Windows.UI.Core.CoreWindow";

    /// <summary>The taskbar button with <paramref name="automationId"/>, or <see langword="null"/>.</summary>
    public static AutomationElement? FindButton(AutomationBase automation, string automationId) =>
        NotificationArea.Taskbar(automation).FindFirstDescendant(cf => cf.ByAutomationId(automationId).And(cf.ByControlType(ControlType.Button)));

    public static async Task<AutomationElement> WaitForButtonAsync(AutomationBase automation)
    {
        AutomationElement? button = null;
        await Wait.UntilAsync(() => (button = FindButton(automation, TestButtonId)) is not null, TimeSpan.FromSeconds(10), "the taskbar button of the test app");
        return button!;
    }

    public static Task WaitForNoButtonAsync(AutomationBase automation) =>
        Wait.UntilAsync(() => FindButton(automation, TestButtonId) is null, TimeSpan.FromSeconds(10), "no taskbar button of the test app");

    /// <summary>
    /// Right-clicks <paramref name="button"/> with the real mouse (the pointer jumps there) and waits for the jump list in
    /// the foreground.
    /// </summary>
    public static async Task<JumpList> OpenAsync(AutomationBase automation, AutomationElement button)
    {
        Rectangle bounds = button.BoundingRectangle;
        Point center = new(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
        EnsureClickHitsTestButton(automation, bounds, center);
        Mouse.Position = center;
        Mouse.Click(MouseButton.Right);
        Native.GetCursorPos(out Native.Point cursor);
        if (!bounds.Contains(cursor.X, cursor.Y))
        {
            throw new InvalidOperationException(
                $"The mouse pointer left the taskbar button {bounds} right after the click (it is at {cursor.X},{cursor.Y}): someone used the mouse during the end-to-end run.");
        }

        nint hwnd = 0;
        await Wait.UntilAsync(
            () =>
            {
                nint foreground = Native.GetForegroundWindow();
                if (foreground == 0 || Native.ClassName(foreground) != JumpListClass || !IsShellExperienceHost(foreground))
                {
                    return false;
                }

                hwnd = foreground;
                return automation.FromHandle(foreground).FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem)) is not null;
            },
            TimeSpan.FromSeconds(10),
            "the jump list of the taskbar button");
        return new JumpList(automation, hwnd);
    }

    /// <summary>
    /// The real click goes where the screen is, not where UI Automation says the button is: an off-screen button (taskbar
    /// overflow, auto-hidden taskbar) has an empty or stale rectangle, and a window may cover the taskbar. The click must
    /// never land on the user's own taskbar button or window, so it is checked before the pointer moves: the point is on the
    /// taskbar and UI Automation finds the test app's button there.
    /// </summary>
    private static void EnsureClickHitsTestButton(AutomationBase automation, Rectangle bounds, Point center)
    {
        if (bounds.IsEmpty || !IsTestButtonAt(automation, center))
        {
            throw new InvalidOperationException(
                $"The taskbar button of the test app {bounds} is not where a click at {center.X},{center.Y} would land; not clicking (taskbar hidden, overflowing or covered).");
        }
    }

    /// <summary>Whether the screen at <paramref name="point"/> is the test app's taskbar button (not a stale rectangle, another button or a window over the taskbar).</summary>
    internal static bool IsTestButtonAt(AutomationBase automation, Point point)
    {
        nint taskbar = Native.FindWindow(NotificationArea.TaskbarClassName, null);
        if (taskbar == 0 || Native.TopLevelWindowAt(point) != taskbar)
        {
            return false;
        }

        // The element under the point, or one of its nearest ancestors, is the test app's button.
        AutomationElement? element = automation.FromPoint(point);
        for (int depth = 0; element is not null && depth < 4; depth++, element = element.Parent)
        {
            if (element.Properties.AutomationId.ValueOrDefault == TestButtonId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An open jump list.</summary>
    internal sealed class JumpList(AutomationBase automation, nint hwnd)
    {
        public nint Handle { get; } = hwnd;

        public AutomationElement Element { get; } = automation.FromHandle(hwnd);

        /// <summary>Every entry (list item) of every list of the jump list: tasks, recent or pinned items, system entries.</summary>
        public AutomationElement[] Entries() => Element.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));

        /// <summary>The lists of the jump list (automation ids such as <c>SystemItemList</c>).</summary>
        public string[] ListIds() =>
            [.. Element.FindAllDescendants(cf => cf.ByControlType(ControlType.List)).Select(static list => list.Properties.AutomationId.ValueOrDefault ?? string.Empty)];

        /// <summary>The window title (localized, e.g. "Jump List for …"), the owning process and the entries, for the report.</summary>
        public string Describe()
        {
            Native.GetWindowThreadProcessId(Handle, out uint processId);
            string process;
            try
            {
                using Process owner = Process.GetProcessById((int)processId);
                process = owner.ProcessName;
            }
            catch (ArgumentException)
            {
                process = "?";
            }

            IEnumerable<string> entries = Entries().Select(static entry => $"'{entry.Properties.Name.ValueOrDefault}' (id '{entry.Properties.AutomationId.ValueOrDefault}')");
            return $"window '{Native.Title(Handle)}' of {process}, lists [{string.Join(", ", ListIds())}], entries [{string.Join(", ", entries)}]";
        }

        /// <summary>
        /// The lists of the jump list captured from the screen (it is a shell window), exactly their rectangle: the shell
        /// window is taller than the menu it shows, and a margin or the union with the button would take in the user's
        /// windows and taskbar buttons around it.
        /// </summary>
        public async Task<string> ScreenshotAsync(string name)
        {
            // UI Automation reports the final place at once while the menu still slides in.
            await Task.Delay(TimeSpan.FromMilliseconds(700));
            Rectangle area = Rectangle.Empty;
            foreach (AutomationElement list in Element.FindAllDescendants(cf => cf.ByControlType(ControlType.List)))
            {
                Rectangle bounds = list.BoundingRectangle;
                area = area.IsEmpty ? bounds : Rectangle.Union(area, bounds);
            }

            return SaveScreenArea(area.IsEmpty ? Element.BoundingRectangle : area, 0, name);
        }

        public bool IsOpen => Native.IsWindowVisible(Handle) && Native.GetForegroundWindow() == Handle;

        /// <summary>
        /// Escape closes the jump list. Pressed again when the jump list is still open after a moment: right after it
        /// opened, the shell may not take keys yet.
        /// </summary>
        public async Task DismissAsync()
        {
            for (int attempt = 1; ; attempt++)
            {
                // Escape goes to the foreground window: only while that is still the jump list.
                if (!IsOpen)
                {
                    return;
                }

                Keyboard.Type(VirtualKeyShort.ESCAPE);
                try
                {
                    await Wait.UntilAsync(() => !IsOpen, TimeSpan.FromSeconds(2), "the jump list to close after Escape");
                    return;
                }
                catch (TimeoutException) when (attempt < 3)
                {
                }
            }
        }

        /// <summary>Runs the entry (Invoke when the entry offers it, otherwise Enter on the focused entry).</summary>
        public async Task RunAsync(AutomationElement entry)
        {
            if (entry.Patterns.Invoke.IsSupported)
            {
                entry.Patterns.Invoke.Pattern.Invoke();
            }
            else
            {
                entry.Focus();
                Keyboard.Type(VirtualKeyShort.RETURN);
            }

            await Wait.UntilAsync(() => !IsOpen, TimeSpan.FromSeconds(5), "the jump list to close after its entry ran");
        }
    }

    /// <summary>
    /// The taskbar button captured from the screen, with a small margin only: the neighbouring buttons belong to the
    /// user's own windows.
    /// </summary>
    public static string ScreenshotButton(AutomationElement button, string name) => SaveScreenArea(button.BoundingRectangle, 4, name);

    private static bool IsShellExperienceHost(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint processId);
        try
        {
            using Process owner = Process.GetProcessById((int)processId);
            return string.Equals(owner.ProcessName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string SaveScreenArea(Rectangle area, int margin, string name)
    {
        area.Inflate(margin, margin);
        Native.Rect screen = Native.VirtualScreen();
        area.Intersect(Rectangle.FromLTRB(screen.Left, screen.Top, screen.Right, screen.Bottom));
        using Bitmap bitmap = new(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size);
        }

        return Screenshots.Save(bitmap, name);
    }
}
