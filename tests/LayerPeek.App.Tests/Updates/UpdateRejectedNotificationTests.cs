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
/// The notification about a refused update (T-181, decision 5) on both OS: the texts in the language of the app (with or
/// without the version the refused feed names), its own fixed identifier (a later refusal replaces it, never the offer of a
/// good update), and the launch argument <see cref="ToastLaunchArgument.UpdateSettings"/>, whose click opens the Startup and
/// updates tab. Only fakes: nothing reaches Windows or macOS.
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
        XDocument silent = XDocument.Parse(ToastContentBuilder.BuildUpdateRejected(new UpdateRejectedNotification("0.5.0", PlaySound: false), CultureInfo.GetCultureInfo("de-DE")));
        XDocument loud = XDocument.Parse(ToastContentBuilder.BuildUpdateRejected(new UpdateRejectedNotification(null, PlaySound: true), CultureInfo.GetCultureInfo("en-US")));

        Assert.Equal(ToastLaunchArgument.UpdateSettings, silent.Root!.Attribute("launch")?.Value);
        Assert.Equal("foreground", silent.Root.Attribute("activationType")?.Value);
        Assert.Equal(
            ["LayerPeek-Update abgelehnt", "Version 0.5.0 wurde nicht installiert, weil ihre Signatur nicht überprüft werden konnte. Für Details hier klicken."],
            silent.Descendants("text").Select(static text => text.Value));
        Assert.Equal("true", silent.Descendants("audio").Single().Attribute("silent")?.Value);
        Assert.Empty(loud.Descendants("audio"));
        Assert.Equal(
            ["LayerPeek update refused", "An update was not installed because its signature could not be verified. Click for details."],
            loud.Descendants("text").Select(static text => text.Value));
    }

    [Fact]
    public void AVersionWithMarkupCannotAddMarkup()
    {
        XDocument toast = XDocument.Parse(ToastContentBuilder.BuildUpdateRejected(new UpdateRejectedNotification("1.0.0\"/><action/>", PlaySound: false), CultureInfo.GetCultureInfo("en-US")));

        Assert.Empty(toast.Descendants("action"));
    }

    [Fact]
    public async Task TheWindowsToastHasItsOwnTag()
    {
        FakeToastPlatform platform = new();
        using ToastNotificationService service = new(platform, static _ => { }, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<ToastNotificationService>());

        service.ShowUpdateRejected(new UpdateRejectedNotification("0.5.0", PlaySound: false));
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

        service.ShowUpdateRejected(new UpdateRejectedNotification("0.5.0", PlaySound: true));
        await Waiting.ForAsync(() => center.Posted.Count == 1);

        UserNotificationRequest request = center.Posted[0];
        Assert.Equal(UpdateNotificationText.RejectedIdentifier, request.Identifier);
        Assert.Equal("Aktualizace LayerPeeku odmítnuta", request.Title);
        Assert.Equal("Verze 0.5.0 se nenainstalovala, protože nešlo ověřit její podpis. Klikněte pro podrobnosti.", request.Body);
        Assert.True(request.PlaySound);
        Assert.Equal(ToastLaunchArgument.UpdateSettings, request.LaunchArgument);
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
