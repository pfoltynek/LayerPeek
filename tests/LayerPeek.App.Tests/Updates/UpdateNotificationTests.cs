using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;

using LayerPeek.App.Notifications;
using LayerPeek.App.SingleInstance;
using LayerPeek.App.Tests.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.Core.Notifications;

namespace LayerPeek.App.Tests.Updates;

/// <summary>
/// The notification about a new version (T-058) on both OS: the texts in the language of the app, the fixed identifier (a
/// newer one replaces it), the launch argument <see cref="ToastLaunchArgument.Update"/>, and its click reaching the update
/// action of the UI (also one that came before the UI ran). Only fakes: nothing reaches Windows or macOS.
/// </summary>
public sealed class UpdateNotificationTests
{
    [Fact]
    public void TheLaunchArgumentIsFixedAndWellFormed()
    {
        Assert.Equal("update=available", ToastLaunchArgument.Update);
        Assert.True(ToastLaunchArgument.IsWellFormed(ToastLaunchArgument.Update));
        Assert.True(ToastLaunchArgument.IsUpdate(ToastLaunchArgument.Update));
        Assert.False(ToastLaunchArgument.IsUpdate("update=available2"));
        Assert.False(ToastLaunchArgument.IsWellFormed("update=now"));
        // A printer id can never produce it.
        Assert.False(ToastLaunchArgument.IsUpdate(ToastLaunchArgument.ForPrinter("update=available")));
        Assert.True(InstanceCommand.TryForToast(ToastLaunchArgument.Update, out InstanceCommand command));
        Assert.Equal(InstanceCommandKind.Toast, command.Kind);
    }

    [Fact]
    public void TheToastCarriesTheTextsTheArgumentAndTheSound()
    {
        XDocument silent = XDocument.Parse(ToastContentBuilder.BuildUpdate(new UpdateNotification("0.3.2", PlaySound: false), CultureInfo.GetCultureInfo("de-DE")));
        XDocument loud = XDocument.Parse(ToastContentBuilder.BuildUpdate(new UpdateNotification("0.3.2", PlaySound: true), CultureInfo.GetCultureInfo("en-US")));

        Assert.Equal(ToastLaunchArgument.Update, silent.Root!.Attribute("launch")?.Value);
        Assert.Equal("foreground", silent.Root.Attribute("activationType")?.Value);
        Assert.Equal(
            ["LayerPeek-Update verfügbar", "Version 0.3.2 ist verfügbar. Hier klicken, um sie zu erhalten."],
            silent.Descendants("text").Select(static text => text.Value));
        Assert.Equal("true", silent.Descendants("audio").Single().Attribute("silent")?.Value);
        Assert.Empty(loud.Descendants("audio"));
        Assert.Equal("LayerPeek update available", loud.Descendants("text").First().Value);
    }

    [Fact]
    public void AVersionWithMarkupCannotAddMarkup()
    {
        XDocument toast = XDocument.Parse(ToastContentBuilder.BuildUpdate(new UpdateNotification("1.0.0\"/><action/>", PlaySound: false), CultureInfo.GetCultureInfo("en-US")));

        Assert.Empty(toast.Descendants("action"));
    }

    [Fact]
    public async Task TheWindowsToastIsTaggedSoANewerOneReplacesIt()
    {
        FakeToastPlatform platform = new();
        using ToastNotificationService service = new(platform, static _ => { }, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<ToastNotificationService>());

        service.ShowUpdateAvailable(new UpdateNotification("0.3.2", PlaySound: false));
        await Waiting.ForAsync(() => platform.Session.Shown.Count == 1);

        Assert.Equal([UpdateNotificationText.Identifier], platform.Session.ShownTags);
        Assert.Contains("update=available", platform.Session.Shown[0], StringComparison.Ordinal);
        platform.Session.Release();
    }

    [Fact]
    public async Task TheMacNotificationHasTheFixedIdentifierAndArgument()
    {
        FakeUserNotificationCenter center = new();
        using UserNotificationService service = new(center, static () => CultureInfo.GetCultureInfo("cs-CZ"), new ListLogger<UserNotificationService>());

        service.ShowUpdateAvailable(new UpdateNotification("0.3.2", PlaySound: true));
        await Waiting.ForAsync(() => center.Posted.Count == 1);

        UserNotificationRequest request = center.Posted[0];
        Assert.Equal(UpdateNotificationText.Identifier, request.Identifier);
        Assert.Equal("Je dostupná aktualizace LayerPeeku", request.Title);
        Assert.Equal("Je dostupná verze 0.3.2. Klikněte sem a získejte ji.", request.Body);
        Assert.True(request.PlaySound);
        Assert.Equal(ToastLaunchArgument.Update, request.LaunchArgument);
    }

    [Fact]
    public async Task AClickStartsTheUpdateActionAlsoWhenItCameBeforeTheUi()
    {
        using ToastActivationRouter router = new(TestWebOpener.Create(new FakeUrlLauncher()), new ListLogger<ToastActivationRouter>());
        ConcurrentQueue<string> opened = new();

        // Before the UI runs (macOS waiting for a display, T-099): held once.
        Assert.True(await router.ActivateAsync(ToastLaunchArgument.Update));
        Assert.Empty(opened);

        router.AttachUpdateOpener(() => opened.Enqueue("update"));
        Assert.Single(opened);

        // Reported twice by Windows within the duplicate window: once.
        await Task.Delay(ToastActivationRouter.DuplicateWindow, TestContext.Current.CancellationToken);
        Assert.True(await router.ActivateAsync(ToastLaunchArgument.Update));
        Assert.False(await router.ActivateAsync(ToastLaunchArgument.Update));
        Assert.Equal(2, opened.Count);
        Assert.Throws<InvalidOperationException>(() => router.AttachUpdateOpener(static () => { }));
    }

    [Fact]
    public async Task AfterQuittingAClickStartsNothing()
    {
        ToastActivationRouter router = new(TestWebOpener.Create(new FakeUrlLauncher()), new ListLogger<ToastActivationRouter>());
        int opened = 0;
        router.AttachUpdateOpener(() => opened++);
        router.Dispose();

        Assert.False(await router.ActivateAsync(ToastLaunchArgument.Update));
        Assert.Equal(0, opened);
    }
}
