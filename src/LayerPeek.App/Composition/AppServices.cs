using System.Runtime.Versioning;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using LayerPeek.App.Cameras;
using LayerPeek.App.Diagnostics;
using LayerPeek.App.History;
using LayerPeek.App.Installer;
using LayerPeek.App.Localization;
using LayerPeek.App.Logging;
using LayerPeek.App.Notifications;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Printers;
using LayerPeek.App.Settings;
using LayerPeek.App.Support;
using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Menu;
using LayerPeek.App.Windows;
using LayerPeek.Camera.FFmpeg;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.History;
using LayerPeek.Core.Images;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Secrets;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Updates;
using LayerPeek.Core.Web;
using LayerPeek.PrusaLink;

namespace LayerPeek.App.Composition;

/// <summary>
/// Composition root. Every service is registered explicitly with a factory (no scanning, no reflection-based
/// constructor selection), as required by Native AOT. Platform implementations are chosen by OS here.
/// UI-thread-only objects (tray icons) are not registered: the container may be disposed off the UI thread;
/// <see cref="TrayHost"/> owns them instead.
/// </summary>
internal static class AppServices
{
    /// <param name="loggerFactory">
    /// Logger factory owned by the caller. Registered as an instance, so disposing the container does not dispose it:
    /// quitting still logs after the container is gone. <see langword="null"/>: the container creates its own.
    /// </param>
    /// <param name="cameraFeedFactory">Video decoder for tests; <see langword="null"/>: the app's (see <see cref="CreateCameraFeedFactory"/>).</param>
    /// <param name="cameraWindowStateStore">
    /// Where camera windows are remembered, for tests; <see langword="null"/>: <c>camera-windows.json</c> in the data folder.
    /// </param>
    /// <param name="environment">Data folder and isolation (T-044); <see langword="null"/>: <see cref="AppEnvironment.Production"/>.</param>
    /// <param name="logLevel">
    /// Minimum level of the file log, set from the settings (T-047); <see langword="null"/>: a level of its own, connected to
    /// nothing (tests).
    /// </param>
    public static ServiceProvider Create(
        ILoggerFactory? loggerFactory = null,
        Func<IServiceProvider, ICameraFeedFactory>? cameraFeedFactory = null,
        Func<IServiceProvider, ICameraWindowStateStore>? cameraWindowStateStore = null,
        AppEnvironment? environment = null,
        LogLevelSwitch? logLevel = null) =>
        Create(loggerFactory, OperatingSystem.IsWindows(), cameraFeedFactory, cameraWindowStateStore, environment, logLevel);

    /// <param name="windows">Use the Windows notification implementation (only effective on Windows); lets tests check the other branch.</param>
    internal static ServiceProvider Create(
        ILoggerFactory? loggerFactory,
        bool windows,
        Func<IServiceProvider, ICameraFeedFactory>? cameraFeedFactory = null,
        Func<IServiceProvider, ICameraWindowStateStore>? cameraWindowStateStore = null,
        AppEnvironment? environment = null,
        LogLevelSwitch? logLevel = null)
    {
        AppEnvironment env = environment ?? AppEnvironment.Production;
        ServiceCollection services = new();

        // The factory of the process (T-024: file log) when given; otherwise messages are discarded (tests).
        services.AddLogging();
        if (loggerFactory is not null)
        {
            services.AddSingleton(loggerFactory);
        }

        services.AddSingleton(TimeProvider.System);
        // Every file of the app lies in the data folder of the environment (T-044: the test folder in test mode).
        services.AddSingleton(env);
        services.AddSingleton<ISecretStore>(_ => CreateSecretStore(env));
        // Created on first use (by the monitoring session on a background thread), so building the provider touches no files.
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(env.DataDirectory(), sp.GetRequiredService<TimeProvider>()));

        services.AddPrusaLink();
        services.AddSingleton<IPrinterClientFactory>(static sp => new PrinterClientFactory(sp.GetRequiredService<PrusaLinkClientFactory>()));
        services.AddSingleton<IPrinterMonitor>(static sp => new PrinterMonitor(
            sp.GetRequiredService<IPrinterClientFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<PrinterMonitor>>()));

        services.AddSingleton<IUiDispatcher>(static _ => new AvaloniaUiDispatcher());
        // Language of the texts (T-029): applied at startup before any text exists, then from every saved setting.
        services.AddSingleton(static _ => new UiLanguage());
        services.AddSingleton(static sp => new UiLanguageObserver(sp.GetRequiredService<UiLanguage>(), sp.GetRequiredService<IUiDispatcher>()));
        // Detailed logging (T-047): the saved setting switches the level of the file log; the test mode stays at Debug.
        services.AddSingleton(sp => new DebugLoggingObserver(
            logLevel ?? new LogLevelSwitch(LogLevel.Information),
            env.IsIsolated,
            sp.GetRequiredService<ILogger<DebugLoggingObserver>>()));
        services.AddSingleton(static sp => new TrayStatusViewModel(sp.GetRequiredService<IPrinterMonitor>(), sp.GetRequiredService<IUiDispatcher>()));
#if DEBUG
        // The test mode never starts a browser: the addresses go to a file of its data folder (T-067).
        services.AddSingleton<IUrlLauncher>(sp => env.IsIsolated
            ? new RecordingUrlLauncher(env.DataDirectory(), sp.GetRequiredService<ILogger<RecordingUrlLauncher>>())
            : new ProcessUrlLauncher(sp.GetRequiredService<ILogger<ProcessUrlLauncher>>()));
#else
        services.AddSingleton<IUrlLauncher>(static sp => new ProcessUrlLauncher(sp.GetRequiredService<ILogger<ProcessUrlLauncher>>()));
#endif
        // The log folder in Explorer / Finder (T-048).
        services.AddSingleton<IFolderLauncher>(static sp => new ProcessFolderLauncher(sp.GetRequiredService<ILogger<ProcessFolderLauncher>>()));
        // Settings (T-015) and diagnostics (T-011) windows both exist.
        services.AddSingleton(static _ => TrayMenuOptions.ForCurrentPlatform);
        // Custom printer images (T-039): files next to settings.json (created on first use, may throw without a data folder) and
        // the images in use, in memory for the tray menu and the diagnostics window.
        services.AddSingleton<IPrinterImageStore>(sp => new FilePrinterImageStore(env.DataDirectory(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(static _ => new CustomPrinterImages());
        // Opens PrusaLink logged in through the local web proxy registered by AddPrusaLink (T-028); the proxy starts on the
        // first click, and disposing the container (quit) stops it.
        services.AddSingleton(static sp => new PrinterWebOpener(
            sp.GetRequiredService<IPrinterWebGateway>(),
            sp.GetRequiredService<IUrlLauncher>(),
            sp.GetRequiredService<ILogger<PrinterWebOpener>>()));
        services.AddSingleton(static sp => new TrayMenuViewModel(
            sp.GetRequiredService<IPrinterMonitor>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<PrinterWebOpener>(),
            sp.GetRequiredService<TrayMenuOptions>(),
            sp.GetRequiredService<CustomPrinterImages>()));
        services.AddSingleton(static sp => new ToastActivationRouter(
            sp.GetRequiredService<PrinterWebOpener>(),
            sp.GetRequiredService<ILogger<ToastActivationRouter>>(),
            sp.GetRequiredService<TimeProvider>()));
        // Test mode (T-044): no toasts, so the Start menu shortcut with the AppUserModelID (T-010) is never written; with the
        // Debug switch of T-067 toasts of the test app with a shortcut of its own.
        services.AddSingleton(sp => CreateNotificationService(sp, windows, env));
        services.AddSingleton(static sp => new PrintEventNotifier(
            sp.GetRequiredService<IPrinterMonitor>(),
            sp.GetRequiredService<INotificationService>(),
            sp.GetRequiredService<ILogger<PrintEventNotifier>>()));
        services.AddSingleton(static sp => new PrintNotificationObserver(sp.GetRequiredService<PrintEventNotifier>()));
        // In-memory only, one day per printer (T-012); read by the temperature chart (T-011).
        services.AddSingleton(static sp => new TelemetryHistory(logger: sp.GetRequiredService<ILogger<TelemetryHistory>>()));
        services.AddSingleton(static sp => new TelemetryHistoryObserver(sp.GetRequiredService<IPrinterMonitor>(), sp.GetRequiredService<TelemetryHistory>()));
        // Diagnostics window (T-011): the printers of the current run, and printer/firmware information on demand.
        services.AddSingleton(static _ => new MonitoredPrinters());
        services.AddSingleton<IPrinterInfoSource>(static sp => new PrinterInfoSource(sp.GetRequiredService<PrusaLinkClientFactory>()));
        // Cameras (T-014): printers whose camera address lost its credentials at startup, for the settings window.
        services.AddSingleton(static _ => new RemovedCameraCredentials());
        services.AddSingleton(static sp => new MonitoringSession(
            sp.GetRequiredService<ISettingsStore>,
            sp.GetRequiredService<IPrinterMonitor>(),
            [
                // First: its language switch is posted to the UI thread before the menu and tooltip updates of the same settings.
                sp.GetRequiredService<UiLanguageObserver>(),
                sp.GetRequiredService<DebugLoggingObserver>(),
                sp.GetRequiredService<TrayStatusViewModel>(),
                sp.GetRequiredService<TrayMenuViewModel>(),
                sp.GetRequiredService<PrintNotificationObserver>(),
                sp.GetRequiredService<TelemetryHistoryObserver>(),
                sp.GetRequiredService<MonitoredPrinters>(),
                sp.GetRequiredService<ToastActivationRouter>(),
            ],
            sp.GetRequiredService<ILogger<MonitoringSession>>(),
            sp.GetRequiredService<RemovedCameraCredentials>(),
            sp.GetRequiredService<TimeProvider>()));

        // Settings window (T-015): saving restarts the session above with the new settings; passwords go to the secret store;
        // web proxies of printers whose login changed are stopped (T-028).
        services.AddSingleton<ISettingsApplier>(static sp => sp.GetRequiredService<MonitoringSession>());
        services.AddSingleton(static sp => PasswordPolicy.For(sp.GetRequiredService<ISecretStore>()));
        services.AddSingleton<ISettingsEditorStore>(static sp => new SettingsEditorStore(
            sp.GetRequiredService<ISettingsStore>,
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<PasswordPolicy>(),
            sp.GetRequiredService<ISettingsApplier>(),
            sp.GetRequiredService<IPrinterWebGateway>(),
            sp.GetRequiredService<ILogger<SettingsEditorStore>>(),
            removedCameraCredentials: sp.GetRequiredService<RemovedCameraCredentials>(),
            imageStore: sp.GetRequiredService<IPrinterImageStore>,
            customImages: sp.GetRequiredService<CustomPrinterImages>(),
            monitorPaths: sp.GetRequiredService<ICameraMonitorPathUpdater>()));
        services.AddSingleton<IConnectionTester>(static sp => new PrusaLinkConnectionTester(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<PrusaLinkClient>>()));
        // Start after logon (T-017), switched in the settings window: Windows only; the test mode's shortcut lives in its data
        // folder. Creating it touches nothing.
        services.AddSingleton(sp => CreateAutostart(env, windows, sp.GetRequiredService<ILogger<StartupShortcutAutostart>>()));
        // Updates of the installed app from GitHub Releases (T-017; offered to the user by T-058). Pre-releases as the setting
        // says, until the user chose by the running version (user decision 2026-10-24).
        services.AddSingleton(sp =>
        {
            string version = AboutViewModel.ProductVersion(typeof(AppServices).Assembly);
            MonitoringSession session = sp.GetRequiredService<MonitoringSession>();
            return new AppUpdates(
                CreateUpdateEngine(env, windows),
                () => PrereleaseUpdates.Offered(session.CurrentSettings?.OfferPrereleaseUpdates, version),
                sp.GetRequiredService<ILogger<AppUpdates>>());
        });

        AddCameras(services, cameraFeedFactory ?? CreateCameraFeedFactory, cameraWindowStateStore ?? CreateCameraWindowStateStore);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>
    /// Windows: toasts (T-010). Other OS: notifications are silently not sent until macOS notifications (T-020) exist.
    /// Creating the toast service starts nothing; <see cref="TrayHost"/> starts it, tests never do. The test mode shows no
    /// toasts, or (Debug switch, T-067) those of the test app with its own Start menu shortcut, never the production ones.
    /// </summary>
    private static INotificationService CreateNotificationService(IServiceProvider services, bool windows, AppEnvironment environment)
    {
        if (!windows || !OperatingSystem.IsWindows())
        {
            return NullNotificationService.Instance;
        }

        string appUserModelId = WindowsAppIdentity.AppUserModelId;
        Func<string?> shortcutPath = AppShortcutRegistration.DefaultShortcutPath;
        // The COM toast activator (T-068): Windows starts the plain exe for a click while the app does not run.
        List<string> activatorArguments = [];
        if (environment.IsIsolated)
        {
#if DEBUG
            if (!environment.TestToasts)
            {
                return NullNotificationService.Instance;
            }

            appUserModelId = AppEnvironment.TestAppUserModelId;
            shortcutPath = TestToastCleanup.DefaultShortcutPath;
            // A start by Windows for a test toast comes up as this test app again, never as the production app.
            activatorArguments = ToastActivatorCommandLine.TestServerArguments(Environment.GetCommandLineArgs()[1..], name => TestSwitch(environment, name));
#else
            return NullNotificationService.Instance;
#endif
        }

        ToastActivationRouter router = services.GetRequiredService<ToastActivationRouter>();
        WindowsToastPlatform platform = CreateWindowsToastPlatform(
            appUserModelId,
            shortcutPath,
            Environment.ProcessPath,
            environment.ToastActivatorClsid,
            activatorArguments,
            services.GetRequiredService<ILoggerFactory>());
        return new ToastNotificationService(
            platform,
            argument => _ = router.ActivateAsync(argument),
            UiLanguage.CurrentTextCulture,
            services.GetRequiredService<ILogger<ToastNotificationService>>());
    }

    /// <summary>
    /// The toast registrations of Windows (T-010, T-068): the COM toast activator (when <paramref name="activatorClsid"/> is
    /// set and the exe is known) and the Start menu shortcut. The same for a start and for the install hook of Velopack (T-017).
    /// </summary>
    /// <param name="activatorArguments">Further arguments of the activator's command line (the test switches of the test app).</param>
    [SupportedOSPlatform("windows")]
    private static WindowsToastPlatform CreateWindowsToastPlatform(
        string appUserModelId,
        Func<string?> shortcutPath,
        string? executable,
        Guid? activatorClsid,
        IReadOnlyList<string> activatorArguments,
        ILoggerFactory loggers)
    {
        ToastActivatorSettings? activator = activatorClsid is { } clsid && !string.IsNullOrEmpty(executable)
            ? new ToastActivatorSettings(clsid, ToastActivatorCommandLine.For(executable, [.. activatorArguments]))
            : null;
        return new WindowsToastPlatform(
            new AppShortcutRegistration(new WindowsShortcutStore(), loggers.CreateLogger<AppShortcutRegistration>()),
            shortcutPath,
            executable,
            loggers.CreateLogger<WindowsToastPlatform>(),
            appUserModelId,
            new ToastActivatorRegistration(new WindowsComServerRegistry(), loggers.CreateLogger<ToastActivatorRegistration>()),
            activator);
    }

    /// <summary>
    /// The Velopack installer hooks (T-017) with the production registrations of Windows: the hooks run only in an installed
    /// app, which is a Release build without the test mode.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static InstallerHooks CreateInstallerHooks(ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        AppEnvironment production = AppEnvironment.Production;
        string? executable = Environment.ProcessPath;
        return new InstallerHooks(
            CreateWindowsToastPlatform(production.AppUserModelId, AppShortcutRegistration.DefaultShortcutPath, executable, production.ToastActivatorClsid, [], loggers),
            new WinRtToastHistory(),
            new WindowsComServerRegistry(),
            new WindowsNotificationSettingsRegistry(),
            // Only the entries of the installed exe (T-061); Explorer's other entries are never touched.
            new TrayIconPromotion(new NotifyIconSettingsRegistry(), loggers.CreateLogger<TrayIconPromotion>()),
            CreateAutostart(production, windows: true, loggers.CreateLogger<StartupShortcutAutostart>()).Autostart,
            executable,
            loggers.CreateLogger<InstallerHooks>());
    }

    /// <summary>
    /// Start after logon (T-017): the app's shortcut in the Startup folder of the environment, to the running exe with the
    /// identity of the Start menu shortcut, only for the app installed by Velopack (user decision 2026-10-24: a shortcut
    /// into a <c>.zip</c> or a development build would never be removed). Not installed: no autostart, the settings show
    /// the option disabled with the reason. Nothing on other OS (macOS: T-019) and nothing for a process that is not the
    /// app's exe (a test host), so that nothing but the app can ever create the shortcut.
    /// </summary>
    internal static AutostartOption CreateAutostart(AppEnvironment environment, bool windows, ILogger<StartupShortcutAutostart> logger) =>
        CreateAutostart(environment, windows, logger, Environment.ProcessPath, File.Exists);

    /// <param name="executable">The running exe.</param>
    /// <param name="fileExists">Whether a file exists (for <see cref="VelopackInstallation.IsInstalled"/>).</param>
    internal static AutostartOption CreateAutostart(AppEnvironment environment, bool windows, ILogger<StartupShortcutAutostart> logger, string? executable, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(fileExists);
        if (!windows || !OperatingSystem.IsWindows() || string.IsNullOrEmpty(executable) || !TrayIconPromotion.AppliesTo(executable))
        {
            return new AutostartOption(null, NeedsInstallation: false);
        }

        if (!VelopackInstallation.IsInstalled(executable, fileExists))
        {
            return new AutostartOption(null, NeedsInstallation: true);
        }

        return new AutostartOption(
            new StartupShortcutAutostart(
                new WindowsShortcutStore(),
                environment.AutostartShortcutPath(),
                new AppShortcut(executable, environment.AppUserModelId, environment.ToastActivatorClsid),
                logger),
            NeedsInstallation: false);
    }

    /// <summary>Velopack's updates (T-017), only in a Velopack installation (never in the test mode, never for the <c>.zip</c>).</summary>
    private static VelopackUpdateEngine? CreateUpdateEngine(AppEnvironment environment, bool windows) =>
        windows && OperatingSystem.IsWindows() && !environment.IsIsolated && VelopackInstallation.IsInstalled(Environment.ProcessPath, File.Exists)
            ? new VelopackUpdateEngine()
            : null;

    /// <summary>
    /// The video decoder of the camera windows and of the camera test (T-014): FFmpeg. This is the only place that chooses
    /// it. Creating it loads nothing; missing native libraries surface as a camera error, the rest of the app works.
    /// </summary>
    private static readonly Func<IServiceProvider, ICameraFeedFactory> CreateCameraFeedFactory =
        static _ => new FFmpegCameraFeedFactory();

    /// <summary>Camera windows and the camera test of the settings window (T-014), on top of <paramref name="cameraFeedFactory"/>.</summary>
    private static void AddCameras(
        ServiceCollection services,
        Func<IServiceProvider, ICameraFeedFactory> cameraFeedFactory,
        Func<IServiceProvider, ICameraWindowStateStore> cameraWindowStateStore)
    {
        services.AddSingleton(cameraFeedFactory);
        // User decision (T-014): 15 s to connect, 10 s without a frame, 5 s between attempts, no backoff.
        services.AddSingleton(static _ => new CameraFeedOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            FrameTimeout = TimeSpan.FromSeconds(10),
            ReconnectDelay = TimeSpan.FromSeconds(5),
        });
        // Stateless: every camera window enumerates its own connection.
        services.AddSingleton(static sp => new ReconnectingCameraFeed(
            sp.GetRequiredService<ICameraFeedFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ReconnectingCameraFeed>>(),
            sp.GetRequiredService<CameraFeedOptions>()));
        services.AddSingleton<ICameraTester>(static sp => new CameraSnapshotTester(
            sp.GetRequiredService<ICameraFeedFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<CameraSnapshotTester>>()));
        // Created on first use; reading and writing the file happen later (TrayHost loads it in the background).
        services.AddSingleton(cameraWindowStateStore);
        // A saved camera monitor found on another connector keeps its new device path (user decision 2026-10-23).
        services.AddSingleton<ICameraMonitorPathUpdater>(static sp => new CameraMonitorPathUpdater(
            sp.GetRequiredService<ISettingsStore>,
            sp.GetRequiredService<ILogger<CameraMonitorPathUpdater>>()));
    }

    private static ICameraWindowStateStore CreateCameraWindowStateStore(IServiceProvider services)
    {
        try
        {
            return new JsonCameraWindowStateStore(
                services.GetRequiredService<AppEnvironment>().DataDirectory(),
                services.GetRequiredService<ILogger<JsonCameraWindowStateStore>>(),
                services.GetRequiredService<TimeProvider>());
        }
        catch (InvalidOperationException)
        {
            // No per-user data folder: camera windows are remembered for this run only.
            return new MemoryCameraWindowStateStore();
        }
    }

    /// <summary>
    /// A new view model for the camera window of <paramref name="printerId"/>: from <paramref name="saved"/> when given
    /// ("Apply" in the settings, T-040: monitoring gets the saved settings only after it restarted), otherwise from the
    /// monitored settings; <see langword="null"/> when the printer is not monitored or has no enabled camera. The caller
    /// disposes it when the window closes. Call on the UI thread.
    /// </summary>
    public static CameraViewModel? CreateCameraViewModel(IServiceProvider services, string printerId, PrinterDefinition? saved = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        PrinterDefinition? printer = saved ?? Monitored(services.GetRequiredService<MonitoredPrinters>(), printerId);
        return printer is not null
            && string.Equals(printer.Id, printerId, StringComparison.OrdinalIgnoreCase)
            && CameraTarget.From(printer) is { } target
            ? new CameraViewModel(
                target,
                services.GetRequiredService<ReconnectingCameraFeed>(),
                services.GetRequiredService<IPrinterMonitor>(),
                services.GetRequiredService<MonitoredPrinters>(),
                services.GetRequiredService<IUiDispatcher>(),
                services.GetRequiredService<ICameraWindowStateStore>(),
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<CameraFeedOptions>(),
                services.GetRequiredService<ILogger<CameraViewModel>>(),
                services.GetRequiredService<ICameraMonitorPathUpdater>())
            : null;

        static PrinterDefinition? Monitored(MonitoredPrinters monitored, string printerId)
        {
            foreach (PrinterDefinition printer in monitored.Current.Printers ?? [])
            {
                if (string.Equals(printer.Id, printerId, StringComparison.OrdinalIgnoreCase))
                {
                    return printer;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// A new view model for one diagnostics window; the caller disposes it when the window closes (it subscribes to the
    /// monitor). Call on the UI thread.
    /// </summary>
    /// <param name="initialPrinterId">Printer to show first (opened from its submenu); <see langword="null"/> = the first printer.</param>
    public static DiagnosticsViewModel CreateDiagnosticsViewModel(IServiceProvider services, string? initialPrinterId = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new DiagnosticsViewModel(
            services.GetRequiredService<IPrinterMonitor>(),
            services.GetRequiredService<MonitoredPrinters>(),
            services.GetRequiredService<TelemetryHistory>(),
            services.GetRequiredService<IUiDispatcher>(),
            services.GetRequiredService<IPrinterInfoSource>(),
            services.GetRequiredService<TimeProvider>(),
            initialPrinterId,
            services.GetRequiredService<CustomPrinterImages>());
    }

    /// <summary>
    /// A new view model for one settings window; the caller disposes it when the window closes and calls
    /// <see cref="SettingsViewModel.LoadAsync"/> after showing it (creating it touches no files). Call on the UI thread.
    /// </summary>
    /// <param name="screens">The monitors offered for the camera window (T-035); <see langword="null"/>: none.</param>
    /// <param name="cameraWindowPlacer">The open camera windows, placed again by "Apply" (T-040); <see langword="null"/>: none.</param>
    internal static SettingsViewModel CreateSettingsViewModel(IServiceProvider services, IScreenCatalog? screens = null, ICameraWindowPlacer? cameraWindowPlacer = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        AppEnvironment environment = services.GetRequiredService<AppEnvironment>();
        return new SettingsViewModel(
            services.GetRequiredService<ISettingsEditorStore>(),
            services.GetRequiredService<IConnectionTester>(),
            services.GetRequiredService<PasswordPolicy>(),
            cameraTester: services.GetRequiredService<ICameraTester>(),
            logger: services.GetRequiredService<ILogger<SettingsViewModel>>(),
            screens: screens,
            cameraWindowStates: services.GetRequiredService<ICameraWindowStateStore>(),
            cameraWindowPlacer: cameraWindowPlacer,
            timeProvider: services.GetRequiredService<TimeProvider>(),
            autostart: services.GetRequiredService<AutostartOption>().Autostart,
            autostartNeedsInstallation: services.GetRequiredService<AutostartOption>().NeedsInstallation)
        {
            // The test mode (never in Release builds) opens the image dialog in its own folder, where the tests put their
            // images (T-067; typing a path into the dialog through UI Automation is not reliable, T-064).
            ImageStartDirectory = environment.IsIsolated ? environment.DataDirectory() : null,
        };
    }

    /// <summary>
    /// The support actions of the About window (T-048, T-049): the settings of the current run (before the first run the
    /// saved ones), the printer states and the log of the process.
    /// </summary>
    internal static SupportActions CreateSupportActions(IServiceProvider services, SupportLog log)
    {
        ArgumentNullException.ThrowIfNull(services);
        MonitoringSession session = services.GetRequiredService<MonitoringSession>();
        Func<ISettingsStore> store = services.GetRequiredService<ISettingsStore>;
        string version = AboutViewModel.ProductVersion(typeof(AppServices).Assembly);
        AppEnvironment environment = services.GetRequiredService<AppEnvironment>();
        return new SupportActions(
            log,
            async cancellationToken => session.CurrentSettings ?? (await store().LoadAsync(cancellationToken).ConfigureAwait(false)).Settings,
            services.GetRequiredService<IPrinterMonitor>(),
            services.GetRequiredService<IFolderLauncher>(),
            language => SupportEnvironment.Current(version, language, static () => FFmpegCameraFeedFactory.LibraryVersion),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<ILogger<SupportActions>>())
        {
            // The test mode (never in Release builds, T-044) leaves nothing in the user's folders or clipboard history (T-064).
            BundleStartDirectory = environment.IsIsolated ? environment.DataDirectory() : null,
            KeepCopiesOutOfClipboardHistory = environment.IsIsolated,
        };
    }

#if DEBUG
    /// <summary>
    /// Debug builds only (T-067): what quitting removes after a test app with toasts, the toasts of the test app, its
    /// Start menu shortcut and its toast activator (T-068); <see langword="null"/> without test toasts (never in production).
    /// </summary>
    internal static TestToastCleanup? CreateTestToastCleanup(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AppEnvironment environment = services.GetRequiredService<AppEnvironment>();
        return environment.IsIsolated && environment.TestToasts && OperatingSystem.IsWindows()
            ? new TestToastCleanup(
                new WinRtToastHistory(),
                new WindowsShortcutStore(),
                TestToastCleanup.DefaultShortcutPath(),
                new WindowsComServerRegistry(),
                services.GetRequiredService<ILogger<TestToastCleanup>>())
            : null;
    }

    /// <summary>
    /// Debug builds only (T-068): the value of a test switch of <paramref name="environment"/> for the command line of the
    /// test app's toast activator; the instance suffix comes from the process (it is not part of the environment).
    /// </summary>
    private static string? TestSwitch(AppEnvironment environment, string name) => name switch
    {
        AppEnvironment.DataDirectoryVariable => environment.DataDirectory(),
        AppEnvironment.TestToastsVariable => environment.TestToasts ? "1" : null,
        AppEnvironment.TestCredentialPrefixVariable => environment.TestCredentialPrefix,
        AppEnvironment.TestStartDelayVariable => environment.TestStartDelay > TimeSpan.Zero
            ? ((long)environment.TestStartDelay.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null,
        _ => Environment.GetEnvironmentVariable(name),
    };
#endif

    private static ISecretStore CreateSecretStore(AppEnvironment environment)
    {
#if DEBUG
        if (environment.IsIsolated)
        {
            // Test mode (T-044): never the user's Credential Manager; with the Debug switch of T-067 the Credential Manager
            // under a test prefix (checked by AppEnvironment), which can never be the production one. Session only (user
            // decision 2026-10-22): never written to disk nor roamed, so a crashed run leaves nothing past the logon session.
            return environment.TestCredentialPrefix is { } prefix && OperatingSystem.IsWindows()
                ? new WindowsCredentialSecretStore(prefix, CredentialManagerNative.CredPersistSession)
                : new TestModeSecretStore(environment.DataDirectory());
        }
#endif

        return OperatingSystem.IsWindows()
            ? new WindowsCredentialSecretStore()
            // macOS Keychain store follows in T-021; until then passwords are unavailable but the app runs.
            : new UnsupportedSecretStore();
    }
}
