#if DEBUG
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using LayerPeek.App.Composition;
using LayerPeek.App.Notifications;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Notifications;

namespace LayerPeek.App.Tests.Notifications;

/// <summary>
/// T-133: the Debug-only stand-in for Windows' notification setting of the test app. While it pretends "off", the toast
/// service reads <see cref="ToastNotifierSetting.DisabledForApplication"/> instead of Windows' own setting (the settings
/// window then warns and sends no test toast); once cleared, Windows' own setting counts again. Only the test mode with
/// toasts gets it. Only the fake platform: nothing reaches Windows.
/// </summary>
public sealed class PretendedToastSettingTests : IDisposable
{
    private readonly FakeToastPlatform _platform = new();
    private readonly ListLogger<PretendedToastSetting> _pretendedLogger = new();
    private readonly PretendedToastSetting _pretended;
    private readonly ToastNotificationService _service;

    public PretendedToastSettingTests()
    {
        _pretended = new PretendedToastSetting(_pretendedLogger);
        _service = new ToastNotificationService(_platform, static _ => { }, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<ToastNotificationService>())
        {
            Pretended = _pretended,
        };
    }

    public void Dispose()
    {
        _service.Dispose();
        _platform.Session.Release();
    }

    [Fact]
    public void NothingIsPretendedAtFirst()
    {
        Assert.Null(_pretended.Value);
    }

    [Fact]
    public async Task APretendedOffIsReadInsteadOfWindowsAndNoTestToastIsShown()
    {
        _platform.Session.CurrentSetting = ToastNotifierSetting.Enabled;

        _pretended.Pretend(ToastNotifierSetting.DisabledForApplication);

        Assert.Equal(ToastNotifierSetting.DisabledForApplication, _pretended.Value);
        Assert.Equal(SystemNotificationState.TurnedOff, await _service.ReadStateAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TestNotificationOutcome.TurnedOff, await _service.ShowTestAsync(playSound: false, TestContext.Current.CancellationToken));
        Assert.Empty(_platform.Session.Shown);
        Assert.Contains(_pretendedLogger.Entries, static e => e.Message.Contains("pretended to be DisabledForApplication", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ClearedWindowsOwnSettingCountsAgain()
    {
        _pretended.Pretend(ToastNotifierSetting.DisabledForApplication);
        Assert.Equal(SystemNotificationState.TurnedOff, await _service.ReadStateAsync(TestContext.Current.CancellationToken));

        _pretended.Pretend(null);

        Assert.Null(_pretended.Value);
        Assert.Equal(SystemNotificationState.Allowed, await _service.ReadStateAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TestNotificationOutcome.Sent, await _service.ShowTestAsync(playSound: false, TestContext.Current.CancellationToken));
        Assert.Single(_platform.Session.Shown);
        Assert.Contains(_pretendedLogger.Entries, static e => e.Message.Contains("own notification setting of the app is read again", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutTheStandInWindowsOwnSettingIsRead()
    {
        using ToastNotificationService plain = new(_platform, static _ => { }, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<ToastNotificationService>());
        _pretended.Pretend(ToastNotifierSetting.DisabledForApplication);
        _platform.Session.CurrentSetting = ToastNotifierSetting.Enabled;

        Assert.Equal(SystemNotificationState.Allowed, await plain.ReadStateAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OnlyTheToastsOfTheTestModeGetTheStandIn()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Toasts exist only on Windows.");
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            await using (ServiceProvider test = AppServices.Create(environment: AppEnvironment.ForTests(folder, toasts: true)))
            {
                ToastNotificationService toasts = Assert.IsType<ToastNotificationService>(test.GetRequiredService<INotificationService>());
                Assert.Same(test.GetRequiredService<PretendedToastSetting>(), toasts.Pretended);
            }

            await using (ServiceProvider production = AppServices.Create(environment: AppEnvironment.Production))
            {
                ToastNotificationService toasts = Assert.IsType<ToastNotificationService>(production.GetRequiredService<INotificationService>());
                Assert.Null(toasts.Pretended);
            }
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }
}
#endif
