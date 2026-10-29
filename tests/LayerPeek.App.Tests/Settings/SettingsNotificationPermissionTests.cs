using LayerPeek.App.Platform;
using LayerPeek.App.Settings;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The warning "notifications are turned off in macOS" of the General tab and its button to System Settings (T-020, user
/// decision 2026-10-29), on a stand-in of the OS settings: System Settings never opens in a test.
/// </summary>
public sealed class SettingsNotificationPermissionTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly FakeEditorStore _store = new();
    private readonly FakeNotificationSettings _system = new();
    private SettingsViewModel? _viewModel;

    public void Dispose() => _viewModel?.Dispose();

    [Fact]
    public async Task TurnedOffNotificationsShowTheWarningAndTheButtonOpensSystemSettings()
    {
        _system.TurnedOff = true;
        SettingsViewModel vm = await LoadAsync(_system);

        Assert.True(vm.ShowsNotificationsTurnedOff);
        Assert.True(vm.OpenNotificationSettingsCommand.CanExecute(null));
        vm.OpenNotificationSettingsCommand.Execute(null);
        Assert.Equal(1, _system.Opened);
        // Saving is not needed and nothing of the settings changed.
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task AllowedNotificationsShowNothing()
    {
        SettingsViewModel vm = await LoadAsync(_system);

        Assert.False(vm.ShowsNotificationsTurnedOff);
        Assert.False(vm.OpenNotificationSettingsCommand.CanExecute(null));
        vm.OpenNotificationSettingsCommand.Execute(null);
        Assert.Equal(0, _system.Opened);
    }

    [Fact]
    public async Task WithoutOsSettingsThereIsNoWarning()
    {
        SettingsViewModel vm = await LoadAsync(null);

        Assert.False(vm.ShowsNotificationsTurnedOff);
        await vm.RefreshNotificationPermissionAsync();
        Assert.False(vm.ShowsNotificationsTurnedOff);
    }

    [Fact]
    public async Task ComingBackAfterAllowingThemHidesTheWarning()
    {
        _system.TurnedOff = true;
        SettingsViewModel vm = await LoadAsync(_system);
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _system.TurnedOff = false;
        await vm.RefreshNotificationPermissionAsync();

        Assert.False(vm.ShowsNotificationsTurnedOff);
        Assert.False(vm.OpenNotificationSettingsCommand.CanExecute(null));
        Assert.Contains(nameof(SettingsViewModel.ShowsNotificationsTurnedOff), changed);

        _system.TurnedOff = true;
        await vm.RefreshNotificationPermissionAsync();
        Assert.True(vm.ShowsNotificationsTurnedOff);
    }

    [Fact]
    public async Task AClosedWindowReadsNothingMore()
    {
        SettingsViewModel vm = await LoadAsync(_system);
        int reads = _system.Reads;
        vm.Dispose();
        _system.TurnedOff = true;

        await vm.RefreshNotificationPermissionAsync();

        Assert.Equal(reads, _system.Reads);
        Assert.False(vm.ShowsNotificationsTurnedOff);
    }

    private async Task<SettingsViewModel> LoadAsync(ISystemNotificationSettings? system)
    {
        _store.Data = new SettingsEditorData(new AppSettings(), SettingsLoadStatus.Loaded, [], Ids());
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, static () => Guid.NewGuid().ToString("N"), productVersion: "0.1.0", notificationSettings: system);
        await _viewModel.LoadAsync();
        return _viewModel;
    }

    private sealed class FakeNotificationSettings : ISystemNotificationSettings
    {
        public bool TurnedOff { get; set; }

        public int Opened { get; private set; }

        public int Reads { get; private set; }

        public Task<bool> AreTurnedOffAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(TurnedOff);
        }

        public void Open() => Opened++;
    }
}
