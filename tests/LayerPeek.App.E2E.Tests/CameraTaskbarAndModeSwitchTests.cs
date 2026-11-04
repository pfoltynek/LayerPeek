using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;

using LayerPeek.App.E2E.Tests.Infrastructure;

using static LayerPeek.App.E2E.Tests.Infrastructure.CameraWindowChecks;

using Wait = LayerPeek.App.E2E.Tests.Infrastructure.Wait;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The taskbar presence rule of T-102 and the switches between the modes on the real app (manual steps W1–W3 of T-102 in
/// T-114, replaced by T-120): a camera window always on top has no taskbar button, every other one has (normal window and
/// full screen yes, the mini preview no, an always-on-top normal window no; settings, diagnostics and About always), checked
/// with the documented Win32 rule and the test app's real button on the taskbar; the printer submenu of an open camera
/// window with its unique access keys (the letter alone brings the camera to the front); and the switch between the pinned
/// normal window (or the mini preview) and full screen without losing the focus, moving the window or changing the
/// monitor, with at most one short hide / show of the window per switch (accepted, T-102 Q5).
/// </summary>
public sealed class CameraTaskbarAndModeSwitchTests(E2EFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly StringBuilder _report = new();

    /// <summary>
    /// W1: the normal window has a button; "Always on top" of the normal window switched on in the settings and applied to
    /// the open window removes it, switched off brings it back; full screen has one; the mini preview (always on top by
    /// default) has none, but settings, diagnostics and About opened meanwhile have theirs; the mini preview with its option
    /// switched off in the settings (saved) has one.
    /// </summary>
    [Fact]
    public async Task TaskbarButtonFollowsAlwaysOnTopAndTheOtherWindowsAlwaysHaveOne()
    {
        E2EFixture.Require();
        await using AppInstance app = await CameraWindowSettings.StartAsync(fixture, "taskbar-rule", CameraPlacement.Window(null, "Normal", topmostNormal: false, topmostMini: true, normalWidth: 640, normalHeight: 360));
        await FailureScreenshots.GuardAsync(app, "taskbar-rule", async () =>
        {
            nint window = await CameraWindowSettings.OpenCameraAsync(app);
            AutomationElement element = app.Element(window);
            await ExpectAsync(app, window, element, topmost: false, button: true, "normal window");

            // Always on top of the normal window in the settings, applied to the open window; then off again.
            await SetInSettingsAsync(app, window, "PrinterCameraWindowNormalTopmost", true, apply: true, "normal window, always on top applied");
            await ExpectAsync(app, window, element, topmost: true, button: false, "normal window, always on top (settings closed)");
            await SetInSettingsAsync(app, window, "PrinterCameraWindowNormalTopmost", false, apply: true, "normal window, always on top off applied");
            await ExpectAsync(app, window, element, topmost: false, button: true, "normal window, not on top (settings closed)");

            await Ui.InvokeAsync(element, "CameraFullScreenButton");
            await Wait.UntilAsync(() => Native.GetWindowRect(window, out Native.Rect rect) && rect.Equals(Native.MonitorOf(window).Monitor), Timeout, "full screen");
            await ExpectAsync(app, window, element, topmost: false, button: true, "full screen", pin: false);

            await Ui.InvokeAsync(element, "CameraMiniButton");
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == (320, 213), Timeout, $"the mini preview (now {WindowChecks.ClientSizeDip(window)})");
            await ExpectAsync(app, window, element, topmost: true, button: false, "mini preview");

            // Settings, diagnostics and About always have a button, also next to the mini preview without one.
            foreach ((string command, AppWindowKind kind) in new[] { ("settings", AppWindowKind.Settings), ("diagnostics", AppWindowKind.Diagnostics), ("about", AppWindowKind.Other) })
            {
                List<nint> before = app.Windows();
                await app.SendAsync(command);
                nint other = await app.WaitForWindowAsync(kind, before);
                await Wait.UntilAsync(() => Native.HasTaskbarButton(other), Timeout, $"{command}: a taskbar button");
                await TaskbarJumpList.WaitForButtonAsync(app.Automation);
                Assert.False(Native.HasTaskbarButton(window), $"{command} open: the mini preview got a taskbar button.");
                _report.AppendLine(CultureInfo.InvariantCulture, $"{command}: 0x{other:X} has a taskbar button; mini preview none");
                await app.CloseWindowAsync(other);
            }

            await ExpectAsync(app, window, element, topmost: true, button: false, "mini preview after the other windows closed");

            // The mini preview's option off in the settings (saved): a button.
            await SetInSettingsAsync(app, window, "PrinterCameraWindowMiniTopmost", false, apply: false, "mini preview, always on top off saved");
            await ExpectAsync(app, window, element, topmost: false, button: true, "mini preview, not on top");
            Assert.Equal((320, 213), WindowChecks.ClientSizeDip(window));
        });
        Screenshots.SaveText(_report.ToString(), "taskbar-rule-report");
    }

    /// <summary>
    /// W2: the printer submenu of an open camera window (opened with the real mouse, then the keyboard inside the app's own
    /// menu): "Open PrusaLink", "Bring camera to front", "Hide camera" with the access keys of the resources, no two the same;
    /// the letter alone of "Bring camera to front" brings the window, covered by another app, to the front with the focus.
    /// (The item with the mouse in every state: <see cref="BringToFrontTests"/>.)
    /// </summary>
    [Fact]
    public async Task PrinterSubmenuOfAnOpenCameraHasUniqueAccessKeysAndTheLetterBringsItToFront()
    {
        E2EFixture.Require();
        await using AppInstance app = await CameraWindowSettings.StartAsync(fixture, "taskbar-submenu", CameraPlacement.Window(null, "Normal", topmostNormal: false, topmostMini: true, normalWidth: 640, normalHeight: 360));
        await FailureScreenshots.GuardAsync(app, "taskbar-submenu", async () =>
        {
            nint window = await CameraWindowSettings.OpenCameraAsync(app);
            // T-146: the recording items follow after a separator (T-161: with the timelapse).
            string[] keys = ["MenuOpenPrusaLink", "MenuCameraToFront", "MenuHideCamera", "MenuStartRecording", "MenuStartTimelapse", "MenuPlayLastRecording", "MenuPlayLastTimelapse", "MenuOpenRecordingsFolder"];
            using CoverWindow cover = await CoverWindow.CreateAsync(CoverWindow.BoundsFor(Native.FrameBounds(window), Native.MonitorOf(window).Monitor, BackgroundMargin));
            await cover.BringToFrontAsync();
            Assert.True(CoverWindow.IsAbove(cover.Handle, window), "The other app's window is not above the camera window.");

            TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
            List<nint> before = app.Windows();
            TrayMenu.Press(app, VirtualKeyShort.DOWN);
            await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
            Assert.StartsWith("Core One", app.Automation.FocusedElement()?.Name ?? string.Empty, StringComparison.Ordinal);
            TrayMenu.Press(app, VirtualKeyShort.RIGHT);
            (AutomationElement[] submenu, nint submenuWindow) = await TrayMenu.SubmenuAsync(app, menu, menu.Items()[0], before);
            Screenshots.Save(submenuWindow, "taskbar-submenu-open-camera");
            string[] names = [.. submenu.Select(static item => item.Name)];
            string[] accessKeys = [.. submenu.Select(TrayMenu.AccessKeyOf)];
            _report.AppendLine(CultureInfo.InvariantCulture, $"Submenu: {string.Join(" | ", names.Zip(accessKeys, static (name, key) => $"{name} ({key})"))}");
            Assert.Equal(keys.Select(static key => TrayMenu.ShownText(TrayMenu.Resource(key))).ToArray(), names);
            Assert.Equal(keys.Select(static key => TrayMenu.AccessKeyLetter(TrayMenu.Resource(key))).ToArray(), accessKeys);
            Assert.True(accessKeys.Distinct(StringComparer.Ordinal).Count() == accessKeys.Length, $"Two items of the submenu share an access key: {string.Join(", ", accessKeys)}.");

            TrayMenu.Press(app, TrayMenu.KeyOf(TrayMenu.AccessKeyLetter(TrayMenu.Resource("MenuCameraToFront"))!));
            await Wait.UntilAsync(() => !menu.IsOpen, Timeout, "the tray menu to close after the letter");
            await Wait.UntilAsync(() => Native.GetForegroundWindow() == window && CoverWindow.IsAbove(window, cover.Handle), TimeSpan.FromSeconds(5), "the camera window in front with the focus after the letter");
            Assert.True(Native.IsWindowVisible(window), "The letter of 'Bring camera to front' closed the camera window.");
            Assert.False(CameraWindowSettings.IsTopmost(window), "Bringing the camera to the front made it always on top.");
            await NotificationArea.CloseOverflowAsync(app.Automation);
        });
        Screenshots.SaveText(_report.ToString(), "taskbar-submenu-report");
    }

    /// <summary>
    /// W3: the always-on-top normal window to full screen and back with F11, and again with F11 and Escape; then the mini
    /// preview to full screen and back with Escape. Every switch keeps the focus on the window, keeps the monitor, gives full
    /// screen its taskbar button and takes it away again, puts the window back exactly where it was (always on top again), and
    /// hides the window at most once (the owner change of the taskbar button, accepted).
    /// </summary>
    [Fact]
    public async Task SwitchingThePinnedWindowToFullScreenAndBackKeepsFocusPlaceAndMonitor()
    {
        E2EFixture.Require();
        TestMonitor chosen = TestMonitors.ChosenForCamera(TestMonitors.All());
        await using AppInstance app = await CameraWindowSettings.StartAsync(fixture, "mode-switch", CameraPlacement.Window(chosen.CameraMonitorJson(), "Normal", topmostNormal: true, topmostMini: true, normalWidth: 640, normalHeight: 360));
        await FailureScreenshots.GuardAsync(app, "mode-switch", async () =>
        {
            nint window = await CameraWindowSettings.OpenCameraAsync(app);
            AutomationElement element = app.Element(window);
            await ExpectPinnedAsync(window, element, on: true, "normal window");
            await WindowActivation.ActivateAsync(window, ImageMiddle(window, mini: false));
            using WinEventRecorder recorder = await WinEventRecorder.StartAsync(app.Process.Id);
            List<string> problems = [];

            await SwitchAndBackAsync(window, VirtualKeyShort.F11, VirtualKeyShort.F11, topmost: true, button: false, "normal-f11-f11", recorder, problems);
            await SwitchAndBackAsync(window, VirtualKeyShort.F11, VirtualKeyShort.ESCAPE, topmost: true, button: false, "normal-f11-escape", recorder, problems);

            await Ui.InvokeAsync(element, "CameraMiniButton");
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == (320, 213), Timeout, $"the mini preview (now {WindowChecks.ClientSizeDip(window)})");
            await ExpectPinnedAsync(window, element, on: true, "mini preview");
            await WindowActivation.ActivateAsync(window, ImageMiddle(window, mini: true));
            await SwitchAndBackAsync(window, VirtualKeyShort.F11, VirtualKeyShort.ESCAPE, topmost: true, button: false, "mini-f11-escape", recorder, problems);
            Assert.Equal((320, 213), WindowChecks.ClientSizeDip(window));
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });
        Screenshots.SaveText(_report.ToString(), "mode-switch-report");
    }

    /// <summary>One switch to full screen with <paramref name="enter"/> and back with <paramref name="leave"/>; problems are collected.</summary>
    private async Task SwitchAndBackAsync(nint window, VirtualKeyShort enter, VirtualKeyShort leave, bool topmost, bool button, string label, WinEventRecorder recorder, List<string> problems)
    {
        Native.Rect before = await Wait.StableAsync(() => Native.FrameBounds(window), Timeout, $"{label}: the window to settle");
        Native.Rect monitor = Native.MonitorOf(window).Monitor;
        Assert.Equal(window, Native.GetForegroundWindow());

        int start = recorder.Events.Count;
        WindowActivation.TypeInto(window, enter);
        await Wait.UntilAsync(() => Native.GetWindowRect(window, out Native.Rect rect) && rect.Equals(monitor), Timeout, $"{label}: full screen on {monitor}");
        await Wait.UntilAsync(() => Native.HasTaskbarButton(window) && !CameraWindowSettings.IsTopmost(window), Timeout, $"{label}: full screen with a taskbar button, not on top");
        List<nint> lostInFullScreen = await ForegroundLossesAsync(window, TimeSpan.FromSeconds(1.5));
        int hidesEntering = Hides(recorder, window, start);
        Screenshots.Save(window, $"mode-switch-{label}-full-screen");

        start = recorder.Events.Count;
        WindowActivation.TypeInto(window, leave);
        Native.Rect after = default;
        try
        {
            await Wait.UntilAsync(() => (after = Native.FrameBounds(window)).Equals(before), Timeout, $"{label}: back at {before}");
        }
        catch (WaitTimeoutException)
        {
            // Reported below with the rectangle it went back to.
        }

        await Wait.UntilAsync(() => Native.HasTaskbarButton(window) == button && CameraWindowSettings.IsTopmost(window) == topmost, Timeout, $"{label}: back {(topmost ? "always on top" : "not on top")}, {(button ? "with" : "without")} a taskbar button");
        List<nint> lostAfter = await ForegroundLossesAsync(window, TimeSpan.FromSeconds(1.5));
        int hidesLeaving = Hides(recorder, window, start);
        Native.Rect monitorAfter = Native.MonitorOf(window).Monitor;
        _report.AppendLine(CultureInfo.InvariantCulture, $"{label}: {before} → full screen {monitor} → {after}; hides {hidesEntering} / {hidesLeaving}; foreground lost {lostInFullScreen.Count} / {lostAfter.Count}");
        foreach (WindowEvent windowEvent in recorder.Events.Where(e => e.Window == window && e.Kind != WinEventRecorder.EventObjectLocationChange))
        {
            _report.AppendLine(CultureInfo.InvariantCulture, $"  {windowEvent}");
        }

        if (!after.Equals(before) && !(Math.Abs(after.Left - before.Left) <= 1 && Math.Abs(after.Top - before.Top) <= 1 && Math.Abs(after.Width - before.Width) <= 1 && Math.Abs(after.Height - before.Height) <= 1))
        {
            problems.Add($"{label}: the window came back at {after}, not at {before}.");
        }

        if (!monitorAfter.Equals(monitor))
        {
            problems.Add($"{label}: the window moved from the monitor {monitor} to {monitorAfter}.");
        }

        if (lostInFullScreen.Count > 0 || lostAfter.Count > 0)
        {
            problems.Add($"{label}: the window lost the foreground (in full screen to {Describe(lostInFullScreen)}, back to {Describe(lostAfter)}).");
        }

        if (hidesEntering > 1 || hidesLeaving > 1)
        {
            problems.Add($"{label}: the window was hidden {hidesEntering} times entering and {hidesLeaving} times leaving full screen (at most one short hide is accepted).");
        }
    }

    private static int Hides(WinEventRecorder recorder, nint window, int start) =>
        recorder.Events.Skip(start).Count(e => e.Window == window && e.Kind == WinEventRecorder.EventObjectHide);

    private static string Describe(List<nint> windows) =>
        windows.Count == 0 ? "none" : string.Join(", ", windows.Select(static hwnd => $"0x{hwnd:X} '{Native.ClassName(hwnd)}'"));

    /// <summary>The window's always-on-top state, its pin (not in full screen) and its taskbar button (the camera window being the app's only window).</summary>
    private async Task ExpectAsync(AppInstance app, nint window, AutomationElement element, bool topmost, bool button, string context, bool pin = true)
    {
        await Wait.UntilAsync(() => CameraWindowSettings.IsTopmost(window) == topmost, Timeout, $"{context}: always on top {(topmost ? "on" : "off")}");
        if (pin)
        {
            await ExpectPinnedAsync(window, element, topmost, context);
        }

        await ExpectTaskbarButtonAsync(app, window, button, onlyWindow: true, context);
        _report.AppendLine(CultureInfo.InvariantCulture, $"{context}: topmost {topmost}, taskbar button {button} (Win32 and the real taskbar)");
        Screenshots.Save(window, $"taskbar-rule-{context.Replace(' ', '-').Replace(",", string.Empty, StringComparison.Ordinal)}");
    }

    /// <summary>
    /// Switches an always-on-top option in the settings and applies it to the open window (Apply) or saves it (Save, which
    /// changes only the z-order); the Win32 rule is checked while the settings are still open, then they are closed (saved).
    /// </summary>
    private async Task SetInSettingsAsync(AppInstance app, nint window, string automationId, bool isChecked, bool apply, string context)
    {
        (nint settings, AutomationElement settingsElement) = await CameraWindowSettings.OpenTabAsync(app);
        await Ui.SetCheckedAsync(settingsElement, automationId, isChecked);
        if (apply)
        {
            await Ui.InvokeAsync(settingsElement, "PrinterCameraWindowApply");
            await Wait.UntilAsync(() => CameraWindowSettings.IsTopmost(window) == isChecked, Timeout, $"{context}: always on top {(isChecked ? "on" : "off")} after Apply");
            await ExpectTaskbarButtonAsync(app, window, button: !isChecked, onlyWindow: false, $"{context} (settings open)");
            Assert.True(Native.HasTaskbarButton(settings), $"{context}: the settings window has no taskbar button.");
        }

        await CameraWindowSettings.CloseAsync(settings, settingsElement, save: true);
        _report.AppendLine(CultureInfo.InvariantCulture, $"{context}: {automationId} = {isChecked}, {(apply ? "applied and saved" : "saved")}");
    }
}
