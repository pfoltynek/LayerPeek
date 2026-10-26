using System.Text.Json;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

namespace LayerPeek.Core.Tests.Settings;

/// <summary><c>printers[*].camera.overlay</c> in the widget shape of settings format 2 (T-077).</summary>
public class SettingsMapperCameraOverlayTests
{
    private const string Future = "future-widget";

    private static readonly CameraOverlayLayout Customized = CameraOverlayLayout.Default
        .MoveTo(OverlayWidgetIds.Temperatures, OverlayAnchor.TopRight, 0)
        .MoveTo(OverlayWidgetIds.Cooldown, OverlayAnchor.BottomLeft, 0)
        .WithSize(OverlayWidgetIds.Status, OverlayWidgetSize.Small)
        .WithSize(OverlayWidgetIds.Times, OverlayWidgetSize.Large)
        .WithEmphasized(OverlayWidgetIds.Times, true)
        .WithVisible(OverlayWidgetIds.PrintName, false);

    [Theory]
    [InlineData("")]
    [InlineData(""", "camera": { "enabled": false }""")]
    [InlineData(""", "camera": { "overlay": null }""")]
    [InlineData(""", "camera": { "overlay": { } }""")]
    [InlineData(""", "camera": { "overlay": { "widgets": null } }""")]
    [InlineData(""", "camera": { "overlay": { "widgets": [] } }""")]
    [InlineData(""", "camera": { "overlay": { "widgets": [ { "id": "status" }, { "id": "times", "anchor": "BottomLeft" } ] } }""")]
    public void MissingOrDefaultOverlayValuesMeanTheDefaultLayout(string camera)
    {
        AppSettings settings = Load(PrinterJson(camera), out List<string> warnings, out List<string> notices);

        Assert.Empty(warnings);
        Assert.Empty(notices);
        Assert.Same(CameraOverlayLayout.Default, Assert.Single(settings.Printers).Camera.Overlay);
    }

    [Fact]
    public void LayoutRoundTrips()
    {
        AppSettings settings = WithLayout(Customized);

        AppSettings loaded = RoundTrip(settings, out List<string> warnings);

        Assert.Empty(warnings);
        Assert.Equal(settings, loaded);
        Assert.Equal(Customized, Assert.Single(loaded.Printers).Camera.Overlay);
    }

    [Fact]
    public void EveryAnchorSizeAndFlagRoundTrips()
    {
        string[] ids = [.. OverlayWidgetIds.Known];
        foreach (OverlayAnchor anchor in OverlayAnchors.All)
        {
            foreach (OverlayWidgetSize size in (OverlayWidgetSize[])[OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large])
            {
                for (int flags = 0; flags < 4; flags++)
                {
                    string id = ids[(flags + (int)size) % ids.Length];
                    CameraOverlayLayout layout = CameraOverlayLayout.Default.MoveTo(id, anchor, 0).WithSize(id, size)
                        .WithEmphasized(id, (flags & 1) != 0).WithVisible(id, (flags & 2) != 0);

                    CameraOverlayLayout loaded = Assert.Single(RoundTrip(WithLayout(layout), out List<string> warnings).Printers).Camera.Overlay;

                    Assert.Empty(warnings);
                    Assert.Equal(layout, loaded);
                }
            }
        }
    }

    [Fact]
    public void LayoutOfEnabledCameraRoundTrips()
    {
        AppSettings settings = WithCamera(new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) { Overlay = Customized });

        Assert.Equal(settings, RoundTrip(settings, out List<string> warnings));
        Assert.Empty(warnings);
    }

    [Fact]
    public void DefaultLayoutIsNotWritten()
    {
        Assert.DoesNotContain("\"overlay\"", Write(WithLayout(CameraOverlayLayout.Default)), StringComparison.Ordinal);

        // Equal to the default when only the order across anchors differs.
        IReadOnlyList<OverlayWidgetPlacement> d = CameraOverlayLayout.Default.Widgets;
        Assert.DoesNotContain("\"overlay\"", Write(WithLayout(new CameraOverlayLayout([d[4], d[0], d[2], d[1], d[3]]))), StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedLayoutIsWrittenAsTheWholeWidgetListInLayoutOrder()
    {
        using JsonDocument json = JsonDocument.Parse(Write(WithLayout(CameraOverlayLayout.Default.WithVisible(OverlayWidgetIds.Cooldown, false))));

        JsonElement overlay = json.RootElement.GetProperty("printers")[0].GetProperty("camera").GetProperty("overlay");
        Assert.Equal(
            """{"widgets":[""" +
            """{"id":"status","anchor":"TopLeft","size":"Medium","emphasized":true,"visible":true},""" +
            """{"id":"print-name","anchor":"TopCenter","size":"Medium","emphasized":false,"visible":true},""" +
            """{"id":"times","anchor":"BottomLeft","size":"Medium","emphasized":false,"visible":true},""" +
            """{"id":"cooldown","anchor":"BottomLeft","size":"Medium","emphasized":false,"visible":false},""" +
            """{"id":"temperatures","anchor":"BottomRight","size":"Medium","emphasized":false,"visible":true}]}""",
            Compact(overlay));
    }

    [Fact]
    public void UnknownWidgetIdIsKeptAndWrittenBack()
    {
        string camera = """, "camera": { "overlay": { "widgets": [ { "id": "future-widget", "anchor": "TopRight", "size": "Large", "emphasized": true, "visible": false } ] } }""";

        AppSettings settings = Load(PrinterJson(camera), out List<string> warnings, out _);

        Assert.Empty(warnings);
        CameraOverlayLayout layout = Assert.Single(settings.Printers).Camera.Overlay;
        Assert.Equal(new OverlayWidgetPlacement(Future, OverlayAnchor.TopRight, OverlayWidgetSize.Large, emphasized: true, visible: false), layout.Find(Future));
        Assert.Equal(CameraOverlayLayout.Default.Widgets, layout.Widgets.Where(static w => w.WidgetId != Future));
        Assert.Contains("""{"id":"future-widget","anchor":"TopRight","size":"Large","emphasized":true,"visible":false}""", Compact(Write(settings)), StringComparison.Ordinal);
        Assert.Equal(settings, RoundTrip(settings, out _));
    }

    [Fact]
    public void UnknownWidgetWithoutValuesUsesTheMainAnchorAndNeutralDefaults()
    {
        AppSettings settings = Load(PrinterJson(""", "camera": { "overlay": { "widgets": [ { "id": "future-widget" } ] } }"""), out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.Equal(new OverlayWidgetPlacement(Future, OverlayAnchor.BottomLeft), settings.Printers[0].Camera.Overlay.Find(Future));
    }

    [Fact]
    public void KnownWidgetsMissingFromTheListAreAddedAtTheirDefaultPlacement()
    {
        AppSettings settings = Load(
            PrinterJson(""", "camera": { "overlay": { "widgets": [ { "id": "temperatures", "anchor": "BottomLeft" }, { "id": "times", "anchor": "BottomLeft", "visible": false } ] } }"""),
            out List<string> warnings,
            out _);

        Assert.Empty(warnings);
        CameraOverlayLayout layout = settings.Printers[0].Camera.Overlay;
        Assert.Equal(["temperatures", "times", "cooldown"], layout.InAnchor(OverlayAnchor.BottomLeft).Select(static w => w.WidgetId));
        Assert.Equal(CameraOverlayLayout.DefaultPlacement(OverlayWidgetIds.Status), layout.Find(OverlayWidgetIds.Status));
        Assert.Equal(CameraOverlayLayout.DefaultPlacement(OverlayWidgetIds.PrintName), layout.Find(OverlayWidgetIds.PrintName));
        Assert.False(layout.Find(OverlayWidgetIds.Times)!.Visible);
    }

    [Fact]
    public void MissingValuesOfAKnownWidgetComeFromItsDefaultPlacement()
    {
        AppSettings settings = Load(PrinterJson(""", "camera": { "overlay": { "widgets": [ { "id": "status", "size": "Large" } ] } }"""), out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.Equal(new OverlayWidgetPlacement(OverlayWidgetIds.Status, OverlayAnchor.TopLeft, OverlayWidgetSize.Large, emphasized: true), settings.Printers[0].Camera.Overlay.Find("status"));
    }

    [Fact]
    public void NamesAndIdsAreCaseInsensitiveAndTrimmed()
    {
        AppSettings settings = Load(
            PrinterJson(""", "camera": { "OVERLAY": { "Widgets": [ { "ID": " Status ", "Anchor": " bottomright ", "SIZE": "large", "Emphasized": false, "VISIBLE": false } ] } }"""),
            out List<string> warnings,
            out _);

        Assert.Empty(warnings);
        Assert.Equal(
            new OverlayWidgetPlacement(OverlayWidgetIds.Status, OverlayAnchor.BottomRight, OverlayWidgetSize.Large, emphasized: false, visible: false),
            settings.Printers[0].Camera.Overlay.Find("status"));
        Assert.Equal("status", settings.Printers[0].Camera.Overlay.Find("status")!.WidgetId);
    }

    [Fact]
    public void DuplicateWidgetKeepsTheFirstEntryWithOneWarning()
    {
        AppSettings settings = Load(
            PrinterJson(""", "camera": { "overlay": { "widgets": [ { "id": "status", "anchor": "TopRight" }, { "id": "STATUS", "anchor": "BottomCenter" }, { "id": "status", "anchor": "BottomRight" }, { "id": "future-widget" }, { "id": "Future-Widget", "anchor": "TopCenter" } ] } }"""),
            out List<string> warnings,
            out _);

        CameraOverlayLayout layout = settings.Printers[0].Camera.Overlay;
        Assert.Equal(OverlayAnchor.TopRight, layout.Find("status")!.Anchor);
        Assert.Equal(OverlayAnchor.BottomLeft, layout.Find(Future)!.Anchor);
        string warning = Assert.Single(warnings);
        Assert.Contains("mk4", warning, StringComparison.Ordinal);
        Assert.Contains("3 duplicate", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("future", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("anchor", "\"Middle\"")]
    [InlineData("anchor", "\"3\"")]
    [InlineData("anchor", "3")]
    [InlineData("anchor", "\"\"")]
    [InlineData("anchor", "\"Top Left\"")]
    [InlineData("size", "\"Huge\"")]
    [InlineData("size", "\"1\"")]
    [InlineData("size", "[]")]
    [InlineData("emphasized", "\"yes\"")]
    [InlineData("visible", "1")]
    [InlineData("visible", "{ }")]
    [InlineData("anchor", "\"\\ud800\"")]
    [InlineData("size", "\"\\udc00x\"")]
    public void UnknownValueFallsBackToTheDefaultWithOneWarningWithoutEchoingIt(string property, string value)
    {
        AppSettings settings = Load(
            PrinterJson($$""", "camera": { "overlay": { "widgets": [ { "id": "times", "anchor": "TopRight", "size": "Large", "emphasized": true, "visible": false, "{{property}}": {{value}} } ] } }"""),
            out List<string> warnings,
            out _);

        OverlayWidgetPlacement times = settings.Printers[0].Camera.Overlay.Find("times")!;
        OverlayWidgetPlacement fallback = CameraOverlayLayout.DefaultPlacement("times")!;
        OverlayWidgetPlacement expected = new("times", OverlayAnchor.TopRight, OverlayWidgetSize.Large, emphasized: true, visible: false);
        expected = property switch
        {
            "anchor" => expected with { Anchor = fallback.Anchor },
            "size" => expected with { Size = fallback.Size },
            "emphasized" => expected with { Emphasized = fallback.Emphasized },
            _ => expected with { Visible = fallback.Visible },
        };
        Assert.Equal(expected, times);
        string warning = Assert.Single(warnings);
        Assert.Contains("Camera overlay of printer 'mk4'", warning, StringComparison.Ordinal);
        Assert.Contains("1 invalid widget value", warning, StringComparison.Ordinal);
        foreach (string echoed in (string[])["Middle", "Huge", "yes", "Top Left"])
        {
            Assert.DoesNotContain(echoed, warning, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SeveralInvalidValuesGiveOneWarning()
    {
        Load(
            PrinterJson(""", "camera": { "overlay": { "widgets": [ { "id": "times", "anchor": "x", "size": "y" }, { "id": "status", "visible": "z" } ] } }"""),
            out List<string> warnings,
            out _);

        Assert.Contains("3 invalid widget values", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("\"status\"")]
    [InlineData("{ }")]
    [InlineData("{ \"id\": null }")]
    [InlineData("{ \"id\": 5 }")]
    [InlineData("{ \"id\": \"\" }")]
    [InlineData("{ \"id\": \"  \" }")]
    [InlineData("{ \"id\": \"a\\u0001b\" }")]
    [InlineData("{ \"anchor\": \"TopLeft\" }")]
    [InlineData("{ \"id\": \"\\ud800\" }")]
    [InlineData("{ \"id\": \"w\\udc00\" }")]
    public void InvalidWidgetEntryIsSkippedWithOneWarning(string entry)
    {
        AppSettings settings = Load(
            PrinterJson($$""", "camera": { "overlay": { "widgets": [ {{entry}}, { "id": "status", "anchor": "BottomCenter" } ] } }"""),
            out List<string> warnings,
            out _);

        CameraOverlayLayout layout = settings.Printers[0].Camera.Overlay;
        Assert.Equal(OverlayAnchor.BottomCenter, layout.Find("status")!.Anchor);
        Assert.Equal(OverlayWidgetIds.Known.Count, layout.Widgets.Count);
        Assert.Contains("1 invalid widget entry", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void TooLongIdIsSkippedWithoutEchoingIt()
    {
        string id = new('w', OverlayWidgetIds.MaxLength + 1);

        AppSettings settings = Load(PrinterJson($$""", "camera": { "overlay": { "widgets": [ { "id": "{{id}}" } ] } }"""), out List<string> warnings, out _);

        Assert.Same(CameraOverlayLayout.Default, settings.Printers[0].Camera.Overlay);
        Assert.DoesNotContain("www", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void WidgetsBeyondTheLimitAreDroppedWithOneWarning()
    {
        string entries = string.Join(", ", Enumerable.Range(0, CameraOverlayLayout.MaxWidgets + 3).Select(static i => $$"""{ "id": "w{{i}}" }"""));

        AppSettings settings = Load(PrinterJson($$""", "camera": { "overlay": { "widgets": [ {{entries}} ] } }"""), out List<string> warnings, out _);

        CameraOverlayLayout layout = settings.Printers[0].Camera.Overlay;
        Assert.Equal(CameraOverlayLayout.MaxWidgets, layout.Widgets.Count);
        Assert.All(OverlayWidgetIds.Known, id => Assert.True(layout.Contains(id)));
        Assert.Contains("too many widgets", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("\"status\"")]
    [InlineData("{ \"id\": \"status\" }")]
    [InlineData("true")]
    public void WidgetsThatAreNotAListGiveTheDefaultLayoutWithAWarning(string widgets)
    {
        AppSettings settings = Load(PrinterJson($$""", "camera": { "overlay": { "widgets": {{widgets}}, "corner": "TopRight" } }"""), out List<string> warnings, out List<string> notices);

        Assert.Same(CameraOverlayLayout.Default, settings.Printers[0].Camera.Overlay);
        Assert.Contains("widget list is invalid", Assert.Single(warnings), StringComparison.Ordinal);
        Assert.Empty(notices);
    }

    [Theory]
    [InlineData(""", "camera": { "overlay": 5 }""")]
    [InlineData(""", "camera": { "overlay": [] }""")]
    public void OverlayThatIsNotAnObjectFailsLikeOtherValues(string camera) =>
        Assert.ThrowsAny<JsonException>(() => Load(PrinterJson(camera), out _, out _));

    [Fact]
    public void UnknownPropertiesOfTheSectionAndOfWidgetsAreIgnored()
    {
        AppSettings settings = Load(
            PrinterJson(""", "camera": { "overlay": { "x": { "y": [1] }, "widgets": [ { "id": "status", "options": { "a": 1 }, "anchor": "TopRight" } ] } }"""),
            out List<string> warnings,
            out _);

        Assert.Empty(warnings);
        Assert.Equal(OverlayAnchor.TopRight, settings.Printers[0].Camera.Overlay.Find("status")!.Anchor);
    }

    [Fact]
    public void PropertyNameThatIsNoValidTextIsIgnored()
    {
        // A lone escaped surrogate cannot be read as a string; like any unknown property it must not fail the file.
        AppSettings settings = Load(
            PrinterJson(""", "camera": { "overlay": { "widgets": [ { "\ud800": 1, "id": "status", "anchor": "TopRight" } ] } }"""),
            out List<string> warnings,
            out _);

        Assert.Empty(warnings);
        Assert.Equal(OverlayAnchor.TopRight, settings.Printers[0].Camera.Overlay.Find("status")!.Anchor);
    }

    [Fact]
    public void LayoutOfDisabledCameraOrInvalidAddressIsKept()
    {
        string widgets = """ "overlay": { "widgets": [ { "id": "status", "anchor": "TopRight" } ] } """;
        AppSettings disabled = Load(PrinterJson($$""", "camera": { "enabled": false, "streamUrl": "rtsp://192.168.0.227/live", {{widgets}} }"""), out _, out _);
        AppSettings invalid = Load(PrinterJson($$""", "camera": { "enabled": true, "streamUrl": "http://192.168.0.227/live", {{widgets}} }"""), out _, out _);
        AppSettings noAddress = Load(PrinterJson($$""", "camera": { {{widgets}} }"""), out List<string> warnings, out _);

        Assert.Empty(warnings);
        foreach (AppSettings settings in (AppSettings[])[disabled, invalid, noAddress])
        {
            Assert.Equal(OverlayAnchor.TopRight, settings.Printers[0].Camera.Overlay.Find("status")!.Anchor);
            Assert.False(settings.Printers[0].Camera.Enabled);
        }

        Assert.Equal(noAddress, RoundTrip(noAddress, out _));
    }

    [Fact]
    public void SkippedPrinterGivesNoOverlayWarning()
    {
        AppSettings settings = Load(
            """{ "printers": [ { "id": "mk4", "connectionType": "PrusaLink", "camera": { "overlay": { "widgets": 5 } } } ] }""",
            out List<string> warnings,
            out _);

        Assert.Empty(settings.Printers);
        Assert.Contains("skipped", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void TooLongPrinterIdIsShortenedInTheWarning()
    {
        string id = new('i', 100);

        Load(
            $$"""{ "printers": [ { "id": "{{id}}", "connectionType": "PrusaLink", "address": "a", "camera": { "overlay": { "widgets": [ null ] } } } ] }""",
            out List<string> warnings,
            out _);

        Assert.DoesNotContain(id, Assert.Single(warnings), StringComparison.Ordinal);
    }

    internal static string PrinterJson(string camera) =>
        $$"""{ "printers": [ { "id": "mk4", "name": "MK4", "connectionType": "PrusaLink", "address": "192.168.0.226"{{camera}} } ] }""";

    internal static AppSettings WithLayout(CameraOverlayLayout layout) => WithCamera(CameraSettings.Disabled with { Overlay = layout });

    private static AppSettings WithCamera(CameraSettings camera) => new()
    {
        Printers = [new PrinterDefinition { Id = "mk4", Name = "MK4", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.0.226", Camera = camera }],
    };

    internal static string Compact(JsonElement element)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            element.WriteTo(writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return Compact(document.RootElement);
    }

    internal static string Write(AppSettings settings) =>
        JsonSerializer.Serialize(SettingsMapper.ToDocument(settings), SettingsJsonContext.Default.SettingsDocument);

    internal static AppSettings RoundTrip(AppSettings settings, out List<string> warnings) => Load(Write(settings), out warnings, out _);

    internal static AppSettings Load(string json, out List<string> warnings, out List<string> notices)
    {
        SettingsDocument document = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsDocument)!;
        warnings = [];
        notices = [];
        return SettingsMapper.ToSettings(document, warnings, notices);
    }
}
