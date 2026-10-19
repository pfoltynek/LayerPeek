using System.Text;

using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.UiTests.Settings;

/// <summary>The settings window (T-015, T-027) through its real XAML: typing, clicking, inline errors, save and cancel.</summary>
public sealed class SettingsWindowTests(HeadlessSession session)
{
    [Fact]
    public Task AddPrinterShowsAnEmptyFormWithTheDefaultUserName() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith());
            Assert.NotNull(FindShownText(ui.Window, Strings.SettingsNoPrinters));

            ui.Click("SettingsAddPrinter");

            ListBox list = ui.Find<ListBox>("SettingsPrinterList");
            Assert.Equal(1, list.ItemCount);
            Assert.Equal(string.Empty, ui.NameBox.Text);
            Assert.Equal(string.Empty, ui.AddressBox.Text);
            Assert.Equal(PrinterUserName.Default, ui.UserNameBox.Text);
            Assert.True(ui.UserNameBox.IsEffectivelyEnabled);
            Assert.True(ui.PasswordBox.IsEffectivelyEnabled);
            Assert.Equal(Strings.SettingsUserNameHint, ui.ShownText("PrinterUserNameHint"));
            Assert.Equal(Strings.SettingsPasswordNewHint, ui.ShownText("PrinterPasswordHint"));

            // Errors appear only after the first save attempt.
            Assert.Null(ui.ShownText("PrinterNameError"));
            Assert.Null(ui.ShownText("PrinterAddressError"));
            Assert.Null(ui.ShownText("PrinterUserNameError"));
        });

    [Fact]
    public Task TypedValuesAreSavedThroughTheStoreAndTheWindowCloses() =>
        session.RunAsync(static async scope =>
        {
            // The real editor store over in-memory settings and secrets: the round trip of the window's values.
            MemorySettingsStore settings = new();
            FakeSecretStore secrets = new();
            RecordingApplier applier = new();
            SettingsEditorStore store = new(
                () => settings,
                secrets,
                SettingsWindowHarness.Supported,
                applier,
                FakeWebGateway.WithoutPassword(),
                NullLogger<SettingsEditorStore>.Instance);
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);

            ui.Click("SettingsAddPrinter");
            ui.Window.TypeOver(ui.NameBox, "MK4 workshop");
            ui.Window.TypeOver(ui.AddressBox, "192.168.1.30");
            ui.Window.TypeOver(ui.UserNameBox, "operator");
            ui.Window.TypeOver(ui.PasswordBox, "s3cret!");

            // The list follows the typed name.
            Assert.NotNull(FindShownText(ui.Find<ListBox>("SettingsPrinterList"), "MK4 workshop"));

            await ui.SaveAsync();

            AppSettings saved = Assert.IsType<AppSettings>(settings.Saved);
            PrinterDefinition printer = Assert.Single(saved.Printers);
            Assert.Equal("p1", printer.Id);
            Assert.Equal("MK4 workshop", printer.Name);
            Assert.Equal(ConnectionType.PrusaLink, printer.ConnectionType);
            Assert.Equal("192.168.1.30", printer.Address);
            Assert.Equal("operator", printer.UserName);
            Assert.Equal("s3cret!", secrets.Secrets[SecretKeys.ForPrinter("p1")]);
            Assert.Same(saved, Assert.Single(applier.Applied));

            Assert.False(ui.IsOpen);
            Assert.Equal(1, ui.ClosedCount);
            Assert.True(ui.ViewModel.IsDisposed);
            Assert.False(ui.Slot.IsOpen);
        });

    [Fact]
    public Task UntouchedUserNameIsSavedAsTheDefault() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith();
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);

            ui.Click("SettingsAddPrinter");
            ui.Window.TypeOver(ui.NameBox, "MK4");
            ui.Window.TypeOver(ui.AddressBox, "192.168.1.30");
            await ui.SaveAsync();

            SettingsChange change = Assert.Single(store.Saves);
            Assert.Equal(PrinterUserName.Default, Assert.Single(change.Updated.Printers).UserName);
            Assert.Empty(change.NewPasswords);
            Assert.False(ui.IsOpen);
        });

    [Theory]
    [InlineData("", nameof(Strings.SettingsErrorUserNameRequired))]
    [InlineData("ma:ker", nameof(Strings.SettingsErrorUserNameColon))]
    public Task InvalidValuesShowInlineErrorsAndNothingIsSaved(string userName, string userNameError) =>
        session.RunAsync(async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith();
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);

            ui.Click("SettingsAddPrinter");
            ui.Window.TypeOver(ui.NameBox, "MK4");
            if (userName.Length == 0)
            {
                ui.Window.ClearText(ui.UserNameBox);
            }
            else
            {
                ui.Window.TypeOver(ui.UserNameBox, userName);
            }

            await ui.SaveAsync();

            Assert.Equal(Strings.ResourceManager.GetString(userNameError, Strings.Culture), ui.ShownText("PrinterUserNameError"));
            Assert.Equal(Strings.SettingsErrorAddressRequired, ui.ShownText("PrinterAddressError"));
            Assert.Null(ui.ShownText("PrinterNameError"));
            Assert.Equal(Strings.SettingsFixErrors, ui.ShownText("SettingsStatus"));
            Assert.Empty(store.Saves);
            Assert.True(ui.IsOpen);

            // Fixing the field removes its error right away.
            ui.Window.TypeOver(ui.UserNameBox, "maker");
            Assert.Null(ui.ShownText("PrinterUserNameError"));
        });

    [Fact]
    public Task PrusaConnectPrinterDisablesUserNameAndPassword() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(CloudPrinter("c1", "Cloud MK4")));

            Assert.Equal("Cloud MK4", ui.NameBox.Text);
            Assert.False(ui.UserNameBox.IsEffectivelyEnabled);
            Assert.False(ui.PasswordBox.IsEffectivelyEnabled);
            Assert.False(ui.UserNameBox.Focus());
            Assert.Equal(Strings.SettingsUserNameNotUsed, ui.ShownText("PrinterUserNameHint"));
            Assert.Equal(Strings.SettingsPasswordNotUsed, ui.ShownText("PrinterPasswordHint"));
        });

    [Fact]
    public Task PasswordIsMaskedAndASavedPasswordIsNeverLoadedIntoTheField() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha"));
            store.Data = store.Data with { PrintersWithPassword = Ids("a") };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);

            TextBox password = ui.PasswordBox;
            Assert.Equal(string.Empty, password.Text ?? string.Empty);
            Assert.Equal(Strings.SettingsPasswordUnchanged, password.PlaceholderText);
            Assert.Equal(Strings.SettingsPasswordKeepHint, ui.ShownText("PrinterPasswordHint"));

            ui.Window.TypeOver(password, "typed secret");

            Assert.Equal("typed secret", ui.ViewModel.SelectedPrinter!.Password);
            Assert.Equal('●', password.PasswordChar);
            Assert.False(password.RevealPassword);
            // What is drawn is masked: the presenter renders the password character, never the text.
            TextPresenter presenter = Assert.Single(password.GetVisualDescendants().OfType<TextPresenter>());
            Assert.Equal('●', presenter.PasswordChar);
        });

    [Fact]
    public Task CancelSavesNothingAndClosesTheWindow() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha"));
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);

            ui.Window.TypeOver(ui.NameBox, "Renamed");
            ui.Window.TypeOver(ui.PasswordBox, "typed secret");
            Assert.True(ui.ViewModel.IsDirty);
            ui.Click("SettingsCancel");

            Assert.Empty(store.Saves);
            Assert.False(ui.IsOpen);
            Assert.True(ui.ViewModel.IsDisposed);
            Assert.False(ui.Slot.IsOpen);
        });

    [Fact]
    public Task ClosingTheWindowReleasesTheViewModel() =>
        session.RunAsync(static async scope =>
        {
            FakeConnectionTester tester = new();
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")), tester);
            ui.Click("PrinterTestConnection");
            Assert.True(ui.ViewModel.SelectedPrinter!.IsTesting);

            // The title bar button / Alt+F4.
            ui.Window.Close();
            ui.Window.Settle();

            Assert.Equal(1, ui.ClosedCount);
            Assert.True(ui.ViewModel.IsDisposed);
            Assert.True(tester.LastToken.IsCancellationRequested);
            Assert.False(ui.Slot.IsOpen);
        });

    [Fact]
    public Task RemovedPrinterIsLeftOutOfTheSave() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta", "192.168.1.21"));
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
            ListBox list = ui.Find<ListBox>("SettingsPrinterList");

            ui.Window.Click(Assert.IsType<ListBoxItem>(list.ContainerFromIndex(1)));
            Assert.Equal("Beta", ui.NameBox.Text);
            ui.Click("SettingsRemovePrinter");

            Assert.Equal(1, list.ItemCount);
            Assert.Equal("Alpha", ui.NameBox.Text);
            await ui.SaveAsync();

            SettingsChange change = Assert.Single(store.Saves);
            Assert.Equal(["a"], change.Updated.Printers.Select(static p => p.Id));
            Assert.False(ui.IsOpen);
        });

    [Fact]
    public Task TestConnectionShowsProgressAndThenTheResult() =>
        session.RunAsync(static async scope =>
        {
            FakeConnectionTester tester = new();
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(), tester);
            ui.Click("SettingsAddPrinter");
            ui.Window.TypeOver(ui.AddressBox, "192.168.1.30");
            ui.Window.TypeOver(ui.PasswordBox, "s3cret!");

            ui.Click("PrinterTestConnection");

            Assert.Equal(Strings.SettingsTesting, ui.ShownText("PrinterTestResult"));
            (PrinterDefinition printer, string? password) = Assert.Single(tester.Calls);
            Assert.Equal("192.168.1.30", printer.Address);
            Assert.Equal(PrinterUserName.Default, printer.UserName);
            Assert.Equal("s3cret!", password);

            tester.Respond(new ConnectionTestResult(ConnectionTestOutcome.Success, PrinterModel: "1.4.0", Firmware: "6.2.0"));
            await ui.ViewModel.SelectedPrinter!.TestConnectionCommand.ExecutionTask!;
            ui.Window.Settle();

            TextBlock result = ui.Find<TextBlock>("PrinterTestResult");
            Assert.Equal(string.Format(Strings.Culture, CompositeFormat.Parse(Strings.SettingsTestSuccess), "1.4.0", "6.2.0"), ui.ShownText("PrinterTestResult"));
            Assert.Contains("success", result.Classes);
            Assert.False(ui.ViewModel.SelectedPrinter.IsTesting);
        });

    [Fact]
    public Task TestConnectionWithAnInvalidUserNameExplainsItWithoutConnecting() =>
        session.RunAsync(static async scope =>
        {
            FakeConnectionTester tester = new();
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")), tester);
            ui.Window.TypeOver(ui.UserNameBox, "ma:ker");

            ui.Click("PrinterTestConnection");

            Assert.Empty(tester.Calls);
            // The long explanation is at the field only (T-033, Q2).
            Assert.Equal(Strings.SettingsFixErrors, ui.ShownText("PrinterTestResult"));
            Assert.Equal(Strings.SettingsErrorUserNameColon, ui.ShownText("PrinterUserNameError"));
            Assert.Contains("failure", ui.Find<TextBlock>("PrinterTestResult").Classes);
        });

    /// <summary>A visible text block below <paramref name="root"/> showing exactly <paramref name="text"/>.</summary>
    internal static string? FindShownText(Avalonia.Visual root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(block => block.IsEffectivelyVisible && block.Text == text)?.Text;
}
