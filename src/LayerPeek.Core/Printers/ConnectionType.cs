namespace LayerPeek.Core.Printers;

/// <summary>
/// Data source used to reach a printer.
/// </summary>
public enum ConnectionType
{
    /// <summary>Local REST API running on the printer.</summary>
    PrusaLink = 1,

    /// <summary>Prusa Connect cloud service.</summary>
    PrusaConnect = 2,
}
