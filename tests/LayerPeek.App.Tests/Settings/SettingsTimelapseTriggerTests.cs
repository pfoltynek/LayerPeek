using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Cameras.Recording;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// T-159: what takes the timelapse frames in the settings window (experimental): every N seconds (the default) or every N-th
/// layer change estimated from the Z axis (N 1–100, default 1, user decision 2026-11-04), for all printers. Only the number
/// of the chosen trigger is shown and validated; Save keeps the other one.
/// </summary>
public sealed class SettingsTimelapseTriggerTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly CameraSettings Camera = new(enabled: true, new Uri("rtsp://192.168.0.229/live"));

    private readonly FakeEditorStore _store = new();
    private readonly FakeCameraRecordings _recordings = new();
    private SettingsViewModel? _viewModel;

    public void Dispose() => _viewModel?.Dispose();

    private async Task<SettingsViewModel> LoadAsync(CameraRecordingSettings? recording = null)
    {
        AppSettings settings = new() { Recording = recording ?? CameraRecordingSettings.Default, Printers = [LinkPrinter("a", "Core One") with { Camera = Camera }] };
        _store.Data = new SettingsEditorData(settings, SettingsLoadStatus.Loaded, [], Ids([.. settings.Printers.Select(static p => p.Id)]));
        _viewModel?.Dispose();
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, static () => Guid.NewGuid().ToString("N"))
        {
            Recordings = _recordings,
        };
        await _viewModel.LoadAsync();
        return _viewModel;
    }

    private static TimelapseTriggerOption Option(SettingsViewModel vm, CameraTimelapseTriggerKind kind) =>
        vm.TimelapseTriggerOptions.Single(o => o.Value == kind);

    [Fact]
    public async Task TheDefaultIsAFrameEveryIntervalAndEveryLayerForTheLayerTrigger()
    {
        SettingsViewModel vm = await LoadAsync();

        Assert.Equal([CameraTimelapseTriggerKind.Time, CameraTimelapseTriggerKind.Layer], vm.TimelapseTriggerOptions.Select(static o => o.Value));
        Assert.Equal([Strings.SettingsTimelapseTriggerTime, Strings.SettingsTimelapseTriggerLayer], vm.TimelapseTriggerOptions.Select(static o => o.Text));
        Assert.Equal(CameraTimelapseTriggerKind.Time, vm.SelectedTimelapseTrigger.Value);
        Assert.True(vm.IsTimelapseByTime);
        Assert.False(vm.IsTimelapseByLayer);
        Assert.Equal(1, vm.TimelapseEveryNthLayer);
        Assert.Equal((1m, 100m), (SettingsViewModel.MinTimelapseEveryNthLayer, SettingsViewModel.MaxTimelapseEveryNthLayer));
        Assert.Equal(Strings.SettingsTimelapseTriggerHint, vm.TimelapseTriggerHelpText);
        Assert.Equal(Strings.SettingsTimelapseEveryNthLayerHint.Replace("{0}", "30", StringComparison.Ordinal), vm.TimelapseEveryNthLayerHint);
        Assert.Equal(vm.TimelapseEveryNthLayerHint, vm.TimelapseEveryNthLayerHelpText);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task TheLayerTriggerAndItsStepAreSavedAndTheIntervalIsKept()
    {
        SettingsViewModel vm = await LoadAsync(new CameraRecordingSettings { TimelapseIntervalSeconds = 45 });
        List<string?> changes = [];
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.SelectedTimelapseTrigger = Option(vm, CameraTimelapseTriggerKind.Layer);
        vm.TimelapseEveryNthLayer = 5;

        Assert.True(vm.IsTimelapseByLayer);
        Assert.False(vm.IsTimelapseByTime);
        Assert.Contains(nameof(SettingsViewModel.IsTimelapseByLayer), changes);
        Assert.Contains(nameof(SettingsViewModel.IsTimelapseByTime), changes);
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        CameraRecordingSettings saved = Assert.Single(_store.Saves).Updated.Recording;
        Assert.Equal(new CameraRecordingSettings { TimelapseIntervalSeconds = 45, TimelapseTrigger = CameraTimelapseTriggerKind.Layer, TimelapseEveryNthLayer = 5 }, saved);
    }

    [Fact]
    public async Task SavedValuesAreLoaded()
    {
        SettingsViewModel vm = await LoadAsync(new CameraRecordingSettings { TimelapseTrigger = CameraTimelapseTriggerKind.Layer, TimelapseEveryNthLayer = 3 });

        Assert.Equal(CameraTimelapseTriggerKind.Layer, vm.SelectedTimelapseTrigger.Value);
        Assert.Same(Option(vm, CameraTimelapseTriggerKind.Layer), vm.SelectedTimelapseTrigger);
        Assert.Equal(3, vm.TimelapseEveryNthLayer);
        Assert.False(vm.IsDirty);

        // Back to the time trigger: the step stays saved for the next switch.
        vm.SelectedTimelapseTrigger = Option(vm, CameraTimelapseTriggerKind.Time);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(new CameraRecordingSettings { TimelapseEveryNthLayer = 3 }, Assert.Single(_store.Saves).Updated.Recording);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(2.5)]
    public async Task AnInvalidStepBlocksSaveSelectsItsTabAndFocusesIt(double layers)
    {
        SettingsViewModel vm = await LoadAsync(new CameraRecordingSettings { TimelapseTrigger = CameraTimelapseTriggerKind.Layer });
        List<string> focused = [];
        vm.FieldFocusRequested += (_, field) => focused.Add(field);

        vm.TimelapseEveryNthLayer = (decimal)layers;
        Assert.True(vm.HasTimelapseEveryNthLayerError);
        Assert.Equal(Strings.SettingsErrorRecordingLimit.Replace("{0}", "1", StringComparison.Ordinal).Replace("{1}", "100", StringComparison.Ordinal), vm.TimelapseEveryNthLayerError);
        Assert.Equal(vm.TimelapseEveryNthLayerError, vm.TimelapseEveryNthLayerHelpText);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(SettingsViewModel.RecordingTab, vm.SelectedTabIndex);
        Assert.Equal([SettingsFieldIds.SettingsTimelapseEveryNthLayer], focused);

        vm.TimelapseEveryNthLayer = null;
        Assert.True(vm.HasTimelapseEveryNthLayerError);
        vm.TimelapseEveryNthLayer = 100;
        Assert.False(vm.HasTimelapseEveryNthLayerError);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(100, Assert.Single(_store.Saves).Updated.Recording.TimelapseEveryNthLayer);
    }

    [Fact]
    public async Task OnlyTheNumberOfTheChosenTriggerIsValidatedAndAHiddenInvalidOneKeepsItsLoadedValue()
    {
        SettingsViewModel vm = await LoadAsync(new CameraRecordingSettings { TimelapseIntervalSeconds = 60, TimelapseEveryNthLayer = 4 });

        vm.TimelapseIntervalSeconds = 2;
        Assert.True(vm.HasTimelapseIntervalError);
        // The layer trigger hides the interval: its error goes with it, and Save keeps the loaded interval.
        vm.SelectedTimelapseTrigger = Option(vm, CameraTimelapseTriggerKind.Layer);
        Assert.False(vm.HasTimelapseIntervalError);
        vm.TimelapseEveryNthLayer = 0;
        Assert.True(vm.HasTimelapseEveryNthLayerError);
        vm.SelectedTimelapseTrigger = Option(vm, CameraTimelapseTriggerKind.Time);
        Assert.False(vm.HasTimelapseEveryNthLayerError);
        Assert.True(vm.HasTimelapseIntervalError);
        vm.SelectedTimelapseTrigger = Option(vm, CameraTimelapseTriggerKind.Layer);
        vm.TimelapseEveryNthLayer = 2;
        // A combo box may write null while its items change.
        vm.SelectedTimelapseTrigger = null!;
        Assert.True(vm.IsTimelapseByLayer);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(
            new CameraRecordingSettings { TimelapseIntervalSeconds = 60, TimelapseTrigger = CameraTimelapseTriggerKind.Layer, TimelapseEveryNthLayer = 2 },
            Assert.Single(_store.Saves).Updated.Recording);
    }

    [Fact]
    public async Task WithoutTheTimelapseRecorderTheTriggerTellsWhy()
    {
        _recordings.IsTimelapseAvailable = false;
        SettingsViewModel vm = await LoadAsync();

        Assert.Equal(Strings.TimelapseUnavailable, vm.TimelapseTriggerHelpText);
        Assert.Equal(Strings.TimelapseUnavailable, vm.TimelapseEveryNthLayerHelpText);
        Assert.True(vm.ShowsTimelapseUnavailable);
    }

    [Fact]
    public async Task TheOptionsAndHintsAreFormattedAgainWhenTheLanguageChanges()
    {
        SettingsViewModel vm = await LoadAsync();
        TimelapseTriggerOption layer = Option(vm, CameraTimelapseTriggerKind.Layer);
        List<string?> optionChanges = [];
        List<string?> changes = [];
        layer.PropertyChanged += (_, e) => optionChanges.Add(e.PropertyName);
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.RefreshTexts();

        Assert.Contains(nameof(TimelapseTriggerOption.Text), optionChanges);
        Assert.Contains(nameof(SettingsViewModel.TimelapseTriggerHelpText), changes);
        Assert.Contains(nameof(SettingsViewModel.TimelapseEveryNthLayerHint), changes);
        Assert.Contains(nameof(SettingsViewModel.TimelapseEveryNthLayerHelpText), changes);
    }
}
