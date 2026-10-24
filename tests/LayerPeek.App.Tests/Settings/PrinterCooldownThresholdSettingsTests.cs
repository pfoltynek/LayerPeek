using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The cool-down threshold (T-079) has no field in the settings window yet (T-080): a save from the window keeps the
/// threshold of every printer (data-loss guard).
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

    private async Task<SettingsViewModel> LoadAsync(params PrinterDefinition[] printers)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = printers }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(_store, new FakeConnectionTester(), Supported, cameraTester: new FakeCameraTester());
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }
}
