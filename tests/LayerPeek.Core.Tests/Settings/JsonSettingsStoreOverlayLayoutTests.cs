using System.Text;
using System.Text.Json;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// The widget layout in <c>settings.json</c> (T-077): a real 0.1.0 file is converted on load and saved in format 2, and
/// <see cref="JsonSettingsStore.SetCameraOverlayLayoutAsync"/> / <see cref="CameraOverlayLayoutWriter"/> replace only the
/// <c>overlay</c> section of one printer, byte for byte for the rest of the file, and announce the written layout.
/// </summary>
public sealed class JsonSettingsStoreOverlayLayoutTests : IDisposable
{
    private const string Fixture = "settings-v1-0.1.0-overlay.json";

    private static readonly CameraOverlayLayout Changed = CameraOverlayLayout.Default
        .MoveTo(OverlayWidgetIds.Temperatures, OverlayAnchor.TopRight, 0)
        .WithSize(OverlayWidgetIds.Times, OverlayWidgetSize.Large)
        .WithVisible(OverlayWidgetIds.Cooldown, false);

    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly JsonSettingsStore _store;

    public JsonSettingsStoreOverlayLayoutTests()
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

    // ---- A settings file of version 0.1.0 (format 1) ----

    [Fact]
    public async Task FileOfVersion010IsConvertedWithOneNoticeAndNoWarnings()
    {
        CopyFixture();

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        string notice = Assert.Single(result.Notices);
        Assert.Contains("4 printers", notice, StringComparison.Ordinal);
        IReadOnlyList<PrinterDefinition> printers = result.Settings.Printers;
        Assert.Equal(CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopRight), printers[0].Camera.Overlay);
        Assert.Same(CameraOverlayLayout.Default, printers[1].Camera.Overlay);
        Assert.Equal(CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopLeft), printers[2].Camera.Overlay);
        Assert.Same(CameraOverlayLayout.Default, printers[3].Camera.Overlay);
        Assert.Equal(CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.BottomRight), printers[4].Camera.Overlay);
        Assert.All(printers, static printer => Assert.All(printer.Camera.Overlay.Widgets, static w => Assert.True(w.Visible)));
        Assert.All(printers, static printer => Assert.Same(CameraOverlayOptions.Default, printer.Camera.LegacyOverlay));

        // Everything else of the file is read as before.
        Assert.Equal(45, printers[0].CooldownThresholdCelsius);
        Assert.Equal(CameraWindowStartMode.Mini, printers[0].Camera.Window.StartMode);
        Assert.Equal(CameraWindowCorner.Center, printers[0].Camera.Window.Normal.Corner);
        Assert.Equal(PrinterImageChoice.Custom, printers[2].Image);
        Assert.Equal("cs", result.Settings.Language);
        Assert.Equal(CameraOverlayOpacity.DefaultPercent, result.Settings.CameraOverlayOpacityPercent);
    }

    [Fact]
    public async Task FileOfVersion010IsSavedInFormatTwoWithoutOldKeysAndStaysStable()
    {
        CopyFixture();
        SettingsLoadResult first = await _store.LoadAsync(Ct);

        await _store.SaveAsync(first.Settings, Ct);
        string saved = await File.ReadAllTextAsync(FilePath, Ct);
        SettingsLoadResult second = await _store.LoadAsync(Ct);
        await _store.SaveAsync(second.Settings, Ct);

        using JsonDocument json = JsonDocument.Parse(saved);
        Assert.Equal(2, json.RootElement.GetProperty("version").GetInt32());
        JsonElement printers = json.RootElement.GetProperty("printers");
        Assert.All(printers.EnumerateArray(), static printer =>
        {
            JsonElement camera = printer.GetProperty("camera");
            if (camera.TryGetProperty("overlay", out JsonElement overlay))
            {
                Assert.Equal(["widgets"], overlay.EnumerateObject().Select(static p => p.Name));
            }
        });
        Assert.False(printers[1].GetProperty("camera").TryGetProperty("overlay", out _));
        Assert.False(printers[3].GetProperty("camera").TryGetProperty("overlay", out _));
        Assert.DoesNotContain("\"corner\": \"TopRight\"", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("PrinterName", saved, StringComparison.Ordinal);
        Assert.Equal(45, printers[0].GetProperty("cooldownThresholdCelsius").GetInt32());
        Assert.Equal("Center", printers[0].GetProperty("camera").GetProperty("window").GetProperty("normal").GetProperty("corner").GetString());

        Assert.Empty(second.Warnings);
        Assert.Empty(second.Notices);
        Assert.Equal(first.Settings, second.Settings);
        Assert.Equal(saved, await File.ReadAllTextAsync(FilePath, Ct));
    }

    [Fact]
    public async Task FileWithoutOverlayStaysWithoutItAfterSaving()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-v1-camera-without-window.json"), FilePath);

        SettingsLoadResult result = await _store.LoadAsync(Ct);
        await _store.SaveAsync(result.Settings, Ct);

        Assert.Empty(result.Warnings);
        Assert.Empty(result.Notices);
        Assert.All(result.Settings.Printers, static printer => Assert.Same(CameraOverlayLayout.Default, printer.Camera.Overlay));
        string json = await File.ReadAllTextAsync(FilePath, Ct);
        Assert.DoesNotContain("\"overlay\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("cameraOverlay", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpacityAndLayoutRoundTripThroughTheFile()
    {
        AppSettings settings = new()
        {
            CameraOverlayOpacityPercent = 95,
            Printers = [Printer("mk4", Changed), Printer("xl", CameraOverlayLayout.Default)],
        };

        await _store.SaveAsync(settings, Ct);
        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Empty(result.Warnings);
        Assert.Equal(settings, result.Settings);
        Assert.Same(CameraOverlayLayout.Default, result.Settings.Printers[1].Camera.Overlay);
    }

    [Fact]
    public async Task TextThatCannotBeReadInTheOverlaySectionOnlyWarns()
    {
        // Lone escaped surrogates cannot be read as strings; the section is read leniently, so the rest of the file stays usable.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            FilePath,
            """
            { "version": 2, "pollingIntervalSeconds": 30, "printers": [
              { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226",
                "camera": { "overlay": { "widgets": [ { "id": "\ud800" }, { "id": "status", "anchor": "\udc00" } ] } } },
              { "id": "xl", "name": "XL", "connectionType": "PrusaLink", "address": "192.168.0.227",
                "camera": { "overlay": { "corner": "\ud800" } } } ] }
            """,
            Ct);

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Settings.PollingInterval);
        Assert.Equal(["mk4", "xl"], result.Settings.Printers.Select(static printer => printer.Id));
        Assert.All(result.Settings.Printers, static printer => Assert.Same(CameraOverlayLayout.Default, printer.Camera.Overlay));
        Assert.Equal(3, result.Warnings.Count);
    }

    // ---- SetCameraOverlayLayoutAsync: only the overlay section of one printer changes ----

    [Fact]
    public async Task OnlyTheOverlaySectionOfThePrinterIsReplaced()
    {
        string original = Lines(
            "{",
            "  // my printers",
            "  \"version\": 1,",
            "  \"future\": { \"overlay\": [1, 2] },",
            "  \"printers\": [",
            "    {",
            "      \"id\": \"mk4\",",
            "      \"name\": \"MK4\", \"connectionType\": \"PrusaLink\", \"address\": \"192.168.0.226\",",
            "      \"camera\": {",
            "        \"enabled\": true,",
            "        \"overlay\": { \"corner\": \"TopRight\", \"normal\": [\"State\"] } /* old */,",
            "        \"streamUrl\": \"rtsp://192.168.0.227/live\"",
            "      }",
            "    },",
            "    { \"id\": \"xl\", \"name\": \"XL\", \"connectionType\": \"PrusaLink\", \"address\": \"192.168.0.230\", \"camera\": { \"overlay\": { \"corner\": \"TopLeft\" } } }",
            "  ]",
            "}");
        const string OldValue = "{ \"corner\": \"TopRight\", \"normal\": [\"State\"] }";

        (bool written, string result) = await SetAsync(original, "MK4", Changed);

        Assert.True(written);
        int start = original.IndexOf(OldValue, StringComparison.Ordinal);
        int end = start + OldValue.Length;
        Assert.StartsWith(original[..start], result, StringComparison.Ordinal);
        Assert.EndsWith(original[end..], result, StringComparison.Ordinal);
        string value = result[start..^(original.Length - end)];
        Assert.Equal(ExpectedOverlay(Changed), SettingsMapperCameraOverlayTests.Compact(value));
        Assert.DoesNotContain("\r", value, StringComparison.Ordinal);
        Assert.All(value.Split('\n').Skip(1), static line => Assert.StartsWith("        ", line, StringComparison.Ordinal));

        SettingsLoadResult loaded = await _store.LoadAsync(Ct);
        Assert.Equal(Changed, loaded.Settings.Printers[0].Camera.Overlay);
        Assert.Equal(CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopLeft), loaded.Settings.Printers[1].Camera.Overlay);
        Assert.Empty(loaded.Warnings);
    }

    [Fact]
    public async Task FileWrittenByTheAppKeepsItsLineEndingsAndIndentation()
    {
        await _store.SaveAsync(new AppSettings { Printers = [Printer("mk4", CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopRight))] }, Ct);
        string original = await File.ReadAllTextAsync(FilePath, Ct);

        bool written = await _store.SetCameraOverlayLayoutAsync("mk4", Changed, Ct);

        Assert.True(written);
        string result = await File.ReadAllTextAsync(FilePath, Ct);
        await _store.SaveAsync((await _store.LoadAsync(Ct)).Settings, Ct);
        Assert.Equal(await File.ReadAllTextAsync(FilePath, Ct), result);
        Assert.NotEqual(original, result);
    }

    [Fact]
    public async Task MissingSectionIsAddedAfterTheLastValueIndentedLikeTheFirstProperty()
    {
        string original = Lines(
            "{ \"printers\": [ {",
            "  \"id\": \"mk4\", \"name\": \"MK4\", \"connectionType\": \"PrusaLink\", \"address\": \"192.168.0.226\",",
            "  \"camera\": {",
            "    \"enabled\": true,",
            "    \"streamUrl\": \"rtsp://192.168.0.227/live\"",
            "  }",
            "} ] }");

        (bool written, string result) = await SetAsync(original, "mk4", Changed);

        Assert.True(written);
        const string Anchor = "\"streamUrl\": \"rtsp://192.168.0.227/live\"";
        int end = original.IndexOf(Anchor, StringComparison.Ordinal) + Anchor.Length;
        Assert.StartsWith(original[..end] + ",\n    \"overlay\": {\n      \"widgets\": [", result, StringComparison.Ordinal);
        Assert.EndsWith("    }" + original[end..], result, StringComparison.Ordinal);
        Assert.Equal(Changed, (await _store.LoadAsync(Ct)).Settings.Printers[0].Camera.Overlay);
    }

    [Fact]
    public async Task OnOneLineTheSectionIsWrittenCompactly()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "a", "camera": { "enabled": false, "overlay": { "corner": "TopLeft" } /* c */ } } ] }""";

        (bool written, string result) = await SetAsync(Original, "mk4", Changed);

        Assert.True(written);
        Assert.Equal(Original.Replace("""{ "corner": "TopLeft" }""", ExpectedOverlay(Changed), StringComparison.Ordinal), result);
    }

    [Fact]
    public async Task DefaultLayoutReplacesTheSectionWithAnEmptyObject()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "a", "camera": { "overlay": { "corner": "TopLeft" } } } ] }""";

        (bool written, string result) = await SetAsync(Original, "mk4", CameraOverlayLayout.Default);

        Assert.True(written);
        Assert.Equal(Original.Replace("""{ "corner": "TopLeft" }""", "{}", StringComparison.Ordinal), result);
        SettingsLoadResult loaded = await _store.LoadAsync(Ct);
        Assert.Same(CameraOverlayLayout.Default, loaded.Settings.Printers[0].Camera.Overlay);
        Assert.Empty(loaded.Notices);
    }

    [Fact]
    public async Task DefaultLayoutWithoutASectionWritesNothing()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "a", "camera": { "enabled": false } } ] }""";

        (bool written, string result) = await SetAsync(Original, "mk4", CameraOverlayLayout.Default);

        Assert.False(written);
        Assert.Equal(Original, result);
    }

    [Fact]
    public async Task SameLayoutWritesNothing()
    {
        await _store.SaveAsync(new AppSettings { Printers = [Printer("mk4", Changed)] }, Ct);
        DateTime before = File.GetLastWriteTimeUtc(FilePath);
        string original = await File.ReadAllTextAsync(FilePath, Ct);

        Assert.False(await _store.SetCameraOverlayLayoutAsync("mk4", Changed, Ct));
        Assert.Equal(original, await File.ReadAllTextAsync(FilePath, Ct));
        Assert.Equal(before, File.GetLastWriteTimeUtc(FilePath));
    }

    [Fact]
    public async Task DuplicateSectionsAndEntriesWithTheSameIdAreAllReplaced()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "camera": { "OVERLAY": { "corner": "TopLeft" }, "overlay": 5 } }, { "id": " Mk4 ", "camera": { "overlay": null } }, { "id": "xl", "camera": { "overlay": { "corner": "TopLeft" } } } ] }""";
        string expected = ExpectedOverlay(Changed);

        (bool written, string result) = await SetAsync(Original, "mk4", Changed);

        Assert.True(written);
        Assert.Equal(
            Original
                .Replace("""{ "corner": "TopLeft" }, "overlay": 5""", $"{expected}, \"overlay\": {expected}", StringComparison.Ordinal)
                .Replace("\"overlay\": null", $"\"overlay\": {expected}", StringComparison.Ordinal),
            result);
    }

    [Fact]
    public async Task UnknownWidgetsOfTheLayoutAreWritten()
    {
        CameraOverlayLayout layout = new([.. Changed.Widgets, new OverlayWidgetPlacement("future-widget", OverlayAnchor.TopCenter, OverlayWidgetSize.Small, emphasized: true, visible: false)]);
        await _store.SaveAsync(new AppSettings { Printers = [Printer("mk4", CameraOverlayLayout.Default)] }, Ct);

        Assert.True(await _store.SetCameraOverlayLayoutAsync("mk4", layout, Ct));

        Assert.Equal(layout, (await _store.LoadAsync(Ct)).Settings.Printers[0].Camera.Overlay);
    }

    [Theory]
    [InlineData("""{ "printers": [ { "id": "xl", "camera": { } } ] }""")]
    [InlineData("""{ "printers": [ { "id": "mk4" } ] }""")]
    [InlineData("""{ "printers": [ { "id": "mk4", "camera": null } ] }""")]
    [InlineData("""{ "printers": { "id": "mk4", "camera": { } } }""")]
    [InlineData("""{ "printers": [ { "id": "mk4", "camera": { } ] }""")]
    [InlineData("""[ { "id": "mk4", "camera": { } } ]""")]
    public async Task FileWithoutACameraOfThatPrinterOrInvalidJsonIsLeftAlone(string original)
    {
        (bool written, string result) = await SetAsync(original, "mk4", Changed);

        Assert.False(written);
        Assert.Equal(original, result);
    }

    [Fact]
    public async Task NestedLookAlikeIsNotTheSection()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "camera": { "x": { "overlay": {} } } } ] }""";

        (bool written, string result) = await SetAsync(Original, "mk4", Changed);

        Assert.True(written);
        Assert.Equal(Original.Replace("""{ "overlay": {} } }""", $$"""{ "overlay": {} }, "overlay": {{ExpectedOverlay(Changed)}} }""", StringComparison.Ordinal), result);
    }

    [Fact]
    public async Task EmptyCameraObjectGetsTheSection()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "camera": { } } ] }""";

        (bool written, string result) = await SetAsync(Original, "mk4", Changed);

        Assert.True(written);
        Assert.Equal(Original.Replace("{ }", $"{{ \"overlay\": {ExpectedOverlay(Changed)} }}", StringComparison.Ordinal), result);
    }

    [Fact]
    public async Task MissingFileIsNotCreated()
    {
        Assert.False(await _store.SetCameraOverlayLayoutAsync("mk4", Changed, Ct));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task ByteOrderMarkIsKept()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "camera": { "overlay": {} } } ] }""";
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(FilePath, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Original)], Ct);

        Assert.True(await _store.SetCameraOverlayLayoutAsync("mk4", Changed, Ct));

        byte[] result = await File.ReadAllBytesAsync(FilePath, Ct);
        Assert.Equal([0xEF, 0xBB, 0xBF], result[..3]);
        Assert.Equal(Original.Replace("{}", ExpectedOverlay(Changed), StringComparison.Ordinal), Encoding.UTF8.GetString(result[3..]));
    }

    [Fact]
    public async Task ArgumentsAreValidated()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SetCameraOverlayLayoutAsync(" ", Changed, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.SetCameraOverlayLayoutAsync("mk4", null!, Ct));
    }

    [Fact]
    public async Task NoTemporaryFilesAreLeft()
    {
        await _store.SaveAsync(new AppSettings { Printers = [Printer("mk4", CameraOverlayLayout.Default)] }, Ct);

        Assert.True(await _store.SetCameraOverlayLayoutAsync("mk4", Changed, Ct));

        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    // ---- CameraOverlayLayoutWriter: announcement and concurrency with the settings window ----

    [Fact]
    public async Task WriterAnnouncesTheWrittenLayout()
    {
        await _store.SaveAsync(new AppSettings { Printers = [Printer("mk4", CameraOverlayLayout.Default)] }, Ct);
        CameraOverlayLayoutWriter writer = new(_store);
        List<CameraOverlayLayoutSavedEventArgs> announced = [];
        writer.LayoutSaved += (sender, e) =>
        {
            Assert.Same(writer, sender);
            announced.Add(e);
        };

        Assert.True(await writer.SaveAsync("mk4", Changed, Ct));
        Assert.False(await writer.SaveAsync("mk4", Changed, Ct));
        Assert.False(await writer.SaveAsync("unknown", Changed, Ct));

        CameraOverlayLayoutSavedEventArgs saved = Assert.Single(announced);
        Assert.Equal("mk4", saved.PrinterId);
        Assert.Same(Changed, saved.Layout);
    }

    [Fact]
    public async Task WriterOverAStoreWithoutAFileWritesAndAnnouncesNothing()
    {
        CameraOverlayLayoutWriter writer = new(new MemoryOnlyStore());
        bool announced = false;
        writer.LayoutSaved += (_, _) => announced = true;

        Assert.False(await writer.SaveAsync("mk4", Changed, Ct));
        Assert.False(announced);
        Assert.Throws<ArgumentNullException>(() => new CameraOverlayLayoutWriter(null!));
    }

    [Fact]
    public async Task SettingsWindowReloadsOnlyTheLayoutAndItsNextSaveKeepsBoth()
    {
        AppSettings saved = new() { Printers = [Printer("mk4", CameraOverlayLayout.Default), Printer("xl", CameraOverlayLayout.Default)] };
        await _store.SaveAsync(saved, Ct);
        CameraOverlayLayoutWriter writer = new(_store);

        // The settings window holds unsaved changes of other values; the announcement updates only the layout (T-092).
        AppSettings editor = saved with { PollingInterval = TimeSpan.FromSeconds(30), CameraOverlayOpacityPercent = 40 };
        writer.LayoutSaved += (_, e) => editor = editor.WithCameraOverlayLayout(e.PrinterId, e.Layout);

        Assert.True(await writer.SaveAsync("mk4", Changed, Ct));
        await _store.SaveAsync(editor, Ct);

        SettingsLoadResult loaded = await _store.LoadAsync(Ct);
        Assert.Equal(Changed, loaded.Settings.Printers[0].Camera.Overlay);
        Assert.Same(CameraOverlayLayout.Default, loaded.Settings.Printers[1].Camera.Overlay);
        Assert.Equal(TimeSpan.FromSeconds(30), loaded.Settings.PollingInterval);
        Assert.Equal(40, loaded.Settings.CameraOverlayOpacityPercent);
    }

    [Fact]
    public async Task ConcurrentLayoutWritesAndSettingsSavesNeverLoseOtherValues()
    {
        AppSettings baseline = new() { Printers = [Printer("mk4", CameraOverlayLayout.Default), Printer("xl", CameraOverlayLayout.Default)] };
        await _store.SaveAsync(baseline, Ct);
        CameraOverlayLayoutWriter writer = new(_store);
        CameraOverlayLayout[] layouts =
        [
            Changed,
            CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopRight),
            CameraOverlayLayout.Default.WithVisible(OverlayWidgetIds.Status, false),
        ];

        for (int i = 0; i < 30; i++)
        {
            CameraOverlayLayout layout = layouts[i % layouts.Length];
            AppSettings window = baseline with { PollingInterval = TimeSpan.FromSeconds(10 + i), CameraOverlayOpacityPercent = 30 + i };

            await Task.WhenAll(
                Task.Run(() => writer.SaveAsync("mk4", layout, Ct), Ct),
                Task.Run(() => _store.SaveAsync(window, Ct), Ct));

            SettingsLoadResult loaded = await _store.LoadAsync(Ct);
            Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
            Assert.Empty(loaded.Warnings);
            Assert.Equal(window.PollingInterval, loaded.Settings.PollingInterval);
            Assert.Equal(window.CameraOverlayOpacityPercent, loaded.Settings.CameraOverlayOpacityPercent);
            Assert.Contains(loaded.Settings.Printers[0].Camera.Overlay, (CameraOverlayLayout[])[layout, CameraOverlayLayout.Default]);
            Assert.Same(CameraOverlayLayout.Default, loaded.Settings.Printers[1].Camera.Overlay);
        }

        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    private static string ExpectedOverlay(CameraOverlayLayout layout) =>
        "{\"widgets\":[" + string.Join(",", layout.Widgets.Select(static w =>
            $$"""{"id":"{{w.WidgetId}}","anchor":"{{w.Anchor}}","size":"{{w.Size}}","emphasized":{{(w.Emphasized ? "true" : "false")}},"visible":{{(w.Visible ? "true" : "false")}}}""")) + "]}";

    private static PrinterDefinition Printer(string id, CameraOverlayLayout layout) => new()
    {
        Id = id,
        Name = id.ToUpperInvariant(),
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.226",
        Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) { Overlay = layout },
    };

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    private void CopyFixture()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", Fixture), FilePath);
    }

    private async Task<(bool Written, string Result)> SetAsync(string original, string printerId, CameraOverlayLayout layout)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, original, Ct);
        bool written = await _store.SetCameraOverlayLayoutAsync(printerId, layout, Ct);
        return (written, await File.ReadAllTextAsync(FilePath, Ct));
    }

    /// <summary>A store without a file: the interface defaults write nothing.</summary>
    private sealed class MemoryOnlyStore : ISettingsStore
    {
        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsLoadResult { Settings = AppSettings.Default, Status = SettingsLoadStatus.NotFound });

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
