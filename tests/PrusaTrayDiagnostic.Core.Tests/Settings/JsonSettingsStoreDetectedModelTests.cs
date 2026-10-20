using System.Text;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.Core.Tests.Settings;

/// <summary>
/// <see cref="JsonSettingsStore.SetDetectedModelAsync"/> on raw JSON (T-039): only the detected model of the printer
/// changes, byte for byte; everything else (skipped entries, unknown values, comments, formatting) stays.
/// </summary>
public sealed class JsonSettingsStoreDetectedModelTests : IDisposable
{
    private const string Address = "10.0.0.5";

    /// <summary>Address and connection type the code was detected for; "@M" in the JSON of a test.</summary>
    private const string Machine = "\"address\": \"10.0.0.5\", \"connectionType\": \"PrusaLink\"";

    private static readonly PrinterModelCode Mk4S = new(1, 4, 1);

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly JsonSettingsStore _store;

    public JsonSettingsStoreDetectedModelTests()
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

    [Fact]
    public async Task MissingPropertyIsAddedAfterTheLastValueIndentedLikeTheFirst()
    {
        string original = Lines(
            "{",
            "  \"version\": 1,",
            "  \"printers\": [",
            "    {",
            "      \"id\": \"a\", @M,",
            "      \"name\": \"Alpha\",",
            "      \"camera\": { \"enabled\": false }",
            "    }",
            "  ]",
            "}");

        (bool written, string result) = await SetAsync(original, "a", Mk4S);

        Assert.True(written);
        Assert.Equal(
            Lines(
                "{",
                "  \"version\": 1,",
                "  \"printers\": [",
                "    {",
                "      \"id\": \"a\", @M,",
                "      \"name\": \"Alpha\",",
                "      \"camera\": { \"enabled\": false },",
                "      \"detectedModel\": \"1.4.1\"",
                "    }",
                "  ]",
                "}"),
            result);
    }

    [Fact]
    public async Task ExistingValueIsReplacedInPlace()
    {
        const string Original = """{ "printers": [ { "id": "a", @M, "detectedModel": "1.4.0", "name": "A" } ] }""";

        (bool written, string result) = await SetAsync(Original, "a", Mk4S);

        Assert.True(written);
        Assert.Equal("""{ "printers": [ { "id": "a", @M, "detectedModel": "1.4.1", "name": "A" } ] }""", result);
    }

    [Fact]
    public async Task ValueOfAnyTypeIsReplacedAndDuplicatesAllChange()
    {
        const string Original = """{ "printers": [ { "detectedModel": null, "id": "a", @M, "DETECTEDMODEL": { "x": [1] } } ] }""";

        (bool written, string result) = await SetAsync(Original, "a", Mk4S);

        Assert.True(written);
        Assert.Equal("""{ "printers": [ { "detectedModel": "1.4.1", "id": "a", @M, "DETECTEDMODEL": "1.4.1" } ] }""", result);
    }

    [Fact]
    public async Task SameValueWritesNothing()
    {
        const string Original = """{ "printers": [ { "id": "a", @M, "detectedModel": "1.4.1" } ] }""";
        await WriteFileAsync(Encoding.UTF8.GetBytes(Original));
        DateTime before = File.GetLastWriteTimeUtc(FilePath);

        bool written = await _store.SetDetectedModelAsync("a", Mk4S, Address, ConnectionType.PrusaLink, Ct);

        Assert.False(written);
        Assert.Equal(Original, await ReadFileAsync());
        Assert.Equal(before, File.GetLastWriteTimeUtc(FilePath));
    }

    [Fact]
    public async Task OnlyTheMatchingPrinterChangesAndIdsAreComparedLikeTheLoader()
    {
        const string Original = """{ "printers": [ { "id": "b", "name": "B" }, { "id": " A ", @M, "name": "A" }, { "name": "no id" }, { "id": "a", @M, "bad": true } ] }""";

        (bool written, string result) = await SetAsync(Original, "a", Mk4S);

        Assert.True(written);
        Assert.Equal(
            """{ "printers": [ { "id": "b", "name": "B" }, { "id": " A ", @M, "name": "A", "detectedModel": "1.4.1" }, { "name": "no id" }, { "id": "a", @M, "bad": true, "detectedModel": "1.4.1" } ] }""",
            result);
    }

    [Theory]
    [InlineData("""{ "printers": [ { "id": "a", "address": "10.0.0.99", "connectionType": "PrusaLink" } ] }""")]
    [InlineData("""{ "printers": [ { "id": "a", "address": "10.0.0.5", "connectionType": "PrusaConnect" } ] }""")]
    [InlineData("""{ "printers": [ { "id": "a", "address": "10.0.0.5" } ] }""")]
    [InlineData("""{ "printers": [ { "id": "a", "connectionType": "PrusaLink" } ] }""")]
    public async Task EntryOfAnotherMachineIsLeftAlone(string original)
    {
        // The file changed meanwhile (another address or connection type): the code belongs to the previous machine.
        (bool written, string result) = await SetAsync(original, "a", Mk4S);

        Assert.False(written);
        Assert.Equal(original, result);
    }

    [Fact]
    public async Task MachineIsComparedLikeTheLoader()
    {
        const string Original = """{ "printers": [ { "id": "a", "address": " 10.0.0.5 ", "connectionType": " prusalink " } ] }""";

        (bool written, string result) = await SetAsync(Original, "a", Mk4S);

        Assert.True(written);
        Assert.Equal("""{ "printers": [ { "id": "a", "address": " 10.0.0.5 ", "connectionType": " prusalink ", "detectedModel": "1.4.1" } ] }""", result);
    }

    [Fact]
    public async Task LoadListsTheIdsOfEveryEntryAlsoSkippedOnes()
    {
        // T-039: data of a printer the loader skipped (here: unknown connection type, missing address) must survive.
        const string Original = """{ "printers": [ { "id": " a ", @M }, { "id": "b", "address": "x", "connectionType": "Serial" }, { "id": "c", "connectionType": "PrusaLink" }, { "name": "no id" }, { "id": "  " } ] }""";
        await WriteFileAsync(Encoding.UTF8.GetBytes(Original));

        SettingsLoadResult loaded = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.Equal(["a"], loaded.Settings.Printers.Select(static printer => printer.Id));
        Assert.Equal(["a", "b", "c"], loaded.StoredPrinterIds);
    }

    [Fact]
    public async Task UnknownPrinterLeavesTheFileAlone()
    {
        const string Original = """{ "printers": [ { "id": "a", @M } ] }""";

        (bool written, string result) = await SetAsync(Original, "zzz", Mk4S);

        Assert.False(written);
        Assert.Equal(Original, result);
    }

    [Fact]
    public async Task NestedPrintersAndOtherIdsAreNotPrinters()
    {
        // Only root.printers[*] are printers; an "id" deeper inside (camera) does not count.
        const string Original = """{ "other": { "printers": [ { "id": "a", @M } ] }, "printers": [ { "id": "b", "camera": { "id": "a", @M } } ] }""";

        (bool written, string result) = await SetAsync(Original, "a", Mk4S);

        Assert.False(written);
        Assert.Equal(Original, result);
    }

    [Fact]
    public async Task CommentsTrailingCommasAndUnknownValuesSurvive()
    {
        string original = Lines(
            "// my printers",
            "{",
            "  \"future\": [1, 2],",
            "  \"printers\": [",
            "    { /* first */ \"id\": \"a\", @M, \"x\": { \"y\": 1 }, },",
            "  ],",
            "}");

        (bool written, string result) = await SetAsync(original, "a", Mk4S);

        Assert.True(written);
        Assert.Equal(original.Replace("\"x\": { \"y\": 1 },", "\"x\": { \"y\": 1 }, \"detectedModel\": \"1.4.1\",", StringComparison.Ordinal), result);
        // Still a file the loader reads.
        SettingsLoadResult loaded = await _store.LoadAsync(Ct);
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
    }

    [Fact]
    public async Task ByteOrderMarkIsPreserved()
    {
        const string Original = """{ "printers": [ { "id": "a", @M } ] }""";
        await WriteFileAsync([.. Bom, .. Encoding.UTF8.GetBytes(Original)]);

        bool written = await _store.SetDetectedModelAsync("a", Mk4S, Address, ConnectionType.PrusaLink, Ct);

        byte[] bytes = await File.ReadAllBytesAsync(FilePath, Ct);
        Assert.True(written);
        Assert.Equal(Bom, bytes[..3]);
        Assert.Equal("""{ "printers": [ { "id": "a", @M, "detectedModel": "1.4.1" } ] }""", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Replace(Machine, "@M", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SavedSettingsRoundTripWithTheCode()
    {
        PrinterDefinition printer = new() { Id = "a", Name = "Alpha", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" };
        await _store.SaveAsync(AppSettings.Default with { Printers = [printer] }, Ct);

        Assert.True(await _store.SetDetectedModelAsync("A", Mk4S, "192.168.1.20", ConnectionType.PrusaLink, Ct));

        SettingsLoadResult loaded = await _store.LoadAsync(Ct);
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(printer with { DetectedModel = Mk4S }, Assert.Single(loaded.Settings.Printers));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task MissingFileIsNotCreated()
    {
        Assert.False(await _store.SetDetectedModelAsync("a", Mk4S, Address, ConnectionType.PrusaLink, Ct));

        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData("{ \"printers\": [ { \"id\": \"a\" } ")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task InvalidJsonIsLeftAlone(string original)
    {
        (bool written, string result) = await SetAsync(original, "a", Mk4S);

        Assert.False(written);
        Assert.Equal(original, result);
    }

    [Fact]
    public async Task FileOfWrongTypesIsStillUpdatedLikeTheScrubberDoes()
    {
        // Valid JSON with a value of the wrong type elsewhere: the loader reports it corrupted, but the change is local.
        const string Original = """{ "pollingIntervalSeconds": "x", "printers": [ { "id": "a", @M } ] }""";

        (bool written, string result) = await SetAsync(Original, "a", Mk4S);

        Assert.True(written);
        Assert.Equal("""{ "pollingIntervalSeconds": "x", "printers": [ { "id": "a", @M, "detectedModel": "1.4.1" } ] }""", result);
    }

    [Fact]
    public async Task CancelledBeforeWritingLeavesTheFile()
    {
        const string Original = """{ "printers": [ { "id": "a", @M } ] }""";
        await WriteFileAsync(Encoding.UTF8.GetBytes(Original));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.SetDetectedModelAsync("a", Mk4S, Address, ConnectionType.PrusaLink, new CancellationToken(canceled: true)));

        Assert.Equal(Original, await ReadFileAsync());
    }

    [Fact]
    public async Task StoreWithoutAFileWritesNothingByDefault()
    {
        ISettingsStore store = new NoFileStore();

        Assert.False(await store.SetDetectedModelAsync("a", Mk4S, Address, ConnectionType.PrusaLink, Ct));
    }

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    /// <summary>Writes <paramref name="content"/> with "@M" expanded to the machine of the printer (<see cref="Machine"/>).</summary>
    private async Task WriteFileAsync(byte[] content)
    {
        Directory.CreateDirectory(_directory);
        string text = Encoding.UTF8.GetString(content).Replace("@M", Machine, StringComparison.Ordinal);
        await File.WriteAllBytesAsync(FilePath, Encoding.UTF8.GetBytes(text), Ct);
    }

    /// <summary>The file with the machine written back as "@M", so that expectations stay short.</summary>
    private async Task<string> ReadFileAsync() => (await File.ReadAllTextAsync(FilePath, Ct)).Replace(Machine, "@M", StringComparison.Ordinal);

    private async Task<(bool Written, string Result)> SetAsync(string original, string printerId, PrinterModelCode code)
    {
        await WriteFileAsync(Encoding.UTF8.GetBytes(original));
        bool written = await _store.SetDetectedModelAsync(printerId, code, Address, ConnectionType.PrusaLink, Ct);
        return (written, await ReadFileAsync());
    }

    private sealed class NoFileStore : ISettingsStore
    {
        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
