using System.Runtime.Versioning;

using Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Layout;
using LayerPeek.App.Diagnostics;
using LayerPeek.App.Localization;
using LayerPeek.App.Logging;
using LayerPeek.App.Notifications;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.MacOS;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Printers;
using LayerPeek.App.Settings;
using LayerPeek.App.Support;
using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Icons;
using LayerPeek.App.Tray.Menu;
using LayerPeek.App.Windows;
using LayerPeek.Core.Images;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Web;

namespace LayerPeek.App.Composition;

/// <summary>
/// Owns the UI-thread-only tray objects (icon set, icon and menu presenters) over the services of <see cref="ServiceHost"/>
/// (the service provider and the monitoring session). Normally created together with them; on macOS without an active
/// display (T-099) the tray attaches to the services that already run. Created and stopped on the UI thread; the UI
/// objects are released there before anything is awaited.
/// </summary>
internal sealed partial class TrayHost : IAsyncDisposable
{
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(3);

    private readonly ServiceHost _core;
    private readonly ServiceProvider _services;
    private readonly ITrayIconSet? _iconSet;
    private readonly TrayIconPresenter<WindowIcon>? _iconPresenter;
    private readonly TrayMenuPresenter? _menuPresenter;
    private readonly PrinterMenuIcons? _menuIcons;
    private readonly UiLanguage _language;
    private readonly TrayStatusViewModel _status;
    private readonly TrayMenuViewModel _menu;
    private int _stopped;

    private TrayHost(
        ServiceHost core,
        ITrayIconSet? iconSet,
        TrayIconPresenter<WindowIcon>? iconPresenter,
        TrayMenuPresenter? menuPresenter,
        PrinterMenuIcons? menuIcons)
    {
        _core = core;
        _services = core.Services;
        _iconSet = iconSet;
        _iconPresenter = iconPresenter;
        _menuPresenter = menuPresenter;
        _menuIcons = menuIcons;
        _language = _services.GetRequiredService<UiLanguage>();
        _status = _services.GetRequiredService<TrayStatusViewModel>();
        _menu = _services.GetRequiredService<TrayMenuViewModel>();
        _language.Changed += OnLanguageChanged;
    }

    /// <summary>
    /// UI thread: the language changed (saved settings, T-029) and the tray (menu, tooltip) already shows the new texts;
    /// open auxiliary windows have to format theirs again.
    /// </summary>
    public event EventHandler? LanguageChanged;

    /// <summary>The services under the tray (T-099: the same instance that ran before the tray existed).</summary>
    internal ServiceHost Core => _core;

    /// <summary>
    /// Loads the settings (off the UI thread), applies their language and only then creates the tray objects, so that the
    /// first menu and tooltip are already in the chosen language; then starts monitoring with the loaded settings. Call on
    /// the UI thread; the returned task continues there.
    /// </summary>
    /// <param name="trayIcon">Tray icon to drive; <see langword="null"/> when the platform has none (monitoring still runs).</param>
    /// <param name="commands">Diagnostics / About / Quit handlers of the tray menu.</param>
    /// <param name="environment">Data folder and isolation of this run (T-044).</param>
    /// <param name="logging">The logging of the process (T-024); owned by <c>Program.Main</c>, it outlives the host.</param>
    public static async Task<TrayHost> StartAsync(TrayIcon? trayIcon, ITrayAppCommands commands, AppEnvironment environment, AppLogging logging)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logging);
        ServiceHost core = await ServiceHost.CreateAsync(environment, logging).ConfigureAwait(true);
        try
        {
            return Attach(core, trayIcon, commands, environment);
        }
        catch
        {
            core.DisposeAfterFailedStart();
            throw;
        }
    }

    /// <summary>
    /// Creates the tray objects over <paramref name="core"/> (they show its current state at once) and starts monitoring
    /// unless it already runs (T-099: started before the UI while macOS had no active display; never a second session).
    /// Call on the UI thread.
    /// </summary>
    public static TrayHost Attach(ServiceHost core, TrayIcon? trayIcon, ITrayAppCommands commands, AppEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(environment);
        ServiceProvider services = core.Services;
        ITrayIconSet? iconSet = null;
        TrayIconPresenter<WindowIcon>? iconPresenter = null;
        TrayMenuPresenter? menuPresenter = null;
        PrinterMenuIcons? menuIcons = null;
        try
        {
            if (trayIcon is not null)
            {
                // Windows: coloured multi-size icons (T-007); macOS: menu bar template icons (T-018).
                iconSet = TrayIconSets.ForCurrentPlatform();
                TrayIconSets.Prepare(trayIcon, iconSet);
                iconPresenter = new TrayIconPresenter<WindowIcon>(
                    new AvaloniaTrayIconTarget(trayIcon, environment.TrayToolTipPrefix),
                    services.GetRequiredService<TrayStatusViewModel>(),
                    iconSet.GetIcon,
                    services.GetRequiredService<ILogger<TrayIconPresenter<WindowIcon>>>());
                // Printer icons (T-039 Windows, T-116 macOS), sized for the platform's menu and display scaling.
                menuIcons = services.GetRequiredService<TrayMenuOptions>().PrinterIcons ? new PrinterMenuIcons(MenuIconSize()) : null;
                menuPresenter = new TrayMenuPresenter(
                    new TrayMenuTarget(new AvaloniaNativeTrayMenu(trayIcon, menuIcons)),
                    services.GetRequiredService<TrayMenuViewModel>(),
                    commands,
                    services.GetRequiredService<ILogger<TrayMenuPresenter>>());

                // The test mode never touches Explorer's settings of the user (T-044).
                if (OperatingSystem.IsWindows() && !environment.IsIsolated)
                {
                    StartTrayIconPromotion(services, core.BackgroundWork);
                }
            }

            // The normal start: monitoring starts now, after the tray exists (it shows "Starting…" meanwhile).
            core.StartMonitoring();
            return new TrayHost(core, iconSet, iconPresenter, menuPresenter, menuIcons);
        }
        catch
        {
            menuPresenter?.Dispose();
            menuIcons?.Dispose();
            iconPresenter?.Dispose();
            iconSet?.Dispose();
            throw;
        }
    }

    /// <summary>Requests the monitoring to stop without waiting (e.g. the OS is ending the session).</summary>
    public void RequestStop() => _core.RequestStop();

    /// <summary>
    /// A click on a toast that reached the app through the COM toast activator (T-068): handled like a click on the banner
    /// (<see cref="ToastActivationRouter"/>: printer id only, its web through the proxy). Never throws; ignored once stopping.
    /// </summary>
    public void ActivateToast(string launchArgument)
    {
        if (Volatile.Read(ref _stopped) == 0)
        {
            _core.ActivateToast(launchArgument);
        }
    }

    /// <summary>The settings window for a click on the test notification (T-101, <see cref="ServiceHost.AttachSettingsOpener"/>).</summary>
    public void AttachSettingsOpener(Action open) => _core.AttachSettingsOpener(open);

    /// <summary>The update the app offers (T-058): the menu item, the notification and the settings window. Valid until <see cref="StopAsync"/>.</summary>
    internal Updates.UpdateOffer UpdateOffer => _services.GetRequiredService<Updates.UpdateOffer>();

    /// <summary>Opens web pages (About window link). Valid until <see cref="StopAsync"/>.</summary>
    public IUrlLauncher UrlLauncher => _services.GetRequiredService<IUrlLauncher>();

    /// <summary>Content of a new About window with the support actions (T-048). Valid until <see cref="StopAsync"/>.</summary>
    public AboutViewModel CreateAboutViewModel() => new(UrlLauncher, AppServices.CreateSupportActions(_services, _core.SupportLog));

    /// <summary>A view model for a new diagnostics window; the window's owner disposes it. Valid until <see cref="StopAsync"/>.</summary>
    public DiagnosticsViewModel CreateDiagnosticsViewModel(string? initialPrinterId) => AppServices.CreateDiagnosticsViewModel(_services, initialPrinterId);

    /// <summary>
    /// A view model for a new settings window; the window's owner disposes it. Valid until <see cref="StopAsync"/>.
    /// <paramref name="screens"/>: the monitors offered for the camera window (those of the settings window);
    /// <paramref name="cameraWindowPlacer"/>: the camera windows, opened or placed again by "Apply" (T-040);
    /// <paramref name="layoutModeLauncher"/>: the camera windows, whose layout mode the Widgets tab starts (T-092).
    /// </summary>
    internal SettingsViewModel CreateSettingsViewModel(IScreenCatalog? screens = null, ICameraWindowPlacer? cameraWindowPlacer = null, ICameraLayoutModeLauncher? layoutModeLauncher = null) =>
        AppServices.CreateSettingsViewModel(_services, screens, cameraWindowPlacer, layoutModeLauncher);

    /// <summary>
    /// A view model for a new camera window of <paramref name="printerId"/> (from <paramref name="saved"/> when given, else
    /// from the monitored settings), or <see langword="null"/> when it has no enabled camera; the window's owner disposes
    /// it. Valid until <see cref="StopAsync"/>.
    /// </summary>
    public CameraViewModel? CreateCameraViewModel(string printerId, PrinterDefinition? saved) => AppServices.CreateCameraViewModel(_services, printerId, saved);

    /// <summary>
    /// UI thread: a camera window opened or closed; the tray menu shows "Hide camera" while it is open (T-040).
    /// Ignored once stopping started.
    /// </summary>
    internal void OnCameraWindowOpenChanged(string printerId, bool isOpen)
    {
        if (Volatile.Read(ref _stopped) == 0)
        {
            _services.GetRequiredService<TrayMenuViewModel>().SetCameraWindowOpen(printerId, isOpen);
        }
    }

    /// <summary>Where the settings and diagnostics windows remember their size (T-094). Valid until <see cref="StopAsync"/>.</summary>
    internal IAuxiliaryWindowStateStore WindowStates => _services.GetRequiredService<IAuxiliaryWindowStateStore>();

    /// <summary>Creates loggers for UI objects outside the container.</summary>
    public ILogger<T> CreateLogger<T>() => _core.LoggerFactory.CreateLogger<T>();

    /// <summary>
    /// Stops monitoring (waiting at most <paramref name="timeout"/>) and releases everything. Call on the UI thread.
    /// </summary>
    public async Task StopAsync(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        // UI-thread-only objects first, synchronously, before any continuation could move to another thread.
        // From here on, menu clicks, late monitor updates and language switches no longer reach the tray.
        _language.Changed -= OnLanguageChanged;
        _menuPresenter?.Dispose();
        _menuIcons?.Dispose();
        _iconPresenter?.Dispose();
        _iconSet?.Dispose();
        await _core.StopAsync(timeout).ConfigureAwait(true);
    }

    public ValueTask DisposeAsync() => new(StopAsync(DefaultStopTimeout));

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        _status.RefreshTexts();
        _menu.RefreshTexts();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// In the background: shows the new tray icon on the taskbar instead of the overflow, once per executable path
    /// (T-061, workaround). Never throws; cancelled when quitting.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void StartTrayIconPromotion(ServiceProvider services, CancellationToken cancellationToken)
    {
        string? executablePath = Environment.ProcessPath;
        if (executablePath is null || !TrayIconPromotion.AppliesTo(executablePath))
        {
            return;
        }

        TrayIconPromotion promotion = new(new NotifyIconSettingsRegistry(), services.GetRequiredService<ILogger<TrayIconPromotion>>());
        _ = Task.Run(() => promotion.PromoteAsync(executablePath, cancellationToken), CancellationToken.None);
    }

    /// <summary>
    /// Size of the tray menu icons. Windows: the system scaling, the menu opens as a new popup, so its monitor is not known
    /// in advance. macOS: the NSMenu image size of Avalonia.Native for the menu font, at the highest display scaling.
    /// </summary>
    private static PrinterMenuIconSize MenuIconSize() =>
        OperatingSystem.IsWindows() ? PrinterMenuIconSize.Windows(SystemDpiNative.GetScaling())
        : OperatingSystem.IsMacOS() ? PrinterMenuIconSize.MacOS(MacMenuMetrics.MenuFontPointSize(), MacMenuMetrics.HighestBackingScaleFactor())
        : PrinterMenuIconSize.Windows(1.0);
}
