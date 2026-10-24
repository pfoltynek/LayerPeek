using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Platform.Windows;

/// <summary>
/// Start after logon on Windows (T-017, user decision 2026-10-24): the app's own shortcut <c>LayerPeek.lnk</c> in the per-user
/// Startup folder, pointing to the running exe. For the installed app that is <c>%LOCALAPPDATA%\LayerPeek\current\LayerPeek.exe</c>,
/// so Velopack keeps it across updates (same path) and deletes it on uninstall (its target lies in the installation; the
/// uninstall hook removes it too). It carries the same AppUserModelID and toast activator as the Start menu shortcut: a
/// Velopack update rewrites the id of every shortcut into the installation anyway, and Windows may take either shortcut
/// for the toasts of the id. No admin rights, no registry.
/// </summary>
internal sealed partial class StartupShortcutAutostart : IAutostart
{
    /// <summary>The same name as the Start menu shortcut (what Windows shows under Settings &gt; Apps &gt; Startup).</summary>
    public const string ShortcutFileName = AppShortcutRegistration.ShortcutFileName;

    private readonly IShortcutStore _store;
    private readonly string _shortcutPath;
    private readonly AppShortcut _desired;
    private readonly ILogger<StartupShortcutAutostart> _logger;

    /// <param name="shortcutPath">The shortcut in the Startup folder (the test mode has its own folder, T-044).</param>
    /// <param name="desired">The running exe with the identity of the Start menu shortcut.</param>
    public StartupShortcutAutostart(IShortcutStore store, string shortcutPath, AppShortcut desired, ILogger<StartupShortcutAutostart> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentException.ThrowIfNullOrWhiteSpace(desired.TargetPath, nameof(desired));
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _shortcutPath = shortcutPath;
        _desired = desired;
        _logger = logger;
    }

    /// <summary>Where the shortcut goes (composition tests).</summary>
    internal string ShortcutPath => _shortcutPath;

    /// <summary>The shortcut it writes (composition tests).</summary>
    internal AppShortcut Desired => _desired;

    /// <summary>
    /// On when the shortcut starts this exe; a shortcut of another copy (a <c>.zip</c> or a development build) or one that
    /// cannot be read counts as off (switching on replaces it).
    /// </summary>
    public bool IsEnabled()
    {
        try
        {
            return _store.Read(_shortcutPath) is { } existing
                && AppShortcutRegistration.Decide(existing, existing with { TargetPath = _desired.TargetPath }) == ShortcutAction.UpToDate;
        }
        catch (Exception ex)
        {
            LogUnreadable(ex);
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            AppShortcut? existing = null;
            try
            {
                existing = _store.Read(_shortcutPath);
            }
            catch (Exception ex)
            {
                // A damaged file with our name: replaced below.
                LogUnreadable(ex);
            }

            if (AppShortcutRegistration.Decide(existing, _desired) != ShortcutAction.UpToDate)
            {
                _store.Write(_shortcutPath, _desired);
            }

            LogSwitched(enabled);
            return;
        }

        _store.Delete(_shortcutPath);
        LogSwitched(enabled);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The autostart shortcut in the Startup folder could not be read.")]
    private partial void LogUnreadable(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Start after logon switched on: {Enabled}.")]
    private partial void LogSwitched(bool enabled);
}
