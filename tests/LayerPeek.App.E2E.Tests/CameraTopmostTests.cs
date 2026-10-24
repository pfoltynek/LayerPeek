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
        try
        {
            // Normal window with the option on, then off in the open window.
            await Wait.UntilAsync(() => CameraWindowSettings.IsTopmost(window), WindowTimeout, "the normal window always on top");
            await ExpectPictureAsync(window);
            await ExpectOrderAsync(window, cameraInFront: true, "topmost-normal-on");
            await SetTopmostAsync(app, "PrinterCameraWindowNormalTopmost", false);
            await Wait.UntilAsync(() => !CameraWindowSettings.IsTopmost(window), WindowTimeout, "the normal window no longer always on top after Save");
            Assert.True(Native.IsWindowVisible(window), "The camera window stays open when the option changes.");
            await ExpectOrderAsync(window, cameraInFront: false, "topmost-normal-off");

            // The mini preview (on by default), then off in the open window.
            await Ui.InvokeAsync(app.Element(window), "CameraMiniButton");
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == (320, 180) && CameraWindowSettings.IsTopmost(window), WindowTimeout, "the mini preview always on top");
            await ExpectPictureAsync(window);
            await ExpectOrderAsync(window, cameraInFront: true, "topmost-mini-on");
            await SetTopmostAsync(app, "PrinterCameraWindowMiniTopmost", false);
            await Wait.UntilAsync(() => !CameraWindowSettings.IsTopmost(window), WindowTimeout, "the mini preview no longer always on top after Save");
            Assert.True(Native.IsWindowVisible(window), "The mini preview stays open when the option changes.");
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

    /// <summary>Switches an always-on-top option in the settings and saves; the camera window stays open.</summary>
    private static async Task SetTopmostAsync(AppInstance app, string automationId, bool isChecked)
    {
        (nint settings, AutomationElement element) = await CameraWindowSettings.OpenTabAsync(app);
        await Ui.SetCheckedAsync(element, automationId, isChecked);
        await CameraWindowSettings.CloseAsync(settings, element, save: true);
    }
}
