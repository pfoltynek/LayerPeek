using System.Text.Json;

using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

namespace LayerPeek.Core.Tests.Settings;

/// <summary><c>printers[*].cooldownThresholdCelsius</c> (T-079) in the settings file.</summary>
public sealed class SettingsMapperCooldownThresholdTests : IDisposable
{
    private readonly string _directory = TestDirectories.NewDirectoryPath();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_directory);

    [Fact]
    public void DefaultIsNotWritten()
    {
        string json = Serialize(new AppSettings { Printers = [Printer()] });

        Assert.DoesNotContain("cooldown", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OtherValueIsWrittenAsAWholeNumber()
    {
        using JsonDocument json = JsonDocument.Parse(Serialize(new AppSettings { Printers = [Printer() with { CooldownThresholdCelsius = 65 }] }));

        JsonElement value = json.RootElement.GetProperty("printers")[0].GetProperty("cooldownThresholdCelsius");
        Assert.Equal(JsonValueKind.Number, value.ValueKind);
        Assert.Equal("65", value.GetRawText());
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    public void ThresholdRoundTrips(int celsius)
    {
        AppSettings settings = new() { Printers = [Printer() with { CooldownThresholdCelsius = celsius }, Printer("b")] };

        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
        Assert.Equal(celsius, loaded.Printers[0].CooldownThresholdCelsius);
        Assert.Equal(CooldownThreshold.DefaultCelsius, loaded.Printers[1].CooldownThresholdCelsius);
    }

    [Theory]
    [InlineData("80", 80)]
    [InlineData("\"80\"", 80)]
    [InlineData("80.0", 80)]
    [InlineData("null", 50)]
    public void ValidValuesLoadWithoutWarning(string value, int expected)
    {
        AppSettings settings = Load(PrinterJson($"\"cooldownThresholdCelsius\": {value}"), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(expected, settings.Printers[0].CooldownThresholdCelsius);
    }

    [Fact]
    public void NameIsReadCaseInsensitively()
    {
        AppSettings settings = Load(PrinterJson("\"CooldownThresholdCelsius\": 33"), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(33, settings.Printers[0].CooldownThresholdCelsius);
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
        AppSettings settings = Load(PrinterJson($"\"cooldownThresholdCelsius\": {value}, \"userName\": \"maker\""), out List<string> warnings);

        Assert.Equal(CooldownThreshold.DefaultCelsius, settings.Printers[0].CooldownThresholdCelsius);
        string warning = Assert.Single(warnings);
        Assert.Contains("'a'", warning, StringComparison.Ordinal);
        Assert.Contains("30–100", warning, StringComparison.Ordinal);
        // The rest of the entry is still loaded.
        Assert.Equal("maker", settings.Printers[0].UserName);
    }

    [Fact]
    public void WrongJsonTypeIsACorruptedFile() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(PrinterJson("\"cooldownThresholdCelsius\": \"warm\""), SettingsJsonContext.Default.SettingsDocument));

    [Fact]
    public void SkippedEntryProducesNoThresholdWarning()
    {
        Load("""{ "printers": [ { "id": "a", "connectionType": "Bogus", "address": "h", "cooldownThresholdCelsius": 5 } ] }""", out List<string> warnings);

        Assert.Contains("skipped", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("settings-v1-without-camera.json")]
    [InlineData("settings-v1-without-username.json")]
    [InlineData("settings-v1-camera-without-window.json")]
    public async Task OlderFilesLoadTheDefault(string fixture)
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), Path.Combine(_directory, JsonSettingsStore.FileName));
        using JsonSettingsStore store = new(_directory);

        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.NotEmpty(result.Settings.Printers);
        Assert.All(result.Settings.Printers, printer => Assert.Equal(CooldownThreshold.DefaultCelsius, printer.CooldownThresholdCelsius));
    }

    [Fact]
    public async Task StoreRoundTripsTheThreshold()
    {
        using JsonSettingsStore store = new(_directory);
        AppSettings settings = new()
        {
            Printers = [Printer() with { CooldownThresholdCelsius = 30 }, Printer("b") with { CooldownThresholdCelsius = 100 }, Printer("c")],
        };

        await store.SaveAsync(settings, Ct);
        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(settings, result.Settings);
    }

    private static string PrinterJson(string extra) =>
        $$"""{ "version": 1, "printers": [ { "id": "a", "connectionType": "PrusaLink", "address": "h", {{extra}} } ] }""";

    private static PrinterDefinition Printer(string id = "a") =>
        new() { Id = id, Name = id, ConnectionType = ConnectionType.PrusaLink, Address = "h" };

    private static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);

    private static AppSettings RoundTrip(AppSettings settings, out List<string> warnings) => Load(Serialize(settings), out warnings);

    private static AppSettings Load(string json, out List<string> warnings)
    {
        SettingsDocument document = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsDocument)!;
        warnings = [];
        return SettingsMapper.ToSettings(document, warnings);
    }
}
