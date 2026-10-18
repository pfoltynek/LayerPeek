using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Web;

namespace PrusaTrayDiagnostic.App.UiTests.Tray;

/// <summary>
/// The tray menu (T-008, T-013, T-028) on the real Avalonia <see cref="NativeMenu"/> of the app's <see cref="TrayIcon"/>
/// (App.axaml), wired like <c>TrayHost</c> does it: view model → presenter → <see cref="TrayMenuTarget"/> →
/// <see cref="AvaloniaNativeTrayMenu"/>. The monitor, the web gateway and the browser are fakes. Rendering of the native
/// menu by the OS stays a manual check.
/// </summary>
public sealed class TrayMenuTests(HeadlessSession session)
{
    [Fact]
    public Task MenuStartsWithTheStartingMessageAndTheFixedItems() =>
        session.RunAsync(static scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);

            Assert.Equal(
                [Strings.ToolTipStarting, "-", Strings.MenuSettings, Strings.MenuDiagnostics, "-", Strings.MenuAbout, Strings.MenuQuit],
                tray.Headers());
            Assert.False(tray.Item(0).IsEnabled);
            Assert.All(tray.Menu.Items.OfType<NativeMenuItem>().Where(static item => item is not NativeMenuItemSeparator).Skip(1), static item => Assert.True(item.IsEnabled));
            return Task.CompletedTask;
        });

    [Fact]
    public Task OnePrinterIsAPlainItemWhoseHeaderFollowsItsState() =>
        session.RunAsync(static scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);

            tray.ViewModel.SetPrinters([TestPrinters.Definition("mk4", "MK4_dílna")]);
            Dispatcher.UIThread.RunJobs();

            NativeMenuItem printer = tray.Item(0);
            // "_" is an access-key marker in Avalonia menus; the adapter doubles it so that the name shows unchanged.
            Assert.Equal(NativeMenuText.EscapeAccessKeys(TrayMenuFormatter.PrinterHeader("MK4_dílna", default)), printer.Header);
            Assert.StartsWith("MK4__dílna", printer.Header, StringComparison.Ordinal);
            Assert.Null(printer.Menu);
            Assert.True(printer.IsEnabled);
            Assert.Equal(Strings.MenuOpenPrusaLink, printer.ToolTip);
            Assert.IsType<NativeMenuItemSeparator>(tray.Menu.Items[1]);

            tray.Monitor.Raise(TestPrinters.Snapshot(PrinterState.Printing, TestPrinters.Job(42.7)));
            Dispatcher.UIThread.RunJobs();

            // Updated in place: the same platform item, a new text.
            Assert.Same(printer, tray.Item(0));
            Assert.Equal(NativeMenuText.EscapeAccessKeys(TrayMenuFormatter.PrinterHeader("MK4_dílna", new PrinterMenuContent(PrinterState.Printing, 42))), printer.Header);
            Assert.Contains("42", printer.Header, StringComparison.Ordinal);
            return Task.CompletedTask;
        });

    [Fact]
    public Task SeveralPrintersGetOneSubmenuEach() =>
        session.RunAsync(static scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);

            tray.ViewModel.SetPrinters([TestPrinters.Definition("mk4", "MK4"), TestPrinters.Definition("xl", "XL")]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(8, tray.Menu.Items.Count);
            Assert.IsType<NativeMenuItemSeparator>(tray.Menu.Items[2]);
            foreach ((int index, string name) in new[] { (0, "MK4"), (1, "XL") })
            {
                NativeMenuItem header = tray.Item(index);
                Assert.StartsWith(name, header.Header, StringComparison.Ordinal);
                NativeMenu submenu = Assert.IsType<NativeMenu>(header.Menu);
                Assert.Equal(
                    [Strings.MenuOpenPrusaLink, Strings.MenuDiagnostics],
                    submenu.Items.Cast<NativeMenuItem>().Select(static item => item.Header));
            }

            // Back to one printer: the submenu becomes a plain item again.
            tray.ViewModel.SetPrinters([TestPrinters.Definition("xl", "XL")]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(7, tray.Menu.Items.Count);
            Assert.Null(tray.Item(0).Menu);
            Assert.StartsWith("XL", tray.Item(0).Header, StringComparison.Ordinal);
            return Task.CompletedTask;
        });

    [Fact]
    public Task ClickingThePrinterOpensItsWebInterfaceThroughTheGateway() =>
        session.RunAsync(static async scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);
            PrinterDefinition mk4 = TestPrinters.Definition("mk4", "MK4");
            tray.ViewModel.SetPrinters([mk4]);
            Dispatcher.UIThread.RunJobs();

            TrayMenu.ClickLikeThePlatform(tray.Item(0));
            await tray.Launcher.WaitForOpenedAsync(1);

            // The gateway is asked for exactly this printer; the browser gets the logged-in proxy address only.
            Assert.Equal(mk4, Assert.Single(tray.Gateway.Opened));
            Assert.Equal(FakeWebGateway.ProxyAddress, Assert.Single(tray.Launcher.OpenedSecret));
            Assert.Empty(tray.Launcher.Opened);
        });

    [Fact]
    public Task OpenFromThePrinterSubmenuUsesThatPrinter() =>
        session.RunAsync(static async scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope, FakeWebGateway.WithoutPassword());
            PrinterDefinition xl = TestPrinters.Definition("xl", "XL") with { Address = "192.168.1.11" };
            tray.ViewModel.SetPrinters([TestPrinters.Definition("mk4", "MK4"), xl]);
            Dispatcher.UIThread.RunJobs();

            NativeMenu submenu = tray.Item(1).Menu!;
            TrayMenu.ClickLikeThePlatform((NativeMenuItem)submenu.Items[0]);
            await tray.Launcher.WaitForOpenedAsync(1);

            Assert.Equal(xl, Assert.Single(tray.Gateway.Opened));
            // No stored password: the plain address of that printer.
            Assert.Equal(PrinterWebAddress.For(xl), Assert.Single(tray.Launcher.Opened));

            // "Diagnostics…" of the submenu preselects the printer.
            TrayMenu.ClickLikeThePlatform((NativeMenuItem)submenu.Items[1]);
            Assert.Equal(["xl"], tray.Commands.DiagnosticsRequests);
        });

    [Fact]
    public Task FixedItemsCallTheAppCommandsAndDisabledItemsDoNothing() =>
        session.RunAsync(static scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);

            TrayMenu.ClickLikeThePlatform(tray.Item(0));
            TrayMenu.ClickLikeThePlatform(tray.ItemWithHeader(Strings.MenuSettings));
            TrayMenu.ClickLikeThePlatform(tray.ItemWithHeader(Strings.MenuDiagnostics));
            TrayMenu.ClickLikeThePlatform(tray.ItemWithHeader(Strings.MenuAbout));
            TrayMenu.ClickLikeThePlatform(tray.ItemWithHeader(Strings.MenuQuit));

            Assert.Equal(1, tray.Commands.SettingsCount);
            Assert.Equal([null], tray.Commands.DiagnosticsRequests);
            Assert.Equal(1, tray.Commands.AboutCount);
            Assert.Equal(1, tray.Commands.QuitCount);
            Assert.Empty(tray.Gateway.Opened);
            return Task.CompletedTask;
        });

    [Fact]
    public Task LanguageSwitchTranslatesTheWholeMenu() =>
        session.RunAsync(static scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);
            tray.ViewModel.SetPrinters([TestPrinters.Definition("mk4", "MK4"), TestPrinters.Definition("xl", "XL")]);
            Dispatcher.UIThread.RunJobs();

            // What TrayHost does on a language switch: the view model formats every text again, the presenter rebuilds.
            new UiLanguage().Apply("cs");
            tray.ViewModel.RefreshTexts();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Ukončit", tray.ItemWithHeader(Strings.MenuQuit).Header);
            Assert.Equal(
                [Strings.MenuOpenPrusaLink, Strings.MenuDiagnostics],
                tray.Item(0).Menu!.Items.Cast<NativeMenuItem>().Select(static item => item.Header));
            Assert.NotEqual("Open PrusaLink in the browser", Strings.MenuOpenPrusaLink);
            Assert.Equal(TrayMenuFormatter.PrinterHeader("MK4", default), tray.Item(0).Header);
            return Task.CompletedTask;
        });

    [Fact]
    public Task DisposedPresenterIgnoresClicksFromAnOpenMenu() =>
        session.RunAsync(static scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);
            NativeMenuItem quit = tray.ItemWithHeader(Strings.MenuQuit);

            tray.Presenter.Dispose();
            TrayMenu.ClickLikeThePlatform(quit);

            Assert.Equal(0, tray.Commands.QuitCount);
            return Task.CompletedTask;
        });

    /// <summary>The tray menu of one test, wired like <c>TrayHost.StartAsync</c>.</summary>
    private sealed class TrayMenu
    {
        private TrayMenu(
            NativeMenu menu,
            FakePrinterMonitor monitor,
            FakeWebGateway gateway,
            FakeUrlLauncher launcher,
            FakeAppCommands commands,
            TrayMenuViewModel viewModel,
            TrayMenuPresenter presenter)
        {
            Menu = menu;
            Monitor = monitor;
            Gateway = gateway;
            Launcher = launcher;
            Commands = commands;
            ViewModel = viewModel;
            Presenter = presenter;
        }

        public NativeMenu Menu { get; }

        public FakePrinterMonitor Monitor { get; }

        public FakeWebGateway Gateway { get; }

        public FakeUrlLauncher Launcher { get; }

        public FakeAppCommands Commands { get; }

        public TrayMenuViewModel ViewModel { get; }

        public TrayMenuPresenter Presenter { get; }

        public static TrayMenu Create(UiTestScope scope, FakeWebGateway? gateway = null)
        {
            TrayIcon icon = Assert.Single(TrayIcon.GetIcons(Application.Current!)!);
            FakePrinterMonitor monitor = new();
            FakeUrlLauncher launcher = new();
            gateway ??= new FakeWebGateway();
            FakeAppCommands commands = new();
            TrayMenuViewModel viewModel = scope.Defer(new TrayMenuViewModel(
                monitor,
                new AvaloniaUiDispatcher(),
                TestWebOpener.Create(launcher, gateway),
                TrayMenuOptions.Default));
            TrayMenuPresenter presenter = scope.Defer(new TrayMenuPresenter(
                new TrayMenuTarget(new AvaloniaNativeTrayMenu(icon)),
                viewModel,
                commands,
                NullLogger.Instance));
            Dispatcher.UIThread.RunJobs();
            return new TrayMenu(icon.Menu!, monitor, gateway, launcher, commands, viewModel, presenter);
        }

        /// <summary>
        /// What the platform menu exporter (Win32 tray menu, NSMenu) calls when the user picks the item: the
        /// <see cref="NativeMenuItem.Click"/> event, then its command.
        /// </summary>
        public static void ClickLikeThePlatform(NativeMenuItem item)
        {
            ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
        }

        public NativeMenuItem Item(int index) => Assert.IsType<NativeMenuItem>(Menu.Items[index]);

        public NativeMenuItem ItemWithHeader(string header) =>
            Assert.Single(Menu.Items.OfType<NativeMenuItem>(), item => item.Header == header);

        /// <summary>Headers in menu order; "-" for a separator.</summary>
        public string?[] Headers() =>
            [.. Menu.Items.Select(static item => item is NativeMenuItemSeparator ? "-" : ((NativeMenuItem)item).Header)];
    }
}
