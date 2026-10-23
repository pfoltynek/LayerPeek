using System.Text;

using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.Core.Tests.Settings;

/// <summary>
/// <see cref="JsonSettingsStore.SetCameraMonitorDevicePathAsync"/> (user decisions 2026-10-23): the saved camera monitor of
/// one printer gets the device path it was found at, byte for byte like the detected model (only the <c>devicePath</c>
/// of its monitor changes; comments, formatting and unknown values stay), and only while the file loads without warnings
/// and still has the expected monitor.
/// </summary>
public sealed class JsonSettingsStoreCameraMonitorTests : IDisposable
{
    private const string OldPath = @"\\?\DISPLAY#DEL415A#5&224edec0&0&UID160002#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string NewPath = @"\\?\DISPLAY#DEL415A#5&224edec0&0&UID160004#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    /// <summary><see cref="OldPath"/> and <see cref="NewPath"/> as the settings serializer writes them.</summary>
    private const string OldPathJson = @"\\\\?\\DISPLAY#DEL415A#5\u0026224edec0\u00260\u0026UID160002#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string NewPathJson = @"\\\\?\\DISPLAY#DEL415A#5\u0026224edec0\u00260\u0026UID160004#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private const string Machine = "\"name\": \"MK4\", \"connectionType\": \"PrusaLink\", \"address\": \"192.168.0.226\"";
    private const string Camera = "\"enabled\": true, \"streamUrl\": \"rtsp://192.168.0.227/live\"";

    private static readonly CameraMonitorId Saved = new("DELL U2719D", 2560, 1440, OldPath, ["path-a"]);

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

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    private async Task<(bool Written, string Result)> SetAsync(string original, string printerId, CameraMonitorId expected, string devicePath)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, original, Ct);
        bool written = await _store.SetCameraMonitorDevicePathAsync(printerId, expected, devicePath, Ct);
        return (written, await File.ReadAllTextAsync(FilePath, Ct));
    }

    [Fact]
    public async Task OnlyTheDevicePathOfTheMonitorChanges()
    {
        string original = Lines(
            "{",
            "  // my printers",
            "  \"version\": 1,",
            "  \"future\": { \"x\": [1, 2] },",
            "  \"printers\": [",
            "    {",
            $"      \"id\": \"mk4\", {Machine},",
            $"      \"camera\": {{ {Camera}, \"window\": {{ \"monitor\": {{ \"displayName\": \"DELL U2719D\", \"width\": 2560, \"height\": 1440,",
            $"        \"devicePath\":   \"{OldPathJson}\" /* keep */, \"knownTwinPaths\": [\"path-a\"], \"extra\": true }} }} }}",
            "    },",
            $"    {{ \"id\": \"xl\", \"name\": \"XL\", \"connectionType\": \"PrusaLink\", \"address\": \"192.168.0.230\", \"camera\": {{ {Camera}, \"window\": {{ \"monitor\": {{ \"displayName\": \"DELL U2719D\", \"width\": 2560, \"height\": 1440, \"devicePath\": \"{OldPathJson}\", \"knownTwinPaths\": [\"path-a\"] }} }} }} }}",
            "  ]",
            "}");

        (bool written, string result) = await SetAsync(original, "MK4", Saved, NewPath);

        Assert.True(written);
        Assert.Equal(original.Replace($"\"devicePath\":   \"{OldPathJson}\"", $"\"devicePath\":   \"{NewPathJson}\"", StringComparison.Ordinal), result);
        SettingsLoadResult loaded = await _store.LoadAsync(Ct);
        Assert.Equal(Saved.WithDevicePath(NewPath), loaded.Settings.Printers[0].Camera.Window.Monitor);
        Assert.Equal(Saved, loaded.Settings.Printers[1].Camera.Window.Monitor);
    }

    [Fact]
    public async Task OlderMonitorWithoutAPathGetsOneIndentedLikeItsFirstProperty()
    {
        string original = Lines(
            "{ \"printers\": [ {",
            $"  \"id\": \"mk4\", {Machine}, \"camera\": {{ {Camera}, \"window\": {{",
            "    \"monitor\": {",
            "      \"displayName\": \"DELL U2719D\",",
            "      \"width\": 2560,",
            "      \"height\": 1440",
            "    }",
            "  } } } ] }");

        (bool written, string result) = await SetAsync(original, "mk4", new CameraMonitorId("DELL U2719D", 2560, 1440), NewPath);

        Assert.True(written);
        Assert.Equal(original.Replace("\"height\": 1440\n", $"\"height\": 1440,\n      \"devicePath\": \"{NewPathJson}\"\n", StringComparison.Ordinal), result);
        Assert.Equal(new CameraMonitorId("DELL U2719D", 2560, 1440, NewPath), (await _store.LoadAsync(Ct)).Settings.Printers[0].Camera.Window.Monitor);
    }

    [Fact]
    public async Task DuplicateDevicePathsAllChangeWhateverTheirCase()
    {
        string original = $$"""{ "printers": [ { "id": "mk4", {{Machine}}, "camera": { {{Camera}}, "window": { "monitor": { "displayName": "DELL U2719D", "width": 2560, "height": 1440, "DEVICEPATH": "x", "devicePath": "{{OldPathJson}}" } } } } ] }""";

        (bool written, string result) = await SetAsync(original, "mk4", new CameraMonitorId("DELL U2719D", 2560, 1440, OldPath), NewPath);

        Assert.True(written);
        Assert.Equal(original.Replace("\"x\"", $"\"{NewPathJson}\"", StringComparison.Ordinal).Replace(OldPathJson, NewPathJson, StringComparison.Ordinal), result);
    }

    [Fact]
    public async Task ByteOrderMarkStays()
    {
        string json = $$"""{ "printers": [ { "id": "mk4", {{Machine}}, "camera": { {{Camera}}, "window": { "monitor": { "displayName": "DELL U2719D", "width": 2560, "height": 1440, "devicePath": "{{OldPathJson}}" } } } } ] }""";
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(FilePath, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(json)], Ct);

        Assert.True(await _store.SetCameraMonitorDevicePathAsync("mk4", new CameraMonitorId("DELL U2719D", 2560, 1440, OldPath), NewPath, Ct));

        byte[] result = await File.ReadAllBytesAsync(FilePath, Ct);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(json.Replace(OldPathJson, NewPathJson, StringComparison.Ordinal))], result);
    }

    [Theory]
    [InlineData("mk4", "other-path")]
    [InlineData("xl", null)]
    public async Task OtherMonitorOrPrinterIsLeftAlone(string printerId, string? storedPath)
    {
        // The monitor changed meanwhile (the user chose another one), or the printer has no such monitor.
        string original = $$"""{ "printers": [ { "id": "mk4", {{Machine}}, "camera": { {{Camera}}, "window": { "monitor": { "displayName": "DELL U2719D", "width": 2560, "height": 1440, "devicePath": "{{storedPath ?? OldPathJson}}", "knownTwinPaths": ["path-a"] } } } } ] }""";

        (bool written, string result) = await SetAsync(original, printerId, Saved, NewPath);

        Assert.False(written);
        Assert.Equal(original, result);
    }

    [Fact]
    public async Task KnownTwinsArePartOfTheExpectedMonitor()
    {
        string original = $$"""{ "printers": [ { "id": "mk4", {{Machine}}, "camera": { {{Camera}}, "window": { "monitor": { "displayName": "DELL U2719D", "width": 2560, "height": 1440, "devicePath": "{{OldPathJson}}" } } } } ] }""";

        (bool written, string result) = await SetAsync(original, "mk4", Saved, NewPath);

        Assert.False(written);
        Assert.Equal(original, result);
    }

    [Fact]
    public async Task SamePathOrNoFileWritesNothing()
    {
        Assert.False(await _store.SetCameraMonitorDevicePathAsync("mk4", Saved, NewPath, Ct));
        Assert.False(File.Exists(FilePath));

        string original = $$"""{ "printers": [ { "id": "mk4", {{Machine}}, "camera": { {{Camera}}, "window": { "monitor": { "displayName": "DELL U2719D", "width": 2560, "height": 1440, "devicePath": "{{OldPathJson}}", "knownTwinPaths": ["path-a"] } } } } ] }""";
        (bool written, string result) = await SetAsync(original, "mk4", Saved, OldPath);

        Assert.False(written);
        Assert.Equal(original, result);
    }

    [Theory]
    [InlineData("""{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "camera": { "enabled": true, "streamUrl": "rtsp://192.168.0.227/live", "window": { "startMode": "Sideways", "monitor": { "displayName": "DELL U2719D", "width": 2560, "height": 1440, "devicePath": "path-b", "knownTwinPaths": ["path-a"] } } } } ] }""")]
    [InlineData("""{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "camera": { "enabled": true, "streamUrl": "rtsp://192.168.0.227/live", "window": { "monitor": { "displayName": "DELL U2719D", "width": 2560, "height": 1440, "devicePath": "path-b", "knownTwinPaths": ["path-a"] } } } }, { "id": "bad" } ] }""")]
    [InlineData("""{ "printers": [ """)]
    public async Task FileWithWarningsOrErrorsIsLeftAlone(string json)
    {
        // The settings window shows such problems; a background update does not touch the file.
        (bool written, string result) = await SetAsync(json, "mk4", new CameraMonitorId("DELL U2719D", 2560, 1440, "path-b", ["path-a"]), NewPath);

        Assert.False(written);
        Assert.Equal(json, result);
    }

    [Fact]
    public async Task InvalidPathIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SetCameraMonitorDevicePathAsync("mk4", Saved, "bad\u0001path", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SetCameraMonitorDevicePathAsync("mk4", Saved, "path-a", Ct));
    }

    [Fact]
    public void WithCameraMonitorChangesOnlyTheExpectedMonitorOfThePrinter()
    {
        CameraMonitorId moved = Saved.WithDevicePath(NewPath);
        AppSettings settings = new() { Printers = [Printer("mk4", Saved), Printer("xl", Saved)] };

        AppSettings changed = settings.WithCameraMonitor("MK4", Saved, moved);

        Assert.Equal(new AppSettings { Printers = [Printer("mk4", moved), Printer("xl", Saved)] }, changed);
        Assert.Same(settings, settings.WithCameraMonitor("mini", Saved, moved));
        Assert.Same(settings, settings.WithCameraMonitor("mk4", moved, Saved));
        Assert.Same(settings, settings.WithCameraMonitor("mk4", Saved, Saved));
        Assert.Same(changed, changed.WithCameraMonitor("mk4", Saved, moved));
    }

    [Fact]
    public async Task CancellationIsHonoured()
    {
        await _store.SaveAsync(new AppSettings { Printers = [Printer("mk4", Saved)] }, Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.SetCameraMonitorDevicePathAsync("mk4", Saved, NewPath, new CancellationToken(canceled: true)));
    }

    private static PrinterDefinition Printer(string id, CameraMonitorId? monitor) => new()
    {
        Id = id,
        Name = id.ToUpperInvariant(),
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.226",
        Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) { Window = CameraWindowDefaults.Default with { Monitor = monitor } },
    };
}
