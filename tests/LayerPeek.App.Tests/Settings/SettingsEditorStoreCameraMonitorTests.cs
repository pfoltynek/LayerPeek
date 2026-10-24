using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// A settings window opened before the camera window saved a monitor's new device path (user decision 2026-10-23) saves
/// that path, like the latest detected model, unless the user chose another monitor in the window.
/// </summary>
public sealed class SettingsEditorStoreCameraMonitorTests : IAsyncDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly CameraMonitorId Loaded = new("DELL U2719D", 2560, 1440, "old-path", ["twin-a"]);

    private readonly List<string> _log = [];
    private readonly MemorySettingsStore _settings;
    private readonly RecordingApplier _applier;
    private readonly FakeWebGateway _gateway = new();
    private readonly RecordingMonitorPathUpdater _monitorPaths = new();

    public SettingsEditorStoreCameraMonitorTests()
    {
        _settings = new MemorySettingsStore(_log);
        _applier = new RecordingApplier(_log);
    }

    public ValueTask DisposeAsync() => _gateway.DisposeAsync();

    private SettingsEditorStore Create(bool withMonitorPaths = true) =>
        new(() => _settings, new FakeSecretStore(_log), Supported, _applier, _gateway, new ListLogger<SettingsEditorStore>(), monitorPaths: withMonitorPaths ? _monitorPaths : null);

    private static PrinterDefinition Printer(CameraMonitorId? monitor, string name = "A") =>
        LinkPrinter("a", name) with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) { Window = CameraWindowDefaults.Default with { Monitor = monitor } } };

    private static AppSettings With(params PrinterDefinition[] printers) => AppSettings.Default with { Printers = printers };

    private static Dictionary<string, string> NoPasswords() => [];

    private CameraMonitorId? SavedMonitor() => Assert.Single(_settings.Saved!.Printers).Camera.Window.Monitor;

    [Fact]
    public async Task SaveTakesThePathSavedWhileTheWindowWasOpen()
    {
        _monitorPaths.SavedPaths[("A", Loaded)] = "new-path";

        await Create().SaveAsync(new SettingsChange(With(Printer(Loaded)), With(Printer(Loaded, name: "Renamed")), NoPasswords()));

        Assert.Equal(Loaded.WithDevicePath("new-path"), SavedMonitor());
        Assert.Equal("Renamed", _settings.Saved!.Printers[0].Name);
        Assert.Equal(Loaded.WithDevicePath("new-path"), Assert.Single(Assert.Single(_applier.Applied).Printers).Camera.Window.Monitor);
    }

    [Fact]
    public async Task RefreshedTwinsOfTheSameMonitorStayWithTheNewPath()
    {
        // The window refreshed the twins (same monitor); "new-path" itself is never a twin of the monitor.
        _monitorPaths.SavedPaths[("a", Loaded)] = "new-path";
        CameraMonitorId refreshed = new("DELL U2719D", 2560, 1440, "old-path", ["twin-a", "new-path", "twin-b"]);

        await Create().SaveAsync(new SettingsChange(With(Printer(Loaded)), With(Printer(refreshed, name: "Renamed")), NoPasswords()));

        Assert.Equal(new CameraMonitorId("DELL U2719D", 2560, 1440, "new-path", ["twin-a", "twin-b"]), SavedMonitor());
    }

    [Fact]
    public async Task MonitorChosenInTheWindowWins()
    {
        _monitorPaths.SavedPaths[("a", Loaded)] = "new-path";
        CameraMonitorId chosen = new("LG HDR 4K", 1920, 1080);

        await Create().SaveAsync(new SettingsChange(With(Printer(Loaded)), With(Printer(chosen)), NoPasswords()));

        Assert.Equal(chosen, SavedMonitor());
    }

    [Fact]
    public async Task WithoutASavedPathOrAnUpdaterNothingChanges()
    {
        await Create().SaveAsync(new SettingsChange(With(Printer(Loaded)), With(Printer(Loaded, name: "Renamed")), NoPasswords()));
        Assert.Equal(Loaded, SavedMonitor());

        _monitorPaths.SavedPaths[("a", Loaded)] = "new-path";
        await Create(withMonitorPaths: false).SaveAsync(new SettingsChange(With(Printer(Loaded)), With(Printer(Loaded, name: "Renamed")), NoPasswords()));
        Assert.Equal(Loaded, SavedMonitor());
    }

    [Fact]
    public async Task NewPrinterAndPrinterWithoutMonitorAreLeftAsTheyAre()
    {
        _monitorPaths.SavedPaths[("a", Loaded)] = "new-path";

        await Create().SaveAsync(new SettingsChange(AppSettings.Default, With(Printer(Loaded)), NoPasswords()));
        Assert.Equal(Loaded, SavedMonitor());

        await Create().SaveAsync(new SettingsChange(With(Printer(null)), With(Printer(null, name: "Renamed")), NoPasswords()));
        Assert.Null(SavedMonitor());
    }
}
