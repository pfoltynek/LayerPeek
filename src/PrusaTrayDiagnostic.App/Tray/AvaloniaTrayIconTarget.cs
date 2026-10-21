using Avalonia.Controls;

namespace PrusaTrayDiagnostic.App.Tray;

/// <summary>Writes to an Avalonia <see cref="TrayIcon"/> (Windows notification area, macOS menu bar).</summary>
/// <param name="toolTipPrefix">
/// Put before every tooltip; empty except in the test mode (T-044), where it tells the test instance's icon apart from the
/// user's own.
/// </param>
internal sealed class AvaloniaTrayIconTarget(TrayIcon trayIcon, string toolTipPrefix = "") : ITrayIconTarget<WindowIcon>
{
    public void SetIcon(WindowIcon icon) => trayIcon.Icon = icon;

    public void SetToolTipText(string text) => trayIcon.ToolTipText = toolTipPrefix + text;
}
