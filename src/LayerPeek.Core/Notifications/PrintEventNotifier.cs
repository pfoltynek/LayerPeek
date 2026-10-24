using Microsoft.Extensions.Logging;

using LayerPeek.Core.Events;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Notifications;

/// <summary>
/// Turns the snapshots of <see cref="IPrinterMonitor"/> into notifications: runs them through a
/// <see cref="PrintEventDetector"/> and shows the events enabled in <see cref="NotificationSettings"/> through
/// <see cref="INotificationService"/>. Nothing is notified until <see cref="Configure"/> is called.
/// Thread-safe: the monitor publishes printers concurrently, each printer's snapshots in order. Detection is serialized
/// under a lock (cheap, no I/O); the notification service is called outside of it, and its failures are logged without
/// affecting monitoring or later notifications.
/// </summary>
public sealed partial class PrintEventNotifier : IDisposable
{
    private readonly IPrinterMonitor _monitor;
    private readonly INotificationService _notifications;
    private readonly ILogger<PrintEventNotifier> _logger;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private Run? _run;
    private bool _disposed;

    public PrintEventNotifier(IPrinterMonitor monitor, INotificationService notifications, ILogger<PrintEventNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(logger);
        _monitor = monitor;
        _notifications = notifications;
        _logger = logger;
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
    }

    /// <summary>
    /// Starts notifying about <paramref name="printers"/> according to <paramref name="settings"/>. Call before every
    /// monitoring run. What is known about printers that stay configured is kept, so restarting monitoring after saving
    /// settings does not repeat a problem that is still present. Removed printers and printers whose address or
    /// connection type changed are forgotten (they start from a new baseline).
    /// </summary>
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

        PrintEventDetectorOptions options = new()
        {
            RemainingTimeThreshold = TimeSpan.FromMinutes(settings.RemainingTimeMinutes),
        };

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            PrintEventDetector detector;
            if (_run is null)
            {
                detector = new PrintEventDetector(options);
            }
            else
            {
                // The detector is only used under the lock, so it can be reconfigured in place.
                detector = _run.Detector;
                detector.UpdateOptions(options);
                Dictionary<string, PrinterDefinition> previous = _run.PrinterById;
                detector.RetainPrinters(id => IsSameMachine(previous, printerById, id));
            }

            _run = new Run(printerById, settings, detector);
        }
    }

    /// <summary>
    /// The printer stays configured and still points to the same machine (a rename keeps it). A changed address or
    /// connection type may be another printer, whose current state must be a new baseline, not a transition.
    /// </summary>
    private static bool IsSameMachine(Dictionary<string, PrinterDefinition> previous, Dictionary<string, PrinterDefinition> current, string printerId) =>
        previous.TryGetValue(printerId, out PrinterDefinition? before)
        && current.TryGetValue(printerId, out PrinterDefinition? after)
        && before.ConnectionType == after.ConnectionType
        && string.Equals(before.Address, after.Address, StringComparison.Ordinal);

    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        lock (_gate)
        {
            _disposed = true;
            _run = null;
        }
    }

    internal void OnSnapshotUpdated(object? sender, PrinterSnapshot snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        Run? run;
        PrinterDefinition? printer;
        IReadOnlyList<PrintEvent> events;
        lock (_gate)
        {
            run = _run;
            // Snapshots of printers that are not (or no longer) configured are ignored.
            if (run is null || !run.PrinterById.TryGetValue(snapshot.PrinterId, out printer))
            {
                return;
            }

            events = run.Detector.Process(snapshot);
        }

        // Indexed loop: enumerating through the interface would allocate on every poll.
        for (int i = 0; i < events.Count; i++)
        {
            PrintEvent printEvent = events[i];
            if (run.Settings.IsEnabledFor(printEvent.Kind))
            {
                Show(new PrintNotification
                {
                    Printer = printer,
                    Kind = printEvent.Kind,
                    Timestamp = printEvent.Timestamp,
                    FileName = printEvent.FileName,
                    Remaining = printEvent.Remaining,
                    PlaySound = run.Settings.ShouldPlaySound(PrintNotification.SeverityOf(printEvent.Kind)),
                });
            }
        }
    }

    private void Show(PrintNotification notification)
    {
        lock (_gate)
        {
            // Narrows (cannot close) the window in which a callback that started before Dispose still shows something;
            // INotificationService implementations tolerate the remaining race.
            if (_disposed)
            {
                return;
            }
        }

        try
        {
            _notifications.Show(notification);
        }
        catch (Exception ex)
        {
            LogShowFailed(ex, notification.Kind, notification.Printer.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Showing the {Kind} notification for printer '{PrinterId}' failed.")]
    private partial void LogShowFailed(Exception exception, PrintEventKind kind, string printerId);

    /// <summary>Everything that belongs to one <see cref="Configure"/> call; replaced as a whole.</summary>
    private sealed record Run(Dictionary<string, PrinterDefinition> PrinterById, NotificationSettings Settings, PrintEventDetector Detector);
}
