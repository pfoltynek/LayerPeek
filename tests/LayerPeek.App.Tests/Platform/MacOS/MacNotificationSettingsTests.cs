using System.Globalization;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Notifications;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.MacOS;
using LayerPeek.App.Tests.Notifications;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// The warning "turned off in macOS" and System Settings &gt; Notifications (T-020), the question of macOS and the test
/// notification (T-101) on a fake notification center and a stand-in opener: System Settings never opens in a test, no
/// permission is asked of macOS and nothing reaches its notification center.
/// </summary>
public sealed class MacNotificationSettingsTests
{
    private readonly FakeUserNotificationCenter _center = new("io.github.pfoltynek.LayerPeek");
    private readonly List<Uri> _opened = [];
    private readonly ListLogger<MacNotificationSettings> _logger = new();
    private CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void TheAddressIsTheNotificationPaneOfTheApp() =>
        Assert.Equal(
            "x-apple.systempreferences:com.apple.Notifications-Settings.extension?id=io.github.pfoltynek.LayerPeek",
            MacNotificationSettings.SettingsAddress("io.github.pfoltynek.LayerPeek").OriginalString);

    [Theory]
    [InlineData((int)NotificationPermission.Denied, (int)SystemNotificationState.TurnedOff)]
    [InlineData((int)NotificationPermission.Allowed, (int)SystemNotificationState.Allowed)]
    [InlineData((int)NotificationPermission.NotDetermined, (int)SystemNotificationState.NotAsked)]
    [InlineData((int)NotificationPermission.Unknown, (int)SystemNotificationState.Unknown)]
    public async Task ThePermissionIsTheStateOfTheWindow(int permission, int state)
    {
        _center.Permission = (NotificationPermission)permission;

        Assert.Equal((SystemNotificationState)state, await Settings().ReadStateAsync(TestContext.Current.CancellationToken));
        bool turnedOff = (SystemNotificationState)state == SystemNotificationState.TurnedOff;
        Assert.Equal(turnedOff, _logger.Entries.Any(static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("turned off in macOS", StringComparison.Ordinal)));
        // Reading never asks.
        Assert.Equal(0, _center.AuthorizationRequests);
    }

    [Fact]
    public async Task AnUnreadablePermissionIsUnknownAndIsLogged()
    {
        _center.PermissionFailure = new TimeoutException("no reply");

        Assert.Equal(SystemNotificationState.Unknown, await Settings().ReadStateAsync(TestContext.Current.CancellationToken));
        Assert.Contains(_logger.Entries, static entry => entry.Exception is TimeoutException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequestingPermissionAsksTheCenterAndReturnsItsAnswer(bool allowed)
    {
        _center.Allowed = allowed;

        Assert.Equal(allowed, await Settings().RequestPermissionAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, _center.AuthorizationRequests);
    }

    [Fact]
    public async Task AFailedRequestIsNotAllowedAndIsLogged()
    {
        // E.g. the user did not answer within the 10 minutes of the center.
        _center.AuthorizationFailure = new TimeoutException("no answer");

        Assert.False(await Settings().RequestPermissionAsync(TestContext.Current.CancellationToken));
        Assert.Contains(_logger.Entries, static entry => entry.Exception is TimeoutException);
    }

    [Fact]
    public async Task ARequestCancelledWhileTheUserThinksIsNotAllowed()
    {
        _center.AuthorizationGate = new TaskCompletionSource();
        using CancellationTokenSource closed = new();
        Task<bool> request = Settings().RequestPermissionAsync(closed.Token);

        await Waiting.ForAsync(() => _center.AuthorizationRequests == 1);
        closed.Cancel();

        Assert.False(await request.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAllowedTestIsPostedWithTheFixedIdentifierTheSoundAndTheSettingsArgument(bool playSound)
    {
        _culture = CultureInfo.GetCultureInfo("de");

        Assert.Equal(TestNotificationOutcome.Sent, await Settings().SendTestAsync(playSound, TestContext.Current.CancellationToken));

        UserNotificationRequest posted = Assert.Single(_center.Posted);
        Assert.Equal(TestNotification.Identifier, posted.Identifier);
        Assert.Equal("Testbenachrichtigung", posted.Title);
        Assert.Equal("Benachrichtigungen von LayerPeek funktionieren. So sehen Druckerereignisse aus.", posted.Body);
        Assert.Equal(playSound, posted.PlaySound);
        Assert.Equal(ToastLaunchArgument.Settings, posted.LaunchArgument);
        // Sending never asks: the window asks first when macOS has not asked yet.
        Assert.Equal(0, _center.AuthorizationRequests);
    }

    [Fact]
    public async Task EveryTestUsesTheSameIdentifierSoMacOSReplacesThePreviousOne()
    {
        MacNotificationSettings settings = Settings();

        await settings.SendTestAsync(playSound: true, TestContext.Current.CancellationToken);
        await settings.SendTestAsync(playSound: true, TestContext.Current.CancellationToken);

        Assert.Equal([TestNotification.Identifier, TestNotification.Identifier], _center.Posted.Select(static request => request.Identifier));
    }

    [Theory]
    [InlineData((int)NotificationPermission.Denied, (int)TestNotificationOutcome.TurnedOff)]
    [InlineData((int)NotificationPermission.NotDetermined, (int)TestNotificationOutcome.Failed)]
    [InlineData((int)NotificationPermission.Unknown, (int)TestNotificationOutcome.Failed)]
    public async Task WithoutPermissionNothingIsPosted(int permission, int outcome)
    {
        _center.Permission = (NotificationPermission)permission;

        Assert.Equal((TestNotificationOutcome)outcome, await Settings().SendTestAsync(playSound: true, TestContext.Current.CancellationToken));
        Assert.Empty(_center.Posted);
        Assert.Equal(0, _center.AuthorizationRequests);
    }

    [Fact]
    public async Task AFailedPostIsAFailureAndIsLogged()
    {
        _center.FailNextPost = new InvalidOperationException("refused");

        Assert.Equal(TestNotificationOutcome.Failed, await Settings().SendTestAsync(playSound: false, TestContext.Current.CancellationToken));
        Assert.Contains(_logger.Entries, static entry => entry.Exception is InvalidOperationException);
    }

    [Fact]
    public void OpenOpensTheNotificationPaneOfTheBundle()
    {
        Settings().Open();

        Assert.Equal([MacNotificationSettings.SettingsAddress("io.github.pfoltynek.LayerPeek")], _opened);
    }

    [Fact]
    public void AFailedOpenIsOnlyLogged()
    {
        new MacNotificationSettings(_center, static _ => throw new InvalidOperationException("refused"), static () => CultureInfo.InvariantCulture, _logger).Open();

        Assert.Contains(_logger.Entries, static entry => entry.Exception is InvalidOperationException);
    }

    private MacNotificationSettings Settings() => new(_center, _opened.Add, () => _culture, _logger);
}
