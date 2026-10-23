using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// The texts of one toast of the test app (T-068): the app name Windows shows in the header (the name of the test
/// shortcut), the printer name (title, unique per test) and the event text (body). Only elements whose names belong to
/// these texts are ever captured or reported, so nothing of other apps' notifications is recorded.
/// </summary>
internal sealed record ToastTexts(string PrinterName, string Body, IReadOnlyList<string>? Others = null)
{
    /// <summary>The app name in the toast header: the test shortcut "Prusa Tray Diagnostic E2E" (T-067).</summary>
    public static string AppName { get; } = Path.GetFileNameWithoutExtension(AppSwitches.TestShortcutFileName);

    /// <summary>Longest name of a button that counts as a control label (close, settings) rather than content.</summary>
    internal const int MaxControlLabel = 40;

    /// <summary>Whether a name is one of this toast's texts (or a combination of them, like an item named after the toast).</summary>
    public bool IsOurs(string name) =>
        name.Contains(PrinterName, StringComparison.Ordinal)
        || name.Contains(Body, StringComparison.Ordinal)
        || name.Contains(AppName, StringComparison.Ordinal)
        || (Others ?? []).Any(other => name.Contains(other, StringComparison.Ordinal));

    /// <summary>
    /// Whether every named element of a subtree belongs to this toast: a foreign text means the subtree also holds
    /// something else (another notification, a time stamp, the window). Images and short button labels (close,
    /// settings) are controls, not content.
    /// </summary>
    public bool OnlyOurs(IEnumerable<(string ControlType, string Name)> elements) =>
        elements.All(element =>
            element.Name.Length == 0
            || IsOurs(element.Name)
            || element.ControlType == nameof(FlaUI.Core.Definitions.ControlType.Image)
            || (element.ControlType is nameof(FlaUI.Core.Definitions.ControlType.Button) or nameof(FlaUI.Core.Definitions.ControlType.SplitButton)
                && element.Name.Length <= MaxControlLabel));

    /// <summary>Most foreign names a region of one toast may hold (its time stamp in the notification center, "now").</summary>
    internal const int MaxForeignNames = 2;

    /// <summary>Longest foreign name a region of one toast may hold (a time stamp, not a notification text).</summary>
    internal const int MaxForeignName = 20;

    /// <summary>
    /// Whether a found region is this one toast and nothing more, so that it may be captured and reported: besides the
    /// toast's own texts (<see cref="OnlyOurs"/>) at most <see cref="MaxForeignNames"/> short names such as a time stamp.
    /// A region with more (e.g. a container whose name happens to hold the printer name) may show other apps'
    /// notifications and is never used.
    /// </summary>
    public bool IsOneToast(IEnumerable<(string ControlType, string Name)> elements)
    {
        List<string> foreign = [.. elements.Where(element => !OnlyOurs([element])).Select(static element => element.Name)];
        return foreign.Count <= MaxForeignNames && foreign.All(static name => name.Length <= MaxForeignName);
    }
}

/// <summary>One element of a toast for the results (only elements of the toast's own region).</summary>
/// <param name="IsOurs">
/// Whether <paramref name="Name"/> is one of the toast's own texts (<see cref="ToastTexts.IsOurs"/>); any other name (a time
/// stamp, a control label) is never written to the results, only its length.
/// </param>
internal sealed record ToastElement(string ControlType, string Name, string AutomationId, string ClassName, Rectangle Bounds, bool IsOurs)
{
    public override string ToString() =>
        $"{ControlType} {(IsOurs || Name.Length == 0 ? $"'{Name}'" : $"(other name, {Name.Length} chars)")} id='{AutomationId}' class='{ClassName}' bounds={Bounds.X},{Bounds.Y} {Bounds.Width}×{Bounds.Height}";
}

/// <summary>A toast of the test app found on screen (banner or notification center).</summary>
/// <param name="Anchor">The element naming the printer (the title, or an element whose name holds it).</param>
/// <param name="Region">The largest ancestor of <paramref name="Anchor"/> with only this toast's texts.</param>
/// <param name="Window">The top-level window it is in.</param>
internal sealed record ToastMatch(AutomationElement Anchor, AutomationElement Region, nint Window, IReadOnlyList<ToastElement> Elements)
{
    public Rectangle Bounds => Region.BoundingRectangle;

    /// <summary>Whether an element is named exactly <paramref name="text"/>.</summary>
    public bool HasExactly(string text) => Elements.Any(element => element.Name == text);

    /// <summary>Whether a name of the region contains <paramref name="text"/>.</summary>
    public bool Mentions(string text) => Elements.Any(element => element.Name.Contains(text, StringComparison.Ordinal));

    public string Describe() => string.Join(Environment.NewLine, Elements.Select(static element => "  " + element));
}

/// <summary>
/// Toasts of the test app through UI Automation (T-068): banners in the windows of the Windows shell, the notification
/// center (Win+N), activation (UIA Invoke, otherwise the real mouse) and captures of the toast's own screen region only.
/// Banners are searched from the desktop root in the top-level windows of the shell processes, by the unique printer
/// name and the event text, never by localized window names.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Toasts
{
    /// <summary>Processes of the Windows 11 shell that host toast banners and the notification center.</summary>
    private static readonly string[] ShellProcesses = ["ShellExperienceHost", "ShellHost"];

    /// <summary>The window class of the shell's XAML flyouts.</summary>
    private const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    /// <summary>Waits for the banner of the toast; the failure lists the shell windows that appeared meanwhile (process, class; no titles).</summary>
    public static async Task<ToastMatch> WaitForBannerAsync(UIA3Automation automation, ToastTexts texts, TimeSpan timeout)
    {
        HashSet<nint> before = [.. TopLevelWindows(automation).Select(static window => window.Handle)];
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (FindBanner(automation, texts) is { } banner)
            {
                return banner;
            }

            if (DateTime.UtcNow >= deadline)
            {
                IEnumerable<string> appeared = TopLevelWindows(automation)
                    .Where(window => !before.Contains(window.Handle))
                    .Select(static window => $"  {window.Process} class='{window.ClassName}'");
                throw new TimeoutException(
                    $"No toast banner with '{texts.PrinterName}' and '{texts.Body}' within {timeout.TotalSeconds:0} s in the windows of {string.Join(", ", ShellProcesses)} or class {CoreWindowClass}. "
                    + $"Top-level windows that appeared meanwhile (process, class):{Environment.NewLine}{string.Join(Environment.NewLine, appeared)}");
            }

            await Task.Delay(Poll);
        }
    }

    /// <summary>The banner, or <see langword="null"/>; the notification center must be closed (it would match too).</summary>
    public static ToastMatch? FindBanner(UIA3Automation automation, ToastTexts texts)
    {
        foreach ((nint handle, _, _, _) in TopLevelWindows(automation).Where(static window => window.IsShell))
        {
            if (TryFind(automation, handle, texts) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>Waits until the banner is gone (it expired); the toast stays in the notification center.</summary>
    public static async Task WaitForBannerGoneAsync(UIA3Automation automation, ToastTexts texts, TimeSpan timeout) =>
        await Wait.UntilAsync(() => FindBanner(automation, texts) is null, timeout, $"the banner of '{texts.PrinterName}' to expire (Settings > Accessibility > Visual effects > Dismiss notifications after)");

    /// <summary>
    /// How long a banner stays: <c>HKCU\Control Panel\Accessibility\MessageDuration</c> (seconds, default 5; Settings &gt;
    /// Accessibility &gt; Visual effects), plus a margin for the animation.
    /// </summary>
    public static TimeSpan BannerLifetime()
    {
        using Microsoft.Win32.RegistryKey? accessibility = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Accessibility");
        int seconds = accessibility?.GetValue("MessageDuration") is int value && value > 0 ? value : 5;
        return TimeSpan.FromSeconds(seconds + 20);
    }

    /// <summary>
    /// Opens the notification center (Win+N), runs <paramref name="use"/> on its window and closes it again with Escape when
    /// it is still in the foreground (a click on a toast closes it by itself). Escape is never sent when the center did
    /// not come up: it would go to the window that has the focus.
    /// </summary>
    public static async Task<T> InNotificationCenterAsync<T>(UIA3Automation automation, Func<AutomationElement, nint, Task<T>> use)
    {
        nint before = Native.GetForegroundWindow();
        Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.KEY_N);
        nint center = 0;
        try
        {
            await Wait.UntilAsync(
                () =>
                {
                    // Only a window of the shell: anything else that comes to the foreground meanwhile is not the center.
                    nint foreground = Native.GetForegroundWindow();
                    if (foreground != 0 && foreground != before && IsShellWindow(foreground))
                    {
                        center = foreground;
                        return true;
                    }

                    return false;
                },
                TimeSpan.FromSeconds(8),
                "the notification center (Win+N) in the foreground (a window of the shell)");
            return await use(automation.FromHandle(center), center);
        }
        finally
        {
            if (center != 0 && Native.GetForegroundWindow() == center)
            {
                Keyboard.Type(VirtualKeyShort.ESCAPE);
                await Task.Delay(500);
            }
        }
    }

    /// <summary>Waits for the toast in a window (the notification center); <see langword="null"/> when it is not there.</summary>
    public static async Task<ToastMatch?> FindInAsync(UIA3Automation automation, nint window, ToastTexts texts, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (TryFind(automation, window, texts) is { } match)
            {
                return match;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(Poll);
        }
    }

    /// <summary>
    /// Whether any element of <paramref name="window"/> names the printer (a toast of it is there, whatever its text);
    /// for the check that the cleanup removed the test app's toasts.
    /// </summary>
    public static bool MentionsPrinter(UIA3Automation automation, nint window, string printerName)
    {
        try
        {
            return automation.FromHandle(window).FindAllDescendants().Any(element => Name(element).Contains(printerName, StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
        {
            return false;
        }
    }

    /// <summary>
    /// Activates the toast like a click: UIA Invoke on the title or an ancestor of it inside the toast (never on another
    /// control, e.g. its close button), and when that opens nothing, the real mouse on the title, if the toast is still
    /// there. Returns how it was activated; fails when nothing opened.
    /// </summary>
    public static async Task<string> ActivateAsync(ToastMatch toast, Func<bool> opened, TimeSpan timeout)
    {
        StringBuilder how = new();
        AutomationElement? invokable = Chain(toast).FirstOrDefault(static element => Safe(() => element.Patterns.Invoke.IsSupported));
        if (invokable is not null)
        {
            how.Append(CultureInfo.InvariantCulture, $"UIA Invoke on {Describe(invokable)}");
            try
            {
                invokable.Patterns.Invoke.Pattern.Invoke();
            }
            catch (Exception ex) when (ex is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
            {
                how.Append(CultureInfo.InvariantCulture, $" failed ({ex.GetType().Name} 0x{ex.HResult:X8})");
            }

            if (await OpenedAsync(opened, timeout))
            {
                return how.ToString();
            }

            how.Append(" opened nothing; ");
        }
        else
        {
            how.Append("no element of the toast supports Invoke; ");
        }

        // Only onto the toast itself: when it went away, a click would hit whatever is under it.
        Rectangle bounds = Safe(() => toast.Anchor.Properties.BoundingRectangle.ValueOrDefault);
        if (bounds.Width <= 0 || bounds.Height <= 0 || Safe(() => toast.Anchor.Properties.IsOffscreen.ValueOrDefault))
        {
            throw new InvalidOperationException($"{how}the toast is gone, so the mouse cannot click it.");
        }

        Point center = new(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
        Mouse.Position = center;
        Mouse.Click(MouseButton.Left);
        how.Append(CultureInfo.InvariantCulture, $"real mouse click at {center.X},{center.Y} (the title)");
        if (!await OpenedAsync(opened, timeout))
        {
            throw new TimeoutException($"{how}: nothing opened within {timeout.TotalSeconds:0} s.");
        }

        return how.ToString();
    }

    /// <summary>The screen region of the toast only (never the whole desktop).</summary>
    public static Bitmap Capture(Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new InvalidOperationException($"The toast has no area to capture ({region}).");
        }

        Bitmap bitmap = new(region.Width, region.Height, PixelFormat.Format32bppArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Waits until the region stops moving (the banner slides in), then a moment for the fade.</summary>
    public static async Task<Rectangle> SettledBoundsAsync(ToastMatch toast)
    {
        Rectangle bounds = await Wait.StableAsync(() => Safe(() => toast.Region.Properties.BoundingRectangle.ValueOrDefault), TimeSpan.FromSeconds(5), "the toast to stop moving");
        await Task.Delay(400);
        return bounds;
    }

    /// <summary>
    /// Where the app icon of the header should be, in screen pixels: an image element of the toast near its top, otherwise
    /// the area left of the app name, otherwise the top left corner of the toast.
    /// </summary>
    public static (Rectangle Area, string How) IconSearchArea(ToastMatch toast, double scale)
    {
        Rectangle region = toast.Bounds;
        int max = (int)Math.Round(48 * scale);
        int pad = (int)Math.Round(6 * scale);
        ToastElement? image = toast.Elements
            .Where(element => element.ControlType == nameof(ControlType.Image)
                && element.Bounds.Width is > 6 && element.Bounds.Width <= max
                && Math.Abs(element.Bounds.Width - element.Bounds.Height) <= 4
                && element.Bounds.Top < region.Top + (region.Height / 2))
            .OrderBy(static element => element.Bounds.Top)
            .ThenBy(static element => element.Bounds.Left)
            .FirstOrDefault();
        if (image is not null)
        {
            Rectangle area = Rectangle.Inflate(image.Bounds, pad, pad);
            return (Rectangle.Intersect(area, region), $"image element {image}");
        }

        ToastElement? name = toast.Elements.FirstOrDefault(static element => element.Name == ToastTexts.AppName && element.Bounds.Height > 0);
        if (name is not null)
        {
            int height = name.Bounds.Height;
            Rectangle area = Rectangle.FromLTRB(name.Bounds.Left - (3 * height), name.Bounds.Top - height, name.Bounds.Left, name.Bounds.Bottom + height);
            return (Rectangle.Intersect(area, region), $"left of the app name {name}");
        }

        Rectangle corner = new(region.Left, region.Top, Math.Min(region.Width, (int)Math.Round(120 * scale)), Math.Min(region.Height, (int)Math.Round(60 * scale)));
        return (corner, "top left corner of the toast (no image element, no app name element)");
    }

    /// <summary>DPI scale (DPI / 96) of a window.</summary>
    public static double ScaleOf(nint window)
    {
        uint dpi = Native.GetDpiForWindow(window);
        return dpi == 0 ? 1 : dpi / 96.0;
    }

    /// <summary>The toast in one window: an element naming the printer, whose region also holds the event text.</summary>
    private static ToastMatch? TryFind(UIA3Automation automation, nint handle, ToastTexts texts)
    {
        try
        {
            AutomationElement window = automation.FromHandle(handle);
            // On screen only: a hidden flyout (the closed notification center) may keep its items in the tree.
            foreach (AutomationElement anchor in window.FindAllDescendants().Where(element =>
                Name(element).Contains(texts.PrinterName, StringComparison.Ordinal)
                && !Safe(() => element.Properties.IsOffscreen.ValueOrDefault)
                && Safe(() => element.Properties.BoundingRectangle.ValueOrDefault) is { Width: > 0, Height: > 0 }))
            {
                AutomationElement region = RegionOf(anchor, window, texts);
                List<ToastElement> elements = ElementsOf(region, texts);
                if (elements.Any(element => element.Name.Contains(texts.Body, StringComparison.Ordinal))
                    && texts.IsOneToast(elements.Select(static element => (element.ControlType, element.Name))))
                {
                    return new ToastMatch(anchor, region, handle, elements);
                }
            }
        }
        // The flyout is being built or torn down (COMException, FlaUI's ElementNotAvailableException): not there (yet).
        catch (Exception ex) when (ex is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
        {
        }

        return null;
    }

    /// <summary>The largest ancestor of <paramref name="anchor"/> (below the window) whose names all belong to the toast.</summary>
    private static AutomationElement RegionOf(AutomationElement anchor, AutomationElement window, ToastTexts texts)
    {
        AutomationElement region = anchor;
        for (AutomationElement? parent = anchor.Parent; parent is not null && !parent.Equals(window); parent = parent.Parent)
        {
            if (!texts.OnlyOurs(ElementsOf(parent, texts).Select(static element => (element.ControlType, element.Name))))
            {
                break;
            }

            region = parent;
        }

        return region;
    }

    /// <summary>The anchor and its ancestors up to the region.</summary>
    private static IEnumerable<AutomationElement> Chain(ToastMatch toast)
    {
        for (AutomationElement? element = toast.Anchor; element is not null; element = element.Parent)
        {
            yield return element;
            if (element.Equals(toast.Region))
            {
                yield break;
            }
        }
    }

    /// <summary>The element and its descendants.</summary>
    private static List<ToastElement> ElementsOf(AutomationElement root, ToastTexts texts) =>
        [.. new[] { root }.Concat(root.FindAllDescendants()).Select(element =>
        {
            string name = Name(element);
            return new ToastElement(
                Safe(() => element.Properties.ControlType.ValueOrDefault.ToString()),
                name,
                Safe(() => element.Properties.AutomationId.ValueOrDefault) ?? string.Empty,
                Safe(() => element.Properties.ClassName.ValueOrDefault) ?? string.Empty,
                Safe(() => element.Properties.BoundingRectangle.ValueOrDefault),
                texts.IsOurs(name));
        })];

    private static string Describe(AutomationElement element) =>
        $"{Safe(() => element.Properties.ControlType.ValueOrDefault.ToString())} id='{Safe(() => element.Properties.AutomationId.ValueOrDefault)}' class='{Safe(() => element.Properties.ClassName.ValueOrDefault)}'";

    private static string Name(AutomationElement element) => Safe(() => element.Properties.Name.ValueOrDefault) ?? string.Empty;

    private static T Safe<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException or FlaUI.Core.Exceptions.PropertyNotSupportedException)
        {
            return default!;
        }
    }

    private static async Task<bool> OpenedAsync(Func<bool> opened, TimeSpan timeout)
    {
        try
        {
            await Wait.UntilAsync(opened, timeout, "the click to open the printer web");
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Visible top-level windows: handle, process name and class (titles are never read).</summary>
    private static List<(nint Handle, string Process, string ClassName, bool IsShell)> TopLevelWindows(UIA3Automation automation)
    {
        List<(nint, string, string, bool)> windows = [];
        Dictionary<int, string> processes = [];
        foreach (AutomationElement window in automation.GetDesktop().FindAllChildren())
        {
            nint handle = Safe(() => window.Properties.NativeWindowHandle.ValueOrDefault);
            int id = Safe(() => window.Properties.ProcessId.ValueOrDefault);
            if (handle == 0 || !Native.IsWindowVisible(handle))
            {
                continue;
            }

            if (!processes.TryGetValue(id, out string? process))
            {
                try
                {
                    using Process owner = System.Diagnostics.Process.GetProcessById(id);
                    process = owner.ProcessName;
                }
                catch (ArgumentException)
                {
                    process = "(gone)";
                }

                processes[id] = process;
            }

            string className = Safe(() => window.Properties.ClassName.ValueOrDefault) ?? string.Empty;
            windows.Add((handle, process, className, IsShell(process, className, id)));
        }

        return windows;
    }

    /// <summary>A window of the shell processes, or a XAML flyout window outside Explorer and this process.</summary>
    private static bool IsShell(string process, string className, int processId) =>
        ShellProcesses.Contains(process, StringComparer.OrdinalIgnoreCase)
        || (className == CoreWindowClass && !string.Equals(process, "explorer", StringComparison.OrdinalIgnoreCase) && processId != Environment.ProcessId);

    /// <summary>Whether a window (the foreground one after Win+N) belongs to the shell (see <see cref="IsShell"/>).</summary>
    private static bool IsShellWindow(nint handle)
    {
        Native.GetWindowThreadProcessId(handle, out uint id);
        try
        {
            using Process owner = System.Diagnostics.Process.GetProcessById((int)id);
            return IsShell(owner.ProcessName, Native.ClassName(handle), (int)id);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
