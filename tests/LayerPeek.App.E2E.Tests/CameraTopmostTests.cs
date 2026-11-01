using System.Drawing;
using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Camera;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The real window order of the camera window over another app (T-071, manual check of T-037): a magenta window of the
/// test process (<see cref="CoverWindow"/>, another process than the app, never a window of the user) over the camera
/// window is brought to the front and activated. With "Always on top" the camera stays in front of it, without it the
/// cover window is in front – also after the option is switched off in the open window (Save, the window stays open);
/// full screen is never in front. Checked three ways: <c>WindowFromPoint</c> in the middle of the camera window, the
/// order of <c>EnumWindows</c> and a capture of a small area there (the camera picture, not magenta).
/// </summary>
public sealed class CameraTopmostTests(E2EFixture fixture)
{
    /// <summary>How far the cover window reaches past the camera window, so that a strip of it can still be clicked.</summary>
    private const int CoverMargin = 48;

    /// <summary>The captured area in the middle of the camera window (well under a quarter of any monitor).</summary>
    private const int ProbeSize = 64;

    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long the camera window is watched after it is visible again following a saved option (T-123).</summary>
    private static readonly TimeSpan StayOpenWatch = TimeSpan.FromSeconds(1);

    private readonly StringBuilder _report = new();

    [Fact]
    public async Task TopmostKeepsTheCameraAboveAnotherApp()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        TestMonitor chosen = TestMonitors.ChosenForCamera(TestMonitors.All());
        await using AppInstance app = await CameraWindowSettings.StartAsync(
            fixture,
            "camera-topmost",
            CameraPlacement.Window(chosen.CameraMonitorJson(), "Normal", topmostNormal: true, topmostMini: true, normalWidth: 640, normalHeight: 360),
            camera);
        nint window = await CameraWindowSettings.OpenCameraAsync(app);
        using WinEventRecorder recorder = await WinEventRecorder.StartAsync(app.Process.Id);
        try
        {
            // Normal window with the option on, then off in the open window.
            await Wait.UntilAsync(() => CameraWindowSettings.IsTopmost(window), WindowTimeout, "the normal window always on top");
            await ExpectPictureAsync(window);
            await ExpectOrderAsync(window, cameraInFront: true, "topmost-normal-on");
            int start = recorder.Events.Count;
            await SetTopmostAsync(app, "PrinterCameraWindowNormalTopmost", false);
            await Wait.UntilAsync(() => !CameraWindowSettings.IsTopmost(window), WindowTimeout, "the normal window no longer always on top after Save");
            await ExpectStaysOpenAsync(app, window, recorder, start, "The camera window stays open when the option changes", "topmost-normal-off");
            await ExpectOrderAsync(window, cameraInFront: false, "topmost-normal-off");

            // The mini preview (on by default), then off in the open window.
            await Ui.InvokeAsync(app.Element(window), "CameraMiniButton");
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == (320, 213) && CameraWindowSettings.IsTopmost(window), WindowTimeout, "the mini preview always on top");
            await ExpectPictureAsync(window);
            await ExpectOrderAsync(window, cameraInFront: true, "topmost-mini-on");
            start = recorder.Events.Count;
            await SetTopmostAsync(app, "PrinterCameraWindowMiniTopmost", false);
            await Wait.UntilAsync(() => !CameraWindowSettings.IsTopmost(window), WindowTimeout, "the mini preview no longer always on top after Save");
            await ExpectStaysOpenAsync(app, window, recorder, start, "The mini preview stays open when the option changes", "topmost-mini-off");
            await ExpectOrderAsync(window, cameraInFront: false, "topmost-mini-off");
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "camera-topmost-report");
        }
    }

    /// <summary>Full screen with both options on: another app activated over it stays in front (never always on top).</summary>
    [Fact]
    public async Task FullScreenIsNeverAboveAnotherApp()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        TestMonitor chosen = TestMonitors.ChosenForCamera(TestMonitors.All());
        await using AppInstance app = await CameraWindowSettings.StartAsync(
            fixture,
            "camera-topmost-full-screen",
            CameraPlacement.Window(chosen.CameraMonitorJson(), "FullScreen", topmostNormal: true, topmostMini: true),
            camera);
        nint window = await CameraWindowSettings.OpenCameraAsync(app);
        try
        {
            await Wait.UntilAsync(() => Native.GetWindowRect(window, out Native.Rect rect) && rect.Equals(chosen.Bounds), WindowTimeout, $"full screen over {chosen}");
            Assert.False(CameraWindowSettings.IsTopmost(window), "Full screen is never always on top (T-037).");
            await ExpectPictureAsync(window);
            await ExpectOrderAsync(window, cameraInFront: false, "topmost-full-screen");
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "camera-topmost-full-screen-report");
        }
    }

    /// <summary>
    /// Creates the cover window over the camera window, brings it to the front and activates it, and checks which of the
    /// two is in front in the middle of the camera window; the cover window is always closed again.
    /// </summary>
    private async Task ExpectOrderAsync(nint camera, bool cameraInFront, string label)
    {
        Native.Rect frame = Native.FrameBounds(camera);
        Native.Rect monitor = Native.MonitorOf(camera).Monitor;
        Native.Rect client = Native.VisibleClientRect(camera);
        Point middle = new(client.Left + (client.Width / 2), client.Top + (client.Height / 2));
        using CoverWindow cover = await CoverWindow.CreateAsync(CoverWindow.BoundsFor(frame, monitor, CoverMargin));
        await cover.BringToFrontAsync();
        // Let both windows draw before the capture.
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        nint front = Native.TopLevelWindowAt(middle);
        bool cameraAbove = CoverWindow.IsAbove(camera, cover.Handle);
        // What is in front there, as the user sees it: a small area of the screen (the shared capture guard of ScreenArea).
        using Bitmap probe = ScreenArea.CaptureBitmap(new Rectangle(middle.X - (ProbeSize / 2), middle.Y - (ProbeSize / 2), ProbeSize, ProbeSize));
        double magenta = Screenshots.FractionNear(probe, CoverWindow.Color);
        Screenshots.Save(probe, $"{label}-middle");
        _report.AppendLine(CultureInfo.InvariantCulture,
            $"{label}: camera 0x{camera:X} frame {frame}, topmost {CameraWindowSettings.IsTopmost(camera)}; cover 0x{cover.Handle:X}; at {middle.X},{middle.Y} 0x{front:X}; camera above cover in z-order: {cameraAbove}; magenta {magenta:P0}");

        nint expected = cameraInFront ? camera : cover.Handle;
        Assert.True(front == expected, $"{label}: WindowFromPoint in the middle of the camera window is 0x{front:X} '{Native.ClassName(front)}', expected the {(cameraInFront ? "camera window" : "cover window")} 0x{expected:X}.");
        Assert.True(cameraAbove == cameraInFront, $"{label}: in the z-order the camera window is {(cameraAbove ? "above" : "below")} the cover window.");
        Assert.True(cameraInFront ? magenta < 0.2 : magenta > 0.9, $"{label}: {magenta:P0} of the middle of the camera window is the cover's magenta.");
    }

    /// <summary>
    /// The camera window shows a picture in its middle (so that the capture tells it from the cover): a square of a quarter
    /// of the shorter side of the client area, inside the picture also when it is letterboxed (full screen on a portrait monitor).
    /// </summary>
    private static Task ExpectPictureAsync(nint camera) => Wait.UntilAsync(
        () =>
        {
            Native.Rect client = Native.VisibleClientRect(camera);
            Native.GetWindowRect(camera, out Native.Rect window);
            int side = Math.Min(client.Width, client.Height) / 4;
            Rectangle middle = new(client.Left - window.Left + ((client.Width - side) / 2), client.Top - window.Top + ((client.Height - side) / 2), side, side);
            using Bitmap frame = Screenshots.Capture(camera);
            return Screenshots.NonBlack(frame, middle) > 0.3;
        },
        TimeSpan.FromSeconds(20),
        "a camera picture in the middle of the window");

    /// <summary>
    /// The camera window stays open after an always-on-top option was switched off and saved (T-123). Avalonia gives the
    /// window its taskbar button back (T-102) by clearing <c>WS_EX_TOPMOST</c> first and then hiding and showing the same
    /// window once (<c>WindowImpl.UpdateWindowProperties</c>, branch <c>ShowInTaskbar</c>: <c>Hide()</c>,
    /// <c>SetParent(null)</c>, <c>Show(activate)</c>; the flicker is accepted, T-102 Q5). A single check right after the
    /// topmost style is gone may therefore fall into that hide. Waits until the window is visible again, then watches it
    /// for <see cref="StayOpenWatch"/>: it fails when the window was destroyed (closed or recreated), did not come back,
    /// was hidden again later, was hidden more than once, or when another camera window appeared. The message (and the
    /// report) records what was seen: the HWND, its state, the hides and shows of the WinEvent hook and the app's windows.
    /// </summary>
    private async Task ExpectStaysOpenAsync(AppInstance app, nint window, WinEventRecorder recorder, int start, string message, string label)
    {
        bool initiallyVisible = Native.IsWindowVisible(window);
        bool reshown = true;
        try
        {
            await Wait.UntilAsync(() => !Native.IsWindow(window) || Native.IsWindowVisible(window), WindowTimeout, $"{label}: the camera window 0x{window:X} visible again");
        }
        catch (WaitTimeoutException)
        {
            // Reported below with everything that was observed.
            reshown = false;
        }

        List<string> lapses = [];
        bool destroyed = false;
        HashSet<nint> otherCameras = [];
        DateTime until = DateTime.UtcNow + StayOpenWatch;
        while (DateTime.UtcNow < until)
        {
            bool exists = Native.IsWindow(window);
            bool visible = exists && Native.IsWindowVisible(window);
            if (!visible)
            {
                destroyed |= !exists;
                lapses.Add(string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:HH:mm:ss.fff} {(exists ? "hidden" : "destroyed")}"));
            }

            foreach (nint other in app.Windows())
            {
                if (other != window && app.KindOf(other) == AppWindowKind.Camera)
                {
                    otherCameras.Add(other);
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        bool existsAtEnd = Native.IsWindow(window);
        bool visibleAtEnd = existsAtEnd && Native.IsWindowVisible(window);
        List<WindowEvent> events = [.. recorder.Events.Skip(start).Where(e => e.Window == window && e.Kind is WinEventRecorder.EventObjectHide or WinEventRecorder.EventObjectShow)];
        int hides = events.Count(e => e.Kind == WinEventRecorder.EventObjectHide);
        string observed = string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: camera 0x{window:X} visible right after the topmost style went {initiallyVisible}, visible again within {WindowTimeout.TotalSeconds:0} s {reshown}; " +
            $"after {StayOpenWatch.TotalSeconds:0.#} s more: exists {existsAtEnd}, visible {visibleAtEnd}, lapses [{string.Join(", ", lapses)}]; " +
            $"hides {hides} since Save, events [{string.Join(", ", events)}]; other camera windows [{Describe(otherCameras)}]; app windows [{Describe(app.Windows())}]");
        _report.AppendLine(observed);

        // The window may still have been visible when the topmost style went (SetWindowPos runs just before the hide), so a
        // sample of the watch can fall into the one accepted hide; a hidden sample is allowed only when the hook saw exactly
        // that one hide. A second hide fails through the hook count, a window that stays hidden through visibleAtEnd.
        bool lapsesAccepted = !destroyed && (lapses.Count == 0 || hides == 1);
        bool stayedOpen = reshown && existsAtEnd && visibleAtEnd && lapsesAccepted && hides <= 1 && otherCameras.Count == 0;
        if (!stayedOpen)
        {
            FailureScreenshots.Capture(app, label);
        }

        Assert.True(stayedOpen, $"{message} (the same window, hidden at most once for its taskbar button): {observed}");
    }

    private static string Describe(IEnumerable<nint> windows) =>
        string.Join(", ", windows.Select(static hwnd => $"0x{hwnd:X} '{Native.ClassName(hwnd)}'"));

    /// <summary>Switches an always-on-top option in the settings and saves; the camera window stays open.</summary>
    private static async Task SetTopmostAsync(AppInstance app, string automationId, bool isChecked)
    {
        (nint settings, AutomationElement element) = await CameraWindowSettings.OpenTabAsync(app);
        await Ui.SetCheckedAsync(element, automationId, isChecked);
        await CameraWindowSettings.CloseAsync(settings, element, save: true);
    }
}
