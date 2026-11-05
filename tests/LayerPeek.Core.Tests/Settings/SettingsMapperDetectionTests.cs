using System.Text.Json;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Detection;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// T-153: the <c>detection</c> section and <c>printers[*].detection</c>: defaults (off) are not written, values round-trip,
/// invalid values fall back to their defaults with a warning, the sampling interval is clamped into 2–5 s.
/// </summary>
public sealed class SettingsMapperDetectionTests
{
    private static PrinterDefinition Printer(PrinterDetectionSettings? detection = null) => new()
    {
        Id = "core-one",
        Name = "Core One",
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.10",
        Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.11/live")),
        Detection = detection ?? PrinterDetectionSettings.Default,
    };

    private static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);

    private static AppSettings Load(string json, out List<string> warnings)
    {
        SettingsDocument document = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsDocument)!;
        warnings = [];
        return SettingsMapper.ToSettings(document, warnings, notices: []);
    }

    private static PrinterDetectionSettings LoadPrinter(string detection, out List<string> warnings) =>
        Load($$"""{ "printers": [ { "id": "core-one", "name": "Core One", "connectionType": "PrusaLink", "address": "192.168.0.10", "detection": {{detection}} } ] }""", out warnings)
            .Printers[0].Detection;

    [Fact]
    public void DefaultsAreOffAndNotWritten()
    {
        string json = Serialize(new AppSettings { Printers = [Printer()] });

        Assert.DoesNotContain("detection", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(PrintFailureDetectionSettings.Default.Enabled);
        Assert.Equal(PrintFailureAlarmRepeat.OncePerPrint, PrintFailureDetectionSettings.Default.AlarmRepeat);
        Assert.True(PrintFailureDetectionSettings.Default.SaveSnapshots);
        Assert.Equal(TimeSpan.FromSeconds(2), PrintFailureDetectionSettings.Default.SampleInterval);
        Assert.False(PrinterDetectionSettings.Default.Enabled);
        Assert.Null(PrinterDetectionSettings.Default.Region);
        Assert.Equal(DetectorSettings.Default, PrinterDetectionSettings.Default.For("bed-change"));
    }

    [Fact]
    public void SettingsRoundTripAndOnlyWhatDiffersIsWritten()
    {
        AppSettings settings = new()
        {
            Detection = new PrintFailureDetectionSettings
            {
                Enabled = true,
                AlarmRepeat = PrintFailureAlarmRepeat.WhilePersisting,
                SaveSnapshots = false,
                SampleInterval = TimeSpan.FromSeconds(3.5),
            },
            Printers =
            [
                Printer(new PrinterDetectionSettings
                {
                    Enabled = true,
                    Region = new DetectionRegion(0.1, 0.5, 0.8, 0.5),
                    Detectors = new Dictionary<string, DetectorSettings>
                    {
                        ["bed-change"] = new() { Sensitivity = 70 },
                        ["spaghetti"] = new() { Enabled = false },
                        ["default-only"] = DetectorSettings.Default,
                    },
                }),
            ],
        };

        string json = Serialize(settings);
        AppSettings loaded = Load(json, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(settings.Detection, loaded.Detection);
        PrinterDetectionSettings detection = loaded.Printers[0].Detection;
        Assert.True(detection.Enabled);
        Assert.Equal(new DetectionRegion(0.1, 0.5, 0.8, 0.5), detection.Region);
        Assert.Equal(70, detection.For("bed-change").Sensitivity);
        Assert.False(detection.For("spaghetti").Enabled);
        // T-167: every entry is the user's choice (what differs from the detector's own default, which only the app knows):
        // one equal to DetectorSettings.Default keeps "enabled": true and comes back, the others only what differs.
        Assert.Equal(3, detection.Detectors.Count);
        Assert.Equal(DetectorSettings.Default, detection.Detectors["default-only"]);
        Assert.Equal(settings.Printers[0].Detection, detection);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement printer = document.RootElement.GetProperty("printers")[0].GetProperty("detection");
        Assert.True(printer.GetProperty("detectors").GetProperty("default-only").GetProperty("enabled").GetBoolean());
        Assert.False(printer.GetProperty("detectors").GetProperty("default-only").TryGetProperty("sensitivity", out _));
        Assert.False(printer.GetProperty("detectors").GetProperty("bed-change").TryGetProperty("enabled", out _));
        Assert.Equal(3.5, document.RootElement.GetProperty("detection").GetProperty("sampleIntervalSeconds").GetDouble());
    }

    [Theory]
    [InlineData("1", 2)]
    [InlineData("0", 2)]
    [InlineData("-3", 2)]
    [InlineData("9", 5)]
    [InlineData("1e9", 5)]
    [InlineData("\"4\"", 4)]
    [InlineData("2.5", 2.5)]
    public void TheSamplingIntervalIsClampedIntoTwoToFiveSeconds(string seconds, double expected)
    {
        AppSettings settings = Load($$"""{ "detection": { "sampleIntervalSeconds": {{seconds}} } }""", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(TimeSpan.FromSeconds(expected), settings.Detection.SampleInterval);
        Assert.Equal(TimeSpan.FromSeconds(2), new PrintFailureDetectionSettings { SampleInterval = TimeSpan.FromMilliseconds(10) }.SampleInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), new PrintFailureDetectionSettings { SampleInterval = TimeSpan.FromHours(1) }.SampleInterval);
    }

    [Theory]
    [InlineData("""{ "enabled": "yes" }""", "master switch")]
    [InlineData("""{ "alarmRepeat": "Always" }""", "alarm repeat")]
    [InlineData("""{ "saveSnapshots": 1 }""", "snapshots")]
    [InlineData("""{ "sampleIntervalSeconds": "often" }""", "sampling interval")]
    [InlineData("""[]""", "detection settings are invalid")]
    public void InvalidGlobalValuesAreTheirDefaultsWithAWarning(string section, string warning)
    {
        AppSettings settings = Load($$"""{ "detection": {{section}} }""", out List<string> warnings);

        Assert.Contains(warning, Assert.Single(warnings), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PrintFailureDetectionSettings.Default, settings.Detection);
    }

    [Fact]
    public void NamesAreCaseInsensitive()
    {
        AppSettings settings = Load("""{ "DETECTION": { "Enabled": true, "AlarmRepeat": "whilepersisting" } }""", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.True(settings.Detection.Enabled);
        Assert.Equal(PrintFailureAlarmRepeat.WhilePersisting, settings.Detection.AlarmRepeat);
    }

    [Theory]
    [InlineData("""{ "x": 0.2, "y": 0.3, "width": 0.5 }""")]
    [InlineData("""{ "x": -0.1, "y": 0, "width": 1, "height": 1 }""")]
    [InlineData("""{ "x": 0, "y": 0, "width": 0, "height": 1 }""")]
    [InlineData("""{ "x": 0, "y": 0, "width": 1.5, "height": 1 }""")]
    [InlineData("""[0, 0, 1, 1]""")]
    public void AnInvalidRegionIsTheDefaultWithAWarning(string region)
    {
        PrinterDetectionSettings detection = LoadPrinter($$"""{ "enabled": true, "region": {{region}} }""", out List<string> warnings);

        Assert.True(detection.Enabled);
        Assert.Null(detection.Region);
        Assert.Contains("bed region", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void ARegionPastTheEdgeIsClippedIntoTheFrame()
    {
        PrinterDetectionSettings detection = LoadPrinter("""{ "region": { "x": 0.6, "y": 0.5, "width": 0.6, "height": 0.5 } }""", out List<string> warnings);

        Assert.Empty(warnings);
        DetectionRegion region = detection.Region!.Value;
        Assert.Equal((0.6, 0.5), (region.X, region.Y));
        Assert.Equal(0.4, region.Width, precision: 9);
    }

    [Theory]
    [InlineData("""{ "detectors": { "Bed Change": { "sensitivity": 10 } } }""", "entry of the detector settings")]
    [InlineData("""{ "detectors": { "bed-change": { "sensitivity": 101 } } }""", "sensitivity")]
    [InlineData("""{ "detectors": { "bed-change": { "sensitivity": 5.5 } } }""", "sensitivity")]
    [InlineData("""{ "detectors": { "bed-change": { "enabled": "no" } } }""", "detector 'bed-change'")]
    [InlineData("""{ "detectors": [] }""", "detector settings")]
    [InlineData("""{ "enabled": 1 }""", "print failure detection is invalid")]
    [InlineData("""{ "darkFilament": "yes" }""", "dark filament mode")]
    [InlineData("""true""", "detection is off")]
    public void InvalidPrinterValuesAreTheirDefaultsWithAWarning(string detection, string warning)
    {
        PrinterDetectionSettings loaded = LoadPrinter(detection, out List<string> warnings);

        Assert.Contains(warning, Assert.Single(warnings), StringComparison.Ordinal);
        Assert.Equal(PrinterDetectionSettings.Default, loaded);
    }

    [Fact]
    public void ADetectorSwitchedOnExplicitlyIsKeptAlsoAtTheDefaults()
    {
        // T-167: a detector that is off by default ("air-printing") switched on by the user; an empty entry is no choice.
        PrinterDetectionSettings detection = LoadPrinter("""{ "detectors": { "air-printing": { "enabled": true }, "bed-change": {} } }""", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(DetectorSettings.Default, Assert.Single(detection.Detectors, static d => d.Key == "air-printing").Value);
        Assert.False(detection.Detectors.ContainsKey("bed-change"));
    }

    [Fact]
    public void UnknownDetectorsOfANewerVersionAreKept()
    {
        PrinterDetectionSettings detection = LoadPrinter("""{ "detectors": { "first-layer": { "sensitivity": 20 } }, "future": 1 }""", out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(20, detection.For("first-layer").Sensitivity);
    }

    /// <summary>T-163: the experimental dark filament mode is off by default, written only while on, and round-trips.</summary>
    [Fact]
    public void TheDarkFilamentModeIsWrittenOnlyWhileOn()
    {
        Assert.False(PrinterDetectionSettings.Default.DarkFilament);
        Assert.DoesNotContain("darkFilament", Serialize(new AppSettings { Printers = [Printer(new PrinterDetectionSettings { Enabled = true })] }), StringComparison.Ordinal);

        string json = Serialize(new AppSettings { Printers = [Printer(new PrinterDetectionSettings { DarkFilament = true })] });
        Assert.Matches("\"darkFilament\":\\s*true", json);

        PrinterDetectionSettings loaded = Load(json, out List<string> warnings).Printers[0].Detection;
        Assert.Empty(warnings);
        Assert.True(loaded.DarkFilament);
        Assert.NotEqual(PrinterDetectionSettings.Default, loaded);
        Assert.False(LoadPrinter("""{ "enabled": true }""", out _).DarkFilament);
    }
}
