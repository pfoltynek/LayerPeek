using Avalonia;

namespace LayerPeek.App.Windows;

/// <summary>
/// Remembered state of an auxiliary window (settings, diagnostics; T-094): the client size (DIP) the user gave the window
/// in its normal state and whether it was maximized. The position is not remembered: the window is centred on the monitor
/// it opens on. <see langword="null"/> size: never resized by the user (the designed size applies).
/// </summary>
internal sealed record AuxiliaryWindowState(Size? ClientSize, bool Maximized)
{
    public static AuxiliaryWindowState Default { get; } = new(null, false);
}

/// <summary>Keys of the remembered auxiliary windows in <c>windows.json</c> (T-094).</summary>
internal static class AuxiliaryWindowKeys
{
    public const string Settings = "settings";

    public const string Diagnostics = "diagnostics";
}
