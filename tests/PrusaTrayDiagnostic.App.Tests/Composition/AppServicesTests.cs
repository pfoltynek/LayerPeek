using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.App.History;
using PrusaTrayDiagnostic.App.Notifications;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.History;
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
    public async Task NotificationPipelineIsRegisteredWithNoOpServiceUntilPlatformImplementationsExist()
    {
        await using ServiceProvider services = AppServices.Create();

        // Windows toasts follow in T-010, macOS in T-020; until then notifications are silently not sent.
        Assert.Same(NullNotificationService.Instance, services.GetRequiredService<INotificationService>());
        Assert.Same(services.GetRequiredService<PrintEventNotifier>(), services.GetRequiredService<PrintEventNotifier>());
        Assert.NotNull(services.GetRequiredService<PrintNotificationObserver>());
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
        Assert.False(services.GetRequiredService<TrayMenuOptions>().SettingsAvailable);

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
