using System.Text.Json;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// T-145: the <c>recording</c> section (folder, size limit) and <c>printers[*].camera.recordPrints</c>: defaults are not
/// written, invalid values fall back to their defaults with a warning that never echoes the folder.
/// </summary>
public sealed class SettingsMapperRecordingTests : IDisposable
{
    private readonly string _directory = TestDirectories.NewDirectoryPath();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_directory, JsonSettingsStore.FileName);

    private static string AbsoluteFolder => Path.Combine(Path.GetTempPath(), "LayerPeek Recordings");

    public void Dispose() => TestDirectories.Delete(_directory);

    private static PrinterDefinition Printer(bool recordPrints, bool enabled = true) => new()
    {
        Id = "mk4",
        Name = "MK4",
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.10",
        Camera = new CameraSettings(enabled, new Uri("rtsp://192.168.0.11/live")) { RecordPrints = recordPrints },
    };

    [Fact]
    public void TheDefaultsAreFiftyGigabytesInTheVideoFolderWithoutAutomaticRecording()
    {
        Assert.Null(AppSettings.Default.Recording.Folder);
        Assert.Equal(50, AppSettings.Default.Recording.SizeLimitGigabytes);
        Assert.Equal(50_000_000_000, AppSettings.Default.Recording.SizeLimitBytes);
        Assert.False(CameraSettings.Disabled.RecordPrints);
    }

    [Fact]
    public void DefaultsAreNotWritten()
    {
        string json = Serialize(new AppSettings { Printers = [Printer(recordPrints: false)] });

        Assert.DoesNotContain("recording", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recordPrints", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnlyWhatDiffersIsWritten()
    {
        using JsonDocument limitOnly = JsonDocument.Parse(Serialize(new AppSettings { Recording = new CameraRecordingSettings { SizeLimitGigabytes = 10 } }));
        using JsonDocument folderOnly = JsonDocument.Parse(Serialize(new AppSettings { Recording = new CameraRecordingSettings { Folder = AbsoluteFolder } }));

        Assert.Equal("{\"sizeLimitGigabytes\":10}", limitOnly.RootElement.GetProperty("recording").GetRawText().Replace(" ", "", StringComparison.Ordinal).ReplaceLineEndings(""));
        JsonElement folder = Assert.Single(folderOnly.RootElement.GetProperty("recording").EnumerateObject()).Value;
        Assert.Equal(AbsoluteFolder, folder.GetString());
    }

    [Fact]
    public void RecordingSettingsRoundTrip()
    {
        AppSettings settings = new()
        {
            Recording = new CameraRecordingSettings { Folder = AbsoluteFolder, SizeLimitGigabytes = 7 },
            Printers = [Printer(recordPrints: true)],
        };

        AppSettings loaded = Load(Serialize(settings), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
        Assert.True(loaded.Printers[0].Camera.RecordPrints);
        Assert.Equal(AbsoluteFolder, loaded.Recording.Folder);
        Assert.Equal(7, loaded.Recording.SizeLimitGigabytes);
    }

    [Fact]
    public void RecordPrintsIsKeptForADisabledCameraWithoutAddress()
    {
        AppSettings settings = new()
        {
            Printers = [new PrinterDefinition
            {
                Id = "mk4",
                Name = "MK4",
                ConnectionType = ConnectionType.PrusaLink,
                Address = "192.168.0.10",
                Camera = CameraSettings.Disabled with { RecordPrints = true },
            }],
        };

        AppSettings loaded = Load(Serialize(settings), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.True(loaded.Printers[0].Camera.RecordPrints);
        Assert.False(loaded.Printers[0].Camera.Enabled);
    }

    [Fact]
    public void RecordPrintsTakesPartInEquality()
    {
        CameraSettings camera = new(true, new Uri("rtsp://h/live"));

        Assert.NotEqual(camera, camera with { RecordPrints = true });
        Assert.NotEqual(new AppSettings(), new AppSettings { Recording = new CameraRecordingSettings { SizeLimitGigabytes = 1 } });
        Assert.Contains("RecordPrints = True", (camera with { RecordPrints = true }).ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"recording\": 5")]
    [InlineData("\"recording\": []")]
    [InlineData("\"recording\": \"x\"")]
    public void ARecordingSectionThatIsNoObjectIsTheDefaultWithAWarning(string section)
    {
        AppSettings settings = Load($"{{ {section} }}", out List<string> warnings);

        Assert.Same(CameraRecordingSettings.Default, settings.Recording);
        Assert.Contains("Recording settings are invalid", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"relative/folder\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{}")]
    public void AnInvalidFolderIsTheDefaultFolderWithAWarningThatDoesNotEchoIt(string folder)
    {
        AppSettings settings = Load($"{{ \"recording\": {{ \"folder\": {folder}, \"sizeLimitGigabytes\": 9 }} }}", out List<string> warnings);

        Assert.Null(settings.Recording.Folder);
        Assert.Equal(9, settings.Recording.SizeLimitGigabytes);
        string warning = Assert.Single(warnings);
        Assert.Contains("Recording folder is invalid", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("relative", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ATooLongFolderIsInvalid()
    {
        string folder = Path.Combine(Path.GetTempPath(), new string('x', CameraRecordingSettings.MaxFolderLength));

        Assert.False(CameraRecordingSettings.IsValidFolder(folder));
        Assert.Throws<ArgumentException>(() => new CameraRecordingSettings { Folder = folder });
        Assert.Throws<ArgumentException>(() => new CameraRecordingSettings { Folder = "relative" });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("0.5")]
    [InlineData("100001")]
    [InlineData("1e300")]
    [InlineData("\"many\"")]
    [InlineData("false")]
    [InlineData("[]")]
    public void AnInvalidLimitIsFiftyGigabytesWithAWarning(string limit)
    {
        AppSettings settings = Load($"{{ \"recording\": {{ \"sizeLimitGigabytes\": {limit} }} }}", out List<string> warnings);

        Assert.Equal(50, settings.Recording.SizeLimitGigabytes);
        Assert.Contains("Recording size limit is invalid", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("100000", 100_000)]
    [InlineData("\"20\"", 20)]
    [InlineData("75.0", 75)]
    public void ValidLimitsAreRead(string limit, int expected)
    {
        AppSettings settings = Load($"{{ \"RECORDING\": {{ \"SizeLimitGigabytes\": {limit} }} }}", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(expected, settings.Recording.SizeLimitGigabytes);
    }

    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    [InlineData("{}")]
    public void AnInvalidRecordPrintsIsOffWithAWarning(string value)
    {
        AppSettings settings = Load(
            $"{{ \"printers\": [ {{ \"id\": \"mk4\", \"connectionType\": \"PrusaLink\", \"address\": \"h\", \"camera\": {{ \"enabled\": true, \"streamUrl\": \"rtsp://h/live\", \"recordPrints\": {value} }} }} ] }}",
            out List<string> warnings);

        Assert.False(settings.Printers[0].Camera.RecordPrints);
        Assert.True(settings.Printers[0].Camera.Enabled);
        Assert.Contains("automatic recording of prints is invalid", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFixtureLoadsAndSavesTheRecordingSettings()
    {
        Directory.CreateDirectory(_directory);
        string fixture = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-v2-recording.json"), Ct);
        await File.WriteAllTextAsync(FilePath, fixture.Replace("__FOLDER__", JsonEncodedText.Encode(AbsoluteFolder).ToString(), StringComparison.Ordinal), Ct);
        JsonSettingsStore store = new(_directory);

        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(new CameraRecordingSettings { Folder = AbsoluteFolder, SizeLimitGigabytes = 120 }, result.Settings.Recording);
        Assert.Equal([true, true, false], result.Settings.Printers.Select(static p => p.Camera.RecordPrints));

        await store.SaveAsync(result.Settings, Ct);
        SettingsLoadResult again = await new JsonSettingsStore(_directory).LoadAsync(Ct);
        Assert.Equal(result.Settings, again.Settings);
        Assert.Empty(again.Warnings);
    }

    [Fact]
    public async Task TheFixtureWithInvalidValuesFallsBackToTheDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-v2-recording-invalid.json"), FilePath);

        SettingsLoadResult result = await new JsonSettingsStore(_directory).LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Same(CameraRecordingSettings.Default, result.Settings.Recording);
        Assert.False(result.Settings.Printers[0].Camera.RecordPrints);
        Assert.True(result.Settings.Printers[0].Camera.Enabled);
        Assert.Equal(3, result.Warnings.Count);
    }

    private static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);

    private static AppSettings Load(string json, out List<string> warnings)
    {
        SettingsDocument document = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsDocument)!;
        warnings = [];
        return SettingsMapper.ToSettings(document, warnings, notices: []);
    }
}
