using System.Text.Json.Nodes;

using FlaUI.Core.AutomationElements;

using LayerPeek.Simulator.Camera;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The app with one printer whose camera window settings a test chooses (T-035, T-037, T-071), and the Camera window
/// sub-tab of its settings.
/// </summary>
internal static class CameraWindowSettings
{
    public const string PrinterId = "e2e-camera-window";

    /// <summary>A camera address nothing answers on (for checks that never open the camera window).</summary>
    private static readonly Uri Unreachable = new("rtsp://127.0.0.1:1/live");

    /// <summary>Starts the app with the printer <see cref="PrinterId"/>, its camera <paramref name="camera"/> and the <c>camera.window</c> section.</summary>
    public static Task<AppInstance> StartAsync(E2EFixture fixture, string name, JsonObject window, RtspCamera? camera = null, string language = "en") =>
        AppInstance.StartAsync(
            fixture,
            name,
            TestSettings.Json(language, [new TestPrinter(PrinterId, "Core One", "127.0.0.1:1", Camera: camera?.StreamUri ?? Unreachable, CameraWindow: window)]));

    /// <summary>Opens the camera window of the printer.</summary>
    public static Task<nint> OpenCameraAsync(AppInstance app) => app.OpenAsync($"camera:{PrinterId}", AppWindowKind.Camera);

    /// <summary>Opens the settings on Printers → the printer → Camera window.</summary>
    public static async Task<(nint Hwnd, AutomationElement Element)> OpenTabAsync(AppInstance app)
    {
        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        AutomationElement element = app.Element(settings);
        (await Ui.ItemsAsync(element, "SettingsPrinterList", 1))[0].Patterns.SelectionItem.Pattern.Select();
        await Ui.SelectAsync(element, "PrinterTabCameraWindow");
        return (settings, element);
    }

    /// <summary>The items of the monitor choice and the selected one.</summary>
    public static Task<(List<string> Items, int Selected)> MonitorChoicesAsync(AutomationElement settings) =>
        Ui.ComboItemsAsync(settings, "PrinterCameraWindowMonitor");

    /// <summary>Closes the settings with Save or Cancel and waits until the window is gone.</summary>
    public static async Task CloseAsync(nint hwnd, AutomationElement element, bool save)
    {
        await Ui.InvokeAsync(element, save ? "SettingsSave" : "SettingsCancel");
        await Wait.UntilAsync(() => !Native.IsWindowVisible(hwnd), TimeSpan.FromSeconds(10), $"the settings to close ({(save ? "Save" : "Cancel")})");
    }

    /// <summary>
    /// The saved <c>camera.window</c> section of the printer in the app's <c>settings.json</c>, or <see langword="null"/>
    /// (also while the app is replacing the file: callers wait for the expected value).
    /// </summary>
    public static JsonNode? SavedWindow(AppInstance app)
    {
        string file = Path.Combine(app.DataDirectory, "settings.json");
        try
        {
            using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonNode.Parse(stream)?["printers"]?[0]?["camera"]?["window"];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public static bool IsTopmost(nint hwnd) => (Native.ExStyle(hwnd) & Native.WsExTopmost) != 0;
}
