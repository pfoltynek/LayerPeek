using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
using PrusaTrayDiagnostic.Core.Settings;
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
    public static ServiceProvider Create(ILoggerFactory? loggerFactory = null)
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
        services.AddSingleton(static sp => new TrayStatusViewModel(sp.GetRequiredService<IPrinterMonitor>(), sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<IUrlLauncher>(static sp => new ProcessUrlLauncher(sp.GetRequiredService<ILogger<ProcessUrlLauncher>>()));
        // The settings window (T-015) does not exist yet: its menu item is disabled. Diagnostics (T-011) is enabled.
        services.AddSingleton(static _ => TrayMenuOptions.Default);
        services.AddSingleton(static sp => new TrayMenuViewModel(
            sp.GetRequiredService<IPrinterMonitor>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IUrlLauncher>(),
            sp.GetRequiredService<TrayMenuOptions>()));
        // Windows toasts (T-010) and macOS notifications (T-020) replace this per OS; until then notifications are silently not sent.
        services.AddSingleton<INotificationService>(static _ => NullNotificationService.Instance);
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
        services.AddSingleton(static sp => new MonitoringSession(
            sp.GetRequiredService<ISettingsStore>,
            sp.GetRequiredService<IPrinterMonitor>(),
            [
                sp.GetRequiredService<TrayStatusViewModel>(),
                sp.GetRequiredService<TrayMenuViewModel>(),
                sp.GetRequiredService<PrintNotificationObserver>(),
                sp.GetRequiredService<TelemetryHistoryObserver>(),
                sp.GetRequiredService<MonitoredPrinters>(),
            ],
            sp.GetRequiredService<ILogger<MonitoringSession>>()));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
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

    private static ISecretStore CreateSecretStore() =>
        OperatingSystem.IsWindows()
            ? new WindowsCredentialSecretStore()
            // macOS Keychain store follows in T-021; until then passwords are unavailable but the app runs.
            : new UnsupportedSecretStore();
}
