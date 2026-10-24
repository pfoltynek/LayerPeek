using System.Text;
using System.Text.Json;

using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

namespace LayerPeek.Core.Tests.Settings;

/// <summary><c>printers[*].detectedModel</c> and <c>printers[*].image</c> (T-039) in the settings file.</summary>
public sealed class SettingsMapperPrinterImageTests : IDisposable
{
    private const string Garbage = "GARBAGE-VALUE";

    private readonly string _directory = TestDirectories.NewDirectoryPath();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_directory);

    [Fact]
    public void DefaultsAreNotWritten()
    {
        string json = Serialize(new AppSettings { Printers = [Printer()] });

        Assert.DoesNotContain("detectedModel", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"image\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"version\": 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectedModelAndImageAreWritten()
    {
        AppSettings settings = new()
        {
            Printers = [Printer() with { DetectedModel = new PrinterModelCode(1, 3, 10), Image = PrinterImageChoice.ForModel(PrinterModel.CoreOneL) }],
        };

        using JsonDocument json = JsonDocument.Parse(Serialize(settings));

        JsonElement printer = json.RootElement.GetProperty("printers")[0];
        Assert.Equal("1.3.10", printer.GetProperty("detectedModel").GetString());
        Assert.Equal("Model", printer.GetProperty("image").GetProperty("mode").GetString());
        Assert.Equal("CoreOneL", printer.GetProperty("image").GetProperty("model").GetString());
    }

    [Fact]
    public void CustomImageIsWrittenWithoutModelOrPath()
    {
        string json = Serialize(new AppSettings { Printers = [Printer() with { Image = PrinterImageChoice.Custom }] });

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement image = document.RootElement.GetProperty("printers")[0].GetProperty("image");
        Assert.Equal("Custom", image.GetProperty("mode").GetString());
        Assert.False(image.TryGetProperty("model", out _));
        Assert.DoesNotContain(".png", json, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<PrinterImageChoice> Choices =>
    [
        PrinterImageChoice.Automatic,
        PrinterImageChoice.Custom,
        .. PrinterModels.Selectable.Select(PrinterImageChoice.ForModel),
    ];

    [Theory]
    [MemberData(nameof(Choices))]
    public void ImageChoiceRoundTrips(PrinterImageChoice choice)
    {
        AppSettings settings = new() { Printers = [Printer() with { Image = choice, DetectedModel = new PrinterModelCode(7, 1, 0) }] };

        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void FileWithoutTheNewFieldsLoadsDefaults()
    {
        AppSettings settings = Load("""{ "version": 1, "printers": [ { "id": "a", "connectionType": "PrusaLink", "address": "h" } ] }""", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Null(settings.Printers[0].DetectedModel);
        Assert.Same(PrinterImageChoice.Automatic, settings.Printers[0].Image);
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "mode": null }""")]
    [InlineData("""{ "mode": "Automatic" }""")]
    [InlineData("""{ "mode": " automatic ", "model": "Mk4" }""")]
    public void AutomaticVariantsLoadWithoutWarning(string image)
    {
        AppSettings settings = Load(PrinterJson($"\"image\": {image}"), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Same(PrinterImageChoice.Automatic, settings.Printers[0].Image);
    }

    [Theory]
    [InlineData("""{ "mode": "model", "model": "mk4s" }""", PrinterModel.Mk4S)]
    [InlineData("""{ "Mode": "MODEL", "Model": " XlPlus " }""", PrinterModel.XlPlus)]
    public void ModelChoiceIsReadLeniently(string image, PrinterModel expected)
    {
        AppSettings settings = Load(PrinterJson($"\"image\": {image}"), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(PrinterImageChoice.ForModel(expected), settings.Printers[0].Image);
    }

    [Theory]
    [InlineData($$"""{ "mode": "{{Garbage}}" }""")]
    [InlineData("""{ "mode": "1" }""")]
    [InlineData($$"""{ "mode": "Model", "model": "{{Garbage}}" }""")]
    [InlineData("""{ "mode": "Model", "model": "Unknown" }""")]
    [InlineData("""{ "mode": "Model", "model": "6" }""")]
    [InlineData("""{ "mode": "Model" }""")]
    public void InvalidImageFallsBackToAutomaticWithWarning(string image)
    {
        AppSettings settings = Load(PrinterJson($"\"image\": {image}, \"detectedModel\": \"1.4.0\""), out List<string> warnings);

        Assert.Same(PrinterImageChoice.Automatic, settings.Printers[0].Image);
        string warning = Assert.Single(warnings);
        Assert.Contains("Image of printer 'a'", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(Garbage, warning, StringComparison.Ordinal);
        // The rest of the entry is still loaded.
        Assert.Equal(new PrinterModelCode(1, 4, 0), settings.Printers[0].DetectedModel);
    }

    [Theory]
    [InlineData(Garbage)]
    [InlineData("1.4")]
    [InlineData("1.4.999")]
    [InlineData("")]
    public void InvalidDetectedModelIsDroppedWithWarning(string value)
    {
        AppSettings settings = Load(PrinterJson($"\"detectedModel\": \"{value}\", \"image\": {{ \"mode\": \"Custom\" }}"), out List<string> warnings);

        Assert.Null(settings.Printers[0].DetectedModel);
        string warning = Assert.Single(warnings);
        Assert.Contains("detected model", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(Garbage, warning, StringComparison.Ordinal);
        Assert.Same(PrinterImageChoice.Custom, settings.Printers[0].Image);
    }

    [Fact]
    public void UnknownButWellFormedCodeIsKept()
    {
        // A newer firmware may report a model this version has no illustration for; a later app version may know it.
        AppSettings settings = Load(PrinterJson("\"detectedModel\": \"9.1.0\""), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(new PrinterModelCode(9, 1, 0), settings.Printers[0].DetectedModel);
        Assert.Contains("\"detectedModel\": \"9.1.0\"", Serialize(settings), StringComparison.Ordinal);
    }

    [Fact]
    public void WarningForSkippedEntryIsNotDuplicatedByImageWarnings()
    {
        Load("""{ "printers": [ { "id": "a", "connectionType": "Bogus", "address": "h", "image": { "mode": "x" }, "detectedModel": "x" } ] }""", out List<string> warnings);

        Assert.Contains("skipped", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void WrongJsonTypeIsACorruptedFile()
    {
        // Same rule as for every other value: a value of the wrong JSON type fails the whole file.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(PrinterJson("\"detectedModel\": 140"), SettingsJsonContext.Default.SettingsDocument));
    }

    [Fact]
    public async Task StoreRoundTripsTheNewFields()
    {
        using JsonSettingsStore store = new(_directory);
        AppSettings settings = new()
        {
            Printers =
            [
                Printer() with { DetectedModel = new PrinterModelCode(1, 4, 1), Image = PrinterImageChoice.Custom },
                Printer("b") with { Image = PrinterImageChoice.ForModel(PrinterModel.Mini) },
            ],
        };

        await store.SaveAsync(settings, Ct);
        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(settings, result.Settings);
    }

    [Fact]
    public async Task CredentialScrubberLeavesImageFieldsUntouched()
    {
        const string Original = """
            { "version": 1, "printers": [ {
                "id": "a", "connectionType": "PrusaLink", "address": "h",
                "detectedModel": "1.4.1",
                "image": { "mode": "Model", "model": "Mk4S", "streamUrl": "rtsp://u:p@10.0.0.6/live", "enabled": true },
                "camera": { "enabled": true, "streamUrl": "rtsp://u:p@10.0.0.6/live" } } ] }
            """;
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, JsonSettingsStore.FileName);
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(Original), Ct);
        using JsonSettingsStore store = new(_directory);

        IReadOnlyList<string> changed = await store.RemoveCameraCredentialsAsync(Ct);

        Assert.Equal(["a"], changed);
        string expected = Original.Replace(
            "\"camera\": { \"enabled\": true, \"streamUrl\": \"rtsp://u:p@10.0.0.6/live\" }",
            "\"camera\": { \"enabled\": false, \"streamUrl\": \"rtsp://10.0.0.6/live\" }",
            StringComparison.Ordinal);
        Assert.Equal(expected, await File.ReadAllTextAsync(path, Ct));
        SettingsLoadResult loaded = await store.LoadAsync(Ct);
        Assert.Equal(new PrinterModelCode(1, 4, 1), loaded.Settings.Printers[0].DetectedModel);
        Assert.Equal(PrinterImageChoice.ForModel(PrinterModel.Mk4S), loaded.Settings.Printers[0].Image);
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
