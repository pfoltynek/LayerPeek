using System.Drawing;
using System.Text.Json.Nodes;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The widget settings on the real app (T-093): a <c>settings.json</c> of version 0.1.0 with the T-036 overlay corner is
/// converted on the first start (T-077) and saved as format 2 without the old keys, and the Widgets tab of the settings
/// (T-092) hides a widget and opens the layout mode of the camera window with its unsaved draft.
/// </summary>
public sealed class WidgetSettingsTests(E2EFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static readonly SimulationTiming LongPrint = new() { HeatUp = TimeSpan.FromSeconds(5), Print = TimeSpan.FromMinutes(30) };

    /// <summary>
    /// The old overlay in the top right corner: the default layout mirrored into it (status bottom right, print name bottom
    /// centre, times top right below the window buttons, temperatures top left), one notice in the log; "Save" in the
    /// settings writes version 2 with the widgets and none of the keys <c>corner</c> / <c>normal</c> / <c>mini</c>.
    /// </summary>
    [Fact]
    public async Task OldOverlayCornerIsMirroredAndSavedAsVersion2()
    {
        E2EFixture.Require();
        JsonObject oldOverlay = new()
        {
            ["corner"] = "TopRight",
            ["normal"] = new JsonArray("PrinterName", "FileName", "State"),
            ["mini"] = new JsonArray("State"),
        };
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "settings-migration", new WidgetRunOptions { Timing = LongPrint, Overlay = oldOverlay });
        await run.GuardAsync("settings-migration", async () =>
        {
            Assert.Contains("\"version\": 1", run.App.ReadSettings(), StringComparison.Ordinal);
            nint window = await run.OpenCameraAsync();
            AutomationElement element = run.App.Element(window);
            Dictionary<string, string> mirrored = new()
            {
                [CameraWidgets.Status] = "BottomRight",
                [CameraWidgets.PrintName] = "BottomCenter",
                [CameraWidgets.Times] = "TopRight",
                [CameraWidgets.Temperatures] = "TopLeft",
            };
            List<string> problems = [];
            await Wait.UntilAsync(
                () => (problems = CameraWidgets.LayoutProblems(element, CameraWidgets.ImageArea(window, element), mirrored)).Count == 0,
                TimeSpan.FromSeconds(60),
                "the default layout mirrored into the old top right corner");
            Screenshots.Save(window, "settings-migration-camera");
            Assert.Single(run.App.ReadLog().Split('\n'), static line => line.Contains("were converted to the widget layout (settings format 2)", StringComparison.Ordinal));

            // The next save in the settings window (here of a new printer name): format 2, the widgets, no old keys.
            nint settings = await run.App.OpenAsync("settings", AppWindowKind.Settings);
            AutomationElement settingsElement = run.App.Element(settings);
            (await Ui.ItemsAsync(settingsElement, "SettingsPrinterList", 1))[0].Patterns.SelectionItem.Pattern.Select();
            await Ui.SelectAsync(settingsElement, "PrinterTabConnection");
            await Ui.SetTextAsync(settingsElement, "PrinterName", "Core One (converted)");
            await CameraWindowSettings.CloseAsync(settings, settingsElement, save: true);
            await Wait.UntilAsync(() => JsonNode.Parse(run.App.ReadSettings())?["version"]?.GetValue<int>() == 2, Timeout, "settings.json of version 2");
            JsonObject overlay = JsonNode.Parse(run.App.ReadSettings())!["printers"]![0]!["camera"]!["overlay"]!.AsObject();
            Screenshots.SaveText(overlay.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), "settings-migration-overlay");
            Assert.True(overlay.ContainsKey("widgets"), "The saved overlay has no widgets.");
            Assert.DoesNotContain(overlay, static pair => pair.Key is "corner" or "normal" or "mini");
            Assert.Contains(overlay["widgets"]!.AsArray(), static widget => (string?)widget!["id"] == "status" && (string?)widget["anchor"] == "BottomRight");
        });
    }

    /// <summary>
    /// The Widgets tab: hiding the print name (unsaved) and "Edit layout in the camera window…" opens the layout mode with
    /// that draft (no print name card); Esc writes nothing; "Save" writes the hidden widget and the camera window drops its
    /// card (T-091 Q4, T-092).
    /// </summary>
    [Fact]
    public async Task WidgetsTabHidesAWidgetAndOpensTheLayoutModeWithItsDraft()
    {
        E2EFixture.Require();
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "settings-widgets", new WidgetRunOptions { Timing = LongPrint });
        await run.GuardAsync("settings-widgets", async () =>
        {
            nint window = await run.OpenCameraAsync();
            AutomationElement element = run.App.Element(window);
            await Wait.UntilAsync(() => CameraWidgets.IsShown(element, CameraWidgets.PrintName), TimeSpan.FromSeconds(60), "the print name card");
            string before = run.App.ReadSettings();

            nint settings = await run.App.OpenAsync("settings", AppWindowKind.Settings);
            AutomationElement settingsElement = run.App.Element(settings);
            await Ui.SelectAsync(settingsElement, "SettingsTabWidgets");
            await Ui.SetCheckedAsync(settingsElement, "WidgetsVisible-print-name", false);
            WindowChecks.AssertNoScrolling(settingsElement, "the Widgets tab");
            Screenshots.Save(settings, "settings-widgets-tab");

            await Ui.InvokeAsync(settingsElement, "WidgetsEditLayout");
            await Wait.UntilAsync(() => Ui.IsShown(element, "CameraLayoutOverlay"), Timeout, "the layout mode opened from the settings");
            Assert.True(Ui.IsShown(element, "CameraLayoutWidget-status"), "The layout mode has no status card.");
            Assert.False(Ui.IsShown(element, "CameraLayoutWidget-print-name"), "The layout mode shows the print name hidden in the settings' draft.");
            Screenshots.Save(window, "settings-widgets-layout-mode");
            await WindowActivation.ActivateAsync(window);
            WindowActivation.TypeInto(window, VirtualKeyShort.ESCAPE);
            await Wait.UntilAsync(() => !Ui.IsShown(element, "CameraLayoutOverlay"), Timeout, "Esc to end the layout mode");
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            Assert.Equal(before, run.App.ReadSettings());
            // Esc leaves the window with its saved layout.
            Assert.True(CameraWidgets.IsShown(element, CameraWidgets.PrintName), "The print name card is gone after Esc.");

            await CameraWindowSettings.CloseAsync(settings, settingsElement, save: true);
            await Wait.UntilAsync(
                () => JsonNode.Parse(run.App.ReadSettings())?["printers"]?[0]?["camera"]?["overlay"]?["widgets"]?.AsArray().Any(static widget => (string?)widget!["id"] == "print-name" && widget["visible"]?.GetValue<bool>() == false) == true,
                Timeout,
                "the hidden print name in settings.json");
            await Wait.UntilAsync(() => !CameraWidgets.IsShown(element, CameraWidgets.PrintName), Timeout, "the print name card to go");
            Screenshots.Save(window, "settings-widgets-saved");
        });
    }
}
