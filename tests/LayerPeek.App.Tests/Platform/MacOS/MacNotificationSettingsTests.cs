using Microsoft.Extensions.Logging;

using LayerPeek.App.Notifications;
using LayerPeek.App.Platform.MacOS;
using LayerPeek.App.Tests.Notifications;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// The warning "turned off in macOS" and System Settings &gt; Notifications (T-020) on a fake notification center and a
/// stand-in opener: System Settings never opens in a test.
/// </summary>
public sealed class MacNotificationSettingsTests
{
    private readonly FakeUserNotificationCenter _center = new("io.github.pfoltynek.LayerPeek");
    private readonly List<Uri> _opened = [];
    private readonly ListLogger<MacNotificationSettings> _logger = new();

    [Fact]
    public void TheAddressIsTheNotificationPaneOfTheApp() =>
        Assert.Equal(
            "x-apple.systempreferences:com.apple.Notifications-Settings.extension?id=io.github.pfoltynek.LayerPeek",
            MacNotificationSettings.SettingsAddress("io.github.pfoltynek.LayerPeek").OriginalString);

    [Theory]
    [InlineData((int)NotificationPermission.Denied, true)]
    [InlineData((int)NotificationPermission.Allowed, false)]
    [InlineData((int)NotificationPermission.NotDetermined, false)]
    [InlineData((int)NotificationPermission.Unknown, false)]
    public async Task OnlyADeniedPermissionIsTurnedOff(int permission, bool turnedOff)
    {
        _center.Permission = (NotificationPermission)permission;

        Assert.Equal(turnedOff, await Settings().AreTurnedOffAsync(TestContext.Current.CancellationToken));
        Assert.Equal(turnedOff, _logger.Entries.Any(static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("turned off in macOS", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AnUnreadablePermissionIsNotTurnedOffAndIsLogged()
    {
        _center.PermissionFailure = new TimeoutException("no reply");

        Assert.False(await Settings().AreTurnedOffAsync(TestContext.Current.CancellationToken));
        Assert.Contains(_logger.Entries, static entry => entry.Exception is TimeoutException);
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
        new MacNotificationSettings(_center, static _ => throw new InvalidOperationException("refused"), _logger).Open();

        Assert.Contains(_logger.Entries, static entry => entry.Exception is InvalidOperationException);
    }

    private MacNotificationSettings Settings() => new(_center, _opened.Add, _logger);
}
