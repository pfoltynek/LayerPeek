namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>What clicking a tray menu item does.</summary>
public enum TrayMenuCommand
{
    /// <summary>Informational item or separator.</summary>
    None,

    /// <summary>Opens <see cref="TrayMenuEntry.WebUri"/> (PrusaLink / Prusa Connect) in the browser.</summary>
    OpenWeb,

    /// <summary>Settings window (T-015).</summary>
    Settings,

    /// <summary>Diagnostics window (T-011).</summary>
    Diagnostics,
    About,
    Quit,
}

/// <summary>
/// One tray menu item as the user sees it. Immutable with value equality, so that an update reaches the platform
/// menu only when something visible (or the click target) changed.
/// </summary>
/// <param name="Header">Display text, not escaped (the platform adapter handles access-key markers).</param>
/// <param name="ToolTip">Hint shown on hover where the platform supports it.</param>
/// <param name="WebUri">Address opened by <see cref="TrayMenuCommand.OpenWeb"/>; already validated as http(s).</param>
internal sealed record TrayMenuEntry(TrayMenuCommand Command, string Header, bool IsEnabled, string? ToolTip = null, Uri? WebUri = null)
{
    public static TrayMenuEntry Separator { get; } = new(TrayMenuCommand.None, string.Empty, IsEnabled: false) { IsSeparator = true };

    public bool IsSeparator { get; private init; }
}

/// <summary>Which auxiliary windows exist; their items are shown disabled until the window is implemented.</summary>
internal sealed record TrayMenuOptions(bool SettingsAvailable, bool DiagnosticsAvailable)
{
    /// <summary>Diagnostics (T-011) exists; settings (T-015) are not implemented yet.</summary>
    public static TrayMenuOptions Default { get; } = new(SettingsAvailable: false, DiagnosticsAvailable: true);
}
