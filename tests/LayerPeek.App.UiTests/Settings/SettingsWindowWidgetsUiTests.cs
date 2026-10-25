using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

using LayerPeek.App.Composition;
using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Settings.Widgets;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// The Widgets tab of the real settings window (T-092, approved design "Návrh UI"): the list with its check boxes, mouse
/// and keyboard, the panels (without settings, Cool-down with the threshold of T-080), the "All widgets" section, the link
/// to the General tab, the layout saved by a camera window, automation ids and the fit in every language.
/// </summary>
public sealed class SettingsWindowWidgetsUiTests(HeadlessSession session)
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly string[] WidgetIds = ["status", "times", "temperatures", "print-name", "cooldown"];

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    private static PrinterDefinition Printer(string id, string name, bool camera = true, CameraOverlayLayout? layout = null) =>
        LinkPrinter(id, name, $"192.168.1.{20 + id.Length}") with { Camera = new CameraSettings(camera, StreamUri) { Overlay = layout ?? CameraOverlayLayout.Default } };

    /// <summary>Opens the window and the Widgets tab with a click on its header, like the user.</summary>
    private static async Task<SettingsWindowHarness> OpenAsync(
        UiTestScope scope,
        FakeEditorStore store,
        FakeLayoutModeLauncher? launcher = null,
        FakeLayoutAnnouncer? announcer = null)
    {
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store, layoutModeLauncher: launcher, layoutWriter: announcer);
        ui.Click("SettingsTabWidgets");
        Assert.Equal(SettingsViewModel.WidgetsTab, ui.ViewModel.SelectedTabIndex);
        return ui;
    }

    private static Task<SettingsWindowHarness> OpenAsync(UiTestScope scope, params PrinterDefinition[] printers) =>
        OpenAsync(scope, SettingsWindowHarness.StoreWith(printers));

    private static ListBoxItem Row(SettingsWindowHarness ui, string id) => ui.Find<ListBoxItem>($"WidgetsItem-{id}");

    private static CheckBox Box(SettingsWindowHarness ui, string id) => ui.Find<CheckBox>($"WidgetsVisible-{id}");

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        window.Settle();
    }

    private static string? FocusedId(Window window) =>
        (window.FocusManager?.GetFocusedElement() as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(AutomationProperties.GetAutomationId)
            .FirstOrDefault(static id => !string.IsNullOrEmpty(id));

    // ---- Defaults, mouse, saving ----

    [Fact]
    public Task TabShowsTheWidgetsOfThePrinterWithTheirPanel() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha", layout: CameraOverlayLayout.Default.WithVisible("times", visible: false)), Printer("b", "Beta"));

            TabItem tab = ui.Find<TabItem>("SettingsTabWidgets");
            Assert.Equal(1, ui.Find<TabControl>("SettingsTabs").IndexFromContainer(tab));
            Assert.Equal("Widgets", AutomationProperties.GetName(tab));
            Assert.False(ui.Window.IsShown<TextBlock>("SettingsTabWidgetsMarker"));
            Assert.Same(ui.ViewModel.Printers[0], ui.Find<ComboBox>("WidgetsPrinter").SelectedItem);
            Assert.Equal("Printer", AutomationProperties.GetName(ui.Find<ComboBox>("WidgetsPrinter")));
            Assert.False(ui.Window.IsShown<TextBlock>("WidgetsCameraOff"));
            Assert.False(ui.Window.IsShown<TextBlock>("WidgetsNoPrinters"));
            ListBox list = ui.Find<ListBox>("WidgetsList");
            Assert.Equal("Camera window widgets", AutomationProperties.GetName(list));
            Assert.Equal(5, list.ItemCount);
            Assert.Equal([true, false, true, true, true], WidgetIds.Select(id => Box(ui, id).IsChecked == true));
            Assert.Equal("Times, hidden", AutomationProperties.GetName(Row(ui, "times")));
            Assert.Equal("Show the Times widget", AutomationProperties.GetName(Box(ui, "times")));
            Assert.True(Row(ui, "status").IsSelected);
            Assert.False(ui.Window.IsShown<TextBlock>("WidgetsItemMarker-cooldown"));
            // Every widget icon is found in the icon resources (a valid IconKey).
            Assert.All(list.GetVisualDescendants().OfType<WidgetIcon>(), static icon => Assert.NotNull(icon.Data));
            Assert.Equal(5, list.GetVisualDescendants().OfType<WidgetIcon>().Count());

            Assert.Equal("Status", AutomationProperties.GetName(ui.Find<StackPanel>("WidgetsDetail")));
            Assert.Equal("Status", ui.ShownText("WidgetsDetailTitle"));
            Assert.Equal(Strings.CameraOverlayWidgetStatusDescription, ui.ShownText("WidgetsDetailDescription"));
            Assert.Equal(Strings.SettingsWidgetsNoSettings, ui.ShownText("WidgetsDetailNoSettings"));
            Assert.Equal(Strings.SettingsOverlayLayoutHint, ui.ShownText("WidgetsDetailLayoutHint"));
            Assert.False(ui.Window.IsShown<NumericUpDown>("WidgetCooldownThreshold"));

            Assert.True(ui.Window.IsShown<Grid>("WidgetsAllSection"));
            Assert.True(ui.Find<Button>("WidgetsEditLayout").IsEffectivelyEnabled);
            Assert.Equal(Strings.SettingsWidgetsEditLayoutHelp, AutomationProperties.GetHelpText(ui.Find<Button>("WidgetsEditLayout")));
            Assert.True(ui.Find<Button>("WidgetsReset").IsEffectivelyEnabled);
            Assert.Equal(Strings.SettingsWidgetsResetHelp, ToolTip.GetTip(ui.Find<Button>("WidgetsReset")));
            Assert.True(ui.Find<Button>("WidgetsApplyAll").IsEffectivelyEnabled);
            Assert.Equal(72, ui.Find<Slider>("WidgetsOpacity").Value);
            Assert.Equal("72%", ui.ShownText("WidgetsOpacityValue"));
            Assert.Equal(Strings.SettingsOverlayOpacityHint, ui.ShownText("WidgetsOpacityHint"));
            Assert.Equal("Card background opacity", AutomationProperties.GetName(ui.Find<Slider>("WidgetsOpacity")));
        });

    [Fact]
    public Task ClickOnACheckBoxSwitchesItSelectsTheRowAndSaves() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer("a", "Alpha"));
            SettingsWindowHarness ui = await OpenAsync(scope, store);

            ui.Window.Click(Box(ui, "temperatures"));

            Assert.False(Box(ui, "temperatures").IsChecked);
            Assert.True(Row(ui, "temperatures").IsSelected);
            Assert.Equal("Temperatures", ui.ShownText("WidgetsDetailTitle"));
            Assert.Equal("Temperatures, hidden", AutomationProperties.GetName(Row(ui, "temperatures")));

            // A click elsewhere in a row only selects it.
            ui.Window.Click(Row(ui, "print-name").GetVisualDescendants().OfType<TextBlock>().First(static block => block.Text == "Print name"));
            Assert.True(Row(ui, "print-name").IsSelected);
            Assert.True(Box(ui, "print-name").IsChecked);

            await ui.SaveAsync();
            Assert.Equal(CameraOverlayLayout.Default.WithVisible("temperatures", visible: false), Assert.Single(store.Saves).Updated.Printers[0].Camera.Overlay);
        });

    [Fact]
    public Task KeyboardMovesThroughTheListAndSpaceSwitchesTheVisibility() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha"));
            ListBoxItem first = Row(ui, "status");
            Assert.True(first.Focus(NavigationMethod.Tab));

            Press(ui.Window, PhysicalKey.ArrowDown);
            Assert.True(Row(ui, "times").IsSelected);
            Assert.Equal("Times", ui.ShownText("WidgetsDetailTitle"));

            Press(ui.Window, PhysicalKey.Space);
            Assert.False(Box(ui, "times").IsChecked);
            Assert.True(Row(ui, "times").IsSelected);
            Press(ui.Window, PhysicalKey.Space);
            Assert.True(Box(ui, "times").IsChecked);

            Press(ui.Window, PhysicalKey.End);
            Assert.True(Row(ui, "cooldown").IsSelected);
            Assert.True(ui.Window.IsShown<NumericUpDown>("WidgetCooldownThreshold"));
            Press(ui.Window, PhysicalKey.Home);
            Assert.True(Row(ui, "status").IsSelected);
            Assert.Equal("WidgetsItem-status", FocusedId(ui.Window));
        });

    [Fact]
    public Task TabMovesThroughTheTabInReadingOrderWithTheListAsOneStop() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha"), Printer("b", "Beta"));
            ui.ViewModel.SelectedWidget = ui.ViewModel.Widgets.Single(static widget => widget.Id == "cooldown");
            ui.Window.Settle();
            Assert.True(ui.Find<TabItem>("SettingsTabWidgets").Focus());

            List<string> order = [];
            object? start = ui.Window.FocusManager?.GetFocusedElement();
            for (int i = 0; i < 40; i++)
            {
                Press(ui.Window, PhysicalKey.Tab);
                object? focused = ui.Window.FocusManager?.GetFocusedElement();
                if (focused is null || ReferenceEquals(focused, start))
                {
                    break;
                }

                string id = FocusedId(ui.Window) ?? focused.GetType().Name;
                order.Add(id.StartsWith("WidgetsItem-", StringComparison.Ordinal) ? "WidgetsList" : id);
            }

            string[] expected =
            [
                "WidgetsPrinter", "WidgetsList", "WidgetCooldownThreshold", "WidgetCooldownApplyAll", "WidgetCooldownNotifyLink",
                "WidgetsEditLayout", "WidgetsReset", "WidgetsApplyAll", "WidgetsOpacity", "SettingsSave", "SettingsCancel",
            ];
            Assert.Equal(expected, order.Where(id => expected.Contains(id)).Distinct());
            // The list is one stop; its check boxes are none.
            Assert.Equal(1, order.Count(static id => id == "WidgetsList"));
            Assert.DoesNotContain(order, static id => id.StartsWith("WidgetsVisible-", StringComparison.Ordinal));
        });

    // ---- Camera off, no printer, new printer ----

    [Fact]
    public Task CameraOffDisablesTheCheckBoxesAndEditLayoutOnly() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha", camera: false), Printer("b", "Beta"));

            Assert.Equal(Strings.SettingsCameraWindowCameraOff, ui.ShownText("WidgetsCameraOff"));
            Assert.All(ui.ViewModel.Widgets, widget => Assert.False(Box(ui, widget.Id).IsEffectivelyEnabled));
            Assert.All(ui.ViewModel.Widgets, widget => Assert.True(Box(ui, widget.Id).IsChecked));
            Button edit = ui.Find<Button>("WidgetsEditLayout");
            Assert.False(edit.IsEffectivelyEnabled);
            Assert.Equal("Turn on this printer's camera and save the settings.", ToolTip.GetTip(edit));
            Assert.True(ToolTip.GetShowOnDisabled(edit));
            Assert.True(ui.Find<Button>("WidgetsReset").IsEffectivelyEnabled);
            Assert.True(ui.Find<Button>("WidgetsApplyAll").IsEffectivelyEnabled);
            Assert.True(ui.Find<Slider>("WidgetsOpacity").IsEffectivelyEnabled);

            // The list still selects, and the Cool-down panel stays usable.
            ui.Window.Click(Row(ui, "cooldown"));
            Press(ui.Window, PhysicalKey.Space);
            Assert.True(Box(ui, "cooldown").IsChecked);
            Assert.True(ui.Find<NumericUpDown>("WidgetCooldownThreshold").IsEffectivelyEnabled);
        });

    [Fact]
    public Task WithoutPrintersOnlyTheHintAndTheAllWidgetsSectionAreShown() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);

            Assert.Equal(Strings.SettingsWidgetsNoPrinters, ui.ShownText("WidgetsNoPrinters"));
            Assert.False(ui.Window.IsShown<ComboBox>("WidgetsPrinter"));
            Assert.False(ui.Window.IsShown<ListBox>("WidgetsList"));
            Assert.False(ui.Window.IsShown<StackPanel>("WidgetsDetail"));
            Assert.False(ui.Find<Button>("WidgetsEditLayout").IsEffectivelyEnabled);
            Assert.False(ui.Find<Button>("WidgetsReset").IsEffectivelyEnabled);
            Assert.False(ui.Find<Button>("WidgetsApplyAll").IsEffectivelyEnabled);
            Assert.True(ui.Find<Slider>("WidgetsOpacity").IsEffectivelyEnabled);
        });

    [Fact]
    public Task NewPrinterCannotEditTheLayoutYet() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha"));
            ui.ViewModel.AddPrinterCommand.Execute(null);
            ui.ViewModel.SelectedPrinter!.CameraEnabled = true;
            ui.Window.Settle();

            Button edit = ui.Find<Button>("WidgetsEditLayout");
            Assert.False(edit.IsEffectivelyEnabled);
            Assert.Equal("Save the settings first.", ToolTip.GetTip(edit));
            Assert.Equal("Save the settings first.", AutomationProperties.GetHelpText(edit));
        });

    // ---- Panels ----

    [Fact]
    public Task CooldownPanelShowsTheThresholdHintAndNotificationState() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha"), Printer("b", "Beta"));
            ui.Window.Click(Row(ui, "cooldown"));

            Assert.Equal("Cool-down", ui.ShownText("WidgetsDetailTitle"));
            Assert.Equal(Strings.CameraOverlayWidgetCooldownDescription, ui.ShownText("WidgetsDetailDescription"));
            Assert.False(ui.Window.IsShown<TextBlock>("WidgetsDetailNoSettings"));
            NumericUpDown threshold = ui.Find<NumericUpDown>("WidgetCooldownThreshold");
            Assert.Equal(50m, threshold.Value);
            Assert.Equal(30m, threshold.Minimum);
            Assert.Equal(100m, threshold.Maximum);
            Assert.Equal("Nozzle temperature threshold (°C)", AutomationProperties.GetName(threshold));
            Assert.Equal(Strings.SettingsCooldownThresholdHint, AutomationProperties.GetHelpText(threshold));
            Assert.Equal("Below this temperature the printer is safe to turn off.", ui.ShownText("WidgetCooldownThresholdHint"));
            Assert.False(ui.Window.IsShown<TextBlock>("WidgetCooldownThresholdError"));
            Assert.Equal("“Printer cooled down” notification: off", ui.ShownText("WidgetCooldownNotifyState"));
            Assert.Equal("Change on the General tab", ui.Find<HyperlinkButton>("WidgetCooldownNotifyLink").Content);

            ui.ViewModel.NotifyCooledDown = true;
            ui.Window.Settle();
            Assert.Equal("“Printer cooled down” notification: on", ui.ShownText("WidgetCooldownNotifyState"));
            ui.ViewModel.NotificationsEnabled = false;
            ui.Window.Settle();
            Assert.Equal("Notifications are turned off.", ui.ShownText("WidgetCooldownNotifyState"));

            // Another printer: the same widget, its values.
            ui.ViewModel.Printers[1].CooldownThresholdCelsius = 77;
            ui.ViewModel.SelectedPrinter = ui.ViewModel.Printers[1];
            ui.Window.Settle();
            Assert.True(Row(ui, "cooldown").IsSelected);
            Assert.Equal(77m, ui.Find<NumericUpDown>("WidgetCooldownThreshold").Value);
        });

    [Fact]
    public Task ThresholdErrorReplacesTheHintBlocksSaveAndMarksWidgetPrinterAndTab() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer("a", "Alpha"), Printer("b", "Beta"));
            SettingsWindowHarness ui = await OpenAsync(scope, store);
            ui.Window.Click(Row(ui, "cooldown"));
            ui.ViewModel.SelectedPrinter!.CooldownThresholdCelsius = 101;
            ui.Click("SettingsTabGeneral");

            await ui.SaveAsync();

            Assert.Empty(store.Saves);
            Assert.True(ui.Find<TabItem>("SettingsTabWidgets").IsSelected);
            Assert.True(ui.Window.IsShown<TextBlock>("SettingsTabWidgetsMarker"));
            Assert.Equal("Widgets – contains errors", AutomationProperties.GetName(ui.Find<TabItem>("SettingsTabWidgets")));
            Assert.True(ui.Window.IsShown<TextBlock>("WidgetsItemMarker-cooldown"));
            Assert.False(ui.Window.IsShown<TextBlock>("WidgetsItemMarker-status"));
            Assert.True(Row(ui, "cooldown").IsSelected);
            Assert.Equal("Enter a whole number from 30 to 100.", ui.ShownText("WidgetCooldownThresholdError"));
            Assert.False(ui.Window.IsShown<TextBlock>("WidgetCooldownThresholdHint"));
            // The printer is marked in the printer selection and in the list of the Printers tab.
            Assert.True(ui.ViewModel.Printers[0].ShowsErrorMarker);
            Assert.Contains(ui.Find<ComboBox>("WidgetsPrinter").GetVisualDescendants().OfType<TextBlock>(), static block => block.Text == "!" && block.IsEffectivelyVisible);

            // The error does not block switching widgets or printers.
            ui.Window.Click(Row(ui, "status"));
            Assert.True(ui.Window.IsShown<TextBlock>("WidgetsItemMarker-cooldown"));
            ui.Window.Click(Row(ui, "cooldown"));
            ui.ViewModel.SelectedPrinter.CooldownThresholdCelsius = 64;
            ui.Window.Settle();
            Assert.False(ui.Window.IsShown<TextBlock>("WidgetsItemMarker-cooldown"));
            Assert.False(ui.Window.IsShown<TextBlock>("SettingsTabWidgetsMarker"));
            Assert.Equal(Strings.SettingsCooldownThresholdHint, ui.ShownText("WidgetCooldownThresholdHint"));
            await ui.SaveAsync();
            Assert.Equal(64, Assert.Single(store.Saves).Updated.Printers[0].CooldownThresholdCelsius);
        });

    [Fact]
    public Task LinkOpensTheGeneralTabAndFocusesTheCooledDownSwitch() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha"));
            ui.Window.Click(Row(ui, "cooldown"));

            ui.Click("WidgetCooldownNotifyLink");
            ui.Window.Settle();

            Assert.True(ui.Find<TabItem>("SettingsTabGeneral").IsSelected);
            Assert.Equal("SettingsNotifyCooledDown", FocusedId(ui.Window));
            Assert.Equal(Strings.SettingsNotifyCooledDownHelp, AutomationProperties.GetHelpText(ui.Find<CheckBox>("SettingsNotifyCooledDown")));
            Assert.Equal("The nozzle temperature threshold is set per printer on the Widgets tab, Cool-down widget.", Strings.SettingsNotifyCooledDownHelp);
        });

    // ---- "… for all printers", Restore defaults, Edit layout ----

    [Fact]
    public Task ForAllPrintersButtonsAreDisabledWithASinglePrinterAndSayWhy() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha"));
            ui.Window.Click(Row(ui, "cooldown"));

            foreach (string id in (ReadOnlySpan<string>)["WidgetsApplyAll", "WidgetCooldownApplyAll"])
            {
                Button button = ui.Find<Button>(id);
                Assert.True(button.IsEffectivelyVisible);
                Assert.False(button.IsEffectivelyEnabled);
                Assert.Equal("Only one printer is set up.", ToolTip.GetTip(button));
                Assert.True(ToolTip.GetShowOnDisabled(button));
            }
        });

    [Fact]
    public Task ForAllPrintersButtonsCopyToThreePrinters() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer("a", "Alpha"), Printer("b", "Beta"), Printer("c", "Gamma"));
            SettingsWindowHarness ui = await OpenAsync(scope, store);
            ui.Window.Click(Box(ui, "print-name"));
            ui.Click("WidgetsApplyAll");

            Assert.Equal(Strings.SettingsOverlayAppliedAll, ui.ShownText("SettingsStatus"));
            ui.Window.Click(Row(ui, "cooldown"));
            ui.ViewModel.SelectedPrinter!.CooldownThresholdCelsius = 35;
            ui.Click("WidgetCooldownApplyAll");
            Assert.Equal(Strings.SettingsCooldownAppliedAll, ui.ShownText("SettingsStatus"));
            Assert.Null(ToolTip.GetTip(ui.Find<Button>("WidgetCooldownApplyAll")));

            await ui.SaveAsync();
            AppSettings saved = Assert.Single(store.Saves).Updated;
            Assert.All(saved.Printers, static printer => Assert.Equal(CameraOverlayLayout.Default.WithVisible("print-name", visible: false), printer.Camera.Overlay));
            Assert.All(saved.Printers, static printer => Assert.Equal(35, printer.CooldownThresholdCelsius));
        });

    [Fact]
    public Task RestoreDefaultsShowsTheDefaultLayout() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha", layout: CameraOverlayLayout.Default.WithVisible("status", visible: false)));
            Assert.False(Box(ui, "status").IsChecked);

            ui.Click("WidgetsReset");

            Assert.True(Box(ui, "status").IsChecked);
            Assert.Equal(Strings.SettingsWidgetsResetDone, ui.ShownText("SettingsStatus"));
        });

    [Fact]
    public Task EditLayoutStartsTheLayoutModeWithTheEditedLayout() =>
        session.RunAsync(static async scope =>
        {
            FakeLayoutModeLauncher launcher = new();
            SettingsWindowHarness ui = await OpenAsync(scope, SettingsWindowHarness.StoreWith(Printer("a", "Alpha")), launcher);
            ui.Window.Click(Box(ui, "times"));

            ui.Click("WidgetsEditLayout");

            (string id, CameraOverlayLayout? layout) = Assert.Single(launcher.Starts);
            Assert.Equal("a", id);
            Assert.Equal(CameraOverlayLayout.Default.WithVisible("times", visible: false), layout);
            Assert.True(ui.IsOpen);
        });

    [Fact]
    public Task LayoutSavedByTheCameraWindowIsShownAndOtherEditsStay() =>
        session.RunAsync(static async scope =>
        {
            FakeLayoutAnnouncer announcer = new();
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer("a", "Alpha"));
            SettingsWindowHarness ui = await OpenAsync(scope, store, announcer: announcer);
            ui.ViewModel.SelectedPrinter!.Name = "Alpha 2";
            ui.Window.Click(Box(ui, "status"));

            announcer.Announce("a", CameraOverlayLayout.Default.WithVisible("temperatures", visible: false));
            ui.Window.Settle();

            Assert.True(Box(ui, "status").IsChecked);
            Assert.False(Box(ui, "temperatures").IsChecked);
            Assert.Equal("Alpha 2", ui.ViewModel.Printers[0].Name);
            await ui.SaveAsync();
            PrinterDefinition saved = Assert.Single(store.Saves).Updated.Printers[0];
            Assert.Equal("Alpha 2", saved.Name);
            Assert.Equal(CameraOverlayLayout.Default.WithVisible("temperatures", visible: false), saved.Camera.Overlay);
        });

    [Fact]
    public Task OpacitySliderChangesTheSavedValue() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer("a", "Alpha"));
            SettingsWindowHarness ui = await OpenAsync(scope, store);
            Slider slider = ui.Find<Slider>("WidgetsOpacity");
            Assert.Equal(30, slider.Minimum);
            Assert.Equal(95, slider.Maximum);
            Assert.True(slider.Focus(NavigationMethod.Tab));

            Press(ui.Window, PhysicalKey.ArrowRight);
            Press(ui.Window, PhysicalKey.ArrowRight);

            Assert.Equal("74%", ui.ShownText("WidgetsOpacityValue"));
            await ui.SaveAsync();
            Assert.Equal(74, Assert.Single(store.Saves).Updated.CameraOverlayOpacityPercent);
        });

    // ---- Language ----

    [Fact]
    public Task TabFollowsALanguageSwitch() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha"));
            ui.Window.Click(Row(ui, "cooldown"));

            Assert.True(new UiLanguage().Apply("cs"));
            ui.Slot.RefreshTexts();
            ui.Window.Settle();

            Assert.Equal("Widgety", AutomationProperties.GetName(ui.Find<TabItem>("SettingsTabWidgets")));
            Assert.Equal("Vychladnutí", ui.ShownText("WidgetsDetailTitle"));
            Assert.Equal("Vychladnutí, zobrazený", AutomationProperties.GetName(Row(ui, "cooldown")));
            Assert.Equal("Upozornění „Tiskárna vychladla“: vypnuté", ui.ShownText("WidgetCooldownNotifyState"));
            Assert.True(Ui.ShowsText(ui.Window, "Všechny widgety"));
        });

    // ---- Fit (design 3.3): no scroll bar at 780 × 532, a scroll bar when smaller ----

    [Theory]
    [MemberData(nameof(Languages))]
    public Task WidgetsTabWithoutPrintersAndEveryWidgetFitsInEveryLanguage(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            List<string> problems = [];
            SettingsWindowHarness empty = await OpenAsync(scope);
            problems.AddRange(Overflow(empty, $"{language} / no printer"));
            empty.Window.Close();

            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "MK4S workshop"), Printer("b", "Beta"));
            foreach (WidgetItemViewModel widget in ui.ViewModel.Widgets)
            {
                ui.ViewModel.SelectedWidget = widget;
                ui.Window.Settle();
                problems.AddRange(Overflow(ui, $"{language} / {widget.Id}"));
                // Design 4.1: the description takes at most 2 lines.
                if (ui.Window.IsShown<TextBlock>("WidgetsDetailDescription"))
                {
                    TextBlock description = ui.Find<TextBlock>("WidgetsDetailDescription");
                    if (description.TextLayout.TextLines.Any(static line => line.HasCollapsed))
                    {
                        problems.Add($"{language} / {widget.Id}: the description is cut.");
                    }
                }
            }

            // Design 3.4: the buttons of the left column stay within 300 DIP on one line.
            foreach (string id in (ReadOnlySpan<string>)["WidgetsEditLayout", "WidgetsReset", "WidgetsApplyAll"])
            {
                Button button = ui.Find<Button>(id);
                TextBlock text = button.GetVisualDescendants().OfType<TextBlock>().First();
                if (button.Bounds.Width > 300.5 || text.TextLayout.TextLines.Count > 1)
                {
                    problems.Add($"{language}: {id} is {button.Bounds.Width:0.#} DIP wide with {text.TextLayout.TextLines.Count} lines.");
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });

    [Fact]
    public Task SmallWindowScrollsAndKeepsEverythingReachable() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha"), Printer("b", "Beta"));
            ui.Window.Click(Row(ui, "cooldown"));
            Assert.Empty(Overflow(ui, "designed size"));

            ui.Window.Width = 640;
            ui.Window.Height = 400;
            ui.Window.Settle();

            ScrollViewer viewer = ui.Window.GetVisualDescendants().OfType<ScrollViewer>().Single(static viewer => viewer.Name == "WidgetsScroll");
            Assert.True(viewer.Extent.Height > viewer.Viewport.Height + 1, "The tab does not scroll in a small window.");
            // The last control is reachable: brought into view, it is inside the viewport.
            Slider slider = ui.Find<Slider>("WidgetsOpacity");
            slider.BringIntoView();
            ui.Window.Settle();
            Point top = slider.TranslatePoint(default, viewer)!.Value;
            Assert.InRange(top.Y, 0, viewer.Viewport.Height - slider.Bounds.Height);
            ui.Click("WidgetsEditLayout");
        });

    /// <summary>The Widgets tab scrolls, or a text that should fit on one line is cut.</summary>
    private static IEnumerable<string> Overflow(SettingsWindowHarness ui, string context)
    {
        ScrollViewer viewer = ui.Window.GetVisualDescendants().OfType<ScrollViewer>().Single(static viewer => viewer.Name == "WidgetsScroll");
        if (viewer.Extent.Height > viewer.Viewport.Height + 0.01 || viewer.Extent.Width > viewer.Viewport.Width + 0.01)
        {
            yield return $"{context}: the tab is {viewer.Extent} in a viewport of {viewer.Viewport}.";
        }

        foreach (TextBlock block in viewer.GetVisualDescendants().OfType<TextBlock>())
        {
            if (block.IsEffectivelyVisible && block.TextLayout.TextLines.Any(static line => line.HasCollapsed) && AutomationProperties.GetAutomationId(block) is "WidgetsCameraOff" or "WidgetsOpacityHint" or "WidgetsDetailDescription")
            {
                yield return $"{context}: '{block.Text}' is cut.";
            }
        }
    }
}
