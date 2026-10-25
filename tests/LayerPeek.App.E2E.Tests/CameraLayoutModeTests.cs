using System.Drawing;
using System.Text.Json;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

using Wait = LayerPeek.App.E2E.Tests.Infrastructure.Wait;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Camera;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The layout mode of the camera window in the real app (T-091): a drag with the real mouse (not simulated by Avalonia) moves
/// a widget card into another anchor, and "Done" writes only the printer's overlay section of the test settings file.
/// </summary>
public sealed class CameraLayoutModeTests(E2EFixture fixture)
{
    private const string PrinterId = "e2e-layout";

    [Fact]
    public async Task RealMouseDragMovesAWidgetAndDoneSavesTheLayout()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "camera-layout",
            TestSettings.Json("en", [new TestPrinter(PrinterId, "Core One", "127.0.0.1:1", Camera: camera.StreamUri)]));

        nint window = await app.OpenAsync($"camera:{PrinterId}", AppWindowKind.Camera);
        AutomationElement element = app.Element(window);
        await Ui.InvokeAsync(element, "CameraLayoutButton");
        await Wait.UntilAsync(() => Ui.IsShown(element, "CameraLayoutOverlay"), TimeSpan.FromSeconds(10), "the layout mode");
        await WindowActivation.ActivateAsync(window);
        Screenshots.Save(window, "camera-layout-mode");

        // Status (top left by default) to the bottom right anchor, with the real mouse in steps.
        Rectangle card = (await Ui.FindAsync(element, "CameraLayoutWidget-status")).BoundingRectangle;
        Rectangle zone = (await Ui.FindAsync(element, "CameraLayoutAnchorBottomRight")).BoundingRectangle;
        Point start = new(card.Left + (card.Width / 2), card.Top + (card.Height / 2));
        Point target = new(zone.Left + (zone.Width / 2), zone.Top + (zone.Height / 2));
        Assert.Equal(window, Native.TopLevelWindowAt(start));
        Mouse.Position = start;
        Mouse.Down(MouseButton.Left);
        try
        {
            for (int step = 1; step <= 10; step++)
            {
                Mouse.Position = new Point(start.X + ((target.X - start.X) * step / 10), start.Y + ((target.Y - start.Y) * step / 10));
                await Task.Delay(30, TestContext.Current.CancellationToken);
            }

            Screenshots.Save(window, "camera-layout-dragging");
        }
        finally
        {
            Mouse.Up(MouseButton.Left);
        }

        await Ui.InvokeAsync(element, "CameraLayoutDone");
        await Wait.UntilAsync(() => !Ui.IsShown(element, "CameraLayoutOverlay"), TimeSpan.FromSeconds(10), "the layout mode to end");
        await Wait.UntilAsync(() => StatusAnchor(app) == "BottomRight", TimeSpan.FromSeconds(10), "the status widget in the bottom right anchor of settings.json");
        Screenshots.Save(window, "camera-layout-saved");
        // The window buttons are back (hidden in the layout mode).
        Assert.True(Ui.IsShown(element, "CameraLayoutButton"));
    }

    /// <summary>The anchor of the status widget in the printer's overlay section of the test settings file.</summary>
    private static string? StatusAnchor(AppInstance app)
    {
        string path = Path.Combine(app.DataDirectory, "settings.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement printer in document.RootElement.GetProperty("printers").EnumerateArray())
            {
                if (printer.GetProperty("id").GetString() == PrinterId
                    && printer.GetProperty("camera").TryGetProperty("overlay", out JsonElement overlay)
                    && overlay.TryGetProperty("widgets", out JsonElement widgets))
                {
                    return widgets.EnumerateArray()
                        .Where(static widget => widget.GetProperty("id").GetString() == "status")
                        .Select(static widget => widget.GetProperty("anchor").GetString())
                        .FirstOrDefault();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Being written: read again.
        }

        return null;
    }
}
