using LayerPeek.Core.Printers;
using LayerPeek.PrusaLink;

namespace LayerPeek.App.Composition;

/// <summary>Creates the client matching <see cref="PrinterDefinition.ConnectionType"/>.</summary>
internal sealed class PrinterClientFactory(PrusaLinkClientFactory prusaLink) : IPrinterClientFactory
{
    /// <exception cref="NotSupportedException">The connection type has no client yet (Prusa Connect, T-005).</exception>
    public IPrinterClient Create(PrinterDefinition printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return printer.ConnectionType switch
        {
            ConnectionType.PrusaLink => prusaLink.Create(printer),
            // The monitor reports the printer as unknown with "No client is available for this printer."
            _ => throw new NotSupportedException($"Connection type {printer.ConnectionType} is not supported yet."),
        };
    }
}
