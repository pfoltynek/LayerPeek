namespace LayerPeek.Simulator.Printers;

/// <summary>
/// A printer model as PrusaLink reports it: <see cref="Code"/> is the <c>printer</c> field of <c>/api/version</c>
/// ("type.version.subversion", Prusa-Firmware-Buddy <c>include/common/printer_model_data.hpp</c>).
/// </summary>
/// <param name="Key">Name on the command line (e.g. <c>mk4s</c>).</param>
/// <param name="ProductName">Name as Prusa writes it.</param>
/// <param name="Code">Model code (e.g. <c>1.4.1</c>).</param>
/// <param name="Hostname">Default host name of the printer.</param>
/// <param name="BedTarget">Bed temperature of the simulated PLA print.</param>
/// <param name="NozzleTarget">Nozzle temperature of the simulated PLA print.</param>
/// <param name="MaxZ">Height of the simulated print in mm.</param>
public sealed record SimulatedModel(string Key, string ProductName, string Code, string Hostname, double BedTarget, double NozzleTarget, double MaxZ)
{
    public static SimulatedModel CoreOne { get; } = new("coreone", "Core One", "7.1.0", "prusa-core-one", 60, 215, 48);

    public static SimulatedModel Mk4S { get; } = new("mk4s", "MK4S", "1.4.1", "prusa-mk4s", 60, 215, 48);

    public static SimulatedModel Xl { get; } = new("xl", "XL", "3.1.0", "prusa-xl", 60, 215, 48);

    public static SimulatedModel Mini { get; } = new("mini", "MINI", "2.1.0", "prusa-mini", 60, 210, 30);

    /// <summary>All models, in the order used when the command line does not name them.</summary>
    public static IReadOnlyList<SimulatedModel> All { get; } = [CoreOne, Mk4S, Xl, Mini];

    /// <summary>Firmware version reported by every simulated printer.</summary>
    public const string Firmware = "6.9.2+10123";

    public static bool TryParse(string? key, out SimulatedModel model)
    {
        foreach (SimulatedModel candidate in All)
        {
            if (string.Equals(candidate.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                model = candidate;
                return true;
            }
        }

        model = CoreOne;
        return false;
    }
}
