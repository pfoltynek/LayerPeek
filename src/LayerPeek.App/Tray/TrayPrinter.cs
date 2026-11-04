using LayerPeek.Core.Printers;

namespace LayerPeek.App.Tray;

/// <summary>A configured printer with its latest snapshot (<see langword="null"/> until it was polled).</summary>
/// <param name="IsRecording">Its camera is being recorded (T-146): the tooltip says so.</param>
internal readonly record struct TrayPrinter(PrinterDefinition Definition, PrinterSnapshot? Snapshot, bool IsRecording = false)
{
    public TrayStatus Status => Snapshot is null ? TrayStatus.Offline : TrayStatusRules.FromPrinterState(Snapshot.State);
}
