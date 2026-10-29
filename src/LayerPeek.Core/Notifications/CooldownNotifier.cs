using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Events;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Notifications;

/// <summary>
/// "Printer cooled down" notification (T-080): shows the <see cref="CooldownMonitor.CooledDown"/> events through
/// <see cref="INotificationService"/> as <see cref="PrintEventKind.CooledDown"/>, when <see cref="NotificationSettings"/>
/// enables it (<see cref="NotificationSettings.IsEnabledFor"/>) and with the sound of every information notification
/// (<see cref="NotificationSettings.ShouldPlaySound"/>). The monitor raises the event at most once per print and never for a
/// print whose end was not observed or that ended already below the thresholds, so nothing is filtered here beyond the
/// settings. Nothing is notified until <see cref="Configure"/> is called.
/// <para>
/// Thread-safe: events arrive on the monitor's publishing threads. The notification service is called outside the lock and
/// its failures are logged without affecting the monitor or later notifications.
/// </para>
/// </summary>
public sealed partial class CooldownNotifier : IDisposable
{
    private readonly CooldownMonitor _cooldown;
    private readonly INotificationService _notifications;
    private readonly ILogger<CooldownNotifier> _logger;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private Run? _run;
    private bool _disposed;

    public CooldownNotifier(CooldownMonitor cooldown, INotificationService notifications, ILogger<CooldownNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(cooldown);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(logger);
        _cooldown = cooldown;
        _notifications = notifications;
        _logger = logger;
        _cooldown.CooledDown += OnCooledDown;
    }

    /// <summary>
    /// Notifies about <paramref name="printers"/> according to <paramref name="settings"/> from now on. Call before every
    /// monitoring run and whenever the settings change, before the <see cref="CooldownMonitor"/> is configured (a raised
    /// threshold may cool a printer down right away).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="printers"/> contains <see langword="null"/>.</exception>
    public void Configure(IReadOnlyList<PrinterDefinition> printers, NotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(printers);
        ArgumentNullException.ThrowIfNull(settings);
        Dictionary<string, PrinterDefinition> printerById = new(printers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition? printer in printers)
        {
            if (printer is null)
            {
                throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            }

            printerById.TryAdd(printer.Id, printer);
        }

        lock (_gate)
        {
            if (!_disposed)
            {
                _run = new Run(printerById, settings);
            }
        }
    }

    public void Dispose()
    {
        _cooldown.CooledDown -= OnCooledDown;
        lock (_gate)
        {
            _disposed = true;
            _run = null;
        }
    }

    private void OnCooledDown(object? sender, CooldownEvent cooled)
    {
        PrinterDefinition? printer;
        NotificationSettings settings;
        lock (_gate)
        {
            // Printers that are not (or no longer) configured are ignored; so is everything after Dispose.
            if (_run is not { } run || cooled.PrinterId is null || !run.PrinterById.TryGetValue(cooled.PrinterId, out printer))
            {
                return;
            }

            settings = run.Settings;
        }

        if (!settings.IsEnabledFor(PrintEventKind.CooledDown))
        {
            return;
        }

        PrintNotification notification = new()
        {
            Printer = printer,
            Kind = PrintEventKind.CooledDown,
            Timestamp = cooled.Timestamp,
            FileName = cooled.FileName,
            Cooldown = cooled,
            PlaySound = settings.ShouldPlaySound(PrintNotification.SeverityOf(PrintEventKind.CooledDown)),
        };

        try
        {
            _notifications.Show(notification);
        }
        catch (Exception ex)
        {
            LogShowFailed(ex, printer.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Showing the cooled-down notification for printer '{PrinterId}' failed.")]
    private partial void LogShowFailed(Exception exception, string printerId);

    /// <summary>Everything that belongs to one <see cref="Configure"/> call; replaced as a whole.</summary>
    private sealed record Run(Dictionary<string, PrinterDefinition> PrinterById, NotificationSettings Settings);
}
