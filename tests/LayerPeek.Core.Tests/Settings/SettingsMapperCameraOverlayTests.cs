using System.Text.Json;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

namespace LayerPeek.Core.Tests.Settings;

/// <summary><c>printers[*].camera.overlay</c> (T-036) in the persisted shape.</summary>
public class SettingsMapperCameraOverlayTests
{
    private static readonly CameraOverlayOptions Customized = new()
    {
        Corner = CameraOverlayCorner.TopRight,
        Normal = CameraOverlayItems.PrinterName | CameraOverlayItems.NozzleTemperature | CameraOverlayItems.BedTemperature | CameraOverlayItems.FileName,
        Mini = CameraOverlayItems.Progress,
    };

    [Theory]
    [InlineData("")]
    [InlineData(""", "camera": { "enabled": false }""")]
    [InlineData(""", "camera": { "overlay": null }""")]
    [InlineData(""", "camera": { "overlay": { } }""")]
    [InlineData(""", "camera": { "overlay": { "corner": null, "normal": null, "mini": null } }""")]
    [InlineData(""", "camera": { "overlay": { "corner": "BottomLeft", "normal": ["State", "Progress", "RemainingTime"], "mini": ["RemainingTime", "State", "Progress"] } }""")]
    public void MissingOrDefaultOverlayValuesMeanDefaults(string camera)
    {
        AppSettings settings = Load(PrinterJson(camera), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Same(CameraOverlayOptions.Default, Assert.Single(settings.Printers).Camera.Overlay);
    }

    [Fact]
    public void OverlayRoundTrips()
    {
        AppSettings settings = WithOverlay(Customized);

        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
        Assert.Equal(Customized, Assert.Single(loaded.Printers).Camera.Overlay);
    }

    [Theory]
    [InlineData(CameraOverlayCorner.TopLeft)]
    [InlineData(CameraOverlayCorner.TopRight)]
    [InlineData(CameraOverlayCorner.BottomLeft)]
    [InlineData(CameraOverlayCorner.BottomRight)]
    public void EveryCornerRoundTrips(CameraOverlayCorner corner)
    {
        AppSettings settings = WithOverlay(Customized with { Corner = corner });

        Assert.Equal(corner, Assert.Single(RoundTrip(settings, out List<string> warnings).Printers).Camera.Overlay.Corner);
        Assert.Empty(warnings);
    }

    [Fact]
    public void EveryItemRoundTripsAlone()
    {
        foreach (CameraOverlayItems item in CameraOverlayOptions.Items)
        {
            AppSettings settings = WithOverlay(new CameraOverlayOptions { Normal = item, Mini = CameraOverlayOptions.AllItems & ~item });

            CameraOverlayOptions loaded = Assert.Single(RoundTrip(settings, out List<string> warnings).Printers).Camera.Overlay;

            Assert.Empty(warnings);
            Assert.Equal(item, loaded.Normal);
            Assert.Equal(CameraOverlayOptions.AllItems & ~item, loaded.Mini);
        }
    }

    [Fact]
    public void NoItemsRoundTripAsEmptyLists()
    {
        CameraOverlayOptions none = new() { Normal = CameraOverlayItems.None, Mini = CameraOverlayItems.None };
        string json = Write(WithOverlay(none));

        Assert.Contains("\"normal\": []", json, StringComparison.Ordinal);
        Assert.Equal(none, Assert.Single(Load(json, out List<string> warnings).Printers).Camera.Overlay);
        Assert.Empty(warnings);
    }

    [Fact]
    public void OverlayOfEnabledCameraRoundTrips()
    {
        AppSettings settings = WithCamera(new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) { Overlay = Customized });

        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void DefaultOverlayIsNotWritten()
    {
        string json = Write(WithOverlay(CameraOverlayOptions.Default));

        Assert.DoesNotContain("\"overlay\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultOverlayWithEqualValuesIsNotWritten()
    {
        string json = Write(WithOverlay(new CameraOverlayOptions { Corner = CameraOverlayCorner.BottomLeft, Normal = CameraOverlayOptions.DefaultItems }));

        Assert.DoesNotContain("\"overlay\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomizedOverlayIsWrittenWithAllValuesInDisplayOrder()
    {
        using JsonDocument json = JsonDocument.Parse(Write(WithOverlay(Customized)));

        JsonElement overlay = json.RootElement.GetProperty("printers")[0].GetProperty("camera").GetProperty("overlay");
        Assert.Equal(
            """{"corner":"TopRight","normal":["PrinterName","NozzleTemperature","BedTemperature","FileName"],"mini":["Progress"]}""",
            Compact(overlay));
    }

    [Fact]
    public void ChangedCornerAloneWritesTheSection()
    {
        using JsonDocument json = JsonDocument.Parse(Write(WithOverlay(CameraOverlayOptions.Default with { Corner = CameraOverlayCorner.TopLeft })));

        JsonElement overlay = json.RootElement.GetProperty("printers")[0].GetProperty("camera").GetProperty("overlay");
        Assert.Equal("""{"corner":"TopLeft","normal":["State","Progress","RemainingTime"],"mini":["State","Progress","RemainingTime"]}""", Compact(overlay));
    }

    [Fact]
    public void PartialOverlayKeepsOtherDefaults()
    {
        AppSettings settings = Load(PrinterJson(""", "camera": { "overlay": { "mini": ["PrinterName"] } }"""), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(CameraOverlayOptions.Default with { Mini = CameraOverlayItems.PrinterName }, Assert.Single(settings.Printers).Camera.Overlay);
    }

    [Fact]
    public void NamesAreCaseInsensitiveTrimmedAndDuplicatesAreHarmless()
    {
        AppSettings settings = Load(
            PrinterJson(""", "camera": { "overlay": { "corner": " topright ", "normal": [" filename", "FILENAME", "bedTemperature"] } }"""),
            out List<string> warnings);

        Assert.Empty(warnings);
        CameraOverlayOptions overlay = Assert.Single(settings.Printers).Camera.Overlay;
        Assert.Equal(CameraOverlayCorner.TopRight, overlay.Corner);
        Assert.Equal(CameraOverlayItems.FileName | CameraOverlayItems.BedTemperature, overlay.Normal);
    }

    [Theory]
    [InlineData("Middle")]
    [InlineData("Center")]
    [InlineData("1")]
    [InlineData("")]
    public void UnknownCornerUsesDefaultWithWarningWithoutEchoingIt(string value)
    {
        AppSettings settings = Load(
            PrinterJson($$""", "camera": { "overlay": { "corner": "{{value}}", "mini": ["FileName"] } }"""),
            out List<string> warnings);

        CameraOverlayOptions overlay = Assert.Single(settings.Printers).Camera.Overlay;
        Assert.Equal(CameraOverlayCorner.BottomLeft, overlay.Corner);
        Assert.Equal(CameraOverlayItems.FileName, overlay.Mini);
        string warning = Assert.Single(warnings);
        Assert.Contains("overlay", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("corner", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mk4", warning, StringComparison.Ordinal);
        if (value.Length > 1)
        {
            Assert.DoesNotContain(value, warning, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("\"Humidity\"")]
    [InlineData("\"16\"")]
    [InlineData("\"State, Progress\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void UnknownItemIsIgnoredWithWarningAndTheOthersAreKept(string unknown)
    {
        AppSettings settings = Load(
            PrinterJson($$""", "camera": { "overlay": { "normal": ["State", {{unknown}}, "FileName"] } }"""),
            out List<string> warnings);

        CameraOverlayOptions overlay = Assert.Single(settings.Printers).Camera.Overlay;
        Assert.Equal(CameraOverlayItems.State | CameraOverlayItems.FileName, overlay.Normal);
        Assert.Equal(CameraOverlayOptions.DefaultItems, overlay.Mini);
        string warning = Assert.Single(warnings);
        Assert.Contains("normal window", warning, StringComparison.Ordinal);
        Assert.Contains("1 unknown item", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("Humidity", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralUnknownItemsGiveOneWarningPerList()
    {
        AppSettings settings = Load(
            PrinterJson(""", "camera": { "overlay": { "corner": "x", "normal": ["a", "b"], "mini": ["c", "Progress"] } }"""),
            out List<string> warnings);

        CameraOverlayOptions overlay = Assert.Single(settings.Printers).Camera.Overlay;
        Assert.Equal(CameraOverlayItems.None, overlay.Normal);
        Assert.Equal(CameraOverlayItems.Progress, overlay.Mini);
        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, static warning => warning.Contains("2 unknown items", StringComparison.Ordinal));
        Assert.Contains(warnings, static warning => warning.Contains("mini preview", StringComparison.Ordinal));
    }

    [Fact]
    public void TooLongPrinterIdIsShortenedInTheWarning()
    {
        string id = new('i', 100);
        AppSettings settings = Load(
            $$"""{ "printers": [ { "id": "{{id}}", "connectionType": "PrusaLink", "address": "a", "camera": { "overlay": { "corner": "x" } } } ] }""",
            out List<string> warnings);

        Assert.Single(settings.Printers);
        Assert.DoesNotContain(id, Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayOfDisabledCameraIsKept()
    {
        AppSettings settings = Load(
            PrinterJson(""", "camera": { "enabled": false, "streamUrl": "rtsp://192.168.0.227/live", "overlay": { "corner": "TopLeft" } }"""),
            out List<string> warnings);

        CameraSettings camera = Assert.Single(settings.Printers).Camera;
        Assert.Empty(warnings);
        Assert.False(camera.Enabled);
        Assert.Equal(CameraOverlayCorner.TopLeft, camera.Overlay.Corner);
    }

    [Fact]
    public void OverlayWithoutAddressIsKept()
    {
        AppSettings settings = Load(PrinterJson(""", "camera": { "overlay": { "mini": [] } }"""), out List<string> warnings);

        CameraSettings camera = Assert.Single(settings.Printers).Camera;
        Assert.Empty(warnings);
        Assert.Null(camera.StreamUri);
        Assert.Equal(CameraOverlayItems.None, camera.Overlay.Mini);
        Assert.Equal(camera, RoundTrip(settings, out _).Printers[0].Camera);
    }

    [Theory]
    [InlineData("http://192.168.0.227/live")]
    [InlineData("rtsp://admin:secret@192.168.0.227/live")]
    public void OverlayIsKeptWhenAddressIsInvalidOrLosesCredentials(string url)
    {
        AppSettings settings = Load(
            PrinterJson($$""", "camera": { "enabled": true, "streamUrl": "{{url}}", "overlay": { "corner": "TopLeft" }, "window": { "startMode": "Mini" } }"""),
            out List<string> warnings);

        CameraSettings camera = Assert.Single(settings.Printers).Camera;
        Assert.False(camera.Enabled);
        Assert.Equal(CameraOverlayCorner.TopLeft, camera.Overlay.Corner);
        Assert.Equal(CameraWindowStartMode.Mini, camera.Window.StartMode);
        Assert.Single(warnings);
    }

    [Fact]
    public void SkippedPrinterGivesNoOverlayWarning()
    {
        AppSettings settings = Load(
            """{ "printers": [ { "id": "mk4", "connectionType": "PrusaLink", "camera": { "overlay": { "corner": "x" } } } ] }""",
            out List<string> warnings);

        Assert.Empty(settings.Printers);
        Assert.Contains("skipped", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(""", "camera": { "overlay": 5 }""")]
    [InlineData(""", "camera": { "overlay": { "normal": "State" } }""")]
    [InlineData(""", "camera": { "overlay": { "mini": [1] } }""")]
    [InlineData(""", "camera": { "overlay": { "corner": 2 } }""")]
    public void WrongJsonTypeOfOverlayFailsLikeOtherValues(string camera) =>
        Assert.ThrowsAny<JsonException>(() => Load(PrinterJson(camera), out _));

    private static string PrinterJson(string camera) =>
        $$"""{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226"{{camera}} } ] }""";

    private static AppSettings WithOverlay(CameraOverlayOptions overlay) => WithCamera(CameraSettings.Disabled with { Overlay = overlay });

    private static AppSettings WithCamera(CameraSettings camera) => new()
    {
        Printers = [new PrinterDefinition { Id = "mk4", Name = "MK4", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.0.226", Camera = camera }],
    };

    private static string Compact(JsonElement element)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            element.WriteTo(writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Write(AppSettings settings) =>
        JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);

    private static AppSettings RoundTrip(AppSettings settings, out List<string> warnings) => Load(Write(settings), out warnings);

    private static AppSettings Load(string json, out List<string> warnings)
    {
        SettingsDocument document = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsDocument)!;
        warnings = [];
        return SettingsMapper.ToSettings(document, warnings);
    }
}
