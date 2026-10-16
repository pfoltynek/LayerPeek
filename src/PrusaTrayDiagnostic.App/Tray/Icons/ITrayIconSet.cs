using Avalonia.Controls;

namespace PrusaTrayDiagnostic.App.Tray.Icons;

/// <summary>
/// Supplies the tray icon for a <see cref="TrayIconKey"/>. Separates the state mapping from the look, so that
/// a platform can use its own set (coloured icons on Windows; macOS menu bar template icons follow in T-018).
/// Used on the UI thread only.
/// </summary>
internal interface ITrayIconSet : IDisposable
{
    /// <exception cref="Exception">Rendering failed; callers keep the previous icon.</exception>
    WindowIcon GetIcon(TrayIconKey key);
}
