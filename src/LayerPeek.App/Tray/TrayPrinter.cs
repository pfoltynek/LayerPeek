using LayerPeek.Core.Printers;

namespace LayerPeek.App.Tray;

/// <summary>A configured printer with its latest snapshot (<see langword="null"/> until it was polled).</summary>
internal readonly record struct TrayPrinter(PrinterDefinition Definition, PrinterSnapshot? Snapshot)
{
    public TrayStatus Status => Snapshot is null ? TrayStatus.Offline : TrayStatusRules.FromPrinterState(Snapshot.State);
}
