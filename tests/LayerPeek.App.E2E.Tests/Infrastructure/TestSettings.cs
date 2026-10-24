using System.Text.Json.Nodes;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>A printer of a <c>settings.json</c> fixture.</summary>
/// <param name="Camera">RTSP address of an enabled camera, or <see langword="null"/>.</param>
/// <param name="CameraWindow">The <c>camera.window</c> section (T-035/T-037), or <see langword="null"/> for the defaults.</param>
internal sealed record TestPrinter(string Id, string Name, string Address, Uri? Camera = null, JsonObject? CameraWindow = null, string? DisabledCamera = null)
{
    public JsonObject ToJson()
    {
        JsonObject printer = new()
        {
            ["id"] = Id,
            ["name"] = Name,
            ["connectionType"] = "PrusaLink",
            ["address"] = Address,
            ["userName"] = "maker",
        };
        if (Camera is not null || DisabledCamera is not null)
        {
            JsonObject camera = new()
            {
                ["enabled"] = Camera is not null,
                ["streamUrl"] = Camera?.ToString() ?? DisabledCamera,
            };
            if (CameraWindow is not null)
            {
                camera["window"] = CameraWindow.DeepClone();
            }

            printer["camera"] = camera;
        }

        return printer;
    }
}

/// <summary>The <c>settings.json</c> (format version 1) a test app starts with.</summary>
internal static class TestSettings
{
    /// <summary>The 8 languages of the app (T-029).</summary>
    public static readonly string[] Languages = ["en", "cs", "sk", "de", "es", "fr", "it", "pl"];

    /// <summary>
    /// The settings of a test app. Notifications are on but silent (sound <c>Off</c>): toasts of the test app play no sound
    /// during a run (user decision 2026-10-22, T-068); the sound attribute of the toast XML is covered by the unit tests of
    /// the app, an audible sound stays a recommended manual check.
    /// </summary>
    public static string Json(string language, IEnumerable<TestPrinter> printers) => new JsonObject
    {
        ["version"] = 1,
        ["pollingIntervalSeconds"] = 5,
        ["language"] = language,
        ["notifications"] = new JsonObject { ["enabled"] = true, ["remainingTimeMinutes"] = 10, ["sound"] = "Off" },
        ["printers"] = new JsonArray([.. printers.Select(static printer => (JsonNode)printer.ToJson())]),
    }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}
