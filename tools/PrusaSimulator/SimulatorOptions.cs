using System.Globalization;
using System.Net;

using LayerPeek.Simulator.Camera;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.Simulator;

/// <summary>Command line of the simulator (see <see cref="Usage"/> and docs/simulator.md).</summary>
public sealed record SimulatorOptions
{
    public const int DefaultFirstPort = 8080;

    public const int DefaultCameraPort = 8554;

    public const int MaxPrinters = 16;

    private static readonly HashSet<string> ValuedOptions = new(StringComparer.Ordinal)
    {
        "--printers", "--port", "--models", "--scenario", "--speed", "--print-minutes", "--user", "--password", "--host",
        "--offline-mode", "--camera-port", "--clip", "--fps",
    };

    public const string Usage = """
        PrusaSimulator - simulated PrusaLink printers and an RTSP camera for LayerPeek.

        Usage: dotnet run --project tools/PrusaSimulator -- [options]

          --printers <n>          number of printers, 1-16 (default 1)
          --port <n>              PrusaLink port of the first printer; the others follow (default 8080; 0 = any free port)
          --models <list>         comma-separated models: coreone, mk4s, xl, mini (default: in this order, repeating)
          --scenario <list>       comma-separated scenario per printer, the last one repeats (default printing):
                                  idle, printing, paused, finished, error, attention, offline, flaky, malformed
          --speed <x>             simulated time speed-up, e.g. 10 (default 1)
          --print-minutes <m>     duration of a simulated print (default 30)
          --user <name>           PrusaLink user (default maker)
          --password <text>       PrusaLink password (default simulator)
          --host <ip>             address to listen on (default 127.0.0.1; 0.0.0.0 or :: = reachable from the network,
                                  then the /_simulator/ control endpoints need the PrusaLink login too)
          --offline-mode <mode>   drop (close the connection, default) or hang (never answer) while offline
          --no-camera             no RTSP camera
          --camera-port <n>       RTSP port (default 8554; 0 = any free port)
          --clip <file>           H.264 Annex B clip to loop (default: the recorded Buddy3D clip)
          --fps <x>               camera frame rate (default 25)
          -h, --help              this help

        Commands while running (type and press Enter):
          list                          state of all printers
          <n|all> <scenario>            switch printer n (1-based) or all printers to a scenario
          <n|all> offline | online      stop / resume answering
          camera offline | online       drop the camera / let clients connect again
          quit                          stop (as Ctrl+C)
        """;

    public int Printers { get; init; } = 1;

    public int FirstPort { get; init; } = DefaultFirstPort;

    /// <summary>Models given on the command line (empty: <see cref="SimulatedModel.All"/> repeating).</summary>
    public IReadOnlyList<SimulatedModel> Models { get; init; } = [];

    /// <summary>Scenario names given on the command line (empty: printing).</summary>
    public IReadOnlyList<string> Scenarios { get; init; } = [];

    public double Speed { get; init; } = 1;

    public TimeSpan PrintDuration { get; init; } = SimulationTiming.Default.Print;

    public string UserName { get; init; } = SimulatedPrinterOptions.DefaultUserName;

    public string Password { get; init; } = SimulatedPrinterOptions.DefaultPassword;

    public IPAddress Address { get; init; } = IPAddress.Loopback;

    public OfflineMode OfflineMode { get; init; } = OfflineMode.Drop;

    public bool Camera { get; init; } = true;

    public int CameraPort { get; init; } = DefaultCameraPort;

    /// <summary>Clip to loop; null for <see cref="SimulatorHost.DefaultClipPath"/>.</summary>
    public string? ClipPath { get; init; }

    public double FramesPerSecond { get; init; } = RtspCameraOptions.DefaultFramesPerSecond;

    public bool ShowHelp { get; init; }

    /// <summary>The model of printer <paramref name="index"/> (0-based).</summary>
    public SimulatedModel ModelOf(int index) =>
        Models.Count == 0 ? SimulatedModel.All[index % SimulatedModel.All.Count] : Models[Math.Min(index, Models.Count - 1)];

    /// <summary>The scenario of printer <paramref name="index"/> (0-based); "offline" is idle and offline.</summary>
    public (PrinterScenario Scenario, bool Offline) ScenarioOf(int index)
    {
        string name = Scenarios.Count == 0 ? "printing" : Scenarios[Math.Min(index, Scenarios.Count - 1)];
        return PrinterScenarios.TryParse(name, out PrinterScenario scenario, out bool offline)
            ? (scenario, offline)
            : throw new InvalidOperationException($"Unknown scenario '{name}'.");
    }

    /// <summary>PrusaLink port of printer <paramref name="index"/> (0 stays 0: any free port).</summary>
    public int PortOf(int index) => FirstPort == 0 ? 0 : FirstPort + index;

    /// <returns>False with an English message for the user when the arguments are invalid.</returns>
    public static bool TryParse(IReadOnlyList<string> args, out SimulatorOptions options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = new SimulatorOptions();
        error = null;
        for (int i = 0; i < args.Count; i++)
        {
            string name = args[i];
            if (name is "-h" or "--help" or "/?")
            {
                options = options with { ShowHelp = true };
                continue;
            }

            if (name == "--no-camera")
            {
                options = options with { Camera = false };
                continue;
            }

            if (!ValuedOptions.Contains(name))
            {
                error = name.StartsWith('-') ? $"Unknown option '{name}'." : $"Unknown argument '{name}'.";
                return false;
            }

            if (i + 1 >= args.Count)
            {
                error = $"Option {name} needs a value.";
                return false;
            }

            string value = args[++i];
            switch (name)
            {
                case "--printers" when TryInt(value, 1, MaxPrinters, out int printers):
                    options = options with { Printers = printers };
                    break;
                case "--port" when TryInt(value, 0, 65535, out int port):
                    options = options with { FirstPort = port };
                    break;
                case "--camera-port" when TryInt(value, 0, 65535, out int cameraPort):
                    options = options with { CameraPort = cameraPort };
                    break;
                case "--models":
                    List<SimulatedModel> models = [];
                    foreach (string key in SplitList(value))
                    {
                        if (!SimulatedModel.TryParse(key, out SimulatedModel model))
                        {
                            error = $"Unknown model '{key}' (use {string.Join(", ", SimulatedModel.All.Select(static m => m.Key))}).";
                            return false;
                        }

                        models.Add(model);
                    }

                    options = options with { Models = models };
                    break;
                case "--scenario":
                    string[] scenarios = SplitList(value);
                    if (Array.Find(scenarios, static s => !PrinterScenarios.TryParse(s, out _, out _)) is { } unknown)
                    {
                        error = $"Unknown scenario '{unknown}' (use {string.Join(", ", PrinterScenarios.Names)}).";
                        return false;
                    }

                    options = options with { Scenarios = scenarios };
                    break;
                case "--speed" when TryDouble(value, 0.01, 10000, out double speed):
                    options = options with { Speed = speed };
                    break;
                case "--print-minutes" when TryDouble(value, 0.1, 10000, out double minutes):
                    options = options with { PrintDuration = TimeSpan.FromMinutes(minutes) };
                    break;
                case "--fps" when TryDouble(value, 1, 120, out double fps):
                    options = options with { FramesPerSecond = fps };
                    break;
                case "--user" when !string.IsNullOrWhiteSpace(value):
                    options = options with { UserName = value.Trim() };
                    break;
                case "--password" when value.Length > 0:
                    options = options with { Password = value };
                    break;
                case "--host" when TryParseHost(value, out IPAddress? address):
                    options = options with { Address = address };
                    break;
                case "--offline-mode" when Enum.TryParse(value, ignoreCase: true, out OfflineMode mode) && Enum.IsDefined(mode) && !int.TryParse(value, out _):
                    options = options with { OfflineMode = mode };
                    break;
                case "--clip" when !string.IsNullOrWhiteSpace(value):
                    options = options with { ClipPath = value };
                    break;
                default:
                    error = $"Invalid value '{value}' for {name}.";
                    return false;
            }
        }

        if (options.FirstPort != 0 && options.FirstPort + options.Printers - 1 > 65535)
        {
            error = "The printer ports go past 65535; use a lower --port.";
            return false;
        }

        if (options.Camera && options.CameraPort != 0 && options.FirstPort != 0
            && options.CameraPort >= options.FirstPort && options.CameraPort < options.FirstPort + options.Printers)
        {
            error = "The camera port is one of the printer ports; choose another --camera-port.";
            return false;
        }

        return true;
    }

    private static bool TryParseHost(string value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IPAddress? address)
    {
        if (value.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            address = IPAddress.Loopback;
            return true;
        }

        return IPAddress.TryParse(value, out address);
    }

    private static string[] SplitList(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryInt(string value, int min, int max, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= min && result <= max;

    private static bool TryDouble(string value, double min, double max, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result) && result >= min && result <= max;
}
