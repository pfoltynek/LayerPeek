using System.Text.Json;

using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// <c>printers[*].cooldownNozzleThresholdCelsius</c> / <c>cooldownBedThresholdCelsius</c> (T-109) in the settings file, and
/// the former single <c>cooldownThresholdCelsius</c> (T-079), which is discarded.
/// </summary>
public sealed class SettingsMapperCooldownThresholdTests : IDisposable
{
    private const string Nozzle = "cooldownNozzleThresholdCelsius";

    private const string Bed = "cooldownBedThresholdCelsius";

    private readonly string _directory = TestDirectories.NewDirectoryPath();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Fields => [Nozzle, Bed];

    public void Dispose() => TestDirectories.Delete(_directory);

    [Fact]
    public void DefaultsAreNotWritten()
    {
        string json = Serialize(new AppSettings { Printers = [Printer()] });

        Assert.DoesNotContain("cooldown", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OtherValuesAreWrittenAsWholeNumbersEachOnlyWhenNotTheDefault()
    {
        using JsonDocument json = JsonDocument.Parse(Serialize(new AppSettings
        {
            Printers = [Printer() with { CooldownThresholds = new(65, 40) }, Printer("b") with { CooldownThresholds = new(40, 55) }],
        }));

        JsonElement printers = json.RootElement.GetProperty("printers");
        Assert.Equal("65", printers[0].GetProperty(Nozzle).GetRawText());
        Assert.False(printers[0].TryGetProperty(Bed, out _));
        Assert.False(printers[1].TryGetProperty(Nozzle, out _));
        Assert.Equal("55", printers[1].GetProperty(Bed).GetRawText());
        Assert.All(printers.EnumerateArray(), static printer => Assert.False(printer.TryGetProperty("cooldownThresholdCelsius", out _)));
    }

    [Theory]
    [InlineData(30, 100)]
    [InlineData(31, 99)]
    [InlineData(50, 40)]
    [InlineData(40, 50)]
    [InlineData(100, 30)]
    public void ThresholdsRoundTrip(int nozzle, int bed)
    {
        AppSettings settings = new() { Printers = [Printer() with { CooldownThresholds = new(nozzle, bed) }, Printer("b")] };

        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
        Assert.Equal(new CooldownThresholds(nozzle, bed), loaded.Printers[0].CooldownThresholds);
        Assert.Equal(CooldownThresholds.Default, loaded.Printers[1].CooldownThresholds);
    }

    [Theory]
    [InlineData("80", 80)]
    [InlineData("\"80\"", 80)]
    [InlineData("80.0", 80)]
    [InlineData("null", 40)]
    public void ValidValuesLoadWithoutWarning(string value, int expected)
    {
        AppSettings nozzle = Load(PrinterJson($"\"{Nozzle}\": {value}"), out List<string> nozzleWarnings);
        AppSettings bed = Load(PrinterJson($"\"{Bed}\": {value}"), out List<string> bedWarnings);

        Assert.Empty(nozzleWarnings);
        Assert.Empty(bedWarnings);
        Assert.Equal(new CooldownThresholds(expected, 40), nozzle.Printers[0].CooldownThresholds);
        Assert.Equal(new CooldownThresholds(40, expected), bed.Printers[0].CooldownThresholds);
    }

    [Fact]
    public void NamesAreReadCaseInsensitively()
    {
        AppSettings settings = Load(PrinterJson("\"CooldownNozzleThresholdCelsius\": 33, \"COOLDOWNBEDTHRESHOLDCELSIUS\": 34"), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(new CooldownThresholds(33, 34), settings.Printers[0].CooldownThresholds);
    }

    [Theory]
    [InlineData("29")]
    [InlineData("101")]
    [InlineData("0")]
    [InlineData("-40")]
    [InlineData("55.5")]
    [InlineData("1e300")]
    public void InvalidValueFallsBackToTheDefaultWithWarning(string value)
    {
        AppSettings settings = Load(PrinterJson($"\"{Nozzle}\": {value}, \"{Bed}\": 60, \"userName\": \"maker\""), out List<string> warnings);

        // Only the invalid one falls back; the other is kept.
        Assert.Equal(new CooldownThresholds(40, 60), settings.Printers[0].CooldownThresholds);
        string warning = Assert.Single(warnings);
        Assert.Contains("'a'", warning, StringComparison.Ordinal);
        Assert.Contains("nozzle", warning, StringComparison.Ordinal);
        Assert.Contains("30–100", warning, StringComparison.Ordinal);
        // The rest of the entry is still loaded.
        Assert.Equal("maker", settings.Printers[0].UserName);
    }

    [Fact]
    public void InvalidBedValueWarnsAboutTheBed()
    {
        AppSettings settings = Load(PrinterJson($"\"{Nozzle}\": 45, \"{Bed}\": 5"), out List<string> warnings);

        Assert.Equal(new CooldownThresholds(45, 40), settings.Printers[0].CooldownThresholds);
        Assert.Contains("bed", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void WrongJsonTypeIsACorruptedFile(string field) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(PrinterJson($"\"{field}\": \"warm\""), SettingsJsonContext.Default.SettingsDocument));

    [Fact]
    public void SkippedEntryProducesNoThresholdWarning()
    {
        Load($$"""{ "printers": [ { "id": "a", "connectionType": "Bogus", "address": "h", "{{Nozzle}}": 5, "{{Bed}}": 5 } ] }""", out List<string> warnings);

        Assert.Contains("skipped", Assert.Single(warnings), StringComparison.Ordinal);
    }

    // ---- The former single threshold (0.3.0 and older, T-079) ----

    [Theory]
    [InlineData("65")]
    [InlineData("50")]
    [InlineData("30")]
    [InlineData("100")]
    [InlineData("29")]
    [InlineData("55.5")]
    public void FormerThresholdIsDiscardedWithANoticeAndNoWarning(string value)
    {
        AppSettings settings = Load(PrinterJson($"\"cooldownThresholdCelsius\": {value}"), out List<string> warnings, out List<string> notices);

        Assert.Empty(warnings);
        Assert.Equal(CooldownThresholds.Default, settings.Printers[0].CooldownThresholds);
        string notice = Assert.Single(notices);
        Assert.Contains("1 printer ", notice, StringComparison.Ordinal);
        Assert.Contains("40 / 40", notice, StringComparison.Ordinal);
        // Without the value.
        Assert.DoesNotContain(value, notice.Replace("40 / 40", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"warm\"")]
    [InlineData("{ \"x\": 1 }")]
    [InlineData("[1, 2]")]
    [InlineData("true")]
    public void FormerThresholdOfAnyJsonTypeNeverMakesTheFileUnreadable(string value)
    {
        AppSettings settings = Load(PrinterJson($"\"cooldownThresholdCelsius\": {value}"), out List<string> warnings, out List<string> notices);

        Assert.Empty(warnings);
        Assert.Single(notices);
        Assert.Equal(CooldownThresholds.Default, settings.Printers[0].CooldownThresholds);
    }

    [Fact]
    public void FormerThresholdNullIsNoNotice()
    {
        Load(PrinterJson("\"cooldownThresholdCelsius\": null"), out List<string> warnings, out List<string> notices);

        Assert.Empty(warnings);
        Assert.Empty(notices);
    }

    [Fact]
    public void NewThresholdsWinOverTheFormerOne()
    {
        AppSettings settings = Load(PrinterJson($"\"cooldownThresholdCelsius\": 70, \"{Nozzle}\": 45, \"{Bed}\": 35"), out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.Equal(new CooldownThresholds(45, 35), settings.Printers[0].CooldownThresholds);
    }

    [Fact]
    public void SeveralPrintersWithTheFormerThresholdGiveOneNotice()
    {
        string json = """
            { "version": 2, "printers": [
              { "id": "a", "connectionType": "PrusaLink", "address": "h", "cooldownThresholdCelsius": 60 },
              { "id": "b", "connectionType": "PrusaLink", "address": "h2" },
              { "id": "c", "connectionType": "PrusaLink", "address": "h3", "cooldownThresholdCelsius": 45 } ] }
            """;

        AppSettings settings = Load(json, out List<string> warnings, out List<string> notices);

        Assert.Empty(warnings);
        Assert.Contains("2 printers", Assert.Single(notices), StringComparison.Ordinal);
        Assert.All(settings.Printers, static printer => Assert.Equal(CooldownThresholds.Default, printer.CooldownThresholds));
    }

    [Fact]
    public async Task FileOfVersion030LoadsWithTheNewDefaultsAndIsSavedWithoutTheFormerThreshold()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, JsonSettingsStore.FileName);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-v2-0.3.0-cooldown.json"), path);
        using JsonSettingsStore store = new(_directory);

        SettingsLoadResult first = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, first.Status);
        Assert.Empty(first.Warnings);
        Assert.Contains("2 printers", Assert.Single(first.Notices), StringComparison.Ordinal);
        Assert.Equal(3, first.Settings.Printers.Count);
        Assert.All(first.Settings.Printers, static printer => Assert.Equal(CooldownThresholds.Default, printer.CooldownThresholds));
        // Everything else of the file is read as before.
        Assert.Equal("cs", first.Settings.Language);
        Assert.True(first.Settings.Notifications.CooledDown);
        Assert.Equal("maker", first.Settings.Printers[0].UserName);

        await store.SaveAsync(first.Settings, Ct);
        string saved = await File.ReadAllTextAsync(path, Ct);
        SettingsLoadResult second = await store.LoadAsync(Ct);

        Assert.DoesNotContain("cooldown", saved.Replace("cooledDown", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(second.Warnings);
        Assert.Empty(second.Notices);
        Assert.Equal(first.Settings, second.Settings);
    }

    [Theory]
    [InlineData("settings-v1-without-camera.json")]
    [InlineData("settings-v1-without-username.json")]
    [InlineData("settings-v1-camera-without-window.json")]
    public async Task OlderFilesLoadTheDefaults(string fixture)
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), Path.Combine(_directory, JsonSettingsStore.FileName));
        using JsonSettingsStore store = new(_directory);

        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.NotEmpty(result.Settings.Printers);
        Assert.All(result.Settings.Printers, printer => Assert.Equal(CooldownThresholds.Default, printer.CooldownThresholds));
    }

    [Fact]
    public async Task StoreRoundTripsTheThresholds()
    {
        using JsonSettingsStore store = new(_directory);
        AppSettings settings = new()
        {
            Printers =
            [
                Printer() with { CooldownThresholds = new(30, 100) },
                Printer("b") with { CooldownThresholds = new(100, 30) },
                Printer("c"),
            ],
        };

        await store.SaveAsync(settings, Ct);
        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Notices);
        Assert.Equal(settings, result.Settings);
    }

    private static string PrinterJson(string extra) =>
        $$"""{ "version": 2, "printers": [ { "id": "a", "connectionType": "PrusaLink", "address": "h", {{extra}} } ] }""";

    private static PrinterDefinition Printer(string id = "a") =>
        new() { Id = id, Name = id, ConnectionType = ConnectionType.PrusaLink, Address = "h" };

    private static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);

    private static AppSettings RoundTrip(AppSettings settings, out List<string> warnings) => Load(Serialize(settings), out warnings);

    private static AppSettings Load(string json, out List<string> warnings) => Load(json, out warnings, out _);

    private static AppSettings Load(string json, out List<string> warnings, out List<string> notices)
    {
        SettingsDocument document = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsDocument)!;
        warnings = [];
        notices = [];
        return SettingsMapper.ToSettings(document, warnings, notices);
    }
}
