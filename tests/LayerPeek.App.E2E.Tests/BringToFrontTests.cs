using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Camera;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// "Bring camera to front" of the real tray menu (T-102) and a second Settings… / Diagnostics… / About of the menu bring an
/// open window back in front of another app (T-119). The other app is a magenta window of the test process
/// (<see cref="CoverWindow"/>, another process than the app, never a window of the user) laid over the window and
/// activated, so that the foreground belongs to another process when the menu item is used – the situation in which
/// Windows refuses a plain <c>SetForegroundWindow</c> of the app (Avalonia's <c>Window.Activate</c>). The menu is opened
/// with the real mouse and its item is invoked through UI Automation (no input reaches the app). After the item the
/// window must be the foreground window and above the cover window; its "always on top" state must be what it was.
/// </summary>
public sealed class BringToFrontTests(E2EFixture fixture)
{
    /// <summary>How far the cover window reaches past the window, so that a strip of it can still be clicked.</summary>
    private const int CoverMargin = 48;

    private const string BringCameraToFront = "Bring camera to front";

    private static readonly TimeSpan FrontTimeout = TimeSpan.FromSeconds(5);

    private readonly StringBuilder _report = new();

    [Fact]
    public async Task BringCameraToFrontRaisesTheCameraAboveAnotherApp()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        TestMonitor chosen = TestMonitors.ChosenForCamera(TestMonitors.All());
        await using AppInstance app = await CameraWindowSettings.StartAsync(
            fixture,
            "bring-to-front-camera",
            CameraPlacement.Window(chosen.CameraMonitorJson(), "Normal", topmostNormal: false, topmostMini: true, normalWidth: 640, normalHeight: 360),
            camera);
        nint window = await CameraWindowSettings.OpenCameraAsync(app);
        try
        {
            // Not pinned (T-117): another app activated over it covers it.
            Assert.False(CameraWindowSettings.IsTopmost(window), "The normal window starts without always on top.");
            await ExpectBroughtToFrontAsync(app, window, () => InvokeCameraItemAsync(app), "camera-normal", topmost: false);

            // Minimized behind another app: restored and in front.
            await ExpectBroughtToFrontAsync(app, window, () => InvokeCameraItemAsync(app), "camera-normal-minimized", topmost: false, minimize: true);

            // Pinned (always on top) and minimized: it has no taskbar button, the menu item is the way back; it stays pinned.
            await Ui.SetCheckedAsync(app.Element(window), "CameraTopmostButton", true);
            await Wait.UntilAsync(() => CameraWindowSettings.IsTopmost(window), TimeSpan.FromSeconds(10), "the normal window pinned");
            await ExpectBroughtToFrontAsync(app, window, () => InvokeCameraItemAsync(app), "camera-normal-pinned-minimized", topmost: true, minimize: true);
            await Ui.SetCheckedAsync(app.Element(window), "CameraTopmostButton", false);
            await Wait.UntilAsync(() => !CameraWindowSettings.IsTopmost(window), TimeSpan.FromSeconds(10), "the normal window unpinned again");

            // The mini preview, always on top (default), then unpinned.
            await Ui.InvokeAsync(app.Element(window), "CameraMiniButton");
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == (320, 213) && CameraWindowSettings.IsTopmost(window), TimeSpan.FromSeconds(10), "the mini preview always on top");
            await ExpectBroughtToFrontAsync(app, window, () => InvokeCameraItemAsync(app), "camera-mini-pinned", topmost: true);
            await Ui.SetCheckedAsync(app.Element(window), "CameraTopmostButton", false);
            await Wait.UntilAsync(() => !CameraWindowSettings.IsTopmost(window), TimeSpan.FromSeconds(10), "the mini preview unpinned");
            await ExpectBroughtToFrontAsync(app, window, () => InvokeCameraItemAsync(app), "camera-mini-unpinned", topmost: false);
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "bring-to-front-camera-report");
        }
    }

    [Fact]
    public async Task SecondMenuChoiceRaisesSettingsDiagnosticsAndAboutAboveAnotherApp()
    {
        E2EFixture.Require();
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "bring-to-front-windows",
            TestSettings.Json("en", [new TestPrinter("e2e-front", "Core One", "127.0.0.1:1")]));
        try
        {
            foreach ((string item, AppWindowKind kind) in new[] { ("Settings…", AppWindowKind.Settings), ("Diagnostics…", AppWindowKind.Diagnostics), ("About", AppWindowKind.Other) })
            {
                List<nint> before = app.Windows();
                await InvokeMainItemAsync(app, item);
                nint window = await app.WaitForWindowAsync(kind, before);
                await ExpectBroughtToFrontAsync(app, window, () => InvokeMainItemAsync(app, item), $"second-{kind}-{item.TrimEnd('…')}", topmost: false);
                await app.CloseWindowAsync(window);
            }
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "bring-to-front-windows-report");
        }
    }

    /// <summary>
    /// Lays the cover window over <paramref name="window"/> and activates it (minimizing the window first when asked),
    /// uses the menu item, and checks that the window is restored, in the foreground and above the cover window, with its
    /// always-on-top state unchanged; the cover window is always closed again.
    /// </summary>
    private async Task ExpectBroughtToFrontAsync(AppInstance app, nint window, Func<Task> invoke, string label, bool topmost, bool minimize = false)
    {
        Native.Rect frame = Native.FrameBounds(window);
        Native.Rect monitor = Native.MonitorOf(window).Monitor;
        using CoverWindow cover = await CoverWindow.CreateAsync(CoverWindow.BoundsFor(frame, monitor, CoverMargin));
        await cover.BringToFrontAsync();
        if (minimize)
        {
            Native.ShowWindow(window, Native.SwMinimize);
            await Wait.UntilAsync(() => Native.IsIconic(window), TimeSpan.FromSeconds(5), "the window minimized");
            // Minimizing must not have moved the foreground to the app.
            await cover.BringToFrontAsync();
        }

        if (!topmost)
        {
            Assert.True(minimize || CoverWindow.IsAbove(cover.Handle, window), $"{label}: the cover window is not above the window before the menu item.");
        }

        nint foregroundBefore = Native.GetForegroundWindow();
        await invoke();
        bool front = await TryWaitAsync(() => Native.GetForegroundWindow() == window && !Native.IsIconic(window) && CoverWindow.IsAbove(window, cover.Handle));
        nint foreground = Native.GetForegroundWindow();
        bool above = CoverWindow.IsAbove(window, cover.Handle);
        _report.AppendLine(CultureInfo.InvariantCulture,
            $"{label}: window 0x{window:X}, cover 0x{cover.Handle:X}; foreground before 0x{foregroundBefore:X}, after 0x{foreground:X} ({Native.ClassName(foreground)}); above cover {above}; iconic {Native.IsIconic(window)}; topmost {CameraWindowSettings.IsTopmost(window)}");
        Screenshots.Save(window, $"bring-to-front-{label}");

        Assert.True(front, $"{label}: after the menu item the foreground is 0x{foreground:X} '{Native.ClassName(foreground)}', not the window 0x{window:X} (above the cover window: {above}, minimized: {Native.IsIconic(window)}).");
        Assert.True(topmost == CameraWindowSettings.IsTopmost(window), $"{label}: always on top is {CameraWindowSettings.IsTopmost(window)} after the menu item, expected {topmost}.");
    }

    /// <summary>"Bring camera to front" of the printer submenu.</summary>
    private static async Task InvokeCameraItemAsync(AppInstance app)
    {
        TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
        (AutomationElement[] submenu, _) = await TrayMenu.ExpandAsync(app, menu, menu.Items()[0]);
        AutomationElement item = Assert.Single(submenu, static candidate => candidate.Name == BringCameraToFront);
        item.Patterns.Invoke.Pattern.Invoke();
        await Wait.UntilAsync(() => !menu.IsOpen, TimeSpan.FromSeconds(5), "the tray menu to close after the item");
    }

    /// <summary>A top-level item of the tray menu.</summary>
    private static async Task InvokeMainItemAsync(AppInstance app, string name)
    {
        TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
        menu.Item(name).Patterns.Invoke.Pattern.Invoke();
        await Wait.UntilAsync(() => !menu.IsOpen, TimeSpan.FromSeconds(5), "the tray menu to close after the item");
    }

    private static async Task<bool> TryWaitAsync(Func<bool> condition)
    {
        try
        {
            await Wait.UntilAsync(condition, FrontTimeout, "the window in front");
            return true;
        }
        catch (WaitTimeoutException)
        {
            return false;
        }
    }
}
