using LayerPeek.App.Cooldown;
using LayerPeek.App.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Events;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Notifications;

/// <summary>T-080: the "printer cooled down" notification gets the printers and notification settings of every run.</summary>
public sealed class CooldownNotificationObserverTests : IDisposable
{
    private readonly FakePrinterMonitor _monitor = new();
    private readonly RecordingNotificationService _service = new();
    private readonly CooldownMonitor _cooldown;
    private readonly CooldownNotifier _notifier;
    private readonly CooldownNotificationObserver _observer;
    private readonly CooldownObserver _cooldownObserver;

    public CooldownNotificationObserverTests()
    {
        _cooldown = new CooldownMonitor(_monitor, TimeProvider.System, new ListLogger<CooldownMonitor>());
        _notifier = new CooldownNotifier(_cooldown, _service, new ListLogger<CooldownNotifier>());
        _observer = new CooldownNotificationObserver(_notifier);
        _cooldownObserver = new CooldownObserver(_cooldown);
    }

    public void Dispose()
    {
        _notifier.Dispose();
        _cooldown.Dispose();
    }

    [Fact]
    public void AppliedSettingsEnableTheNotification()
    {
        PrinterDefinition printer = Definition();
        Apply(new AppSettings { Printers = [printer], Notifications = new NotificationSettings { CooledDown = true } });

        RaiseCooledPrint();

        PrintNotification notification = Assert.Single(_service.Notifications);
        Assert.Equal(PrintEventKind.CooledDown, notification.Kind);
        Assert.Same(printer, notification.Printer);
        Assert.False(notification.PlaySound);
    }

    [Fact]
    public void DefaultSettingsNotifyNothing()
    {
        Apply(new AppSettings { Printers = [Definition()] });

        RaiseCooledPrint();

        Assert.Equal(CooldownPhase.CooledDown, _cooldown.GetState("mk4").Phase);
        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void UpdatedSettingsWithoutRestartReachTheNotifier()
    {
        Apply(new AppSettings { Printers = [Definition()] });

        AppSettings enabled = new() { Printers = [Definition()], Notifications = new NotificationSettings { CooledDown = true, Sound = NotificationSound.All } };
        ((IMonitoringObserver)_observer).UpdateSettings(enabled);
        ((IMonitoringObserver)_cooldownObserver).UpdateSettings(enabled);
        RaiseCooledPrint();

        Assert.True(Assert.Single(_service.Notifications).PlaySound);
    }

    [Fact]
    public void MonitoringFailureIsNotNotified()
    {
        Apply(new AppSettings { Printers = [Definition()], Notifications = new NotificationSettings { CooledDown = true } });

        _observer.ReportMonitoringFailed();

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new CooldownNotificationObserver(null!));
        Assert.Throws<ArgumentNullException>(() => _observer.ApplySettings(null!));
    }

    /// <summary>In the order of the monitoring session: the notification before the cool-down monitor.</summary>
    private void Apply(AppSettings settings)
    {
        _observer.ApplySettings(settings);
        _cooldownObserver.ApplySettings(settings);
    }

    /// <summary>
    /// A print, then "finished" readings from 200 °C down to 38 °C, the bed from 60 °C to 39 °C (the last two of both at or
    /// below the default thresholds of 40 °C, T-109).
    /// </summary>
    private void RaiseCooledPrint()
    {
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(50)) with { Telemetry = Hot(215, 215, 60) });
        double[] nozzle = [200, 120, 70, 40, 38];
        double[] bed = [60, 55, 48, 40, 39];
        for (int i = 0; i < nozzle.Length; i++)
        {
            _monitor.Raise(Snapshot(PrinterState.Finished, Job(100)) with { Timestamp = Now.AddSeconds(5 * (i + 1)), Telemetry = Hot(nozzle[i], 0, bed[i]) });
        }
    }

    private static Telemetry Hot(double nozzle, double target, double bed) => new() { Nozzle = new Temperature(nozzle, target), Bed = new Temperature(bed, 0) };

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<PrintNotification> Notifications { get; } = [];

        public void Show(PrintNotification notification) => Notifications.Add(notification);
    }
}
