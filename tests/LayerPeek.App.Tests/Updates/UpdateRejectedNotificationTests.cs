using System.Globalization;
using System.Xml.Linq;

using LayerPeek.App.Notifications;
using LayerPeek.App.Settings;
using LayerPeek.App.SingleInstance;
using LayerPeek.App.Tests.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.Core.Notifications;

namespace LayerPeek.App.Tests.Updates;

/// <summary>
/// The notification about a refused update (T-181, decision 5) on both OS: the texts in the language of the app (never the
/// version the refused feed names, decision 2026-11-11), its own fixed identifier (a later refusal replaces it, never the offer
/// of a good update), and the launch argument <see cref="ToastLaunchArgument.UpdateSettings"/>, whose click opens the Startup
/// and updates tab. The withdrawal of the offer's notification (decision 2026-11-11) by its identifier, in the order of the
/// queue. Only fakes: nothing reaches Windows or macOS.
/// </summary>
public sealed class UpdateRejectedNotificationTests
{
    [Fact]
    public void TheLaunchArgumentOpensTheStartupAndUpdatesTab()
    {
        Assert.Equal("settings=updates", ToastLaunchArgument.UpdateSettings);
        Assert.True(ToastLaunchArgument.IsWellFormed(ToastLaunchArgument.UpdateSettings));
        Assert.True(ToastLaunchArgument.IsSettings(ToastLaunchArgument.UpdateSettings));
        Assert.False(ToastLaunchArgument.IsUpdate(ToastLaunchArgument.UpdateSettings));
        Assert.True(ToastLaunchArgument.TryGetSettingsPage(ToastLaunchArgument.UpdateSettings, out SettingsPage page));
        Assert.Equal(SettingsPage.StartupUpdates, page);
        Assert.False(ToastLaunchArgument.TryGetSettingsPage("settings=updates2", out _));
        // A printer id can never produce it.
        Assert.False(ToastLaunchArgument.IsSettings(ToastLaunchArgument.ForPrinter("settings=updates")));
        Assert.True(InstanceCommand.TryForToast(ToastLaunchArgument.UpdateSettings, out InstanceCommand command));
        Assert.Equal(InstanceCommandKind.Toast, command.Kind);
        Assert.NotEqual(UpdateNotificationText.Identifier, UpdateNotificationText.RejectedIdentifier);
    }

    [Fact]
    public void TheToastCarriesTheTextsTheArgumentAndTheSound()
    {
        XDocument silent = XDocument.Parse(ToastContentBuilder.BuildUpdateRejected(new UpdateRejectedNotification(PlaySound: false), CultureInfo.GetCultureInfo("de-DE")));
        XDocument loud = XDocument.Parse(ToastContentBuilder.BuildUpdateRejected(new UpdateRejectedNotification(PlaySound: true), CultureInfo.GetCultureInfo("en-US")));

        Assert.Equal(ToastLaunchArgument.UpdateSettings, silent.Root!.Attribute("launch")?.Value);
        Assert.Equal("foreground", silent.Root.Attribute("activationType")?.Value);
        Assert.Equal(
            ["LayerPeek-Update abgelehnt", "Ein Update wurde nicht installiert, weil seine Signatur nicht überprüft werden konnte. Für Details hier klicken."],
            silent.Descendants("text").Select(static text => text.Value));
        Assert.Equal("true", silent.Descendants("audio").Single().Attribute("silent")?.Value);
        Assert.Empty(loud.Descendants("audio"));
        Assert.Equal(
            ["LayerPeek update refused", "An update was not installed because its signature could not be verified. Click for details."],
            loud.Descendants("text").Select(static text => text.Value));
    }

    [Fact]
    public async Task TheWindowsToastHasItsOwnTag()
    {
        FakeToastPlatform platform = new();
        using ToastNotificationService service = new(platform, static _ => { }, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<ToastNotificationService>());

        service.ShowUpdateRejected(new UpdateRejectedNotification(PlaySound: false));
        await Waiting.ForAsync(() => platform.Session.Shown.Count == 1);

        Assert.Equal([UpdateNotificationText.RejectedIdentifier], platform.Session.ShownTags);
        Assert.Contains("settings=updates", platform.Session.Shown[0], StringComparison.Ordinal);
        platform.Session.Release();
    }

    [Fact]
    public async Task TheMacNotificationHasItsOwnIdentifierAndArgument()
    {
        FakeUserNotificationCenter center = new();
        using UserNotificationService service = new(center, static () => CultureInfo.GetCultureInfo("cs-CZ"), new ListLogger<UserNotificationService>());

        service.ShowUpdateRejected(new UpdateRejectedNotification(PlaySound: true));
        await Waiting.ForAsync(() => center.Posted.Count == 1);

        UserNotificationRequest request = center.Posted[0];
        Assert.Equal(UpdateNotificationText.RejectedIdentifier, request.Identifier);
        Assert.Equal("Aktualizace LayerPeeku odmítnuta", request.Title);
        Assert.Equal("Aktualizace se nenainstalovala, protože nešlo ověřit její podpis. Klikněte pro podrobnosti.", request.Body);
        Assert.True(request.PlaySound);
        Assert.Equal(ToastLaunchArgument.UpdateSettings, request.LaunchArgument);
    }

    [Fact]
    public async Task TheWindowsToastOfTheOfferIsWithdrawnByItsTagAfterTheToastsBeforeIt()
    {
        FakeToastPlatform platform = new();
        using ToastNotificationService service = new(platform, static _ => { }, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<ToastNotificationService>());
        platform.Session.Block();

        service.ShowUpdateAvailable(new UpdateNotification("0.5.0", PlaySound: false));
        service.WithdrawUpdateAvailable();
        service.ShowUpdateRejected(new UpdateRejectedNotification(PlaySound: false));
        await platform.Session.WaitUntilShowEnteredAsync();
        // The offer is still being shown: the withdrawal waits for it.
        Assert.Empty(platform.Session.Removed);
        platform.Session.Release();
        await Waiting.ForAsync(() => platform.Session.Shown.Count == 2);

        Assert.Equal([UpdateNotificationText.Identifier], platform.Session.Removed);
        Assert.Equal([UpdateNotificationText.Identifier, UpdateNotificationText.RejectedIdentifier], platform.Session.ShownTags);
    }

    [Fact]
    public async Task AFailedWithdrawalOfTheToastIsOnlyLogged()
    {
        FakeToastPlatform platform = new();
        ListLogger<ToastNotificationService> logger = new();
        using ToastNotificationService service = new(platform, static _ => { }, static () => CultureInfo.GetCultureInfo("en-US"), logger);
        platform.Session.FailNextRemove = new InvalidOperationException("Element not found");

        service.WithdrawUpdateAvailable();
        service.ShowUpdateRejected(new UpdateRejectedNotification(PlaySound: false));
        await Waiting.ForAsync(() => platform.Session.Shown.Count == 1);

        Assert.Contains(logger.Entries, static entry => entry.Message.Contains("could not be withdrawn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheMacNotificationOfTheOfferIsWithdrawnByItsIdentifierWithoutAskingForPermission()
    {
        FakeUserNotificationCenter center = new() { Permission = NotificationPermission.Denied, Allowed = false };
        using UserNotificationService service = new(center, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<UserNotificationService>());

        service.WithdrawUpdateAvailable();
        await Waiting.ForAsync(() => center.Removed.Count == 1);

        Assert.Equal([UpdateNotificationText.Identifier], center.Removed);
        Assert.Equal(0, center.AuthorizationRequests);
        Assert.Empty(center.Posted);
    }

    [Fact]
    public async Task TheMacWithdrawalComesAfterTheOfferThatStillWaits()
    {
        FakeUserNotificationCenter center = new();
        using UserNotificationService service = new(center, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<UserNotificationService>());
        center.AuthorizationGate = new TaskCompletionSource();

        service.ShowUpdateAvailable(new UpdateNotification("0.5.0", PlaySound: false));
        service.WithdrawUpdateAvailable();
        await Waiting.ForAsync(() => center.AuthorizationRequests == 1);
        Assert.Empty(center.Removed);
        center.AuthorizationGate.SetResult();
        await Waiting.ForAsync(() => center.Removed.Count == 1);

        Assert.Equal(UpdateNotificationText.Identifier, Assert.Single(center.Posted).Identifier);
    }

    [Fact]
    public async Task AClickOpensTheStartupAndUpdatesTab()
    {
        using ToastActivationRouter router = new(TestWebOpener.Create(new FakeUrlLauncher()), new ListLogger<ToastActivationRouter>());
        List<SettingsPage> opened = [];
        router.AttachSettingsOpener(opened.Add);

        Assert.True(await router.ActivateAsync(ToastLaunchArgument.UpdateSettings));

        Assert.Equal([SettingsPage.StartupUpdates], opened);
    }
}
