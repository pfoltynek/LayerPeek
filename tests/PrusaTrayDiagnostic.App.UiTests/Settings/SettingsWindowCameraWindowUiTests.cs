using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Cameras;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.UiTests.Settings;

/// <summary>The "Camera window" sub-tab of the real settings window (T-035), on three fake monitors.</summary>
public sealed class SettingsWindowCameraWindowUiTests(HeadlessSession session)
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraWindowDefaults Custom = new()
    {
        RememberLastPlacement = false,
        StartMode = CameraWindowStartMode.FullScreen,
        Monitor = new CameraMonitorId("BenQ PD2700U", 3840, 2160),
        Normal = new CameraWindowLayout(new CameraWindowSize(1280, 720), CameraWindowCorner.TopRight),
        Mini = new CameraWindowLayout(new CameraWindowSize(400, 225), CameraWindowCorner.TopLeft),
    };

    private static PrinterDefinition Printer(bool cameraEnabled = true, CameraWindowDefaults? window = null) =>
        LinkPrinter("a", "Alpha") with { Camera = new CameraSettings(cameraEnabled, StreamUri) { Window = window ?? CameraWindowDefaults.Default } };

    /// <summary>Opens the window and the Camera window sub-tab with a click on its header, like the user.</summary>
    private static async Task<SettingsWindowHarness> OpenAsync(UiTestScope scope, PrinterDefinition printer, FakeEditorStore? store = null, ICameraWindowStateStore? states = null)
    {
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
            scope,
            store ?? SettingsWindowHarness.StoreWith(printer),
            screens: FakeScreenCatalog.ThreeMonitors(),
            cameraWindowStates: states);
        ui.Click("PrinterTabCameraWindow");
        Assert.Equal(SettingsViewModel.CameraWindowPrinterTab, ui.ViewModel.SelectedPrinterTabIndex);
        return ui;
    }

    /// <summary>The text box inside an editable combo box (where the user types).</summary>
    private static TextBox EditBox(ComboBox combo) => combo.GetVisualDescendants().OfType<TextBox>().First();

    [Fact]
    public Task SubTabShowsTheSavedValues() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer(window: Custom));

            Assert.False(ui.Find<CheckBox>("PrinterCameraWindowRemember").IsChecked);
            Assert.False(ui.Window.IsShown<TextBlock>("PrinterCameraWindowCameraOff"));
            Assert.True(Ui.ShowsText(ui.Window, "Full screen"));
            Assert.True(Ui.ShowsText(ui.Window, "BenQ PD2700U (3840 × 2160) – not connected"));
            Assert.Equal("1280 × 720", ui.Find<ComboBox>("PrinterCameraWindowNormalSize").Text);
            Assert.Equal("400 × 225", ui.Find<ComboBox>("PrinterCameraWindowMiniSize").Text);
            Assert.True(Ui.ShowsText(ui.Window, "Top right"));
            Assert.True(Ui.ShowsText(ui.Window, "Top left"));
            Assert.Equal("Normal window – Size", AutomationProperties.GetName(ui.Find<ComboBox>("PrinterCameraWindowNormalSize")));
            Assert.Equal("Mini preview – Position", AutomationProperties.GetName(ui.Find<ComboBox>("PrinterCameraWindowMiniCorner")));
            Assert.Equal(4, ui.Find<ComboBox>("PrinterCameraWindowNormalSize").ItemCount);
        });

    [Fact]
    public Task MonitorListOffersThePrimaryAndTheConnectedMonitors() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer());
            ComboBox monitors = ui.Find<ComboBox>("PrinterCameraWindowMonitor");

            Assert.Equal(4, monitors.ItemCount);
            Assert.True(Ui.ShowsText(ui.Window, "Primary monitor"));

            monitors.SelectedIndex = 1;
            ui.Window.Settle();

            Assert.True(Ui.ShowsText(ui.Window, "Monitor 1 – LG HDR 4K (1920 × 1080)"));
            Assert.Equal(new CameraMonitorId("LG HDR 4K", 1920, 1080), ui.ViewModel.SelectedPrinter!.SelectedMonitor.Value);
        });

    [Fact]
    public Task FieldsAreDisabledWhileTheCameraIsOffAndKeepTheirValues() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer(cameraEnabled: false, window: Custom));

            Assert.True(ui.Window.IsShown<TextBlock>("PrinterCameraWindowCameraOff"));
            Assert.False(ui.Find<ComboBox>("PrinterCameraWindowStartMode").IsEffectivelyEnabled);
            Assert.False(ui.Find<ComboBox>("PrinterCameraWindowNormalSize").IsEffectivelyEnabled);
            Assert.False(ui.Find<CheckBox>("PrinterCameraWindowRemember").IsEffectivelyEnabled);
            Assert.Equal("1280 × 720", ui.Find<ComboBox>("PrinterCameraWindowNormalSize").Text);

            ui.ViewModel.SelectedPrinter!.CameraEnabled = true;
            ui.Window.Settle();

            Assert.False(ui.Window.IsShown<TextBlock>("PrinterCameraWindowCameraOff"));
            Assert.True(ui.Find<ComboBox>("PrinterCameraWindowNormalSize").IsEffectivelyEnabled);
        });

    [Fact]
    public Task TypedSizePresetAndCornerAreSaved() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer());
            MemoryCameraWindowStateStore states = new();
            states.Set("a", new CameraWindowState(CameraWindowMode.Mini, null, new CameraWindowPlacement(1, 2, 400, 225)));
            SettingsWindowHarness ui = await OpenAsync(scope, Printer(), store, states);
            ComboBox normal = ui.Find<ComboBox>("PrinterCameraWindowNormalSize");
            ComboBox mini = ui.Find<ComboBox>("PrinterCameraWindowMiniSize");

            ui.Window.TypeOver(EditBox(normal), "1000x562");
            mini.SelectedIndex = 0;
            ui.Find<ComboBox>("PrinterCameraWindowNormalCorner").SelectedIndex = 3;
            ui.Window.Click(ui.Find<CheckBox>("PrinterCameraWindowRemember"));
            ui.Window.Settle();

            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            Assert.Equal("1000x562", printer.NormalSizeText);
            Assert.Equal("640 × 360", printer.MiniSizeText);
            await ui.SaveAsync();

            CameraWindowDefaults saved = Assert.Single(store.Saves).Updated.Printers[0].Camera.Window;
            Assert.Equal(new CameraWindowLayout(new CameraWindowSize(1000, 562), CameraWindowCorner.BottomRight), saved.Normal);
            Assert.Equal(new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.BottomRight), saved.Mini);
            Assert.False(saved.RememberLastPlacement);
            // Changed defaults forget the remembered window of the printer (T-035).
            Assert.Same(CameraWindowState.Default, states.Get("a"));
            Assert.False(ui.IsOpen);
        });

    [Fact]
    public Task InvalidSizeShowsTheErrorUnderItsRowAndMarksTheSubTab() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer());
            SettingsWindowHarness ui = await OpenAsync(scope, Printer(), store);

            ui.Window.TypeOver(EditBox(ui.Find<ComboBox>("PrinterCameraWindowNormalSize")), "100 × 50");

            Assert.Equal("Enter a width of 160–16384 and a height of 90–16384.", ui.ShownText("PrinterCameraWindowNormalSizeError"));
            Assert.Null(ui.ShownText("PrinterCameraWindowMiniSizeError"));
            Assert.True(ui.Window.IsShown<TextBlock>("PrinterTabCameraWindowMarker"));
            Assert.False(ui.Window.IsShown<TextBlock>("PrinterTabCameraMarker"));
            Assert.Equal("Camera window – contains errors", AutomationProperties.GetName(ui.Find<TabItem>("PrinterTabCameraWindow")));

            ui.Click("PrinterTabConnection");
            await ui.SaveAsync();

            Assert.Empty(store.Saves);
            Assert.True(ui.Find<TabItem>("PrinterTabCameraWindow").IsSelected);
        });

    [Fact]
    public Task TabMovesThroughTheSubTabInReadingOrder() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer());
            Assert.True(ui.Find<TabItem>("PrinterTabCameraWindow").Focus());

            List<string> order = [];
            for (int i = 0; i < 20 && !order.Contains("SettingsCancel"); i++)
            {
                ui.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                ui.Window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                ui.Window.Settle();
                if (FocusedId(ui.Window) is { } id && (order.Count == 0 || order[^1] != id))
                {
                    order.Add(id);
                }
            }

            Assert.Equal(
                [
                    "PrinterCameraWindowRemember", "PrinterCameraWindowStartMode", "PrinterCameraWindowMonitor",
                    "PrinterCameraWindowNormalSize", "PrinterCameraWindowNormalCorner", "PrinterCameraWindowMiniSize",
                    "PrinterCameraWindowMiniCorner", "SettingsSave", "SettingsCancel",
                ],
                order);
        });

    [Fact]
    public Task CtrlPageDownReachesTheSubTabAndWrapsAround() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(Printer()));
            Assert.True(ui.NameBox.Focus());

            Press(ui, PhysicalKey.PageDown);
            Press(ui, PhysicalKey.PageDown);
            Assert.True(ui.Find<TabItem>("PrinterTabCameraWindow").IsSelected);
            Assert.Equal("PrinterTabCameraWindow", FocusedId(ui.Window));

            Press(ui, PhysicalKey.PageDown);
            Assert.True(ui.Find<TabItem>("PrinterTabConnection").IsSelected);

            Press(ui, PhysicalKey.PageUp);
            Assert.True(ui.Find<TabItem>("PrinterTabCameraWindow").IsSelected);
        });

    [Fact]
    public Task TextsAndComboItemsFollowALanguageSwitch() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer(window: Custom));
            ui.Window.TypeOver(EditBox(ui.Find<ComboBox>("PrinterCameraWindowMiniSize")), "1x1");
            Assert.True(Ui.ShowsText(ui.Window, "Camera window"));

            Assert.True(new UiLanguage().Apply("de"));
            ui.Slot.RefreshTexts();
            ui.Window.Settle();

            Assert.True(Ui.ShowsText(ui.Window, "Kamerafenster"));
            Assert.True(Ui.ShowsText(ui.Window, "Vollbild"));
            Assert.True(Ui.ShowsText(ui.Window, "BenQ PD2700U (3840 × 2160) – nicht angeschlossen"));
            Assert.True(Ui.ShowsText(ui.Window, "Oben rechts"));
            Assert.True(Ui.ShowsText(ui.Window, "Normales Fenster"));
            Assert.Equal("Geben Sie eine Breite von 160–16384 und eine Höhe von 90–16384 ein.", ui.ShownText("PrinterCameraWindowMiniSizeError"));
            Assert.Equal("Kamerafenster – enthält Fehler", AutomationProperties.GetName(ui.Find<TabItem>("PrinterTabCameraWindow")));
            Assert.Equal("Mini-Vorschau – Größe", AutomationProperties.GetName(ui.Find<ComboBox>("PrinterCameraWindowMiniSize")));
            // The typed text stays.
            Assert.Equal("1x1", ui.Find<ComboBox>("PrinterCameraWindowMiniSize").Text);
        });

    private static void Press(SettingsWindowHarness ui, PhysicalKey key)
    {
        ui.Window.KeyPressQwerty(key, RawInputModifiers.Control);
        ui.Window.KeyReleaseQwerty(key, RawInputModifiers.Control);
        ui.Window.Settle();
    }

    private static string? FocusedId(Window window) =>
        (window.FocusManager?.GetFocusedElement() as Avalonia.Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(AutomationProperties.GetAutomationId)
            .FirstOrDefault(static id => !string.IsNullOrEmpty(id));
}
