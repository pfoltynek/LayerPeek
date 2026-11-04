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
/// The dashcam settings of the settings window (T-148, user decisions 2026-11-04): its length (whole minutes, 1–10) and
/// triggers on the Recording tab, "Keep the last minutes" per printer on its Camera sub-tab; disabled with the reason when
/// this build cannot record. Saved by Save.
/// </summary>
public sealed class SettingsDashcamTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly CameraSettings Camera = new(enabled: true, new Uri("rtsp://192.168.0.227/live"));

    private readonly FakeEditorStore _store = new();
    private readonly FakeCameraRecordings _recordings = new();
    private SettingsViewModel? _viewModel;

    public void Dispose() => _viewModel?.Dispose();

    private static PrinterDefinition Printer(CameraSettings camera) => LinkPrinter("a", "Alpha") with { Camera = camera };

    private async Task<SettingsViewModel> LoadAsync(AppSettings settings)
    {
        _store.Data = new SettingsEditorData(settings, SettingsLoadStatus.Loaded, [], Ids([.. settings.Printers.Select(static p => p.Id)]));
        _viewModel?.Dispose();
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, static () => Guid.NewGuid().ToString("N"))
        {
            Recordings = _recordings,
        };
        await _viewModel.LoadAsync();
        return _viewModel;
    }

    [Fact]
    public async Task LoadShowsTheDefaultsAndTheHintWithTheTimeAfterTheEvent()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });

        Assert.Equal(2, vm.DashcamMinutes);
        Assert.True(vm.DashcamOnError && vm.DashcamOnAttention && vm.DashcamOnStopped && vm.DashcamOnFailureDetection);
        Assert.True(vm.ShowsDashcamMinutesHint);
        Assert.Equal(Strings.SettingsDashcamMinutesHint.Replace("{0}", "30", StringComparison.Ordinal), vm.DashcamMinutesHint);
        Assert.Equal(vm.DashcamMinutesHint, vm.DashcamMinutesHelpText);
        Assert.False(vm.Printers[0].Dashcam);
        Assert.True(vm.Printers[0].CanEditDashcam);
        Assert.Equal(Strings.SettingsDashcamPrinterHint, vm.Printers[0].DashcamHint);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task TheLengthTheTriggersAndThePrintersDashcamAreSaved()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });

        vm.DashcamMinutes = 7;
        vm.DashcamOnAttention = false;
        vm.DashcamOnFailureDetection = false;
        vm.Printers[0].Dashcam = true;
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        AppSettings saved = Assert.Single(_store.Saves).Updated;
        Assert.Equal(new CameraRecordingSettings { DashcamMinutes = 7, DashcamOnAttention = false, DashcamOnFailureDetection = false }, saved.Recording);
        Assert.True(saved.Printers[0].Camera.Dashcam);
        Assert.False(saved.Printers[0].Camera.RecordPrints);
    }

    [Fact]
    public async Task SavedValuesAreLoaded()
    {
        CameraRecordingSettings recording = new() { DashcamMinutes = 10, DashcamOnError = false, DashcamOnStopped = false };
        SettingsViewModel vm = await LoadAsync(new AppSettings { Recording = recording, Printers = [Printer(Camera with { Dashcam = true })] });

        Assert.Equal(10, vm.DashcamMinutes);
        Assert.False(vm.DashcamOnError);
        Assert.True(vm.DashcamOnAttention);
        Assert.False(vm.DashcamOnStopped);
        Assert.True(vm.Printers[0].Dashcam);

        // Another change keeps them.
        vm.RecordingLimitGigabytes = 9;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(recording with { SizeLimitGigabytes = 9 }, Assert.Single(_store.Saves).Updated.Recording);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(2.5)]
    public async Task AnInvalidLengthBlocksSaveAndSelectsItsTab(double minutes)
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });
        List<string> focused = [];
        vm.FieldFocusRequested += (_, field) => focused.Add(field);

        vm.DashcamMinutes = (decimal)minutes;
        Assert.True(vm.HasDashcamMinutesError);
        Assert.False(vm.ShowsDashcamMinutesHint);
        Assert.Equal(Strings.SettingsErrorRecordingLimit.Replace("{0}", "1", StringComparison.Ordinal).Replace("{1}", "10", StringComparison.Ordinal), vm.DashcamMinutesError);
        Assert.Equal(vm.DashcamMinutesError, vm.DashcamMinutesHelpText);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(SettingsViewModel.RecordingTab, vm.SelectedTabIndex);
        Assert.Equal([SettingsFieldIds.SettingsDashcamMinutes], focused);

        vm.DashcamMinutes = null;
        Assert.True(vm.HasDashcamMinutesError);
        vm.DashcamMinutes = 3;
        Assert.False(vm.HasDashcamMinutesError);
    }

    [Fact]
    public async Task ThePrintersDashcamNeedsTheCameraAndIsKeptWhileItIsOff()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });
        PrinterEditorViewModel printer = vm.Printers[0];
        List<string?> changes = [];
        printer.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        printer.Dashcam = true;

        printer.CameraEnabled = false;

        Assert.False(printer.CanEditDashcam);
        Assert.Contains(nameof(PrinterEditorViewModel.CanEditDashcam), changes);
        await vm.SaveCommand.ExecuteAsync(null);
        CameraSettings saved = Assert.Single(_store.Saves).Updated.Printers[0].Camera;
        Assert.True(saved.Dashcam);
        Assert.False(saved.Enabled);
    }

    [Fact]
    public async Task WithoutTheRecorderThePrintersDashcamIsDisabledWithTheReason()
    {
        _recordings.IsAvailable = false;
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera with { Dashcam = true })] });

        Assert.False(vm.Printers[0].CanEditDashcam);
        Assert.Equal(Strings.RecordingUnavailable, vm.Printers[0].DashcamHint);
        Assert.True(vm.Printers[0].Dashcam);
    }
}
