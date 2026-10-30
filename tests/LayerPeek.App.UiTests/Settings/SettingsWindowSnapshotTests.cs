using LayerPeek.App.Localization;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Cameras;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// What the settings window looks like (Skia rendering compared with the PNGs in <c>Snapshots/</c>). The window keeps its
/// XAML size (780 × 532, T-033, T-094), the system language is English and the app language "system" unless a test switches it. Values
/// are set through the view model, not typed, so that no text box has the focus (no caret) and no pointer hovers.
/// Update the references with <c>LAYERPEEK_UPDATE_SNAPSHOTS=1</c> (see the task notes of T-031).
/// </summary>
public sealed class SettingsWindowSnapshotTests(HeadlessSession session)
{
    [Fact]
    public Task PrusaLinkPrinterWithUserNameAndSavedPassword() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(
                LinkPrinter("a", "MK4 workshop", address: "192.168.1.30", userName: "operator"),
                CloudPrinter("c1", "Cloud XL"));
            store.Data = store.Data with { PrintersWithPassword = Ids("a") };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);

            SnapshotAssert.Matches(ui.Window, "settings-prusalink");
        });

    [Fact]
    public Task PrinterImagesInTheListAndOnTheButton() =>
        session.RunAsync(static async scope =>
        {
            // T-039: detected, chosen, custom (a 2:1 picture, letterboxed) and generic images; the custom one is selected.
            byte[] custom = Printers.UiTestImages.Png(64, 32, static (x, _) => x < 32 ? 0xFFFD5000 : 0xFF2B2C33);
            FakeEditorStore store = SettingsWindowHarness.StoreWith(
                LinkPrinter("a", "MK4S workshop", address: "192.168.1.30") with { DetectedModel = new Core.Printers.PrinterModelCode(1, 4, 1) },
                LinkPrinter("b", "Core One L", address: "192.168.1.31") with { Image = Core.Printers.PrinterImageChoice.ForModel(Core.Printers.PrinterModel.CoreOneL) },
                LinkPrinter("c", "XL 5T", address: "192.168.1.32") with { DetectedModel = new Core.Printers.PrinterModelCode(3, 1, 1) },
                LinkPrinter("d", "MINI kitchen", address: "192.168.1.33") with { DetectedModel = new Core.Printers.PrinterModelCode(2, 1, 0) },
                LinkPrinter("e", "Garage printer", address: "192.168.1.34") with { Image = Core.Printers.PrinterImageChoice.Custom },
                CloudPrinter("f", "Cloud iX"));
            store.Data = store.Data with { CustomImages = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["e"] = custom } };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
            ui.ViewModel.SelectedPrinter = ui.ViewModel.Printers[4];

            SnapshotAssert.Matches(ui.Window, "settings-printer-images");
        });

    [Fact]
    public Task PrusaConnectPrinterWithDisabledLoginFields() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(CloudPrinter("c1", "Cloud XL")));

            SnapshotAssert.Matches(ui.Window, "settings-prusaconnect");
        });

    [Fact]
    public Task ValidationErrorsAtTheFields() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenWithValidationErrorsAsync(scope);

            SnapshotAssert.Matches(ui.Window, "settings-validation");
        });

    [Fact]
    public Task ValidationErrorsAfterSwitchingToCzech() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenWithValidationErrorsAsync(scope);

            // As App.OnLanguageChanged: the resources switch, then the open window formats its texts again.
            new UiLanguage().Apply("cs");
            ui.Slot.RefreshTexts();

            SnapshotAssert.Matches(ui.Window, "settings-validation-cs");
        });

    [Fact]
    public Task CameraSubTabWithASuccessfulTest() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraTester cameraTester = new() { Immediate = new CameraTestResult(CameraTestOutcome.Success, 1920, 1080) };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) }),
                cameraTester: cameraTester);
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;
            await ui.ViewModel.SelectedPrinter!.TestCameraCommand.ExecuteAsync(null);

            SnapshotAssert.Matches(ui.Window, "settings-camera");
        });

    [Fact]
    public Task CameraSubTabWithTheSnapshotOfASuccessfulTest() =>
        session.RunAsync(static async scope =>
        {
            // 16:9 like the Buddy3D camera, four colours so that the orientation and scaling are visible.
            CameraImage image = TestCameraImages.Quadrants(1920, 1080, 0xFFE05A1E, 0xFF2E7D32, 0xFF1565C0, 0xFFF9A825);
            FakeCameraTester cameraTester = new() { Immediate = new CameraTestResult(CameraTestOutcome.Success, 1920, 1080, Image: image) };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) }),
                cameraTester: cameraTester);
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;
            await ui.ViewModel.SelectedPrinter!.TestCameraCommand.ExecuteAsync(null);

            SnapshotAssert.Matches(ui.Window, "settings-camera-preview");
        });

    [Fact]
    public Task CameraSubTabWithA4By3SnapshotInGerman() =>
        session.RunAsync(static async scope =>
        {
            // A 4:3 image gets black bands at the sides; German has a long success text next to the preview.
            new UiLanguage().Apply("de");
            CameraImage image = TestCameraImages.Quadrants(640, 480, 0xFFE05A1E, 0xFF2E7D32, 0xFF1565C0, 0xFFF9A825);
            FakeCameraTester cameraTester = new() { Immediate = new CameraTestResult(CameraTestOutcome.Success, 640, 480, Image: image) };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) }),
                cameraTester: cameraTester);
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;
            await ui.ViewModel.SelectedPrinter!.TestCameraCommand.ExecuteAsync(null);

            SnapshotAssert.Matches(ui.Window, "settings-camera-preview-4x3-de");
        });

    [Fact]
    public Task CameraSubTabWithTheCredentialsNoticeAndAnError() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: false, new Uri("rtsp://192.168.0.227/live")) });
            store.Data = store.Data with { CamerasWithRemovedCredentials = Ids("a") };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store, cameraTester: new FakeCameraTester { Immediate = new CameraTestResult(CameraTestOutcome.Timeout) });
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;
            await ui.ViewModel.SelectedPrinter!.TestCameraCommand.ExecuteAsync(null);

            SnapshotAssert.Matches(ui.Window, "settings-camera-notice");
        });

    [Fact]
    public Task GeneralTab() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop")));
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.GeneralTab;

            SnapshotAssert.Matches(ui.Window, "settings-general");
        });

    /// <summary>
    /// T-101: the result "sent" next to the test button in French (the longest button and a two-line result), with the terms
    /// of the OS of the reference set (Focus on macOS, Do not disturb on Windows).
    /// </summary>
    [Fact]
    public Task GeneralTabAfterATestNotificationInFrench() =>
        session.RunAsync(static async scope =>
        {
            new UiLanguage().Apply("fr");
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop")), macTerms: OperatingSystem.IsMacOS());
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.GeneralTab;
            await ui.ViewModel.SendTestNotificationCommand.ExecuteAsync(null);

            Assert.True(ui.ViewModel.ShowsTestNotificationResult);
            SnapshotAssert.Matches(ui.Window, "settings-general-test-sent-fr");
        });

    /// <summary>
    /// T-101 (ui-designer review 2026-10-30): notifications turned off in the OS, in German (the longest warning) with the
    /// button that opens the OS settings, with the terms of the OS of the reference set.
    /// </summary>
    [Fact]
    public Task GeneralTabWithNotificationsTurnedOffInGerman() =>
        session.RunAsync(static async scope =>
        {
            new UiLanguage().Apply("de");
            FakeSystemNotificationSettings system = new() { TurnedOff = true };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 Werkstatt")), macTerms: OperatingSystem.IsMacOS(), notificationSettings: system);
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.GeneralTab;
            ui.Window.Settle();

            Assert.True(ui.Window.IsShown<Avalonia.Controls.Button>("SettingsOpenNotificationSettings"));
            SnapshotAssert.Matches(ui.Window, "settings-general-turned-off-de");
        });

    /// <summary>T-101 (ui-designer review 2026-10-30): the result "sent" next to the test button in the dark theme.</summary>
    [Fact]
    public Task GeneralTabAfterATestNotificationInTheDarkTheme() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop")), macTerms: OperatingSystem.IsMacOS());
            ui.Window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.GeneralTab;
            await ui.ViewModel.SendTestNotificationCommand.ExecuteAsync(null);
            ui.Window.Settle();

            Assert.True(ui.ViewModel.ShowsTestNotificationResult);
            SnapshotAssert.Matches(ui.Window, "settings-general-test-sent-dark");
        });

    /// <summary>T-017: start after logon (shown as on Windows) and pre-release updates, on by default for 0.x.</summary>
    [Fact]
    public Task StartupAndUpdatesTab() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop")));
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.StartupUpdatesTab;

            Assert.True(ui.ViewModel.IsAutostartAvailable);
            Assert.False(ui.ViewModel.StartWithWindows);
            Assert.True(ui.ViewModel.OfferPrereleaseUpdates);
            SnapshotAssert.Matches(ui.Window, "settings-startup-updates");
        });

    /// <summary>T-017 (user decision 2026-10-24): a .zip or a development build shows autostart disabled with the reason.</summary>
    [Fact]
    public Task StartupAndUpdatesTabWithoutAnInstallation() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop")), autostartNeedsInstallation: true);
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.StartupUpdatesTab;
            ui.Window.Settle();

            Assert.False(ui.ViewModel.IsAutostartAvailable);
            Assert.Equal(LayerPeek.App.Resources.Strings.SettingsNeedsInstallation, ui.ShownText("SettingsStartWithWindowsNotInstalled"));
            // The hint about pre-releases would mislead without an installation: only the reason is shown.
            Assert.Null(ui.ShownText("SettingsOfferPrereleaseUpdatesHint"));
            Assert.Equal(LayerPeek.App.Resources.Strings.SettingsNeedsInstallation, ui.ShownText("SettingsOfferPrereleaseUpdatesNotInstalled"));
            SnapshotAssert.Matches(ui.Window, "settings-startup-updates-not-installed");
        });

    [Fact]
    public Task GeneralTabWithErrors() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop")));
            ui.ViewModel.PollingIntervalSeconds = 0;
            ui.ViewModel.RemainingTimeMinutes = 2000;
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);

            SnapshotAssert.Matches(ui.Window, "settings-general-errors");
        });

    [Fact]
    public Task ErrorsOnBothSubTabsAndARefusedTestInFrench() =>
        session.RunAsync(static async scope =>
        {
            // French has the longest texts (T-033 design): the tightest Printer sub-tab.
            new UiLanguage().Apply("fr");
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop"));
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            printer.Name = new string('n', SettingsViewModel.MaxNameLength + 1);
            printer.Address = "ftp://printer";
            printer.UserName = "mäker";
            printer.Password = "\uD800";
            printer.CameraEnabled = true;
            await printer.TestConnectionCommand.ExecuteAsync(null);
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            Assert.Empty(store.Saves);

            SnapshotAssert.Matches(ui.Window, "settings-errors-fr");
        });

    [Fact]
    public Task CameraWindowSubTab() =>
        session.RunAsync(static async scope =>
        {
            // Three monitors; the saved one is connected (Monitor 1 – LG …).
            CameraWindowDefaults window = new()
            {
                StartMode = CameraWindowStartMode.Mini,
                Monitor = new CameraMonitorId("LG HDR 4K", 1920, 1080),
                Normal = new CameraWindowLayout(new CameraWindowSize(1280, 720), CameraWindowCorner.TopRight),
            };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) { Window = window } }),
                screens: FakeScreenCatalog.ThreeMonitors());
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraWindowPrinterTab;

            SnapshotAssert.Matches(ui.Window, "settings-camera-window");
        });

    // ---- Widgets tab (T-092, design 10) ----

    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static async Task<SettingsWindowHarness> OpenWidgetsAsync(UiTestScope scope, params Core.Printers.PrinterDefinition[] printers)
    {
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(printers));
        ui.ViewModel.SelectedTabIndex = SettingsViewModel.WidgetsTab;
        ui.Window.Settle();
        return ui;
    }

    private static void SelectWidget(SettingsWindowHarness ui, string id)
    {
        ui.ViewModel.SelectedWidget = ui.ViewModel.Widgets.Single(widget => widget.Id == id);
        ui.Window.Settle();
    }

    [Fact]
    public Task WidgetsTab() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenWidgetsAsync(
                scope,
                LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: true, StreamUri) { Overlay = Core.Cameras.Overlay.CameraOverlayLayout.Default.WithVisible("times", visible: false) } },
                LinkPrinter("b", "Beta", "192.168.1.21"),
                CloudPrinter("c", "Cloud XL"));

            SnapshotAssert.Matches(ui.Window, "settings-widgets");
        });

    [Fact]
    public Task WidgetsTabInTheDarkTheme() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenWidgetsAsync(scope, LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: true, StreamUri) }, LinkPrinter("b", "Beta", "192.168.1.21"));
            ui.Window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            SelectWidget(ui, "cooldown");

            SnapshotAssert.Matches(ui.Window, "settings-widgets-dark");
        });

    /// <summary>T-110 (design T-104 2): the "Mini preview" item with its panel, one item off.</summary>
    [Theory]
    [InlineData("en", "settings-widgets-mini-strip")]
    [InlineData("de", "settings-widgets-mini-strip-de")]
    [InlineData("fr", "settings-widgets-mini-strip-fr")]
    public Task WidgetsTabMiniStrip(string language, string snapshot) =>
        session.RunAsync(async scope =>
        {
            new UiLanguage().Apply(language);
            SettingsWindowHarness ui = await OpenWidgetsAsync(
                scope,
                LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: true, StreamUri) { MiniStrip = new CameraMiniStripSettings { Remaining = false } } },
                LinkPrinter("b", "Beta", "192.168.1.21"));
            ui.ViewModel.SelectedWidgetsListEntry = ui.ViewModel.MiniStripEntry;
            ui.Window.Settle();

            SnapshotAssert.Matches(ui.Window, snapshot);
        });

    /// <summary>T-110 (ui-designer review 2026-10-30): the "Mini preview" panel in the dark theme, one item off.</summary>
    [Fact]
    public Task WidgetsTabMiniStripInTheDarkTheme() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenWidgetsAsync(
                scope,
                LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: true, StreamUri) { MiniStrip = new CameraMiniStripSettings { Remaining = false } } },
                LinkPrinter("b", "Beta", "192.168.1.21"));
            ui.Window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            ui.ViewModel.SelectedWidgetsListEntry = ui.ViewModel.MiniStripEntry;
            ui.Window.Settle();

            SnapshotAssert.Matches(ui.Window, "settings-widgets-mini-strip-dark");
        });

    /// <summary>T-110 (ui-designer review 2026-10-30): the "Mini preview" panel with the camera off (disabled fields), in Italian.</summary>
    [Fact]
    public Task WidgetsTabMiniStripWithTheCameraOffInItalian() =>
        session.RunAsync(static async scope =>
        {
            new UiLanguage().Apply("it");
            SettingsWindowHarness ui = await OpenWidgetsAsync(
                scope,
                LinkPrinter("a", "MK4 officina") with { Camera = new CameraSettings(enabled: false, StreamUri) { MiniStrip = new CameraMiniStripSettings { Cooldown = false } } },
                LinkPrinter("b", "Beta", "192.168.1.21"));
            ui.ViewModel.SelectedWidgetsListEntry = ui.ViewModel.MiniStripEntry;
            ui.Window.Settle();

            SnapshotAssert.Matches(ui.Window, "settings-widgets-mini-strip-camera-off-it");
        });

    [Fact]
    public Task WidgetsTabCooldownWithASinglePrinterInGerman() =>
        session.RunAsync(static async scope =>
        {
            // Design 3.4: the longest language, both "… for all printers" disabled.
            new UiLanguage().Apply("de");
            SettingsWindowHarness ui = await OpenWidgetsAsync(scope, LinkPrinter("a", "MK4 Werkstatt") with { Camera = new CameraSettings(enabled: true, StreamUri) });
            SelectWidget(ui, "cooldown");

            SnapshotAssert.Matches(ui.Window, "settings-widgets-cooldown-de");
        });

    [Fact]
    public Task WidgetsTabCooldownWithAThresholdErrorInFrench() =>
        session.RunAsync(static async scope =>
        {
            new UiLanguage().Apply("fr");
            SettingsWindowHarness ui = await OpenWidgetsAsync(scope, LinkPrinter("a", "MK4 atelier") with { Camera = new CameraSettings(enabled: true, StreamUri) }, LinkPrinter("b", "Beta", "192.168.1.21"));
            ui.ViewModel.Printers[0].CooldownBedThresholdCelsius = 120;
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            ui.Window.Settle();

            SnapshotAssert.Matches(ui.Window, "settings-widgets-cooldown-error-fr");
        });

    [Fact]
    public Task WidgetsTabWithTheCameraOffInItalian() =>
        session.RunAsync(static async scope =>
        {
            new UiLanguage().Apply("it");
            SettingsWindowHarness ui = await OpenWidgetsAsync(scope, LinkPrinter("a", "MK4 officina") with { Camera = new CameraSettings(enabled: false, StreamUri) }, LinkPrinter("b", "Beta", "192.168.1.21"));

            SnapshotAssert.Matches(ui.Window, "settings-widgets-camera-off-it");
        });

    [Fact]
    public Task WidgetsTabWithoutPrinters() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenWidgetsAsync(scope);

            SnapshotAssert.Matches(ui.Window, "settings-widgets-no-printers");
        });

    [Fact]
    public Task CameraWindowSubTabWithSizeErrorsAndADisconnectedMonitorInFrench() =>
        session.RunAsync(static async scope =>
        {
            new UiLanguage().Apply("fr");
            CameraWindowDefaults window = CameraWindowDefaults.Default with { Monitor = new CameraMonitorId("BenQ PD2700U", 3840, 2160) };
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) { Window = window } });
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store, screens: FakeScreenCatalog.ThreeMonitors());
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            printer.NormalSizeText = "100 x 50";
            printer.MiniSizeText = "20000 x 90";
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            Assert.Empty(store.Saves);

            SnapshotAssert.Matches(ui.Window, "settings-camera-window-errors-fr");
        });

    [Fact]
    public Task CameraWindowSubTabWithTheCameraOffInCzech() =>
        session.RunAsync(static async scope =>
        {
            new UiLanguage().Apply("cs");
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: false, new Uri("rtsp://192.168.0.227/live")) }),
                screens: FakeScreenCatalog.ThreeMonitors());
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraWindowPrinterTab;

            SnapshotAssert.Matches(ui.Window, "settings-camera-window-off-cs");
        });

    /// <summary>T-094: maximized on a 1366 × 768 screen at 100 % (client 1366 × 705), the content stretches.</summary>
    [Fact]
    public Task MaximizedOnALaptopScreen() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(
                LinkPrinter("a", "MK4 workshop", address: "192.168.1.30", userName: "operator"),
                CloudPrinter("c1", "Cloud XL"));
            store.Data = store.Data with { PrintersWithPassword = Ids("a") };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
            ui.Window.WindowState = Avalonia.Controls.WindowState.Maximized;
            ui.Window.Width = 1366;
            ui.Window.Height = 705;

            SnapshotAssert.Matches(ui.Window, "settings-maximized");
        });

    /// <summary>A new printer with a name, no address and a user name with a colon, after a save attempt.</summary>
    private static async Task<SettingsWindowHarness> OpenWithValidationErrorsAsync(UiTestScope scope)
    {
        FakeEditorStore store = SettingsWindowHarness.StoreWith();
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
        ui.ViewModel.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
        printer.Name = "MK4";
        printer.UserName = "ma:ker";
        await ui.ViewModel.SaveCommand.ExecuteAsync(null);
        Assert.Empty(store.Saves);
        return ui;
    }
}
