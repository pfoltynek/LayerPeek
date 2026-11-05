namespace LayerPeek.Core.Printers;

/// <summary>
/// Data source used to reach a printer. Only PrusaLink since Prusa Connect support ended (T-170, 2026-11-05); the type
/// stays in the model and in <c>settings.json</c> ("connection") so that another source could be added later.
/// </summary>
public enum ConnectionType
{
    /// <summary>Local REST API running on the printer.</summary>
    PrusaLink = 1,
}
