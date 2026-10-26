using LayerPeek.App.Installer;
using LayerPeek.App.Notifications;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Tests.Platform.Windows;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Installer;

/// <summary>
/// The installer hooks of Velopack (T-017): after the install the toast registrations of a normal start (COM activator,
/// Start menu shortcut with the AppUserModelID and the activator); before the uninstall the system registrations go (toast
/// history, COM activator, notification settings of the app, the taskbar choice of the tray icon, autostart), each step
/// also when an earlier one failed. Settings and passwords stay (decided 2026-10-21). Fakes only: nothing touches the
/// registry, the Start menu or the notification center.
/// </summary>
public sealed class InstallerHooksTests
{
    private const string Exe = @"C:\Users\u\AppData\Local\LayerPeek\current\LayerPeek.exe";

    private static readonly Guid ProductionClsid = new("EC47413C-984D-447A-866A-5F780E408DE7");

    private readonly FakeToastRegistration _toasts = new();
    private readonly RecordingToastHistory _history = new();
    private readonly InMemoryComServerRegistry _activators = new();
    private readonly InMemoryNotificationSettings _notificationSettings = new();
    private readonly InMemoryNotifyIconSettings _trayIcons = new();
    private readonly FakeAutostart _autostart = new() { Enabled = true };
    private readonly ListLogger<InstallerHooks> _logger = new();

    [Fact]
    public void TheProductionIdentityIsUsed()
    {
        Assert.Equal("LayerPeek", InstallerHooks.AppUserModelId);
        Assert.Equal(ProductionClsid, InstallerHooks.ToastActivatorClsid);
    }

    [Fact]
    public void AfterInstallRegistersToastsLikeAStart()
    {
        IReadOnlyList<string> failures = Hooks().AfterInstall();

        Assert.Empty(failures);
        Assert.Equal(1, _toasts.Registrations);
        // Nothing of the uninstall.
        Assert.Empty(_history.Cleared);
        Assert.Empty(_activators.Deleted);
        Assert.True(_autostart.Enabled);
    }

    [Fact]
    public void AfterInstallSwitchesAutostartOn()
    {
        _autostart.Enabled = false;

        Assert.Empty(Hooks().AfterInstall());

        Assert.True(_autostart.Enabled);
    }

    /// <summary>The app works without autostart: a failure does not fail the install, it goes to the log.</summary>
    [Fact]
    public void AfterInstallDoesNotFailWhenAutostartCannotBeSwitchedOn()
    {
        _autostart.Enabled = false;
        _autostart.Failure = new IOException("autostart failed");

        Assert.Empty(Hooks().AfterInstall());

        Assert.False(_autostart.Enabled);
        Assert.Equal(1, _toasts.Registrations);
        Assert.Contains(_logger.Entries, static entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning && entry.Exception is IOException);
    }

    [Fact]
    public void AfterInstallWithoutAutostartRegistersToasts()
    {
        InstallerHooks hooks = new(_toasts, _history, _activators, _notificationSettings, Promotion(), autostart: null, Exe, _logger);

        Assert.Empty(hooks.AfterInstall());
        Assert.Equal(1, _toasts.Registrations);
    }

    [Fact]
    public void AfterInstallReportsAFailedRegistration()
    {
        _toasts.Failure = new UnauthorizedAccessException("denied");

        Assert.Contains("denied", Assert.Single(Hooks().AfterInstall()), StringComparison.Ordinal);
    }

    [Fact]
    public void BeforeUninstallRemovesEverySystemRegistration()
    {
        _activators.Servers[ProductionClsid] = $"\"{Exe}\" -ToastActivated";
        _notificationSettings.Apps.Add("LayerPeek");
        _trayIcons.Add("1", Exe, promoted: 1);
        _trayIcons.Add("2", @"C:\Other\Other.exe", promoted: 1);

        IReadOnlyList<string> failures = Hooks().BeforeUninstall();

        Assert.Empty(failures);
        Assert.Equal(["LayerPeek"], _history.Cleared);
        Assert.Equal([ProductionClsid], _activators.Deleted);
        Assert.Empty(_activators.Servers);
        Assert.Equal(["LayerPeek"], _notificationSettings.Deleted);
        // Only the value of the app's own entry; the entries themselves stay (T-061).
        Assert.Equal(["1"], _trayIcons.ClearedKeys);
        Assert.False(_autostart.Enabled);
        Assert.Equal(0, _toasts.Registrations);
    }

    [Fact]
    public void BeforeUninstallRunsEveryStepWhenEarlierOnesFail()
    {
        _history.Failure = new InvalidOperationException("history failed");
        _activators.DeleteFailure = new UnauthorizedAccessException("activator failed");
        _notificationSettings.Failure = new IOException("settings failed");
        _trayIcons.Add("1", Exe, promoted: 1);
        _autostart.Failure = new IOException("autostart failed");

        IReadOnlyList<string> failures = Hooks().BeforeUninstall();

        Assert.Equal(4, failures.Count);
        Assert.Contains(failures, failure => failure.Contains("history failed", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("activator failed", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("settings failed", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("autostart failed", StringComparison.Ordinal));
        // The tray icon step ran too (it reports its own failures to the log).
        Assert.Equal(["1"], _trayIcons.ClearedKeys);
    }

    [Fact]
    public void BeforeUninstallWithoutAnythingRegisteredSucceeds()
    {
        _autostart.Enabled = false;

        Assert.Empty(Hooks().BeforeUninstall());
        Assert.Empty(_trayIcons.ClearedKeys);
    }

    [Fact]
    public void BeforeUninstallWithoutAutostartOrExecutableStillCleansUp()
    {
        InstallerHooks hooks = new(_toasts, _history, _activators, _notificationSettings, Promotion(), autostart: null, executablePath: null, _logger);

        Assert.Empty(hooks.BeforeUninstall());
        Assert.Equal(["LayerPeek"], _history.Cleared);
        Assert.Equal([ProductionClsid], _activators.Deleted);
    }

    /// <summary>Another host (dotnet.exe) shares its tray entries with other apps: never touched.</summary>
    [Fact]
    public void BeforeUninstallLeavesTheTrayEntriesOfAnotherHost()
    {
        const string host = @"C:\Program Files\dotnet\dotnet.exe";
        _trayIcons.Add("1", host, promoted: 1);

        InstallerHooks hooks = new(_toasts, _history, _activators, _notificationSettings, Promotion(), _autostart, host, _logger);

        Assert.Empty(hooks.BeforeUninstall());
        Assert.Empty(_trayIcons.ClearedKeys);
    }

    private InstallerHooks Hooks() =>
        new(_toasts, _history, _activators, _notificationSettings, Promotion(), _autostart, Exe, _logger);

    private TrayIconPromotion Promotion() => new(_trayIcons, new ListLogger<TrayIconPromotion>());

    private sealed class FakeToastRegistration : IToastRegistration
    {
        public int Registrations { get; private set; }

        public Exception? Failure { get; set; }

        public void Register()
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Registrations++;
        }
    }

    private sealed class RecordingToastHistory : IToastHistory
    {
        public List<string> Cleared { get; } = [];

        public Exception? Failure { get; set; }

        public void Clear(string appUserModelId)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Cleared.Add(appUserModelId);
        }
    }

    private sealed class InMemoryNotificationSettings : IAppNotificationSettings
    {
        public HashSet<string> Apps { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Deleted { get; } = [];

        public Exception? Failure { get; set; }

        public bool Delete(string appUserModelId)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Deleted.Add(appUserModelId);
            return Apps.Remove(appUserModelId);
        }
    }
}

/// <summary>Autostart in memory.</summary>
internal sealed class FakeAutostart : IAutostart
{
    public bool Enabled { get; set; }

    public Exception? Failure { get; set; }

    public Exception? ReadFailure { get; set; }

    public List<bool> Changes { get; } = [];

    public bool IsEnabled() => ReadFailure is not null ? throw ReadFailure : Enabled;

    public void SetEnabled(bool enabled)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Changes.Add(enabled);
        Enabled = enabled;
    }
}
