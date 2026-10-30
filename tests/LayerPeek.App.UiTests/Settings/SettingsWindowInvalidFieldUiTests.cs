using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// T-112: an invalid field of the settings window is marked by a 2 DIP outline in <c>SettingsErrorBrush</c> (class
/// <c>invalid</c>) exactly while its error is shown, in every state (pointer over, focused), without changing its size or
/// moving its text; a screen reader gets its error as the help text of the focused element (also of the TextBox inside a
/// NumericUpDown or an editable ComboBox); a failed Save, and a refused Test connection / Test camera within its sub-tab,
/// focuses the first invalid field. Every shown field error has its marked field (guard against a field forgotten later).
/// </summary>
public sealed class SettingsWindowInvalidFieldUiTests(HeadlessSession session)
{
    private const string Invalid = "invalid";

    private static readonly Color LightErrorColor = Color.Parse("#C42B1C");
    private static readonly Color DarkErrorColor = Color.Parse("#FF99A4");
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    /// <summary>Error texts with <c>Classes="error"</c> that belong to no field (the notification state of the General tab, T-101).</summary>
    private static readonly HashSet<string> NonFieldErrors = ["SettingsNotificationsTurnedOff", "SettingsTestNotificationResult"];

    /// <summary>The validated fields of the window (the table of the task, without the image import, which is no field error).</summary>
    private static readonly Dictionary<string, FieldCase> Cases = new()
    {
        [SettingsFieldIds.PrinterName] = new("PrinterNameError", View.Printer, ui => Printer(ui).Name = new string('n', SettingsViewModel.MaxNameLength + 1), ui => Printer(ui).Name = "MK4 workshop"),
        [SettingsFieldIds.PrinterAddress] = new("PrinterAddressError", View.Printer, ui => Printer(ui).Address = "ftp://printer", ui => Printer(ui).Address = "192.168.1.20"),
        [SettingsFieldIds.PrinterUserName] = new("PrinterUserNameError", View.Printer, ui => Printer(ui).UserName = "ma:ker", ui => Printer(ui).UserName = "maker"),
        [SettingsFieldIds.PrinterPassword] = new("PrinterPasswordError", View.Printer, ui => Printer(ui).Password = "\uD800", ui => Printer(ui).Password = "secret"),
        [SettingsFieldIds.PrinterCameraUrl] = new("PrinterCameraUrlError", View.Camera, ui => Printer(ui).CameraUrl = string.Empty, ui => Printer(ui).CameraUrl = StreamUri.ToString()),
        [SettingsFieldIds.PrinterCameraWindowNormalSize] = new("PrinterCameraWindowNormalSizeError", View.CameraWindow, ui => Printer(ui).NormalSizeText = "100 x 50", ui => Printer(ui).NormalSizeText = "1280 x 720"),
        [SettingsFieldIds.PrinterCameraWindowMiniSize] = new("PrinterCameraWindowMiniSizeError", View.CameraWindow, ui => Printer(ui).MiniSizeText = "20000 x 90", ui => Printer(ui).MiniSizeText = "320 x 180"),
        [SettingsFieldIds.WidgetCooldownNozzleThreshold] = new("WidgetCooldownThresholdError", View.Cooldown, ui => Printer(ui).CooldownNozzleThresholdCelsius = 120, ui => Printer(ui).CooldownNozzleThresholdCelsius = 40),
        [SettingsFieldIds.WidgetCooldownBedThreshold] = new("WidgetCooldownThresholdError", View.Cooldown, ui => Printer(ui).CooldownBedThresholdCelsius = 120, ui => Printer(ui).CooldownBedThresholdCelsius = 40),
        [SettingsFieldIds.SettingsPollingInterval] = new("SettingsPollingIntervalError", View.General, ui => ui.ViewModel.PollingIntervalSeconds = 0, ui => ui.ViewModel.PollingIntervalSeconds = 5),
        [SettingsFieldIds.SettingsRemainingTime] = new("SettingsRemainingTimeError", View.General, ui => ui.ViewModel.RemainingTimeMinutes = 2000, ui => ui.ViewModel.RemainingTimeMinutes = 10),
    };

    private enum View
    {
        Printer,
        Camera,
        CameraWindow,
        Cooldown,
        General,
    }

    public static TheoryData<string> Fields => [.. Cases.Keys];

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    // ---- The outline ----

    [Theory]
    [MemberData(nameof(Fields))]
    public Task AnInvalidFieldHasTheRedOutlineOnlyWhileItsErrorIsShown(string field) =>
        session.RunAsync(async scope =>
        {
            FieldCase test = Cases[field];
            SettingsWindowHarness ui = await OpenAsync(scope);
            Show(ui, test.View);
            Control control = ui.Find<Control>(field);
            Assert.DoesNotContain(Invalid, control.Classes);
            Color validColor = OutlineColor(control);
            Assert.NotEqual(LightErrorColor, validColor);

            test.Break(ui);
            await SaveAsync(ui);

            control = ui.Find<Control>(field);
            Assert.NotNull(ui.ShownText(test.Error));
            Assert.Contains(Invalid, control.Classes);
            AssertRedOutline(control, LightErrorColor, "after Save (focused)");
            AssertEveryShownErrorHasItsMarkedField(ui);

            // The red stays under the pointer and without the focus (it beats the pointer-over and focus colours of the theme).
            ui.Window.MouseMove(Ui.CentreOf(control, ui.Window));
            ui.Window.Settle();
            AssertRedOutline(control, LightErrorColor, "focused, pointer over");
            ui.Window.FocusManager!.Focus(null);
            ui.Window.Settle();
            AssertRedOutline(control, LightErrorColor, "pointer over");
            ui.Window.MouseMove(new Point(1, 1));
            ui.Window.Settle();
            AssertRedOutline(control, LightErrorColor, "at rest");

            test.Fix(ui);
            ui.Window.Settle();

            Assert.Null(ui.ShownText(test.Error));
            Assert.DoesNotContain(Invalid, control.Classes);
            Assert.Equal(validColor, OutlineColor(control));
            Assert.Equal(1, OutlineThickness(control));
        });

    [Fact]
    public Task AnInvalidFieldIsNotMarkedBeforeItsErrorIsShown() =>
        session.RunAsync(static async scope =>
        {
            // A new printer: its empty address is invalid, but the error shows only after an edit or a save attempt (T-033).
            FakeEditorStore store = SettingsWindowHarness.StoreWith();
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
            ui.ViewModel.AddPrinterCommand.Execute(null);
            ui.Window.Settle();
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            Assert.True(printer.HasConnectionErrors);

            Assert.Null(ui.ShownText("PrinterAddressError"));
            Assert.False(printer.HasAddressError);
            Assert.DoesNotContain(Invalid, ui.AddressBox.Classes);
            Assert.DoesNotContain(Invalid, ui.NameBox.Classes);

            await SaveAsync(ui);

            // The empty name and address of the new printer are both invalid; the valid user name and password are not marked.
            Assert.NotNull(ui.ShownText("PrinterAddressError"));
            Assert.Contains(Invalid, ui.AddressBox.Classes);
            Assert.NotNull(ui.ShownText("PrinterNameError"));
            Assert.Contains(Invalid, ui.NameBox.Classes);
            Assert.Same(ui.NameBox, Focused(ui));
            Assert.DoesNotContain(Invalid, ui.UserNameBox.Classes);
            Assert.DoesNotContain(Invalid, ui.PasswordBox.Classes);
        });

    [Fact]
    public Task CooldownMarksOnlyTheInvalidThreshold() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);
            Show(ui, View.Cooldown);
            PrinterEditorViewModel printer = Printer(ui);
            string error = Format(Strings.SettingsCooldownThresholdError, Core.Cooldown.CooldownThreshold.MinCelsius, Core.Cooldown.CooldownThreshold.MaxCelsius);

            printer.CooldownBedThresholdCelsius = 120;
            await SaveAsync(ui);
            AssertCooldown(ui, nozzle: false, bed: true, error);

            printer.CooldownBedThresholdCelsius = 40;
            printer.CooldownNozzleThresholdCelsius = 10;
            await SaveAsync(ui);
            AssertCooldown(ui, nozzle: true, bed: false, error);

            printer.CooldownBedThresholdCelsius = 101;
            await SaveAsync(ui);
            AssertCooldown(ui, nozzle: true, bed: true, error);

            printer.CooldownNozzleThresholdCelsius = 50;
            printer.CooldownBedThresholdCelsius = 40;
            ui.Window.Settle();
            Assert.Null(ui.ShownText("WidgetCooldownThresholdError"));
            Assert.DoesNotContain(Invalid, ui.Find<NumericUpDown>("WidgetCooldownNozzleThreshold").Classes);
            Assert.DoesNotContain(Invalid, ui.Find<NumericUpDown>("WidgetCooldownBedThreshold").Classes);
        });

    [Fact]
    public Task TheOutlineUsesTheErrorColourOfTheDarkTheme() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);
            ui.Window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            Printer(ui).Address = "ftp://printer";
            ui.ViewModel.PollingIntervalSeconds = 0;
            Printer(ui).NormalSizeText = "1 x 1";
            ui.Window.Settle();

            AssertRedOutline(ui.AddressBox, DarkErrorColor, "dark");
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraWindowPrinterTab;
            ui.Window.Settle();
            AssertRedOutline(ui.Find<ComboBox>(SettingsFieldIds.PrinterCameraWindowNormalSize), DarkErrorColor, "dark");
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.GeneralTab;
            ui.Window.Settle();
            AssertRedOutline(ui.Find<NumericUpDown>(SettingsFieldIds.SettingsPollingInterval), DarkErrorColor, "dark");
        });

    [Fact]
    public Task ADisabledFieldIsNeverMarked() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);
            PrinterEditorViewModel printer = Printer(ui);
            printer.NormalSizeText = "100 x 50";
            await SaveAsync(ui);
            ComboBox size = ui.Find<ComboBox>(SettingsFieldIds.PrinterCameraWindowNormalSize);
            Assert.Contains(Invalid, size.Classes);

            // The camera off disables the camera window fields; their sizes are then not checked (T-033 Q9).
            printer.CameraEnabled = false;
            ui.Window.Settle();
            Assert.False(size.IsEffectivelyEnabled);
            Assert.DoesNotContain(Invalid, size.Classes);
        });

    [Fact]
    public Task TheDisabledUserNameOfAPrusaConnectPrinterIsNeverMarked() =>
        session.RunAsync(static async scope =>
        {
            // Prusa Connect disables the user name, whose error is only for PrusaLink.
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(CloudPrinter("c1", "Cloud XL")));
            Printer(ui).UserName = "ma:ker";
            await SaveAsync(ui);

            Assert.False(ui.UserNameBox.IsEffectivelyEnabled);
            Assert.Null(ui.ShownText("PrinterUserNameError"));
            Assert.DoesNotContain(Invalid, ui.UserNameBox.Classes);
        });

    // ---- No layout shift ----

    [Theory]
    [MemberData(nameof(Fields))]
    public Task TheOutlineChangesNeitherTheSizeOfTheFieldNorThePlaceOfItsText(string field) =>
        session.RunAsync(async scope =>
        {
            FieldCase test = Cases[field];
            SettingsWindowHarness ui = await OpenAsync(scope);
            Show(ui, test.View);
            Control control = ui.Find<Control>(field);
            Geometry valid = GeometryOf(ui, control, focused: false);
            Geometry validFocused = GeometryOf(ui, control, focused: true);
            Assert.Equal(valid, validFocused);

            test.Break(ui);
            await SaveAsync(ui);

            control = ui.Find<Control>(field);
            Assert.Contains(Invalid, control.Classes);
            Assert.Equal(valid, GeometryOf(ui, control, focused: true));
            Assert.Equal(valid, GeometryOf(ui, control, focused: false));
        });

    // ---- The focus after Save, Test connection and Test camera ----

    [Fact]
    public Task SaveFocusesTheFirstInvalidFieldOfThePrinterSubTab() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);
            Printer(ui).Address = "ftp://printer";
            Printer(ui).Password = "\uD800";
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.GeneralTab;
            ui.Window.Settle();

            await SaveAsync(ui);

            Assert.True(ui.Find<TabItem>("PrinterTabConnection").IsSelected);
            Assert.Same(ui.AddressBox, Focused(ui));
            AssertFocusedPeer(ui, Strings.SettingsAddress, ui.ShownText("PrinterAddressError"));
        });

    [Fact]
    public Task SaveFocusesTheTextOfTheInvalidCameraWindowSize() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);
            Printer(ui).NormalSizeText = "100 x 50";

            await SaveAsync(ui);

            ComboBox size = ui.Find<ComboBox>(SettingsFieldIds.PrinterCameraWindowNormalSize);
            TextBox text = Part<TextBox>(size, "PART_EditableTextBox");
            Assert.Same(text, Focused(ui));
            Assert.True(text.IsFocused);
            AssertFocusedPeer(ui, Printer(ui).NormalSizeName, ui.ShownText("PrinterCameraWindowNormalSizeError"));
            // The valid size next to it keeps no help text (it has no hint).
            Assert.Null(AutomationProperties.GetHelpText(ui.Find<ComboBox>(SettingsFieldIds.PrinterCameraWindowMiniSize)));
        });

    [Fact]
    public Task SaveFocusesTheTextOfTheInvalidPollingInterval() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);
            ui.ViewModel.PollingIntervalSeconds = 0;
            ui.ViewModel.RemainingTimeMinutes = 2000;

            await SaveAsync(ui);

            Assert.True(ui.Find<TabItem>("SettingsTabGeneral").IsSelected);
            NumericUpDown interval = ui.Find<NumericUpDown>(SettingsFieldIds.SettingsPollingInterval);
            TextBox text = Part<TextBox>(interval, "PART_TextBox");
            Assert.Same(text, Focused(ui));
            AssertFocusedPeer(ui, Strings.SettingsPollingInterval, ui.ShownText("SettingsPollingIntervalError"));
            // Focused, the inner TextBox draws no accent over the red outline (design 7).
            Assert.Equal(LightErrorColor, ColorOf(Part<Border>(text, "PART_BorderElement").BorderBrush));
        });

    [Fact]
    public Task SaveFocusesTheInvalidBedThreshold() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);
            Printer(ui).CooldownBedThresholdCelsius = 120;

            await SaveAsync(ui);

            Assert.True(ui.Find<TabItem>("SettingsTabWidgets").IsSelected);
            NumericUpDown bed = ui.Find<NumericUpDown>(SettingsFieldIds.WidgetCooldownBedThreshold);
            Assert.Same(Part<TextBox>(bed, "PART_TextBox"), Focused(ui));
            AssertFocusedPeer(ui, Strings.SettingsCooldownBedThreshold, ui.ShownText("WidgetCooldownThresholdError"));
        });

    [Fact]
    public Task ARefusedConnectionTestFocusesTheFirstInvalidFieldOfItsSubTab() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);
            Printer(ui).UserName = "ma:ker";
            ui.Window.Settle();

            ui.Click("PrinterTestConnection");
            ui.Window.Settle();

            Assert.Equal(Strings.SettingsFixErrors, ui.ShownText("PrinterTestResult"));
            Assert.Same(ui.UserNameBox, Focused(ui));
            AssertFocusedPeer(ui, Strings.SettingsUserName, ui.ShownText("PrinterUserNameError"));
        });

    [Fact]
    public Task ARefusedCameraTestFocusesTheCameraAddress() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;
            Printer(ui).CameraUrl = "rtsp://user:secret@192.168.0.227/live";
            ui.Window.Settle();

            ui.Click("PrinterCameraTest");
            ui.Window.Settle();

            Assert.Equal(Strings.SettingsFixErrors, ui.ShownText("PrinterCameraTestResult"));
            TextBox url = ui.Find<TextBox>(SettingsFieldIds.PrinterCameraUrl);
            Assert.Same(url, Focused(ui));
            AssertFocusedPeer(ui, Strings.SettingsCameraUrl, ui.ShownText("PrinterCameraUrlError"));
        });

    // ---- What a screen reader gets for a valid field ----

    [Fact]
    public Task AValidFieldKeepsItsHintAsTheHelpText() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);
            PrinterEditorViewModel printer = Printer(ui);

            Assert.Null(AutomationProperties.GetHelpText(ui.NameBox));
            Assert.Equal(Strings.SettingsAddressHint, AutomationProperties.GetHelpText(ui.AddressBox));
            Assert.Equal(Strings.SettingsUserNameHint, AutomationProperties.GetHelpText(ui.UserNameBox));
            Assert.Equal(printer.PasswordHint, AutomationProperties.GetHelpText(ui.PasswordBox));
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;
            ui.Window.Settle();
            Assert.Equal(Strings.SettingsCameraUrlHint, AutomationProperties.GetHelpText(ui.Find<TextBox>(SettingsFieldIds.PrinterCameraUrl)));
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraWindowPrinterTab;
            ui.Window.Settle();
            Assert.Null(AutomationProperties.GetHelpText(ui.Find<ComboBox>(SettingsFieldIds.PrinterCameraWindowNormalSize)));
            Show(ui, View.Cooldown);
            NumericUpDown nozzle = ui.Find<NumericUpDown>(SettingsFieldIds.WidgetCooldownNozzleThreshold);
            Assert.Equal(Strings.SettingsCooldownThresholdHint, AutomationProperties.GetHelpText(nozzle));
            Assert.Equal(Strings.SettingsCooldownThresholdHint, AutomationProperties.GetHelpText(ui.Find<NumericUpDown>(SettingsFieldIds.WidgetCooldownBedThreshold)));
            // The TextBox inside that holds the focus has the name and help text of its control.
            TextBox nozzleText = Part<TextBox>(nozzle, "PART_TextBox");
            Assert.Equal(Strings.SettingsCooldownThresholdHint, ControlAutomationPeer.CreatePeerForElement(nozzleText).GetHelpText());
            Assert.Equal(Strings.SettingsCooldownThreshold, ControlAutomationPeer.CreatePeerForElement(nozzleText).GetName());
            Show(ui, View.General);
            Assert.Equal(Strings.SettingsPollingIntervalHint, AutomationProperties.GetHelpText(ui.Find<NumericUpDown>(SettingsFieldIds.SettingsPollingInterval)));
            Assert.Null(AutomationProperties.GetHelpText(ui.Find<NumericUpDown>(SettingsFieldIds.SettingsRemainingTime)));
        });

    [Fact]
    public Task TheHelpTextFollowsALanguageSwitch() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);
            Printer(ui).Address = "ftp://printer";
            ui.Window.Settle();

            new UiLanguage().Apply("cs");
            ui.Slot.RefreshTexts();
            ui.Window.Settle();

            Assert.Equal(Strings.SettingsErrorAddressInvalid, AutomationProperties.GetHelpText(ui.AddressBox));
            Assert.Equal(Strings.SettingsUserNameHint, AutomationProperties.GetHelpText(ui.UserNameBox));
            Assert.Contains(Invalid, ui.AddressBox.Classes);
        });

    // ---- Every language: the guard with all errors of a view shown ----

    [Theory]
    [MemberData(nameof(Languages))]
    public Task EveryShownErrorHasItsMarkedFieldInEveryLanguage(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            SettingsWindowHarness ui = await OpenAsync(scope);
            foreach (FieldCase test in Cases.Values)
            {
                test.Break(ui);
            }

            ui.Window.Settle();
            foreach (View view in Enum.GetValues<View>())
            {
                Show(ui, view);
                AssertEveryShownErrorHasItsMarkedField(ui);
            }
        });

    // ---- Helpers ----

    private static async Task<SettingsWindowHarness> OpenAsync(UiTestScope scope) =>
        await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: true, StreamUri) }));

    private static PrinterEditorViewModel Printer(SettingsWindowHarness ui) => ui.ViewModel.Printers[0];

    private static async Task SaveAsync(SettingsWindowHarness ui)
    {
        await ui.ViewModel.SaveCommand.ExecuteAsync(null);
        ui.Window.Settle();
    }

    private static void Show(SettingsWindowHarness ui, View view)
    {
        switch (view)
        {
            case View.Printer:
            case View.Camera:
            case View.CameraWindow:
                ui.ViewModel.SelectedTabIndex = SettingsViewModel.PrintersTab;
                ui.ViewModel.SelectedPrinterTabIndex = view switch
                {
                    View.Printer => SettingsViewModel.ConnectionPrinterTab,
                    View.Camera => SettingsViewModel.CameraPrinterTab,
                    _ => SettingsViewModel.CameraWindowPrinterTab,
                };
                break;
            case View.Cooldown:
                ui.ViewModel.SelectedTabIndex = SettingsViewModel.WidgetsTab;
                ui.ViewModel.SelectedWidget = ui.ViewModel.Widgets.Single(static widget => widget.Id == "cooldown");
                break;
            default:
                ui.ViewModel.SelectedTabIndex = SettingsViewModel.GeneralTab;
                break;
        }

        ui.Window.Settle();
    }

    private static IInputElement? Focused(SettingsWindowHarness ui) => ui.Window.FocusManager?.GetFocusedElement();

    /// <summary>The automation peer of the element that really has the keyboard focus: its name and help text (= the error).</summary>
    private static void AssertFocusedPeer(SettingsWindowHarness ui, string name, string? error)
    {
        Assert.NotNull(error);
        Control focused = Assert.IsAssignableFrom<Control>(Focused(ui));
        AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(focused);
        Assert.Equal(name, peer.GetName());
        Assert.Equal(error, peer.GetHelpText());
    }

    private static void AssertCooldown(SettingsWindowHarness ui, bool nozzle, bool bed, string error)
    {
        NumericUpDown nozzleBox = ui.Find<NumericUpDown>(SettingsFieldIds.WidgetCooldownNozzleThreshold);
        NumericUpDown bedBox = ui.Find<NumericUpDown>(SettingsFieldIds.WidgetCooldownBedThreshold);
        // One error text for both (Q1 A).
        Assert.Equal(error, ui.ShownText("WidgetCooldownThresholdError"));
        Assert.Equal(nozzle, nozzleBox.Classes.Contains(Invalid));
        Assert.Equal(bed, bedBox.Classes.Contains(Invalid));
        Assert.Equal(nozzle ? error : Strings.SettingsCooldownThresholdHint, AutomationProperties.GetHelpText(nozzleBox));
        Assert.Equal(bed ? error : Strings.SettingsCooldownThresholdHint, AutomationProperties.GetHelpText(bedBox));
        Assert.Same(Part<TextBox>(nozzle ? nozzleBox : bedBox, "PART_TextBox"), Focused(ui));
        AssertEveryShownErrorHasItsMarkedField(ui);
    }

    /// <summary>
    /// The guard of the task: every shown field error (<c>TextBlock.error</c>, automation id = field + "Error") has its field
    /// with the class <c>invalid</c>, the one of the cool-down thresholds at least one of the two. A new validated field
    /// without the outline fails here.
    /// </summary>
    private static void AssertEveryShownErrorHasItsMarkedField(SettingsWindowHarness ui)
    {
        List<TextBlock> errors = [.. ui.Window.GetVisualDescendants().OfType<TextBlock>().Where(static text => text.IsEffectivelyVisible && text.Classes.Contains("error"))];
        foreach (TextBlock error in errors)
        {
            string id = AutomationProperties.GetAutomationId(error) ?? throw new InvalidOperationException($"A field error without an automation id: '{error.Text}'.");
            if (NonFieldErrors.Contains(id))
            {
                continue;
            }

            Assert.True(id.EndsWith("Error", StringComparison.Ordinal), $"The error '{id}' is not named after its field (field + \"Error\").");
            string[] fields = id == "WidgetCooldownThresholdError"
                ? [SettingsFieldIds.WidgetCooldownNozzleThreshold, SettingsFieldIds.WidgetCooldownBedThreshold]
                : [id[..^"Error".Length]];
            bool marked = fields.Any(field => ui.Window.GetVisualDescendants().OfType<Control>().Any(control =>
                AutomationProperties.GetAutomationId(control) == field && control.Classes.Contains(Invalid) && control.IsEffectivelyVisible));
            Assert.True(marked, $"The error '{id}' is shown, but no field {string.Join(" / ", fields)} is marked as invalid.");
        }
    }

    /// <summary>The outline: 2 DIP in the error colour around the whole control.</summary>
    private static void AssertRedOutline(Control control, Color expected, string state)
    {
        Assert.True(OutlineColor(control) == expected, $"{AutomationProperties.GetAutomationId(control)} ({state}): outline {OutlineColor(control)}, expected {expected}.");
        Assert.True(OutlineThickness(control) == 2, $"{AutomationProperties.GetAutomationId(control)} ({state}): outline {OutlineThickness(control)} DIP, expected 2.");
        if (control is NumericUpDown)
        {
            // Only the outline is red: the dividers of ▲ ▼ keep the theme colour.
            foreach (RepeatButton button in control.GetVisualDescendants().OfType<RepeatButton>())
            {
                Assert.NotEqual(expected, ColorOf(button.BorderBrush));
                Assert.Equal(new Thickness(1, 0, 0, 0), button.BorderThickness);
            }

            // The inner TextBox draws its border (the accent when focused) over the left, top and bottom of the outline.
            Border inner = Part<Border>(Part<TextBox>(control, "PART_TextBox"), "PART_BorderElement");
            Assert.Equal(expected, ColorOf(inner.BorderBrush));
            Assert.Equal(new Thickness(2, 2, 0, 2), inner.BorderThickness);
        }
    }

    private static Color OutlineColor(Control control) => ColorOf(Outline(control).BorderBrush);

    private static double OutlineThickness(Control control)
    {
        Thickness thickness = Outline(control).BorderThickness;
        Assert.True(thickness.IsUniform, $"Outline {thickness} is not uniform.");
        return thickness.Left;
    }

    private static Border Outline(Control control) => control switch
    {
        NumericUpDown => Part<ButtonSpinner>(control, "PART_Spinner").GetVisualDescendants().OfType<Border>().First(static border => border.Child is Avalonia.Controls.DockPanel),
        ComboBox => Part<Border>(control, "Background"),
        TextBox => Part<Border>(control, "PART_BorderElement"),
        _ => throw new InvalidOperationException($"No outline for {control.GetType().Name}."),
    };

    private static Color ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : default;

    /// <summary>The first named part of a template below <paramref name="control"/>.</summary>
    private static T Part<T>(Visual control, string name)
        where T : Control =>
        control.GetVisualDescendants().OfType<T>().FirstOrDefault(part => part.Name == name)
            ?? throw new InvalidOperationException($"No {typeof(T).Name} #{name} in the template of {control.GetType().Name}.");

    /// <summary>The text box that holds the keyboard focus of a field.</summary>
    private static TextBox TextPart(Control control) => control switch
    {
        NumericUpDown => Part<TextBox>(control, "PART_TextBox"),
        ComboBox => Part<TextBox>(control, "PART_EditableTextBox"),
        TextBox box => box,
        _ => throw new InvalidOperationException($"No text part in {control.GetType().Name}."),
    };

    /// <summary>The size of the field and the place of its text (the text presenter, relative to the field).</summary>
    private static Geometry GeometryOf(SettingsWindowHarness ui, Control control, bool focused)
    {
        TextBox text = TextPart(control);
        if (focused)
        {
            Assert.True(text.Focus(NavigationMethod.Tab));
        }
        else
        {
            ui.Window.FocusManager!.Focus(null);
        }

        ui.Window.Settle();
        TextPresenter presenter = Part<TextPresenter>(text, "PART_TextPresenter");
        Point origin = presenter.TranslatePoint(default, control) ?? throw new InvalidOperationException("The text is not in its field.");
        return new Geometry(control.Bounds.Size, origin, presenter.Bounds.Height);
    }

    private static string Format(string format, object? arg0, object? arg1) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, format, arg0, arg1);

    private sealed record FieldCase(string Error, View View, Action<SettingsWindowHarness> Break, Action<SettingsWindowHarness> Fix);

    private readonly record struct Geometry(Size Size, Point TextOrigin, double TextHeight);
}
