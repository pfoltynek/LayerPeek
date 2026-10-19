using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.App.History;
using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Notifications;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Camera.FFmpeg;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.History;
using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Notifications;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.Core.Settings;
using PrusaTrayDiagnostic.Core.Web;
using PrusaTrayDiagnostic.PrusaLink;

namespace PrusaTrayDiagnostic.App.Composition;

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
    /// Where camera windows are remembered, for tests; <see langword="null"/>: <c>camera-windows.json</c> in the user data folder.
    /// </param>
    public static ServiceProvider Create(
        ILoggerFactory? loggerFactory = null,
        Func<IServiceProvider, ICameraFeedFactory>? cameraFeedFactory = null,
        Func<IServiceProvider, ICameraWindowStateStore>? cameraWindowStateStore = null) =>
        Create(loggerFactory, OperatingSystem.IsWindows(), cameraFeedFactory, cameraWindowStateStore);

    /// <param name="windows">Use the Windows notification implementation (only effective on Windows); lets tests check the other branch.</param>
    internal static ServiceProvider Create(
        ILoggerFactory? loggerFactory,
        bool windows,
        Func<IServiceProvider, ICameraFeedFactory>? cameraFeedFactory = null,
        Func<IServiceProvider, ICameraWindowStateStore>? cameraWindowStateStore = null)
    {
        ServiceCollection services = new();

        // No logging provider yet: messages are discarded until a diagnostics/log task adds one.
        services.AddLogging();
        if (loggerFactory is not null)
        {
            services.AddSingleton(loggerFactory);
        }

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISecretStore>(static _ => CreateSecretStore());
        // Created on first use (by the monitoring session on a background thread), so building the provider touches no files.
        services.AddSingleton<ISettingsStore>(static _ => JsonSettingsStore.CreateDefault());

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
        services.AddSingleton(static sp => new TrayStatusViewModel(sp.GetRequiredService<IPrinterMonitor>(), sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<IUrlLauncher>(static sp => new ProcessUrlLauncher(sp.GetRequiredService<ILogger<ProcessUrlLauncher>>()));
        // Settings (T-015) and diagnostics (T-011) windows both exist.
        services.AddSingleton(static _ => TrayMenuOptions.Default);
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
            sp.GetRequiredService<TrayMenuOptions>()));
        services.AddSingleton(static sp => new ToastActivationRouter(sp.GetRequiredService<IUrlLauncher>(), sp.GetRequiredService<ILogger<ToastActivationRouter>>()));
        services.AddSingleton(sp => CreateNotificationService(sp, windows));
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
                sp.GetRequiredService<TrayStatusViewModel>(),
                sp.GetRequiredService<TrayMenuViewModel>(),
                sp.GetRequiredService<PrintNotificationObserver>(),
                sp.GetRequiredService<TelemetryHistoryObserver>(),
                sp.GetRequiredService<MonitoredPrinters>(),
                sp.GetRequiredService<ToastActivationRouter>(),
            ],
            sp.GetRequiredService<ILogger<MonitoringSession>>(),
            sp.GetRequiredService<RemovedCameraCredentials>()));

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
            removedCameraCredentials: sp.GetRequiredService<RemovedCameraCredentials>()));
        services.AddSingleton<IConnectionTester>(static sp => new PrusaLinkConnectionTester(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<PrusaLinkClient>>()));

        AddCameras(services, cameraFeedFactory ?? CreateCameraFeedFactory, cameraWindowStateStore ?? CreateCameraWindowStateStore);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>
    /// Windows: toasts (T-010). Other OS: notifications are silently not sent until macOS notifications (T-020) exist.
    /// Creating the toast service starts nothing; <see cref="TrayHost"/> starts it, tests never do.
    /// </summary>
    private static INotificationService CreateNotificationService(IServiceProvider services, bool windows)
    {
        if (!windows || !OperatingSystem.IsWindows())
        {
            return NullNotificationService.Instance;
        }

        ToastActivationRouter router = services.GetRequiredService<ToastActivationRouter>();
        WindowsToastPlatform platform = new(
            new AppShortcutRegistration(new WindowsShortcutStore(), services.GetRequiredService<ILogger<AppShortcutRegistration>>()),
            AppShortcutRegistration.DefaultShortcutPath,
            Environment.ProcessPath,
            services.GetRequiredService<ILogger<WindowsToastPlatform>>());
        return new ToastNotificationService(
            platform,
            argument => _ = router.ActivateAsync(argument),
            UiLanguage.CurrentTextCulture,
            services.GetRequiredService<ILogger<ToastNotificationService>>());
    }

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
    }

    private static ICameraWindowStateStore CreateCameraWindowStateStore(IServiceProvider services)
    {
        try
        {
            return JsonCameraWindowStateStore.CreateDefault(services.GetRequiredService<ILogger<JsonCameraWindowStateStore>>());
        }
        catch (InvalidOperationException)
        {
            // No per-user data folder: camera windows are remembered for this run only.
            return new MemoryCameraWindowStateStore();
        }
    }

    /// <summary>
    /// A new view model for the camera window of <paramref name="printerId"/>; <see langword="null"/> when the printer is
    /// not monitored or has no enabled camera. The caller disposes it when the window closes. Call on the UI thread.
    /// </summary>
    public static CameraViewModel? CreateCameraViewModel(IServiceProvider services, string printerId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        if (services.GetRequiredService<MonitoredPrinters>().Current.Printers is not { } printers)
        {
            return null;
        }

        foreach (PrinterDefinition printer in printers)
        {
            if (string.Equals(printer.Id, printerId, StringComparison.OrdinalIgnoreCase))
            {
                return CameraTarget.From(printer) is { } target
                    ? new CameraViewModel(
                        target,
                        services.GetRequiredService<ReconnectingCameraFeed>(),
                        services.GetRequiredService<IPrinterMonitor>(),
                        services.GetRequiredService<MonitoredPrinters>(),
                        services.GetRequiredService<IUiDispatcher>(),
                        services.GetRequiredService<ICameraWindowStateStore>(),
                        services.GetRequiredService<TimeProvider>(),
                        services.GetRequiredService<CameraFeedOptions>(),
                        services.GetRequiredService<ILogger<CameraViewModel>>())
                    : null;
            }
        }

        return null;
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
            initialPrinterId);
    }

    /// <summary>
    /// A new view model for one settings window; the caller disposes it when the window closes and calls
    /// <see cref="SettingsViewModel.LoadAsync"/> after showing it (creating it touches no files). Call on the UI thread.
    /// </summary>
    public static SettingsViewModel CreateSettingsViewModel(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new SettingsViewModel(
            services.GetRequiredService<ISettingsEditorStore>(),
            services.GetRequiredService<IConnectionTester>(),
            services.GetRequiredService<PasswordPolicy>(),
            cameraTester: services.GetRequiredService<ICameraTester>());
    }

    private static ISecretStore CreateSecretStore() =>
        OperatingSystem.IsWindows()
            ? new WindowsCredentialSecretStore()
            // macOS Keychain store follows in T-021; until then passwords are unavailable but the app runs.
            : new UnsupportedSecretStore();
}
