using System.Net;

namespace PrusaTrayDiagnostic.Simulator.Printers;

/// <summary>How a printer that is offline treats requests.</summary>
public enum OfflineMode
{
    /// <summary>Accepts the connection and drops it without an answer (the client sees a connection error at once).</summary>
    Drop,

    /// <summary>Never answers while offline (the client runs into its timeout), like a printer switched off.</summary>
    Hang,
}

/// <summary>Settings of a <see cref="SimulatedPrinter"/>.</summary>
public sealed record SimulatedPrinterOptions
{
    public const string DefaultUserName = "maker";

    public const string DefaultPassword = "simulator";

    public SimulatedModel Model { get; init; } = SimulatedModel.CoreOne;

    public PrinterScenario Scenario { get; init; } = PrinterScenario.Printing;

    /// <summary>Start not responding (recover with <see cref="PrinterSimulation.SetOffline"/>).</summary>
    public bool StartOffline { get; init; }

    /// <summary>Address to listen on; loopback by default (use <see cref="IPAddress.Any"/> for the local network).</summary>
    public IPAddress Address { get; init; } = IPAddress.Loopback;

    /// <summary>TCP port; 0 lets the OS choose a free one (tests).</summary>
    public int Port { get; init; }

    /// <summary>PrusaLink user (the printer's default is "maker").</summary>
    public string UserName { get; init; } = DefaultUserName;

    /// <summary>PrusaLink password; also accepted as <c>X-Api-Key</c>.</summary>
    public string Password { get; init; } = DefaultPassword;

    /// <summary>Host name reported by the printer; the model's default when null.</summary>
    public string? Hostname { get; init; }

    /// <summary>Simulated seconds per real second.</summary>
    public double Speed { get; init; } = 1;

    public SimulationTiming Timing { get; init; } = SimulationTiming.Default;

    public OfflineMode OfflineMode { get; init; } = OfflineMode.Drop;

    /// <summary>Clock of the simulation and of the nonce lifetime.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
