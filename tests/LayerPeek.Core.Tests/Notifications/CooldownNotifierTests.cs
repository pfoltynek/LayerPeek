using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Events;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Tests.Monitoring;

using static LayerPeek.Core.Tests.Cooldown.CooldownTestData;

namespace LayerPeek.Core.Tests.Notifications;

/// <summary>
/// "Printer cooled down" notification (T-080): the events of a real <see cref="CooldownMonitor"/> fed by simulated
/// snapshots, filtered and given a sound by <see cref="NotificationSettings"/> like every other notification.
/// </summary>
public sealed class CooldownNotifierTests : IDisposable
{
    private const double End = 5;

    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(PollSeconds);

    private static readonly PrinterDefinition Mk4 = new() { Id = "mk4", Name = "Workshop MK4", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" };

    private static readonly NotificationSettings Enabled = new() { CooledDown = true };

    private readonly EventMonitor _monitor = new();
    private readonly CooldownMonitor _cooldown;
    private readonly RecordingNotificationService _service = new();
    private readonly ListLogger<CooldownNotifier> _logger = new();
    private readonly CooldownNotifier _notifier;

    public CooldownNotifierTests()
    {
        _cooldown = new CooldownMonitor(_monitor, new FakeTimeProvider(Start), new ListLogger<CooldownMonitor>());
        _notifier = new CooldownNotifier(_cooldown, _service, _logger);
    }

    public void Dispose()
    {
        _notifier.Dispose();
        _cooldown.Dispose();
    }

    [Fact]
    public void CooledDownIsNotifiedOncePerJobWithPrinterJobAndTemperatures()
    {
        Configure([Mk4], Enabled);
        Raise(Printing(0));

        RunCurve("mk4", End, End + 600);

        PrintNotification notification = Assert.Single(_service.Notifications);
        Assert.Same(Mk4, notification.Printer);
        Assert.Equal(PrintEventKind.CooledDown, notification.Kind);
        Assert.Equal(NotificationSeverity.Information, notification.Severity);
        Assert.Equal("benchy.bgcode", notification.FileName);
        Assert.Null(notification.Remaining);
        CooldownEvent cooled = Assert.IsType<CooldownEvent>(notification.Cooldown);
        Assert.Equal(CooldownPrintOutcome.Finished, cooled.Outcome);
        // T-109: both readings and thresholds.
        Assert.Equal(CooldownThresholds.Default, cooled.Thresholds);
        Assert.InRange(cooled.NozzleCelsius, Ambient, CooldownThreshold.DefaultNozzleCelsius);
        Assert.InRange(cooled.BedCelsius!.Value, Ambient, CooldownThreshold.DefaultBedCelsius);
        Assert.Equal(cooled.Timestamp, notification.Timestamp);
        Assert.Equal(CooldownPhase.CooledDown, _cooldown.GetState("mk4").Phase);
    }

    [Fact]
    public void StoppedPrintIsNotifiedWithItsOutcome()
    {
        Configure([Mk4], Enabled);
        Raise(Printing(0));

        for (double t = End; t <= End + 600; t += PollSeconds)
        {
            Raise(Cooling(PrinterState.Stopped, End, t));
        }

        PrintNotification notification = Assert.Single(_service.Notifications);
        Assert.Equal(CooldownPrintOutcome.Stopped, notification.Cooldown?.Outcome);
    }

    [Fact]
    public void NotificationIsOffByDefault()
    {
        Configure([Mk4], new NotificationSettings());
        Raise(Printing(0));

        RunCurve("mk4", End, End + 600);

        Assert.Equal(CooldownPhase.CooledDown, _cooldown.GetState("mk4").Phase);
        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void MasterSwitchSuppressesTheNotification()
    {
        Configure([Mk4], Enabled with { Enabled = false });
        Raise(Printing(0));

        RunCurve("mk4", End, End + 600);

        Assert.Equal(CooldownPhase.CooledDown, _cooldown.GetState("mk4").Phase);
        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void OtherSwitchesDoNotMatter()
    {
        NotificationSettings onlyCooledDown = new()
        {
            CooledDown = true,
            PrintStarted = false,
            PrintFinished = false,
            PrintPaused = false,
            PrintResumed = false,
            PrintStopped = false,
            Error = false,
            Attention = false,
            Offline = false,
            RemainingTimeMinutes = 0,
        };
        Configure([Mk4], onlyCooledDown);
        Raise(Printing(0));

        RunCurve("mk4", End, End + 600);

        Assert.Single(_service.Notifications);
    }

    [Fact]
    public void PrintEndedBelowTheThresholdNotifiesNothing()
    {
        Configure([Mk4], Enabled);
        Raise(Printing(0));
        Raise(Snap(PrinterState.Finished, End, 39, 0, job: Job()));
        Raise(Snap(PrinterState.Finished, End + 5, 38, 0, job: Job()));
        Raise(Snap(PrinterState.Finished, End + 10, 37, 0, job: Job()));

        Assert.Equal(CooldownPhase.CooledDown, _cooldown.GetState("mk4").Phase);
        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void PrintAlreadyEndedAtStartNotifiesNothing()
    {
        // User decision 11: the end of the print was not observed (app start), so the cool-down is not tracked at all.
        Configure([Mk4], Enabled);

        RunCurve("mk4", End, End + 600);

        Assert.Equal(CooldownPhase.None, _cooldown.GetState("mk4").Phase);
        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void ErrorEndsThePrintWithoutANotification()
    {
        Configure([Mk4], Enabled);
        Raise(Printing(0));
        Raise(Snap(PrinterState.Error, End, NozzlePrintTemperature, 0, job: Job()));

        for (double t = End + 5; t <= End + 600; t += PollSeconds)
        {
            Raise(Cooling(PrinterState.Error, End, t));
        }

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void EachPrintIsNotifiedOnce()
    {
        Configure([Mk4], Enabled);
        Raise(Printing(0));
        RunCurve("mk4", End, End + 600);
        PrintJob cube = Job(8, "cube.bgcode");
        Raise(Printing(700, job: cube));
        for (double t = 705; t <= 1300; t += PollSeconds)
        {
            Raise(Cooling(PrinterState.Finished, 705, t, job: cube));
        }

        Assert.Collection(
            _service.Notifications,
            first => Assert.Equal("benchy.bgcode", first.FileName),
            second => Assert.Equal("cube.bgcode", second.FileName));
    }

    [Theory]
    [InlineData(NotificationSound.Off, false)]
    [InlineData(NotificationSound.ImportantOnly, false)]
    [InlineData(NotificationSound.All, true)]
    public void SoundFollowsTheSettingLikeOtherInformationNotifications(NotificationSound sound, bool expected)
    {
        NotificationSettings settings = Enabled with { Sound = sound };
        Configure([Mk4], settings);
        Raise(Printing(0));

        RunCurve("mk4", End, End + 600);

        PrintNotification notification = Assert.Single(_service.Notifications);
        Assert.Equal(expected, notification.PlaySound);
        // The same rule as for a finished print, which is an information notification too.
        Assert.Equal(settings.ShouldPlaySound(PrintNotification.SeverityOf(PrintEventKind.PrintFinished)), notification.PlaySound);
    }

    [Fact]
    public void NothingIsNotifiedBeforeConfigure()
    {
        _cooldown.Configure([Mk4], Poll);
        Raise(Printing(0));

        RunCurve("mk4", End, End + 600);

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void PrinterThatIsNotConfiguredForNotificationsIsIgnored()
    {
        PrinterDefinition xl = Mk4 with { Id = "xl", Name = "Office XL", Address = "192.168.1.21" };
        _cooldown.Configure([Mk4, xl], Poll);
        _notifier.Configure([Mk4], Enabled);
        Raise(Printing(0, "xl"));

        RunCurve("xl", End, End + 600);

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void NotificationUsesTheLatestDefinitionOfThePrinter()
    {
        Configure([Mk4], Enabled);
        Raise(Printing(0));
        PrinterDefinition renamed = Mk4 with { Name = "Renamed MK4" };
        Configure([renamed], Enabled);

        RunCurve("mk4", End, End + 600);

        Assert.Same(renamed, Assert.Single(_service.Notifications).Printer);
    }

    [Fact]
    public void TurningTheSwitchOnWhileCoolingNotifiesAtTheEnd()
    {
        Configure([Mk4], new NotificationSettings());
        Raise(Printing(0));
        RunCurve("mk4", End, End + 30);
        Assert.Equal(CooldownPhase.Cooling, _cooldown.GetState("mk4").Phase);

        Configure([Mk4], Enabled);
        RunCurve("mk4", End, End + 600, from: End + 35);

        Assert.Single(_service.Notifications);
    }

    [Fact]
    public void ThresholdRaisedAboveTheNozzleNotifiesWithTheNewThreshold()
    {
        Configure([Mk4], Enabled);
        Raise(Printing(0));
        RunCurve("mk4", End, End + 90);
        Assert.Empty(_service.Notifications);

        // The nozzle is at about 70 °C. The notifier gets the settings first, as the monitoring session does.
        PrinterDefinition raised = Mk4 with { CooldownThresholds = new(80, 60) };
        Configure([raised], Enabled);

        PrintNotification notification = Assert.Single(_service.Notifications);
        Assert.Equal(80, notification.Cooldown?.NozzleThresholdCelsius);
        Assert.Equal(60, notification.Cooldown?.BedThresholdCelsius);
        Assert.Same(raised, notification.Printer);
    }

    [Fact]
    public void FailingNotificationServiceIsLoggedAndLaterNotificationsStillShow()
    {
        PrinterDefinition xl = Mk4 with { Id = "xl", Name = "Office XL", Address = "192.168.1.21" };
        Configure([Mk4, xl], Enabled);
        _service.ThrowOnce = new InvalidOperationException("toast failed");
        Raise(Printing(0));
        Raise(Printing(0, "xl"));

        RunCurve("mk4", End, End + 600);
        RunCurve("xl", End, End + 600);

        Assert.Equal("xl", Assert.Single(_service.Notifications).Printer.Id);
        (LogLevel level, Exception? exception, string message) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Same(_service.ThrownException, exception);
        Assert.Contains("mk4", message, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsNotifiedAfterDispose()
    {
        Configure([Mk4], Enabled);
        Raise(Printing(0));
        RunCurve("mk4", End, End + 30);

        _notifier.Dispose();
        RunCurve("mk4", End, End + 600, from: End + 35);

        Assert.Equal(CooldownPhase.CooledDown, _cooldown.GetState("mk4").Phase);
        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void ConfigureAfterDisposeIsIgnored()
    {
        _notifier.Dispose();

        _notifier.Configure([Mk4], Enabled);
        _cooldown.Configure([Mk4], Poll);
        Raise(Printing(0));
        RunCurve("mk4", End, End + 600);

        Assert.Empty(_service.Notifications);
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        Assert.Throws<ArgumentNullException>(() => new CooldownNotifier(null!, _service, _logger));
        Assert.Throws<ArgumentNullException>(() => new CooldownNotifier(_cooldown, null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new CooldownNotifier(_cooldown, _service, null!));
        Assert.Throws<ArgumentNullException>(() => _notifier.Configure(null!, Enabled));
        Assert.Throws<ArgumentNullException>(() => _notifier.Configure([Mk4], null!));
        Assert.Throws<ArgumentException>(() => _notifier.Configure([Mk4, null!], Enabled));
    }

    /// <summary>Both, as the monitoring session does on every run: the notifier first, then the cool-down monitor.</summary>
    private void Configure(IReadOnlyList<PrinterDefinition> printers, NotificationSettings settings)
    {
        _notifier.Configure(printers, settings);
        _cooldown.Configure(printers, Poll);
    }

    private void Raise(PrinterSnapshot snapshot) => _monitor.Raise(snapshot);

    private void RunCurve(string printerId, double end, double until, double? from = null)
    {
        for (double t = from ?? end; t <= until; t += PollSeconds)
        {
            Raise(Cooling(PrinterState.Finished, end, t, printerId));
        }
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        private readonly ConcurrentQueue<PrintNotification> _notifications = new();

        public Exception? ThrowOnce { get; set; }

        public Exception? ThrownException { get; private set; }

        public IReadOnlyList<PrintNotification> Notifications => [.. _notifications];

        public void Show(PrintNotification notification)
        {
            if (ThrowOnce is { } exception)
            {
                ThrowOnce = null;
                ThrownException = exception;
                throw exception;
            }

            _notifications.Enqueue(notification);
        }
    }

    /// <summary>Monitor that raises snapshots on demand, synchronously on the calling thread.</summary>
    private sealed class EventMonitor : IPrinterMonitor
    {
        public event EventHandler<PrinterSnapshot>? SnapshotUpdated;

        public IReadOnlyList<PrinterSnapshot> Snapshots => [];

        public bool TryGetSnapshot(string printerId, [NotNullWhen(true)] out PrinterSnapshot? snapshot)
        {
            snapshot = null;
            return false;
        }

        public Task RunAsync(IReadOnlyList<PrinterDefinition> printers, TimeSpan pollingInterval, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Raise(PrinterSnapshot snapshot) => SnapshotUpdated?.Invoke(this, snapshot);
    }
}
