using System.Text.Json;

using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// T-159: <c>recording.timelapseTrigger</c> and <c>recording.timelapseEveryNthLayer</c>: the defaults (by time, every
/// layer) are not written, invalid values fall back to them with a warning that does not echo the value.
/// </summary>
public sealed class SettingsMapperTimelapseLayerTests : IDisposable
{
    private readonly string _directory = TestDirectories.NewDirectoryPath();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_directory);

    [Fact]
    public void TheDefaultsAreTheTimeTriggerAndEveryLayer()
    {
        CameraRecordingSettings recording = CameraRecordingSettings.Default;

        Assert.Equal(CameraTimelapseTriggerKind.Time, recording.TimelapseTrigger);
        Assert.Equal(1, recording.TimelapseEveryNthLayer);
        Assert.DoesNotContain("recording", Serialize(AppSettings.Default), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Layer", Serialize(new AppSettings { Recording = new CameraRecordingSettings { TimelapseIntervalSeconds = 60 } }), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void TheLayerStepIsWithinItsRange(int layers) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraRecordingSettings { TimelapseEveryNthLayer = layers });

    [Fact]
    public void AnUndefinedTriggerIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraRecordingSettings { TimelapseTrigger = (CameraTimelapseTriggerKind)7 });

    [Fact]
    public void OnlyWhatDiffersIsWrittenAndItRoundTrips()
    {
        AppSettings settings = new() { Recording = new CameraRecordingSettings { TimelapseTrigger = CameraTimelapseTriggerKind.Layer, TimelapseEveryNthLayer = 5 } };

        string json = Serialize(settings);
        AppSettings loaded = Load(json, out List<string> warnings);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            "{\"timelapseTrigger\":\"layer\",\"timelapseEveryNthLayer\":5}",
            document.RootElement.GetProperty("recording").GetRawText().Replace(" ", "", StringComparison.Ordinal).ReplaceLineEndings(""));
        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);

        // The layer trigger with every layer writes only the trigger.
        Assert.Contains("\"timelapseTrigger\":\"layer\"}", Serialize(new AppSettings { Recording = new CameraRecordingSettings { TimelapseTrigger = CameraTimelapseTriggerKind.Layer } }).Replace(" ", "", StringComparison.Ordinal).ReplaceLineEndings(""), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTriggerAndTheLayerStepTakePartInEquality()
    {
        Assert.NotEqual(CameraRecordingSettings.Default, CameraRecordingSettings.Default with { TimelapseTrigger = CameraTimelapseTriggerKind.Layer });
        Assert.NotEqual(CameraRecordingSettings.Default, CameraRecordingSettings.Default with { TimelapseEveryNthLayer = 2 });
    }

    [Theory]
    [InlineData("\"layer\"", CameraTimelapseTriggerKind.Layer)]
    [InlineData("\"Layer\"", CameraTimelapseTriggerKind.Layer)]
    [InlineData("\"time\"", CameraTimelapseTriggerKind.Time)]
    [InlineData("null", CameraTimelapseTriggerKind.Time)]
    public void ValidTriggersAreRead(string value, CameraTimelapseTriggerKind expected)
    {
        AppSettings settings = Load($"{{ \"recording\": {{ \"TimelapseTrigger\": {value} }} }}", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(expected, settings.Recording.TimelapseTrigger);
    }

    [Theory]
    [InlineData("\"layers\"")]
    [InlineData("1")]
    [InlineData("true")]
    public void AnInvalidTriggerIsTheTimeTriggerWithAWarning(string value)
    {
        AppSettings settings = Load($"{{ \"recording\": {{ \"timelapseTrigger\": {value}, \"timelapseIntervalSeconds\": 60 }} }}", out List<string> warnings);

        Assert.Equal(CameraTimelapseTriggerKind.Time, settings.Recording.TimelapseTrigger);
        Assert.Equal(60, settings.Recording.TimelapseIntervalSeconds);
        string warning = Assert.Single(warnings);
        Assert.Contains("Timelapse trigger is invalid", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("layers", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("2.5")]
    [InlineData("\"often\"")]
    [InlineData("false")]
    public void AnInvalidLayerStepIsEveryLayerWithAWarning(string value)
    {
        AppSettings settings = Load($"{{ \"recording\": {{ \"timelapseTrigger\": \"layer\", \"timelapseEveryNthLayer\": {value} }} }}", out List<string> warnings);

        Assert.Equal(CameraTimelapseTriggerKind.Layer, settings.Recording.TimelapseTrigger);
        Assert.Equal(1, settings.Recording.TimelapseEveryNthLayer);
        Assert.Contains("Timelapse layer step is invalid", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    [InlineData("\"3\"", 3)]
    public void ValidLayerStepsAreRead(string value, int expected)
    {
        AppSettings settings = Load($"{{ \"recording\": {{ \"timelapseEveryNthLayer\": {value} }} }}", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(expected, settings.Recording.TimelapseEveryNthLayer);
    }

    [Fact]
    public async Task TheFixtureLoadsAndSavesTheLayerTrigger()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-v2-timelapse-layer.json"), Path.Combine(_directory, JsonSettingsStore.FileName));
        JsonSettingsStore store = new(_directory);

        SettingsLoadResult result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(new CameraRecordingSettings { TimelapseTrigger = CameraTimelapseTriggerKind.Layer, TimelapseEveryNthLayer = 2 }, result.Settings.Recording);

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
