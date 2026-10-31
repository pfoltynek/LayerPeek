using System.Text;

using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// The "always on top" option switched in the camera window (T-117): <see cref="JsonSettingsStore.SetCameraWindowTopmostAsync"/>
/// replaces only <c>topmostNormal</c> / <c>topmostMini</c> of one printer, byte for byte for the rest of the file (like
/// <see cref="JsonSettingsStore.SetCameraOverlayLayoutAsync"/>), and <see cref="CameraWindowTopmostWriter"/> announces it.
/// </summary>
public sealed class JsonSettingsStoreCameraTopmostTests : IDisposable
{
    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly JsonSettingsStore _store;

    public JsonSettingsStoreCameraTopmostTests()
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

    // ---- The model ----

    [Fact]
    public void WithTopmostChangesOnlyTheOptionOfTheMode()
    {
        CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };

        CameraWindowDefaults normal = defaults.WithTopmost(CameraWindowMode.Normal, true);
        CameraWindowDefaults mini = defaults.WithTopmost(CameraWindowMode.Mini, false);

        Assert.Equal(defaults with { TopmostNormal = true }, normal);
        Assert.Equal(defaults with { TopmostMini = false }, mini);
        Assert.Same(defaults, defaults.WithTopmost(CameraWindowMode.Normal, false));
        Assert.Same(defaults, defaults.WithTopmost(CameraWindowMode.Mini, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => defaults.WithTopmost(CameraWindowMode.FullScreen, true));
    }

    [Fact]
    public void SettingsWithTopmostChangeOnlyThatPrinterCaseInsensitively()
    {
        AppSettings settings = new() { Printers = [Printer("mk4"), Printer("xl")], PollingInterval = TimeSpan.FromSeconds(9) };

        AppSettings updated = settings.WithCameraWindowTopmost("MK4", CameraWindowMode.Normal, true);

        Assert.True(updated.Printers[0].Camera.Window.TopmostNormal);
        Assert.Equal(settings.Printers[0] with { Camera = settings.Printers[0].Camera with { Window = CameraWindowDefaults.Default with { TopmostNormal = true } } }, updated.Printers[0]);
        Assert.Same(settings.Printers[1], updated.Printers[1]);
        Assert.Equal(settings.PollingInterval, updated.PollingInterval);
        Assert.Same(settings, settings.WithCameraWindowTopmost("mk4", CameraWindowMode.Mini, true));
        Assert.Same(settings, settings.WithCameraWindowTopmost("unknown", CameraWindowMode.Normal, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.WithCameraWindowTopmost("mk4", CameraWindowMode.FullScreen, true));
    }

    // ---- SetCameraWindowTopmostAsync: only the option of one printer changes ----

    [Fact]
    public async Task OnlyTheValueOfThePrinterIsReplaced()
    {
        string original = Lines(
            "{",
            "  // my printers",
            "  \"version\": 2,",
            "  \"future\": { \"topmostNormal\": false },",
            "  \"printers\": [",
            "    {",
            "      \"id\": \"mk4\",",
            "      \"name\": \"MK4\", \"connectionType\": \"PrusaLink\", \"address\": \"192.168.0.226\",",
            "      \"camera\": {",
            "        \"enabled\": true,",
            "        \"window\": { \"startMode\": \"Mini\", \"topmostNormal\": false /* old */, \"topmostMini\": true },",
            "        \"streamUrl\": \"rtsp://192.168.0.227/live\"",
            "      }",
            "    },",
            "    { \"id\": \"xl\", \"name\": \"XL\", \"connectionType\": \"PrusaLink\", \"address\": \"192.168.0.230\", \"camera\": { \"enabled\": true, \"streamUrl\": \"rtsp://192.168.0.228/live\", \"window\": { \"topmostNormal\": false } } }",
            "  ]",
            "}");

        (CameraWindowTopmostStoreResult written, string result) = await SetAsync(original, "MK4", CameraWindowMode.Normal, true);

        Assert.Equal(CameraWindowTopmostStoreResult.Written, written);
        Assert.Equal(original.Replace("\"topmostNormal\": false /* old */", "\"topmostNormal\": true /* old */", StringComparison.Ordinal), result);
        SettingsLoadResult loaded = await _store.LoadAsync(Ct);
        Assert.Empty(loaded.Warnings);
        Assert.True(loaded.Settings.Printers[0].Camera.Window.TopmostNormal);
        Assert.Equal(CameraWindowStartMode.Mini, loaded.Settings.Printers[0].Camera.Window.StartMode);
        Assert.False(loaded.Settings.Printers[1].Camera.Window.TopmostNormal);
    }

    [Theory]
    [InlineData(CameraWindowMode.Normal, true)]
    [InlineData(CameraWindowMode.Mini, false)]
    public async Task FileWrittenByTheAppStaysAsAFullSaveWritesIt(CameraWindowMode mode, bool topmost)
    {
        CameraWindowDefaults window = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };
        await _store.SaveAsync(new AppSettings { Printers = [Printer("mk4", window), Printer("xl", window)] }, Ct);
        string original = await File.ReadAllTextAsync(FilePath, Ct);

        Assert.Equal(CameraWindowTopmostStoreResult.Written, await _store.SetCameraWindowTopmostAsync("mk4", mode, topmost, Ct));

        string result = await File.ReadAllTextAsync(FilePath, Ct);
        Assert.NotEqual(original, result);
        SettingsLoadResult loaded = await _store.LoadAsync(Ct);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(window.WithTopmost(mode, topmost), loaded.Settings.Printers[0].Camera.Window);
        Assert.Equal(window, loaded.Settings.Printers[1].Camera.Window);
        // A full save of what was loaded writes the same bytes: only the value changed, in place.
        await _store.SaveAsync(loaded.Settings, Ct);
        Assert.Equal(result, await File.ReadAllTextAsync(FilePath, Ct));
    }

    [Fact]
    public async Task MissingWindowSectionIsAddedIndentedLikeTheCameraValues()
    {
        string original = Lines(
            "{ \"printers\": [ {",
            "  \"id\": \"mk4\", \"name\": \"MK4\", \"connectionType\": \"PrusaLink\", \"address\": \"192.168.0.226\",",
            "  \"camera\": {",
            "    \"enabled\": true,",
            "    \"streamUrl\": \"rtsp://192.168.0.227/live\"",
            "  }",
            "} ] }");

        (CameraWindowTopmostStoreResult written, string result) = await SetAsync(original, "mk4", CameraWindowMode.Mini, false);

        Assert.Equal(CameraWindowTopmostStoreResult.Written, written);
        Assert.Equal(
            original.Replace(
                "\"streamUrl\": \"rtsp://192.168.0.227/live\"\n",
                "\"streamUrl\": \"rtsp://192.168.0.227/live\",\n    \"window\": {\n      \"topmostMini\": false\n    }\n",
                StringComparison.Ordinal),
            result);
        SettingsLoadResult loaded = await _store.LoadAsync(Ct);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(CameraWindowDefaults.Default with { TopmostMini = false }, loaded.Settings.Printers[0].Camera.Window);
    }

    [Fact]
    public async Task WindowsLineEndingsAreFollowed()
    {
        string original = Lines(
            "{ \"printers\": [ {",
            "  \"id\": \"mk4\",",
            "  \"camera\": {",
            "    \"streamUrl\": \"rtsp://192.168.0.227/live\"",
            "  }",
            "} ] }").Replace("\n", "\r\n", StringComparison.Ordinal);

        (CameraWindowTopmostStoreResult written, string result) = await SetAsync(original, "mk4", CameraWindowMode.Normal, true);

        Assert.Equal(CameraWindowTopmostStoreResult.Written, written);
        Assert.Contains(",\r\n    \"window\": {\r\n      \"topmostNormal\": true\r\n    }\r\n  }", result, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\r", result, StringComparison.Ordinal);
        Assert.Equal(0, result.Replace("\r\n", string.Empty, StringComparison.Ordinal).Count(static c => c == '\n'));
    }

    [Fact]
    public async Task OnOneLineTheSectionIsWrittenCompactly()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "camera": { "streamUrl": "rtsp://192.168.0.227/live" } } ] }""";

        (CameraWindowTopmostStoreResult written, string result) = await SetAsync(Original, "mk4", CameraWindowMode.Normal, true);

        Assert.Equal(CameraWindowTopmostStoreResult.Written, written);
        Assert.Equal("""{ "printers": [ { "id": "mk4", "camera": { "streamUrl": "rtsp://192.168.0.227/live", "window": { "topmostNormal": true } } } ] }""", result);
    }

    [Fact]
    public async Task MissingValueIsAddedAfterTheLastValueOfTheWindow()
    {
        string original = Lines(
            "{ \"printers\": [ { \"id\": \"mk4\", \"name\": \"MK4\", \"connectionType\": \"PrusaLink\", \"address\": \"192.168.0.226\", \"camera\": {",
            "  \"window\": {",
            "    \"startMode\": \"Normal\",",
            "    \"normal\": { \"width\": 800, \"height\": 450, \"corner\": \"Center\" }",
            "  }",
            "} } ] }");

        (CameraWindowTopmostStoreResult written, string result) = await SetAsync(original, "mk4", CameraWindowMode.Normal, true);

        Assert.Equal(CameraWindowTopmostStoreResult.Written, written);
        Assert.Equal(
            original.Replace("\"corner\": \"Center\" }\n", "\"corner\": \"Center\" },\n    \"topmostNormal\": true\n", StringComparison.Ordinal),
            result);
        Assert.True((await _store.LoadAsync(Ct)).Settings.Printers[0].Camera.Window.TopmostNormal);
    }

    [Fact]
    public async Task EmptyWindowAndCameraObjectsGetTheValue()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "camera": { "window": {} } }, { "id": "xl", "camera": {} } ] }""";

        (CameraWindowTopmostStoreResult first, string afterFirst) = await SetAsync(Original, "mk4", CameraWindowMode.Normal, true);
        Assert.Equal(CameraWindowTopmostStoreResult.Written, first);
        Assert.Equal("""{ "printers": [ { "id": "mk4", "camera": { "window": { "topmostNormal": true } } }, { "id": "xl", "camera": {} } ] }""", afterFirst);

        Assert.Equal(CameraWindowTopmostStoreResult.Written, await _store.SetCameraWindowTopmostAsync("xl", CameraWindowMode.Mini, false, Ct));
        Assert.Equal(
            """{ "printers": [ { "id": "mk4", "camera": { "window": { "topmostNormal": true } } }, { "id": "xl", "camera": { "window": { "topmostMini": false }} } ] }""",
            await File.ReadAllTextAsync(FilePath, Ct));
    }

    [Fact]
    public async Task NullWindowIsReplacedByASectionWithTheValue()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "camera": { "window": null, "enabled": true } } ] }""";

        (CameraWindowTopmostStoreResult written, string result) = await SetAsync(Original, "mk4", CameraWindowMode.Mini, false);

        Assert.Equal(CameraWindowTopmostStoreResult.Written, written);
        Assert.Equal(Original.Replace("\"window\": null", "\"window\": { \"topmostMini\": false }", StringComparison.Ordinal), result);
        Assert.False((await _store.LoadAsync(Ct)).Settings.Printers[0].Camera.Window.TopmostMini);
    }

    [Theory]
    [InlineData(CameraWindowMode.Normal, false)]
    [InlineData(CameraWindowMode.Mini, true)]
    public async Task DefaultValueWithoutAValueOrSectionWritesNothing(CameraWindowMode mode, bool topmost)
    {
        const string Original = """{ "printers": [ { "id": "mk4", "camera": { "enabled": true } }, { "id": "xl", "camera": { "window": { "startMode": "Mini" } } }, { "id": "mini", "camera": { "window": null } } ] }""";

        foreach (string id in (string[])["mk4", "xl", "mini"])
        {
            (CameraWindowTopmostStoreResult written, string result) = await SetAsync(Original, id, mode, topmost);
            Assert.Equal(CameraWindowTopmostStoreResult.AlreadyStored, written);
            Assert.Equal(Original, result);
        }
    }

    [Fact]
    public async Task SameValueWritesNothing()
    {
        await _store.SaveAsync(new AppSettings { Printers = [Printer("mk4", CameraWindowDefaults.Default with { TopmostNormal = true })] }, Ct);
        DateTime before = File.GetLastWriteTimeUtc(FilePath);
        string original = await File.ReadAllTextAsync(FilePath, Ct);

        Assert.Equal(CameraWindowTopmostStoreResult.AlreadyStored, await _store.SetCameraWindowTopmostAsync("mk4", CameraWindowMode.Normal, true, Ct));
        Assert.Equal(CameraWindowTopmostStoreResult.AlreadyStored, await _store.SetCameraWindowTopmostAsync("mk4", CameraWindowMode.Mini, true, Ct));

        Assert.Equal(original, await File.ReadAllTextAsync(FilePath, Ct));
        Assert.Equal(before, File.GetLastWriteTimeUtc(FilePath));
    }

    [Fact]
    public async Task DuplicateValuesSectionsAndEntriesWithTheSameIdAreAllReplaced()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "camera": { "window": { "topmostMini": true, "TopmostMini": null } }, "camera": { "window": { "topmostMini": true } } }, { "id": " MK4 ", "camera": { "window": { "topmostMini": true } } }, { "id": "xl", "camera": { "window": { "topmostMini": true } } } ] }""";

        (CameraWindowTopmostStoreResult written, string result) = await SetAsync(Original, "mk4", CameraWindowMode.Mini, false);

        Assert.Equal(CameraWindowTopmostStoreResult.Written, written);
        Assert.Equal(
            """{ "printers": [ { "id": "mk4", "camera": { "window": { "topmostMini": false, "TopmostMini": false } }, "camera": { "window": { "topmostMini": false } } }, { "id": " MK4 ", "camera": { "window": { "topmostMini": false } } }, { "id": "xl", "camera": { "window": { "topmostMini": true } } } ] }""",
            result);
    }

    [Fact]
    public async Task NestedLookAlikeIsNotTheValue()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "camera": { "window": { "normal": { "topmostNormal": false } } }, "window": { "topmostNormal": false } } ], "window": { "topmostNormal": false } }""";

        (CameraWindowTopmostStoreResult written, string result) = await SetAsync(Original, "mk4", CameraWindowMode.Normal, true);

        Assert.Equal(CameraWindowTopmostStoreResult.Written, written);
        Assert.Equal(
            """{ "printers": [ { "id": "mk4", "camera": { "window": { "normal": { "topmostNormal": false }, "topmostNormal": true } }, "window": { "topmostNormal": false } } ], "window": { "topmostNormal": false } }""",
            result);
    }

    [Theory]
    [InlineData("""{ "printers": [ { "id": "mk4", "name": "MK4" } ] }""")]
    [InlineData("""{ "printers": [ { "id": "xl", "camera": { "window": { "topmostNormal": false } } } ] }""")]
    [InlineData("""{ "printers": [ { "id": "mk4", "camera": { "window": { "topmostNormal": false } } ] }""")]
    [InlineData("""[ { "id": "mk4", "camera": { "window": { "topmostNormal": false } } } ]""")]
    public async Task FileWithoutACameraOfThatPrinterOrInvalidJsonIsLeftAlone(string original)
    {
        (CameraWindowTopmostStoreResult written, string result) = await SetAsync(original, "mk4", CameraWindowMode.Normal, true);

        Assert.Equal(CameraWindowTopmostStoreResult.LeftAlone, written);
        Assert.Equal(original, result);
    }

    [Theory]
    [InlineData("""{ "version": 3, "printers": [ { "id": "mk4", "camera": { "window": { "topmostNormal": false } } } ] }""")]
    [InlineData("""{ "version": "x", "printers": [ { "id": "mk4", "camera": { "window": { "topmostNormal": false } } } ] }""")]
    [InlineData("""{ "version": { }, "printers": [ { "id": "mk4", "camera": { "window": { "topmostNormal": false } } } ] }""")]
    public async Task FileOfANewerOrUnreadableFormatIsLeftAlone(string original)
    {
        (CameraWindowTopmostStoreResult written, string result) = await SetAsync(original, "mk4", CameraWindowMode.Normal, true);

        Assert.Equal(CameraWindowTopmostStoreResult.LeftAlone, written);
        Assert.Equal(original, result);
    }

    [Fact]
    public async Task MissingFileIsNotCreated()
    {
        Assert.Equal(CameraWindowTopmostStoreResult.LeftAlone, await _store.SetCameraWindowTopmostAsync("mk4", CameraWindowMode.Normal, true, Ct));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task ByteOrderMarkIsKept()
    {
        const string Original = """{ "printers": [ { "id": "mk4", "camera": { "window": { "topmostMini": true } } } ] }""";
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(FilePath, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Original)], Ct);

        Assert.Equal(CameraWindowTopmostStoreResult.Written, await _store.SetCameraWindowTopmostAsync("mk4", CameraWindowMode.Mini, false, Ct));

        byte[] result = await File.ReadAllBytesAsync(FilePath, Ct);
        Assert.Equal([0xEF, 0xBB, 0xBF], result[..3]);
        Assert.Equal(Original.Replace("true", "false", StringComparison.Ordinal), Encoding.UTF8.GetString(result[3..]));
    }

    [Fact]
    public async Task ArgumentsAreValidated()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SetCameraWindowTopmostAsync(" ", CameraWindowMode.Normal, true, Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _store.SetCameraWindowTopmostAsync("mk4", CameraWindowMode.FullScreen, true, Ct));
        CameraWindowTopmostWriter writer = new(_store);
        await Assert.ThrowsAsync<ArgumentException>(() => writer.SaveAsync("", CameraWindowMode.Normal, true, Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => writer.SaveAsync("mk4", CameraWindowMode.FullScreen, true, Ct));
    }

    [Fact]
    public async Task NoTemporaryFilesAreLeft()
    {
        await _store.SaveAsync(new AppSettings { Printers = [Printer("mk4")] }, Ct);

        Assert.Equal(CameraWindowTopmostStoreResult.Written, await _store.SetCameraWindowTopmostAsync("mk4", CameraWindowMode.Normal, true, Ct));

        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    // ---- CameraWindowTopmostWriter: announcement and concurrency with the settings window ----

    [Fact]
    public async Task WriterAnnouncesWrittenAndAlreadyStoredOptionsWithTheTrimmedPrinterId()
    {
        AppSettings stored = new() { Printers = [Printer("mk4")] };
        await _store.SaveAsync(stored, Ct);
        CameraWindowTopmostWriter writer = new(_store);
        List<CameraWindowTopmostSavedEventArgs> announced = [];
        writer.TopmostSaved += (sender, e) =>
        {
            Assert.Same(writer, sender);
            announced.Add(e);
        };

        Assert.True(await writer.SaveAsync("  mk4\t", CameraWindowMode.Mini, false, Ct));
        string afterFirst = await File.ReadAllTextAsync(FilePath, Ct);
        // The file holds it already: nothing written, still a success that is announced (the running settings catch up).
        Assert.True(await writer.SaveAsync("mk4", CameraWindowMode.Mini, false, Ct));
        Assert.Equal(afterFirst, await File.ReadAllTextAsync(FilePath, Ct));
        // Left alone: no announcement.
        Assert.False(await writer.SaveAsync("unknown", CameraWindowMode.Normal, true, Ct));

        Assert.Equal(2, announced.Count);
        Assert.All(announced, static saved =>
        {
            Assert.Equal("mk4", saved.PrinterId);
            Assert.Equal(CameraWindowMode.Mini, saved.Mode);
            Assert.False(saved.Topmost);
        });
        Assert.False(stored.WithCameraWindowTopmost(announced[0].PrinterId, announced[0].Mode, announced[0].Topmost).Printers[0].Camera.Window.TopmostMini);
        Assert.False((await _store.LoadAsync(Ct)).Settings.Printers[0].Camera.Window.TopmostMini);
    }

    [Fact]
    public async Task OptionASettingsSaveWroteAlreadyIsASuccessWithoutAWrite()
    {
        // The settings window saved "always on top" for the normal window; monitoring has not taken it over yet (its old run
        // is still ending), so the open camera window still shows it off and the click asks for "on" again.
        await _store.SaveAsync(new AppSettings { Printers = [Printer("mk4", CameraWindowDefaults.Default with { TopmostNormal = true })] }, Ct);
        string original = await File.ReadAllTextAsync(FilePath, Ct);
        DateTime before = File.GetLastWriteTimeUtc(FilePath);
        CameraWindowTopmostWriter writer = new(_store);
        List<CameraWindowTopmostSavedEventArgs> announced = [];
        writer.TopmostSaved += (_, e) => announced.Add(e);

        Assert.True(await writer.SaveAsync("mk4", CameraWindowMode.Normal, true, Ct));

        Assert.Equal(original, await File.ReadAllTextAsync(FilePath, Ct));
        Assert.Equal(before, File.GetLastWriteTimeUtc(FilePath));
        CameraWindowTopmostSavedEventArgs saved = Assert.Single(announced);
        Assert.True(saved.Topmost);
        Assert.Equal(CameraWindowMode.Normal, saved.Mode);
    }

    [Theory]
    [InlineData("""{ "printers": [ { "id": "mk4", "camera": { "window": [ { "topmostNormal": false } ] } } ] }""")]
    [InlineData("""{ "printers": [ { "id": "mk4", "camera": { "window": "normal" } } ] }""")]
    [InlineData("""{ "printers": [ { "id": "mk4", "camera": { "window": 1 } } ] }""")]
    [InlineData("""{ "printers": [ { "id": "mk4", "camera": { "window": true } } ] }""")]
    [InlineData("""{ "printers": [ { "id": "mk4", "camera": { "window": { "topmostNormal": false } } }, { "id": "MK4", "camera": { "window": [] } } ] }""")]
    public async Task WindowOfAnotherTypeIsLeftAloneWithoutADuplicateKey(string original)
    {
        foreach (bool topmost in (bool[])[true, false])
        {
            (CameraWindowTopmostStoreResult written, string result) = await SetAsync(original, "mk4", CameraWindowMode.Normal, topmost);

            Assert.Equal(CameraWindowTopmostStoreResult.LeftAlone, written);
            Assert.Equal(original, result);
        }

        CameraWindowTopmostWriter writer = new(_store);
        int announced = 0;
        writer.TopmostSaved += (_, _) => announced++;
        Assert.False(await writer.SaveAsync("mk4", CameraWindowMode.Normal, true, Ct));
        Assert.Equal(0, announced);
    }

    [Fact]
    public async Task FailingSubscriberIsLoggedAndNeitherStopsTheOthersNorFailsTheSave()
    {
        await _store.SaveAsync(new AppSettings { Printers = [Printer("mk4")] }, Ct);
        ListLogger<CameraWindowTopmostWriter> logger = new();
        CameraWindowTopmostWriter writer = new(_store, logger);
        InvalidOperationException failure = new("subscriber failed");
        List<string> calls = [];
        writer.TopmostSaved += (_, _) =>
        {
            calls.Add("first");
            throw failure;
        };
        writer.TopmostSaved += (_, e) => calls.Add("second " + e.PrinterId);

        Assert.True(await writer.SaveAsync("mk4", CameraWindowMode.Normal, true, Ct));

        Assert.Equal(["first", "second mk4"], calls);
        (LogLevel level, Exception? exception, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Same(failure, exception);
        Assert.Contains("mk4", message, StringComparison.Ordinal);
        Assert.True((await _store.LoadAsync(Ct)).Settings.Printers[0].Camera.Window.TopmostNormal);
    }

    [Fact]
    public async Task WriterOverAStoreWithoutAFileWritesAndAnnouncesNothing()
    {
        CameraWindowTopmostWriter writer = new(new MemoryOnlyStore());
        int announced = 0;
        writer.TopmostSaved += (_, _) => announced++;

        Assert.False(await writer.SaveAsync("mk4", CameraWindowMode.Normal, true, Ct));
        Assert.Equal(0, announced);
    }

    [Fact]
    public async Task ConcurrentTopmostWritesAndSettingsSavesNeverLoseOtherValues()
    {
        AppSettings baseline = new() { Printers = [Printer("mk4"), Printer("xl")] };
        await _store.SaveAsync(baseline, Ct);
        CameraWindowTopmostWriter writer = new(_store);

        for (int i = 0; i < 30; i++)
        {
            bool topmost = i % 2 == 0;
            AppSettings window = baseline with { PollingInterval = TimeSpan.FromSeconds(10 + i) };

            await Task.WhenAll(
                Task.Run(() => writer.SaveAsync("mk4", CameraWindowMode.Normal, topmost, Ct), Ct),
                Task.Run(() => _store.SaveAsync(window, Ct), Ct));

            SettingsLoadResult loaded = await _store.LoadAsync(Ct);
            Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
            Assert.Empty(loaded.Warnings);
            Assert.Equal(window.PollingInterval, loaded.Settings.PollingInterval);
            Assert.Equal(CameraWindowDefaults.Default, loaded.Settings.Printers[1].Camera.Window);
            Assert.Equal(CameraWindowDefaults.Default.TopmostMini, loaded.Settings.Printers[0].Camera.Window.TopmostMini);
        }

        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    private static PrinterDefinition Printer(string id, CameraWindowDefaults? window = null) => new()
    {
        Id = id,
        Name = id.ToUpperInvariant(),
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.226",
        Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) { Window = window ?? CameraWindowDefaults.Default },
    };

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    private async Task<(CameraWindowTopmostStoreResult Written, string Result)> SetAsync(string original, string printerId, CameraWindowMode mode, bool topmost)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, original, Ct);
        CameraWindowTopmostStoreResult written = await _store.SetCameraWindowTopmostAsync(printerId, mode, topmost, Ct);
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
