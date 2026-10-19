using System.Text.Json;

using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;
using PrusaTrayDiagnostic.Core.Settings.Storage;

namespace PrusaTrayDiagnostic.Core.Tests.Settings;

/// <summary><c>printers[*].camera.window</c> (T-035) in the persisted shape.</summary>
public class SettingsMapperCameraWindowTests
{
    private static readonly CameraWindowDefaults Customized = new()
    {
        RememberLastPlacement = false,
        StartMode = CameraWindowStartMode.FullScreen,
        Monitor = new CameraMonitorId(@"\\.\DISPLAY2", 2560, 1440),
        Normal = new CameraWindowLayout(new CameraWindowSize(1280, 720), CameraWindowCorner.TopRight),
        Mini = new CameraWindowLayout(new CameraWindowSize(333, 222), CameraWindowCorner.BottomLeft),
    };

    [Theory]
    [InlineData("")]
    [InlineData(""", "camera": { "enabled": false }""")]
    [InlineData(""", "camera": { "window": null }""")]
    [InlineData(""", "camera": { "window": { } }""")]
    [InlineData(""", "camera": { "window": { "rememberLastPlacement": null, "startMode": null, "monitor": null, "normal": null, "mini": null } }""")]
    [InlineData(""", "camera": { "window": { "normal": { }, "mini": { "width": null, "height": null, "corner": null } } }""")]
    public void MissingWindowValuesMeanDefaults(string camera)
    {
        AppSettings settings = Load(PrinterJson(camera), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(CameraWindowDefaults.Default, Assert.Single(settings.Printers).Camera.Window);
    }

    [Fact]
    public void WindowRoundTrips()
    {
        AppSettings settings = WithWindow(Customized);

        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
        Assert.Equal(Customized, Assert.Single(loaded.Printers).Camera.Window);
    }

    [Fact]
    public void WindowWithoutMonitorRoundTrips()
    {
        AppSettings settings = WithWindow(Customized with { Monitor = null });

        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Null(Assert.Single(loaded.Printers).Camera.Window.Monitor);
    }

    [Fact]
    public void WindowOfEnabledCameraRoundTrips()
    {
        AppSettings settings = WithCamera(new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) { Window = Customized });

        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void DefaultWindowIsNotWritten()
    {
        string json = Write(WithWindow(CameraWindowDefaults.Default));

        Assert.DoesNotContain("\"window\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomizedWindowIsWrittenWithAllValues()
    {
        using JsonDocument json = JsonDocument.Parse(Write(WithWindow(Customized)));

        JsonElement window = json.RootElement.GetProperty("printers")[0].GetProperty("camera").GetProperty("window");
        Assert.Equal(
            """{"rememberLastPlacement":false,"startMode":"FullScreen","monitor":{"displayName":"\\\\.\\DISPLAY2","width":2560,"height":1440},"normal":{"width":1280,"height":720,"corner":"TopRight"},"mini":{"width":333,"height":222,"corner":"BottomLeft"}}""",
            Compact(window));
    }

    [Fact]
    public void PrimaryMonitorIsNotWritten()
    {
        string json = Write(WithWindow(Customized with { Monitor = null }));

        Assert.Contains("\"window\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"monitor\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PartialWindowKeepsOtherDefaults()
    {
        AppSettings settings = Load(PrinterJson(""", "camera": { "window": { "startMode": "Mini", "normal": { "corner": "TopLeft" } } }"""), out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(
            CameraWindowDefaults.Default with
            {
                StartMode = CameraWindowStartMode.Mini,
                Normal = CameraWindowDefaults.Default.Normal with { Corner = CameraWindowCorner.TopLeft },
            },
            Assert.Single(settings.Printers).Camera.Window);
    }

    [Fact]
    public void NamesAreCaseInsensitiveAndNumbersMayBeQuoted()
    {
        AppSettings settings = Load(
            PrinterJson(""", "camera": { "window": { "startMode": " fullscreen ", "normal": { "width": "640", "height": "360", "corner": "bottomright" } } }"""),
            out List<string> warnings);

        Assert.Empty(warnings);
        CameraWindowDefaults window = Assert.Single(settings.Printers).Camera.Window;
        Assert.Equal(CameraWindowStartMode.FullScreen, window.StartMode);
        Assert.Equal(new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.BottomRight), window.Normal);
    }

    [Theory]
    [InlineData("Maximized")]
    [InlineData("1")]
    [InlineData("")]
    public void UnknownStartModeUsesDefaultWithWarning(string value)
    {
        AppSettings settings = Load(PrinterJson($$""", "camera": { "window": { "startMode": "{{value}}", "rememberLastPlacement": false } }"""), out List<string> warnings);

        CameraWindowDefaults window = Assert.Single(settings.Printers).Camera.Window;
        Assert.Equal(CameraWindowStartMode.Last, window.StartMode);
        Assert.False(window.RememberLastPlacement);
        string warning = Assert.Single(warnings);
        Assert.Contains("start mode", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mk4", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("Maximized", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Middle")]
    [InlineData("4")]
    public void UnknownCornerUsesDefaultCornerWithWarning(string value)
    {
        AppSettings settings = Load(
            PrinterJson($$""", "camera": { "window": { "mini": { "width": 400, "height": 225, "corner": "{{value}}" } } }"""),
            out List<string> warnings);

        Assert.Equal(
            new CameraWindowLayout(new CameraWindowSize(400, 225), CameraWindowDefaults.Default.Mini.Corner),
            Assert.Single(settings.Printers).Camera.Window.Mini);
        Assert.Contains("mini preview", Assert.Single(warnings), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("159", "541")]
    [InlineData("961", "89")]
    [InlineData("16385", "541")]
    [InlineData("961", "1e300")]
    [InlineData("-961", "541")]
    [InlineData("960.5", "541")]
    [InlineData("961", "null")]
    [InlineData("null", "541")]
    public void InvalidSizeUsesDefaultSizeWithWarningAndKeepsCorner(string width, string height)
    {
        AppSettings settings = Load(
            PrinterJson($$""", "camera": { "window": { "normal": { "width": {{width}}, "height": {{height}}, "corner": "TopLeft" }, "mini": { "width": 400, "height": 225 } } }"""),
            out List<string> warnings);

        CameraWindowDefaults window = Assert.Single(settings.Printers).Camera.Window;
        Assert.Equal(new CameraWindowLayout(CameraWindowDefaults.Default.Normal.Size, CameraWindowCorner.TopLeft), window.Normal);
        Assert.Equal(new CameraWindowSize(400, 225), window.Mini.Size);
        string warning = Assert.Single(warnings);
        Assert.Contains("normal window size", warning, StringComparison.OrdinalIgnoreCase);
        // The values from the file are not echoed (the default size is).
        Assert.DoesNotContain(width.TrimStart('-'), warning, StringComparison.Ordinal);
        Assert.DoesNotContain(height, warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(double.NaN, 540)]
    [InlineData(960, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, double.NaN)]
    public void NonFiniteSizeUsesDefaultSizeWithWarning(double width, double height)
    {
        SettingsDocument document = new()
        {
            Printers =
            [
                new PrinterDocument
                {
                    Id = "mk4",
                    ConnectionType = "PrusaLink",
                    Address = "192.168.0.226",
                    Camera = new CameraDocument { Window = new CameraWindowDocument { Mini = new CameraWindowLayoutDocument { Width = width, Height = height } } },
                },
            ],
        };
        List<string> warnings = [];

        AppSettings settings = SettingsMapper.ToSettings(document, warnings);

        Assert.Equal(CameraWindowDefaults.Default.Mini, Assert.Single(settings.Printers).Camera.Window.Mini);
        Assert.Contains("mini preview size", Assert.Single(warnings), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{ "displayName": "", "width": 1920, "height": 1080 }""")]
    [InlineData("""{ "displayName": "SECRETNAME", "width": 0, "height": 1080 }""")]
    [InlineData("""{ "displayName": "SECRETNAME", "width": 1920 }""")]
    [InlineData("""{ "displayName": "SECRETNAME", "width": 1920.5, "height": 1080 }""")]
    [InlineData("""{ "displayName": "SECRETNAME", "width": 1e300, "height": 1080 }""")]
    [InlineData("""{ "width": 1920, "height": 1080 }""")]
    [InlineData("""{ "displayName": "SECRET\u0007NAME", "width": 1920, "height": 1080 }""")]
    [InlineData("""{ }""")]
    public void InvalidMonitorMeansPrimaryWithWarningWithoutEchoingIt(string monitor)
    {
        AppSettings settings = Load(PrinterJson($$""", "camera": { "window": { "monitor": {{monitor}}, "startMode": "Mini" } }"""), out List<string> warnings);

        CameraWindowDefaults window = Assert.Single(settings.Printers).Camera.Window;
        Assert.Null(window.Monitor);
        Assert.Equal(CameraWindowStartMode.Mini, window.StartMode);
        string warning = Assert.Single(warnings);
        Assert.Contains("monitor", warning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void TooLongMonitorNameMeansPrimaryWithWarning()
    {
        string name = new('x', CameraMonitorId.MaxDisplayNameLength + 1);

        AppSettings settings = Load(PrinterJson($$""", "camera": { "window": { "monitor": { "displayName": "{{name}}", "width": 1920, "height": 1080 } } }"""), out List<string> warnings);

        Assert.Null(Assert.Single(settings.Printers).Camera.Window.Monitor);
        Assert.DoesNotContain(name, Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralInvalidValuesGiveOneWarningEach()
    {
        AppSettings settings = Load(
            PrinterJson(""", "camera": { "window": { "startMode": "x", "monitor": { }, "normal": { "width": 1 }, "mini": { "corner": "x" } } }"""),
            out List<string> warnings);

        Assert.Equal(CameraWindowDefaults.Default, Assert.Single(settings.Printers).Camera.Window);
        Assert.Equal(4, warnings.Count);
    }

    [Fact]
    public void WindowOfDisabledCameraIsKept()
    {
        AppSettings settings = Load(
            PrinterJson(""", "camera": { "enabled": false, "streamUrl": "rtsp://192.168.0.227/live", "window": { "startMode": "Mini" } }"""),
            out List<string> warnings);

        CameraSettings camera = Assert.Single(settings.Printers).Camera;
        Assert.Empty(warnings);
        Assert.False(camera.Enabled);
        Assert.Equal(CameraWindowStartMode.Mini, camera.Window.StartMode);
    }

    [Theory]
    [InlineData("http://192.168.0.227/live")]
    [InlineData("rtsp://admin:secret@192.168.0.227/live")]
    public void WindowIsKeptWhenAddressIsInvalidOrLosesCredentials(string url)
    {
        AppSettings settings = Load(
            PrinterJson($$""", "camera": { "enabled": true, "streamUrl": "{{url}}", "window": { "startMode": "Mini" } }"""),
            out List<string> warnings);

        CameraSettings camera = Assert.Single(settings.Printers).Camera;
        Assert.False(camera.Enabled);
        Assert.Equal(CameraWindowStartMode.Mini, camera.Window.StartMode);
        Assert.Single(warnings);
    }

    [Fact]
    public void SkippedPrinterGivesNoWindowWarning()
    {
        AppSettings settings = Load(
            """{ "printers": [ { "id": "mk4", "connectionType": "PrusaLink", "camera": { "window": { "startMode": "x" } } } ] }""",
            out List<string> warnings);

        Assert.Empty(settings.Printers);
        Assert.Contains("skipped", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void WrongJsonTypeOfWindowFailsLikeOtherValues() =>
        Assert.ThrowsAny<JsonException>(() => Load(PrinterJson(""", "camera": { "window": 5 }"""), out _));

    private static string PrinterJson(string camera) =>
        $$"""{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226"{{camera}} } ] }""";

    private static AppSettings WithWindow(CameraWindowDefaults window) => WithCamera(CameraSettings.Disabled with { Window = window });

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
