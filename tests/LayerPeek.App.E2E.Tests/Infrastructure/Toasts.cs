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
using FlaUI.UIA3;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The texts of one toast of the test app (T-068): the app name Windows shows in the header (the name of the test
/// shortcut), the printer name (title, unique per test) and the event text (body). Only elements whose names belong to
/// these texts are ever captured or reported, so nothing of other apps' notifications is recorded.
/// </summary>
internal sealed record ToastTexts(string PrinterName, string Body, IReadOnlyList<string>? Others = null)
{
    /// <summary>The app name in the toast header: the test shortcut "LayerPeek E2E" (T-067).</summary>
    public static string AppName { get; } = Path.GetFileNameWithoutExtension(AppSwitches.TestShortcutFileName);

    /// <summary>Longest name of a button that counts as a control label (close, settings) rather than content.</summary>
    internal const int MaxControlLabel = 40;

    /// <summary>
    /// Automation ids of the buttons of a toast itself (banner and notification center, Windows 11 26300): their localized
    /// labels name the action and the app and may be longer than <see cref="MaxControlLabel"/>, but they are controls.
    /// </summary>
    internal static IReadOnlyList<string> ChromeButtonIds { get; } = ["SettingsButton", "DismissButton", "ExpandButton"];

    /// <summary>Whether a name is one of this toast's texts (or a combination of them, like an item named after the toast).</summary>
    public bool IsOurs(string name) =>
        name.Contains(PrinterName, StringComparison.Ordinal)
        || name.Contains(Body, StringComparison.Ordinal)
        || name.Contains(AppName, StringComparison.Ordinal)
        || (Others ?? []).Any(other => name.Contains(other, StringComparison.Ordinal));

    /// <summary>
    /// Whether every named element of a subtree belongs to this toast: a foreign text means the subtree also holds
    /// something else (another notification, a time stamp, the window). Images, short button labels and the toast's own
    /// buttons (<see cref="ChromeButtonIds"/>) are controls, not content.
    /// </summary>
    public bool OnlyOurs(IEnumerable<(string ControlType, string Name, string AutomationId)> elements) =>
        elements.All(element =>
            element.Name.Length == 0
            || IsOurs(element.Name)
            || element.ControlType == nameof(FlaUI.Core.Definitions.ControlType.Image)
            || (element.ControlType is nameof(FlaUI.Core.Definitions.ControlType.Button) or nameof(FlaUI.Core.Definitions.ControlType.SplitButton)
                && (element.Name.Length <= MaxControlLabel || ChromeButtonIds.Contains(element.AutomationId, StringComparer.Ordinal))));

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
    public bool IsOneToast(IEnumerable<(string ControlType, string Name, string AutomationId)> elements)
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
/// <param name="Region">The anchor or its nearest ancestor that also holds the event text: the toast card (captured).</param>
/// <param name="Window">The top-level window it is in.</param>
internal sealed record ToastMatch(AutomationElement Anchor, AutomationElement Region, nint Window, IReadOnlyList<ToastElement> Elements)
{
    public Rectangle Bounds => Region.BoundingRectangle;

    /// <summary>Whether an element is named exactly <paramref name="text"/>.</summary>
    public bool HasExactly(string text) => Elements.Any(element => element.Name == text);

    /// <summary>
    /// Whether an element has <paramref name="text"/> as one whole line of its name: Windows 11 26300 shows the body
    /// lines of a toast (event text, file name) in one element, one per line.
    /// </summary>
    public bool HasLine(string text) => Elements.Any(element => IsLineOf(element.Name, text));

    internal static bool IsLineOf(string name, string line) =>
        name.Split('\n').Any(part => string.Equals(part.TrimEnd('\r'), line, StringComparison.Ordinal));

    /// <summary>
    /// The elements where they are once the region settled at <paramref name="settled"/>: their bounds were read when the
    /// toast was found, while a banner may still slide in, so they move by as much as the region (its own entry comes
    /// first) moved since.
    /// </summary>
    public IReadOnlyList<ToastElement> ElementsAt(Rectangle settled)
    {
        if (Elements.Count == 0)
        {
            return Elements;
        }

        Point found = Elements[0].Bounds.Location;
        Size moved = new(settled.X - found.X, settled.Y - found.Y);
        return [.. Elements.Select(element => element with { Bounds = element.Bounds.IsEmpty ? element.Bounds : new Rectangle(element.Bounds.Location + moved, element.Bounds.Size) })];
    }

    /// <summary>Whether a name of the region contains <paramref name="text"/>.</summary>
    public bool Mentions(string text) => Elements.Any(element => element.Name.Contains(text, StringComparison.Ordinal));

    public string Describe() => string.Join(Environment.NewLine, Elements.Select(static element => "  " + element));
}

/// <summary>
/// Toasts of the test app through UI Automation (T-068): banners in the windows of the Windows shell, the notification
/// center (opened from the taskbar clock), activation (UIA Invoke, otherwise the real mouse) and captures of the toast's own screen region only.
/// Banners are searched from the desktop root in the top-level windows of the shell processes, by the unique printer
/// name and the event text, never by localized window names.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Toasts
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    /// <summary>Waits for the banner of the toast; the failure lists the shell windows that appeared meanwhile (process, class; no titles).</summary>
    public static async Task<ToastMatch> WaitForBannerAsync(UIA3Automation automation, ToastTexts texts, TimeSpan timeout)
    {
        HashSet<nint> before = [.. TopLevelWindows(automation).Select(static window => window.Handle)];
        HashSet<string> appeared = [];
        string? seen = null;
        DateTime deadline = DateTime.UtcNow + timeout;
        DateTime nextDiagnosis = DateTime.UtcNow;
        while (true)
        {
            if (FindBanner(automation, texts) is { } banner)
            {
                return banner;
            }

            // A banner lives a few seconds: what was there is recorded while waiting (windows that appeared, and once an
            // element naming the printer that did not count as the banner).
            foreach ((nint handle, string process, string className, _) in TopLevelWindows(automation).Where(window => !before.Contains(window.Handle)))
            {
                appeared.Add($"  {process} class='{className}'");
            }

            if (seen is null && DateTime.UtcNow >= nextDiagnosis)
            {
                seen = Diagnose(automation, texts, shellOnly: true);
                nextDiagnosis = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"No toast banner with '{texts.PrinterName}' and '{texts.Body}' within {timeout.TotalSeconds:0} s in the windows of {string.Join(", ", NotificationCenter.ShellProcesses)} or class {NotificationCenter.CoreWindowClass}. "
                    + $"Top-level windows that appeared meanwhile (process, class):{Environment.NewLine}{string.Join(Environment.NewLine, appeared)}{Environment.NewLine}"
                    + $"Elements naming the printer seen while waiting:{Environment.NewLine}{seen ?? "  none"}");
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
    /// Opens the notification center (the taskbar clock, <see cref="NotificationCenter"/>), runs <paramref name="use"/> on its window and closes it again with Escape when
    /// it is still in the foreground (a click on a toast closes it by itself). Escape is never sent when the center did
    /// not come up: it would go to the window that has the focus.
    /// </summary>
    public static async Task<T> InNotificationCenterAsync<T>(UIA3Automation automation, Func<AutomationElement, nint, Task<T>> use)
    {
        (nint center, _) = await NotificationCenter.OpenAsync(automation);
        try
        {
            return await use(automation.FromHandle(center), center);
        }
        finally
        {
            await NotificationCenter.CloseAsync(center);
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
    /// <param name="mouseOnly">
    /// Only the real mouse: in the notification center of Windows 11 26300, UIA Invoke on the toast's list item closes the
    /// center without the click a user makes.
    /// </param>
    public static async Task<string> ActivateAsync(ToastMatch toast, Func<bool> opened, TimeSpan timeout, bool mouseOnly = false)
    {
        StringBuilder how = new();
        AutomationElement? invokable = mouseOnly ? null : Chain(toast).FirstOrDefault(static element => Safe(() => element.Patterns.Invoke.IsSupported));
        if (mouseOnly)
        {
            how.Append("mouse only; ");
        }
        else if (invokable is not null)
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
            // Gone = Windows took the click as an activation of the toast, but the app was not told.
            bool stillShown = Safe(() => toast.Anchor.Properties.BoundingRectangle.ValueOrDefault) is { Width: > 0, Height: > 0 }
                && !Safe(() => toast.Anchor.Properties.IsOffscreen.ValueOrDefault);
            throw new TimeoutException($"{how}: nothing opened within {timeout.TotalSeconds:0} s; the toast is {(stillShown ? "still shown" : "gone")} after the click.");
        }

        return how.ToString();
    }

    /// <summary>
    /// The screen region of the toast only, through the shared guard of screen captures (<see cref="ScreenArea"/>: one
    /// element on one monitor, at most a quarter of it, never the desktop).
    /// </summary>
    public static Bitmap Capture(Rectangle region) => ScreenArea.CaptureBitmap(region);

    /// <summary>
    /// Waits until the region has stopped moving for <see cref="SettleTime"/> (the banner and the notification center
    /// slide in; two reads 100 ms apart may still be equal mid-animation, which once captured a toast clipped to half its
    /// width), then a moment for the fade.
    /// </summary>
    public static async Task<Rectangle> SettledBoundsAsync(ToastMatch toast)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        Rectangle last = Bounds();
        DateTime since = DateTime.UtcNow;
        while (DateTime.UtcNow - since < SettleTime || last.Width <= 0 || last.Height <= 0)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"The toast did not stop moving within 10 s (last bounds {last}).");
            }

            await Task.Delay(Poll);
            Rectangle now = Bounds();
            if (now != last)
            {
                last = now;
                since = DateTime.UtcNow;
            }
        }

        await Task.Delay(300);
        return last;

        Rectangle Bounds() => Safe(() => toast.Region.Properties.BoundingRectangle.ValueOrDefault);
    }

    /// <summary>How long the bounds of a toast must stay the same before it is captured.</summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// Where the app icon of the header should be, in screen pixels: an image element of the toast near its top, otherwise
    /// the area left of the app name, otherwise the top left corner of the toast.
    /// </summary>
    /// <param name="settled">The bounds of the toast once it stopped moving (<see cref="SettledBoundsAsync"/>).</param>
    public static (Rectangle Area, string How) IconSearchArea(ToastMatch toast, Rectangle settled, double scale)
    {
        Rectangle region = settled;
        IReadOnlyList<ToastElement> elements = toast.ElementsAt(settled);
        int max = (int)Math.Round(48 * scale);
        int pad = (int)Math.Round(6 * scale);
        ToastElement? image = elements
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

        ToastElement? name = elements.FirstOrDefault(static element => element.Name == ToastTexts.AppName && element.Bounds.Height > 0);
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

    /// <summary>
    /// Why a toast was not found, for the failure message: every visible top-level window with an element naming the
    /// printer, and per such element whether it is on screen, its bounds, and what its ancestors hold (whether they are
    /// one toast; foreign names only by type, automation id, class and length). No foreign text is ever written.
    /// </summary>
    /// <param name="shellOnly">Only the windows of the shell (cheap enough to repeat while a banner may be shown); otherwise every process.</param>
    /// <returns>The report, or <see langword="null"/> when no window has an element naming the printer.</returns>
    public static string? Diagnose(UIA3Automation automation, ToastTexts texts, bool shellOnly = false)
    {
        StringBuilder report = new();
        foreach ((nint handle, string process, string className, bool isShell) in TopLevelWindows(automation).Where(window => !shellOnly || window.IsShell))
        {
            try
            {
                AutomationElement window = automation.FromHandle(handle);
                List<AutomationElement> anchors = [.. window.FindAllDescendants().Where(element => Name(element).Contains(texts.PrinterName, StringComparison.Ordinal))];
                if (anchors.Count == 0)
                {
                    continue;
                }

                report.AppendLine(CultureInfo.InvariantCulture, $"  window {process} class='{className}' shell={isShell}: {anchors.Count} element(s) naming the printer");
                foreach (AutomationElement anchor in anchors.Take(3))
                {
                    report.AppendLine(CultureInfo.InvariantCulture,
                        $"    {Describe(anchor)} offscreen={Safe(() => anchor.Properties.IsOffscreen.ValueOrDefault)} bounds={Safe(() => anchor.Properties.BoundingRectangle.ValueOrDefault)}");
                    int level = 0;
                    for (AutomationElement? parent = anchor.Parent; parent is not null && !parent.Equals(window) && level < 8; parent = parent.Parent, level++)
                    {
                        List<ToastElement> elements = ElementsOf(parent, texts);
                        List<string> foreign = [.. elements
                            .Where(element => !texts.OnlyOurs([(element.ControlType, element.Name, element.AutomationId)]))
                            .Select(static element => $"{element.ControlType} id='{element.AutomationId}' class='{element.ClassName}' ({element.Name.Length} chars)")];
                        report.AppendLine(CultureInfo.InvariantCulture,
                            $"      ancestor {level + 1}: {Describe(parent)}, {elements.Count} elements, body {(elements.Any(element => element.Name.Contains(texts.Body, StringComparison.Ordinal)) ? "found" : "missing")}, "
                            + $"one toast={texts.IsOneToast(elements.Select(static element => (element.ControlType, element.Name, element.AutomationId)))}, foreign names {foreign.Count}: {string.Join("; ", foreign.Take(8))}");
                    }
                }
            }
            catch (Exception ex) when (ex is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  window {process} class='{className}': not readable ({ex.GetType().Name})");
            }
        }

        return report.Length == 0 ? null : report.ToString().TrimEnd();
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
                if (RegionOf(anchor, window, texts) is ({ } region, { } elements))
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

    /// <summary>
    /// The toast around <paramref name="anchor"/>: the anchor or its nearest ancestor (below the window) that also holds
    /// the event text, if that is this one toast and nothing more (<see cref="ToastTexts.IsOneToast"/>). The nearest, not
    /// the largest: a larger container (the banner's scroll viewer) reaches past the toast's visible card, and a capture
    /// of it would show the windows behind.
    /// </summary>
    private static (AutomationElement Region, List<ToastElement> Elements)? RegionOf(AutomationElement anchor, AutomationElement window, ToastTexts texts)
    {
        for (AutomationElement? element = anchor; element is not null && !element.Equals(window); element = element.Parent)
        {
            List<ToastElement> elements = ElementsOf(element, texts);
            if (elements.Any(item => item.Name.Contains(texts.Body, StringComparison.Ordinal)))
            {
                return texts.IsOneToast(elements.Select(static item => (item.ControlType, item.Name, item.AutomationId))) ? (element, elements) : null;
            }
        }

        return null;
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

    /// <summary>A UI Automation call that timed out (UIA_E_TIMEOUT, as COM error or as FlaUI's <see cref="TimeoutException"/>).</summary>
    internal static bool IsUiaTimeout(Exception exception) =>
        exception is TimeoutException || (exception is COMException && IsUiaTimeoutCode(exception.HResult));

    internal static bool IsUiaTimeoutCode(int hresult) => hresult == UiaETimeout;

    /// <summary>UIA_E_TIMEOUT.</summary>
    private const int UiaETimeout = unchecked((int)0x80131505);

    /// <summary>Visible top-level windows: handle, process name and class (titles are never read).</summary>
    private static List<(nint Handle, string Process, string ClassName, bool IsShell)> TopLevelWindows(UIA3Automation automation)
    {
        List<(nint, string, string, bool)> windows = [];
        Dictionary<int, string> processes = [];
        AutomationElement[] children;
        try
        {
            children = automation.GetDesktop().FindAllChildren();
        }
        catch (Exception ex) when (IsUiaTimeout(ex))
        {
            // UIA busy for this one poll (run 2026-10-23: 0x80131505 while a banner slid in): nothing seen, the caller polls again.
            return windows;
        }

        foreach (AutomationElement window in children)
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
            windows.Add((handle, process, className, NotificationCenter.IsShell(process, className, id)));
        }

        return windows;
    }
}
