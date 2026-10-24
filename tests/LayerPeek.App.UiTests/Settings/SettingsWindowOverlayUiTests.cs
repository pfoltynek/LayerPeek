using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>The "Overlay" sub-tab of the real settings window (T-036): corner, 9 × 2 check boxes, hints, layout, keyboard.</summary>
public sealed class SettingsWindowOverlayUiTests(HeadlessSession session)
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    /// <summary>Width of the two check box columns (T-033 design).</summary>
    private const double ColumnWidth = 120;

    private static readonly string[] ItemIds =
        ["PrinterName", "State", "NozzleTemperature", "BedTemperature", "Progress", "RemainingTime", "FinishTime", "PrintTime", "FileName"];

    private static readonly CameraOverlayOptions Custom = new()
    {
        Corner = CameraOverlayCorner.TopRight,
        Normal = CameraOverlayItems.PrinterName | CameraOverlayItems.BedTemperature,
        Mini = CameraOverlayItems.FileName,
    };

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    private static PrinterDefinition Printer(bool cameraEnabled = true, CameraOverlayOptions? overlay = null) =>
        LinkPrinter("a", "Alpha") with { Camera = new CameraSettings(cameraEnabled, StreamUri) { LegacyOverlay = overlay ?? CameraOverlayOptions.Default } };

    /// <summary>Opens the window and the Overlay sub-tab with a click on its header, like the user.</summary>
    private static async Task<SettingsWindowHarness> OpenAsync(UiTestScope scope, PrinterDefinition printer, FakeEditorStore? store = null)
    {
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store ?? SettingsWindowHarness.StoreWith(printer));
        ui.Click("PrinterTabOverlay");
        Assert.Equal(SettingsViewModel.OverlayPrinterTab, ui.ViewModel.SelectedPrinterTabIndex);
        return ui;
    }

    private static CheckBox Box(SettingsWindowHarness ui, string item, bool mini) => ui.Find<CheckBox>($"PrinterOverlay{item}{(mini ? "Mini" : "Normal")}");

    private static Rect BoundsIn(Control control, Visual root) => new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);

    [Fact]
    public Task SubTabShowsTheDefaults() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer());

            Assert.Equal("Overlay", ui.Find<TabItem>("PrinterTabOverlay").Header is TextBlock header ? header.Text : null);
            Assert.True(Ui.ShowsText(ui.Window, "Overlay corner"));
            Assert.True(Ui.ShowsText(ui.Window, "Bottom left"));
            Assert.Equal("Normal window and full screen", ui.ShownText("PrinterOverlayNormalHeader"));
            Assert.Equal("Mini preview", ui.ShownText("PrinterOverlayMiniHeader"));
            Assert.Equal(Strings.SettingsOverlayHint, ui.ShownText("PrinterOverlayHint"));
            Assert.False(ui.Window.IsShown<TextBlock>("PrinterOverlayCameraOff"));
            foreach (string item in ItemIds)
            {
                bool expected = item is "State" or "Progress" or "RemainingTime";
                Assert.Equal(expected, Box(ui, item, mini: false).IsChecked);
                Assert.Equal(expected, Box(ui, item, mini: true).IsChecked);
            }

            Assert.Equal("Printer state – Normal window and full screen", AutomationProperties.GetName(Box(ui, "State", mini: false)));
            Assert.Equal("File name – Mini preview", AutomationProperties.GetName(Box(ui, "FileName", mini: true)));
            Assert.Equal("Overlay corner", AutomationProperties.GetName(ui.Find<ComboBox>("PrinterOverlayCorner")));
        });

    [Fact]
    public Task RowsAreGroupedPrinterDataFirst() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer());

            double previous = double.MinValue;
            foreach (string item in ItemIds)
            {
                Rect normal = BoundsIn(Box(ui, item, mini: false), ui.Window);
                Rect mini = BoundsIn(Box(ui, item, mini: true), ui.Window);
                Assert.True(normal.Y > previous, item);
                Assert.Equal(normal.Y, mini.Y, 1);
                Assert.True(mini.X > normal.X, item);
                previous = normal.Y;
            }
        });

    [Fact]
    public Task ClickedCheckBoxesAndCornerAreSaved() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer());
            SettingsWindowHarness ui = await OpenAsync(scope, Printer(), store);

            foreach (string item in (ReadOnlySpan<string>)["State", "Progress", "RemainingTime", "PrinterName", "BedTemperature"])
            {
                ui.Window.Click(Box(ui, item, mini: false));
            }

            foreach (string item in (ReadOnlySpan<string>)["State", "Progress", "RemainingTime", "FileName"])
            {
                ui.Window.Click(Box(ui, item, mini: true));
            }

            ui.Find<ComboBox>("PrinterOverlayCorner").SelectedIndex = 1;
            ui.Window.Settle();
            await ui.SaveAsync();

            Assert.Equal(Custom, Assert.Single(store.Saves).Updated.Printers[0].Camera.LegacyOverlay);
            Assert.False(ui.IsOpen);
        });

    [Fact]
    public Task SavedValuesAreShown() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer(overlay: Custom));

            Assert.True(Ui.ShowsText(ui.Window, "Top right"));
            Assert.True(Box(ui, "PrinterName", mini: false).IsChecked);
            Assert.True(Box(ui, "BedTemperature", mini: false).IsChecked);
            Assert.False(Box(ui, "State", mini: false).IsChecked);
            Assert.True(Box(ui, "FileName", mini: true).IsChecked);
            Assert.False(Box(ui, "Progress", mini: true).IsChecked);
        });

    [Fact]
    public Task FieldsAreDisabledWhileTheCameraIsOffAndKeepTheirValues() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer(cameraEnabled: false, overlay: Custom));

            Assert.True(ui.Window.IsShown<TextBlock>("PrinterOverlayCameraOff"));
            Assert.True(ui.Window.IsShown<TextBlock>("PrinterOverlayHint"));
            Assert.False(ui.Find<ComboBox>("PrinterOverlayCorner").IsEffectivelyEnabled);
            Assert.All(ItemIds, item =>
            {
                Assert.False(Box(ui, item, mini: false).IsEffectivelyEnabled);
                Assert.False(Box(ui, item, mini: true).IsEffectivelyEnabled);
            });
            Assert.True(Box(ui, "PrinterName", mini: false).IsChecked);

            ui.ViewModel.SelectedPrinter!.CameraEnabled = true;
            ui.Window.Settle();

            Assert.False(ui.Window.IsShown<TextBlock>("PrinterOverlayCameraOff"));
            Assert.True(ui.Find<ComboBox>("PrinterOverlayCorner").IsEffectivelyEnabled);
            Assert.True(Box(ui, "FileName", mini: true).IsEffectivelyEnabled);
            Assert.True(Box(ui, "PrinterName", mini: false).IsChecked);
            Assert.True(Box(ui, "FileName", mini: true).IsChecked);
        });

    [Fact]
    public Task TabMovesThroughTheSubTabRowByRow() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer());
            Assert.True(ui.Find<TabItem>("PrinterTabOverlay").Focus());

            List<string> order = [];
            for (int i = 0; i < 30 && !order.Contains("SettingsCancel"); i++)
            {
                ui.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                ui.Window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                ui.Window.Settle();
                if (FocusedId(ui.Window) is { } id && (order.Count == 0 || order[^1] != id))
                {
                    order.Add(id);
                }
            }

            List<string> expected = ["PrinterOverlayCorner"];
            foreach (string item in ItemIds)
            {
                expected.Add($"PrinterOverlay{item}Normal");
                expected.Add($"PrinterOverlay{item}Mini");
            }

            expected.AddRange(["SettingsSave", "SettingsCancel"]);
            Assert.Equal(expected, order);
        });

    [Theory]
    [MemberData(nameof(Languages))]
    public Task TableFitsItsColumnsInEveryLanguage(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            SettingsWindowHarness ui = await OpenAsync(scope, Printer(cameraEnabled: false));
            ui.Window.Settle();
            List<string> problems = [];

            // Column headers: at most 2 lines in their 120 DIP column, no word cut.
            foreach (string id in (ReadOnlySpan<string>)["PrinterOverlayNormalHeader", "PrinterOverlayMiniHeader"])
            {
                TextBlock header = ui.Find<TextBlock>(id);
                if (header.Bounds.Width > ColumnWidth + 0.01)
                {
                    problems.Add($"{language}: {id} is {header.Bounds.Width:0.#} DIP wide.");
                }

                if (header.TextLayout.TextLines.Count > 2)
                {
                    problems.Add($"{language}: '{header.Text}' has {header.TextLayout.TextLines.Count} lines.");
                }

                foreach (string word in header.Text!.Split(' '))
                {
                    TextBlock probe = new() { Text = word, FontFamily = header.FontFamily, FontSize = header.FontSize, FontWeight = header.FontWeight };
                    probe.Measure(Size.Infinity);
                    if (probe.DesiredSize.Width > ColumnWidth + 0.5)
                    {
                        problems.Add($"{language}: '{word}' needs {probe.DesiredSize.Width:0.#} DIP.");
                    }
                }
            }

            // Check boxes: inside their column, at least 32 DIP high (click target), each row below the previous.
            Rect normalHeader = BoundsIn(ui.Find<TextBlock>("PrinterOverlayNormalHeader"), ui.Window);
            Rect miniHeader = BoundsIn(ui.Find<TextBlock>("PrinterOverlayMiniHeader"), ui.Window);
            foreach (string item in ItemIds)
            {
                Rect normal = BoundsIn(Box(ui, item, mini: false), ui.Window);
                Rect mini = BoundsIn(Box(ui, item, mini: true), ui.Window);
                if (normal.Height < 32 - 0.01 || normal.Right > mini.X || Math.Abs(normal.Center.X - normalHeader.Center.X) > 1 || Math.Abs(mini.Center.X - miniHeader.Center.X) > 1)
                {
                    problems.Add($"{language}: check boxes of {item} at {normal} and {mini}, headers at {normalHeader} and {miniHeader}.");
                }
            }

            // The hints on the right have room for at least a few words per line.
            TextBlock hint = ui.Find<TextBlock>("PrinterOverlayHint");
            if (hint.Bounds.Width < 100)
            {
                problems.Add($"{language}: the hint column is only {hint.Bounds.Width:0.#} DIP wide.");
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });

    [Fact]
    public Task TextsFollowALanguageSwitch() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer(overlay: Custom));
            Assert.True(Ui.ShowsText(ui.Window, "Nozzle temperature"));

            Assert.True(new UiLanguage().Apply("de"));
            ui.Slot.RefreshTexts();
            ui.Window.Settle();

            Assert.True(Ui.ShowsText(ui.Window, "Einblendung"));
            Assert.True(Ui.ShowsText(ui.Window, "Ecke der Einblendung"));
            Assert.True(Ui.ShowsText(ui.Window, "Oben rechts"));
            Assert.True(Ui.ShowsText(ui.Window, "Düsentemperatur"));
            Assert.Equal("Normales Fenster und Vollbild", ui.ShownText("PrinterOverlayNormalHeader"));
            Assert.Equal("Mini-Vorschau", ui.ShownText("PrinterOverlayMiniHeader"));
            Assert.Equal(Strings.SettingsOverlayHint, ui.ShownText("PrinterOverlayHint"));
            Assert.StartsWith("Auftragsdaten", ui.ShownText("PrinterOverlayHint"), StringComparison.Ordinal);
            Assert.Equal("Druckername – Normales Fenster und Vollbild", AutomationProperties.GetName(Box(ui, "PrinterName", mini: false)));
            Assert.Equal("Dateiname – Mini-Vorschau", AutomationProperties.GetName(Box(ui, "FileName", mini: true)));
            Assert.Equal("Ecke der Einblendung", AutomationProperties.GetName(ui.Find<ComboBox>("PrinterOverlayCorner")));
            // The values stay.
            Assert.True(Box(ui, "PrinterName", mini: false).IsChecked);
            Assert.Equal(CameraOverlayCorner.TopRight, ui.ViewModel.SelectedPrinter!.SelectedOverlayCorner.Value);
        });

    private static string? FocusedId(Window window) =>
        (window.FocusManager?.GetFocusedElement() as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(AutomationProperties.GetAutomationId)
            .FirstOrDefault(static id => !string.IsNullOrEmpty(id));
}
