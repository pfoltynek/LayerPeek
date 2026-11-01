using System.Drawing;
using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

using LayerPeek.App.E2E.Tests.Infrastructure;

using static LayerPeek.App.E2E.Tests.Infrastructure.CameraWindowChecks;

using Wait = LayerPeek.App.E2E.Tests.Infrastructure.Wait;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The pin of the camera window on the real app (T-117; manual check of T-114 replaced by T-120): in the top right corner
/// of the normal window and the mini preview, none in full screen; the checked pin stays visible dimmed without the pointer,
/// the unchecked one hides with the other buttons; a click of the real mouse switches "always on top" at once (Win32
/// <c>WS_EX_TOPMOST</c>, in front of a window of another process) and the taskbar button disappears / comes back (T-102);
/// the context menu with the right button, the Menu key and Shift+F10 (two items in the normal window, underlined access keys
/// after Alt; only "Always on top", checked by its state, in the mini preview); what a screen reader gets (button, name
/// "Always on top: on / off", toggle state), Space and Enter switch it and the focus stays; the choice survives a restart and
/// the open settings show it. The camera address answers nothing, so the image is black and the drawn buttons can be told
/// apart by their pixels (captured with PrintWindow, never the screen).
/// </summary>
public sealed class CameraPinTests(E2EFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>"Always on top" switches "at once": the window style follows the click within this time.</summary>
    private static readonly TimeSpan AtOnce = TimeSpan.FromSeconds(2);

    private readonly StringBuilder _report = new();

    /// <summary>
    /// The pin is the last window button in the top right corner (6 DIP from the edges) of the normal window and the mini
    /// preview and missing in full screen; with the pointer away the checked pin is drawn dimmed and the other buttons not at
    /// all; with the pointer over the image every button is drawn at full strength.
    /// </summary>
    [Fact]
    public async Task PinSitsInTheTopRightCornerAndStaysDimmedWithoutThePointer()
    {
        E2EFixture.Require();
        await using AppInstance app = await CameraWindowSettings.StartAsync(fixture, "pin-corner", CameraPlacement.Window(null, "Normal", topmostNormal: true, topmostMini: true, normalWidth: 640, normalHeight: 360));
        await GuardAsync(app, "pin-corner", async () =>
        {
            nint window = await CameraWindowSettings.OpenCameraAsync(app);
            AutomationElement element = app.Element(window);
            await ExpectPinnedAsync(window, element, on: true, "normal window");
            using (CoverWindow background = await BackgroundBelowAsync(window, DarkBackground))
            {
                await ExpectCornerAndLookAsync(window, element, background, "normal");
            }

            await Ui.InvokeAsync(element, "CameraMiniButton");
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == (320, 213), Timeout, $"the mini preview (now {WindowChecks.ClientSizeDip(window)})");
            await ExpectPinnedAsync(window, element, on: true, "mini preview");
            using (CoverWindow background = await BackgroundBelowAsync(window, DarkBackground))
            {
                await ExpectCornerAndLookAsync(window, element, background, "mini");
            }

            await Ui.InvokeAsync(element, "CameraFullScreenButton");
            await Wait.UntilAsync(() => Native.GetWindowRect(window, out Native.Rect rect) && rect.Equals(Native.MonitorOf(window).Monitor), Timeout, "full screen");
            await Wait.UntilAsync(() => !Pin(element).Shown, Timeout, "no pin in full screen");
            Assert.False(CameraWindowSettings.IsTopmost(window), "Full screen is never always on top.");
            Screenshots.Save(window, "pin-corner-full-screen");
        });
    }

    /// <summary>
    /// A click of the real mouse on the pin switches "always on top" at once, in the normal window and in the mini preview:
    /// the window style, the taskbar button (Win32 rule and the real button of the test app on the taskbar, the camera window
    /// being the app's only window), the order in front of a window of another process that was activated over it, the
    /// focus on the camera window and the saved option of that mode. After the click and with the pointer away only the
    /// dimmed pin is left (on) or nothing (off).
    /// </summary>
    [Fact]
    public async Task PinClickSwitchesAlwaysOnTopTheTaskbarButtonAndTheOrderAtOnce()
    {
        E2EFixture.Require();
        await using AppInstance app = await CameraWindowSettings.StartAsync(fixture, "pin-click", CameraPlacement.Window(null, "Normal", topmostNormal: false, topmostMini: true, normalWidth: 640, normalHeight: 360));
        await GuardAsync(app, "pin-click", async () =>
        {
            nint window = await CameraWindowSettings.OpenCameraAsync(app);
            AutomationElement element = app.Element(window);
            await ExpectPinnedAsync(window, element, on: false, "normal window at the start");
            await ExpectTaskbarButtonAsync(app, window, button: true, onlyWindow: true, "normal window at the start");
            _report.AppendLine(await ExpectOrderAsync(window, cameraInFront: false, "normal-start"));

            await ClickAndExpectAsync(app, window, element, on: true, "normal-pinned");
            await ClickAndExpectAsync(app, window, element, on: false, "normal-unpinned");
            await Wait.UntilAsync(() => CameraWindowSettings.SavedWindow(app)?["topmostNormal"]?.GetValue<bool>() == false, Timeout, "topmostNormal false in settings.json");

            await Ui.InvokeAsync(element, "CameraMiniButton");
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == (320, 213), Timeout, $"the mini preview (now {WindowChecks.ClientSizeDip(window)})");
            await ExpectPinnedAsync(window, element, on: true, "mini preview (on by default)");
            await ExpectTaskbarButtonAsync(app, window, button: false, onlyWindow: true, "mini preview (on by default)");

            await ClickAndExpectAsync(app, window, element, on: false, "mini-unpinned");
            await Wait.UntilAsync(() => CameraWindowSettings.SavedWindow(app)?["topmostMini"]?.GetValue<bool>() == false, Timeout, "topmostMini false in settings.json");
            await ClickAndExpectAsync(app, window, element, on: true, "mini-pinned");
            await Wait.UntilAsync(() => CameraWindowSettings.SavedWindow(app)?["topmostMini"]?.GetValue<bool>() == true, Timeout, "topmostMini true in settings.json");
            Assert.False(CameraWindowSettings.SavedWindow(app)?["topmostNormal"]?.GetValue<bool>(), "The mini preview's pin changed the option of the normal window.");
        });
    }

    /// <summary>
    /// What a screen reader gets from the pin (a button named "Always on top: off / on" with the toggle state) and the
    /// keyboard: Tab reaches it, Space and Enter switch "always on top", the focus stays on the pin and the window keeps the
    /// foreground although the taskbar button changes; the same in the mini preview.
    /// </summary>
    [Fact]
    public async Task SpaceAndEnterSwitchThePinAndTheFocusStaysOnIt()
    {
        E2EFixture.Require();
        await using AppInstance app = await CameraWindowSettings.StartAsync(fixture, "pin-keyboard", CameraPlacement.Window(null, "Normal", topmostNormal: false, topmostMini: true, normalWidth: 640, normalHeight: 360));
        await GuardAsync(app, "pin-keyboard", async () =>
        {
            nint window = await CameraWindowSettings.OpenCameraAsync(app);
            AutomationElement element = app.Element(window);
            await ExpectPinnedAsync(window, element, on: false, "normal window");
            PinState pin = Pin(element);
            _report.AppendLine(CultureInfo.InvariantCulture, $"Normal window, pin for a screen reader: {pin}");
            Assert.Equal(ControlType.Button, pin.Type);

            await WindowActivation.ActivateAsync(window, ImageMiddle(window, mini: false));
            await FocusWithTabAsync(app.Automation, window, PinId);
            await PressAndExpectAsync(app, window, element, VirtualKeyShort.SPACE, on: true, "normal-space");
            await PressAndExpectAsync(app, window, element, VirtualKeyShort.RETURN, on: false, "normal-enter");

            await Ui.InvokeAsync(element, "CameraMiniButton");
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == (320, 213), Timeout, $"the mini preview (now {WindowChecks.ClientSizeDip(window)})");
            await ExpectPinnedAsync(window, element, on: true, "mini preview");
            await WindowActivation.ActivateAsync(window, ImageMiddle(window, mini: true));
            await FocusWithTabAsync(app.Automation, window, PinId);
            await PressAndExpectAsync(app, window, element, VirtualKeyShort.SPACE, on: false, "mini-space");
            await PressAndExpectAsync(app, window, element, VirtualKeyShort.RETURN, on: true, "mini-enter");
            Assert.Equal((320, 213), WindowChecks.ClientSizeDip(window));
        });
    }

    /// <summary>
    /// The context menu of the image: the right button, the Menu key and Shift+F10 (sent to the camera window only). In the
    /// normal window "Edit layout" and "Always on top" (access keys E and T, distinct, underlined only after Alt), the second
    /// one checked like the pin; in the mini preview only "Always on top", checked by the mini preview's own option. A click
    /// on the item, its letter alone (without Alt in a menu opened with the right button or in the mini preview, after Alt
    /// was released in the normal window, T-121) and Alt+its letter switch "always on top" and close the menu; Escape
    /// closes the menu and changes nothing else (the mini preview stays).
    /// </summary>
    [Fact]
    public async Task ContextMenuOpensWithTheRightButtonTheMenuKeyAndShiftF10()
    {
        E2EFixture.Require();
        await using AppInstance app = await CameraWindowSettings.StartAsync(fixture, "pin-context-menu", CameraPlacement.Window(null, "Normal", topmostNormal: false, topmostMini: true, normalWidth: 640, normalHeight: 360));
        await GuardAsync(app, "pin-context-menu", async () =>
        {
            nint window = await CameraWindowSettings.OpenCameraAsync(app);
            AutomationElement element = app.Element(window);
            await ExpectPinnedAsync(window, element, on: false, "normal window");
            string topmostKey = TrayMenu.AccessKeyLetter(Text("CameraTopmostMenu"))!;
            string layoutKey = TrayMenu.AccessKeyLetter(Text("CameraLayoutEditMenu"))!;
            Assert.NotEqual(layoutKey, topmostKey);

            // Normal window: the right button, then the item.
            CameraContextMenu menu = await OpenWithRightButtonAsync(app, window, mini: false);
            AssertItems(menu, mini: false, on: false, "normal, right button");
            Screenshots.Save(menu.Handle, "pin-context-menu-normal-right-button");
            await ClickMenuItemAsync(menu, menu.Item(TopmostMenuItemId));
            await ExpectClosedAndPinnedAsync(window, element, menu, on: true, "normal, item");

            // The right button, then the letter alone without any Alt: the menu's own popup has the keyboard (T-121).
            menu = await OpenWithRightButtonAsync(app, window, mini: false);
            AssertItems(menu, mini: false, on: true, "normal, right button again");
            TrayMenu.Press(app, TrayMenu.KeyOf(topmostKey));
            await ExpectClosedAndPinnedAsync(window, element, menu, on: false, $"normal, right button, {topmostKey} alone");

            // The Menu key with the pointer away from the window: access keys underlined after Alt only, then the letter.
            using (CoverWindow background = await BackgroundBelowAsync(window, DarkBackground))
            {
                await MovePointerBesideAsync(window, background);
            }

            menu = await OpenWithKeysAsync(app, window, mini: false, VirtualKeyShort.APPS);
            AssertItems(menu, mini: false, on: false, "normal, Menu key");
            await ExpectUnderlinesAfterAltAsync(app, menu, "normal");
            TrayMenu.Press(app, TrayMenu.KeyOf(topmostKey));
            await ExpectClosedAndPinnedAsync(window, element, menu, on: true, $"normal, Alt released, then {topmostKey}");

            // Shift+F10, then Escape: closed, nothing switched.
            menu = await OpenWithKeysAsync(app, window, mini: false, VirtualKeyShort.SHIFT, VirtualKeyShort.F10);
            AssertItems(menu, mini: false, on: true, "normal, Shift+F10");
            await EscapeAsync(app, menu);
            await ExpectClosedAndPinnedAsync(window, element, menu, on: true, "normal, Escape");

            // The mini preview (on by default): only "Always on top", checked.
            await Ui.InvokeAsync(element, "CameraMiniButton");
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == (320, 213), Timeout, $"the mini preview (now {WindowChecks.ClientSizeDip(window)})");
            await ExpectPinnedAsync(window, element, on: true, "mini preview");
            menu = await OpenWithRightButtonAsync(app, window, mini: true);
            AssertItems(menu, mini: true, on: true, "mini, right button");
            Screenshots.Save(menu.Handle, "pin-context-menu-mini-right-button");
            await ClickMenuItemAsync(menu, menu.Item(TopmostMenuItemId));
            await ExpectClosedAndPinnedAsync(window, element, menu, on: false, "mini, item");

            menu = await OpenWithKeysAsync(app, window, mini: true, VirtualKeyShort.APPS);
            AssertItems(menu, mini: true, on: false, "mini, Menu key");
            TrayMenu.Press(app, TrayMenu.KeyOf(topmostKey));
            await ExpectClosedAndPinnedAsync(window, element, menu, on: true, $"mini, {topmostKey} alone");

            // Alt+letter keeps working.
            menu = await OpenWithKeysAsync(app, window, mini: true, VirtualKeyShort.APPS);
            AssertItems(menu, mini: true, on: true, "mini, Menu key again");
            PressAltWith(app, TrayMenu.KeyOf(topmostKey));
            await ExpectClosedAndPinnedAsync(window, element, menu, on: false, $"mini, Alt+{topmostKey}");

            menu = await OpenWithKeysAsync(app, window, mini: true, VirtualKeyShort.SHIFT, VirtualKeyShort.F10);
            AssertItems(menu, mini: true, on: false, "mini, Shift+F10");
            Screenshots.Save(menu.Handle, "pin-context-menu-mini-shift-f10");
            await EscapeAsync(app, menu);
            await ExpectClosedAndPinnedAsync(window, element, menu, on: false, "mini, Escape");
            Assert.True(WindowChecks.ClientSizeDip(window) == (320, 213), $"Escape closing the menu also left the mini preview ({WindowChecks.ClientSizeDip(window)}).");
        });
    }

    /// <summary>
    /// The pin while the settings are open with an unsaved change: the open settings show the new option at once, the
    /// unsaved change stays (and is not written), Cancel keeps the pinned option; after a restart of the app the normal
    /// window opens always on top without a taskbar button, the pin says so, and the settings show it.
    /// </summary>
    [Fact]
    public async Task PinSurvivesARestartAndTheOpenSettingsShowIt()
    {
        E2EFixture.Require();
        await using AppInstance app = await CameraWindowSettings.StartAsync(fixture, "pin-restart", CameraPlacement.Window(null, "Normal", normalCorner: "TopLeft", topmostNormal: false, topmostMini: true, normalWidth: 640, normalHeight: 360));
        await GuardAsync(app, "pin-restart", async () =>
        {
            nint window = await CameraWindowSettings.OpenCameraAsync(app);
            AutomationElement element = app.Element(window);
            await ExpectPinnedAsync(window, element, on: false, "normal window");

            (nint settings, AutomationElement settingsElement) = await CameraWindowSettings.OpenTabAsync(app);
            Assert.Equal(ToggleState.Off, ToggleOf(settingsElement, "PrinterCameraWindowNormalTopmost"));
            // An unsaved change of another option.
            await Ui.SetCheckedAsync(settingsElement, "PrinterCameraWindowMiniTopmost", false);

            // The pin through its UI Automation toggle (the settings window may cover it; the real click is tested above).
            await Ui.SetCheckedAsync(element, PinId, true);
            await ExpectPinnedAsync(window, element, on: true, "pinned while the settings are open");
            await Wait.UntilAsync(() => ToggleOf(settingsElement, "PrinterCameraWindowNormalTopmost") == ToggleState.On, Timeout, "the open settings to show the pinned normal window");
            Assert.Equal(ToggleState.Off, ToggleOf(settingsElement, "PrinterCameraWindowMiniTopmost"));
            await Wait.UntilAsync(() => CameraWindowSettings.SavedWindow(app)?["topmostNormal"]?.GetValue<bool>() == true, Timeout, "topmostNormal true in settings.json");
            Assert.True(CameraWindowSettings.SavedWindow(app)?["topmostMini"]?.GetValue<bool>(), "The unsaved change of the mini preview's option was written with the pin.");
            Screenshots.Save(settings, "pin-restart-settings-open");
            await CameraWindowSettings.CloseAsync(settings, settingsElement, save: false);
            Assert.True(CameraWindowSettings.SavedWindow(app)?["topmostNormal"]?.GetValue<bool>(), "Cancel in the settings undid the pin.");
            Assert.True(CameraWindowSettings.SavedWindow(app)?["topmostMini"]?.GetValue<bool>(), "Cancel wrote the unsaved change.");

            await app.RestartAsync();
            window = await CameraWindowSettings.OpenCameraAsync(app);
            element = app.Element(window);
            await ExpectPinnedAsync(window, element, on: true, "normal window after the restart");
            await ExpectTaskbarButtonAsync(app, window, button: false, onlyWindow: true, "normal window after the restart");
            Screenshots.Save(window, "pin-restart-camera");
            (settings, settingsElement) = await CameraWindowSettings.OpenTabAsync(app);
            Assert.Equal(ToggleState.On, ToggleOf(settingsElement, "PrinterCameraWindowNormalTopmost"));
            Assert.Equal(ToggleState.On, ToggleOf(settingsElement, "PrinterCameraWindowMiniTopmost"));
            Screenshots.Save(settings, "pin-restart-settings-after-restart");
            await CameraWindowSettings.CloseAsync(settings, settingsElement, save: false);
        });
    }

    /// <summary>Runs the test body with failure screenshots; the report is saved also when it fails.</summary>
    private async Task GuardAsync(AppInstance app, string name, Func<Task> body)
    {
        try
        {
            await FailureScreenshots.GuardAsync(app, name, body);
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), $"{name}-report");
        }
    }

    /// <summary>Corner, order and look of the pin in the current mode (the background lies below the window).</summary>
    private async Task ExpectCornerAndLookAsync(nint window, AutomationElement element, CoverWindow background, string mode)
    {
        double scale = WindowChecks.Scaling(window);
        Native.Rect client = Native.VisibleClientRect(window);
        PinState pin = Pin(element);
        _report.AppendLine(CultureInfo.InvariantCulture, $"{mode}: client {client}, scale {scale:0.##}, pin {pin}");
        Assert.True(pin.Shown, $"{mode}: no pin.");
        Assert.Equal(ControlType.Button, pin.Type);
        int margin = (int)Math.Round(6 * scale);
        Assert.True(Math.Abs(client.Right - margin - pin.Bounds.Right) <= 2 && Math.Abs(pin.Bounds.Top - (client.Top + margin)) <= 2, $"{mode}: the pin {pin.Bounds} is not 6 DIP from the top right corner of the client area {client}.");
        foreach (string id in OtherWindowButtons)
        {
            if (Ui.TryFind(element, id) is { } button && !button.Properties.IsOffscreen.ValueOrDefault)
            {
                Assert.True(button.BoundingRectangle.Right <= pin.Bounds.Left, $"{mode}: {id} {button.BoundingRectangle} is not left of the pin {pin.Bounds} (the pin is the last button).");
            }
        }

        await MovePointerBesideAsync(window, background);
        ButtonLook away = Look(window, element);
        Screenshots.Save(window, $"pin-corner-{mode}-pointer-away");
        await MovePointerOntoAsync(window, ImageMiddle(window, mini: mode == "mini"));
        ButtonLook hover = Look(window, element);
        Screenshots.Save(window, $"pin-corner-{mode}-pointer-over");
        _report.AppendLine(CultureInfo.InvariantCulture, $"  pointer away: {away}; pointer over the image: {hover}");
        Assert.True(away.PinDimmed, $"{mode}: the checked pin without the pointer is not drawn dimmed ({away}).");
        Assert.True(ButtonLook.IsHidden(away.Others), $"{mode}: the other window buttons are drawn without the pointer ({away}).");
        Assert.True(hover.Pin > away.Pin && hover.Pin >= 240 && hover.Others >= 200, $"{mode}: the buttons are not drawn at full strength with the pointer over the image ({hover}, away {away}).");
        await MovePointerBesideAsync(window, background);
    }

    /// <summary>Clicks the pin and checks everything that follows it at once.</summary>
    private async Task ClickAndExpectAsync(AppInstance app, nint window, AutomationElement element, bool on, string label)
    {
        await ClickPinAsync(window, element);
        DateTime clicked = DateTime.UtcNow;
        await Wait.UntilAsync(() => CameraWindowSettings.IsTopmost(window) == on, AtOnce, $"{label}: WS_EX_TOPMOST {(on ? "set" : "cleared")} right after the click");
        TimeSpan took = DateTime.UtcNow - clicked;
        await ExpectPinnedAsync(window, element, on, label);
        await ExpectTaskbarButtonAsync(app, window, button: !on, onlyWindow: true, label);
        List<nint> lost = await ForegroundLossesAsync(window, TimeSpan.FromSeconds(1));
        Assert.True(lost.Count == 0, $"{label}: the camera window lost the foreground after the click (to {string.Join(", ", lost.Select(static hwnd => $"0x{hwnd:X} '{Native.ClassName(hwnd)}'"))}).");

        // With the pointer away: only the dimmed pin (on) or nothing (off).
        using (CoverWindow background = await BackgroundBelowAsync(window, DarkBackground))
        {
            await MovePointerBesideAsync(window, background);
            ButtonLook look = Look(window, element);
            Screenshots.Save(window, $"pin-click-{label}-pointer-away");
            string focus = $"UIA focus '{Focused(app.Automation)}', {Native.ThreadFocus(window)}";
            _report.AppendLine(CultureInfo.InvariantCulture, $"{label}: switched in {took.TotalMilliseconds:0} ms; pointer away: {look}; {focus}");
            Assert.True(on ? look.PinDimmed : ButtonLook.IsHidden(look.Pin), $"{label}: the pin without the pointer is {(on ? "not dimmed" : "still drawn")} ({look}; {focus}).");
            Assert.True(ButtonLook.IsHidden(look.Others), $"{label}: other window buttons are drawn without the pointer ({look}; {focus}).");
        }

        _report.AppendLine(await ExpectOrderAsync(window, cameraInFront: on, label));
    }

    /// <summary>
    /// Presses a key on the focused pin and checks the switch, the foreground and that the keyboard focus stayed on the pin.
    /// UI Automation may report the focus late (or on the window) right after the window's owner changed with the taskbar
    /// button (T-120 run 2026-10-31: '' after Enter in the mini preview): what counts is where the next key goes, so when UIA
    /// does not name the pin within 2 s, Space is pressed once more and must switch the pin back, then again to restore.
    /// </summary>
    private async Task PressAndExpectAsync(AppInstance app, nint window, AutomationElement element, VirtualKeyShort key, bool on, string label)
    {
        WindowActivation.TypeInto(window, key);
        await ExpectPinnedAsync(window, element, on, label);
        List<nint> lost = await ForegroundLossesAsync(window, TimeSpan.FromSeconds(1));
        Assert.True(lost.Count == 0, $"{label}: the camera window lost the foreground after {key}.");
        bool reported = await TryWaitAsync(() => Focused(app.Automation) == PinId, TimeSpan.FromSeconds(2));
        string focused = Focused(app.Automation);
        string win32 = Native.ThreadFocus(window);
        _report.AppendLine(CultureInfo.InvariantCulture, $"{label}: pin {Pin(element)}; UIA focus '{focused}'; {win32}; foreground lost to {lost.Count} window(s)");
        if (reported)
        {
            return;
        }

        // Where does the next key go? Space on the pin switches it back.
        WindowActivation.TypeInto(window, VirtualKeyShort.SPACE);
        bool switched = await TryWaitAsync(() => CameraWindowSettings.IsTopmost(window) == !on, TimeSpan.FromSeconds(2));
        _report.AppendLine(CultureInfo.InvariantCulture, $"  UIA did not report the pin; Space afterwards {(switched ? "switched the pin back (the focus is on it)" : "did NOT switch the pin")}; UIA focus now '{Focused(app.Automation)}'");
        Assert.True(switched, $"{label}: after {key} the keyboard focus is no longer on the pin (UIA focus '{focused}', {win32}; Space does not switch the pin).");
        WindowActivation.TypeInto(window, VirtualKeyShort.SPACE);
        await ExpectPinnedAsync(window, element, on, $"{label}, restored");
    }

    private static async Task<bool> TryWaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        try
        {
            await Wait.UntilAsync(condition, timeout, "the condition");
            return true;
        }
        catch (WaitTimeoutException)
        {
            return false;
        }
    }

    /// <summary>Right-clicks the middle of the image with the real mouse and waits for the menu.</summary>
    private static async Task<CameraContextMenu> OpenWithRightButtonAsync(AppInstance app, nint window, bool mini)
    {
        Point point = ImageMiddle(window, mini);
        await MovePointerOntoAsync(window, point);
        Mouse.Click(MouseButton.Right);
        return await WaitForContextMenuAsync(app, window);
    }

    /// <summary>Activates the camera window (a click on its image) and presses the keys into it; waits for the menu.</summary>
    private static async Task<CameraContextMenu> OpenWithKeysAsync(AppInstance app, nint window, bool mini, params VirtualKeyShort[] keys)
    {
        await WindowActivation.ActivateAsync(window, ImageMiddle(window, mini));
        WindowActivation.TypeChordInto(window, keys);
        return await WaitForContextMenuAsync(app, window);
    }

    private void AssertItems(CameraContextMenu menu, bool mini, bool on, string context)
    {
        string description = Describe(menu);
        _report.AppendLine(CultureInfo.InvariantCulture, $"{context}: {description}");
        string[] expected = mini ? [TopmostMenuItemId] : [LayoutMenuItemId, TopmostMenuItemId];
        Assert.True(menu.Items.Select(static item => item.Properties.AutomationId.ValueOrDefault ?? string.Empty).SequenceEqual(expected), $"{context}: the menu shows {description}, expected {string.Join(", ", expected)}.");
        AutomationElement topmost = menu.Item(TopmostMenuItemId);
        Assert.Equal(TrayMenu.ShownText(Text("CameraTopmostMenu")), topmost.Name);
        Assert.Equal(TrayMenu.AccessKeyLetter(Text("CameraTopmostMenu")), TrayMenu.AccessKeyOf(topmost));
        Assert.True(topmost.IsEnabled, $"{context}: 'Always on top' is disabled.");
        Assert.True(Checked(topmost) == (on ? ToggleState.On : ToggleState.Off), $"{context}: 'Always on top' is {Checked(topmost)?.ToString() ?? "without a check state"}, expected {(on ? "checked" : "unchecked")}.");
        if (!mini)
        {
            AutomationElement layout = menu.Item(LayoutMenuItemId);
            Assert.Equal(TrayMenu.ShownText(Text("CameraLayoutEditMenu")), layout.Name);
            Assert.Equal(TrayMenu.AccessKeyLetter(Text("CameraLayoutEditMenu")), TrayMenu.AccessKeyOf(layout));
            Assert.True(layout.IsEnabled, $"{context}: 'Edit layout' is disabled in a 640 × 360 window.");
        }
    }

    /// <summary>The menu captured before and after Alt: only the access keys get underlined, one line in each item.</summary>
    private static async Task ExpectUnderlinesAfterAltAsync(AppInstance app, CameraContextMenu menu, string context)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);
        using Bitmap before = Screenshots.Capture(menu.Handle);
        Screenshots.Save(before, $"pin-context-menu-{context}-before-alt");
        TrayMenu.Press(app, VirtualKeyShort.ALT);
        await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);
        Assert.True(menu.IsOpen, $"{context}: Alt closed the context menu.");
        using Bitmap after = Screenshots.Capture(menu.Handle);
        Screenshots.Save(after, $"pin-context-menu-{context}-alt");
        Native.GetWindowRect(menu.Handle, out Native.Rect rect);
        Dictionary<string, Rectangle> rows = menu.Items.ToDictionary(
            static item => item.Name,
            item => new Rectangle(item.BoundingRectangle.Left - rect.Left, item.BoundingRectangle.Top - rect.Top, item.BoundingRectangle.Width, item.BoundingRectangle.Height));
        (List<string> problems, List<string> lines) = PixelChecks.CheckUnderlines(after, before, rows);
        Screenshots.SaveText(string.Join(Environment.NewLine, [.. lines, .. problems]), $"pin-context-menu-{context}-underlines");
        Assert.True(problems.Count == 0, $"{context}: {string.Join(Environment.NewLine, problems)}");
    }

    /// <summary>
    /// Alt held down with <paramref name="key"/> for the open menu (only while a window of the app has the keyboard). The
    /// letter alone, the usual way since T-121, goes through <see cref="TrayMenu.Press"/>.
    /// </summary>
    private static void PressAltWith(AppInstance app, VirtualKeyShort key)
    {
        nint foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out uint process);
        Assert.True(process == (uint)app.Process.Id, $"Not pressing Alt+{key}: the app does not have the keyboard (foreground 0x{foreground:X} '{Native.ClassName(foreground)}').");
        Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, key);
    }

    /// <summary>Escape for the open menu (only while a window of the app has the keyboard).</summary>
    private static async Task EscapeAsync(AppInstance app, CameraContextMenu menu)
    {
        Assert.True(menu.IsOpen, "The context menu closed by itself before Escape.");
        TrayMenu.Press(app, VirtualKeyShort.ESCAPE);
        await Wait.UntilAsync(() => !menu.IsOpen, Timeout, "Escape to close the context menu");
    }

    private static async Task ExpectClosedAndPinnedAsync(nint window, AutomationElement element, CameraContextMenu menu, bool on, string context)
    {
        await Wait.UntilAsync(() => !menu.IsOpen, Timeout, $"{context}: the context menu to close");
        await ExpectPinnedAsync(window, element, on, context);
    }

    private static ToggleState? ToggleOf(AutomationElement root, string automationId) =>
        Ui.TryFind(root, automationId) is { } element && element.Patterns.Toggle.TryGetPattern(out FlaUI.Core.Patterns.ITogglePattern? toggle) && toggle is not null
            ? toggle.ToggleState.ValueOrDefault
            : null;
}
