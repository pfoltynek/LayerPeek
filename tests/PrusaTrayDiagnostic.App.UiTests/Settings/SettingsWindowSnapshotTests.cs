using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Settings;
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
