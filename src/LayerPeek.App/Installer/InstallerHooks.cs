using Microsoft.Extensions.Logging;

using LayerPeek.App.Composition;
using LayerPeek.App.Notifications;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Installer;

/// <summary>The registrations toasts need, as a normal start makes them (<see cref="WindowsToastPlatform.Register"/>).</summary>
internal interface IToastRegistration
{
    /// <summary>Registers the COM toast activator and creates or updates the Start menu shortcut.</summary>
    void Register();
}

/// <summary>
/// What the Velopack hooks do (T-017), without UI and within Velopack's time limit:
/// <list type="bullet">
/// <item><c>--veloapp-install</c>: the toast registrations of a normal start (COM activator <c>LocalServer32</c>, and the
/// Start menu shortcut Velopack created gets the activator's <c>ToastActivatorCLSID</c>; docs/velopack.md point 4), so
/// that clicks on toasts reach the app before it ever ran (a silent install does not start it).</item>
/// <item><c>--veloapp-uninstall</c>: the system registrations go: the app's toasts in the notification center, the COM
/// activator, Windows' notification settings of the app, the taskbar choice of the tray icon (T-061) and autostart. Each
/// step runs even when an earlier one failed. The Start menu shortcut is deleted by Velopack itself; settings and passwords
/// stay (decided 2026-10-21).</item>
/// </list>
/// Always the production identity: an installed app is a Release build.
/// </summary>
internal sealed partial class InstallerHooks
{
    /// <summary>The app id whose toasts and notification settings go (<see cref="AppEnvironment.ProductionAppUserModelId"/>).</summary>
    public const string AppUserModelId = AppEnvironment.ProductionAppUserModelId;

    private readonly IToastRegistration _toasts;
    private readonly IToastHistory _history;
    private readonly IComServerRegistry _activators;
    private readonly IAppNotificationSettings _notificationSettings;
    private readonly TrayIconPromotion _trayIcons;
    private readonly IAutostart? _autostart;
    private readonly string? _executablePath;
    private readonly ILogger<InstallerHooks> _logger;

    /// <param name="trayIcons">Clears the taskbar choice of the tray icon of <paramref name="executablePath"/> (T-061).</param>
    /// <param name="autostart">The app's autostart; <see langword="null"/> when there is none to remove.</param>
    /// <param name="executablePath">The installed exe (<c>current\LayerPeek.exe</c>), normally <see cref="Environment.ProcessPath"/>.</param>
    public InstallerHooks(
        IToastRegistration toasts,
        IToastHistory history,
        IComServerRegistry activators,
        IAppNotificationSettings notificationSettings,
        TrayIconPromotion trayIcons,
        IAutostart? autostart,
        string? executablePath,
        ILogger<InstallerHooks> logger)
    {
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(activators);
        ArgumentNullException.ThrowIfNull(notificationSettings);
        ArgumentNullException.ThrowIfNull(trayIcons);
        ArgumentNullException.ThrowIfNull(logger);
        _toasts = toasts;
        _history = history;
        _activators = activators;
        _notificationSettings = notificationSettings;
        _trayIcons = trayIcons;
        _autostart = autostart;
        _executablePath = executablePath;
        _logger = logger;
    }

    /// <summary>The CLSID of the production toast activator (T-068), removed on uninstall.</summary>
    public static Guid ToastActivatorClsid { get; } = new(AppEnvironment.ProductionToastActivatorClsid);

    /// <summary><c>--veloapp-install</c>. Never throws.</summary>
    /// <returns>What failed; empty when everything is registered.</returns>
    public IReadOnlyList<string> AfterInstall()
    {
        List<string> failures = [];
        Step(failures, "Registering the toasts", _toasts.Register);
        return failures;
    }

    /// <summary><c>--veloapp-uninstall</c>. Never throws.</summary>
    /// <returns>What failed; empty when everything is gone.</returns>
    public IReadOnlyList<string> BeforeUninstall()
    {
        List<string> failures = [];
        Step(failures, "Clearing the notifications", () => _history.Clear(AppUserModelId));
        // After the toasts: a click on a toast left in the notification center can no longer start a deleted exe.
        Step(failures, "Unregistering the toast activator", () => _activators.Delete(ToastActivatorClsid));
        Step(failures, "Deleting the notification settings", () => _notificationSettings.Delete(AppUserModelId));
        if (_executablePath is { } executable && TrayIconPromotion.AppliesTo(executable))
        {
            // Never throws; its failures go to the log.
            int cleared = _trayIcons.ClearPromotions(executable);
            LogTrayIconsCleared(cleared);
        }

        if (_autostart is { } autostart)
        {
            Step(failures, "Removing the autostart", () => autostart.SetEnabled(false));
        }

        return failures;
    }

    private void Step(List<string> failures, string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            failures.Add($"{name} failed: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
            LogStepFailed(ex, name);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Uninstall: the taskbar choice of {Count} tray icon entries was cleared.")]
    private partial void LogTrayIconsCleared(int count);

    // Debug only: the failures are reported to Velopack in the result (a warning here would report them twice).
    [LoggerMessage(Level = LogLevel.Debug, Message = "Installer hook step failed: {Step}.")]
    private partial void LogStepFailed(Exception exception, string step);
}
