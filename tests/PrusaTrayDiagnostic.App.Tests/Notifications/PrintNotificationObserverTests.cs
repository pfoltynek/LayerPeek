using PrusaTrayDiagnostic.App.Notifications;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Events;
using PrusaTrayDiagnostic.Core.Notifications;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Notifications;

public sealed class PrintNotificationObserverTests : IDisposable
{
    private readonly FakePrinterMonitor _monitor = new();
    private readonly RecordingNotificationService _service = new();
    private readonly PrintEventNotifier _notifier;
    private readonly PrintNotificationObserver _observer;

    public PrintNotificationObserverTests()
    {
        _notifier = new PrintEventNotifier(_monitor, _service, new ListLogger<PrintEventNotifier>());
        _observer = new PrintNotificationObserver(_notifier);
    }

    public void Dispose() => _notifier.Dispose();

    [Fact]
    public void AppliedSettingsConfigureTheNotifier()
    {
        PrinterDefinition printer = Definition();
        _observer.ApplySettings(new AppSettings { Printers = [printer], Notifications = new NotificationSettings { PrintPaused = false } });

        _monitor.Raise(Snapshot(PrinterState.Idle));
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10)) with { Timestamp = Now.AddSeconds(5) });
        _monitor.Raise(Snapshot(PrinterState.Paused, Job(10)) with { Timestamp = Now.AddSeconds(10) });

        PrintNotification notification = Assert.Single(_service.Notifications);
        Assert.Equal(PrintEventKind.PrintStarted, notification.Kind);
        Assert.Same(printer, notification.Printer);
    }

    [Fact]
    public void UpdatedSettingsWithoutRestartReachTheNotifier()
    {
        PrinterDefinition printer = Definition();
        _observer.ApplySettings(new AppSettings { Printers = [printer] });
        _monitor.Raise(Snapshot(PrinterState.Idle));

        ((IMonitoringObserver)_observer).UpdateSettings(new AppSettings { Printers = [printer], Notifications = new NotificationSettings { PrintStarted = false } });
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(10)) with { Timestamp = Now.AddSeconds(5) });
        _monitor.Raise(Snapshot(PrinterState.Paused, Job(10)) with { Timestamp = Now.AddSeconds(10) });

        // The started print is not announced any more, and the printer kept its state (no new baseline).
        Assert.Equal(PrintEventKind.PrintPaused, Assert.Single(_service.Notifications).Kind);
    }

    [Fact]
    public void MonitoringFailureIsNotNotified()
    {
        _observer.ApplySettings(new AppSettings { Printers = [Definition()] });

        _observer.ReportMonitoringFailed();

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new PrintNotificationObserver(null!));
        Assert.Throws<ArgumentNullException>(() => _observer.ApplySettings(null!));
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<PrintNotification> Notifications { get; } = [];

        public void Show(PrintNotification notification) => Notifications.Add(notification);
    }
}
