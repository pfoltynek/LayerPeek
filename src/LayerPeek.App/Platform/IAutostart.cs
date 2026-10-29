namespace LayerPeek.App.Platform;

/// <summary>
/// Whether the app starts after the user logs on (T-017; Windows: the app's own shortcut in the Startup folder; macOS, T-019:
/// "Open at Login", the Login Item of <c>SMAppService</c>). Off by default; the settings window switches it.
/// </summary>
internal interface IAutostart
{
    /// <summary>Whether this copy of the app starts after logon.</summary>
    bool IsEnabled();

    /// <summary>Switches the start after logon on or off.</summary>
    /// <exception cref="Exception">The OS refused the change.</exception>
    void SetEnabled(bool enabled);
}

/// <summary>The autostart of this run for the settings window.</summary>
/// <param name="Autostart"><see langword="null"/> where there is none.</param>
/// <param name="NeedsInstallation">
/// No autostart only because the app is not installed by the installer (a <c>.zip</c> or a development build, T-017; on
/// macOS not running from <c>LayerPeek.app</c> in a permanent place, T-019): the settings show the option disabled with the
/// reason.
/// </param>
internal sealed record AutostartOption(IAutostart? Autostart, bool NeedsInstallation = false);
