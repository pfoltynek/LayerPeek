using PrusaTrayDiagnostic.Core.Events;
using PrusaTrayDiagnostic.Core.Notifications;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.Core.Tests.Settings;

public class NotificationSettingsTests
{
    [Fact]
    public void DefaultRemainingTimeIsTenMinutes()
    {
        Assert.Equal(10, NotificationSettings.DefaultRemainingTimeMinutes);
        Assert.Equal(10, new NotificationSettings().RemainingTimeMinutes);
        Assert.Equal(10, AppSettings.Default.Notifications.RemainingTimeMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(NotificationSettings.MaxRemainingTimeMinutes)]
    public void RemainingTimeInsideAllowedRangeIsAccepted(int minutes)
    {
        Assert.Equal(minutes, new NotificationSettings { RemainingTimeMinutes = minutes }.RemainingTimeMinutes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(NotificationSettings.MaxRemainingTimeMinutes + 1)]
    public void RemainingTimeOutsideAllowedRangeIsRejected(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationSettings { RemainingTimeMinutes = minutes });
    }

    [Fact]
    public void EqualityIncludesRemainingTime()
    {
        Assert.NotEqual(new NotificationSettings(), new NotificationSettings { RemainingTimeMinutes = 5 });
        Assert.Equal(new NotificationSettings { RemainingTimeMinutes = 5 }, new NotificationSettings { RemainingTimeMinutes = 5 });
    }

    [Theory]
    [InlineData(PrintEventKind.PrintStarted)]
    [InlineData(PrintEventKind.PrintFinished)]
    [InlineData(PrintEventKind.PrintPaused)]
    [InlineData(PrintEventKind.PrintResumed)]
    [InlineData(PrintEventKind.PrintStopped)]
    [InlineData(PrintEventKind.Error)]
    [InlineData(PrintEventKind.AttentionRequired)]
    [InlineData(PrintEventKind.PrinterOffline)]
    [InlineData(PrintEventKind.PrinterOnline)]
    [InlineData(PrintEventKind.RemainingTime)]
    [InlineData(PrintEventKind.CredentialsRejected)]
    public void EveryEventIsEnabledByDefaultAndDisabledByMasterSwitch(PrintEventKind kind)
    {
        Assert.True(new NotificationSettings().IsEnabledFor(kind));
        Assert.False(new NotificationSettings { Enabled = false }.IsEnabledFor(kind));
    }

    [Theory]
    [InlineData(PrintEventKind.PrintStarted, nameof(NotificationSettings.PrintStarted))]
    [InlineData(PrintEventKind.PrintFinished, nameof(NotificationSettings.PrintFinished))]
    [InlineData(PrintEventKind.PrintPaused, nameof(NotificationSettings.PrintPaused))]
    [InlineData(PrintEventKind.PrintResumed, nameof(NotificationSettings.PrintResumed))]
    [InlineData(PrintEventKind.PrintStopped, nameof(NotificationSettings.PrintStopped))]
    [InlineData(PrintEventKind.Error, nameof(NotificationSettings.Error))]
    [InlineData(PrintEventKind.AttentionRequired, nameof(NotificationSettings.Attention))]
    [InlineData(PrintEventKind.CredentialsRejected, nameof(NotificationSettings.Attention))]
    [InlineData(PrintEventKind.PrinterOffline, nameof(NotificationSettings.Offline))]
    [InlineData(PrintEventKind.PrinterOnline, nameof(NotificationSettings.Offline))]
    [InlineData(PrintEventKind.RemainingTime, nameof(NotificationSettings.RemainingTimeMinutes))]
    public void EachEventHasItsOwnSwitch(PrintEventKind kind, string setting)
    {
        NotificationSettings disabled = setting switch
        {
            nameof(NotificationSettings.PrintStarted) => new NotificationSettings { PrintStarted = false },
            nameof(NotificationSettings.PrintFinished) => new NotificationSettings { PrintFinished = false },
            nameof(NotificationSettings.PrintPaused) => new NotificationSettings { PrintPaused = false },
            nameof(NotificationSettings.PrintResumed) => new NotificationSettings { PrintResumed = false },
            nameof(NotificationSettings.PrintStopped) => new NotificationSettings { PrintStopped = false },
            nameof(NotificationSettings.Error) => new NotificationSettings { Error = false },
            nameof(NotificationSettings.Attention) => new NotificationSettings { Attention = false },
            nameof(NotificationSettings.Offline) => new NotificationSettings { Offline = false },
            _ => new NotificationSettings { RemainingTimeMinutes = 0 },
        };

        Assert.False(disabled.IsEnabledFor(kind));
        foreach (PrintEventKind other in Enum.GetValues<PrintEventKind>())
        {
            bool sharesSwitch = (kind is PrintEventKind.PrinterOffline or PrintEventKind.PrinterOnline
                    && other is PrintEventKind.PrinterOffline or PrintEventKind.PrinterOnline)
                || (kind is PrintEventKind.AttentionRequired or PrintEventKind.CredentialsRejected
                    && other is PrintEventKind.AttentionRequired or PrintEventKind.CredentialsRejected);
            if (other != kind && !sharesSwitch)
            {
                Assert.True(disabled.IsEnabledFor(other), $"{other} must not depend on {setting}.");
            }
        }
    }

    [Fact]
    public void DefaultSoundIsImportantOnly()
    {
        Assert.Equal(NotificationSound.ImportantOnly, new NotificationSettings().Sound);
    }

    [Fact]
    public void UndefinedSoundIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationSettings { Sound = (NotificationSound)42 });
    }

    [Theory]
    [InlineData(NotificationSound.Off, NotificationSeverity.Information, false)]
    [InlineData(NotificationSound.Off, NotificationSeverity.Warning, false)]
    [InlineData(NotificationSound.Off, NotificationSeverity.ActionRequired, false)]
    [InlineData(NotificationSound.ImportantOnly, NotificationSeverity.Information, false)]
    [InlineData(NotificationSound.ImportantOnly, NotificationSeverity.Warning, false)]
    [InlineData(NotificationSound.ImportantOnly, NotificationSeverity.ActionRequired, true)]
    [InlineData(NotificationSound.All, NotificationSeverity.Information, true)]
    [InlineData(NotificationSound.All, NotificationSeverity.Warning, true)]
    [InlineData(NotificationSound.All, NotificationSeverity.ActionRequired, true)]
    public void ShouldPlaySoundFollowsSoundSetting(NotificationSound sound, NotificationSeverity severity, bool expected)
    {
        Assert.Equal(expected, new NotificationSettings { Sound = sound }.ShouldPlaySound(severity));
    }

    [Theory]
    [InlineData(PrintEventKind.Error, true)]
    [InlineData(PrintEventKind.AttentionRequired, true)]
    [InlineData(PrintEventKind.CredentialsRejected, true)]
    [InlineData(PrintEventKind.PrinterOffline, false)]
    [InlineData(PrintEventKind.PrintFinished, false)]
    public void ImportantEventsPlaySoundByDefault(PrintEventKind kind, bool expected)
    {
        PrintNotification notification = new()
        {
            Printer = new() { Id = "mk4", Name = "MK4", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" },
            Kind = kind,
            Timestamp = DateTimeOffset.UnixEpoch,
        };

        Assert.Equal(expected, new NotificationSettings().ShouldPlaySound(notification.Severity));
    }

    [Fact]
    public void UndefinedEventKindIsNotEnabled()
    {
        Assert.False(new NotificationSettings().IsEnabledFor((PrintEventKind)999));
    }
}
