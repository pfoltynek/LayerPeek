using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Cameras;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.Core.Cameras;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.UiTests.Settings;

/// <summary>
/// What the settings window looks like (Skia rendering compared with the PNGs in <c>Snapshots/</c>). The window keeps its
/// XAML size (780 × 508, T-033), the system language is English and the app language "system" unless a test switches it. Values
/// are set through the view model, not typed, so that no text box has the focus (no caret) and no pointer hovers.
/// Update the references with <c>PTD_UPDATE_SNAPSHOTS=1</c> (see the task notes of T-031).
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

    [Fact]
    public Task OverlaySubTab() =>
        session.RunAsync(static async scope =>
        {
            CameraOverlayOptions overlay = new()
            {
                Corner = CameraOverlayCorner.TopRight,
                Normal = CameraOverlayItems.PrinterName | CameraOverlayItems.State | CameraOverlayItems.NozzleTemperature | CameraOverlayItems.Progress | CameraOverlayItems.FinishTime,
                Mini = CameraOverlayItems.Progress | CameraOverlayItems.RemainingTime,
            };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) { Overlay = overlay } }));
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.OverlayPrinterTab;

            SnapshotAssert.Matches(ui.Window, "settings-overlay");
        });

    [Fact]
    public Task OverlaySubTabWithTheCameraOffInItalian() =>
        session.RunAsync(static async scope =>
        {
            // Italian has the widest sub-tab headers (T-033 design); the camera-off hint is the tallest content.
            new UiLanguage().Apply("it");
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4 workshop") with { Camera = new CameraSettings(enabled: false, new Uri("rtsp://192.168.0.227/live")) }));
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.OverlayPrinterTab;

            SnapshotAssert.Matches(ui.Window, "settings-overlay-off-it");
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
