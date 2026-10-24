using LayerPeek.App.Settings;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>The "Detailed logging (Debug)" check box of the settings window (T-047).</summary>
public sealed class SettingsDebugLoggingTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly FakeEditorStore _store = new();
    private SettingsViewModel? _viewModel;

    public void Dispose() => _viewModel?.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadShowsTheSavedSetting(bool enabled)
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { DebugLogging = enabled });

        Assert.Equal(enabled, vm.DebugLogging);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task TurningItOnIsSaved()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });

        vm.DebugLogging = true;
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        SettingsChange change = Assert.Single(_store.Saves);
        Assert.False(change.Original.DebugLogging);
        Assert.True(change.Updated.DebugLogging);
    }

    [Fact]
    public async Task TurningItBackIsNotAChange()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { DebugLogging = true });

        vm.DebugLogging = false;
        vm.DebugLogging = true;

        Assert.False(vm.IsDirty);
    }

    private async Task<SettingsViewModel> LoadAsync(AppSettings settings)
    {
        _store.Data = new SettingsEditorData(settings, SettingsLoadStatus.Loaded, [], Ids([.. settings.Printers.Select(static p => p.Id)]));
        _viewModel?.Dispose();
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, static () => Guid.NewGuid().ToString("N"));
        await _viewModel.LoadAsync();
        return _viewModel;
    }
}
