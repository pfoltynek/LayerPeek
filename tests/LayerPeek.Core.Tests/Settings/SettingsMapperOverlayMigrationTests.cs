using System.Text.Json;

using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Settings.Storage;

using static LayerPeek.Core.Tests.Settings.SettingsMapperCameraOverlayTests;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>
/// Conversion of the T-036 overlay (<c>corner</c> / <c>normal</c> / <c>mini</c>) to the widget layout (T-077, user decision
/// Q4): the item choices are dropped, the default layout is mirrored so that its main anchor lands in the old corner.
/// </summary>
public class SettingsMapperOverlayMigrationTests
{
    /// <summary>Every old corner: the anchor of every widget (T-076 3.3 mirroring table).</summary>
    [Theory]
    [InlineData("BottomLeft", OverlayAnchor.TopLeft, OverlayAnchor.TopCenter, OverlayAnchor.BottomLeft, OverlayAnchor.BottomRight)]
    [InlineData("BottomRight", OverlayAnchor.TopRight, OverlayAnchor.TopCenter, OverlayAnchor.BottomRight, OverlayAnchor.BottomLeft)]
    [InlineData("TopLeft", OverlayAnchor.BottomLeft, OverlayAnchor.BottomCenter, OverlayAnchor.TopLeft, OverlayAnchor.TopRight)]
    [InlineData("TopRight", OverlayAnchor.BottomRight, OverlayAnchor.BottomCenter, OverlayAnchor.TopRight, OverlayAnchor.TopLeft)]
    public void EveryOldCornerGivesTheMirroredDefaultLayout(string corner, OverlayAnchor status, OverlayAnchor printName, OverlayAnchor timesAndCooldown, OverlayAnchor temperatures)
    {
        AppSettings settings = Load(
            PrinterJson($$""", "camera": { "overlay": { "corner": "{{corner}}", "normal": ["State"], "mini": [] } }"""),
            out List<string> warnings,
            out _);

        Assert.Empty(warnings);
        CameraOverlayLayout layout = settings.Printers[0].Camera.Overlay;
        Assert.Equal(new OverlayWidgetPlacement("status", status, OverlayWidgetSize.Medium, emphasized: true), layout.Find("status"));
        Assert.Equal(new OverlayWidgetPlacement("print-name", printName), layout.Find("print-name"));
        Assert.Equal(new OverlayWidgetPlacement("times", timesAndCooldown), layout.Find("times"));
        Assert.Equal(new OverlayWidgetPlacement("cooldown", timesAndCooldown), layout.Find("cooldown"));
        Assert.Equal(new OverlayWidgetPlacement("temperatures", temperatures), layout.Find("temperatures"));
        Assert.Equal(["times", "cooldown"], layout.InAnchor(timesAndCooldown).Select(static w => w.WidgetId));
        Assert.Equal(OverlayWidgetIds.Known.Count, layout.Widgets.Count);
        Assert.Equal(CameraOverlayLayout.FromLegacyCorner(Enum.Parse<OverlayAnchor>(corner)), layout);
    }

    [Theory]
    [InlineData("""{ "corner": "BottomLeft" }""")]
    [InlineData("""{ "corner": " bottomleft ", "normal": ["PrinterName", "FileName"], "mini": [] }""")]
    [InlineData("""{ "normal": [], "mini": ["Progress"] }""")]
    [InlineData("""{ "corner": null, "normal": null, "mini": null }""")]
    public void DefaultCornerGivesExactlyTheDefaultAndWritesNothing(string overlay)
    {
        AppSettings settings = Load(PrinterJson($$""", "camera": { "overlay": {{overlay}} }"""), out List<string> warnings, out _);

        Assert.Empty(warnings);
        Assert.Same(CameraOverlayLayout.Default, settings.Printers[0].Camera.Overlay);
        Assert.DoesNotContain("\"overlay\"", Write(settings), StringComparison.Ordinal);
    }

    /// <summary>Whatever was chosen in T-036, every widget starts visible (no lossy conversion of the visibility).</summary>
    [Theory]
    [InlineData("[]", "[]")]
    [InlineData("""["PrinterName", "State", "NozzleTemperature", "BedTemperature", "Progress", "RemainingTime", "FinishTime", "PrintTime", "FileName"]""", """["PrinterName", "State", "NozzleTemperature", "BedTemperature", "Progress", "RemainingTime", "FinishTime", "PrintTime", "FileName"]""")]
    [InlineData("""["FileName"]""", "[]")]
    [InlineData("[]", """["NozzleTemperature"]""")]
    [InlineData("""["Unknown", null, 5]""", """{ "x": 1 }""")]
    [InlineData("\"State\"", "7")]
    public void OldItemChoicesNeverChangeTheVisibility(string normal, string mini)
    {
        AppSettings settings = Load(
            PrinterJson($$""", "camera": { "overlay": { "corner": "TopRight", "normal": {{normal}}, "mini": {{mini}} } }"""),
            out List<string> warnings,
            out _);

        Assert.Empty(warnings);
        Assert.Equal(CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopRight), settings.Printers[0].Camera.Overlay);
        Assert.All(settings.Printers[0].Camera.Overlay.Widgets, static w => Assert.True(w.Visible));
    }

    [Theory]
    [InlineData("\"Middle\"")]
    [InlineData("\"TopCenter\"")]
    [InlineData("\"BottomCenter\"")]
    [InlineData("\"Center\"")]
    [InlineData("\"2\"")]
    [InlineData("\"\"")]
    [InlineData("2")]
    [InlineData("[\"TopRight\"]")]
    public void InvalidOldCornerGivesTheDefaultLayoutWithAWarningWithoutEchoingIt(string corner)
    {
        AppSettings settings = Load(PrinterJson($$""", "camera": { "overlay": { "corner": {{corner}} } }"""), out List<string> warnings, out List<string> notices);

        Assert.Same(CameraOverlayLayout.Default, settings.Printers[0].Camera.Overlay);
        string warning = Assert.Single(warnings);
        Assert.Contains("Camera overlay of printer 'mk4'", warning, StringComparison.Ordinal);
        Assert.Contains("corner", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("Middle", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("TopCenter", warning, StringComparison.Ordinal);
        Assert.Single(notices);
    }

    [Fact]
    public void NewShapeWinsOverTheOldOne()
    {
        AppSettings settings = Load(
            PrinterJson(""", "camera": { "overlay": { "corner": "TopRight", "normal": [], "widgets": [ { "id": "status", "anchor": "BottomCenter" } ], "mini": [] } }"""),
            out List<string> warnings,
            out List<string> notices);

        Assert.Empty(warnings);
        Assert.Empty(notices);
        Assert.Equal(CameraOverlayLayout.Default.MoveTo("status", OverlayAnchor.BottomCenter, 0), settings.Printers[0].Camera.Overlay);
    }

    [Fact]
    public void OneValueFreeNoticeForTheWholeFile()
    {
        string json = $$"""
            { "printers": [
              { "id": "a", "connectionType": "PrusaLink", "address": "10.0.0.1", "camera": { "overlay": { "corner": "TopRight", "normal": ["FileName"] } } },
              { "id": "b", "connectionType": "PrusaLink", "address": "10.0.0.2", "camera": { "overlay": { "corner": "BottomLeft" } } },
              { "id": "c", "connectionType": "PrusaLink", "address": "10.0.0.3", "camera": { "overlay": { "widgets": [] } } },
              { "id": "d", "connectionType": "PrusaLink", "address": "10.0.0.4" }
            ] }
            """;

        AppSettings settings = Load(json, out List<string> warnings, out List<string> notices);

        Assert.Empty(warnings);
        string notice = Assert.Single(notices);
        Assert.Contains("2 printers", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("TopRight", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("'a'", notice, StringComparison.Ordinal);
        Assert.Equal(CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopRight), settings.Printers[0].Camera.Overlay);
        Assert.Same(CameraOverlayLayout.Default, settings.Printers[1].Camera.Overlay);
    }

    [Fact]
    public void FileWithoutOldKeysGivesNoNotice()
    {
        Load(PrinterJson(""", "camera": { "enabled": false }"""), out _, out List<string> notices);

        Assert.Empty(notices);
    }

    [Theory]
    [InlineData("TopLeft")]
    [InlineData("TopRight")]
    [InlineData("BottomLeft")]
    [InlineData("BottomRight")]
    public void ConversionIsIdempotentAcrossSaveAndLoad(string corner)
    {
        AppSettings first = Load(PrinterJson($$""", "camera": { "overlay": { "corner": "{{corner}}", "mini": ["State"] } }"""), out _, out _);
        string saved = Write(first);

        AppSettings second = Load(saved, out List<string> warnings, out List<string> notices);

        Assert.Empty(warnings);
        Assert.Empty(notices);
        Assert.Equal(first, second);
        Assert.Equal(saved, Write(second));
    }

    [Fact]
    public void SavedFileHasVersionTwoAndNoOldKeys()
    {
        AppSettings settings = Load(PrinterJson(""", "camera": { "overlay": { "corner": "TopRight", "normal": ["State"], "mini": ["State"] } }"""), out _, out _);

        string json = Write(settings);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(2, SettingsDocument.CurrentVersion);
        JsonElement overlay = document.RootElement.GetProperty("printers")[0].GetProperty("camera").GetProperty("overlay");
        Assert.Equal(["widgets"], overlay.EnumerateObject().Select(static p => p.Name));
        Assert.DoesNotContain("\"corner\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"normal\"", json.Replace("\"normal\": {", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("\"mini\"", json.Replace("\"mini\": {", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void NewerFormatWarnsAsBeforeAndStillReadsTheLayout()
    {
        AppSettings settings = Load(
            """{ "version": 3, "printers": [ { "id": "mk4", "connectionType": "PrusaLink", "address": "10.0.0.1", "camera": { "overlay": { "widgets": [ { "id": "status", "anchor": "BottomRight" } ] } } } ] }""",
            out List<string> warnings,
            out _);

        Assert.Contains("newer version (format 3)", Assert.Single(warnings), StringComparison.Ordinal);
        Assert.Equal(OverlayAnchor.BottomRight, settings.Printers[0].Camera.Overlay.Find("status")!.Anchor);
    }

    [Fact]
    public void VersionTwoFileDoesNotWarn()
    {
        Load("""{ "version": 2 }""", out List<string> warnings, out _);

        Assert.Empty(warnings);
    }
}
