using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

using static LayerPeek.Core.Tests.Settings.SettingsMapperCameraOverlayTests;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// T-203: "Hardware decoding" of the camera video, <see cref="AppSettings.CameraHardwareDecoding"/> (global, automatic by
/// default) and <c>cameraHardwareDecoding</c> in the settings file and the diagnostic bundle.
/// </summary>
public sealed class CameraHardwareDecodingSettingsTests
{
    [Fact]
    public void AutomaticIsTheDefaultAndPartOfEquality()
    {
        Assert.Equal(CameraHardwareDecoding.Automatic, AppSettings.Default.CameraHardwareDecoding);
        Assert.NotEqual(new AppSettings(), new AppSettings { CameraHardwareDecoding = CameraHardwareDecoding.Off });
        Assert.Equal(new AppSettings { CameraHardwareDecoding = CameraHardwareDecoding.Off }, new AppSettings { CameraHardwareDecoding = CameraHardwareDecoding.Off });
        Assert.NotEqual(new AppSettings().GetHashCode(), new AppSettings { CameraHardwareDecoding = CameraHardwareDecoding.Off }.GetHashCode());
    }

    [Fact]
    public void AnUndefinedValueIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings { CameraHardwareDecoding = (CameraHardwareDecoding)7 });

    [Fact]
    public void TheDefaultIsNotWritten() =>
        Assert.DoesNotContain("cameraHardwareDecoding", Write(new AppSettings()), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void OffIsWrittenAndRoundTrips()
    {
        AppSettings settings = new() { CameraHardwareDecoding = CameraHardwareDecoding.Off };

        Assert.Contains("\"cameraHardwareDecoding\": \"Off\"", Write(settings), StringComparison.Ordinal);
        AppSettings loaded = RoundTrip(settings, out List<string> warnings);
        Assert.Empty(warnings);
        Assert.Equal(CameraHardwareDecoding.Off, loaded.CameraHardwareDecoding);
        Assert.Equal(settings, loaded);
    }

    [Theory]
    [InlineData("""{ "cameraHardwareDecoding": "off" }""", CameraHardwareDecoding.Off)]
    [InlineData("""{ "cameraHardwareDecoding": " OFF " }""", CameraHardwareDecoding.Off)]
    [InlineData("""{ "cameraHardwareDecoding": "Automatic" }""", CameraHardwareDecoding.Automatic)]
    public void TheNameIsReadInAnyCase(string json, CameraHardwareDecoding expected)
    {
        AppSettings settings = Load(json, out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.Equal(expected, settings.CameraHardwareDecoding);
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "version": 2, "pollingIntervalSeconds": 5, "printers": [] }""")]
    [InlineData("""{ "cameraHardwareDecoding": null }""")]
    public void AMissingValueIsTheDefault(string json)
    {
        AppSettings settings = Load(json, out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.Equal(CameraHardwareDecoding.Automatic, settings.CameraHardwareDecoding);
    }

    [Theory]
    [InlineData("\"videotoolbox\"")]
    [InlineData("\"1\"")]
    [InlineData("\"\"")]
    public void AnUnknownValueIsTheDefaultWithAWarning(string value)
    {
        AppSettings settings = Load($$"""{ "cameraHardwareDecoding": {{value}} }""", out List<string> warnings, out _);

        Assert.Equal(CameraHardwareDecoding.Automatic, settings.CameraHardwareDecoding);
        string warning = Assert.Single(warnings);
        Assert.Contains("hardware decoding", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDiagnosticBundleCarriesTheChoice()
    {
        Assert.Contains(
            "\"cameraHardwareDecoding\": \"Off\"",
            SettingsExport.ToSupportJson(new AppSettings { CameraHardwareDecoding = CameraHardwareDecoding.Off }),
            StringComparison.Ordinal);
        // The default is the missing value, as in the saved file.
        Assert.DoesNotContain("cameraHardwareDecoding", SettingsExport.ToSupportJson(new AppSettings()), StringComparison.Ordinal);
    }
}
