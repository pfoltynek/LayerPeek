namespace LayerPeek.App.Platform;

/// <summary>
/// Whether the app starts after the user logs on (T-017; Windows: the app's own shortcut in the Startup folder; macOS, T-019:
/// "Open at Login", the Login Item of <c>SMAppService</c>). Windows: switched on by the install (user decision 2026-10-26);
/// macOS: off by default. The settings window switches it.
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

/// <summary>
/// The OS has the autostart, but the user switched it off in the settings of the OS, and only they can allow it there
/// (macOS, T-019: a Login Item in the state <c>requiresApproval</c>). The settings window then offers to open those settings
/// (<see cref="IAutostartApproval"/>, user decision 2026-10-29).
/// </summary>
internal sealed class AutostartNeedsApprovalException : Exception
{
    public AutostartNeedsApprovalException()
    {
    }

    public AutostartNeedsApprovalException(string message)
        : base(message)
    {
    }

    public AutostartNeedsApprovalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// An autostart the user can allow only in the settings of the OS (macOS, T-019: System Settings &gt; General &gt; Login
/// Items). Implemented next to <see cref="IAutostart"/> by the same object.
/// </summary>
internal interface IAutostartApproval
{
    /// <summary>
    /// Whether only the user can allow the autostart now, in the settings of the OS (macOS: the Login Item is in the state
    /// <c>requiresApproval</c>). Read when the settings window opens and becomes active again (T-019, user decision
    /// 2026-10-29). Never throws (an unreadable state counts as no). The stand-in of the test mode never needs approval.
    /// </summary>
    bool NeedsApproval();

    /// <summary>
    /// Opens the settings of the OS where the user allows the autostart; UI thread. Never throws (a failure is only logged).
    /// The stand-in of the test mode opens nothing.
    /// </summary>
    void OpenApprovalSettings();
}
