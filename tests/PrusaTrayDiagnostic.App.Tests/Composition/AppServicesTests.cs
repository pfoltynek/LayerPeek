using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.App.History;
using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Notifications;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.History;
using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Notifications;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.Core.Web;
using PrusaTrayDiagnostic.PrusaLink.WebProxy;

namespace PrusaTrayDiagnostic.App.Tests.Composition;

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
        // macOS notifications follow in T-020; until then notifications are silently not sent.
        await using ServiceProvider services = AppServices.Create(loggerFactory: null, windows: false);

        Assert.Same(NullNotificationService.Instance, services.GetRequiredService<INotificationService>());
        AssertNotificationPipeline(services);
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
        Assert.Equal(OperatingSystem.IsWindows(), policy.IsSupported);

        // One view model per opened window; creating it does not load anything yet (no disk access in this test).
        using SettingsViewModel first = AppServices.CreateSettingsViewModel(services);
        using SettingsViewModel second = AppServices.CreateSettingsViewModel(services);
        Assert.NotSame(first, second);
        Assert.Equal(OperatingSystem.IsWindows(), first.IsPasswordSupported);
        Assert.True(first.IsLoading);
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
