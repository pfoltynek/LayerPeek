using System.Globalization;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Events;
using LayerPeek.Core.Notifications;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Notifications;

/// <summary>
/// The macOS notification service (T-020) on a fake notification center: the texts, sound and click argument of the Windows
/// toasts, permission asked with the first notification, a denial logged once per change. Nothing reaches macOS.
/// </summary>
public sealed class UserNotificationServiceTests : IDisposable
{
    private readonly FakeUserNotificationCenter _center = new();
    private readonly ListLogger<UserNotificationService> _logger = new();
    private UserNotificationService? _service;

    private UserNotificationService Service => _service ??= Create(static () => CultureInfo.GetCultureInfo("en-US"));

    public void Dispose() => _service?.Dispose();

    [Fact]
    public async Task ANotificationCarriesTheTextsSoundAndClickArgumentOfTheToast()
    {
        Service.Show(Notification(PrintEventKind.PrintFinished) with { FileName = "benchy.bgcode", PlaySound = true });
        Service.Show(Notification(PrintEventKind.PrintPaused) with { PlaySound = false });

        await Waiting.ForAsync(() => _center.Posted.Count == 2);
        UserNotificationRequest finished = _center.Posted[0];
        Assert.Equal("MK4", finished.Title);
        Assert.Equal("Print finished\nbenchy.bgcode", finished.Body);
        Assert.True(finished.PlaySound);
        Assert.Equal(ToastLaunchArgument.ForPrinter("mk4"), finished.LaunchArgument);
        UserNotificationRequest paused = _center.Posted[1];
        Assert.Equal("Print paused", paused.Body);
        Assert.False(paused.PlaySound);
        Assert.NotEqual(finished.Identifier, paused.Identifier);
    }

    [Fact]
    public async Task TheTextsAreThoseOfTheToastBuilder()
    {
        CultureInfo czech = CultureInfo.GetCultureInfo("cs-CZ");
        PrintNotification[] notifications =
        [
            Notification(PrintEventKind.RemainingTime) with { Remaining = TimeSpan.FromMinutes(10) },
            Notification(PrintEventKind.Error) with { Printer = Definition("p2", "\u0001") },
            new()
            {
                Printer = Definition(),
                Kind = PrintEventKind.CooledDown,
                Timestamp = Now,
                Cooldown = new CooldownEvent("mk4", Now, "benchy.bgcode", CooldownPrintOutcome.Finished, 39.6, 40),
            },
        ];
        _service = Create(() => czech);
        foreach (PrintNotification notification in notifications)
        {
            _service.Show(notification);
        }

        await Waiting.ForAsync(() => _center.Posted.Count == notifications.Length);
        for (int i = 0; i < notifications.Length; i++)
        {
            NotificationLines lines = ToastContentBuilder.Lines(notifications[i], czech);
            Assert.Equal(lines.Title, _center.Posted[i].Title);
            Assert.Equal(lines.Detail is null ? lines.Body : lines.Body + "\n" + lines.Detail, _center.Posted[i].Body);
        }

        // A name with nothing showable falls back to the id, like the toast title.
        Assert.Equal("p2", _center.Posted[1].Title);
    }

    [Fact]
    public async Task TheLanguageIsReadForEveryNotification()
    {
        string language = "en-US";
        _service = Create(() => CultureInfo.GetCultureInfo(language));
        _service.Show(Notification(PrintEventKind.PrintFinished));
        await Waiting.ForAsync(() => _center.Posted.Count == 1);
        language = "de-DE";
        _service.Show(Notification(PrintEventKind.PrintFinished));

        await Waiting.ForAsync(() => _center.Posted.Count == 2);
        Assert.Equal(ToastContentBuilder.EventText(PrintEventKind.PrintFinished, null, CultureInfo.GetCultureInfo("en-US")), _center.Posted[0].Body);
        Assert.Equal(ToastContentBuilder.EventText(PrintEventKind.PrintFinished, null, CultureInfo.GetCultureInfo("de-DE")), _center.Posted[1].Body);
        Assert.NotEqual(_center.Posted[0].Body, _center.Posted[1].Body);
    }

    [Fact]
    public async Task NothingIsAskedBeforeTheFirstNotification()
    {
        _ = Service;
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(0, _center.AuthorizationRequests);

        Service.Show(Notification(PrintEventKind.PrintFinished));
        await Waiting.ForAsync(() => _center.Posted.Count == 1);
        Assert.Equal(1, _center.AuthorizationRequests);
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Information && entry.Message.Contains("allowed in macOS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADeniedPermissionDropsTheNotificationsAndIsLoggedOncePerChange()
    {
        _center.Allowed = false;
        Service.Show(Notification(PrintEventKind.PrintStarted));
        Service.Show(Notification(PrintEventKind.PrintPaused));
        await Waiting.ForAsync(() => _center.AuthorizationRequests == 2);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(_center.Posted);
        Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("turned off in macOS", StringComparison.Ordinal));

        // Allowed in System Settings: the next notification is shown without a restart.
        _center.Allowed = true;
        Service.Show(Notification(PrintEventKind.PrintResumed));
        await Waiting.ForAsync(() => _center.Posted.Count == 1);
        Assert.Equal("Print resumed", _center.Posted[0].Body);

        _center.Allowed = false;
        Service.Show(Notification(PrintEventKind.PrintStopped));
        await Waiting.ForAsync(() => _center.AuthorizationRequests == 4);
        await Waiting.ForAsync(() => _logger.Entries.Count(static entry => entry.Message.Contains("turned off in macOS", StringComparison.Ordinal)) == 2);
        Assert.Single(_center.Posted);
    }

    [Fact]
    public async Task NotificationsWaitForTheAnswerOfTheUser()
    {
        _center.AuthorizationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Service.Show(Notification(PrintEventKind.PrintStarted));
        await Waiting.ForAsync(() => _center.AuthorizationRequests == 1);
        Service.Show(Notification(PrintEventKind.PrintFinished));
        Assert.Empty(_center.Posted);

        _center.AuthorizationGate.SetResult();
        await Waiting.ForAsync(() => _center.Posted.Count == 2);
        Assert.Equal(["Print started", "Print finished"], _center.Posted.Select(static posted => posted.Body));
    }

    [Fact]
    public async Task AFailedPermissionRequestDropsOnlyThatNotification()
    {
        _center.AuthorizationFailure = new InvalidOperationException("no reply");
        Service.Show(Notification(PrintEventKind.PrintStarted));
        await Waiting.ForAsync(() => _logger.Entries.Any(static entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException));

        _center.AuthorizationFailure = null;
        Service.Show(Notification(PrintEventKind.PrintFinished));
        await Waiting.ForAsync(() => _center.Posted.Count == 1);
        Assert.Equal("Print finished", _center.Posted[0].Body);
    }

    [Fact]
    public async Task AFailedPostIsLoggedAndTheNextIsShown()
    {
        _center.FailNextPost = new InvalidOperationException("refused");
        Service.Show(Notification(PrintEventKind.Error));
        Service.Show(Notification(PrintEventKind.PrinterOffline));

        await Waiting.ForAsync(() => _center.Posted.Count == 1);
        Assert.Equal("The printer is offline", _center.Posted[0].Body);
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Error && entry.Message.Contains("Showing the Error notification", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATooLongIdIsShownWithoutAClickTarget()
    {
        Service.Show(Notification(PrintEventKind.PrintFinished) with { Printer = Definition(new string('x', ToastLaunchArgument.MaxLength + 1), "Long") });

        await Waiting.ForAsync(() => _center.Posted.Count == 1);
        Assert.Null(_center.Posted[0].LaunchArgument);
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("opens nothing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFullQueueDropsNotificationsWithAWarning()
    {
        _center.AuthorizationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Service.Show(Notification(PrintEventKind.PrintStarted));
        await Waiting.ForAsync(() => _center.AuthorizationRequests == 1);
        for (int i = 0; i < UserNotificationService.MaxPending + 3; i++)
        {
            Service.Show(Notification(PrintEventKind.PrintPaused));
        }

        Assert.Equal(3, _logger.Entries.Count(static entry => entry.Message.Contains("dropped the PrintPaused", StringComparison.Ordinal)));
        _center.AuthorizationGate.SetResult();
        await Waiting.ForAsync(() => _center.Posted.Count == UserNotificationService.MaxPending + 1);
    }

    [Fact]
    public async Task AfterDisposeNothingIsShownAndCompletionEnds()
    {
        UserNotificationService service = Service;
        Assert.False(service.Completion.IsCompleted);
        service.Dispose();
        service.Show(Notification(PrintEventKind.PrintFinished));

        await service.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        Assert.Empty(_center.Posted);
        Assert.Equal(0, _center.AuthorizationRequests);
        service.Dispose();
    }

    [Fact]
    public async Task DisposeEndsAWaitForTheUser()
    {
        _center.AuthorizationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Service.Show(Notification(PrintEventKind.PrintStarted));
        await Waiting.ForAsync(() => _center.AuthorizationRequests == 1);

        Service.Dispose();
        await Service.Completion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        _center.AuthorizationGate.SetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(_center.Posted);
    }

    [Fact]
    public async Task SettingsReachTheNextNotificationThroughTheNotifier()
    {
        // The sound comes from notifications.sound via PrintNotification.PlaySound (T-010); the service only passes it on.
        foreach (bool sound in (ReadOnlySpan<bool>)[true, false, true])
        {
            Service.Show(Notification(PrintEventKind.AttentionRequired) with { PlaySound = sound });
        }

        await Waiting.ForAsync(() => _center.Posted.Count == 3);
        Assert.Equal([true, false, true], _center.Posted.Select(static posted => posted.PlaySound));
    }

    private UserNotificationService Create(Func<CultureInfo> culture) => new(_center, culture, _logger);

    private static PrintNotification Notification(PrintEventKind kind) => new()
    {
        Printer = Definition(),
        Kind = kind,
        Timestamp = Now,
    };
}
