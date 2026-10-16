using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.Notifications;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Notifications;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;

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
