using System.Drawing;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using FlaUI.Core.AutomationElements;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Simulator.Camera;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The camera window of the real app with the fake RTSP camera of T-014 (and the real camera when it is reachable):
/// live picture, modes, always on top, taskbar button, placement, Apply and Restore defaults of the settings
/// (T-014, T-034, T-035, T-037, T-040, T-043, T-025).
/// </summary>
public sealed class CameraWindowTests(E2EFixture fixture)
{
    private const string PrinterId = "e2e-camera";

    /// <summary>The user's Buddy3D camera (T-014); used only when it answers within <see cref="RealCameraProbeTimeout"/>.</summary>
    private static readonly Uri RealCamera = new("rtsp://192.168.0.227/live");

    private static readonly TimeSpan RealCameraProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Margin of the camera window from the work area edges in its corners (T-035).</summary>
    private const int CornerMarginDip = 16;

    [Fact]
    public async Task FakeCameraWindowModesPlacementAndApply()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "camera-fake",
            TestSettings.Json("en", [new TestPrinter(PrinterId, "Core One", "127.0.0.1:1", Camera: camera.StreamUri)]));

        // Normal window (default: 960×540, centered on the primary monitor, not on top, with a taskbar button).
        nint window = await app.OpenAsync($"camera:{PrinterId}", AppWindowKind.Camera);
        AutomationElement element = app.Element(window);
        await ExpectLivePictureAsync(window, "camera-fake-normal");
        AssertNormalWindow(window, "camera normal");
        AssertCentered(window, Primary(), "camera normal");

        // Mini preview: 320×180 in the bottom right corner, always on top, no taskbar button (T-037, T-040).
        await Ui.InvokeAsync(element, "CameraMiniButton");
        await Wait.UntilAsync(() => ClientDip(window) == (320, 180), TimeSpan.FromSeconds(10), "the mini preview size");
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Screenshots.Save(window, "camera-fake-mini");
        await Wait.UntilAsync(() => IsTopmost(window), TimeSpan.FromSeconds(5), "the mini preview on top (default)");
        Assert.False(Native.HasTaskbarButton(window), "The mini preview has no taskbar button.");
        AssertInCorner(window, Primary(), right: true, bottom: true, "camera mini");
        Native.Rect miniRect = WindowRect(window);

        // Full screen from the mini preview, and back to the same place (T-040).
        await Ui.InvokeAsync(element, "CameraFullScreenButton");
        await Wait.UntilAsync(() => WindowRect(window).Equals(Native.MonitorOf(window).Monitor), TimeSpan.FromSeconds(10), "full screen on the monitor");
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Screenshots.Save(window, "camera-fake-fullscreen");
        await Wait.UntilAsync(() => !IsTopmost(window), TimeSpan.FromSeconds(5), "full screen not on top (never always on top)");
        await Ui.InvokeAsync(element, "CameraFullScreenButton");
        await Wait.UntilAsync(() => WindowRect(window).Equals(miniRect), TimeSpan.FromSeconds(10), $"the mini preview back at {miniRect}");
        await Wait.UntilAsync(() => IsTopmost(window), TimeSpan.FromSeconds(5), "the mini preview on top again");

        await Ui.InvokeAsync(element, "CameraNormalButton");
        await Wait.UntilAsync(() => ClientDip(window) == (960, 540), TimeSpan.FromSeconds(10), "the normal window size");
        AssertNormalWindow(window, "camera normal again");

        // Settings, camera tab: the test shows a preview of the stream (T-034).
        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        AutomationElement settingsElement = app.Element(settings);
        (await Ui.ItemsAsync(settingsElement, "SettingsPrinterList", 1))[0].Patterns.SelectionItem.Pattern.Select();
        await Ui.SelectAsync(settingsElement, "PrinterTabCamera");
        await Ui.InvokeAsync(settingsElement, "PrinterCameraTest");
        await Wait.UntilAsync(() => Ui.IsShown(settingsElement, "PrinterCameraPreview"), TimeSpan.FromSeconds(20), "the camera preview in the settings");
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Screenshots.Save(settings, "camera-fake-settings-preview");
        WindowChecks.AssertNoScrolling(settingsElement, "settings camera tab with the preview");

        // Camera window tab: top left corner and always on top for the normal window, then Apply (T-040).
        await Ui.SelectAsync(settingsElement, "PrinterTabCameraWindow");
        await Ui.SelectComboItemAsync(settingsElement, "PrinterCameraWindowNormalCorner", 0);
        await Ui.SetCheckedAsync(settingsElement, "PrinterCameraWindowNormalTopmost", true);
        await Ui.InvokeAsync(settingsElement, "PrinterCameraWindowApply");
        await Wait.UntilAsync(() => IsTopmost(window), TimeSpan.FromSeconds(10), "the normal window on top after Apply");
        AssertInCorner(window, Primary(), right: false, bottom: false, "camera normal after Apply (top left)");
        Screenshots.Save(settings, "camera-fake-settings-apply");
        Screenshots.Save(window, "camera-fake-normal-top-left");

        // Another monitor (when there is one): Apply moves the open window there (T-035, T-040).
        if (Screen.AllScreens.FirstOrDefault(static screen => !screen.Primary) is { } other)
        {
            Regex size = new($@"\b{other.Bounds.Width}\D+{other.Bounds.Height}\b");
            string chosen = await Ui.SelectComboItemAsync(settingsElement, "PrinterCameraWindowMonitor", (text, index) => index > 0 && size.IsMatch(text), $"the monitor {other.Bounds.Width}×{other.Bounds.Height}");
            await Ui.InvokeAsync(settingsElement, "PrinterCameraWindowApply");
            await Wait.UntilAsync(() => Native.MonitorOf(window).Monitor.Left == other.Bounds.Left && Native.MonitorOf(window).Monitor.Top == other.Bounds.Top, TimeSpan.FromSeconds(10), $"the camera window on '{chosen}'");
            AssertInCorner(window, Native.MonitorOf(window), right: false, bottom: false, "camera normal on the other monitor");
            Screenshots.Save(window, "camera-fake-normal-other-monitor");
        }

        // Restore defaults + Apply: centered on the primary monitor again, not on top.
        await Ui.InvokeAsync(settingsElement, "PrinterCameraWindowReset");
        await Ui.InvokeAsync(settingsElement, "PrinterCameraWindowApply");
        await Wait.UntilAsync(() => !IsTopmost(window), TimeSpan.FromSeconds(10), "the normal window no longer on top");
        await Wait.UntilAsync(() => Native.MonitorOf(window).Monitor.Equals(Primary().Monitor), TimeSpan.FromSeconds(10), "the camera window back on the primary monitor");
        AssertCentered(window, Primary(), "camera normal after Restore defaults");
        Screenshots.Save(settings, "camera-fake-settings-restored");
        WindowChecks.AssertNoScrolling(settingsElement, "settings camera window tab after Apply");
        await app.CloseWindowAsync(settings);

        // The tray item toggles the window closed (T-040); its state is remembered in the test data folder only.
        await app.SendAsync($"camera:{PrinterId}");
        await Wait.UntilAsync(() => !Native.IsWindowVisible(window), TimeSpan.FromSeconds(10), "the camera window to close");
        await Wait.UntilAsync(() => File.Exists(Path.Combine(app.DataDirectory, "camera-windows.json")), TimeSpan.FromSeconds(10), "camera-windows.json in the test data folder");
    }

    /// <summary>
    /// "Open as: Mini preview" + Apply (T-040, user report 2026-10-21): a closed camera window opens as the mini preview at
    /// its default size in the bottom right corner of the chosen monitor (another one when there is one), on top and without
    /// a taskbar button; an open window switches mode and monitor in one Apply.
    /// </summary>
    [Fact]
    public async Task ApplyOpensAndPlacesTheMiniPreview()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "camera-apply-mini",
            TestSettings.Json("en", [new TestPrinter(PrinterId, "Core One", "127.0.0.1:1", Camera: camera.StreamUri)]));

        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        AutomationElement settingsElement = app.Element(settings);
        (await Ui.ItemsAsync(settingsElement, "SettingsPrinterList", 1))[0].Patterns.SelectionItem.Pattern.Select();
        await Ui.SelectAsync(settingsElement, "PrinterTabCameraWindow");
        Screen? other = Screen.AllScreens.FirstOrDefault(static screen => !screen.Primary);
        string target = "the primary monitor";
        if (other is not null)
        {
            Regex size = new($@"\b{other.Bounds.Width}\D+{other.Bounds.Height}\b");
            target = await Ui.SelectComboItemAsync(settingsElement, "PrinterCameraWindowMonitor", (text, index) => index > 0 && size.IsMatch(text), $"the monitor {other.Bounds.Width}×{other.Bounds.Height}");
        }

        // Start modes in the order of CameraWindowStartMode: same as last time, normal window, mini preview, full screen.
        await Ui.SelectComboItemAsync(settingsElement, "PrinterCameraWindowStartMode", 2);
        await Ui.InvokeAsync(settingsElement, "PrinterCameraWindowApply");
        nint window = await app.WaitForWindowAsync(AppWindowKind.Camera);
        if (other is not null)
        {
            await Wait.UntilAsync(() => Native.MonitorOf(window).Monitor.Left == other.Bounds.Left && Native.MonitorOf(window).Monitor.Top == other.Bounds.Top, TimeSpan.FromSeconds(10), $"the mini preview on '{target}'");
        }

        await ExpectMiniPreviewAsync(window, other is null ? Primary() : Native.MonitorOf(window), $"closed window, Apply ('{target}')");
        // Shown without activation: the settings window keeps the focus (user decision 2026-10-21).
        Assert.NotEqual(window, Native.GetForegroundWindow());
        await ExpectLivePictureAsync(window, "camera-apply-mini-opened");

        // The open mini preview: normal window on the primary monitor in one Apply (mode and monitor together).
        await Ui.SelectComboItemAsync(settingsElement, "PrinterCameraWindowMonitor", 0);
        await Ui.SelectComboItemAsync(settingsElement, "PrinterCameraWindowStartMode", 1);
        await Ui.InvokeAsync(settingsElement, "PrinterCameraWindowApply");
        await Wait.UntilAsync(() => ClientDip(window) == (960, 540) && Native.HasTaskbarButton(window), TimeSpan.FromSeconds(10), "the normal window after Apply");
        await Wait.UntilAsync(() => Native.MonitorOf(window).Monitor.Equals(Primary().Monitor), TimeSpan.FromSeconds(10), "the normal window on the primary monitor");
        AssertNormalWindow(window, "camera normal after Apply");
        AssertCentered(window, Primary(), "camera normal after Apply");

        // And the open normal window back to the mini preview.
        await Ui.SelectComboItemAsync(settingsElement, "PrinterCameraWindowStartMode", 2);
        await Ui.InvokeAsync(settingsElement, "PrinterCameraWindowApply");
        await ExpectMiniPreviewAsync(window, Primary(), "open window, Apply (primary monitor)");
        Screenshots.Save(window, "camera-apply-mini-primary");
        Assert.Single(app.Windows(), hwnd => app.KindOf(hwnd) == AppWindowKind.Camera && Native.IsWindowVisible(hwnd));
        await app.CloseWindowAsync(settings);
    }

    /// <summary>The window is the mini preview: 320×180, in the bottom right corner of <paramref name="monitor"/>, on top, no taskbar button.</summary>
    private static async Task ExpectMiniPreviewAsync(nint window, Native.MonitorInfo monitor, string context)
    {
        await Wait.UntilAsync(() => ClientDip(window) == (320, 180), TimeSpan.FromSeconds(10), $"{context}: 320×180 (now {ClientDip(window)})");
        await Wait.UntilAsync(() => IsTopmost(window), TimeSpan.FromSeconds(5), $"{context}: on top");
        await Wait.UntilAsync(() => !Native.HasTaskbarButton(window), TimeSpan.FromSeconds(5), $"{context}: no taskbar button");
        Assert.True(Native.IsWindowVisible(window), $"{context}: the window is visible.");
        AssertInCorner(window, monitor, right: true, bottom: true, context);
    }

    [Fact]
    public async Task RealCameraShowsALivePicture()
    {
        E2EFixture.Require();
        Assert.SkipUnless(await IsReachableAsync(RealCamera), $"The real camera {RealCamera} does not answer within {RealCameraProbeTimeout.TotalSeconds:0} s.");
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "camera-real",
            TestSettings.Json("en", [new TestPrinter(PrinterId, "Real camera", "127.0.0.1:1", Camera: RealCamera)]));

        nint window = await app.OpenAsync($"camera:{PrinterId}", AppWindowKind.Camera);
        await ExpectLivePictureAsync(window, "camera-real-normal", TimeSpan.FromSeconds(30));
        AssertNormalWindow(window, "real camera normal");

        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        AutomationElement settingsElement = app.Element(settings);
        (await Ui.ItemsAsync(settingsElement, "SettingsPrinterList", 1))[0].Patterns.SelectionItem.Pattern.Select();
        await Ui.SelectAsync(settingsElement, "PrinterTabCamera");
        await Ui.InvokeAsync(settingsElement, "PrinterCameraTest");
        await Wait.UntilAsync(() => Ui.IsShown(settingsElement, "PrinterCameraPreview"), TimeSpan.FromSeconds(30), "the real camera preview in the settings");
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Screenshots.Save(settings, "camera-real-settings-preview");
        WindowChecks.AssertNoScrolling(settingsElement, "settings camera tab with the real preview");
    }

    /// <summary>The window shows a picture (not black) that changes over time (live stream).</summary>
    private static async Task ExpectLivePictureAsync(nint window, string screenshot, TimeSpan? timeout = null)
    {
        Rectangle client = ClientArea(window);
        await Wait.UntilAsync(
            () =>
            {
                using Bitmap frame = Screenshots.Capture(window);
                return Screenshots.NonBlack(frame, client) > 0.3;
            },
            timeout ?? TimeSpan.FromSeconds(20),
            "a camera picture");

        using Bitmap first = Screenshots.Capture(window);
        Screenshots.Save(first, screenshot);
        double change = 0;
        await Wait.UntilAsync(
            () =>
            {
                using Bitmap later = Screenshots.Capture(window);
                change = Math.Max(change, Screenshots.Difference(first, later));
                return change > 0.002;
            },
            TimeSpan.FromSeconds(10),
            "the camera picture to change (live stream)");
    }

    private static void AssertNormalWindow(nint window, string context)
    {
        Assert.Equal((960, 540), ClientDip(window));
        Assert.False(IsTopmost(window), $"{context}: the normal window is not on top by default.");
        Assert.True(Native.HasTaskbarButton(window), $"{context}: the normal window has a taskbar button.");
        WindowChecks.AssertTaskbarProperties(window, context);
        WindowChecks.AssertHasIcon(window, context);
    }

    private static Native.MonitorInfo Primary() =>
        Native.PrimaryMonitor();

    private static (int Width, int Height) ClientDip(nint hwnd) => WindowChecks.ClientSizeDip(hwnd);

    private static bool IsTopmost(nint hwnd) => (Native.ExStyle(hwnd) & Native.WsExTopmost) != 0;

    private static Native.Rect WindowRect(nint hwnd)
    {
        Assert.True(Native.GetWindowRect(hwnd, out Native.Rect rect));
        return rect;
    }

    /// <summary>The client area in window coordinates (for the captures).</summary>
    private static Rectangle ClientArea(nint hwnd)
    {
        Native.Rect window = WindowRect(hwnd);
        Native.Rect client = Native.VisibleClientRect(hwnd);
        return new Rectangle(client.Left - window.Left, client.Top - window.Top, client.Width, client.Height);
    }

    /// <summary>The visible frame (without the invisible resize borders) is centered in the work area.</summary>
    private static void AssertCentered(nint hwnd, Native.MonitorInfo monitor, string context)
    {
        Native.Rect frame = Native.FrameBounds(hwnd);
        int dx = (frame.Left + frame.Right) / 2 - (monitor.Work.Left + monitor.Work.Right) / 2;
        int dy = (frame.Top + frame.Bottom) / 2 - (monitor.Work.Top + monitor.Work.Bottom) / 2;
        Assert.True(Math.Abs(dx) <= 4 && Math.Abs(dy) <= 4, $"{context}: frame {frame} is not centered in the work area {monitor.Work} ({dx}, {dy}).");
    }

    /// <summary>The visible frame lies 16 DIP from the chosen edges of the work area.</summary>
    private static void AssertInCorner(nint hwnd, Native.MonitorInfo monitor, bool right, bool bottom, string context)
    {
        Native.Rect frame = Native.FrameBounds(hwnd);
        int margin = (int)Math.Round(CornerMarginDip * WindowChecks.Scaling(hwnd));
        int dx = right ? monitor.Work.Right - margin - frame.Right : frame.Left - (monitor.Work.Left + margin);
        int dy = bottom ? monitor.Work.Bottom - margin - frame.Bottom : frame.Top - (monitor.Work.Top + margin);
        Assert.True(Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2, $"{context}: frame {frame} is not {margin} px from the {(bottom ? "bottom" : "top")} {(right ? "right" : "left")} of the work area {monitor.Work} ({dx}, {dy}).");
    }

    private static async Task<bool> IsReachableAsync(Uri camera)
    {
        using TcpClient client = new();
        using CancellationTokenSource timeout = new(RealCameraProbeTimeout);
        try
        {
            await client.ConnectAsync(camera.Host, camera.IsDefaultPort || camera.Port < 0 ? 554 : camera.Port, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }
}
