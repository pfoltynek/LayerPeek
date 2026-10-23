using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.Core.Tests.Settings;

/// <summary>
/// <see cref="JsonSettingsStore.SetCameraMonitorAsync"/> (user decision 2026-10-23): the saved camera monitor of one
/// printer gets the device path it was found on, through the normal save, only while the file still has the expected
/// monitor and loads without warnings.
/// </summary>
public sealed class JsonSettingsStoreCameraMonitorTests : IDisposable
{
    private static readonly CameraMonitorId Saved = new("DELL U2719D", 2560, 1440, "path-b", ["path-a"]);
    private static readonly CameraMonitorId Moved = Saved.WithDevicePath("path-moved");

    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly JsonSettingsStore _store;

    public JsonSettingsStoreCameraMonitorTests()
    {
        _store = new JsonSettingsStore(_directory);
    }

    private string FilePath => Path.Combine(_directory, JsonSettingsStore.FileName);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _store.Dispose();
        TestDirectories.Delete(_directory);
    }

    private static PrinterDefinition Printer(string id, CameraMonitorId? monitor) => new()
    {
        Id = id,
        Name = id.ToUpperInvariant(),
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.226",
        Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) { Window = CameraWindowDefaults.Default with { Monitor = monitor } },
    };

    private static AppSettings Settings(CameraMonitorId? monitor) => new() { Printers = [Printer("mk4", monitor), Printer("xl", Saved)] };

    [Fact]
    public async Task MonitorOfThePrinterGetsTheNewPath()
    {
        await _store.SaveAsync(Settings(Saved), Ct);

        bool written = await _store.SetCameraMonitorAsync("MK4", Saved, Moved, Ct);

        SettingsLoadResult loaded = await _store.LoadAsync(Ct);
        Assert.True(written);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(Settings(Saved) with { Printers = [Printer("mk4", Moved), Printer("xl", Saved)] }, loaded.Settings);
    }

    [Fact]
    public async Task MonitorChangedMeanwhileIsLeftAlone()
    {
        await _store.SaveAsync(Settings(Saved.WithDevicePath("path-c")), Ct);
        byte[] before = await File.ReadAllBytesAsync(FilePath, Ct);

        Assert.False(await _store.SetCameraMonitorAsync("mk4", Saved, Moved, Ct));
        Assert.Equal(before, await File.ReadAllBytesAsync(FilePath, Ct));
    }

    [Fact]
    public async Task KnownTwinsArePartOfTheExpectedMonitor()
    {
        // The user chose the monitor again meanwhile (other twins): that choice wins.
        await _store.SaveAsync(Settings(new CameraMonitorId("DELL U2719D", 2560, 1440, "path-b")), Ct);

        Assert.False(await _store.SetCameraMonitorAsync("mk4", Saved, Moved, Ct));
    }

    [Fact]
    public async Task UnknownPrinterNoFileOrNoChangeWriteNothing()
    {
        Assert.False(await _store.SetCameraMonitorAsync("mk4", Saved, Moved, Ct));
        Assert.False(File.Exists(FilePath));

        await _store.SaveAsync(Settings(Saved), Ct);
        byte[] before = await File.ReadAllBytesAsync(FilePath, Ct);

        Assert.False(await _store.SetCameraMonitorAsync("mini", Saved, Moved, Ct));
        Assert.False(await _store.SetCameraMonitorAsync("mk4", Saved, Saved, Ct));
        Assert.Equal(before, await File.ReadAllBytesAsync(FilePath, Ct));
    }

    [Theory]
    [InlineData("""{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "camera": { "enabled": true, "streamUrl": "rtsp://192.168.0.227/live", "window": { "startMode": "Sideways", "monitor": { "displayName": "DELL U2719D", "width": 2560, "height": 1440, "devicePath": "path-b", "knownTwinPaths": ["path-a"] } } } } ] }""")]
    [InlineData("""{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "camera": { "enabled": true, "streamUrl": "rtsp://192.168.0.227/live", "window": { "monitor": { "displayName": "DELL U2719D", "width": 2560, "height": 1440, "devicePath": "path-b", "knownTwinPaths": ["path-a"] } } } }, { "id": "bad" } ] }""")]
    [InlineData("""{ "printers": [ """)]
    public async Task FileWithWarningsOrErrorsIsLeftAlone(string json)
    {
        // A save would drop what the loader skipped or corrected; that is the user's call in the settings window.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, json, Ct);

        Assert.False(await _store.SetCameraMonitorAsync("mk4", Saved, Moved, Ct));
        Assert.Equal(json, await File.ReadAllTextAsync(FilePath, Ct));
    }

    [Fact]
    public void WithCameraMonitorChangesOnlyTheExpectedMonitorOfThePrinter()
    {
        AppSettings settings = Settings(Saved);

        AppSettings changed = settings.WithCameraMonitor("MK4", Saved, Moved);

        Assert.Equal(Settings(Saved) with { Printers = [Printer("mk4", Moved), Printer("xl", Saved)] }, changed);
        Assert.Same(settings, settings.WithCameraMonitor("mini", Saved, Moved));
        Assert.Same(settings, settings.WithCameraMonitor("mk4", Moved, Saved));
        Assert.Same(settings, settings.WithCameraMonitor("mk4", Saved, Saved));
        Assert.Same(changed, changed.WithCameraMonitor("mk4", Saved, Moved));
    }

    [Fact]
    public async Task CancellationIsHonoured()
    {
        await _store.SaveAsync(Settings(Saved), Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.SetCameraMonitorAsync("mk4", Saved, Moved, new CancellationToken(canceled: true)));
    }
}
