using System.Text;
using System.Text.Json;

using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// T-201: the install hook of Velopack reads only the start after logon the user chose, synchronously and without writing
/// anything (no migration, no temporary file), so that a reinstall respects a switched-off autostart.
/// </summary>
public sealed class JsonSettingsStoreStartAtLoginTests : IDisposable
{
    private const string Lf = "\n";
    private const string CrLf = "\r\n";

    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly JsonSettingsStore _store;

    public JsonSettingsStoreStartAtLoginTests()
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
    public void WithoutAFolderThereIsNoChoice()
    {
        Assert.Null(_store.ReadStartAtLoginChoice());
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void WithoutAFileThereIsNoChoice()
    {
        Directory.CreateDirectory(_directory);

        Assert.Null(_store.ReadStartAtLoginChoice());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASavedChoiceIsRead(bool start)
    {
        await _store.SaveAsync(new AppSettings { StartAtLogin = start, DebugLogging = true }, Ct);

        Assert.Equal(start, _store.ReadStartAtLoginChoice());
    }

    [Fact]
    public async Task SettingsWithoutAChoiceHaveNone()
    {
        await _store.SaveAsync(new AppSettings { DebugLogging = true }, Ct);

        Assert.Null(_store.ReadStartAtLoginChoice());
    }

    /// <summary>A file of an older version (format 1, T-036 overlay) is read as it is: nothing is converted or written.</summary>
    [Fact]
    public void ReadingWritesNothing()
    {
        Directory.CreateDirectory(_directory);
        const string json = """{ "version": 1, "startAtLogin": false, "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "10.0.0.5", "camera": { "enabled": true, "overlay": { "corner": "TopLeft" } } } ] }""";
        File.WriteAllText(FilePath, json, Encoding.UTF8);
        DateTime written = File.GetLastWriteTimeUtc(FilePath);

        Assert.False(_store.ReadStartAtLoginChoice());

        Assert.Equal(json, File.ReadAllText(FilePath, Encoding.UTF8));
        Assert.Equal(written, File.GetLastWriteTimeUtc(FilePath));
        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    /// <summary>A file of a newer version still tells the choice; values the mapper would warn about do not matter.</summary>
    [Fact]
    public void AFileOfANewerVersionTellsTheChoice()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "version": 99, "pollingIntervalSeconds": 0, "startAtLogin": true, "somethingNew": [1, 2] }""", Encoding.UTF8);

        Assert.True(_store.ReadStartAtLoginChoice());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "startAtLogin": "no" }""")]
    public void ACorruptedFileIsReported(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, content, Encoding.UTF8);

        Assert.ThrowsAny<JsonException>(() => _store.ReadStartAtLoginChoice());
    }

    /// <summary>
    /// Review of T-201: a value elsewhere that the loader rejects (the whole file is then unreadable for it) does not hide a
    /// stored "off".
    /// </summary>
    [Theory]
    [InlineData("""{ "debugLogging": "yes", "startAtLogin": false }""")]
    [InlineData("""{ "pollingIntervalSeconds": "fast", "printers": { "not": "a list" }, "startAtLogin": false }""")]
    [InlineData("""{ "startAtLogin": false, "notifications": [ 1, 2 ] }""")]
    public void AnUnrelatedTypeErrorKeepsTheStoredChoice(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, content, Encoding.UTF8);

        Assert.False(_store.ReadStartAtLoginChoice());
    }

    [Theory]
    [InlineData("""{ "startAtLogin": 0 }""")]
    [InlineData("""{ "startAtLogin": "false" }""")]
    [InlineData("""{ "startAtLogin": { } }""")]
    [InlineData("""[ { "startAtLogin": false } ]""")]
    [InlineData("")]
    public void AnUnreadableChoiceIsReported(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, content, Encoding.UTF8);

        Assert.ThrowsAny<JsonException>(() => _store.ReadStartAtLoginChoice());
    }

    /// <summary>Like the loader: names case-insensitive, the last of duplicates, comments and a BOM allowed, nested ones ignored.</summary>
    [Theory]
    [InlineData("""{ "StartAtLogin": true }""", true)]
    [InlineData("""{ "startAtLogin": true, "startAtLogin": false }""", false)]
    [InlineData("""{ /* note */ "startAtLogin": false, }""", false)]
    [InlineData("""{ "startAtLogin": null }""", null)]
    [InlineData("""{ "printers": [ { "startAtLogin": false } ] }""", null)]
    public void TheChoiceIsReadLikeTheLoaderReadsIt(string content, bool? expected)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(expected, _store.ReadStartAtLoginChoice());
    }

    [Fact]
    public void AFileWithoutASettingsObjectHasNoChoice()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "null", Encoding.UTF8);

        Assert.Null(_store.ReadStartAtLoginChoice());
    }

    [Fact]
    public void AFileTooLargeToBeSettingsIsReported()
    {
        Directory.CreateDirectory(_directory);
        using (FileStream stream = new(FilePath, FileMode.CreateNew))
        {
            stream.SetLength((1024 * 1024) + 1);
        }

        Assert.Throws<InvalidDataException>(() => _store.ReadStartAtLoginChoice());
    }

    /// <summary>T-201 migration: without a file, the default settings with the choice are written.</summary>
    [Fact]
    public async Task WithoutAFileTheChoiceIsWrittenWithTheDefaults()
    {
        Assert.True(await _store.SetStartAtLoginChoiceIfUnsetAsync(false, Ct));

        SettingsLoadResult result = await _store.LoadAsync(Ct);
        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(AppSettings.Default with { StartAtLogin = false }, result.Settings);
        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    /// <summary>Only the value is added, after the last root property and indented like the first one; nothing else changes.</summary>
    [Theory]
    [InlineData(Lf)]
    [InlineData(CrLf)]
    public async Task TheChoiceIsAddedWithoutTouchingTheRest(string newline)
    {
        Directory.CreateDirectory(_directory);
        string json = string.Join(newline, "{", "  // kept", "  \"version\": 2,", "  \"debugLogging\": true,", "  \"somethingNew\": { \"a\": [1, 2] }", "}", "");
        File.WriteAllText(FilePath, json);

        Assert.True(await _store.SetStartAtLoginChoiceIfUnsetAsync(false, Ct));

        string expected = string.Join(newline, "{", "  // kept", "  \"version\": 2,", "  \"debugLogging\": true,", "  \"somethingNew\": { \"a\": [1, 2] },", "  \"startAtLogin\": false", "}", "");
        Assert.Equal(expected, File.ReadAllText(FilePath));
        Assert.False(_store.ReadStartAtLoginChoice());
        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("{}", "{ \"startAtLogin\": false }")]
    [InlineData("{ \"debugLogging\": true }", "{ \"debugLogging\": true, \"startAtLogin\": false }")]
    [InlineData("{ \"startAtLogin\": null, \"debugLogging\": true }", "{ \"startAtLogin\": false, \"debugLogging\": true }")]
    // T-201 review: of duplicates the last one counts for the reader, so the writer replaces the last null.
    [InlineData("{ \"startAtLogin\": true, \"startAtLogin\": null }", "{ \"startAtLogin\": true, \"startAtLogin\": false }")]
    // T-201 review: comments inside an empty settings object stay.
    [InlineData("{ // note\n}", "{ // note\n\"startAtLogin\": false }")]
    [InlineData("{ /* note */ }", "{ /* note */ \"startAtLogin\": false }")]
    [InlineData("{/* note */}", "{/* note */ \"startAtLogin\": false }")]
    public async Task TheChoiceIsAddedToCompactFiles(string json, string expected)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, json);

        Assert.True(await _store.SetStartAtLoginChoiceIfUnsetAsync(false, Ct));

        Assert.Equal(expected, File.ReadAllText(FilePath));
        // The reader agrees with what was written, and so does the loader.
        Assert.False(_store.ReadStartAtLoginChoice());
        SettingsLoadResult loaded = await _store.LoadAsync(Ct);
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.False(loaded.Settings.StartAtLogin);
    }

    /// <summary>A stored choice, a file of a newer or unreadable format, a broken file and a root of another kind stay as they are.</summary>
    [Theory]
    [InlineData("""{ "startAtLogin": true }""")]
    [InlineData("""{ "startAtLogin": false }""")]
    [InlineData("""{ "startAtLogin": "odd" }""")]
    [InlineData("""{ "startAtLogin": null, "startAtLogin": true }""")]
    [InlineData("""{ "version": 99, "debugLogging": true }""")]
    [InlineData("""{ "version": "two" }""")]
    [InlineData("""{ damaged""")]
    [InlineData("""[ ]""")]
    [InlineData("""null""")]
    [InlineData("")]
    public async Task AFileThatIsNotOursToChangeIsLeftAlone(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, json);
        DateTime written = File.GetLastWriteTimeUtc(FilePath);

        Assert.False(await _store.SetStartAtLoginChoiceIfUnsetAsync(false, Ct));

        Assert.Equal(json, File.ReadAllText(FilePath));
        Assert.Equal(written, File.GetLastWriteTimeUtc(FilePath));
        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task AFileTooLargeToBeSettingsIsLeftAlone()
    {
        Directory.CreateDirectory(_directory);
        using (FileStream stream = new(FilePath, FileMode.CreateNew))
        {
            stream.SetLength((1024 * 1024) + 1);
        }

        Assert.False(await _store.SetStartAtLoginChoiceIfUnsetAsync(false, Ct));

        Assert.Equal((1024 * 1024) + 1, new FileInfo(FilePath).Length);
    }

    /// <summary>The settings saved by the app get the value and still load unchanged otherwise.</summary>
    [Fact]
    public async Task SavedSettingsGetTheChoiceAndLoadCleanly()
    {
        AppSettings settings = new() { DebugLogging = true, Language = "de" };
        await _store.SaveAsync(settings, Ct);

        Assert.True(await _store.SetStartAtLoginChoiceIfUnsetAsync(false, Ct));

        SettingsLoadResult result = await _store.LoadAsync(Ct);
        Assert.Empty(result.Warnings);
        Assert.Equal(settings with { StartAtLogin = false }, result.Settings);
    }
}
