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
/// The timelapse settings of the settings window (T-160, T-161; experimental, user decisions 2026-11-04): the time between
/// frames (whole seconds, 5–600, default 30, global) on the Recording tab and "Take a timelapse of prints" per printer on its
/// Camera sub-tab (off by default); disabled with the reason when this build takes no timelapses. Saved by Save; invalid
/// values block it like the other fields (T-112).
/// </summary>
public sealed class SettingsTimelapseTests : IDisposable
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
    public async Task LoadShowsTheDefaultsAndTheHintWithTheFrameRate()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });

        Assert.Equal(30, vm.TimelapseIntervalSeconds);
        Assert.False(vm.HasTimelapseIntervalError);
        Assert.False(vm.ShowsTimelapseUnavailable);
        Assert.True(vm.IsTimelapseAvailable);
        Assert.Equal(Strings.SettingsTimelapseIntervalHint.Replace("{0}", "30", StringComparison.Ordinal), vm.TimelapseIntervalHint);
        Assert.Equal(vm.TimelapseIntervalHint, vm.TimelapseIntervalHelpText);
        Assert.Equal((5m, 600m), (SettingsViewModel.MinTimelapseIntervalSeconds, SettingsViewModel.MaxTimelapseIntervalSeconds));
        PrinterEditorViewModel printer = vm.Printers[0];
        Assert.False(printer.TimelapsePrints);
        Assert.True(printer.CanEditTimelapsePrints);
        Assert.Equal(Strings.SettingsTimelapsePrintsHint, printer.TimelapsePrintsHint);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task TheIntervalAndThePrintersTimelapseAreSaved()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });

        vm.TimelapseIntervalSeconds = 600;
        vm.Printers[0].TimelapsePrints = true;
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        AppSettings saved = Assert.Single(_store.Saves).Updated;
        Assert.Equal(new CameraRecordingSettings { TimelapseIntervalSeconds = 600 }, saved.Recording);
        Assert.True(saved.Printers[0].Camera.TimelapsePrints);
        Assert.False(saved.Printers[0].Camera.RecordPrints);
        Assert.False(saved.Printers[0].Camera.Dashcam);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavedValuesAreLoadedAndKept(bool cameraEnabled)
    {
        CameraSettings camera = cameraEnabled ? Camera with { TimelapsePrints = true } : CameraSettings.Disabled with { TimelapsePrints = true };
        AppSettings settings = new()
        {
            Recording = new CameraRecordingSettings { TimelapseIntervalSeconds = 120 },
            Printers = [Printer(camera)],
        };
        SettingsViewModel vm = await LoadAsync(settings);
        Assert.Equal(120, vm.TimelapseIntervalSeconds);
        Assert.True(vm.Printers[0].TimelapsePrints);
        Assert.Equal(cameraEnabled, vm.Printers[0].CanEditTimelapsePrints);
        Assert.False(vm.IsDirty);

        vm.DashcamMinutes = 3;
        vm.Printers[0].Dashcam = true;
        await vm.SaveCommand.ExecuteAsync(null);

        AppSettings saved = Assert.Single(_store.Saves).Updated;
        Assert.Equal(120, saved.Recording.TimelapseIntervalSeconds);
        Assert.Equal(3, saved.Recording.DashcamMinutes);
        Assert.True(saved.Printers[0].Camera.TimelapsePrints);
        Assert.True(saved.Printers[0].Camera.Dashcam);
    }

    [Fact]
    public async Task ChangingTheIntervalKeepsEveryOtherRecordingDashcamAndDetectionValue()
    {
        // Review of T-161: the compacted Recording tab and the new controls must not lose any value of T-146, T-148, T-154.
        CameraRecordingSettings recording = new()
        {
            Folder = Path.Combine(Path.GetTempPath(), "LayerPeekReviewRecordings"),
            SizeLimitGigabytes = 7,
            DashcamMinutes = 9,
            DashcamOnError = false,
            DashcamOnAttention = false,
            DashcamOnStopped = false,
            DashcamOnFailureDetection = false,
            TimelapseIntervalSeconds = 15,
        };
        LayerPeek.Core.Detection.PrintFailureDetectionSettings detection = new()
        {
            Enabled = true,
            SaveSnapshots = false,
            AlarmRepeat = LayerPeek.Core.Detection.PrintFailureAlarmRepeat.WhilePersisting,
            SampleInterval = TimeSpan.FromSeconds(4),
        };
        CameraSettings camera = Camera with { RecordPrints = true, Dashcam = true, TimelapsePrints = true };
        LayerPeek.Core.Detection.PrinterDetectionSettings printerDetection = new()
        {
            Enabled = true,
            Region = new LayerPeek.Core.Detection.DetectionRegion(0.1, 0.2, 0.5, 0.4),
        };
        AppSettings settings = new()
        {
            Recording = recording,
            Detection = detection,
            Printers = [Printer(camera) with { Detection = printerDetection }],
        };
        SettingsViewModel vm = await LoadAsync(settings);
        Assert.False(vm.IsDirty);

        vm.TimelapseIntervalSeconds = 45;
        await vm.SaveCommand.ExecuteAsync(null);

        AppSettings saved = Assert.Single(_store.Saves).Updated;
        Assert.Equal(recording with { TimelapseIntervalSeconds = 45 }, saved.Recording);
        Assert.Equal(detection, saved.Detection);
        Assert.Equal(camera, saved.Printers[0].Camera);
        Assert.Equal(printerDetection, saved.Printers[0].Detection);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(601)]
    [InlineData(0)]
    [InlineData(7.5)]
    public async Task AnInvalidIntervalBlocksSaveSelectsItsTabAndFocusesIt(double seconds)
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });
        List<string> focused = [];
        vm.FieldFocusRequested += (_, field) => focused.Add(field);

        vm.TimelapseIntervalSeconds = (decimal)seconds;
        Assert.True(vm.HasTimelapseIntervalError);
        Assert.Equal(Strings.SettingsErrorRecordingLimit.Replace("{0}", "5", StringComparison.Ordinal).Replace("{1}", "600", StringComparison.Ordinal), vm.TimelapseIntervalError);
        Assert.Equal(vm.TimelapseIntervalError, vm.TimelapseIntervalHelpText);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(SettingsViewModel.RecordingTab, vm.SelectedTabIndex);
        Assert.Equal([SettingsFieldIds.SettingsTimelapseInterval], focused);

        vm.TimelapseIntervalSeconds = null;
        Assert.True(vm.HasTimelapseIntervalError);
        vm.TimelapseIntervalSeconds = 5;
        Assert.False(vm.HasTimelapseIntervalError);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(5, Assert.Single(_store.Saves).Updated.Recording.TimelapseIntervalSeconds);
    }

    [Fact]
    public async Task ThePrintersTimelapseNeedsTheCameraAndIsKeptWhileItIsOff()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });
        PrinterEditorViewModel printer = vm.Printers[0];
        List<string?> changes = [];
        printer.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        printer.TimelapsePrints = true;

        printer.CameraEnabled = false;

        Assert.False(printer.CanEditTimelapsePrints);
        Assert.Contains(nameof(PrinterEditorViewModel.CanEditTimelapsePrints), changes);
        await vm.SaveCommand.ExecuteAsync(null);
        CameraSettings saved = Assert.Single(_store.Saves).Updated.Printers[0].Camera;
        Assert.True(saved.TimelapsePrints);
        Assert.False(saved.Enabled);
    }

    [Fact]
    public async Task WithoutTheTimelapseRecorderTheOptionsAreDisabledWithTheReason()
    {
        _recordings.IsTimelapseAvailable = false;
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera with { TimelapsePrints = true })] });

        Assert.False(vm.IsTimelapseAvailable);
        Assert.True(vm.ShowsTimelapseUnavailable);
        Assert.Equal(Strings.TimelapseUnavailable, vm.TimelapseIntervalHelpText);
        Assert.False(vm.Printers[0].CanEditTimelapsePrints);
        Assert.Equal(Strings.TimelapseUnavailable, vm.Printers[0].TimelapsePrintsHint);
        // The value stays as it was saved.
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(_store.Saves);
        vm.RecordingLimitGigabytes = 9;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(Assert.Single(_store.Saves).Updated.Printers[0].Camera.TimelapsePrints);
    }
}
