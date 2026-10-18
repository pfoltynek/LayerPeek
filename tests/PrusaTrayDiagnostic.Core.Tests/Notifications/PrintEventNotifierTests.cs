using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.Events;
using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Notifications;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;
using PrusaTrayDiagnostic.Core.Tests.Monitoring;

namespace PrusaTrayDiagnostic.Core.Tests.Notifications;

public sealed class PrintEventNotifierTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly PrinterDefinition Mk4 = new() { Id = "mk4", Name = "Workshop MK4", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" };

    private readonly EventMonitor _monitor = new();
    private readonly RecordingNotificationService _service = new();
    private readonly ListLogger<PrintEventNotifier> _logger = new();
    private readonly PrintEventNotifier _notifier;

    public PrintEventNotifierTests() => _notifier = new PrintEventNotifier(_monitor, _service, _logger);

    public void Dispose() => _notifier.Dispose();

    [Fact]
    public void EventIsDeliveredWithPrinterDefinitionAndJob()
    {
        _notifier.Configure([Mk4], new NotificationSettings());

        _monitor.Raise(Snapshot(PrinterState.Idle, Start));
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(5), Job("benchy.bgcode")));

        PrintNotification notification = Assert.Single(_service.Notifications);
        Assert.Same(Mk4, notification.Printer);
        Assert.Equal(PrintEventKind.PrintStarted, notification.Kind);
        Assert.Equal(NotificationSeverity.Information, notification.Severity);
        Assert.Equal("benchy.bgcode", notification.FileName);
        Assert.Null(notification.Remaining);
        Assert.Equal(Start.AddSeconds(5), notification.Timestamp);
    }

    [Fact]
    public void EachPrinterNotifiesWithItsOwnNameAmongSeveralPrinters()
    {
        PrinterDefinition xl = new() { Id = "xl", Name = "Office XL", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.21" };
        PrinterDefinition mini = new() { Id = "mini", Name = "Mini", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.22" };
        _notifier.Configure([Mk4, xl, mini], new NotificationSettings());
        foreach (string id in new[] { "mk4", "xl", "mini" })
        {
            _monitor.Raise(Snapshot(PrinterState.Idle, Start, printerId: id));
        }

        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(5), Job("a"), printerId: "xl"));
        _monitor.Raise(Snapshot(PrinterState.Error, Start.AddSeconds(5), printerId: "mini"));
        _monitor.Raise(Snapshot(PrinterState.Idle, Start.AddSeconds(5), printerId: "mk4"));

        Assert.Collection(
            _service.Notifications,
            notification => Assert.Equal(("Office XL", PrintEventKind.PrintStarted), (notification.Printer.Name, notification.Kind)),
            notification => Assert.Equal(("Mini", PrintEventKind.Error), (notification.Printer.Name, notification.Kind)));
        Assert.Same(xl, _service.Notifications[0].Printer);
        Assert.Same(mini, _service.Notifications[1].Printer);
    }

    [Fact]
    public void MasterSwitchSuppressesEverything()
    {
        _notifier.Configure([Mk4], new NotificationSettings { Enabled = false });

        RaiseFullPrint();

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void DisabledEventKindIsSuppressedOthersAreDelivered()
    {
        _notifier.Configure([Mk4], new NotificationSettings { PrintStarted = false });

        RaiseFullPrint();

        Assert.Equal([PrintEventKind.RemainingTime, PrintEventKind.PrintFinished], _service.Kinds);
    }

    [Fact]
    public void RemainingTimeThresholdComesFromSettings()
    {
        _notifier.Configure([Mk4], new NotificationSettings { RemainingTimeMinutes = 5 });

        _monitor.Raise(Snapshot(PrinterState.Printing, Start, Job("a", remainingMinutes: 30)));
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(5), Job("a", remainingMinutes: 9)));
        Assert.Empty(_service.Notifications);

        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(10), Job("a", remainingMinutes: 5)));

        PrintNotification notification = Assert.Single(_service.Notifications);
        Assert.Equal(PrintEventKind.RemainingTime, notification.Kind);
        Assert.Equal(TimeSpan.FromMinutes(5), notification.Remaining);
    }

    [Fact]
    public void ZeroRemainingTimeThresholdDisablesTheAlert()
    {
        _notifier.Configure([Mk4], new NotificationSettings { RemainingTimeMinutes = 0 });

        RaiseFullPrint();

        Assert.Equal([PrintEventKind.PrintStarted, PrintEventKind.PrintFinished], _service.Kinds);
    }

    [Fact]
    public void OfflineAndOnlineAreDeliveredAfterDelay()
    {
        _notifier.Configure([Mk4], new NotificationSettings());

        _monitor.Raise(Snapshot(PrinterState.Idle, Start));
        _monitor.Raise(Snapshot(PrinterState.Offline, Start.AddSeconds(5)));
        _monitor.Raise(Snapshot(PrinterState.Offline, Start.AddSeconds(5) + PrintEventDetectorOptions.Default.OfflineDelay));
        _monitor.Raise(Snapshot(PrinterState.Idle, Start.AddMinutes(5)));

        Assert.Equal([PrintEventKind.PrinterOffline, PrintEventKind.PrinterOnline], _service.Kinds);
        Assert.Equal(NotificationSeverity.Warning, _service.Notifications[0].Severity);
        Assert.Equal(NotificationSeverity.Information, _service.Notifications[1].Severity);
    }

    [Fact]
    public void ThrowingNotificationServiceIsLoggedAndDoesNotStopLaterNotifications()
    {
        _notifier.Configure([Mk4], new NotificationSettings());
        _service.ThrowOnce = new InvalidOperationException("toast failed");

        _monitor.Raise(Snapshot(PrinterState.Idle, Start));
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(5), Job("a")));
        _monitor.Raise(Snapshot(PrinterState.Paused, Start.AddSeconds(10), Job("a")));

        Assert.Equal([PrintEventKind.PrintPaused], _service.Kinds);
        (LogLevel level, Exception? exception, string message) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Same(_service.ThrownException, exception);
        Assert.Contains("mk4", message, StringComparison.Ordinal);
        Assert.Contains(nameof(PrintEventKind.PrintStarted), message, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotsBeforeConfigureAreIgnored()
    {
        _monitor.Raise(Snapshot(PrinterState.Idle, Start));
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(5), Job("a")));

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void SnapshotsOfUnconfiguredPrintersAreIgnored()
    {
        _notifier.Configure([Mk4], new NotificationSettings());

        _monitor.Raise(Snapshot(PrinterState.Idle, Start, printerId: "xl"));
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(5), Job("a"), printerId: "xl"));

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void PrinterIdIsMatchedCaseInsensitively()
    {
        _notifier.Configure([Mk4], new NotificationSettings());

        _monitor.Raise(Snapshot(PrinterState.Idle, Start, printerId: "MK4"));
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(5), Job("a"), printerId: "MK4"));

        Assert.Same(Mk4, Assert.Single(_service.Notifications).Printer);
    }

    [Fact]
    public void ReconfigureKeepsTrackingSoOngoingErrorIsNotRepeated()
    {
        _notifier.Configure([Mk4], new NotificationSettings());
        _monitor.Raise(Snapshot(PrinterState.Error, Start));

        // Saving settings restarts monitoring; the error that is still there must not be notified again.
        _notifier.Configure([Mk4], new NotificationSettings { PrintPaused = false });
        _monitor.Raise(Snapshot(PrinterState.Error, Start.AddSeconds(5)));
        _monitor.Raise(Snapshot(PrinterState.Idle, Start.AddSeconds(10)));
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(15), Job("a")));

        Assert.Equal([PrintEventKind.Error, PrintEventKind.PrintStarted], _service.Kinds);
    }

    [Fact]
    public void ChangedAddressStartsFromNewBaseline()
    {
        _notifier.Configure([Mk4], new NotificationSettings());
        _monitor.Raise(Snapshot(PrinterState.Printing, Start, Job("a") with { Id = 5 }));

        // Another machine under the same id: its running print is its baseline, not a new start.
        _notifier.Configure([Mk4 with { Address = "192.168.1.99" }], new NotificationSettings());
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(5), Job("b") with { Id = 3 }));

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void ChangedConnectionTypeStartsFromNewBaseline()
    {
        _notifier.Configure([Mk4], new NotificationSettings());
        _monitor.Raise(Snapshot(PrinterState.Printing, Start, Job("a") with { Id = 5 }));

        _notifier.Configure([Mk4 with { ConnectionType = ConnectionType.PrusaConnect }], new NotificationSettings());
        _monitor.Raise(Snapshot(PrinterState.Finished, Start.AddSeconds(5), Job("c") with { Id = 9 }));

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void RenamedPrinterKeepsTracking()
    {
        _notifier.Configure([Mk4], new NotificationSettings());
        _monitor.Raise(Snapshot(PrinterState.Error, Start));

        PrinterDefinition renamed = Mk4 with { Name = "Renamed" };
        _notifier.Configure([renamed], new NotificationSettings());
        _monitor.Raise(Snapshot(PrinterState.Error, Start.AddSeconds(5)));
        _monitor.Raise(Snapshot(PrinterState.Idle, Start.AddSeconds(10)));
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(15), Job("a")));

        Assert.Equal([PrintEventKind.Error, PrintEventKind.PrintStarted], _service.Kinds);
        Assert.Same(renamed, _service.Notifications[1].Printer);
    }

    [Fact]
    public void RemovedPrinterStartsFromNewBaselineWhenAddedAgain()
    {
        _notifier.Configure([Mk4], new NotificationSettings());
        _monitor.Raise(Snapshot(PrinterState.Idle, Start));

        _notifier.Configure([], new NotificationSettings());
        _notifier.Configure([Mk4], new NotificationSettings());
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(5), Job("a")));

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void RejectedCredentialsAreDeliveredAsActionRequiredUnderAttentionSwitch()
    {
        _notifier.Configure([Mk4], new NotificationSettings());
        _monitor.Raise(Snapshot(PrinterState.Idle, Start));
        _monitor.Raise(Snapshot(PrinterState.Unauthorized, Start.AddSeconds(5)));

        PrintNotification notification = Assert.Single(_service.Notifications);
        Assert.Equal(PrintEventKind.CredentialsRejected, notification.Kind);
        Assert.Equal(NotificationSeverity.ActionRequired, notification.Severity);

        _notifier.Configure([Mk4], new NotificationSettings { Attention = false });
        _monitor.Raise(Snapshot(PrinterState.Idle, Start.AddSeconds(10)));
        _monitor.Raise(Snapshot(PrinterState.Unauthorized, Start.AddSeconds(15)));

        Assert.Single(_service.Notifications);
    }

    [Theory]
    [InlineData(NotificationSound.ImportantOnly, PrinterState.Error, true)]
    [InlineData(NotificationSound.ImportantOnly, PrinterState.Printing, false)]
    [InlineData(NotificationSound.All, PrinterState.Printing, true)]
    [InlineData(NotificationSound.Off, PrinterState.Error, false)]
    public void PlaySoundFollowsSoundSetting(NotificationSound sound, PrinterState state, bool expected)
    {
        _notifier.Configure([Mk4], new NotificationSettings { Sound = sound });
        _monitor.Raise(Snapshot(PrinterState.Idle, Start));

        _monitor.Raise(Snapshot(state, Start.AddSeconds(5), Job("a")));

        Assert.Equal(expected, Assert.Single(_service.Notifications).PlaySound);
    }

    [Fact]
    public void NothingIsShownAfterDisposeEvenWithinOneSnapshot()
    {
        _notifier.Configure([Mk4], new NotificationSettings());
        _monitor.Raise(Snapshot(PrinterState.Idle, Start));
        _monitor.Raise(Snapshot(PrinterState.Offline, Start.AddSeconds(5)));
        _monitor.Raise(Snapshot(PrinterState.Offline, Start.AddMinutes(2)));
        // Shutdown while a snapshot with two events (online + started) is being delivered.
        _service.OnShow = notification =>
        {
            if (notification.Kind == PrintEventKind.PrinterOnline)
            {
                _notifier.Dispose();
            }
        };

        _monitor.RaiseIgnoringSubscription(_notifier, Snapshot(PrinterState.Printing, Start.AddMinutes(3), Job("a")));

        Assert.Equal([PrintEventKind.PrinterOffline, PrintEventKind.PrinterOnline], _service.Kinds);
    }

    [Fact]
    public void ConfigureUsesUpdatedPrinterDefinition()
    {
        _notifier.Configure([Mk4], new NotificationSettings());
        PrinterDefinition renamed = Mk4 with { Name = "Renamed" };
        _notifier.Configure([renamed], new NotificationSettings());

        _monitor.Raise(Snapshot(PrinterState.Idle, Start));
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(5), Job("a")));

        Assert.Same(renamed, Assert.Single(_service.Notifications).Printer);
    }

    [Fact]
    public void DisposeUnsubscribesFromMonitor()
    {
        Assert.Equal(1, _monitor.SubscriberCount);

        _notifier.Dispose();
        _notifier.Dispose();

        Assert.Equal(0, _monitor.SubscriberCount);
    }

    [Fact]
    public void ConfigureAfterDisposeIsIgnored()
    {
        _notifier.Dispose();

        _notifier.Configure([Mk4], new NotificationSettings());
        _monitor.RaiseIgnoringSubscription(_notifier, Snapshot(PrinterState.Idle, Start));
        _monitor.RaiseIgnoringSubscription(_notifier, Snapshot(PrinterState.Printing, Start.AddSeconds(5), Job("a")));

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void NullSnapshotIsIgnored()
    {
        _notifier.Configure([Mk4], new NotificationSettings());

        _monitor.Raise(null!);

        Assert.Empty(_service.Notifications);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void ConfigureRejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => _notifier.Configure(null!, new NotificationSettings()));
        Assert.Throws<ArgumentNullException>(() => _notifier.Configure([Mk4], null!));
        Assert.Throws<ArgumentException>(() => _notifier.Configure([Mk4, null!], new NotificationSettings()));
    }

    [Fact]
    public async Task ConcurrentPrintersAreEachReportedExactlyOnce()
    {
        const int PrinterCount = 16;
        PrinterDefinition[] printers = new PrinterDefinition[PrinterCount];
        for (int i = 0; i < PrinterCount; i++)
        {
            printers[i] = Mk4 with { Id = "p" + i };
        }

        _notifier.Configure(printers, new NotificationSettings());

        // Like the monitor: one loop per printer, loops run concurrently, each printer's snapshots in order.
        Task[] loops = new Task[PrinterCount];
        for (int i = 0; i < PrinterCount; i++)
        {
            string id = printers[i].Id;
            loops[i] = Task.Run(
                () =>
                {
                    _monitor.Raise(Snapshot(PrinterState.Idle, Start, printerId: id));
                    for (int poll = 1; poll <= 50; poll++)
                    {
                        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(poll), Job("a"), printerId: id));
                    }

                    _monitor.Raise(Snapshot(PrinterState.Finished, Start.AddSeconds(60), Job("a"), printerId: id));
                },
                TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(loops).WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        foreach (PrinterDefinition printer in printers)
        {
            PrintEventKind[] kinds = [.. _service.Notifications.Where(n => n.Printer == printer).Select(static n => n.Kind)];
            Assert.Equal([PrintEventKind.PrintStarted, PrintEventKind.PrintFinished], kinds);
        }
    }

    [Theory]
    [InlineData(PrintEventKind.PrintStarted, NotificationSeverity.Information)]
    [InlineData(PrintEventKind.PrintFinished, NotificationSeverity.Information)]
    [InlineData(PrintEventKind.PrintPaused, NotificationSeverity.Information)]
    [InlineData(PrintEventKind.PrintResumed, NotificationSeverity.Information)]
    [InlineData(PrintEventKind.PrintStopped, NotificationSeverity.Information)]
    [InlineData(PrintEventKind.RemainingTime, NotificationSeverity.Information)]
    [InlineData(PrintEventKind.PrinterOnline, NotificationSeverity.Information)]
    [InlineData(PrintEventKind.PrinterOffline, NotificationSeverity.Warning)]
    [InlineData(PrintEventKind.Error, NotificationSeverity.ActionRequired)]
    [InlineData(PrintEventKind.AttentionRequired, NotificationSeverity.ActionRequired)]
    [InlineData(PrintEventKind.CredentialsRejected, NotificationSeverity.ActionRequired)]
    [InlineData((PrintEventKind)999, NotificationSeverity.Information)]
    public void SeverityFollowsEventKind(PrintEventKind kind, NotificationSeverity expected)
    {
        PrintNotification notification = new() { Printer = Mk4, Kind = kind, Timestamp = Start };

        Assert.Equal(expected, notification.Severity);
    }

    [Fact]
    public void NullNotificationServiceAcceptsEverything()
    {
        NullNotificationService.Instance.Show(new PrintNotification { Printer = Mk4, Kind = PrintEventKind.Error, Timestamp = Start });
    }

    /// <summary>Idle → started → 10 min left → finished.</summary>
    private void RaiseFullPrint()
    {
        _monitor.Raise(Snapshot(PrinterState.Idle, Start));
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(5), Job("a", remainingMinutes: 30)));
        _monitor.Raise(Snapshot(PrinterState.Printing, Start.AddSeconds(10), Job("a", remainingMinutes: 10)));
        _monitor.Raise(Snapshot(PrinterState.Finished, Start.AddSeconds(15), Job("a")));
    }

    private static PrintJob Job(string fileName, double? remainingMinutes = null) => new()
    {
        Id = 7,
        FileName = fileName,
        ProgressPercent = 50,
        Remaining = remainingMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null,
    };

    private static PrinterSnapshot Snapshot(PrinterState state, DateTimeOffset timestamp, PrintJob? job = null, string printerId = "mk4") =>
        new() { PrinterId = printerId, State = state, Timestamp = timestamp, Job = job };

    private sealed class RecordingNotificationService : INotificationService
    {
        private readonly ConcurrentQueue<PrintNotification> _notifications = new();

        public Exception? ThrowOnce { get; set; }

        /// <summary>Runs after a notification was recorded.</summary>
        public Action<PrintNotification>? OnShow { get; set; }

        public Exception? ThrownException { get; private set; }

        public IReadOnlyList<PrintNotification> Notifications => [.. _notifications];

        public IReadOnlyList<PrintEventKind> Kinds => [.. _notifications.Select(static n => n.Kind)];

        public void Show(PrintNotification notification)
        {
            if (ThrowOnce is { } exception)
            {
                ThrowOnce = null;
                ThrownException = exception;
                throw exception;
            }

            _notifications.Enqueue(notification);
            OnShow?.Invoke(notification);
        }
    }

    /// <summary>Monitor that raises snapshots on demand, synchronously on the calling thread.</summary>
    private sealed class EventMonitor : IPrinterMonitor
    {
        public event EventHandler<PrinterSnapshot>? SnapshotUpdated;

        public int SubscriberCount => SnapshotUpdated?.GetInvocationList().Length ?? 0;

        public IReadOnlyList<PrinterSnapshot> Snapshots => [];

        public bool TryGetSnapshot(string printerId, [NotNullWhen(true)] out PrinterSnapshot? snapshot)
        {
            snapshot = null;
            return false;
        }

        public Task RunAsync(IReadOnlyList<PrinterDefinition> printers, TimeSpan pollingInterval, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Raise(PrinterSnapshot snapshot) => SnapshotUpdated?.Invoke(this, snapshot);

        /// <summary>Simulates a publish that started before the handler was removed (the monitor copies its handler list).</summary>
        public void RaiseIgnoringSubscription(PrintEventNotifier notifier, PrinterSnapshot snapshot) =>
            notifier.OnSnapshotUpdated(this, snapshot);
    }
}
