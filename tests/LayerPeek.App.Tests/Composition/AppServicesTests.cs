using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Previews;
using LayerPeek.App.Composition;
using LayerPeek.App.Cooldown;
using LayerPeek.App.Diagnostics;
using LayerPeek.App.History;
using LayerPeek.App.Localization;
using LayerPeek.App.Logging;
using LayerPeek.App.Notifications;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.MacOS;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Settings;
using LayerPeek.App.Support;
using LayerPeek.App.Tests.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Menu;
using LayerPeek.Camera.FFmpeg;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.History;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Secrets;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Web;
using LayerPeek.PrusaLink.WebProxy;

namespace LayerPeek.App.Tests.Composition;

public sealed class AppServicesTests
{
    [Fact]
    public async Task ResolvesTrayServicesWithoutTouchingUserData()
    {
        // Resolving must not create the settings store (it is created lazily by the session), so this test leaves nothing behind.
        await using ServiceProvider services = AppServices.Create();

        Assert.IsType<PrinterMonitor>(services.GetRequiredService<IPrinterMonitor>());
        Assert.IsType<PrinterClientFactory>(services.GetRequiredService<IPrinterClientFactory>());
        Assert.Same(services.GetRequiredService<TrayStatusViewModel>(), services.GetRequiredService<TrayStatusViewModel>());
        Assert.Same(services.GetRequiredService<TrayMenuViewModel>(), services.GetRequiredService<TrayMenuViewModel>());
        Assert.IsType<ProcessUrlLauncher>(services.GetRequiredService<IUrlLauncher>());
        Assert.NotNull(services.GetRequiredService<MonitoringSession>());
    }

    /// <summary>
    /// T-017: only the app's own exe may create the autostart shortcut, and updates exist only in a Velopack installation;
    /// the test host is neither, so nothing here can touch the Startup folder or GitHub.
    /// </summary>
    [Fact]
    public async Task OutsideTheInstalledAppThereIsNoAutostartAndNoUpdate()
    {
        await using ServiceProvider services = AppServices.Create();

        Assert.Null(services.GetRequiredService<LayerPeek.App.Platform.AutostartOption>().Autostart);
        LayerPeek.App.Installer.AppUpdates updates = services.GetRequiredService<LayerPeek.App.Installer.AppUpdates>();
        Assert.False(updates.IsAvailable);
        Assert.Null(await updates.CheckAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>User decision 2026-10-24: start after logon only for the app installed by the installer.</summary>
    [Fact]
    public void AutostartExistsOnlyForTheInstalledApp()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows rules of the autostart (macOS: MacAutostartCompositionTests, T-019).");
        const string installed = @"C:\Users\u\AppData\Local\LayerPeek\current\LayerPeek.exe";
        HashSet<string> files = new(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Users\u\AppData\Local\LayerPeek\Update.exe",
            @"C:\Users\u\AppData\Local\LayerPeek\current\sq.version",
        };
        Microsoft.Extensions.Logging.Abstractions.NullLogger<StartupShortcutAutostart> logger = new();

        AutostartOption inInstallation = AppServices.CreateAutostart(AppEnvironment.Production, windows: true, logger, installed, files.Contains);
        AutostartOption zip = AppServices.CreateAutostart(AppEnvironment.Production, windows: true, logger, @"C:\Tools\LayerPeek\LayerPeek.exe", files.Contains);
        AutostartOption host = AppServices.CreateAutostart(AppEnvironment.Production, windows: true, logger, @"C:\dotnet\testhost.exe", files.Contains);
        AutostartOption otherOs = AppServices.CreateAutostart(AppEnvironment.Production, windows: false, logger, installed, files.Contains);

        StartupShortcutAutostart autostart = Assert.IsType<StartupShortcutAutostart>(inInstallation.Autostart);
        Assert.Equal(installed, autostart.Desired.TargetPath);
        Assert.False(inInstallation.NeedsInstallation);
        Assert.Equal(new AutostartOption(null, NeedsInstallation: true), zip);
        Assert.Equal(new AutostartOption(null, NeedsInstallation: false), host);
        Assert.Equal(new AutostartOption(null, NeedsInstallation: false), otherOs);
    }

    [Fact]
    public async Task SuppliedLoggerFactoryIsUsedAndOutlivesContainer()
    {
        TrackingLoggerFactory loggerFactory = new();
        ServiceProvider services = AppServices.Create(loggerFactory);

        Assert.Same(loggerFactory, services.GetRequiredService<ILoggerFactory>());
        _ = services.GetRequiredService<ILogger<MonitoringSession>>();
        await services.DisposeAsync();

        // Quit logs after the container was disposed (T-024), so the factory must still be alive.
        Assert.False(loggerFactory.Disposed);
        Assert.Contains(typeof(MonitoringSession).FullName!, loggerFactory.Categories);
    }

    [Fact]
    public async Task LanguageIsASingletonStartingWithTheSystemLanguage()
    {
        await using ServiceProvider services = AppServices.Create();

        UiLanguage language = services.GetRequiredService<UiLanguage>();
        Assert.Same(language, services.GetRequiredService<UiLanguage>());
        Assert.Null(language.Code);
        Assert.Same(services.GetRequiredService<UiLanguageObserver>(), services.GetRequiredService<UiLanguageObserver>());
    }

    [Fact]
    public async Task SavedDebugLoggingSwitchesTheLevelOfTheProcessLog()
    {
        // T-047: TrayHost passes the level of the process log; the observer in the container switches exactly that one.
        LogLevelSwitch level = new(LogLevel.Information);
        await using ServiceProvider services = AppServices.Create(logLevel: level);

        DebugLoggingObserver observer = services.GetRequiredService<DebugLoggingObserver>();
        observer.ApplySettings(new AppSettings { DebugLogging = true });

        Assert.Equal(LogLevel.Debug, level.MinimumLevel);
        // Saved settings reach it through the monitoring session, like every other observer.
        Assert.Contains(observer, services.GetRequiredService<MonitoringSession>().Observers);
    }

    [Fact]
    public async Task SupportActionsComeFromTheContainerWithoutTouchingUserData()
    {
        // T-048: created per About window; nothing is read before an action runs.
        await using ServiceProvider services = AppServices.Create();

        SupportActions support = AppServices.CreateSupportActions(services, new SupportLog(null, static _ => true));

        Assert.Null(support.LogDirectory);
        Assert.False(await support.OpenLogFolderAsync());
        Assert.IsType<ProcessFolderLauncher>(services.GetRequiredService<IFolderLauncher>());
    }

    [Fact]
    public async Task WindowsUsesToastNotifications()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Toast notifications exist only on Windows.");
        await using ServiceProvider services = AppServices.Create();

        // Resolving creates no thread, no shortcut and no toast: the service starts on TrayHost.Start / first Show.
        ToastNotificationService toasts = Assert.IsType<ToastNotificationService>(services.GetRequiredService<INotificationService>());
        Assert.Same(toasts, services.GetRequiredService<INotificationService>());
        AssertNotificationPipeline(services);
    }

    [Fact]
    public async Task OtherOperatingSystemsSilentlyDropNotifications()
    {
        // Without a notification center (another OS; on macOS the test host, which has no bundle, so Program.Main of the app
        // would open none, T-020) notifications are silently not sent and the settings window gets no OS switch.
        await using ServiceProvider services = AppServices.Create(loggerFactory: null, windows: false);

        Assert.Same(NullNotificationService.Instance, services.GetRequiredService<INotificationService>());
        Assert.Null(services.GetRequiredService<SystemNotificationSettingsOption>().Settings);
        AssertNotificationPipeline(services);
    }

    [Fact]
    public async Task MacOSUsesTheNotificationCenterOfThisRun()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The notification center of macOS (T-020).");
        FakeUserNotificationCenter center = new(AppEnvironment.ProductionBundleIdentifier);
        await using ServiceProvider services = AppServices.Create(loggerFactory: null, windows: false, macNotifications: center);

        // Resolving asks nothing: the permission is asked with the first notification.
        UserNotificationService notifications = Assert.IsType<UserNotificationService>(services.GetRequiredService<INotificationService>());
        Assert.Same(center, notifications.Center);
        Assert.IsType<MacNotificationSettings>(services.GetRequiredService<SystemNotificationSettingsOption>().Settings);
        using SettingsViewModel settings = AppServices.CreateSettingsViewModel(services);
        Assert.False(settings.ShowsNotificationsTurnedOff);
        Assert.Equal(0, center.AuthorizationRequests);
        Assert.Empty(center.Posted);
        AssertNotificationPipeline(services);
    }

    [Fact]
    public async Task WindowsNeverUsesTheNotificationCenterOfMacOS()
    {
        FakeUserNotificationCenter center = new(AppEnvironment.ProductionBundleIdentifier);
        await using ServiceProvider services = AppServices.Create(loggerFactory: null, windows: true, macNotifications: center);

        Assert.IsNotType<UserNotificationService>(services.GetRequiredService<INotificationService>());
        Assert.IsNotType<MacNotificationSettings>(services.GetRequiredService<SystemNotificationSettingsOption>().Settings);
    }

    /// <summary>T-101: the settings window of Windows reads and tests through the toasts of this run, without starting them.</summary>
    [Fact]
    public async Task WindowsSettingsUseTheToastsOfThisRun()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Toast notifications exist only on Windows.");
        await using ServiceProvider services = AppServices.Create();

        Assert.IsType<ToastNotificationSettings>(services.GetRequiredService<SystemNotificationSettingsOption>().Settings);
        ToastNotificationService toasts = Assert.IsType<ToastNotificationService>(services.GetRequiredService<INotificationService>());
        Assert.False(toasts.Completion.IsCompleted);
    }

#if DEBUG
    [Fact]
    public void OnlyProductionOpensSystemSettings()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "System Settings of macOS (T-020).");
        string folder = Path.Combine(Path.GetTempPath(), "LayerPeek.App.Tests.NoSystemSettings");

        Action<Uri> production = AppServices.OpenSystemSettings(AppEnvironment.Production, NullLogger.Instance);
        Action<Uri> testMode = AppServices.OpenSystemSettings(AppEnvironment.ForTests(folder, toasts: true), NullLogger.Instance);

        Assert.Equal(nameof(MacSystemSettings.Open), production.Method.Name);
        Assert.NotEqual(nameof(MacSystemSettings.Open), testMode.Method.Name);
        // The stand-in of the test mode only logs: calling it opens nothing.
        testMode(MacNotificationSettings.SettingsAddress(AppEnvironment.TestBundleIdentifier));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task TestModeOnlyLogsOpeningTheLocalNetworkSettings()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOSVersionAtLeast(15), "Local Network permission of macOS 15+ (T-097).");
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            RecordingLoggerFactory logs = new();
            await using ServiceProvider services = AppServices.Create(logs, windows: false, environment: AppEnvironment.ForTests(folder));

            ILocalNetworkPrivacy privacy = services.GetRequiredService<ILocalNetworkPrivacy>();
            Assert.IsType<MacLocalNetworkPrivacy>(privacy);
            // The stand-in of the test mode: System Settings never opens in tests (T-020 seam).
            privacy.OpenSettings();

            Assert.Contains(logs.Messages, static m => m == "Test mode: System Settings is not opened (x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_LocalNetwork).");
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }
#endif

    [Fact]
    public async Task WithoutTheLocalNetworkPermissionThereIsNone()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOSVersionAtLeast(15), "macOS 15+ has the Local Network permission (T-097).");
        await using ServiceProvider services = AppServices.Create(loggerFactory: null, windows: OperatingSystem.IsWindows());

        Assert.Same(NoLocalNetworkPrivacy.Instance, services.GetRequiredService<ILocalNetworkPrivacy>());
    }

    private static void AssertNotificationPipeline(ServiceProvider services)
    {
        Assert.Same(services.GetRequiredService<PrintEventNotifier>(), services.GetRequiredService<PrintEventNotifier>());
        Assert.NotNull(services.GetRequiredService<PrintNotificationObserver>());
        Assert.Same(services.GetRequiredService<ToastActivationRouter>(), services.GetRequiredService<ToastActivationRouter>());
        // The router receives the printers of every run, so a click opens the current address.
        Assert.Contains(services.GetRequiredService<ToastActivationRouter>(), services.GetRequiredService<MonitoringSession>().Observers);
    }

    [Fact]
    public async Task CooldownMonitorIsRegisteredAsSingletonAndGetsTheSettingsOfEveryRun()
    {
        await using ServiceProvider services = AppServices.Create();

        Assert.Same(services.GetRequiredService<CooldownMonitor>(), services.GetRequiredService<CooldownMonitor>());
        Assert.Contains(services.GetRequiredService<CooldownObserver>(), services.GetRequiredService<MonitoringSession>().Observers);
    }

    [Fact]
    public async Task CooldownNotificationIsConfiguredBeforeTheCooldownMonitor()
    {
        await using ServiceProvider services = AppServices.Create();

        Assert.Same(services.GetRequiredService<CooldownNotifier>(), services.GetRequiredService<CooldownNotifier>());
        List<IMonitoringObserver> observers = [.. services.GetRequiredService<MonitoringSession>().Observers];
        int notification = observers.IndexOf(services.GetRequiredService<CooldownNotificationObserver>());
        int monitor = observers.IndexOf(services.GetRequiredService<CooldownObserver>());
        Assert.True(notification >= 0, "The cool-down notification gets no settings.");
        Assert.True(notification < monitor, "A threshold raised by the same settings would be notified with the old ones.");
    }

    [Fact]
    public async Task TelemetryHistoryIsRegisteredAsSingletonWithObserver()
    {
        await using ServiceProvider services = AppServices.Create();

        Assert.Same(services.GetRequiredService<TelemetryHistory>(), services.GetRequiredService<TelemetryHistory>());
        Assert.Same(services.GetRequiredService<TelemetryHistoryObserver>(), services.GetRequiredService<TelemetryHistoryObserver>());
        Assert.Equal(TimeSpan.FromDays(1), services.GetRequiredService<TelemetryHistory>().Retention);
    }

    [Fact]
    public async Task DiagnosticsWindowServicesAreRegisteredAndItsMenuItemEnabled()
    {
        await using ServiceProvider services = AppServices.Create();

        Assert.Same(services.GetRequiredService<MonitoredPrinters>(), services.GetRequiredService<MonitoredPrinters>());
        Assert.IsType<PrinterInfoSource>(services.GetRequiredService<IPrinterInfoSource>());
        Assert.True(services.GetRequiredService<TrayMenuOptions>().DiagnosticsAvailable);
        Assert.True(services.GetRequiredService<TrayMenuOptions>().SettingsAvailable);

        // One view model per opened window; nothing is configured yet, so it shows the starting notice.
        using DiagnosticsViewModel first = AppServices.CreateDiagnosticsViewModel(services);
        using DiagnosticsViewModel second = AppServices.CreateDiagnosticsViewModel(services);
        Assert.NotSame(first, second);
        Assert.Same(services.GetRequiredService<TelemetryHistory>(), first.History);
        Assert.True(first.HasNotice);
    }

    [Fact]
    public async Task SecretStoreMatchesOperatingSystem()
    {
        await using ServiceProvider services = AppServices.Create();

        ISecretStore store = services.GetRequiredService<ISecretStore>();

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsCredentialSecretStore>(store);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // The production service, local login Keychain (T-021); creating it does not touch the Keychain.
            Assert.Equal(KeychainSecretStore.DefaultServicePrefix, Assert.IsType<KeychainSecretStore>(store).Service);
        }
        else
        {
            Assert.IsType<UnsupportedSecretStore>(store);
        }
    }

    [Fact]
    public async Task SettingsWindowServicesAreRegisteredAndItsMenuItemEnabled()
    {
        await using ServiceProvider services = AppServices.Create();

        Assert.True(services.GetRequiredService<TrayMenuOptions>().SettingsAvailable);
        // Saved settings restart the running monitoring session.
        Assert.Same(services.GetRequiredService<MonitoringSession>(), services.GetRequiredService<ISettingsApplier>());
        Assert.IsType<SettingsEditorStore>(services.GetRequiredService<ISettingsEditorStore>());
        Assert.IsType<PrusaLinkConnectionTester>(services.GetRequiredService<IConnectionTester>());
        // Derived from the registered store, so the window and the store can never disagree.
        PasswordPolicy policy = services.GetRequiredService<PasswordPolicy>();
        Assert.Equal(PasswordPolicy.For(services.GetRequiredService<ISecretStore>()), policy);
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), policy.IsSupported);

        // One view model per opened window; creating it does not load anything yet (no disk access in this test).
        using SettingsViewModel first = AppServices.CreateSettingsViewModel(services);
        using SettingsViewModel second = AppServices.CreateSettingsViewModel(services);
        Assert.NotSame(first, second);
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), first.IsPasswordSupported);
        Assert.True(first.IsLoading);
    }

    [Fact]
    public async Task CameraServicesUseFFmpegWithTheDecidedTimingWithoutTouchingUserData()
    {
        await using ServiceProvider services = AppServices.Create();

        Assert.IsType<FFmpegCameraFeedFactory>(services.GetRequiredService<ICameraFeedFactory>());
        CameraFeedOptions options = services.GetRequiredService<CameraFeedOptions>();
        Assert.Equal((TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)), (options.ConnectTimeout, options.FrameTimeout, options.ReconnectDelay));
        Assert.Same(services.GetRequiredService<ReconnectingCameraFeed>(), services.GetRequiredService<ReconnectingCameraFeed>());
        Assert.Equal(TimeSpan.FromSeconds(15), Assert.IsType<CameraSnapshotTester>(services.GetRequiredService<ICameraTester>()).Timeout);
        // Only the type: resolving it touches no files (nothing is loaded or saved in this test).
        Assert.IsType<JsonCameraWindowStateStore>(services.GetRequiredService<ICameraWindowStateStore>());
        Assert.Same(services.GetRequiredService<RemovedCameraCredentials>(), services.GetRequiredService<RemovedCameraCredentials>());
    }

    /// <summary>
    /// T-087: one in-memory preview cache over the PrusaLink fetcher (T-086), fed with every snapshot and the printers of
    /// every run, and one shared store of decoded thumbnails that the Print name widget of every camera window uses.
    /// </summary>
    [Fact]
    public async Task PreviewCacheIsWiredToMonitoringAndToTheCameraWindows()
    {
        await using ServiceProvider services = AppServices.Create(
            cameraFeedFactory: static _ => UnavailableCameraFeedFactory.Instance,
            cameraWindowStateStore: static _ => new MemoryCameraWindowStateStore());

        Assert.Same(services.GetRequiredService<PrintPreviewCache>(), services.GetRequiredService<PrintPreviewCache>());
        Assert.Same(services.GetRequiredService<PrintThumbnails>(), services.GetRequiredService<PrintThumbnails>());
        PrintPreviewObserver observer = services.GetRequiredService<PrintPreviewObserver>();
        Assert.Contains(observer, services.GetRequiredService<MonitoringSession>().Observers);

        PrinterDefinition saved = new() { Id = "a", Name = "Alpha", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.0.226", Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) };
        using CameraViewModel? camera = AppServices.CreateCameraViewModel(services, "a", saved);
        Assert.NotNull(camera);
        PrintNameWidgetViewModel widget = Assert.IsType<PrintNameWidgetViewModel>(camera.Overlay.Widgets.Single(static widget => widget.Id == LayerPeek.Core.Cameras.Overlay.OverlayWidgetIds.PrintName));
        Assert.Same(services.GetRequiredService<PrintThumbnails>(), widget.Thumbnails);
    }

    [Fact]
    public async Task CameraWindowOfApplyComesFromTheSavedPrinter()
    {
        // T-040: Apply opens the window before monitoring knows the saved printer (or its camera).
        await using ServiceProvider services = AppServices.Create(
            cameraFeedFactory: static _ => UnavailableCameraFeedFactory.Instance,
            cameraWindowStateStore: static _ => new MemoryCameraWindowStateStore());
        PrinterDefinition saved = new() { Id = "a", Name = "Alpha", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.0.226", Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) };

        using CameraViewModel? camera = AppServices.CreateCameraViewModel(services, "A", saved);

        Assert.NotNull(camera);
        Assert.Equal("a", camera.PrinterId);
        Assert.Null(AppServices.CreateCameraViewModel(services, "a", saved with { Camera = CameraSettings.Disabled }));
        Assert.Null(AppServices.CreateCameraViewModel(services, "b", saved));
    }

    [Fact]
    public async Task CameraWindowNeedsAMonitoredPrinterWithAnEnabledCamera()
    {
        MemoryCameraWindowStateStore states = new();
        await using ServiceProvider services = AppServices.Create(
            cameraFeedFactory: static _ => UnavailableCameraFeedFactory.Instance,
            cameraWindowStateStore: _ => states);
        Assert.Same(states, services.GetRequiredService<ICameraWindowStateStore>());
        IMonitoringObserver printers = services.GetRequiredService<MonitoredPrinters>();

        // Before monitoring started nothing is known.
        Assert.Null(AppServices.CreateCameraViewModel(services, "a"));

        printers.ApplySettings(new AppSettings
        {
            Printers =
            [
                new PrinterDefinition { Id = "a", Name = "Alpha", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.0.226", Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) { Window = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.FullScreen } } },
                new PrinterDefinition { Id = "b", Name = "Beta", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.0.228" },
            ],
        });

        Assert.Null(AppServices.CreateCameraViewModel(services, "b"));
        Assert.Null(AppServices.CreateCameraViewModel(services, "missing"));
        using CameraViewModel? camera = AppServices.CreateCameraViewModel(services, "A");
        Assert.NotNull(camera);
        Assert.Equal("a", camera.PrinterId);
        Assert.Contains("Alpha", camera.Title, StringComparison.Ordinal);
        // T-035: the window defaults of the printer reach the window.
        Assert.Equal(CameraWindowStartMode.FullScreen, camera.Defaults.StartMode);
        Assert.Equal(CameraWindowMode.FullScreen, camera.Mode);
        // The window state goes to the memory store, never to the user data folder.
        camera.ShowMiniCommand.Execute(null);
        camera.Dispose();
        Assert.Equal(CameraWindowMode.Mini, states.Get("a").Mode);
    }

    [Fact]
    public async Task WebGatewayIsTheSharedPrusaLinkProxyAndStopsWithTheContainer()
    {
        ServiceProvider services = AppServices.Create();
        IPrinterWebGateway gateway = services.GetRequiredService<IPrinterWebGateway>();

        Assert.IsType<PrusaLinkWebGateway>(gateway);
        Assert.Same(gateway, services.GetRequiredService<PrusaLinkWebGateway>());
        Assert.Same(gateway, services.GetRequiredService<PrinterWebOpener>().Gateway);

        // Quitting disposes the container asynchronously (TrayHost.StopAsync); the gateway then refuses new proxies.
        await services.DisposeAsync().AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        PrinterDefinition printer = new() { Id = "mk4", Name = "MK4", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.10" };
        await Assert.ThrowsAsync<ObjectDisposedException>(() => gateway.OpenAsync(printer, TestContext.Current.CancellationToken));
    }

    /// <summary>Records the messages of every logger it creates.</summary>
    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly ListLogger<RecordingLoggerFactory> _logger = new();

        public IEnumerable<string> Messages => _logger.Entries.Select(static e => e.Message);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => _logger;

        public void Dispose()
        {
        }
    }

    private sealed class TrackingLoggerFactory : ILoggerFactory
    {
        public bool Disposed { get; private set; }

        public List<string> Categories { get; } = [];

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            Categories.Add(categoryName);
            return NullLogger.Instance;
        }

        public void Dispose() => Disposed = true;
    }
}
