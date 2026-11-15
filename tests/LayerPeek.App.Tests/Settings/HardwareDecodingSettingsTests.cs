using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// T-204: "Hardware decoding" in the settings: Automatic (the default) or Off, the description of the selected option, dirty
/// tracking and saving (global, for every camera).
/// </summary>
public sealed class HardwareDecodingSettingsTests : IDisposable
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

    private async Task<SettingsViewModel> LoadAsync(CameraHardwareDecoding mode = CameraHardwareDecoding.Automatic)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = [LinkPrinter("a", "Alpha")], CameraHardwareDecoding = mode }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(_store, new FakeConnectionTester(), Supported);
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }

    private async Task<AppSettings> SaveAsync(SettingsViewModel vm)
    {
        await vm.SaveCommand.ExecuteAsync(null);
        return Assert.Single(_store.Saves).Updated;
    }

    private static HardwareDecodingOption Option(SettingsViewModel vm, CameraHardwareDecoding mode) =>
        vm.HardwareDecodingOptions.Single(option => option.Value == mode);

    [Fact]
    public async Task TheDefaultIsAutomatic()
    {
        SettingsViewModel vm = await LoadAsync();

        Assert.Equal([CameraHardwareDecoding.Automatic, CameraHardwareDecoding.Off], vm.HardwareDecodingOptions.Select(static option => option.Value));
        Assert.Equal(["Automatic", "Off"], vm.HardwareDecodingOptions.Select(static option => option.Text));
        Assert.Equal(CameraHardwareDecoding.Automatic, vm.SelectedHardwareDecoding!.Value);
        Assert.StartsWith("The graphics hardware decodes", vm.HardwareDecodingDescription, StringComparison.Ordinal);
        // The note is the composition root's decision (by OS); a view model without it shows none.
        Assert.False(vm.ShowsHardwareDecodingSoftwareNote);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task OffIsDirtySavedAndBackToAutomaticIsClean()
    {
        SettingsViewModel vm = await LoadAsync();
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SelectedHardwareDecoding = Option(vm, CameraHardwareDecoding.Off);

        Assert.Equal(CameraHardwareDecoding.Off, vm.SelectedHardwareDecoding!.Value);
        Assert.StartsWith("Always decoded by the processor", vm.HardwareDecodingDescription, StringComparison.Ordinal);
        Assert.Contains(nameof(SettingsViewModel.SelectedHardwareDecoding), changed);
        Assert.Contains(nameof(SettingsViewModel.HardwareDecodingDescription), changed);
        Assert.True(vm.IsDirty);

        vm.SelectedHardwareDecoding = Option(vm, CameraHardwareDecoding.Automatic);
        Assert.False(vm.IsDirty);

        vm.SelectedHardwareDecoding = Option(vm, CameraHardwareDecoding.Off);
        Assert.Equal(CameraHardwareDecoding.Off, (await SaveAsync(vm)).CameraHardwareDecoding);
    }

    [Fact]
    public async Task ASavedOffIsShownAndKeptWhenSomethingElseIsSaved()
    {
        SettingsViewModel vm = await LoadAsync(CameraHardwareDecoding.Off);

        Assert.Equal(CameraHardwareDecoding.Off, vm.SelectedHardwareDecoding!.Value);
        Assert.False(vm.IsDirty);

        vm.CameraWindowAutoClose = !vm.CameraWindowAutoClose;
        Assert.Equal(CameraHardwareDecoding.Off, (await SaveAsync(vm)).CameraHardwareDecoding);
    }

    [Fact]
    public async Task NullOrTheSameOptionChangesNothing()
    {
        SettingsViewModel vm = await LoadAsync();
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SelectedHardwareDecoding = null;
        vm.SelectedHardwareDecoding = Option(vm, CameraHardwareDecoding.Automatic);

        Assert.DoesNotContain(nameof(SettingsViewModel.SelectedHardwareDecoding), changed);
        Assert.Equal(CameraHardwareDecoding.Automatic, vm.SelectedHardwareDecoding!.Value);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task TheTextsFollowTheLanguage()
    {
        SettingsViewModel vm = await LoadAsync(CameraHardwareDecoding.Off);
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        using (new CultureScope("cs-CZ"))
        {
            vm.RefreshTexts();

            Assert.Equal(["Automaticky", "Vypnuto"], vm.HardwareDecodingOptions.Select(static option => option.Text));
            Assert.StartsWith("Vždy dekóduje procesor", vm.HardwareDecodingDescription, StringComparison.Ordinal);
        }

        Assert.Contains(nameof(SettingsViewModel.HardwareDecodingDescription), changed);
    }
}
