using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using LayerPeek.App.Cameras;
using LayerPeek.App.Localization;
using LayerPeek.App.Logging;
using LayerPeek.App.Notifications;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Printers;
using LayerPeek.App.Settings;
using LayerPeek.App.Support;
using LayerPeek.App.Tray;
using LayerPeek.App.Windows;
using LayerPeek.Core.Images;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Web;

namespace LayerPeek.App.Composition;

/// <summary>
/// The part of the app that runs without any UI object (T-099): the service provider with the loaded settings, the
/// monitoring session, notifications and the background work of startup. Normally <see cref="TrayHost"/> creates it on the
/// UI thread and the tray attaches right away; on macOS without an active display <c>Program.Main</c> creates and starts
/// it before Avalonia exists (on the main thread, through <see cref="StartupUiDispatcher"/>), and the tray attaches to the
/// same instance once the display wakes. Created, started and stopped on the UI thread (the main thread before Avalonia).
/// </summary>
internal sealed partial class ServiceHost
{
    /// <summary>A save writes a few small items in milliseconds; quitting must still stay within the overall limit.</summary>
    private static readonly TimeSpan PendingSaveTimeout = TimeSpan.FromSeconds(1);

    /// <summary>How long startup waits at most for the remembered window sizes (T-094); after that they apply once read.</summary>
    private static readonly TimeSpan WindowStateLoadTimeout = TimeSpan.FromSeconds(1);

    /// <summary>How long quitting waits for requests in progress through the local web proxies (T-028).</summary>
    private static readonly TimeSpan WebProxyStopTimeout = TimeSpan.FromSeconds(1);

    private readonly ILogger<ServiceHost> _logger;
    private readonly AppEnvironment _environment;
    private readonly AppSettings _initialSettings;
    private readonly SettingsLoadStatus _loadStatus;
    private readonly IReadOnlyList<string> _storedPrinterIds;
    private readonly CancellationTokenSource _backgroundWork;
#if DEBUG
    private readonly TestToastCleanup? _testToastCleanup;
#endif
    private MonitoringSession? _session;
    private int _stopped;

    private ServiceHost(
        ILoggerFactory loggerFactory,
        AppLogging logging,
        AppEnvironment environment,
        ServiceProvider services,
        AppSettings initialSettings,
        SettingsLoadStatus loadStatus,
        IReadOnlyList<string> storedPrinterIds,
        CancellationTokenSource backgroundWork)
    {
        LoggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ServiceHost>();
        _environment = environment;
        Services = services;
        SupportLog = new SupportLog(logging.Directory, logging.Flush);
        _initialSettings = initialSettings;
        _loadStatus = loadStatus;
        _storedPrinterIds = storedPrinterIds;
        _backgroundWork = backgroundWork;
#if DEBUG
        _testToastCleanup = AppServices.CreateTestToastCleanup(services);
#endif
    }

    /// <summary>The services of the app. Valid until <see cref="StopAsync"/>.</summary>
    public ServiceProvider Services { get; }

    /// <summary>The logger factory of the process; it outlives the host.</summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>The log folder for the support actions (T-048).</summary>
    public SupportLog SupportLog { get; }

    /// <summary>Whether <see cref="StartMonitoring"/> ran.</summary>
    public bool IsMonitoring => _session is not null;

    /// <summary>Cancelled when stopping starts: background work of startup (printer images, tray icon promotion).</summary>
    public CancellationToken BackgroundWork => _backgroundWork.Token;

    /// <summary>
    /// Builds the services, reads the remembered window sizes and the settings (off the UI thread) and applies their language,
    /// so that the first texts (tray, notifications) are already in the chosen language. Starts no monitoring yet. Call on
    /// the UI thread (the main thread before Avalonia); the returned task continues there.
    /// </summary>
    /// <param name="environment">Data folder and isolation of this run (T-044).</param>
    /// <param name="logging">The logging of the process (T-024); owned by <c>Program.Main</c>, it outlives the host.</param>
    /// <param name="uiDispatcher">
    /// The UI thread's dispatcher of the services; <see langword="null"/>: Avalonia's (the normal start). Before Avalonia
    /// exists: a <see cref="StartupUiDispatcher"/>.
    /// </param>
    public static async Task<ServiceHost> CreateAsync(AppEnvironment environment, AppLogging logging, IUiDispatcher? uiDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logging);
        // Not owned by the container: it outlives the container dispose during quit.
        ILoggerFactory loggerFactory = logging.Factory;
        ServiceProvider services = AppServices.Create(loggerFactory, environment: environment, logLevel: logging.Level, uiDispatcher: uiDispatcher);
        CancellationTokenSource backgroundWork = new();
        try
        {
            // Remembered window sizes (T-094) are read alongside the settings: a tiny file, ready before a window can open.
            Task windowStatesLoaded = services.GetRequiredService<IAuxiliaryWindowStateStore>().LoadAsync(CancellationToken.None);
            ILogger<MonitoringSession> sessionLogger = services.GetRequiredService<ILogger<MonitoringSession>>();
            (AppSettings initialSettings, SettingsLoadStatus loadStatus, IReadOnlyList<string> storedPrinterIds) = await Task.Run(
                () => MonitoringSession.LoadSettingsWithStatusAsync(
                    services.GetRequiredService<ISettingsStore>,
                    sessionLogger,
                    CancellationToken.None,
                    services.GetRequiredService<RemovedCameraCredentials>()),
                CancellationToken.None).ConfigureAwait(true);

            // Never faults for file problems; a file held by another process does not delay the tray for long.
            await windowStatesLoaded.WaitAsync(WindowStateLoadTimeout)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

            // Before the tray view models (resolved by the tray or the monitoring session) format their first texts.
            services.GetRequiredService<UiLanguage>().Apply(initialSettings.Language);
            return new ServiceHost(loggerFactory, logging, environment, services, initialSettings, loadStatus, storedPrinterIds, backgroundWork);
        }
        catch
        {
            backgroundWork.Dispose();
            services.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Starts monitoring with the loaded settings, the toast registration (Windows) and the background work of startup.
    /// Once; later calls do nothing. Call on the UI thread (the main thread before Avalonia).
    /// </summary>
    public void StartMonitoring()
    {
        if (_session is not null || Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        MonitoringSession session = Services.GetRequiredService<MonitoringSession>();
        _session = session;
#if DEBUG
        // The test mode may delay monitoring (T-067): the tray already exists and shows "Starting…" meanwhile.
        session.Start(_initialSettings, _environment.TestStartDelay);
#else
        session.Start(_initialSettings);
#endif
        // Registers the app for toasts in the background now, not with the first notification (T-010).
        if (Services.GetRequiredService<INotificationService>() is ToastNotificationService toasts)
        {
            toasts.Start();
        }

        // Remembered camera windows are read in the background, long before a camera window opens; never throws.
        _ = Services.GetRequiredService<ICameraWindowStateStore>().LoadAsync(CancellationToken.None);

        // Custom printer images (T-039) in the background: orphans go only with really loaded settings; never throws.
        ServiceProvider services = Services;
        AppSettings settings = _initialSettings;
        SettingsLoadStatus status = _loadStatus;
        IReadOnlyList<string> storedPrinterIds = _storedPrinterIds;
        CancellationToken backgroundWork = _backgroundWork.Token;
        _ = Task.Run(
            () => PrinterImageStartup.RunAsync(
                services.GetRequiredService<IPrinterImageStore>,
                services.GetRequiredService<CustomPrinterImages>(),
                settings,
                status,
                storedPrinterIds,
                services.GetRequiredService<ILogger<CustomPrinterImages>>(),
                backgroundWork),
            CancellationToken.None);
    }

    /// <summary>Requests the monitoring to stop without waiting (e.g. the OS is ending the session).</summary>
    public void RequestStop() => _session?.RequestStop();

    /// <summary>
    /// A click on a notification (T-068, T-020): handled like a click on the banner (<see cref="ToastActivationRouter"/>:
    /// printer id only, its web through the proxy), also before the tray exists (T-099). Never throws; ignored once stopping.
    /// </summary>
    public void ActivateToast(string launchArgument)
    {
        if (Volatile.Read(ref _stopped) == 0)
        {
            _ = Services.GetRequiredService<ToastActivationRouter>().ActivateAsync(launchArgument);
        }
    }

    /// <summary>
    /// The UI runs (T-101): a click on the test notification brings the settings window to the front through
    /// <paramref name="open"/> (any thread; it posts to the UI thread itself); one that came earlier is handled now. Once.
    /// </summary>
    public void AttachSettingsOpener(Action open) => Services.GetRequiredService<ToastActivationRouter>().AttachSettingsOpener(open);

    /// <summary>
    /// Stops monitoring (waiting at most <paramref name="timeout"/>) and releases the services. The tray (if any) released
    /// its UI objects before. Call on the UI thread (the main thread before Avalonia). Safe to repeat.
    /// </summary>
    public async Task StopAsync(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _backgroundWork.Cancel();

        await WaitForPendingSaveAsync().ConfigureAwait(true);
        if (_session is { } session)
        {
            // A detected model being written (T-039): a tiny atomic write; an interrupted one keeps the previous file.
            await session.ModelSaves.WaitAsync(PendingSaveTimeout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        // A camera monitor path being written (user decision 2026-10-23): the same kind of tiny atomic write, before the
        // settings store goes with the container. Never throws.
        await Services.GetRequiredService<ICameraMonitorPathUpdater>().WaitForPendingAsync(PendingSaveTimeout).ConfigureAwait(true);
        await WaitForCameraWindowStateAsync().ConfigureAwait(true);
        await WaitForWindowStateAsync().ConfigureAwait(true);
        // In parallel: open browser tabs of the local web proxies (T-028) stop working, and their ports close.
        await Task.WhenAll(_session?.StopAsync(timeout) ?? Task.CompletedTask, StopWebProxiesAsync()).ConfigureAwait(true);
#if DEBUG
        // Read before the container goes: the toast thread may still be registering the test app (writing its shortcut).
        Task toastThreadEnded = _testToastCleanup is not null && Services.GetRequiredService<INotificationService>() is ToastNotificationService toastService
            ? toastService.Completion
            : Task.CompletedTask;
        // macOS (T-020): the notifications of the test bundle go when the test app quits; never those of the production app.
        UserNotificationService? testNotifications = _environment is { IsIsolated: true, TestToasts: true }
            && Services.GetRequiredService<INotificationService>() is UserNotificationService userNotifications
            ? userNotifications
            : null;
#endif
        try
        {
            // The session was stopped above, so disposing it (via the container) does not wait again.
            await Services.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Recorded here, while the (caller-owned) logger factory is certainly alive; quitting goes on.
            LogDisposeFailed(ex);
        }

#if DEBUG
        await RunTestToastCleanupAsync(_testToastCleanup, toastThreadEnded, _logger).ConfigureAwait(true);
        await RunTestNotificationCleanupAsync(testNotifications, _logger).ConfigureAwait(true);
#endif

        _backgroundWork.Dispose();
    }

    /// <summary>A start that failed after <see cref="CreateAsync"/> (the tray could not be created): releases without waiting.</summary>
    internal void DisposeAfterFailedStart()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _backgroundWork.Cancel();
        _backgroundWork.Dispose();
        Services.Dispose();
    }

#if DEBUG
    /// <summary>
    /// Debug test mode with toasts (T-067): once the toast thread has ended (released, so it can no longer write the
    /// shortcut while registering), the toasts of the test app leave the notification center and its Start menu shortcut
    /// is deleted. Off the UI thread (WinRT, multithreaded apartment), each step within its quit limit; never throws (the
    /// cleanup logs its own failures). A cleanup that cannot finish here is left to <c>--layerpeek-e2e-clear-toasts</c>.
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

    /// <summary>
    /// Debug test mode with notifications on macOS (T-020): once the notification task has ended (disposed with the
    /// container, so it hands nothing more to macOS), the delivered and pending notifications of the test bundle are removed
    /// and checked gone, within the quit limit. Never throws; a cleanup that cannot finish here is left to
    /// <c>--layerpeek-e2e-clear-toasts</c> of the test bundle.
    /// </summary>
    internal static async Task RunTestNotificationCleanupAsync(UserNotificationService? notifications, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (notifications is null)
        {
            return;
        }

        try
        {
            await notifications.Completion.WaitAsync(TestToastThreadTimeout).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            LogTestToastThreadStillRunning(logger, ex);
        }

        try
        {
            Platform.MacOS.TestNotificationCleanup cleanup = new(notifications.Center, logger);
            _ = await Task.Run(() => cleanup.RunAsync(TestNotificationCleanupTimeout), CancellationToken.None).ConfigureAwait(true);
        }
        catch (ArgumentException ex)
        {
            // Not the test bundle: nothing is removed.
            LogTestToastCleanupFailed(logger, ex);
        }
    }

    private static readonly TimeSpan TestNotificationCleanupTimeout = TimeSpan.FromSeconds(2);

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
            if (!await Services.GetRequiredService<ISettingsEditorStore>().WaitForPendingSaveAsync(PendingSaveTimeout).ConfigureAwait(true))
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
            if (!await Services.GetRequiredService<ICameraWindowStateStore>().WaitForPendingSaveAsync(PendingSaveTimeout).ConfigureAwait(true))
            {
                LogCameraWindowStateTimedOut(PendingSaveTimeout);
            }
        }
        catch (Exception ex)
        {
            LogCameraWindowStateFailed(ex);
        }
    }

    /// <summary>The settings and diagnostics windows closed by quitting remember their size (T-094); a tiny file as well.</summary>
    private async Task WaitForWindowStateAsync()
    {
        try
        {
            if (!await Services.GetRequiredService<IAuxiliaryWindowStateStore>().WaitForPendingSaveAsync(PendingSaveTimeout).ConfigureAwait(true))
            {
                LogWindowStateTimedOut(PendingSaveTimeout);
            }
        }
        catch (Exception ex)
        {
            LogWindowStateFailed(ex);
        }
    }

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
            await Services.GetRequiredService<IPrinterWebGateway>().StopAllAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWebProxyStopFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving the window state did not finish within {Timeout} while quitting.")]
    private partial void LogWindowStateTimedOut(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "Waiting for the window state to be saved failed while quitting.")]
    private partial void LogWindowStateFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving the camera window state did not finish within {Timeout} while quitting.")]
    private partial void LogCameraWindowStateTimedOut(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "Waiting for the camera window state to be saved failed while quitting.")]
    private partial void LogCameraWindowStateFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A settings save did not finish within {Timeout} while quitting.")]
    private partial void LogPendingSaveTimedOut(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "Waiting for a settings save failed while quitting.")]
    private partial void LogPendingSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping the web proxies failed while quitting.")]
    private partial void LogWebProxyStopFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Releasing services failed while stopping.")]
    private partial void LogDisposeFailed(Exception exception);
}
