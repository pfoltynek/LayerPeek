using Avalonia.Controls;

namespace PrusaTrayDiagnostic.App.Tray;

/// <summary>Writes to an Avalonia <see cref="TrayIcon"/> (Windows notification area, macOS menu bar).</summary>
internal sealed class AvaloniaTrayIconTarget(TrayIcon trayIcon) : ITrayIconTarget<WindowIcon>
{
    public void SetIcon(WindowIcon icon) => trayIcon.Icon = icon;

    public void SetToolTipText(string text) => trayIcon.ToolTipText = text;
}
