using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Notifications;

/// <summary>
/// Handles a click on a notification: launch argument → configured printer → its web interface
/// (<see cref="PrinterWebAddress"/>, the same rules as the tray menu) → browser. Knows the printers of the current
/// monitoring run, so an old notification opens the current address and one of a removed printer opens nothing.
/// Thread-safe: clicks arrive on an OS thread.
/// </summary>
internal sealed partial class ToastActivationRouter : IMonitoringObserver
{
    private readonly IUrlLauncher _launcher;
    private readonly ILogger<ToastActivationRouter> _logger;

    // Replaced as a whole, never mutated after publishing.
    private volatile Dictionary<string, PrinterDefinition> _printers = new(StringComparer.OrdinalIgnoreCase);

    public ToastActivationRouter(IUrlLauncher launcher, ILogger<ToastActivationRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(logger);
        _launcher = launcher;
        _logger = logger;
    }

    /// <summary>Opens the web interface of the printer in <paramref name="argument"/>; never throws.</summary>
    /// <returns><see langword="true"/> when the browser was started.</returns>
    public Task<bool> ActivateAsync(string? argument)
    {
        if (!ToastLaunchArgument.TryGetPrinterId(argument, out string? printerId))
        {
            LogMalformedArgument();
            return Task.FromResult(false);
        }

        if (!_printers.TryGetValue(printerId, out PrinterDefinition? printer))
        {
            // Removed since the notification was shown; nothing to open.
            LogUnknownPrinter(printerId);
            return Task.FromResult(false);
        }

        Uri? address = PrinterWebAddress.For(printer);
        if (address is null)
        {
            // The address itself is not logged: it could contain credentials.
            LogNoWebAddress(printer.Id);
            return Task.FromResult(false);
        }

        return _launcher.OpenAsync(address);
    }

    void IMonitoringObserver.ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Dictionary<string, PrinterDefinition> printers = new(settings.Printers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in settings.Printers)
        {
            printers.TryAdd(printer.Id, printer);
        }

        _printers = printers;
    }

    /// <summary>The printers stay configured; clicks keep working.</summary>
    void IMonitoringObserver.ReportMonitoringFailed()
    {
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored a notification click with an unrecognized argument.")]
    private partial void LogMalformedArgument();

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification click for printer '{PrinterId}', which is no longer configured; nothing to open.")]
    private partial void LogUnknownPrinter(string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification click for printer '{PrinterId}', whose address cannot be opened in a browser.")]
    private partial void LogNoWebAddress(string printerId);
}
