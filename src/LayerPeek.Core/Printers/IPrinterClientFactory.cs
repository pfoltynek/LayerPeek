namespace LayerPeek.Core.Printers;

/// <summary>
/// Creates the <see cref="IPrinterClient"/> matching <see cref="PrinterDefinition.ConnectionType"/>.
/// </summary>
public interface IPrinterClientFactory
{
    IPrinterClient Create(PrinterDefinition printer);
}
