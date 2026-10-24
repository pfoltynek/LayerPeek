using System.Text;

using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// <see cref="JsonSettingsStore.RemoveCameraCredentialsAsync"/> on raw JSON: only the affected values change, byte for byte.
/// JSON escapes are built from <see cref="B"/> (a backslash) so that no tool or compiler step can turn them into
/// plain characters before the test runs.
/// </summary>
public sealed class JsonSettingsStoreScrubTests : IDisposable
{
    private static readonly string B = ((char)92).ToString();

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly JsonSettingsStore _store;

    public JsonSettingsStoreScrubTests()
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
    public async Task EscapedValueIdAndPropertyNamesAreDecoded()
    {
        // Value: rtsp://us"er:p\a/ss@xsecret@10.0.0.6/live, written with \" \\ \/ and \u escapes (also for "@" and "s").
        string url = $"rtsp://us{B}\"er:p{B}{B}a{B}/ss{B}u0040x{B}u0073ecret@10.0.0.6{B}/live";
        string original =
            $$"""{ "printers": [ { "i{{B}}u0064": "m{{B}}u006b4", "c{{B}}u0061mera": { "en{{B}}u0061bled": true, "str{{B}}u0065amUrl": "{{url}}" } } ] }""";

        (IReadOnlyList<string> changed, string result) = await ScrubAsync(original);

        Assert.Equal(["mk4"], changed);
        string expected = original
            .Replace($"\"{url}\"", "\"rtsp://10.0.0.6/live\"", StringComparison.Ordinal)
            .Replace($"\"en{B}u0061bled\": true", $"\"en{B}u0061bled\": false", StringComparison.Ordinal);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ByteOrderMarkIsPreserved()
    {
        const string Original = """{ "printers": [ { "id": "a", "camera": { "enabled": true, "streamUrl": "rtsp://u:p@10.0.0.6/live" } } ] }""";
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(FilePath, [.. Bom, .. Encoding.UTF8.GetBytes(Original)], Ct);

        IReadOnlyList<string> changed = await _store.RemoveCameraCredentialsAsync(Ct);

        byte[] bytes = await File.ReadAllBytesAsync(FilePath, Ct);
        Assert.Equal(["a"], changed);
        Assert.Equal(Bom, bytes[..3]);
        Assert.Equal(
            """{ "printers": [ { "id": "a", "camera": { "enabled": false, "streamUrl": "rtsp://10.0.0.6/live" } } ] }""",
            Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public async Task PropertyNamesAreMatchedRegardlessOfCase()
    {
        const string Original = """{ "PRINTERS": [ { "ID": "a", "Camera": { "ENABLED": true, "StreamURL": "rtsp://u:p@10.0.0.6/live" } } ] }""";

        (IReadOnlyList<string> changed, string result) = await ScrubAsync(Original);

        Assert.Equal(["a"], changed);
        Assert.Equal(
            """{ "PRINTERS": [ { "ID": "a", "Camera": { "ENABLED": false, "StreamURL": "rtsp://10.0.0.6/live" } } ] }""",
            result);
    }

    [Fact]
    public async Task DuplicatePropertiesAreAllHandled()
    {
        // The loader would use only the last value of each duplicate; every candidate is cleaned (safe side). A camera
        // object whose addresses are all clean keeps its "enabled".
        const string Original = """
            { "printers": [ { "id": "a",
              "camera": { "streamUrl": "rtsp://u:p@10.0.0.6/live", "enabled": true, "streamUrl": "rtsp://10.0.0.7/live", "enabled": true },
              "camera": { "enabled": true, "streamUrl": "rtsp://10.0.0.8/live" },
              "camera": { "enabled": true, "streamUrl": "rtsp://v:q@10.0.0.9/live" } } ] }
            """;

        (IReadOnlyList<string> changed, string result) = await ScrubAsync(Original);

        Assert.Equal(["a"], changed);
        Assert.Equal(
            """
            { "printers": [ { "id": "a",
              "camera": { "streamUrl": "rtsp://10.0.0.6/live", "enabled": false, "streamUrl": "rtsp://10.0.0.7/live", "enabled": false },
              "camera": { "enabled": true, "streamUrl": "rtsp://10.0.0.8/live" },
              "camera": { "enabled": false, "streamUrl": "rtsp://10.0.0.9/live" } } ] }
            """,
            result);
    }

    [Fact]
    public async Task NestedValuesInsideCameraAreLeftAlone()
    {
        // Only camera.streamUrl / camera.enabled are settings; anything deeper is unknown data and stays as it is.
        const string Original = """
            { "printers": [ { "id": "a", "camera": {
              "advanced": { "streamUrl": "rtsp://x:y@10.0.0.1/z", "enabled": true, "list": [ { "streamUrl": "a@b" } ] },
              "streamUrl": "rtsp://u:p@10.0.0.6/live", "enabled": true } } ] }
            """;

        (IReadOnlyList<string> changed, string result) = await ScrubAsync(Original);

        Assert.Equal(["a"], changed);
        Assert.Equal(
            Original
                .Replace("\"rtsp://u:p@10.0.0.6/live\", \"enabled\": true", "\"rtsp://10.0.0.6/live\", \"enabled\": false", StringComparison.Ordinal),
            result);
    }

    [Fact]
    public async Task CameraWindowSectionIsLeftByteForByte()
    {
        // T-035: camera.window holds the window defaults; it is never touched, even when it contains look-alike values.
        string window = string.Join(
            "\r\n",
            "\"window\" : { /* defaults */ \"rememberLastPlacement\": false, \"startMode\" : \"FullScreen\",",
            "\t\"monitor\": { \"displayName\": \"\\\\\\\\.\\\\DISPLAY2\", \"width\": 2560, \"height\": 1440 },",
            "\t\"normal\": { \"width\": 1280, \"height\": 720, \"corner\": \"TopRight\" }, \"mini\": { \"width\": 333, \"height\": 222, \"corner\": \"BottomLeft\", },",
            "\t\"streamUrl\": \"rtsp://x:y@10.0.0.1/z\", \"enabled\": true }");
        string original = $$"""{ "printers": [ { "id": "a", "camera": { "enabled": true, {{window}}, "streamUrl": "rtsp://u:p@10.0.0.6/live" } } ] }""";

        (IReadOnlyList<string> changed, string result) = await ScrubAsync(original);

        Assert.Equal(["a"], changed);
        Assert.Equal(
            $$"""{ "printers": [ { "id": "a", "camera": { "enabled": false, {{window}}, "streamUrl": "rtsp://10.0.0.6/live" } } ] }""",
            result);
    }

    [Fact]
    public async Task CameraOverlaySectionIsLeftByteForByte()
    {
        // T-036: camera.overlay holds the overlay items; it is never touched, even when it contains look-alike values.
        string overlay = string.Join(
            "\r\n",
            "\"overlay\" : { /* items */ \"corner\": \"TopRight\",",
            "\t\"normal\": [ \"State\", \"rtsp://a:b@10.0.0.1/c\" , ], \"mini\": [],",
            "\t\"streamUrl\": \"rtsp://x:y@10.0.0.1/z\", \"enabled\": true }");
        string original = $$"""{ "printers": [ { "id": "a", "camera": { "enabled": true, {{overlay}}, "streamUrl": "rtsp://u:p@10.0.0.6/live" } } ] }""";

        (IReadOnlyList<string> changed, string result) = await ScrubAsync(original);

        Assert.Equal(["a"], changed);
        Assert.Equal(
            $$"""{ "printers": [ { "id": "a", "camera": { "enabled": false, {{overlay}}, "streamUrl": "rtsp://10.0.0.6/live" } } ] }""",
            result);
    }

    [Fact]
    public async Task CommentsTrailingCommasAndWhitespaceArePreserved()
    {
        string original = string.Join(
            "\r\n",
            "/* head */ {",
            "\t\"printers\" :\t[ // list",
            "\t\t{ \"id\" : \"a\" , \"camera\"\t:\t{ \"enabled\"   :   true , /* in between */ \"streamUrl\"  :  \"rtsp://u:p@10.0.0.6/live\" , } , } ,",
            "\t] ,",
            "} // tail",
            string.Empty);

        (IReadOnlyList<string> changed, string result) = await ScrubAsync(original);

        Assert.Equal(["a"], changed);
        Assert.Equal(
            original
                .Replace("\"enabled\"   :   true", "\"enabled\"   :   false", StringComparison.Ordinal)
                .Replace("\"rtsp://u:p@10.0.0.6/live\"", "\"rtsp://10.0.0.6/live\"", StringComparison.Ordinal),
            result);
    }

    [Fact]
    public async Task InvalidUtf8LeavesTheFileAlone()
    {
        byte[] original =
        [
            .. Encoding.UTF8.GetBytes("""{ "printers": [ { "id": "a", "camera": { "enabled": true, "streamUrl": "rtsp://u:p@10.0.0.6/li"""),
            0xFF,
            .. Encoding.UTF8.GetBytes("""ve" } } ] }"""),
        ];
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(FilePath, original, Ct);

        IReadOnlyList<string> changed = await _store.RemoveCameraCredentialsAsync(Ct);

        Assert.Empty(changed);
        Assert.Equal(original, await File.ReadAllBytesAsync(FilePath, Ct));
    }

    [Fact]
    public async Task FileWithWrongValueTypesIsCleanedToo()
    {
        // Syntactically valid JSON that LoadAsync rejects as corrupted (a bool where a number belongs): the
        // credentials are removed anyway (T-014 decision), nothing else changes.
        const string Original = """
            { "pollingIntervalSeconds": true,
              "printers": [ { "id": "a", "connectionType": "PrusaLink", "address": "h", "camera": { "enabled": true, "streamUrl": "rtsp://u:p@10.0.0.6/live" } } ] }
            """;
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, Original, Ct);
        Assert.Equal(SettingsLoadStatus.Corrupted, (await _store.LoadAsync(Ct)).Status);

        IReadOnlyList<string> changed = await _store.RemoveCameraCredentialsAsync(Ct);

        Assert.Equal(["a"], changed);
        Assert.Equal(
            Original.Replace("\"enabled\": true, \"streamUrl\": \"rtsp://u:p@10.0.0.6/live\"", "\"enabled\": false, \"streamUrl\": \"rtsp://10.0.0.6/live\"", StringComparison.Ordinal),
            await File.ReadAllTextAsync(FilePath, Ct));
        Assert.Equal(SettingsLoadStatus.Corrupted, (await _store.LoadAsync(Ct)).Status);
    }

    private async Task<(IReadOnlyList<string> Changed, string Result)> ScrubAsync(string original)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(FilePath, Encoding.UTF8.GetBytes(original), Ct);
        IReadOnlyList<string> changed = await _store.RemoveCameraCredentialsAsync(Ct);
        return (changed, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(FilePath, Ct)));
    }
}
