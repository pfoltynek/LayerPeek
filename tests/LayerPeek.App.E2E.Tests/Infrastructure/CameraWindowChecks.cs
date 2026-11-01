using System.Drawing;
using System.Globalization;
using System.Text;

using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// Shared checks of the camera window's pin, context menu, taskbar button and z-order on the real app (T-120: the manual
/// checks of T-102, T-117 and T-118 automated). Only windows of the app and of the test process are clicked or typed into;
/// keys go to the app's own foreground window (never a global shortcut).
/// </summary>
internal static class CameraWindowChecks
{
    public const string PinId = "CameraTopmostButton";

    public const string TopmostMenuItemId = "CameraTopmostMenuItem";

    public const string LayoutMenuItemId = "CameraLayoutMenuItem";

    /// <summary>The window buttons that hide without the pointer (the pin stays dimmed while it is on, T-117).</summary>
    public static readonly string[] OtherWindowButtons = ["CameraLayoutButton", "CameraFullScreenButton", "CameraNormalButton", "CameraMiniButton"];

    /// <summary>How far the background / cover windows reach past the camera window, so that a strip of them stays free.</summary>
    public const int BackgroundMargin = 48;

    /// <summary>Background behind a camera window: a light desktop (white wallpaper, Explorer in light mode).</summary>
    public static readonly Color LightBackground = Color.FromArgb(255, 255, 255);

    /// <summary>Background behind a camera window: a dark desktop (dark wallpaper, a dark window).</summary>
    public static readonly Color DarkBackground = Color.FromArgb(0x20, 0x20, 0x20);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>The text of a resource of the app (English), e.g. <c>CameraTopmostOn</c>.</summary>
    public static string Text(string key) => AppStrings.Get("en", key);

    /// <summary>
    /// A window of the test process in <paramref name="color"/> laid right below <paramref name="window"/>, reaching
    /// <see cref="BackgroundMargin"/> past it: what is behind the camera window, never covering it, and a place for the
    /// pointer beside the window that belongs to the test.
    /// </summary>
    public static async Task<CoverWindow> BackgroundBelowAsync(nint window, Color color)
    {
        CoverWindow background = await CoverWindow.CreateAsync(CoverWindow.BoundsFor(Native.FrameBounds(window), Native.MonitorOf(window).Monitor, BackgroundMargin), color);
        background.PutBelow(window);
        return background;
    }

    /// <summary>
    /// Moves the real pointer off <paramref name="window"/> onto the strip of <paramref name="background"/> left (or right)
    /// of it, so that the window loses its hover state; returns the point. The pointer glides there in small steps like a
    /// user's hand: a single jump straight out of the window right after it was hidden and shown again (the taskbar button
    /// coming back, T-102) leaves Avalonia without a mouse move to arm its leave tracking, so the window would keep its
    /// hover state (T-120 run 2026-10-31: all buttons still drawn after unpinning; a real pointer never jumps).
    /// </summary>
    public static async Task<Point> MovePointerBesideAsync(nint window, CoverWindow background)
    {
        Native.Rect frame = Native.FrameBounds(window);
        int y = frame.Top + (frame.Height / 2);
        Point[] candidates = [new(frame.Left - (BackgroundMargin / 2), y), new(frame.Right + (BackgroundMargin / 2), y), new(frame.Left + (frame.Width / 2), frame.Top - (BackgroundMargin / 2)), new(frame.Left + (frame.Width / 2), frame.Bottom + (BackgroundMargin / 2))];
        Point? point = candidates.Cast<Point?>().FirstOrDefault(candidate => Native.TopLevelWindowAt(candidate!.Value) == background.Handle)
            ?? throw new InvalidOperationException($"No strip of the background window 0x{background.Handle:X} beside the camera window {frame} is free for the pointer.");
        await GlideAsync(point.Value);
        // The window buttons and the outline fade in 0.15 s.
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        return point.Value;
    }

    /// <summary>Moves the real pointer to <paramref name="target"/> in 12 steps of 15 ms (only hovering, no button).</summary>
    public static async Task GlideAsync(Point target)
    {
        Point start = Mouse.Position;
        for (int step = 1; step <= 12; step++)
        {
            Mouse.Position = new Point(start.X + ((target.X - start.X) * step / 12), start.Y + ((target.Y - start.Y) * step / 12));
            await Task.Delay(TimeSpan.FromMilliseconds(15), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Moves the real pointer onto <paramref name="point"/> of <paramref name="window"/> (checked to be that window there)
    /// and lets the hover fades run (<paramref name="settle"/>, default 0.5 s).
    /// </summary>
    public static async Task MovePointerOntoAsync(nint window, Point point, TimeSpan? settle = null)
    {
        Assert.True(Native.TopLevelWindowAt(point) == window, $"The point {point} is not on the camera window 0x{window:X} (0x{Native.TopLevelWindowAt(point):X} '{Native.ClassName(Native.TopLevelWindowAt(point))}' is there).");
        Mouse.Position = point;
        await Task.Delay(settle ?? TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
    }

    /// <summary>The middle of the image of the camera window (the client area without the mini preview's strip).</summary>
    public static Point ImageMiddle(nint window, bool mini)
    {
        Native.Rect client = Native.VisibleClientRect(window);
        int imageHeight = mini ? (int)Math.Round(client.Width * 9.0 / 16) : client.Height;
        return new Point(client.Left + (client.Width / 2), client.Top + (imageHeight / 2));
    }

    public static Point Center(Rectangle bounds) => new(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));

    /// <summary>Clicks the pin with the real mouse (the pointer first rests on it, like a user's).</summary>
    public static async Task ClickPinAsync(nint window, AutomationElement element)
    {
        Point point = Center((await Ui.FindAsync(element, PinId)).BoundingRectangle);
        await MovePointerOntoAsync(window, point);
        Mouse.Click(MouseButton.Left);
    }

    /// <summary>
    /// Clicks <paramref name="item"/> of the open context menu with the real mouse (checked to hit the menu's own window),
    /// as a user chooses it.
    /// </summary>
    public static async Task ClickMenuItemAsync(CameraContextMenu menu, AutomationElement item)
    {
        Point point = Center(item.BoundingRectangle);
        Assert.True(Native.TopLevelWindowAt(point) == menu.Handle, $"The menu item {item.BoundingRectangle} is not on the context menu's window 0x{menu.Handle:X}.");
        Mouse.Position = point;
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        Mouse.Click(MouseButton.Left);
    }

    /// <summary>What a screen reader gets from the pin: shown, control type, name and toggle state.</summary>
    public static PinState Pin(AutomationElement element)
    {
        AutomationElement? pin = Ui.TryFind(element, PinId);
        if (pin is null || pin.Properties.IsOffscreen.ValueOrDefault)
        {
            return new PinState(false, ControlType.Unknown, string.Empty, null, Rectangle.Empty);
        }

        ToggleState? state = pin.Patterns.Toggle.TryGetPattern(out FlaUI.Core.Patterns.ITogglePattern? toggle) && toggle is not null ? toggle.ToggleState.ValueOrDefault : null;
        return new PinState(true, pin.Properties.ControlType.ValueOrDefault, pin.Properties.Name.ValueOrDefault ?? string.Empty, state, pin.BoundingRectangle);
    }

    /// <summary>Waits until the pin says <paramref name="on"/> to UI Automation (toggle state and name) and the window is (not) always on top.</summary>
    public static async Task ExpectPinnedAsync(nint window, AutomationElement element, bool on, string context)
    {
        try
        {
            await Wait.UntilAsync(
                () => CameraWindowSettings.IsTopmost(window) == on
                    && Pin(element) is { Shown: true } pin && pin.Toggle == (on ? ToggleState.On : ToggleState.Off) && pin.Name == Text(on ? "CameraTopmostOn" : "CameraTopmostOff"),
                Timeout,
                $"{context}: always on top {(on ? "on" : "off")}");
        }
        catch (WaitTimeoutException timeout)
        {
            throw new WaitTimeoutException($"{timeout.Message} Now: WS_EX_TOPMOST {CameraWindowSettings.IsTopmost(window)}, pin {Pin(element)}.", timeout);
        }
    }

    /// <summary>
    /// The taskbar button of the window: the documented Win32 rule (<see cref="Native.HasTaskbarButton"/>) and, when
    /// <paramref name="onlyWindow"/> (no other window of the app is open), the test app's real button on the taskbar of the
    /// primary monitor through UI Automation.
    /// </summary>
    public static async Task ExpectTaskbarButtonAsync(AppInstance app, nint window, bool button, bool onlyWindow, string context)
    {
        try
        {
            await Wait.UntilAsync(() => Native.HasTaskbarButton(window) == button, Timeout, $"{context}: {(button ? "a" : "no")} taskbar button by the Win32 rule");
        }
        catch (WaitTimeoutException timeout)
        {
            throw new WaitTimeoutException($"{timeout.Message} Now: owner 0x{Native.GetWindow(window, Native.GwOwner):X}, ex style 0x{Native.ExStyle(window):X}, visible {Native.IsWindowVisible(window)}.", timeout);
        }

        if (!onlyWindow)
        {
            return;
        }

        if (button)
        {
            await TaskbarJumpList.WaitForButtonAsync(app.Automation);
        }
        else
        {
            await TaskbarJumpList.WaitForNoButtonAsync(app.Automation);
        }
    }

    /// <summary>
    /// Creates a cover window of the test process over <paramref name="camera"/>, activates it like a user switching to
    /// another app and checks which of the two is in front in the middle of the camera window (WindowFromPoint and the
    /// z-order); the cover window is closed again. Returns a line for the report.
    /// </summary>
    public static async Task<string> ExpectOrderAsync(nint camera, bool cameraInFront, string label)
    {
        Native.Rect frame = Native.FrameBounds(camera);
        Native.Rect client = Native.VisibleClientRect(camera);
        Point middle = new(client.Left + (client.Width / 2), client.Top + (client.Height / 4));
        using CoverWindow cover = await CoverWindow.CreateAsync(CoverWindow.BoundsFor(frame, Native.MonitorOf(camera).Monitor, BackgroundMargin));
        await cover.BringToFrontAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        nint front = Native.TopLevelWindowAt(middle);
        bool above = CoverWindow.IsAbove(camera, cover.Handle);
        string line = string.Create(CultureInfo.InvariantCulture, $"{label}: camera 0x{camera:X} topmost {CameraWindowSettings.IsTopmost(camera)}, cover 0x{cover.Handle:X}; at {middle.X},{middle.Y} 0x{front:X}; camera above the cover: {above}");
        Assert.True(front == (cameraInFront ? camera : cover.Handle), $"{line}: expected the {(cameraInFront ? "camera window" : "other app's window")} in front.");
        Assert.True(above == cameraInFront, $"{line}: wrong z-order.");
        return line;
    }

    /// <summary>
    /// The brightest pixel (mean of the channels) of <paramref name="screen"/> (screen coordinates) in a capture of the
    /// window made with <see cref="Screenshots.Capture"/>.
    /// </summary>
    public static int Brightest(Bitmap capture, nint window, Rectangle screen)
    {
        Native.GetWindowRect(window, out Native.Rect rect);
        int brightest = 0;
        for (int y = Math.Max(0, screen.Top - rect.Top); y < Math.Min(capture.Height, screen.Bottom - rect.Top); y++)
        {
            for (int x = Math.Max(0, screen.Left - rect.Left); x < Math.Min(capture.Width, screen.Right - rect.Left); x++)
            {
                Color c = capture.GetPixel(x, y);
                brightest = Math.Max(brightest, (c.R + c.G + c.B) / 3);
            }
        }

        return brightest;
    }

    /// <summary>
    /// The pin and the other window buttons as they are drawn now (the brightest pixel of each; the image is black without
    /// a picture): the pin dimmed while it is on and the pointer is away, hidden while it is off; the others hidden.
    /// </summary>
    public static ButtonLook Look(nint window, AutomationElement element)
    {
        using Bitmap capture = Screenshots.Capture(window);
        Rectangle pin = Ui.TryFind(element, PinId)?.BoundingRectangle ?? Rectangle.Empty;
        int others = 0;
        foreach (string id in OtherWindowButtons)
        {
            if (Ui.TryFind(element, id) is { } button && !button.Properties.IsOffscreen.ValueOrDefault)
            {
                others = Math.Max(others, Brightest(capture, window, button.BoundingRectangle));
            }
        }

        return new ButtonLook(pin.IsEmpty ? 0 : Brightest(capture, window, pin), others);
    }

    /// <summary>Focuses an element of the window with Tab (the window must be in the foreground; at most 12 presses).</summary>
    public static async Task FocusWithTabAsync(AutomationBase automation, nint window, string automationId)
    {
        List<string> seen = [];
        for (int i = 0; i < 12; i++)
        {
            string focused = automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault ?? string.Empty;
            if (focused == automationId)
            {
                return;
            }

            seen.Add(focused);
            WindowActivation.TypeInto(window, VirtualKeyShort.TAB);
            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Tab never focused {automationId} (focused: {string.Join(" → ", seen)}).");
    }

    /// <summary>The automation id of the focused element.</summary>
    public static string Focused(AutomationBase automation) => automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault ?? string.Empty;

    /// <summary>
    /// The open context menu of the camera window (an Avalonia popup, a window of its own): the window and its shown menu
    /// items, waited for after it was opened by any means.
    /// </summary>
    public static async Task<CameraContextMenu> WaitForContextMenuAsync(AppInstance app, nint camera)
    {
        CameraContextMenu? menu = null;
        await Wait.UntilAsync(() => (menu = FindContextMenu(app, camera)) is not null, Timeout, "the context menu of the camera window");
        return menu!;
    }

    public static CameraContextMenu? FindContextMenu(AppInstance app, nint camera)
    {
        foreach (nint hwnd in app.Windows().Where(hwnd => hwnd != camera))
        {
            try
            {
                AutomationElement root = app.Element(hwnd);
                if (root.FindFirstDescendant(cf => cf.ByAutomationId(TopmostMenuItemId)) is { } topmost && !topmost.Properties.IsOffscreen.ValueOrDefault)
                {
                    AutomationElement[] items = [.. root.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)).Where(static item => !item.Properties.IsOffscreen.ValueOrDefault)];
                    return new CameraContextMenu(hwnd, root, items);
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TimeoutException or FlaUI.Core.Exceptions.ElementNotAvailableException)
            {
                // A popup opening or closing right now.
            }
        }

        return null;
    }

    /// <summary>The checked state of a menu item through its UI Automation toggle pattern (Avalonia's menu item peer has one).</summary>
    public static ToggleState? Checked(AutomationElement item) =>
        item.Patterns.Toggle.TryGetPattern(out FlaUI.Core.Patterns.ITogglePattern? toggle) && toggle is not null ? toggle.ToggleState.ValueOrDefault : null;

    /// <summary>A short description of a menu for the report: id, name, access key and check state of each item.</summary>
    public static string Describe(CameraContextMenu menu)
    {
        StringBuilder text = new();
        foreach (AutomationElement item in menu.Items)
        {
            text.Append(CultureInfo.InvariantCulture, $"[{item.Properties.AutomationId.ValueOrDefault} '{item.Name}' key '{TrayMenu.AccessKeyOf(item)}' checked {Checked(item)?.ToString() ?? "-"} enabled {item.IsEnabled}] ");
        }

        return text.ToString();
    }

    /// <summary>
    /// Keeps checking for <paramref name="duration"/> that <paramref name="window"/> stays the foreground window (no focus
    /// lost after a mode switch or a hide / show of the window); returns the other foreground windows seen.
    /// </summary>
    public static async Task<List<nint>> ForegroundLossesAsync(nint window, TimeSpan duration)
    {
        List<nint> others = [];
        DateTime end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            nint foreground = Native.GetForegroundWindow();
            if (foreground != window && !others.Contains(foreground))
            {
                others.Add(foreground);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        return others;
    }
}

/// <summary>The pin as UI Automation reports it.</summary>
internal readonly record struct PinState(bool Shown, ControlType Type, string Name, ToggleState? Toggle, Rectangle Bounds)
{
    public override string ToString() => Shown ? $"{Type} '{Name}' {Toggle?.ToString() ?? "no toggle"} {Bounds}" : "not shown";
}

/// <summary>The brightest pixel of the pin and of the other window buttons (0–255).</summary>
internal readonly record struct ButtonLook(int Pin, int Others)
{
    /// <summary>The checked pin without the pointer: visible but dimmed (opacity 0.85 of white: about 217).</summary>
    public bool PinDimmed => Pin is >= 120 and <= 238;

    /// <summary>Hidden: nothing brighter than the black image around it.</summary>
    public static bool IsHidden(int brightest) => brightest <= 40;

    public override string ToString() => $"pin {Pin}, other buttons {Others}";
}

/// <summary>The open context menu of a camera window.</summary>
internal sealed record CameraContextMenu(nint Handle, AutomationElement Element, AutomationElement[] Items)
{
    public bool IsOpen => Native.IsWindowVisible(Handle);

    public AutomationElement Item(string automationId) =>
        Items.FirstOrDefault(item => item.Properties.AutomationId.ValueOrDefault == automationId)
        ?? throw new InvalidOperationException($"No shown item '{automationId}' in the context menu: {CameraWindowChecks.Describe(this)}");
}
