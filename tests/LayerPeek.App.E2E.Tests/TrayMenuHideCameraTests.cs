using System.Drawing;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Camera;

using Wait = LayerPeek.App.E2E.Tests.Infrastructure.Wait;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// T-168: "Hide camera" clicked with the real mouse in the printer submenu closes the whole tray menu, not only the submenu.
/// The click changes the submenu's structure (no "Bring camera to front" any more); before the fix the main menu stayed
/// open in front of the tray icon (since T-146).
/// </summary>
public sealed class TrayMenuHideCameraTests(E2EFixture fixture)
{
    private const string PrinterId = "e2e-tray-hide";

    [Fact]
    public async Task HideCameraClickedWithTheMouseClosesTheWholeMenu()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "tray-hide-camera",
            TestSettings.Json("en", [new TestPrinter(PrinterId, "Core One", "127.0.0.1:1", Camera: camera.StreamUri)]));
        nint cameraWindow = await app.OpenAsync($"camera:{PrinterId}", AppWindowKind.Camera);

        TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
        try
        {
            (AutomationElement[] submenu, nint submenuWindow) = await TrayMenu.ExpandAsync(app, menu, menu.Items()[0]);
            Screenshots.Save(submenuWindow, "tray-hide-camera-submenu");
            Rectangle hide = submenu.Single(static item => item.Name == "Hide camera").BoundingRectangle;
            Mouse.Click(new Point(hide.Left + (hide.Width / 2), hide.Top + (hide.Height / 2)));

            await Wait.UntilAsync(() => !Native.IsWindowVisible(cameraWindow), TimeSpan.FromSeconds(10), "the camera window to close");
            await Wait.UntilAsync(() => !menu.IsOpen && !Native.IsWindowVisible(submenuWindow), TimeSpan.FromSeconds(3), "the whole tray menu to close after Hide camera");
        }
        finally
        {
            await TrayMenu.CloseAsync(menu);
        }
    }
}
