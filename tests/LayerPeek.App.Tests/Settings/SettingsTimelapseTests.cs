using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Cameras.Recording;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// T-160: the timelapse settings have no controls yet (T-161), so saving the settings window keeps them as they were
/// saved: the interval of the Recording tab and "Timelapse of every print" of each printer.
/// </summary>
public sealed class SettingsTimelapseTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly CameraSettings Camera = new(enabled: true, new Uri("rtsp://192.168.0.227/live")) { TimelapsePrints = true };

    private readonly FakeEditorStore _store = new();
    private readonly FakeCameraRecordings _recordings = new();
    private SettingsViewModel? _viewModel;

    public void Dispose() => _viewModel?.Dispose();

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavingKeepsTheIntervalAndTheTimelapseOfEveryPrint(bool cameraEnabled)
    {
        CameraSettings camera = cameraEnabled ? Camera : CameraSettings.Disabled with { TimelapsePrints = true };
        AppSettings settings = new()
        {
            Recording = new CameraRecordingSettings { TimelapseIntervalSeconds = 120 },
            Printers = [LinkPrinter("a", "Alpha") with { Camera = camera }],
        };
        SettingsViewModel vm = await LoadAsync(settings);
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
}
