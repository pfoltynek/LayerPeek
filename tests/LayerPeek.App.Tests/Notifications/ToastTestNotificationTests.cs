using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;

using LayerPeek.App.Notifications;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Events;
using LayerPeek.Core.Notifications;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Notifications;

/// <summary>
/// T-101 on Windows: the settings window reads <c>ToastNotifier.Setting</c> and shows the test toast through the toast
/// service of T-010, on its thread, with the fixed tag; Settings &gt; System &gt; Notifications through a stand-in opener.
/// Only the fake platform: nothing reaches Windows.
/// </summary>
public sealed class ToastTestNotificationTests : IDisposable
{
    private readonly FakeToastPlatform _platform = new();
    private readonly ConcurrentQueue<string?> _activations = new();
    private readonly ListLogger<ToastNotificationService> _logger = new();
    private readonly ToastNotificationService _service;

    public ToastTestNotificationTests() =>
        _service = new ToastNotificationService(_platform, _activations.Enqueue, static () => CultureInfo.GetCultureInfo("de-DE"), _logger);

    public void Dispose()
    {
        _service.Dispose();
        _platform.Session.Release();
    }

    [Theory]
    [InlineData((int)ToastNotifierSetting.Enabled, (int)SystemNotificationState.Allowed)]
    [InlineData((int)ToastNotifierSetting.DisabledForApplication, (int)SystemNotificationState.TurnedOff)]
    [InlineData((int)ToastNotifierSetting.DisabledForUser, (int)SystemNotificationState.TurnedOff)]
    [InlineData((int)ToastNotifierSetting.DisabledByGroupPolicy, (int)SystemNotificationState.BlockedByPolicy)]
    [InlineData((int)ToastNotifierSetting.DisabledByManifest, (int)SystemNotificationState.Unknown)]
    [InlineData(99, (int)SystemNotificationState.Unknown)]
    public async Task TheSettingOfWindowsIsReadOnTheToastThread(int setting, int state)
    {
        _platform.Session.CurrentSetting = (ToastNotifierSetting)setting;

        Assert.Equal((SystemNotificationState)state, await _service.ReadStateAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, _platform.OpenCount);
        Assert.NotEqual(Environment.CurrentManagedThreadId, _platform.Session.SettingThreadId);
        Assert.Equal(_platform.OpenThreadId, _platform.Session.SettingThreadId);
        Assert.Empty(_platform.Session.Shown);
    }

    [Fact]
    public async Task AnUnreadableSettingIsUnknownAndIsLogged()
    {
        _platform.Session.SettingFailure = new InvalidOperationException("no setting");

        Assert.Equal(SystemNotificationState.Unknown, await _service.ReadStateAsync(TestContext.Current.CancellationToken));
        Assert.Contains(_logger.Entries, static e => e.Exception is InvalidOperationException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheTestToastIsShownLikeTheOthersWithTheFixedTag(bool playSound)
    {
        Assert.Equal(TestNotificationOutcome.Sent, await _service.ShowTestAsync(playSound, TestContext.Current.CancellationToken));

        string xml = Assert.Single(_platform.Session.Shown);
        Assert.Equal([TestNotification.Identifier], _platform.Session.ShownTags);
        Assert.Equal(_platform.OpenThreadId, _platform.Session.ShowThreadId);
        XElement toast = XElement.Parse(xml);
        Assert.Equal(ToastLaunchArgument.Settings, (string?)toast.Attribute("launch"));
        Assert.Equal("foreground", (string?)toast.Attribute("activationType"));
        Assert.Equal(["Testbenachrichtigung", "Benachrichtigungen von LayerPeek funktionieren. So sehen Druckerereignisse aus."], toast.Descendants("text").Select(static text => text.Value));
        Assert.Equal(!playSound, toast.Element("audio")?.Attribute("silent")?.Value == "true");
    }

    [Fact]
    public async Task EveryTestToastHasTheSameTagSoWindowsReplacesThePreviousOne()
    {
        await _service.ShowTestAsync(playSound: true, TestContext.Current.CancellationToken);
        await _service.ShowTestAsync(playSound: true, TestContext.Current.CancellationToken);
        _service.Show(new PrintNotification { Printer = Definition(), Kind = PrintEventKind.PrintFinished, Timestamp = Now });
        await _platform.Session.WaitForShownAsync(3);

        // Printer toasts keep no tag: they never replace each other or the test.
        Assert.Equal([TestNotification.Identifier, TestNotification.Identifier, null], _platform.Session.ShownTags);
    }

    [Theory]
    [InlineData((int)ToastNotifierSetting.DisabledForApplication, (int)TestNotificationOutcome.TurnedOff)]
    [InlineData((int)ToastNotifierSetting.DisabledForUser, (int)TestNotificationOutcome.TurnedOff)]
    [InlineData((int)ToastNotifierSetting.DisabledByGroupPolicy, (int)TestNotificationOutcome.BlockedByPolicy)]
    public async Task WindowsWouldDropTheToastSoItIsNotShown(int setting, int outcome)
    {
        _platform.Session.CurrentSetting = (ToastNotifierSetting)setting;

        Assert.Equal((TestNotificationOutcome)outcome, await _service.ShowTestAsync(playSound: true, TestContext.Current.CancellationToken));
        Assert.Empty(_platform.Session.Shown);
    }

    [Fact]
    public async Task AnUnreadableSettingStillShowsTheToast()
    {
        _platform.Session.SettingFailure = new InvalidOperationException("no setting");

        Assert.Equal(TestNotificationOutcome.Sent, await _service.ShowTestAsync(playSound: false, TestContext.Current.CancellationToken));
        Assert.Single(_platform.Session.Shown);
    }

    [Fact]
    public async Task AFailingToastIsAFailureAndIsLogged()
    {
        _platform.Session.FailNext = new InvalidOperationException("refused");

        Assert.Equal(TestNotificationOutcome.Failed, await _service.ShowTestAsync(playSound: true, TestContext.Current.CancellationToken));
        Assert.Contains(_logger.Entries, static e => e.Exception is InvalidOperationException && e.Message.Contains("test notification", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnavailableToastsAreUnknownAndFailed()
    {
        _platform.OpenFailure = new InvalidOperationException("no WinRT");

        Assert.Equal(SystemNotificationState.Unknown, await _service.ReadStateAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TestNotificationOutcome.Failed, await _service.ShowTestAsync(playSound: true, TestContext.Current.CancellationToken));
        Assert.Equal(1, _platform.OpenCount);
    }

    [Fact]
    public async Task ADisposedServiceAnswersAtOnce()
    {
        _service.Dispose();

        Assert.Equal(SystemNotificationState.Unknown, await _service.ReadStateAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TestNotificationOutcome.Failed, await _service.ShowTestAsync(playSound: true, TestContext.Current.CancellationToken));
        Assert.Equal(0, _platform.OpenCount);
    }

    [Fact]
    public async Task ARequestWaitingWhenTheServiceIsDisposedEnds()
    {
        // The thread is busy with a notification.
        _platform.Session.Block();
        _service.Show(new PrintNotification { Printer = Definition(), Kind = PrintEventKind.PrintFinished, Timestamp = Now });
        await _platform.Session.WaitUntilShowEnteredAsync();
        Task<TestNotificationOutcome> waiting = _service.ShowTestAsync(playSound: true, TestContext.Current.CancellationToken);

        _service.Dispose();

        Assert.Equal(TestNotificationOutcome.Failed, await waiting.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AClosedWindowStopsWaitingForTheThread()
    {
        _platform.Session.Block();
        _service.Show(new PrintNotification { Printer = Definition(), Kind = PrintEventKind.PrintFinished, Timestamp = Now });
        await _platform.Session.WaitUntilShowEnteredAsync();
        using CancellationTokenSource closed = new();
        Task<SystemNotificationState> waiting = _service.ReadStateAsync(closed.Token);

        closed.Cancel();

        Assert.Equal(SystemNotificationState.Unknown, await waiting.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheSettingsOfWindowsUseTheToastServiceAndNeverAsk()
    {
        List<Uri> opened = [];
        ToastNotificationSettings settings = new(_service, opened.Add, new ListLogger<ToastNotificationSettings>());

        Assert.True(await settings.RequestPermissionAsync(TestContext.Current.CancellationToken));
        Assert.Equal(SystemNotificationState.Allowed, await settings.ReadStateAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TestNotificationOutcome.Sent, await settings.SendTestAsync(playSound: true, TestContext.Current.CancellationToken));
        Assert.Single(_platform.Session.Shown);

        await settings.OpenAsync();
        Assert.Equal([new Uri("ms-settings:notifications")], opened);
    }

    [Fact]
    public async Task AFailedOpenOfSettingsIsOnlyLogged()
    {
        ListLogger<ToastNotificationSettings> logger = new();
        ToastNotificationSettings settings = new(_service, static _ => throw new InvalidOperationException("no Settings"), logger);

        await settings.OpenAsync();

        Assert.Contains(logger.Entries, static e => e.Exception is InvalidOperationException);
    }

    [Fact]
    public void TheTestToastIsInTheGivenLanguageAndPlaysTheDefaultSound()
    {
        string xml = ToastContentBuilder.BuildTest(CultureInfo.GetCultureInfo("fr-FR"), playSound: true);

        XElement toast = XElement.Parse(xml);
        Assert.Equal(["Notification de test", "Les notifications de LayerPeek fonctionnent. Les événements des imprimantes s'afficheront ainsi."], toast.Descendants("text").Select(static text => text.Value));
        // With the sound: no audio element (Windows plays the default sound).
        Assert.Null(toast.Element("audio"));
    }
}
