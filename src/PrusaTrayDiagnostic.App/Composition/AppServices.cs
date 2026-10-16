using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Monitoring;
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
    public static ServiceProvider Create()
    {
        ServiceCollection services = new();

        // No logging provider yet: messages are discarded until a diagnostics/log task adds one.
        services.AddLogging();
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
        services.AddSingleton(static sp => new MonitoringSession(
            sp.GetRequiredService<ISettingsStore>,
            sp.GetRequiredService<IPrinterMonitor>(),
            sp.GetRequiredService<TrayStatusViewModel>(),
            sp.GetRequiredService<ILogger<MonitoringSession>>()));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static ISecretStore CreateSecretStore() =>
        OperatingSystem.IsWindows()
            ? new WindowsCredentialSecretStore()
            // macOS Keychain store follows in T-021; until then passwords are unavailable but the app runs.
            : new UnsupportedSecretStore();
}
