using System.Runtime.Versioning;

using Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Logging;
using PrusaTrayDiagnostic.App.Notifications;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Support;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Icons;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Images;
using PrusaTrayDiagnostic.Core.Notifications;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;
using PrusaTrayDiagnostic.Core.Web;

namespace PrusaTrayDiagnostic.App.Composition;

/// <summary>
/// Owns everything that runs while the app lives: the service provider, the monitoring session, and the
/// UI-thread-only tray objects (icon set, icon and menu presenters). Created and stopped on the UI thread; the UI
/// objects are released there before anything is awaited.
/// </summary>
internal sealed partial class TrayHost : IAsyncDisposable
{
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(3);

    /// <summary>A save writes a few small items in milliseconds; quitting must still stay within the overall limit.</summary>
    private static readonly TimeSpan PendingSaveTimeout = TimeSpan.FromSeconds(1);

    /// <summary>How long quitting waits for requests in progress through the local web proxies (T-028).</summary>
    private static readonly TimeSpan WebProxyStopTimeout = TimeSpan.FromSeconds(1);

    private readonly ILoggerFactory _loggerFactory;
    private readonly SupportLog _supportLog;
    private readonly ILogger<TrayHost> _logger;
    private readonly ServiceProvider _services;
    private readonly MonitoringSession _session;
    private readonly ITrayIconSet? _iconSet;
    private readonly TrayIconPresenter<WindowIcon>? _iconPresenter;
    private readonly TrayMenuPresenter? _menuPresenter;
    private readonly PrinterMenuIcons? _menuIcons;
    private readonly CancellationTokenSource _backgroundWork;
    private readonly UiLanguage _language;
    private readonly TrayStatusViewModel _status;
    private readonly TrayMenuViewModel _menu;
#if DEBUG
    private readonly TestToastCleanup? _testToastCleanup;
#endif
    private int _stopped;

    private TrayHost(
        ILoggerFactory loggerFactory,
        SupportLog supportLog,
        ServiceProvider services,
        MonitoringSession session,
        ITrayIconSet? iconSet,
        TrayIconPresenter<WindowIcon>? iconPresenter,
        TrayMenuPresenter? menuPresenter,
        PrinterMenuIcons? menuIcons,
        CancellationTokenSource backgroundWork)
    {
        _loggerFactory = loggerFactory;
        _supportLog = supportLog;
        _logger = loggerFactory.CreateLogger<TrayHost>();
        _services = services;
        _session = session;
        _iconSet = iconSet;
        _iconPresenter = iconPresenter;
        _menuPresenter = menuPresenter;
        _menuIcons = menuIcons;
        _backgroundWork = backgroundWork;
        _language = services.GetRequiredService<UiLanguage>();
        _status = services.GetRequiredService<TrayStatusViewModel>();
        _menu = services.GetRequiredService<TrayMenuViewModel>();
#if DEBUG
        _testToastCleanup = AppServices.CreateTestToastCleanup(services);
#endif
        _language.Changed += OnLanguageChanged;
    }

    /// <summary>
    /// UI thread: the language changed (saved settings, T-029) and the tray (menu, tooltip) already shows the new texts;
    /// open auxiliary windows have to format theirs again.
    /// </summary>
    public event EventHandler? LanguageChanged;

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
        // Not owned by the container: it outlives the container dispose during quit.
        ILoggerFactory loggerFactory = logging.Factory;
        ServiceProvider services = AppServices.Create(loggerFactory, environment: environment, logLevel: logging.Level);
        // Concrete type here; the field is ITrayIconSet so that T-018 can select the macOS set by OS.
        ColorTrayIconSet? iconSet = null;
        TrayIconPresenter<WindowIcon>? iconPresenter = null;
        TrayMenuPresenter? menuPresenter = null;
        PrinterMenuIcons? menuIcons = null;
        CancellationTokenSource backgroundWork = new();
        try
        {
            ILogger<MonitoringSession> sessionLogger = services.GetRequiredService<ILogger<MonitoringSession>>();
            (AppSettings initialSettings, SettingsLoadStatus loadStatus, IReadOnlyList<string> storedPrinterIds) = await Task.Run(
                () => MonitoringSession.LoadSettingsWithStatusAsync(
                    services.GetRequiredService<ISettingsStore>,
                    sessionLogger,
                    CancellationToken.None,
                    services.GetRequiredService<RemovedCameraCredentials>()),
                CancellationToken.None).ConfigureAwait(true);

            // Before the tray view models (resolved below) format their first texts.
            services.GetRequiredService<UiLanguage>().Apply(initialSettings.Language);

            if (trayIcon is not null)
            {
                iconSet = CreateIconSet();
                iconPresenter = new TrayIconPresenter<WindowIcon>(
                    new AvaloniaTrayIconTarget(trayIcon, environment.TrayToolTipPrefix),
                    services.GetRequiredService<TrayStatusViewModel>(),
                    iconSet.GetIcon,
                    services.GetRequiredService<ILogger<TrayIconPresenter<WindowIcon>>>());
                // Printer icons (T-039) only where the platform shows them (Windows), sized for the system scaling.
                menuIcons = services.GetRequiredService<TrayMenuOptions>().PrinterIcons ? new PrinterMenuIcons(SystemScaling()) : null;
                menuPresenter = new TrayMenuPresenter(
                    new TrayMenuTarget(new AvaloniaNativeTrayMenu(trayIcon, menuIcons)),
                    services.GetRequiredService<TrayMenuViewModel>(),
                    commands,
                    services.GetRequiredService<ILogger<TrayMenuPresenter>>());

                // The test mode never touches Explorer's settings of the user (T-044).
                if (OperatingSystem.IsWindows() && !environment.IsIsolated)
                {
                    StartTrayIconPromotion(services, backgroundWork.Token);
                }
            }

            MonitoringSession session = services.GetRequiredService<MonitoringSession>();
#if DEBUG
            // The test mode may delay monitoring (T-067): the tray already exists and shows "Starting…" meanwhile.
            session.Start(initialSettings, environment.TestStartDelay);
#else
            session.Start(initialSettings);
#endif
            // Registers the app for toasts in the background now, not with the first notification (T-010).
            if (services.GetRequiredService<INotificationService>() is ToastNotificationService toasts)
            {
                toasts.Start();
            }

            // Remembered camera windows are read in the background, long before a camera window opens; never throws.
            _ = services.GetRequiredService<ICameraWindowStateStore>().LoadAsync(CancellationToken.None);

            // Custom printer images (T-039) in the background: orphans go only with really loaded settings; never throws.
            _ = Task.Run(
                () => PrinterImageStartup.RunAsync(
                    services.GetRequiredService<IPrinterImageStore>,
                    services.GetRequiredService<CustomPrinterImages>(),
                    initialSettings,
                    loadStatus,
                    storedPrinterIds,
                    services.GetRequiredService<ILogger<CustomPrinterImages>>(),
                    backgroundWork.Token),
                CancellationToken.None);

            return new TrayHost(loggerFactory, new SupportLog(logging.Directory, logging.Flush), services, session, iconSet, iconPresenter, menuPresenter, menuIcons, backgroundWork);
        }
        catch
        {
            menuPresenter?.Dispose();
            menuIcons?.Dispose();
            iconPresenter?.Dispose();
            iconSet?.Dispose();
            backgroundWork.Dispose();
            services.Dispose();
            throw;
        }
    }

    /// <summary>Requests the monitoring to stop without waiting (e.g. the OS is ending the session).</summary>
    public void RequestStop() => _session.RequestStop();

    /// <summary>
    /// A click on a toast that reached the app through the COM toast activator (T-068): handled like a click on the banner
    /// (<see cref="ToastActivationRouter"/>: printer id only, its web through the proxy). Never throws; ignored once stopping.
    /// </summary>
    public void ActivateToast(string launchArgument)
    {
        if (Volatile.Read(ref _stopped) == 0)
        {
            _ = _services.GetRequiredService<ToastActivationRouter>().ActivateAsync(launchArgument);
        }
    }

    /// <summary>Opens web pages (About window link). Valid until <see cref="StopAsync"/>.</summary>
    public IUrlLauncher UrlLauncher => _services.GetRequiredService<IUrlLauncher>();

    /// <summary>Content of a new About window with the support actions (T-048). Valid until <see cref="StopAsync"/>.</summary>
    public AboutViewModel CreateAboutViewModel() => new(UrlLauncher, AppServices.CreateSupportActions(_services, _supportLog));

    /// <summary>A view model for a new diagnostics window; the window's owner disposes it. Valid until <see cref="StopAsync"/>.</summary>
    public DiagnosticsViewModel CreateDiagnosticsViewModel(string? initialPrinterId) => AppServices.CreateDiagnosticsViewModel(_services, initialPrinterId);

    /// <summary>
    /// A view model for a new settings window; the window's owner disposes it. Valid until <see cref="StopAsync"/>.
    /// <paramref name="screens"/>: the monitors offered for the camera window (those of the settings window);
    /// <paramref name="cameraWindowPlacer"/>: the camera windows, opened or placed again by "Apply" (T-040).
    /// </summary>
    internal SettingsViewModel CreateSettingsViewModel(IScreenCatalog? screens = null, ICameraWindowPlacer? cameraWindowPlacer = null) =>
        AppServices.CreateSettingsViewModel(_services, screens, cameraWindowPlacer);

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

    /// <summary>Creates loggers for UI objects outside the container.</summary>
    public ILogger<T> CreateLogger<T>() => _loggerFactory.CreateLogger<T>();

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
        _backgroundWork.Cancel();

        await WaitForPendingSaveAsync().ConfigureAwait(true);
        // A detected model being written (T-039): a tiny atomic write; an interrupted one keeps the previous file.
        await _session.ModelSaves.WaitAsync(PendingSaveTimeout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        await WaitForCameraWindowStateAsync().ConfigureAwait(true);
        // In parallel: open browser tabs of the local web proxies (T-028) stop working, and their ports close.
        await Task.WhenAll(_session.StopAsync(timeout), StopWebProxiesAsync()).ConfigureAwait(true);
#if DEBUG
        // Read before the container goes: the toast thread may still be registering the test app (writing its shortcut).
        Task toastThreadEnded = _testToastCleanup is not null && _services.GetRequiredService<INotificationService>() is ToastNotificationService toastService
            ? toastService.Completion
            : Task.CompletedTask;
#endif
        try
        {
            // The session was stopped above, so disposing it (via the container) does not wait again.
            await _services.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Recorded here, while the (caller-owned) logger factory is certainly alive; quitting goes on.
            LogDisposeFailed(ex);
        }

#if DEBUG
        await RunTestToastCleanupAsync(_testToastCleanup, toastThreadEnded, _logger).ConfigureAwait(true);
#endif

        _backgroundWork.Dispose();
    }

    public ValueTask DisposeAsync() => new(StopAsync(DefaultStopTimeout));

#if DEBUG
    /// <summary>
    /// Debug test mode with toasts (T-067): once the toast thread has ended (released, so it can no longer write the
    /// shortcut while registering), the toasts of the test app leave the notification center and its Start menu shortcut
    /// is deleted. Off the UI thread (WinRT, multithreaded apartment), each step within its quit limit; never throws (the
    /// cleanup logs its own failures). A cleanup that cannot finish here is left to <c>--ptd-e2e-clear-toasts</c>.
    /// </summary>
    internal static async Task RunTestToastCleanupAsync(TestToastCleanup? cleanup, Task toastThreadEnded, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(toastThreadEnded);
        ArgumentNullException.ThrowIfNull(logger);
        if (cleanup is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            await toastThreadEnded.WaitAsync(TestToastThreadTimeout).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            // Cleaned up anyway: the hung thread most likely is in an OS call, not about to write the shortcut.
            LogTestToastThreadStillRunning(logger, ex);
        }

        try
        {
            await Task.Run(cleanup.Run, CancellationToken.None).WaitAsync(TestToastCleanupTimeout).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            LogTestToastCleanupFailed(logger, ex);
        }
    }

    private static readonly TimeSpan TestToastThreadTimeout = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan TestToastCleanupTimeout = TimeSpan.FromSeconds(1);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Test mode: the toast thread did not end while quitting; removing the toasts and the shortcut of the test app anyway.")]
    private static partial void LogTestToastThreadStillRunning(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Test mode: removing the toasts and the shortcut of the test app did not finish while quitting.")]
    private static partial void LogTestToastCleanupFailed(ILogger logger, Exception exception);
#endif

    /// <summary>
    /// A save from the settings window that is still running finishes first (at most <see cref="PendingSaveTimeout"/>), so
    /// that quitting in the middle of it does not leave a new password behind without its settings.
    /// </summary>
    private async Task WaitForPendingSaveAsync()
    {
        try
        {
            if (!await _services.GetRequiredService<ISettingsEditorStore>().WaitForPendingSaveAsync(PendingSaveTimeout).ConfigureAwait(true))
            {
                LogPendingSaveTimedOut(PendingSaveTimeout);
            }
        }
        catch (Exception ex)
        {
            LogPendingSaveFailed(ex);
        }
    }

    /// <summary>Camera windows closed by quitting save where they were; a tiny file, written within the same limit as settings.</summary>
    private async Task WaitForCameraWindowStateAsync()
    {
        try
        {
            if (!await _services.GetRequiredService<ICameraWindowStateStore>().WaitForPendingSaveAsync(PendingSaveTimeout).ConfigureAwait(true))
            {
                LogCameraWindowStateTimedOut(PendingSaveTimeout);
            }
        }
        catch (Exception ex)
        {
            LogCameraWindowStateFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving the camera window state did not finish within {Timeout} while quitting.")]
    private partial void LogCameraWindowStateTimedOut(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "Waiting for the camera window state to be saved failed while quitting.")]
    private partial void LogCameraWindowStateFailed(Exception exception);

    /// <summary>
    /// Stops the local web proxies, waiting at most <see cref="WebProxyStopTimeout"/> for requests in progress (e.g. an
    /// upload); after that they are stopped without waiting. Disposing the container afterwards finds none running.
    /// Never faults.
    /// </summary>
    private async Task StopWebProxiesAsync()
    {
        try
        {
            using CancellationTokenSource timeout = new(WebProxyStopTimeout);
            await _services.GetRequiredService<IPrinterWebGateway>().StopAllAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWebProxyStopFailed(ex);
        }
    }

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "A settings save did not finish within {Timeout} while quitting.")]
    private partial void LogPendingSaveTimedOut(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "Waiting for a settings save failed while quitting.")]
    private partial void LogPendingSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping the web proxies failed while quitting.")]
    private partial void LogWebProxyStopFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Releasing services failed while stopping.")]
    private partial void LogDisposeFailed(Exception exception);

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

    /// <summary>Scaling for the tray menu icons: the menu opens as a new popup, so its monitor is not known in advance.</summary>
    private static double SystemScaling() => OperatingSystem.IsWindows() ? SystemDpiNative.GetScaling() : 1.0;

    // macOS menu bar template icons follow in T-018; until then every OS uses the coloured set.
    private static ColorTrayIconSet CreateIconSet() => new(multiSize: OperatingSystem.IsWindows());
}
