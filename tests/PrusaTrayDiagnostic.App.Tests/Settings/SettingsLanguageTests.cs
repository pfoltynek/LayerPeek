using System.ComponentModel;
using System.Globalization;

using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.Tests.Settings;

/// <summary>The language selection of the settings window and its texts after a language switch (T-029).</summary>
public sealed class SettingsLanguageTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs");

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private readonly FakeConnectionTester _tester = new();
    private SettingsViewModel? _viewModel;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public void LanguagesAreSystemDefaultThenEveryLanguageUnderItsOwnName()
    {
        SettingsViewModel vm = Create();

        Assert.Equal(
            ["System default", "Čeština", "Deutsch", "English", "Español", "Français", "Italiano", "Polski", "Slovenčina"],
            vm.LanguageOptions.Select(static o => o.Text));
        Assert.Equal([null, "cs", "de", "en", "es", "fr", "it", "pl", "sk"], vm.LanguageOptions.Select(static o => o.Code));
        Assert.Equal(
            AppLanguages.Supported.Order(StringComparer.Ordinal),
            vm.LanguageOptions.Skip(1).Select(static o => o.Code!).Order(StringComparer.Ordinal));
        Assert.Null(vm.SelectedLanguage.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("de")]
    [InlineData("sk")]
    public async Task LoadSelectsTheSavedLanguage(string? code)
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Language = code });

        Assert.Equal(code, vm.SelectedLanguage.Code);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task SaveWritesTheSelectedLanguage()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });

        vm.SelectedLanguage = vm.LanguageOptions.Single(static o => o.Code == "pl");
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        SettingsChange change = Assert.Single(_store.Saves);
        Assert.Null(change.Original.Language);
        Assert.Equal("pl", change.Updated.Language);
    }

    [Fact]
    public async Task SystemDefaultIsSavedAsNoLanguage()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Language = "fr" });

        vm.SelectedLanguage = vm.LanguageOptions[0];
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(_store.Saves).Updated.Language);
    }

    [Fact]
    public async Task ChangingTheLanguageBackIsNotAChange()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Language = "it" });

        vm.SelectedLanguage = vm.LanguageOptions.Single(static o => o.Code == "es");
        vm.SelectedLanguage = vm.LanguageOptions.Single(static o => o.Code == "it");

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task NullFromTheSelectorKeepsTheLanguage()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Language = "de" });

        vm.SelectedLanguage = null!;

        Assert.Equal("de", vm.SelectedLanguage.Code);
    }

    [Fact]
    public async Task RefreshTextsFormatsEveryTextInTheNewLanguageAndKeepsTheEdits()
    {
        _store.Data = new SettingsEditorData(
            new AppSettings { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Alpha")] },
            SettingsLoadStatus.Corrupted,
            [],
            Ids("a"));
        SettingsViewModel vm = Create();
        await vm.LoadAsync();
        PrinterEditorViewModel printer = vm.Printers[0];
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.NotReachable);
        await printer.TestConnectionCommand.ExecuteAsync(null);
        vm.PollingIntervalSeconds = 0.5m;
        await vm.SaveCommand.ExecuteAsync(null);
        SoundOption sound = vm.SelectedSound;
        LanguageOption language = vm.SelectedLanguage;
        List<string?> soundChanges = [];
        ((INotifyPropertyChanged)sound).PropertyChanged += (_, e) => soundChanges.Add(e.PropertyName);
        Assert.Equal("Another printer already has this name.", printer.NameError);
        Assert.Equal("Some values are not valid. Fix the highlighted fields.", vm.StatusText);

        using (new CultureScope("cs-CZ"))
        {
            vm.RefreshTexts();

            Assert.Equal(Cs(nameof(Strings.SettingsNoticeCorrupted)), vm.Notice);
            Assert.Equal(Cs(nameof(Strings.SettingsFixErrors)), vm.StatusText);
            Assert.Equal(Cs(nameof(Strings.SettingsErrorNameDuplicate)), printer.NameError);
            Assert.Equal(Cs(nameof(Strings.SettingsTestNotReachable)), printer.TestResultText);
            Assert.Equal(Cs(nameof(Strings.SettingsPasswordKeepHint)), printer.PasswordHint);
            Assert.Equal(Cs(nameof(Strings.SettingsPasswordUnchanged)), printer.PasswordWatermark);
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Cs(nameof(Strings.SettingsErrorPollingInterval)), 1m, 300m), vm.PollingIntervalError);
            Assert.Equal(Cs(nameof(Strings.SettingsSoundImportantOnly)), sound.Text);
            Assert.Equal(Cs(nameof(Strings.SettingsConnectionPrusaLink)), printer.SelectedConnectionType.Text);
            Assert.Equal("Podle systému", vm.LanguageOptions[0].Text);
            Assert.Equal("Deutsch", vm.LanguageOptions[2].Text);
        }

        // Same option instances (the selection stays), the edits are kept.
        Assert.Contains(nameof(LocalizedOption.Text), soundChanges);
        Assert.Same(sound, vm.SelectedSound);
        Assert.Same(language, vm.SelectedLanguage);
        Assert.Equal(0.5m, vm.PollingIntervalSeconds);
        Assert.Equal("Alpha", printer.Name);
        Assert.True(printer.TestFailed);
    }

    [Fact]
    public async Task RefreshTextsFormatsUserNameTextsInTheNewLanguageAndKeepsTheUserName()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.UserName = "john:doe";
        List<string?> changes = [];
        printer.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.Equal("The user name must not contain a colon (:).", printer.UserNameError);

        using (new CultureScope("cs-CZ"))
        {
            vm.RefreshTexts();

            Assert.Equal(Cs(nameof(Strings.SettingsErrorUserNameColon)), printer.UserNameError);
            Assert.Equal(Cs(nameof(Strings.SettingsUserNameHint)), printer.UserNameHint);
        }

        Assert.Contains(nameof(PrinterEditorViewModel.UserNameError), changes);
        Assert.Contains(nameof(PrinterEditorViewModel.UserNameHint), changes);
        Assert.Equal("john:doe", printer.UserName);
    }

    [Fact]
    public async Task RefreshTextsFormatsTheTestResultOfAnInvalidUserNameInTheNewLanguage()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.UserName = new string('u', 1000);
        await printer.TestConnectionCommand.ExecuteAsync(null);
        Assert.NotNull(printer.UserNameError);
        // A refused test shows the short text; the explanation is at the field (T-033, Q2).
        Assert.Equal(Strings.ResourceManager.GetString(nameof(Strings.SettingsFixErrors), CultureInfo.InvariantCulture), printer.TestResultText);
        Assert.Empty(_tester.Calls);

        using (new CultureScope("cs-CZ"))
        {
            vm.RefreshTexts();

            string czech = string.Format(CultureInfo.CurrentCulture, Cs(nameof(Strings.SettingsErrorUserNameTooLong)), PrinterUserName.MaxLength);
            Assert.Equal(czech, printer.UserNameError);
            Assert.Equal(Cs(nameof(Strings.SettingsFixErrors)), printer.TestResultText);
            Assert.True(printer.TestFailed);
        }
    }

    [Fact]
    public async Task RefreshTextsOfANewPrinterKeepsItsErrorsHidden()
    {
        SettingsViewModel vm = await LoadAsync(AppSettings.Default);
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel printer = vm.Printers[0];

        using (new CultureScope("cs-CZ"))
        {
            vm.RefreshTexts();

            Assert.Null(printer.NameError);
            Assert.Null(printer.AddressError);
            Assert.Equal(Cs(nameof(Strings.SettingsNewPrinter)), printer.DisplayName);
        }
    }

    [Fact]
    public void RefreshTextsBeforeLoadingShowsNoErrors()
    {
        SettingsViewModel vm = Create();

        vm.RefreshTexts();

        Assert.Null(vm.PollingIntervalError);
        Assert.Null(vm.RemainingTimeError);
        Assert.Null(vm.Notice);
        Assert.Null(vm.StatusText);
    }

    [Fact]
    public async Task RefreshTextsAfterDisposeDoesNothing()
    {
        SettingsViewModel vm = await LoadAsync(AppSettings.Default);
        vm.Dispose();

        vm.RefreshTexts();
    }

    private static string Cs(string key) => Strings.ResourceManager.GetString(key, Czech)!;

    private SettingsViewModel Create()
    {
        _viewModel?.Dispose();
        SettingsViewModel vm = new(_store, _tester, Supported, static () => Guid.NewGuid().ToString("N"));
        _viewModel = vm;
        return vm;
    }

    private async Task<SettingsViewModel> LoadAsync(AppSettings settings)
    {
        _store.Data = new SettingsEditorData(settings, SettingsLoadStatus.Loaded, [], Ids([.. settings.Printers.Select(static p => p.Id)]));
        SettingsViewModel vm = Create();
        await vm.LoadAsync();
        return vm;
    }
}
