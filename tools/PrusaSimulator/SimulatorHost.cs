using LayerPeek.Simulator.Camera;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.Simulator;

/// <summary>
/// Everything the command line describes, running: the printers and the camera. Also usable in-process (e.g. by
/// end-to-end tests) instead of starting the program.
/// </summary>
public sealed class SimulatorHost : IAsyncDisposable
{
    private SimulatorHost(IReadOnlyList<SimulatedPrinter> printers, RtspCamera? camera)
    {
        Printers = printers;
        Camera = camera;
    }

    /// <summary>The recorded Buddy3D clip (T-014 fixture) next to the simulator's assembly.</summary>
    public static string DefaultClipPath { get; } = Path.Combine(AppContext.BaseDirectory, "Clips", "buddy3d.h264");

    public IReadOnlyList<SimulatedPrinter> Printers { get; }

    /// <summary>Null when started with <c>--no-camera</c>.</summary>
    public RtspCamera? Camera { get; }

    /// <exception cref="IOException">A port is taken, or the clip cannot be read.</exception>
    /// <exception cref="InvalidDataException">The clip is not H.264 Annex B.</exception>
    public static async Task<SimulatorHost> StartAsync(SimulatorOptions options, CancellationToken cancellationToken, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        TimeProvider clock = timeProvider ?? TimeProvider.System;
        List<SimulatedPrinter> printers = [];
        RtspCamera? camera = null;
        try
        {
            if (options.Camera)
            {
                string clipPath = options.ClipPath ?? DefaultClipPath;
                if (!File.Exists(clipPath))
                {
                    throw new FileNotFoundException($"The camera clip '{clipPath}' does not exist.", clipPath);
                }

                H264Clip clip = H264Clip.Load(clipPath);
                try
                {
                    camera = new RtspCamera(new RtspCameraOptions
                    {
                        Clip = clip,
                        Address = options.Address,
                        Port = options.CameraPort,
                        FramesPerSecond = options.FramesPerSecond,
                    });
                }
                catch (System.Net.Sockets.SocketException ex)
                {
                    throw new IOException($"The camera port {options.CameraPort} cannot be used: {ex.Message}", ex);
                }
            }

            SimulationTiming timing = SimulationTiming.Default with { Print = options.PrintDuration };
            for (int i = 0; i < options.Printers; i++)
            {
                (PrinterScenario scenario, bool offline) = options.ScenarioOf(i);
                printers.Add(await SimulatedPrinter.StartAsync(
                    new SimulatedPrinterOptions
                    {
                        Model = options.ModelOf(i),
                        Scenario = scenario,
                        StartOffline = offline,
                        Address = options.Address,
                        Port = options.PortOf(i),
                        UserName = options.UserName,
                        Password = options.Password,
                        Speed = options.Speed,
                        Timing = timing,
                        OfflineMode = options.OfflineMode,
                        TimeProvider = clock,
                    },
                    cancellationToken).ConfigureAwait(false));
            }

            return new SimulatorHost(printers, camera);
        }
        catch
        {
            await DisposeAllAsync(printers, camera).ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync() => DisposeAllAsync(Printers, Camera);

    private static async ValueTask DisposeAllAsync(IReadOnlyList<SimulatedPrinter> printers, RtspCamera? camera)
    {
        foreach (SimulatedPrinter printer in printers)
        {
            await printer.DisposeAsync().ConfigureAwait(false);
        }

        if (camera is not null)
        {
            await camera.DisposeAsync().ConfigureAwait(false);
        }
    }
}
