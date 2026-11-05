using System.Text.Json;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// T-148: the dashcam values of the <c>recording</c> section (length, triggers) and <c>printers[*].camera.dashcam</c>:
/// defaults are not written, invalid values fall back to their defaults with a warning.
/// </summary>
public sealed class SettingsMapperDashcamTests : IDisposable
{
    private readonly string _directory = TestDirectories.NewDirectoryPath();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_directory);

    private static PrinterDefinition Printer(bool dashcam) => new()
    {
        Id = "mk4",
        Name = "MK4",
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.10",
        Camera = new CameraSettings(true, new Uri("rtsp://192.168.0.11/live")) { Dashcam = dashcam },
    };

    [Fact]
    public void TheDefaultsAreTwoMinutesWithEveryTriggerAndNoDashcam()
    {
        CameraRecordingSettings recording = AppSettings.Default.Recording;

        Assert.Equal(2, recording.DashcamMinutes);
        Assert.True(recording.DashcamOnError && recording.DashcamOnAttention && recording.DashcamOnStopped && recording.DashcamOnFailureDetection);
        Assert.All(Enum.GetValues<CameraDashcamTrigger>(), trigger => Assert.True(recording.SavesDashcamOn(trigger)));
        Assert.False(CameraSettings.Disabled.Dashcam);
        string json = Serialize(new AppSettings { Printers = [Printer(dashcam: false)] });
        Assert.DoesNotContain("dashcam", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recording", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnlyWhatDiffersIsWrittenAndItRoundTrips()
    {
        AppSettings settings = new()
        {
            Recording = new CameraRecordingSettings { DashcamMinutes = 10, DashcamOnStopped = false },
            Printers = [Printer(dashcam: true)],
        };

        string json = Serialize(settings);
        AppSettings loaded = Load(json, out List<string> warnings);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            "{\"dashcamMinutes\":10,\"dashcamOnStopped\":false}",
            document.RootElement.GetProperty("recording").GetRawText().Replace(" ", "", StringComparison.Ordinal).ReplaceLineEndings(""));
        Assert.True(document.RootElement.GetProperty("printers")[0].GetProperty("camera").GetProperty("dashcam").GetBoolean());
        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
        Assert.False(loaded.Recording.SavesDashcamOn(CameraDashcamTrigger.Stopped));
        Assert.True(loaded.Recording.SavesDashcamOn(CameraDashcamTrigger.Manual));
    }

    [Fact]
    public void TheDashcamTakesPartInEquality()
    {
        CameraSettings camera = new(true, new Uri("rtsp://h/live"));

        Assert.NotEqual(camera, camera with { Dashcam = true });
        Assert.NotEqual(camera.GetHashCode(), (camera with { Dashcam = true }).GetHashCode());
        Assert.Contains("Dashcam = True", (camera with { Dashcam = true }).ToString(), StringComparison.Ordinal);
        Assert.NotEqual(CameraRecordingSettings.Default, CameraRecordingSettings.Default with { DashcamOnError = false });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("2.5")]
    [InlineData("\"long\"")]
    [InlineData("true")]
    public void AnInvalidLengthIsTwoMinutesWithAWarning(string minutes)
    {
        AppSettings settings = Load($"{{ \"recording\": {{ \"dashcamMinutes\": {minutes}, \"sizeLimitGigabytes\": 9 }} }}", out List<string> warnings);

        Assert.Equal(2, settings.Recording.DashcamMinutes);
        Assert.Equal(9, settings.Recording.SizeLimitGigabytes);
        Assert.Contains("Dashcam length is invalid", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("10", 10)]
    [InlineData("\"3\"", 3)]
    public void ValidLengthsAreRead(string minutes, int expected)
    {
        AppSettings settings = Load($"{{ \"recording\": {{ \"DashcamMinutes\": {minutes} }} }}", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(expected, settings.Recording.DashcamMinutes);
    }

    [Theory]
    [InlineData("dashcamOnError")]
    [InlineData("dashcamOnAttention")]
    [InlineData("dashcamOnStopped")]
    [InlineData("dashcamOnFailureDetection")]
    public void AnInvalidTriggerIsOnWithAWarning(string trigger)
    {
        AppSettings settings = Load($"{{ \"recording\": {{ \"{trigger}\": \"no\" }} }}", out List<string> warnings);

        Assert.Same(CameraRecordingSettings.Default, settings.Recording);
        Assert.Contains("Dashcam trigger", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    public void AnInvalidDashcamIsOffWithAWarning(string value)
    {
        AppSettings settings = Load(
            $"{{ \"printers\": [ {{ \"id\": \"mk4\", \"connectionType\": \"PrusaLink\", \"address\": \"h\", \"camera\": {{ \"enabled\": true, \"streamUrl\": \"rtsp://h/live\", \"dashcam\": {value} }} }} ] }}",
            out List<string> warnings);

        Assert.False(settings.Printers[0].Camera.Dashcam);
        Assert.Contains("keeping the last minutes of a print is invalid", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDashcamIsKeptForADisabledCamera()
    {
        AppSettings settings = new()
        {
            Printers = [Printer(dashcam: true) with { Camera = CameraSettings.Disabled with { Dashcam = true } }],
        };

        AppSettings loaded = Load(Serialize(settings), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.True(loaded.Printers[0].Camera.Dashcam);
        Assert.False(loaded.Printers[0].Camera.Enabled);
    }

    [Fact]
    public async Task TheFixtureLoadsAndSavesTheDashcamSettings()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-v2-dashcam.json"), Path.Combine(_directory, JsonSettingsStore.FileName));
        JsonSettingsStore store = new(_directory);

        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(new CameraRecordingSettings { DashcamMinutes = 5, DashcamOnAttention = false, DashcamOnFailureDetection = false }, result.Settings.Recording);
        Assert.Equal([true, false], result.Settings.Printers.Select(static p => p.Camera.Dashcam));
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
