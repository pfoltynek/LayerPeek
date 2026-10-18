using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.UiTests.Settings;

/// <summary>
/// What the settings window looks like (Skia rendering compared with the PNGs in <c>Snapshots/</c>). The window keeps its
/// XAML size (780 × 680), the system language is English and the app language "system" unless a test switches it. Values
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
