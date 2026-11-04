using System.Text.Json;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// T-160: the timelapse interval of the <c>recording</c> section and <c>printers[*].camera.timelapsePrints</c>: defaults
/// are not written, invalid values fall back to their defaults with a warning.
/// </summary>
public sealed class SettingsMapperTimelapseTests : IDisposable
{
    private readonly string _directory = TestDirectories.NewDirectoryPath();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_directory);

    private static PrinterDefinition Printer(bool timelapsePrints) => new()
    {
        Id = "mk4",
        Name = "MK4",
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.10",
        Camera = new CameraSettings(true, new Uri("rtsp://192.168.0.11/live")) { TimelapsePrints = timelapsePrints },
    };

    [Fact]
    public void TheDefaultsAreThirtySecondsAndNoTimelapse()
    {
        CameraRecordingSettings recording = AppSettings.Default.Recording;

        Assert.Equal(30, recording.TimelapseIntervalSeconds);
        Assert.Equal(TimeSpan.FromSeconds(30), recording.TimelapseInterval);
        Assert.False(CameraSettings.Disabled.TimelapsePrints);
        string json = Serialize(new AppSettings { Printers = [Printer(timelapsePrints: false)] });
        Assert.DoesNotContain("timelapse", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recording", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(601)]
    public void TheIntervalIsWithinItsRange(int seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraRecordingSettings { TimelapseIntervalSeconds = seconds });

    [Fact]
    public void OnlyWhatDiffersIsWrittenAndItRoundTrips()
    {
        AppSettings settings = new()
        {
            Recording = new CameraRecordingSettings { TimelapseIntervalSeconds = 5 },
            Printers = [Printer(timelapsePrints: true)],
        };

        string json = Serialize(settings);
        AppSettings loaded = Load(json, out List<string> warnings);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            "{\"timelapseIntervalSeconds\":5}",
            document.RootElement.GetProperty("recording").GetRawText().Replace(" ", "", StringComparison.Ordinal).ReplaceLineEndings(""));
        Assert.True(document.RootElement.GetProperty("printers")[0].GetProperty("camera").GetProperty("timelapsePrints").GetBoolean());
        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void TheTimelapseTakesPartInEquality()
    {
        CameraSettings camera = new(true, new Uri("rtsp://h/live"));

        Assert.NotEqual(camera, camera with { TimelapsePrints = true });
        Assert.NotEqual(camera.GetHashCode(), (camera with { TimelapsePrints = true }).GetHashCode());
        Assert.NotEqual(CameraRecordingSettings.Default, CameraRecordingSettings.Default with { TimelapseIntervalSeconds = 60 });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("601")]
    [InlineData("30.5")]
    [InlineData("\"often\"")]
    [InlineData("true")]
    [InlineData("null")]
    public void AnInvalidIntervalIsThirtySecondsWithAWarning(string seconds)
    {
        AppSettings settings = Load($"{{ \"recording\": {{ \"timelapseIntervalSeconds\": {seconds}, \"sizeLimitGigabytes\": 9 }} }}", out List<string> warnings);

        Assert.Equal(30, settings.Recording.TimelapseIntervalSeconds);
        Assert.Equal(9, settings.Recording.SizeLimitGigabytes);
        if (seconds == "null")
        {
            // A null value is a missing one (like every other value of the section).
            Assert.Empty(warnings);
        }
        else
        {
            Assert.Contains("Timelapse interval is invalid", Assert.Single(warnings), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("600", 600)]
    [InlineData("\"45\"", 45)]
    public void ValidIntervalsAreRead(string seconds, int expected)
    {
        AppSettings settings = Load($"{{ \"recording\": {{ \"TimelapseIntervalSeconds\": {seconds} }} }}", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(expected, settings.Recording.TimelapseIntervalSeconds);
    }

    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    public void AnInvalidTimelapseOptionIsOffWithAWarning(string value)
    {
        AppSettings settings = Load(
            $"{{ \"printers\": [ {{ \"id\": \"mk4\", \"connectionType\": \"PrusaLink\", \"address\": \"h\", \"camera\": {{ \"enabled\": true, \"streamUrl\": \"rtsp://h/live\", \"timelapsePrints\": {value} }} }} ] }}",
            out List<string> warnings);

        Assert.False(settings.Printers[0].Camera.TimelapsePrints);
        Assert.Contains("timelapse of every print is invalid", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTimelapseOptionIsKeptForADisabledCamera()
    {
        AppSettings settings = new()
        {
            Printers = [Printer(timelapsePrints: true) with { Camera = CameraSettings.Disabled with { TimelapsePrints = true } }],
        };

        AppSettings loaded = Load(Serialize(settings), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.True(loaded.Printers[0].Camera.TimelapsePrints);
        Assert.False(loaded.Printers[0].Camera.Enabled);
    }

    [Fact]
    public async Task TheFixtureLoadsAndSavesTheTimelapseSettings()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-v2-timelapse.json"), Path.Combine(_directory, JsonSettingsStore.FileName));
        JsonSettingsStore store = new(_directory);

        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(new CameraRecordingSettings { TimelapseIntervalSeconds = 60 }, result.Settings.Recording);
        Assert.Equal([true, false], result.Settings.Printers.Select(static p => p.Camera.TimelapsePrints));
        Assert.Equal([false, true], result.Settings.Printers.Select(static p => p.Camera.RecordPrints));

        await store.SaveAsync(result.Settings, Ct);
        SettingsLoadResult again = await new JsonSettingsStore(_directory).LoadAsync(Ct);
        Assert.Equal(result.Settings, again.Settings);
        Assert.Empty(again.Warnings);
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
