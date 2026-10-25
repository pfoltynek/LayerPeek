using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The cool-down threshold of each printer (T-079) in the settings window (T-080): the field with its validation
/// (whole degrees 30–100), saving, "Use threshold for all printers" and the "Printer cooled down" notification switch.
/// </summary>
public sealed class PrinterCooldownThresholdSettingsTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private SettingsViewModel? _viewModel;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public async Task UnrelatedEditKeepsTheThreshold()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha") with { CooldownThresholdCelsius = 35 }, LinkPrinter("b", "Beta", "192.168.1.21"));

        vm.Printers[0].Name = "Alpha 2";
        vm.Printers[1].Name = "Beta 2";
        await vm.SaveCommand.ExecuteAsync(null);

        SettingsChange change = Assert.Single(_store.Saves);
        Assert.Equal([35, 50], change.Updated.Printers.Select(static p => p.CooldownThresholdCelsius));
    }

    [Fact]
    public async Task UntouchedPrinterWithThresholdIsNotDirty()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha") with { CooldownThresholdCelsius = 90 });

        Assert.False(vm.IsDirty);
    }

    // ---- The field ----

    [Fact]
    public async Task FieldShowsTheSavedThresholdAndANewPrinterTheDefault()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha") with { CooldownThresholdCelsius = 35 });

        Assert.Equal(35m, vm.Printers[0].CooldownThresholdCelsius);
        Assert.Null(vm.Printers[0].CooldownThresholdError);
        Assert.True(vm.Printers[0].ShowsCooldownThresholdHint);

        vm.AddPrinterCommand.Execute(null);

        Assert.Equal(CooldownThreshold.DefaultCelsius, vm.SelectedPrinter!.CooldownThresholdCelsius);
        Assert.Equal(30m, PrinterEditorViewModel.MinCooldownThresholdCelsius);
        Assert.Equal(100m, PrinterEditorViewModel.MaxCooldownThresholdCelsius);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(64)]
    [InlineData(100)]
    public async Task ValidThresholdIsSaved(int celsius)
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));

        vm.Printers[0].CooldownThresholdCelsius = celsius;
        Assert.True(vm.IsDirty);
        Assert.Null(vm.Printers[0].CooldownThresholdError);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(celsius, Assert.Single(Assert.Single(_store.Saves).Updated.Printers).CooldownThresholdCelsius);
        Assert.False(vm.IsDirty);
    }

    [Theory]
    [InlineData("29")]
    [InlineData("101")]
    [InlineData("0")]
    [InlineData("-50")]
    [InlineData("50.5")]
    [InlineData("79228162514264337593543950335")]
    [InlineData(null)]
    public async Task InvalidThresholdIsNeverSavedAndShowsTheError(string? value)
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha") with { CooldownThresholdCelsius = 40 }, LinkPrinter("b", "Beta", "192.168.1.21"));
        vm.SelectedPrinterTabIndex = SettingsViewModel.ConnectionPrinterTab;
        vm.SelectedTabIndex = SettingsViewModel.GeneralTab;
        PrinterEditorViewModel printer = vm.Printers[1];

        printer.CooldownThresholdCelsius = value is null ? null : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("Enter a whole number from 30 to 100.", printer.CooldownThresholdError);
        Assert.False(printer.ShowsCooldownThresholdHint);
        Assert.True(printer.HasErrors);
        Assert.True(printer.ShowsOverlayErrorMarker);
        Assert.Equal("Overlay – contains errors", printer.OverlayTabName);
        Assert.False(printer.ShowsConnectionErrorMarker);
        Assert.False(printer.ShowsCameraErrorMarker);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Same(printer, vm.SelectedPrinter);
        Assert.Equal(SettingsViewModel.PrintersTab, vm.SelectedTabIndex);
        Assert.Equal(SettingsViewModel.OverlayPrinterTab, vm.SelectedPrinterTabIndex);
        Assert.True(vm.StatusIsError);
    }

    [Fact]
    public async Task FixingTheThresholdRemovesTheErrorAndSaves()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.CooldownThresholdCelsius = 120;
        Assert.NotNull(printer.CooldownThresholdError);

        printer.CooldownThresholdCelsius = 75;

        Assert.Null(printer.CooldownThresholdError);
        Assert.True(printer.ShowsCooldownThresholdHint);
        Assert.False(printer.ShowsOverlayErrorMarker);
        Assert.Equal("Overlay", printer.OverlayTabName);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(75, Assert.Single(Assert.Single(_store.Saves).Updated.Printers).CooldownThresholdCelsius);
    }

    [Fact]
    public async Task ThresholdStaysEditableWithTheCameraOff()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        Assert.False(vm.Printers[0].CameraEnabled);

        vm.Printers[0].CooldownThresholdCelsius = 45;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(45, Assert.Single(Assert.Single(_store.Saves).Updated.Printers).CooldownThresholdCelsius);
    }

    [Fact]
    public async Task ErrorFollowsALanguageSwitch()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        vm.Printers[0].CooldownThresholdCelsius = 10;

        using (new UiCultureScope("cs"))
        {
            vm.RefreshTexts();

            Assert.Equal("Zadejte celé číslo od 30 do 100.", vm.Printers[0].CooldownThresholdError);
            Assert.Equal("Překryv – obsahuje chyby", vm.Printers[0].OverlayTabName);
        }
    }

    // ---- Use threshold for all printers ----

    [Fact]
    public async Task ApplyToAllCopiesTheThresholdToTheOtherPrintersAndSavesWithTheRest()
    {
        PrinterDefinition beta = LinkPrinter("b", "Beta", "192.168.1.21") with { CooldownThresholdCelsius = 90 };
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"), beta, CloudPrinter("c", "Cloud XL"));
        PrinterEditorViewModel alpha = vm.Printers[0];
        alpha.CooldownThresholdCelsius = 42;

        Assert.True(alpha.ApplyCooldownThresholdToAllCommand.CanExecute(null));
        alpha.ApplyCooldownThresholdToAllCommand.Execute(null);

        Assert.Equal([42m, 42m, 42m], vm.Printers.Select(static p => p.CooldownThresholdCelsius));
        Assert.Equal("The threshold was copied to the other printers. It applies after saving.", vm.StatusText);
        Assert.False(vm.StatusIsError);
        Assert.True(vm.IsDirty);
        // Nothing is saved yet.
        Assert.Empty(_store.Saves);

        await vm.SaveCommand.ExecuteAsync(null);

        SettingsChange change = Assert.Single(_store.Saves);
        Assert.Equal([42, 42, 42], change.Updated.Printers.Select(static p => p.CooldownThresholdCelsius));
        // Only the threshold changed.
        Assert.Equal(change.Original.Printers.Select(static p => p with { CooldownThresholdCelsius = 42 }), change.Updated.Printers);
    }

    [Fact]
    public async Task ApplyToAllFollowsALanguageSwitch()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta", "192.168.1.21"));
        vm.Printers[0].ApplyCooldownThresholdToAllCommand.Execute(null);

        using (new UiCultureScope("cs"))
        {
            vm.RefreshTexts();

            Assert.Equal("Práh se zkopíroval do ostatních tiskáren. Projeví se po uložení.", vm.StatusText);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(25)]
    public async Task InvalidThresholdIsNotCopied(int? celsius)
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta", "192.168.1.21") with { CooldownThresholdCelsius = 60 });
        PrinterEditorViewModel alpha = vm.Printers[0];
        alpha.CooldownThresholdCelsius = celsius;

        alpha.ApplyCooldownThresholdToAllCommand.Execute(null);

        Assert.Equal(60m, vm.Printers[1].CooldownThresholdCelsius);
        Assert.NotNull(alpha.CooldownThresholdError);
        Assert.NotEqual("The threshold was copied to the other printers. It applies after saving.", vm.StatusText);
    }

    [Fact]
    public async Task ApplyToAllIsDisabledWithASinglePrinterAndSaysWhy()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel alpha = vm.Printers[0];

        Assert.False(alpha.ApplyCooldownThresholdToAllCommand.CanExecute(null));
        Assert.Equal("Only one printer is set up.", alpha.ApplyToAllUnavailableReason);

        alpha.ApplyCooldownThresholdToAllCommand.Execute(null);
        Assert.Null(vm.StatusText);

        vm.AddPrinterCommand.Execute(null);

        Assert.True(alpha.ApplyCooldownThresholdToAllCommand.CanExecute(null));
        Assert.Null(alpha.ApplyToAllUnavailableReason);

        vm.RemovePrinterCommand.Execute(null);

        Assert.False(alpha.ApplyCooldownThresholdToAllCommand.CanExecute(null));
        Assert.Equal("Only one printer is set up.", alpha.ApplyToAllUnavailableReason);
    }

    [Fact]
    public async Task ApplyToAllReachesAnUnsavedNewPrinter()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel added = vm.SelectedPrinter!;
        vm.Printers[0].CooldownThresholdCelsius = 33;

        vm.Printers[0].ApplyCooldownThresholdToAllCommand.Execute(null);

        Assert.Equal(33m, added.CooldownThresholdCelsius);
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

    private async Task<SettingsViewModel> LoadAsync(params PrinterDefinition[] printers)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = printers }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(_store, new FakeConnectionTester(), Supported, cameraTester: new FakeCameraTester());
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }
}
