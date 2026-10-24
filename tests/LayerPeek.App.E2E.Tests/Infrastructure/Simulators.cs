using System.Text.Json.Nodes;

using LayerPeek.Simulator.Camera;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The simulated devices of T-045 on 127.0.0.1: a PrusaLink printer (Digest login like the firmware) and the Buddy3D
/// RTSP camera looping the recorded clip of T-014 in real time for any number of clients.
/// </summary>
internal static class Simulators
{
    private static readonly string ClipPath = Path.Combine(AppContext.BaseDirectory, "Clips", "buddy3d.h264");

    private static readonly Lazy<H264Clip> Clip = new(() => H264Clip.Load(ClipPath));

    /// <summary>A printer in <paramref name="scenario"/>; dispose it after the app.</summary>
    public static Task<SimulatedPrinter> StartPrinterAsync(PrinterScenario scenario = PrinterScenario.Printing) =>
        SimulatedPrinter.StartAsync(new SimulatedPrinterOptions { Scenario = scenario }, TestContext.Current.CancellationToken);

    /// <summary>An idle printer of <paramref name="model"/> (its model code in <c>/api/version</c>, T-039); dispose it after the app.</summary>
    public static Task<SimulatedPrinter> StartModelAsync(SimulatedModel model) =>
        SimulatedPrinter.StartAsync(new SimulatedPrinterOptions { Model = model, Scenario = PrinterScenario.Idle }, TestContext.Current.CancellationToken);

    /// <summary>The address as a user enters it in the settings (host and port).</summary>
    public static string Address(this SimulatedPrinter printer) => $"127.0.0.1:{printer.Port}";

    /// <summary>The seed of the app's test-mode secret store for <paramref name="printerId"/> (the simulator's password).</summary>
    public static Dictionary<string, string> Secrets(string printerId, SimulatedPrinter printer) =>
        new() { [$"printer/{printerId}"] = printer.Password };

    public static RtspCamera StartCamera() => new(new RtspCameraOptions { Clip = Clip.Value });

    /// <summary>
    /// Waits until the app has detected the model of every printer in <paramref name="printerIds"/> and written it to its
    /// <c>settings.json</c> (T-039): from then on the tray menu and the windows show the model's illustration.
    /// </summary>
    public static Task WaitForDetectedModelsAsync(AppInstance app, params string[] printerIds) =>
        Wait.UntilAsync(
            () =>
            {
                try
                {
                    JsonArray? printers = JsonNode.Parse(app.ReadSettings())?["printers"]?.AsArray();
                    return printerIds.All(id => printers?.Any(p => (string?)p?["id"] == id && p?["detectedModel"] is not null) == true);
                }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
                {
                    // Being written right now.
                    return false;
                }
            },
            TimeSpan.FromSeconds(30),
            $"the detected models of {string.Join(", ", printerIds)} in settings.json");
}
