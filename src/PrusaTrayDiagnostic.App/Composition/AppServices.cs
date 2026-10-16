using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Notifications;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
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
    public static ServiceProvider Create(ILoggerFactory? loggerFactory = null) => Create(loggerFactory, OperatingSystem.IsWindows());

    /// <param name="windows">Use the Windows notification implementation (only effective on Windows); lets tests check the other branch.</param>
    internal static ServiceProvider Create(ILoggerFactory? loggerFactory, bool windows)
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
        // Settings (T-015) and diagnostics (T-011) windows do not exist yet: their menu items are disabled.
        services.AddSingleton(static _ => TrayMenuOptions.Default);
        services.AddSingleton(static sp => new TrayMenuViewModel(
            sp.GetRequiredService<IPrinterMonitor>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IUrlLauncher>(),
            sp.GetRequiredService<TrayMenuOptions>()));
        services.AddSingleton(static sp => new ToastActivationRouter(sp.GetRequiredService<IUrlLauncher>(), sp.GetRequiredService<ILogger<ToastActivationRouter>>()));
        services.AddSingleton(sp => CreateNotificationService(sp, windows));
        services.AddSingleton(static sp => new PrintEventNotifier(
            sp.GetRequiredService<IPrinterMonitor>(),
            sp.GetRequiredService<INotificationService>(),
            sp.GetRequiredService<ILogger<PrintEventNotifier>>()));
        services.AddSingleton(static sp => new PrintNotificationObserver(sp.GetRequiredService<PrintEventNotifier>()));
        services.AddSingleton(static sp => new MonitoringSession(
            sp.GetRequiredService<ISettingsStore>,
            sp.GetRequiredService<IPrinterMonitor>(),
            [
                sp.GetRequiredService<TrayStatusViewModel>(),
                sp.GetRequiredService<TrayMenuViewModel>(),
                sp.GetRequiredService<PrintNotificationObserver>(),
                sp.GetRequiredService<ToastActivationRouter>(),
            ],
            sp.GetRequiredService<ILogger<MonitoringSession>>()));

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
            CultureInfo.CurrentUICulture,
            services.GetRequiredService<ILogger<ToastNotificationService>>());
    }

    private static ISecretStore CreateSecretStore() =>
        OperatingSystem.IsWindows()
            ? new WindowsCredentialSecretStore()
            // macOS Keychain store follows in T-021; until then passwords are unavailable but the app runs.
            : new UnsupportedSecretStore();
}
