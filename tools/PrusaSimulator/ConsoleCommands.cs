using System.Globalization;

using LayerPeek.Simulator.Printers;

namespace LayerPeek.Simulator;

/// <summary>The commands typed while the simulator runs (see <see cref="SimulatorOptions.Usage"/>).</summary>
public static class ConsoleCommands
{
    /// <returns>False for <c>quit</c>.</returns>
    public static bool Execute(SimulatorHost host, string line, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(output);
        string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return true;
        }

        string command = words[0].ToUpperInvariant();
        switch (command)
        {
            case "QUIT" or "EXIT" or "Q":
                return false;
            case "LIST" or "L":
                WriteState(host, output);
                return true;
            case "HELP" or "?":
                output.WriteLine(SimulatorOptions.Usage);
                return true;
            case "CAMERA" when words.Length == 2 && host.Camera is { } camera:
                switch (words[1].ToUpperInvariant())
                {
                    case "OFFLINE":
                        camera.SetOffline(true);
                        output.WriteLine("camera: offline");
                        return true;
                    case "ONLINE":
                        camera.SetOffline(false);
                        output.WriteLine("camera: online");
                        return true;
                }

                break;
        }

        if (words.Length == 2 && Targets(host, words[0]) is { } targets)
        {
            string action = words[1];
            foreach (SimulatedPrinter printer in targets)
            {
                if (action.Equals("online", StringComparison.OrdinalIgnoreCase))
                {
                    printer.Simulation.SetOffline(false);
                }
                else if (PrinterScenarios.TryParse(action, out PrinterScenario scenario, out bool offline))
                {
                    if (offline)
                    {
                        printer.Simulation.SetOffline(true);
                    }
                    else
                    {
                        printer.Simulation.SetScenario(scenario);
                    }
                }
                else
                {
                    output.WriteLine($"Unknown scenario '{action}' (use {string.Join(", ", PrinterScenarios.Names)}, online).");
                    return true;
                }
            }

            WriteState(host, output);
            return true;
        }

        output.WriteLine("Unknown command; type 'help'.");
        return true;
    }

    /// <summary>One line per printer: number, model, address, scenario, online, state, progress, temperatures.</summary>
    public static void WriteState(SimulatorHost host, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(output);
        for (int i = 0; i < host.Printers.Count; i++)
        {
            SimulatedPrinter printer = host.Printers[i];
            SimulatedStatus status = printer.Simulation.GetStatus();
            string job = status.Job is { } j
                ? string.Create(CultureInfo.InvariantCulture, $" job {j.Id} {j.Progress:0.0} % {j.File.DisplayName}")
                : "";
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{i + 1}. {printer.Model.ProductName,-8} {printer.BaseUri} {PrinterScenarios.Format(printer.Simulation.Scenario),-9} {(printer.Simulation.IsOnline ? "online " : "OFFLINE")} {status.State,-9} nozzle {status.TempNozzle:0.0}/{status.TargetNozzle:0} bed {status.TempBed:0.0}/{status.TargetBed:0}{job}"));
        }

        if (host.Camera is { } camera)
        {
            output.WriteLine($"camera: {camera.StreamUri} {(camera.IsOffline ? "OFFLINE" : "online")}, {camera.ActiveSessions} client(s)");
        }
    }

    private static IEnumerable<SimulatedPrinter>? Targets(SimulatorHost host, string word)
    {
        if (word.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return host.Printers;
        }

        return int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= 1 && number <= host.Printers.Count
            ? [host.Printers[number - 1]]
            : null;
    }
}
