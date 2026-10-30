using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Menu;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Updates;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Updates;

namespace LayerPeek.App.UiTests.Tray;

/// <summary>
/// T-058 on the real Avalonia <see cref="NativeMenu"/> of the app's tray icon, wired like <c>TrayHost</c>: "New version X.Y.Z
/// available" appears only while a newer version is offered, right above "About", and its click reaches the app command.
/// The check is a fake: no network.
/// </summary>
public sealed class TrayMenuUpdateUiTests(HeadlessSession session)
{
    [Fact]
    public Task TheItemIsShownOnlyWhileANewVersionIsOffered() =>
        session.RunAsync(static async scope =>
        {
            FakeChecker checker = new();
            UpdateCheckService service = scope.Defer(new UpdateCheckService(checker, ReleaseVersion.Parse("0.3.1"), new MemoryUpdateStateStore(), NullNotificationService.Instance, TimeProvider.System, NullLogger<UpdateCheckService>.Instance));
            UpdateOffer offer = scope.Defer(new UpdateOffer(service, installer: null, needsMoveToApplications: false, new FakeUrlLauncher(), NullLogger<UpdateOffer>.Instance));
            ((IMonitoringObserver)offer).ApplySettings(new AppSettings { CheckForUpdates = false });
            TrayIcon icon = Assert.Single(TrayIcon.GetIcons(Application.Current!)!);
            FakeAppCommands commands = new();
            TrayMenuViewModel viewModel = scope.Defer(new TrayMenuViewModel(new FakePrinterMonitor(), new AvaloniaUiDispatcher(), TestWebOpener.Create(new FakeUrlLauncher()), TrayMenuOptions.Default, updates: offer));
            scope.Defer(new TrayMenuPresenter(new TrayMenuTarget(new AvaloniaNativeTrayMenu(icon)), viewModel, commands, NullLogger.Instance));
            viewModel.SetPrinters([TestPrinters.Definition("mk4", "MK4")]);
            Dispatcher.UIThread.RunJobs();
            NativeMenu menu = icon.Menu!;
            string header = Strings.MenuUpdateAvailable.Replace("{0}", "0.3.2", StringComparison.Ordinal);

            // No newer version: no item.
            await offer.CheckNowAsync(TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(header, Headers(menu));

            // 0.3.2 released: the item right above "About".
            checker.Next = new UpdateRelease(ReleaseVersion.Parse("0.3.2"), UpdateRelease.PageFor(ReleaseVersion.Parse("0.3.2")), IsPrerelease: true);
            await offer.CheckNowAsync(TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            List<string?> headers = Headers(menu);
            int index = headers.IndexOf(header);
            Assert.True(index > 0, string.Join(" | ", headers));
            Assert.Equal(Strings.MenuAbout, headers[index + 1]);
            NativeMenuItem item = menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == header);
            Assert.True(item.IsEnabled);
            Assert.Equal(Strings.MenuUpdateOpenPageHint, item.ToolTip);

            ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, commands.InstallUpdateCount);

            // Gone again (e.g. the pre-release withdrawn): the item goes.
            checker.Next = null;
            await offer.CheckNowAsync(TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(header, Headers(menu));
            Assert.Equal(Strings.MenuAbout, Headers(menu)[^2]);
        });

    private static List<string?> Headers(NativeMenu menu) =>
        [.. menu.Items.Select(static item => item is NativeMenuItemSeparator ? "-" : ((NativeMenuItem)item).Header)];

    private sealed class FakeChecker : IUpdateChecker
    {
        public UpdateRelease? Next { get; set; }

        public Task<UpdateRelease?> CheckAsync(bool includePrereleases, CancellationToken cancellationToken) => Task.FromResult(Next);
    }
}
