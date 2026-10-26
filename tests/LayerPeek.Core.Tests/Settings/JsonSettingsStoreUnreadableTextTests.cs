using System.Text;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// Text in <c>settings.json</c> that cannot be read as a string: a lone escaped surrogate (<c>"\ud800"</c>) or invalid
/// UTF-8 bytes. Reading such a string throws <see cref="InvalidOperationException"/>; no operation of the store may let
/// it escape (an exception from <see cref="JsonSettingsStore.LoadAsync"/> would make the app start without the stored
/// printers). A typed value is a value of the wrong type (the file is <see cref="SettingsLoadStatus.Corrupted"/> and kept);
/// the raw overlay section only warns (<see cref="JsonSettingsStoreOverlayLayoutTests"/>).
/// </summary>
public sealed class JsonSettingsStoreUnreadableTextTests : IDisposable
{
    // Replaced by the unreadable text in each case: an escaped lone surrogate or the invalid UTF-8 bytes C3 28.
    private const string Marker = "#";

    private const string Printer = """{ "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "camera": { "enabled": true, "streamUrl": "rtsp://192.168.0.227/live" } }""";

    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly JsonSettingsStore _store;

    public JsonSettingsStoreUnreadableTextTests()
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

    public static TheoryData<string> TypedValues() =>
    [
        """{ "language": "#", "printers": [ PRINTER ] }""",
        """{ "notifications": { "sound": "#" }, "printers": [ PRINTER ] }""",
        """{ "printers": [ { "id": "#", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226" } ] }""",
        """{ "printers": [ { "id": "mk4", "name": "#", "connectionType": "PrusaLink", "address": "192.168.0.226" } ] }""",
        """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "#", "address": "192.168.0.226" } ] }""",
        """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "#" } ] }""",
        """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "userName": "#" } ] }""",
        """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "detectedModel": "#" } ] }""",
        """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "image": { "mode": "#" } } ] }""",
        """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "camera": { "enabled": true, "streamUrl": "rtsp://#/live" } } ] }""",
        """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "camera": { "window": { "startMode": "#" } } } ] }""",
        """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "camera": { "window": { "monitor": { "displayName": "#", "width": 1920, "height": 1080 } } } } ] }""",
        """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "camera": { "window": { "monitor": { "displayName": "D", "width": 1920, "height": 1080, "knownTwinPaths": [ "#" ] } } } } ] }""",
        """{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "camera": { "window": { "normal": { "corner": "#" } } } } ] }""",
        """{ "pollingIntervalSeconds": "#", "printers": [ PRINTER ] }""",
        """{ "version": "#", "printers": [ PRINTER ] }""",
    ];

    [Theory]
    [MemberData(nameof(TypedValues))]
    public async Task EscapedLoneSurrogateInATypedValueIsACorruptedFileThatIsKept(string json)
    {
        string content = Escaped(json);
        await WriteAsync(Encoding.UTF8.GetBytes(content));

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Corrupted, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Empty(result.Settings.Printers);
        Assert.Equal(content, await File.ReadAllTextAsync(FilePath, Ct));
    }

    [Theory]
    [MemberData(nameof(TypedValues))]
    public async Task InvalidUtf8InATypedValueIsACorruptedFileThatIsKept(string json)
    {
        byte[] content = InvalidUtf8(json);
        await WriteAsync(content);

        SettingsLoadResult result = await _store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Corrupted, result.Status);
        Assert.Empty(result.Settings.Printers);
        Assert.Equal(content, await File.ReadAllBytesAsync(FilePath, Ct));
    }

    [Theory]
    [InlineData("""{ "#": 1, "printers": [ PRINTER ] }""")]
    [InlineData("""{ "printers": [ { "#": 1, "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226" } ] }""")]
    [InlineData("""{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226", "camera": { "#": 1 } } ] }""")]
    public async Task UnreadablePropertyNameNeverEscapesTheLoad(string json)
    {
        // The serializer treats an escaped lone surrogate in a name as a corrupted file and invalid UTF-8 as an unknown
        // property; neither may throw.
        await WriteAsync(Encoding.UTF8.GetBytes(Escaped(json)));
        Assert.Equal(SettingsLoadStatus.Corrupted, (await _store.LoadAsync(Ct)).Status);

        await WriteAsync(InvalidUtf8(json));
        SettingsLoadResult result = await _store.LoadAsync(Ct);
        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal("mk4", Assert.Single(result.Settings.Printers).Id);
    }

    [Theory]
    [MemberData(nameof(TypedValues))]
    public async Task WritersNeverThrowAndKeepAFileWithUnreadableTextByteForByte(string json)
    {
        // The byte-level writers of single values (detected model, overlay) may still write their own value into a
        // syntactically valid file the loader rejects, as documented on ISettingsStore; nothing else in the file changes.
        (byte[] Content, byte[] Unreadable)[] files =
        [
            (Encoding.UTF8.GetBytes(Escaped(json)), "\"\\ud800\""u8.ToArray()),
            (InvalidUtf8(json), [(byte)'"', 0xC3, 0x28, (byte)'"']),
        ];
        foreach ((byte[] content, byte[] unreadable) in files)
        {
            await WriteAsync(content);

            await _store.RemoveCameraCredentialsAsync(Ct);
            Assert.Equal(content, await File.ReadAllBytesAsync(FilePath, Ct));

            Assert.False(await _store.SetCameraMonitorDevicePathAsync("mk4", new CameraMonitorId("D", 1920, 1080), "path", Ct));
            Assert.Equal(content, await File.ReadAllBytesAsync(FilePath, Ct));

            byte[] before = await File.ReadAllBytesAsync(FilePath, Ct);
            bool written = await _store.SetDetectedModelAsync("mk4", new PrinterModelCode(1, 4, 1), "192.168.0.226", ConnectionType.PrusaLink, Ct);
            AssertOnlyOwnValueWritten(before, await File.ReadAllBytesAsync(FilePath, Ct), written, unreadable);

            before = await File.ReadAllBytesAsync(FilePath, Ct);
            CameraOverlayLayout layout = CameraOverlayLayout.Default.WithVisible(OverlayWidgetIds.Status, false);
            written = await _store.SetCameraOverlayLayoutAsync("mk4", layout, Ct);
            AssertOnlyOwnValueWritten(before, await File.ReadAllBytesAsync(FilePath, Ct), written, unreadable);
        }
    }

    /// <summary>
    /// Unchanged when nothing was written; otherwise one contiguous change: the unchanged start and end of the file cover
    /// the whole original except nothing (an inserted value) or (part of) the writer's own old value, the unreadable string
    /// itself (<paramref name="unreadableValue"/>, quotes included).
    /// </summary>
    private static void AssertOnlyOwnValueWritten(byte[] before, byte[] after, bool written, byte[] unreadableValue)
    {
        if (!written)
        {
            Assert.Equal(before, after);
            return;
        }

        int start = before.AsSpan().CommonPrefixLength(after);
        int end = 0;
        while (end < before.Length - start && end < after.Length - start && before[^(end + 1)] == after[^(end + 1)])
        {
            end++;
        }

        byte[] replaced = before[start..^end];
        Assert.True(unreadableValue.AsSpan().IndexOf(replaced) >= 0, $"Changed outside the written value: {Convert.ToHexString(replaced)}");
        Assert.NotEqual(before, after);
    }

    private static string Escaped(string json) =>
        json.Replace("PRINTER", Printer, StringComparison.Ordinal).Replace(Marker, "\\ud800", StringComparison.Ordinal);

    private static byte[] InvalidUtf8(string json)
    {
        string[] parts = json.Replace("PRINTER", Printer, StringComparison.Ordinal).Split(Marker);
        return [.. Encoding.UTF8.GetBytes(parts[0]), 0xC3, 0x28, .. Encoding.UTF8.GetBytes(parts[1])];
    }

    private async Task WriteAsync(byte[] content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(FilePath, content, Ct);
    }
}
