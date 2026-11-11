using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// T-185: the frame rate of the mini preview in the settings: the profiles Economy / Balanced / Smooth (5 / 10 fps / no
/// limit), the slider of 1–30 fps that selects "Custom" off a profile's value, the description of the selected profile,
/// dirty tracking and saving (global, for every camera).
/// </summary>
public sealed class MiniFrameRateSettingsTests : IDisposable
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

    private async Task<SettingsViewModel> LoadAsync(int? rate = MiniPreviewFrameRate.Default)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = [LinkPrinter("a", "Alpha")], MiniPreviewFrameRate = rate }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(_store, new FakeConnectionTester(), Supported);
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }

    private async Task<int?> SaveAsync(SettingsViewModel vm)
    {
        await vm.SaveCommand.ExecuteAsync(null);
        return Assert.Single(_store.Saves).Updated.MiniPreviewFrameRate;
    }

    private static MiniFrameRateProfileOption Profile(SettingsViewModel vm, MiniPreviewFrameRateProfile profile) =>
        vm.MiniFrameRateProfiles.Single(option => option.Value == profile);

    [Fact]
    public async Task TheDefaultIsBalancedTenFps()
    {
        SettingsViewModel vm = await LoadAsync();

        Assert.Equal(
            [MiniPreviewFrameRateProfile.Economy, MiniPreviewFrameRateProfile.Balanced, MiniPreviewFrameRateProfile.Smooth, MiniPreviewFrameRateProfile.Custom],
            vm.MiniFrameRateProfiles.Select(static option => option.Value));
        Assert.Equal(["Economy", "Balanced", "Smooth", "Custom"], vm.MiniFrameRateProfiles.Select(static option => option.Text));
        Assert.Equal(MiniPreviewFrameRateProfile.Balanced, vm.SelectedMiniFrameRateProfile!.Value);
        Assert.Equal(10, vm.MiniFrameRateSlider);
        Assert.Equal("10\u00A0fps", vm.MiniFrameRateText);
        Assert.StartsWith("10\u00A0fps (recommended)", vm.MiniFrameRateDescription, StringComparison.Ordinal);
        Assert.Equal((1.0, 30.0), (SettingsViewModel.MinMiniFrameRate, SettingsViewModel.MaxMiniFrameRate));
        Assert.False(vm.IsDirty);
    }

    [Theory]
    [InlineData(5, MiniPreviewFrameRateProfile.Economy, 5, "5\u00A0fps")]
    [InlineData(null, MiniPreviewFrameRateProfile.Smooth, 30, "No limit")]
    [InlineData(17, MiniPreviewFrameRateProfile.Custom, 17, "17\u00A0fps")]
    [InlineData(30, MiniPreviewFrameRateProfile.Custom, 30, "30\u00A0fps")]
    public async Task TheSavedLimitIsShownWithItsProfile(int? rate, MiniPreviewFrameRateProfile profile, double slider, string text)
    {
        SettingsViewModel vm = await LoadAsync(rate);

        Assert.Equal(profile, vm.SelectedMiniFrameRateProfile!.Value);
        Assert.Equal(slider, vm.MiniFrameRateSlider);
        Assert.Equal(text, vm.MiniFrameRateText);
        Assert.False(vm.IsDirty);
    }

    [Theory]
    [InlineData(MiniPreviewFrameRateProfile.Economy, 5, 5.0, "5\u00A0fps", "5\u00A0fps: the lowest CPU")]
    [InlineData(MiniPreviewFrameRateProfile.Smooth, null, 30.0, "No limit", "The camera's full frame rate")]
    public async Task AProfileMovesTheSliderToItsLimitAndIsSaved(MiniPreviewFrameRateProfile profile, int? rate, double slider, string text, string description)
    {
        SettingsViewModel vm = await LoadAsync();
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SelectedMiniFrameRateProfile = Profile(vm, profile);

        Assert.Equal(profile, vm.SelectedMiniFrameRateProfile!.Value);
        Assert.Equal(slider, vm.MiniFrameRateSlider);
        Assert.Equal(text, vm.MiniFrameRateText);
        Assert.StartsWith(description, vm.MiniFrameRateDescription, StringComparison.Ordinal);
        Assert.Contains(nameof(SettingsViewModel.MiniFrameRateSlider), changed);
        Assert.Contains(nameof(SettingsViewModel.MiniFrameRateDescription), changed);
        Assert.True(vm.IsDirty);
        Assert.Equal(rate, await SaveAsync(vm));
    }

    [Fact]
    public async Task MovingTheSliderOffAProfileSelectsCustomAndOntoOneSelectsIt()
    {
        SettingsViewModel vm = await LoadAsync();

        vm.MiniFrameRateSlider = 7;
        Assert.Equal(MiniPreviewFrameRateProfile.Custom, vm.SelectedMiniFrameRateProfile!.Value);
        Assert.Equal("7\u00A0fps", vm.MiniFrameRateText);
        Assert.StartsWith("Your own limit", vm.MiniFrameRateDescription, StringComparison.Ordinal);
        Assert.True(vm.IsDirty);

        vm.MiniFrameRateSlider = 5;
        Assert.Equal(MiniPreviewFrameRateProfile.Economy, vm.SelectedMiniFrameRateProfile!.Value);

        vm.MiniFrameRateSlider = 10;
        Assert.Equal(MiniPreviewFrameRateProfile.Balanced, vm.SelectedMiniFrameRateProfile!.Value);
        // Back to the saved value.
        Assert.False(vm.IsDirty);

        // From "Smooth" (the slider at its right end) to the right end by hand: a limit of 30 fps.
        vm.SelectedMiniFrameRateProfile = Profile(vm, MiniPreviewFrameRateProfile.Smooth);
        vm.MiniFrameRateSlider = 29;
        vm.MiniFrameRateSlider = 30;
        Assert.Equal(MiniPreviewFrameRateProfile.Custom, vm.SelectedMiniFrameRateProfile!.Value);
        Assert.Equal(30, await SaveAsync(vm));
    }

    [Theory]
    [InlineData(12.4, 12)]
    [InlineData(12.5, 13)]
    [InlineData(0.0, 1)]
    [InlineData(99.0, 30)]
    public async Task TheSliderIsRoundedAndClampedToWholeFps(double value, int expected)
    {
        SettingsViewModel vm = await LoadAsync();

        vm.MiniFrameRateSlider = value;

        Assert.Equal(expected, vm.MiniFrameRateSlider);
        Assert.Equal(expected, await SaveAsync(vm));
    }

    [Fact]
    public async Task ANaNOrTheSameValueChangesNothing()
    {
        SettingsViewModel vm = await LoadAsync();
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.MiniFrameRateSlider = double.NaN;
        vm.MiniFrameRateSlider = 10;
        vm.SelectedMiniFrameRateProfile = null;
        vm.SelectedMiniFrameRateProfile = Profile(vm, MiniPreviewFrameRateProfile.Balanced);

        Assert.DoesNotContain(changed, static name => name is nameof(SettingsViewModel.MiniFrameRateSlider) or nameof(SettingsViewModel.SelectedMiniFrameRateProfile));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task CustomKeepsTheSlidersValue()
    {
        SettingsViewModel vm = await LoadAsync();

        vm.SelectedMiniFrameRateProfile = Profile(vm, MiniPreviewFrameRateProfile.Custom);

        Assert.Equal(MiniPreviewFrameRateProfile.Custom, vm.SelectedMiniFrameRateProfile!.Value);
        Assert.Equal(10, vm.MiniFrameRateSlider);
        Assert.Equal("10\u00A0fps", vm.MiniFrameRateText);
        // The same limit as Balanced: nothing to save (and a reload shows Balanced again).
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task CustomAfterSmoothTakesTheRightEndOfTheSlider()
    {
        SettingsViewModel vm = await LoadAsync(rate: null);

        vm.SelectedMiniFrameRateProfile = Profile(vm, MiniPreviewFrameRateProfile.Custom);

        Assert.Equal("30\u00A0fps", vm.MiniFrameRateText);
        Assert.Equal(30, await SaveAsync(vm));
    }

    [Fact]
    public async Task TheTextsFollowTheLanguage()
    {
        SettingsViewModel vm = await LoadAsync(rate: null);
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        using (new CultureScope("cs-CZ"))
        {
            vm.RefreshTexts();

            Assert.Equal("Plynulý", vm.SelectedMiniFrameRateProfile!.Text);
            Assert.Equal("Bez omezení", vm.MiniFrameRateText);
            Assert.StartsWith("Plná snímková frekvence", vm.MiniFrameRateDescription, StringComparison.Ordinal);
        }

        Assert.Contains(nameof(SettingsViewModel.MiniFrameRateText), changed);
        Assert.Contains(nameof(SettingsViewModel.MiniFrameRateDescription), changed);
    }
}
