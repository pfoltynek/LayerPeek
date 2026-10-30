using System.Text.Json;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// <c>printers[*].camera.miniStrip</c> (T-110, design T-104 6, Q9 A): only the items that are off are written, missing ones
/// are on, invalid values are on with a warning, and the old T-036 <c>overlay.mini</c> is unrelated.
/// </summary>
public sealed class SettingsMapperMiniStripTests : IDisposable
{
    private readonly string _directory = TestDirectories.NewDirectoryPath();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] KeyNames = ["progressBar", "percent", "remaining", "finish", "cooldown", "nameRow", "chartRow"];

    public static TheoryData<string> Keys => [.. KeyNames];

    public void Dispose() => TestDirectories.Delete(_directory);

    [Fact]
    public void DefaultHasEverythingOn()
    {
        CameraMiniStripSettings strip = CameraMiniStripSettings.Default;

        Assert.True(strip.ProgressBar);
        Assert.True(strip.Percent);
        Assert.True(strip.Remaining);
        Assert.True(strip.Finish);
        Assert.True(strip.Cooldown);
        Assert.True(strip.NameRow);
        Assert.True(strip.ChartRow);
        Assert.Equal(CameraMiniStripSettings.Default, new CameraMiniStripSettings());
        Assert.Same(CameraMiniStripSettings.Default, CameraSettings.Disabled.MiniStrip);
    }

    [Fact]
    public void MiniStripTakesPartInCameraEquality()
    {
        CameraSettings camera = new(enabled: true, new Uri("rtsp://h/live"));
        CameraSettings other = camera with { MiniStrip = new CameraMiniStripSettings { Percent = false } };

        Assert.NotEqual(camera, other);
        Assert.Equal(other, camera with { MiniStrip = new CameraMiniStripSettings { Percent = false } });
        Assert.Contains("Percent = False", other.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => camera with { MiniStrip = null! });
    }

    [Fact]
    public void AllOnIsNotWritten()
    {
        string json = Serialize(new AppSettings { Printers = [Printer(CameraMiniStripSettings.Default)] });

        Assert.DoesNotContain("miniStrip", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnlyTheItemsThatAreOffAreWritten()
    {
        using JsonDocument json = JsonDocument.Parse(Serialize(new AppSettings
        {
            Printers = [Printer(new CameraMiniStripSettings { Remaining = false, ChartRow = false })],
        }));

        JsonElement strip = json.RootElement.GetProperty("printers")[0].GetProperty("camera").GetProperty("miniStrip");
        Assert.Equal(
            ["remaining:False", "chartRow:False"],
            strip.EnumerateObject().Select(static property => $"{property.Name}:{property.Value.GetBoolean()}"));
    }

    public static TheoryData<CameraMiniStripSettings> RoundTripCases =>
    [
        CameraMiniStripSettings.Default,
        new CameraMiniStripSettings { ProgressBar = false },
        new CameraMiniStripSettings { Percent = false },
        new CameraMiniStripSettings { Remaining = false },
        new CameraMiniStripSettings { Finish = false },
        new CameraMiniStripSettings { Cooldown = false },
        new CameraMiniStripSettings { NameRow = false },
        new CameraMiniStripSettings { ChartRow = false },
        AllOff,
    ];

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void RoundTrips(CameraMiniStripSettings strip)
    {
        AppSettings settings = new() { Printers = [Printer(strip), Printer(CameraMiniStripSettings.Default, "b")] };

        AppSettings loaded = Load(Serialize(settings), out List<string> warnings, out List<string> notices);

        Assert.Empty(warnings);
        Assert.Empty(notices);
        Assert.Equal(settings, loaded);
        Assert.Equal(strip, loaded.Printers[0].Camera.MiniStrip);
        Assert.Same(CameraMiniStripSettings.Default, loaded.Printers[1].Camera.MiniStrip);
    }

    [Fact]
    public void KeptWhileTheCameraIsDisabled()
    {
        PrinterDefinition printer = Printer(AllOff) with { Camera = CameraSettings.Disabled with { MiniStrip = AllOff } };

        AppSettings loaded = Load(Serialize(new AppSettings { Printers = [printer] }), out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.False(loaded.Printers[0].Camera.Enabled);
        Assert.Equal(AllOff, loaded.Printers[0].Camera.MiniStrip);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"miniStrip\": {}")]
    [InlineData("\"miniStrip\": null")]
    [InlineData("\"miniStrip\": { \"percent\": true, \"futureItem\": false }")]
    public void MissingSectionOrKeysAreOnWithoutWarning(string section)
    {
        AppSettings settings = Load(CameraJson(section), out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.Same(CameraMiniStripSettings.Default, settings.Printers[0].Camera.MiniStrip);
    }

    [Fact]
    public void NamesAreReadCaseInsensitivelyAndTheLastDuplicateWins()
    {
        AppSettings settings = Load(
            CameraJson("\"MiniStrip\": { \"PERCENT\": false, \"ChartRow\": false, \"chartrow\": true, \"nameRow\": \"x\", \"nameRow\": false }"),
            out List<string> warnings,
            out _);

        Assert.Empty(warnings);
        Assert.Equal(new CameraMiniStripSettings { Percent = false, NameRow = false }, settings.Printers[0].Camera.MiniStrip);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void InvalidValueIsOnWithAWarningNamingOnlyTheKey(string key)
    {
        string others = string.Join(", ", KeyNames.Where(other => other != key).Select(static other => $"\"{other}\": false"));
        foreach (string value in new[] { "\"false\"", "0", "null", "{ \"x\": false }", "[false]" })
        {
            AppSettings settings = Load(CameraJson($"\"miniStrip\": {{ {others}, \"{key}\": {value} }}"), out List<string> warnings, out _);

            string warning = Assert.Single(warnings);
            Assert.Contains($"'{key}'", warning, StringComparison.Ordinal);
            Assert.Contains("'a'", warning, StringComparison.Ordinal);
            Assert.DoesNotContain(value, warning, StringComparison.Ordinal);
            // The invalid one is on; the other keys of the section still apply.
            Assert.Equal(AllOff, Turn(settings.Printers[0].Camera.MiniStrip, key, on: false));
            Assert.True(IsOn(settings.Printers[0].Camera.MiniStrip, key));
            // The rest of the file is still read.
            Assert.True(settings.Printers[0].Camera.Enabled);
            Assert.Equal("maker", settings.Printers[0].UserName);
        }
    }

    [Theory]
    [InlineData("\"off\"")]
    [InlineData("false")]
    [InlineData("42")]
    [InlineData("[ { \"percent\": false } ]")]
    public void SectionThatIsNoObjectTurnsEverythingOnWithOneWarning(string value)
    {
        AppSettings settings = Load(CameraJson($"\"miniStrip\": {value}"), out List<string> warnings, out _);

        string warning = Assert.Single(warnings);
        Assert.Contains("Mini preview strip", warning, StringComparison.Ordinal);
        Assert.Contains("'a'", warning, StringComparison.Ordinal);
        Assert.Same(CameraMiniStripSettings.Default, settings.Printers[0].Camera.MiniStrip);
        Assert.Equal("maker", settings.Printers[0].UserName);
    }

    [Fact]
    public void OldOverlayMiniIsNotTheStripAndItsConversionIsUnchanged()
    {
        const string overlay = "\"overlay\": { \"corner\": \"TopLeft\", \"normal\": [\"progress\"], \"mini\": [\"progress\"] }";
        AppSettings withStrip = Load(CameraJson($"{overlay}, \"miniStrip\": {{ \"percent\": false }}"), out List<string> warnings, out List<string> notices);
        AppSettings withoutStrip = Load(CameraJson(overlay), out List<string> warningsWithout, out List<string> noticesWithout);

        Assert.Equal(new CameraMiniStripSettings { Percent = false }, withStrip.Printers[0].Camera.MiniStrip);
        Assert.Same(CameraMiniStripSettings.Default, withoutStrip.Printers[0].Camera.MiniStrip);
        // The conversion of the T-036 overlay behaves as before, with or without the new section.
        Assert.Equal(CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopLeft), withStrip.Printers[0].Camera.Overlay);
        Assert.Equal(withoutStrip.Printers[0].Camera.Overlay, withStrip.Printers[0].Camera.Overlay);
        Assert.Equal(warningsWithout, warnings);
        Assert.Equal(noticesWithout, notices);
        Assert.Single(notices);

        // Saving writes the new section and never the old keys.
        string saved = Serialize(withStrip);
        using JsonDocument json = JsonDocument.Parse(saved);
        JsonElement camera = json.RootElement.GetProperty("printers")[0].GetProperty("camera");
        Assert.False(camera.GetProperty("overlay").TryGetProperty("mini", out _));
        Assert.False(camera.GetProperty("miniStrip").GetProperty("percent").GetBoolean());
    }

    [Theory]
    [InlineData("settings-v1-0.1.0-overlay.json")]
    [InlineData("settings-v2-0.3.0-cooldown.json")]
    [InlineData("settings-v1-without-camera.json")]
    [InlineData("settings-v1-camera-without-window.json")]
    public async Task OlderFilesLoadWithTheDefaultStripAndNoNewWarnings(string fixture)
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), Path.Combine(_directory, JsonSettingsStore.FileName));
        using JsonSettingsStore store = new(_directory);

        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.DoesNotContain(result.Warnings, static warning => warning.Contains("strip", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(result.Settings.Printers);
        Assert.All(result.Settings.Printers, static printer => Assert.Same(CameraMiniStripSettings.Default, printer.Camera.MiniStrip));
    }

    [Fact]
    public async Task StoreRoundTripsWithoutChangingTheVersion()
    {
        using JsonSettingsStore store = new(_directory);
        AppSettings settings = new()
        {
            Printers = [Printer(new CameraMiniStripSettings { Cooldown = false, NameRow = false }), Printer(AllOff, "b"), Printer(CameraMiniStripSettings.Default, "c")],
        };

        await store.SaveAsync(settings, Ct);
        SettingsLoadResult result = await store.LoadAsync(Ct);
        using JsonDocument saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, JsonSettingsStore.FileName), Ct));

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(settings, result.Settings);
        Assert.Equal(SettingsDocument.CurrentVersion, saved.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(2, SettingsDocument.CurrentVersion);
    }

    private static CameraMiniStripSettings AllOff => new()
    {
        ProgressBar = false,
        Percent = false,
        Remaining = false,
        Finish = false,
        Cooldown = false,
        NameRow = false,
        ChartRow = false,
    };

    private static CameraMiniStripSettings Turn(CameraMiniStripSettings strip, string key, bool on) => key switch
    {
        "progressBar" => strip with { ProgressBar = on },
        "percent" => strip with { Percent = on },
        "remaining" => strip with { Remaining = on },
        "finish" => strip with { Finish = on },
        "cooldown" => strip with { Cooldown = on },
        "nameRow" => strip with { NameRow = on },
        "chartRow" => strip with { ChartRow = on },
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private static bool IsOn(CameraMiniStripSettings strip, string key) => Turn(strip, key, on: false) != strip;

    private static string CameraJson(string extra) =>
        $$"""
        { "version": 2, "printers": [ { "id": "a", "connectionType": "PrusaLink", "address": "h", "userName": "maker",
          "camera": { "enabled": true, "streamUrl": "rtsp://h/live"{{(extra.Length > 0 ? ", " + extra : string.Empty)}} } } ] }
        """;

    private static PrinterDefinition Printer(CameraMiniStripSettings strip, string id = "a") => new()
    {
        Id = id,
        Name = id,
        ConnectionType = ConnectionType.PrusaLink,
        Address = "h",
        Camera = new CameraSettings(enabled: true, new Uri("rtsp://h/live")) { MiniStrip = strip },
    };

    private static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);

    private static AppSettings Load(string json, out List<string> warnings, out List<string> notices)
    {
        SettingsDocument document = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsDocument)!;
        warnings = [];
        notices = [];
        return SettingsMapper.ToSettings(document, warnings, notices);
    }
}
