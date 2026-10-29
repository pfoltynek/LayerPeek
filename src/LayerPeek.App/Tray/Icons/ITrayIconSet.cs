using Avalonia.Controls;

namespace LayerPeek.App.Tray.Icons;

/// <summary>
/// Supplies the tray icon for a <see cref="TrayIconKey"/>. Separates the state mapping from the look, so that
/// a platform can use its own set (coloured icons on Windows, menu bar template icons on macOS: T-018).
/// Used on the UI thread only.
/// </summary>
internal interface ITrayIconSet : IDisposable
{
    /// <summary>The icons are macOS template images (black and alpha only, recoloured by the system).</summary>
    bool IsTemplate { get; }

    /// <exception cref="Exception">Rendering failed; callers keep the previous icon.</exception>
    WindowIcon GetIcon(TrayIconKey key);
}
