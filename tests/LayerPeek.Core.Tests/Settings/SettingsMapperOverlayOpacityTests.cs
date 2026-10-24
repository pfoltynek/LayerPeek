using System.Text.Json;

using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Settings;

using static LayerPeek.Core.Tests.Settings.SettingsMapperCameraOverlayTests;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>The global card background opacity, <c>cameraOverlay.backgroundOpacityPercent</c> (T-077).</summary>
public class SettingsMapperOverlayOpacityTests
{
    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "version": 1, "pollingIntervalSeconds": 5, "printers": [] }""")]
    [InlineData("""{ "cameraOverlay": null }""")]
    [InlineData("""{ "cameraOverlay": { } }""")]
    [InlineData("""{ "cameraOverlay": { "backgroundOpacityPercent": null } }""")]
    public void MissingOpacityMeansTheDefault(string json)
    {
        AppSettings settings = Load(json, out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.Equal(CameraOverlayOpacity.DefaultPercent, settings.CameraOverlayOpacityPercent);
    }

    [Fact]
    public void DefaultOpacityIsNotWritten()
    {
        string json = Write(new AppSettings());

        Assert.DoesNotContain("cameraOverlay", json, StringComparison.Ordinal);
        Assert.DoesNotContain("backgroundOpacity", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(94)]
    [InlineData(95)]
    public void OpacityRoundTripsAsAWholeNumber(int percent)
    {
        AppSettings settings = new() { CameraOverlayOpacityPercent = percent };
        string json = Write(settings);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(percent, document.RootElement.GetProperty("cameraOverlay").GetProperty("backgroundOpacityPercent").GetInt32());
        Assert.Equal(settings, Load(json, out List<string> warnings, out _));
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("29")]
    [InlineData("96")]
    [InlineData("0")]
    [InlineData("-50")]
    [InlineData("72.5")]
    [InlineData("1e300")]
    public void InvalidOpacityFallsBackToTheDefaultWithAWarning(string value)
    {
        AppSettings settings = Load($$"""{ "cameraOverlay": { "backgroundOpacityPercent": {{value}} } }""", out List<string> warnings, out _);

        Assert.Equal(CameraOverlayOpacity.DefaultPercent, settings.CameraOverlayOpacityPercent);
        string warning = Assert.Single(warnings);
        Assert.Contains("opacity", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("30–95", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void QuotedNumberIsAccepted()
    {
        AppSettings settings = Load("""{ "CameraOverlay": { "BackgroundOpacityPercent": "40" } }""", out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.Equal(40, settings.CameraOverlayOpacityPercent);
    }
}
