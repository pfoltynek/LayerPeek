using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The cool-down thresholds of each printer (T-079; nozzle and bed since T-109) in the settings window (T-080, T-092): the
/// two fields with their validation (whole degrees 30–100), saving, "Use thresholds for all printers" and the "Printer
/// cooled down" notification switch.
/// </summary>
public sealed class PrinterCooldownThresholdSettingsTests : IDisposable
{
    private const string AppliedAll = "The thresholds were copied to the other printers. They apply after saving.";

    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private SettingsViewModel? _viewModel;

    public static TheoryData<bool> Fields => [true, false];

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public async Task UnrelatedEditKeepsTheThresholds()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha") with { CooldownThresholds = new(35, 55) }, LinkPrinter("b", "Beta", "192.168.1.21"));

        vm.Printers[0].Name = "Alpha 2";
        vm.Printers[1].Name = "Beta 2";
        await vm.SaveCommand.ExecuteAsync(null);

        SettingsChange change = Assert.Single(_store.Saves);
        Assert.Equal([new CooldownThresholds(35, 55), CooldownThresholds.Default], change.Updated.Printers.Select(static p => p.CooldownThresholds));
    }

    [Fact]
    public async Task UntouchedPrinterWithThresholdsIsNotDirty()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha") with { CooldownThresholds = new(90, 31) });

        Assert.False(vm.IsDirty);
    }

    // ---- The fields ----

    [Fact]
    public async Task FieldsShowTheSavedThresholdsAndANewPrinterTheDefaults()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha") with { CooldownThresholds = new(35, 55) });

        Assert.Equal(35m, vm.Printers[0].CooldownNozzleThresholdCelsius);
        Assert.Equal(55m, vm.Printers[0].CooldownBedThresholdCelsius);
        Assert.Null(vm.Printers[0].CooldownThresholdError);
        Assert.True(vm.Printers[0].ShowsCooldownThresholdHint);

        vm.AddPrinterCommand.Execute(null);

        // T-109: nozzle 40 °C and bed 40 °C.
        Assert.Equal(40m, vm.SelectedPrinter!.CooldownNozzleThresholdCelsius);
        Assert.Equal(40m, vm.SelectedPrinter.CooldownBedThresholdCelsius);
        Assert.Equal(30m, PrinterEditorViewModel.MinCooldownThresholdCelsius);
        Assert.Equal(100m, PrinterEditorViewModel.MaxCooldownThresholdCelsius);
    }

    [Theory]
    [InlineData(30, 100)]
    [InlineData(64, 45)]
    [InlineData(100, 30)]
    public async Task ValidThresholdsAreSaved(int nozzle, int bed)
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));

        vm.Printers[0].CooldownNozzleThresholdCelsius = nozzle;
        vm.Printers[0].CooldownBedThresholdCelsius = bed;
        Assert.True(vm.IsDirty);
        Assert.Null(vm.Printers[0].CooldownThresholdError);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new CooldownThresholds(nozzle, bed), Assert.Single(Assert.Single(_store.Saves).Updated.Printers).CooldownThresholds);
        Assert.False(vm.IsDirty);
    }

    [Theory]
    [InlineData("29", true)]
    [InlineData("101", true)]
    [InlineData("0", true)]
    [InlineData("-50", true)]
    [InlineData("50.5", true)]
    [InlineData("79228162514264337593543950335", true)]
    [InlineData(null, true)]
    [InlineData("29", false)]
    [InlineData("50.5", false)]
    [InlineData(null, false)]
    public async Task InvalidThresholdIsNeverSavedAndShowsTheError(string? value, bool nozzle)
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha") with { CooldownThresholds = new(40, 45) }, LinkPrinter("b", "Beta", "192.168.1.21"));
        vm.SelectedPrinterTabIndex = SettingsViewModel.ConnectionPrinterTab;
        vm.SelectedTabIndex = SettingsViewModel.GeneralTab;
        PrinterEditorViewModel printer = vm.Printers[1];

        Set(printer, nozzle, value is null ? null : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal("Enter a whole number from 30 to 100.", printer.CooldownThresholdError);
        Assert.False(printer.ShowsCooldownThresholdHint);
        Assert.True(printer.HasErrors);
        Assert.True(printer.ShowsWidgetErrorMarker);
        Assert.True(vm.ShowsWidgetsErrorMarker);
        Assert.Equal("Widgets – contains errors", vm.WidgetsTabName);
        Assert.False(printer.ShowsConnectionErrorMarker);
        Assert.False(printer.ShowsCameraErrorMarker);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Same(printer, vm.SelectedPrinter);
        // T-092: the Widgets tab with the printer; the printer sub-tab stays.
        Assert.Equal(SettingsViewModel.WidgetsTab, vm.SelectedTabIndex);
        Assert.Equal(SettingsViewModel.ConnectionPrinterTab, vm.SelectedPrinterTabIndex);
        Assert.True(vm.StatusIsError);
    }

    [Fact]
    public async Task BothInvalidShowOneError()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.CooldownNozzleThresholdCelsius = null;
        printer.CooldownBedThresholdCelsius = 120;
        Assert.Equal("Enter a whole number from 30 to 100.", printer.CooldownThresholdError);

        // Fixing one keeps the error for the other.
        printer.CooldownNozzleThresholdCelsius = 40;
        Assert.NotNull(printer.CooldownThresholdError);

        printer.CooldownBedThresholdCelsius = 40;
        Assert.Null(printer.CooldownThresholdError);
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task FixingTheThresholdRemovesTheErrorAndSaves(bool nozzle)
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        Set(printer, nozzle, 120);
        Assert.NotNull(printer.CooldownThresholdError);

        Set(printer, nozzle, 75);

        Assert.Null(printer.CooldownThresholdError);
        Assert.True(printer.ShowsCooldownThresholdHint);
        Assert.False(printer.ShowsWidgetErrorMarker);
        Assert.False(_viewModel!.ShowsWidgetsErrorMarker);
        Assert.Equal("Widgets", _viewModel.WidgetsTabName);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(
            nozzle ? new CooldownThresholds(75, 40) : new CooldownThresholds(40, 75),
            Assert.Single(Assert.Single(_store.Saves).Updated.Printers).CooldownThresholds);
    }

    [Fact]
    public async Task ThresholdsStayEditableWithTheCameraOff()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        Assert.False(vm.Printers[0].CameraEnabled);

        vm.Printers[0].CooldownNozzleThresholdCelsius = 45;
        vm.Printers[0].CooldownBedThresholdCelsius = 35;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new CooldownThresholds(45, 35), Assert.Single(Assert.Single(_store.Saves).Updated.Printers).CooldownThresholds);
    }

    [Fact]
    public async Task ErrorFollowsALanguageSwitch()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        vm.Printers[0].CooldownBedThresholdCelsius = 10;

        using (new UiCultureScope("cs"))
        {
            vm.RefreshTexts();

            Assert.Equal("Zadejte celé číslo od 30 do 100.", vm.Printers[0].CooldownThresholdError);
            Assert.Equal("Widgety – obsahuje chyby", vm.WidgetsTabName);
        }
    }

    // ---- Use thresholds for all printers ----

    [Fact]
    public async Task ApplyToAllCopiesBothThresholdsToTheOtherPrintersAndSavesWithTheRest()
    {
        PrinterDefinition beta = LinkPrinter("b", "Beta", "192.168.1.21") with { CooldownThresholds = new(90, 31) };
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"), beta, CloudPrinter("c", "Cloud XL"));
        PrinterEditorViewModel alpha = vm.Printers[0];
        alpha.CooldownNozzleThresholdCelsius = 42;
        alpha.CooldownBedThresholdCelsius = 36;

        Assert.True(alpha.ApplyCooldownThresholdsToAllCommand.CanExecute(null));
        alpha.ApplyCooldownThresholdsToAllCommand.Execute(null);

        Assert.Equal([42m, 42m, 42m], vm.Printers.Select(static p => p.CooldownNozzleThresholdCelsius));
        Assert.Equal([36m, 36m, 36m], vm.Printers.Select(static p => p.CooldownBedThresholdCelsius));
        Assert.Equal(AppliedAll, vm.StatusText);
        Assert.False(vm.StatusIsError);
        Assert.True(vm.IsDirty);
        // Nothing is saved yet.
        Assert.Empty(_store.Saves);

        await vm.SaveCommand.ExecuteAsync(null);

        SettingsChange change = Assert.Single(_store.Saves);
        CooldownThresholds copied = new(42, 36);
        Assert.All(change.Updated.Printers, p => Assert.Equal(copied, p.CooldownThresholds));
        // Only the thresholds changed.
        Assert.Equal(change.Original.Printers.Select(p => p with { CooldownThresholds = copied }), change.Updated.Printers);
    }

    [Fact]
    public async Task ApplyToAllCopiesTheBedThresholdAlsoWhenTheNozzleThresholdIsTheSame()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta", "192.168.1.21") with { CooldownThresholds = new(40, 70) });

        vm.Printers[0].ApplyCooldownThresholdsToAllCommand.Execute(null);

        Assert.Equal(40m, vm.Printers[1].CooldownBedThresholdCelsius);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task ApplyToAllFollowsALanguageSwitch()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta", "192.168.1.21"));
        vm.Printers[0].ApplyCooldownThresholdsToAllCommand.Execute(null);

        using (new UiCultureScope("cs"))
        {
            vm.RefreshTexts();

            Assert.Equal("Prahy se zkopírovaly do ostatních tiskáren. Projeví se po uložení.", vm.StatusText);
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(25, true)]
    [InlineData(null, false)]
    [InlineData(101, false)]
    public async Task InvalidThresholdCopiesNothing(int? celsius, bool nozzle)
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta", "192.168.1.21") with { CooldownThresholds = new(60, 61) });
        PrinterEditorViewModel alpha = vm.Printers[0];
        Set(alpha, nozzle, celsius);

        alpha.ApplyCooldownThresholdsToAllCommand.Execute(null);

        // Neither of the two is copied.
        Assert.Equal(60m, vm.Printers[1].CooldownNozzleThresholdCelsius);
        Assert.Equal(61m, vm.Printers[1].CooldownBedThresholdCelsius);
        Assert.NotNull(alpha.CooldownThresholdError);
        Assert.NotEqual(AppliedAll, vm.StatusText);
    }

    [Fact]
    public async Task ApplyToAllIsDisabledWithASinglePrinterAndSaysWhy()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel alpha = vm.Printers[0];

        Assert.False(alpha.ApplyCooldownThresholdsToAllCommand.CanExecute(null));
        Assert.Equal("Only one printer is set up.", alpha.ApplyToAllUnavailableReason);

        alpha.ApplyCooldownThresholdsToAllCommand.Execute(null);
        Assert.Null(vm.StatusText);

        vm.AddPrinterCommand.Execute(null);

        Assert.True(alpha.ApplyCooldownThresholdsToAllCommand.CanExecute(null));
        Assert.Null(alpha.ApplyToAllUnavailableReason);

        vm.RemovePrinterCommand.Execute(null);

        Assert.False(alpha.ApplyCooldownThresholdsToAllCommand.CanExecute(null));
        Assert.Equal("Only one printer is set up.", alpha.ApplyToAllUnavailableReason);
    }

    [Fact]
    public async Task ApplyToAllReachesAnUnsavedNewPrinter()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel added = vm.SelectedPrinter!;
        vm.Printers[0].CooldownNozzleThresholdCelsius = 33;
        vm.Printers[0].CooldownBedThresholdCelsius = 34;

        vm.Printers[0].ApplyCooldownThresholdsToAllCommand.Execute(null);

        Assert.Equal(33m, added.CooldownNozzleThresholdCelsius);
        Assert.Equal(34m, added.CooldownBedThresholdCelsius);
    }

    // ---- "Printer cooled down" notification switch ----

    [Fact]
    public async Task NotificationSwitchIsOffByDefaultAndSaved()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        Assert.False(vm.NotifyCooledDown);

        vm.NotifyCooledDown = true;
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        NotificationSettings saved = Assert.Single(_store.Saves).Updated.Notifications;
        Assert.True(saved.CooledDown);
        Assert.Equal(new NotificationSettings { CooledDown = true }, saved);
    }

    [Fact]
    public async Task SavedNotificationSwitchIsLoaded()
    {
        _store.Data = new SettingsEditorData(
            new AppSettings { Printers = [LinkPrinter("a", "Alpha")], Notifications = new NotificationSettings { CooledDown = true } },
            SettingsLoadStatus.Loaded,
            [],
            Ids());
        SettingsViewModel vm = new(_store, new FakeConnectionTester(), Supported, cameraTester: new FakeCameraTester());
        _viewModel = vm;
        await vm.LoadAsync();

        Assert.True(vm.NotifyCooledDown);
        Assert.False(vm.IsDirty);

        vm.NotifyCooledDown = false;
        Assert.True(vm.IsDirty);
    }

    private static void Set(PrinterEditorViewModel printer, bool nozzle, decimal? value)
    {
        if (nozzle)
        {
            printer.CooldownNozzleThresholdCelsius = value;
        }
        else
        {
            printer.CooldownBedThresholdCelsius = value;
        }
    }

    private async Task<SettingsViewModel> LoadAsync(params PrinterDefinition[] printers)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = printers }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(_store, new FakeConnectionTester(), Supported, cameraTester: new FakeCameraTester());
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }
}
