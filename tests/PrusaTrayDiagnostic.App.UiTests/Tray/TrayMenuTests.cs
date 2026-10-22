using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
/// The tray menu (T-008, T-013, T-028, T-042) on the real Avalonia <see cref="NativeMenu"/> of the app's <see cref="TrayIcon"/>
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

            // "Diagnostics…" above "Settings…" (2026-10-21), disabled until printers are shown.
            Assert.Equal(
                [Strings.ToolTipStarting, "-", Strings.MenuDiagnostics, Strings.MenuSettings, "-", Strings.MenuAbout, Strings.MenuQuit],
                tray.Headers());
            Assert.False(tray.Item(0).IsEnabled);
            Assert.False(tray.ItemWithHeader(Strings.MenuDiagnostics).IsEnabled);
            Assert.All(
                tray.Menu.Items.OfType<NativeMenuItem>().Where(static item => item is not NativeMenuItemSeparator && item.Header != Strings.MenuDiagnostics).Skip(1),
                static item => Assert.True(item.IsEnabled));
            return Task.CompletedTask;
        });

    [Fact]
    public Task NoPrinterShowsTheMessageAndAddPrinter() =>
        session.RunAsync(static scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);

            tray.ViewModel.SetPrinters([]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(
                [Strings.ToolTipNoPrinter, Strings.MenuAddPrinter, "-", Strings.MenuDiagnostics, Strings.MenuSettings, "-", Strings.MenuAbout, Strings.MenuQuit],
                tray.Headers());
            Assert.False(tray.Item(0).IsEnabled);
            Assert.False(tray.ItemWithHeader(Strings.MenuDiagnostics).IsEnabled);
            Assert.True(tray.Item(1).IsEnabled);
            Assert.Null(tray.Item(1).Menu);

            TrayMenu.ClickLikeThePlatform(tray.Item(0));
            TrayMenu.ClickLikeThePlatform(tray.Item(1));
            Assert.Equal(1, tray.Commands.AddPrinterCount);
            Assert.Equal(0, tray.Commands.SettingsCount);

            // A printer saved: the item goes away.
            tray.ViewModel.SetPrinters([TestPrinters.Definition("mk4", "MK4")]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(7, tray.Menu.Items.Count);
            Assert.DoesNotContain(Strings.MenuAddPrinter, tray.Headers());
            Assert.True(tray.ItemWithHeader(Strings.MenuDiagnostics).IsEnabled);
            return Task.CompletedTask;
        });

    [Fact]
    public Task OnePrinterIsASubmenuWhoseHeaderFollowsItsState() =>
        session.RunAsync(static scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);

            tray.ViewModel.SetPrinters([TestPrinters.Definition("mk4", "MK4_dílna")]);
            Dispatcher.UIThread.RunJobs();

            NativeMenuItem printer = tray.Item(0);
            // "_" is an access-key marker in Avalonia menus; the adapter escapes it so that the name shows unchanged.
            Assert.Equal(NativeMenuText.EscapeAccessKeys(TrayMenuFormatter.PrinterHeader("MK4_dílna", default)), printer.Header);
            Assert.StartsWith("MK4_dílna: ", NativeMenuText.DisplayTextOf(printer.Header!), StringComparison.Ordinal);
            Assert.True(printer.IsEnabled);
            Assert.Null(printer.ToolTip);
            // Like several printers (T-042): a submenu with the printer's actions, resource texts with their access keys.
            NativeMenu submenu = Assert.IsType<NativeMenu>(printer.Menu);
            Assert.Equal(
                [Strings.MenuOpenPrusaLink],
                submenu.Items.Cast<NativeMenuItem>().Select(static item => item.Header));
            Assert.IsType<NativeMenuItemSeparator>(tray.Menu.Items[1]);

            tray.Monitor.Raise(TestPrinters.Snapshot(PrinterState.Printing, TestPrinters.Job(42.7)));
            Dispatcher.UIThread.RunJobs();

            // Updated in place: the same platform item and submenu, a new text.
            Assert.Same(printer, tray.Item(0));
            Assert.Same(submenu, printer.Menu);
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
                    [Strings.MenuOpenPrusaLink],
                    submenu.Items.Cast<NativeMenuItem>().Select(static item => item.Header));
            }

            // Back to one printer: still a submenu (T-042).
            tray.ViewModel.SetPrinters([TestPrinters.Definition("xl", "XL")]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(7, tray.Menu.Items.Count);
            Assert.NotNull(tray.Item(0).Menu);
            Assert.StartsWith("XL", tray.Item(0).Header, StringComparison.Ordinal);
            return Task.CompletedTask;
        });

    [Fact]
    public Task ThePrinterItemOnlyOpensItsSubmenuAndItsWebItemUsesTheGateway() =>
        session.RunAsync(static async scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);
            PrinterDefinition mk4 = TestPrinters.Definition("mk4", "MK4");
            tray.ViewModel.SetPrinters([mk4]);
            Dispatcher.UIThread.RunJobs();

            // T-042: no click-to-web on the printer item any more (T-008).
            TrayMenu.ClickLikeThePlatform(tray.Item(0));
            Assert.Empty(tray.Gateway.Opened);
            Assert.Empty(tray.Launcher.OpenedSecret);

            TrayMenu.ClickLikeThePlatform((NativeMenuItem)tray.Item(0).Menu!.Items[0]);
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

            // The submenu has no "Diagnostics…" any more (2026-10-21).
            Assert.Single(submenu.Items);
        });

    [Fact]
    public Task DiagnosticsInTheMainMenuFollowsThePrintersAndOpensWithoutAPrinter() =>
        session.RunAsync(static async scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope, FakeWebGateway.WithoutPassword());
            PrinterDefinition xl = TestPrinters.Definition("xl", "XL") with { Address = "192.168.1.11" };

            // Disabled while starting: a click does nothing.
            NativeMenuItem diagnostics = tray.ItemWithHeader(Strings.MenuDiagnostics);
            NativeMenuItem settings = tray.ItemWithHeader(Strings.MenuSettings);
            object separator = tray.Menu.Items[1];
            TrayMenu.ClickLikeThePlatform(diagnostics);
            Assert.Empty(tray.Commands.DiagnosticsRequests);

            tray.ViewModel.SetPrinters([TestPrinters.Definition("mk4", "MK4"), xl]);
            Dispatcher.UIThread.RunJobs();

            // The item is enabled in place (2026-10-21): the fixed part is not rebuilt, so an open menu stays open.
            Assert.Equal(
                [TrayMenuFormatter.PrinterHeader("MK4", default), TrayMenuFormatter.PrinterHeader("XL", default), "-", Strings.MenuDiagnostics, Strings.MenuSettings, "-", Strings.MenuAbout, Strings.MenuQuit],
                tray.Headers());
            Assert.Same(diagnostics, tray.ItemWithHeader(Strings.MenuDiagnostics));
            Assert.Same(settings, tray.ItemWithHeader(Strings.MenuSettings));
            Assert.Same(separator, tray.Menu.Items[2]);
            Assert.True(diagnostics.IsEnabled);
            TrayMenu.ClickLikeThePlatform(diagnostics);
            Assert.Equal([null], tray.Commands.DiagnosticsRequests);

            TrayMenu.ClickLikeThePlatform((NativeMenuItem)tray.Item(1).Menu!.Items[0]);
            await tray.Launcher.WaitForOpenedAsync(1);
            Assert.Equal(PrinterWebAddress.For(xl), Assert.Single(tray.Launcher.Opened));

            // A poll updates the printer item in place, the fixed part stays.
            tray.Monitor.Raise(TestPrinters.Snapshot(PrinterState.Printing, TestPrinters.Job(10), printerId: "mk4"));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(diagnostics, tray.ItemWithHeader(Strings.MenuDiagnostics));

            // Monitoring failed: disabled again.
            tray.ViewModel.ReportMonitoringFailed();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(diagnostics, tray.ItemWithHeader(Strings.MenuDiagnostics));
            Assert.False(diagnostics.IsEnabled);
            TrayMenu.ClickLikeThePlatform(diagnostics);
            Assert.Single(tray.Commands.DiagnosticsRequests);
            Assert.Equal([Strings.ToolTipMonitoringFailed, "-"], tray.Headers()[..2]);
        });

    [Fact]
    public Task FixedItemsCallTheAppCommandsAndDisabledItemsDoNothing() =>
        session.RunAsync(static scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);

            TrayMenu.ClickLikeThePlatform(tray.Item(0));
            TrayMenu.ClickLikeThePlatform(tray.ItemWithHeader(Strings.MenuDiagnostics));
            TrayMenu.ClickLikeThePlatform(tray.ItemWithHeader(Strings.MenuSettings));
            TrayMenu.ClickLikeThePlatform(tray.ItemWithHeader(Strings.MenuAbout));
            TrayMenu.ClickLikeThePlatform(tray.ItemWithHeader(Strings.MenuQuit));

            Assert.Equal(1, tray.Commands.SettingsCount);
            Assert.Empty(tray.Commands.DiagnosticsRequests);
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

            Assert.Equal("U_končit", tray.ItemWithHeader(Strings.MenuQuit).Header);
            Assert.Equal(
                [Strings.MenuOpenPrusaLink],
                tray.Item(0).Menu!.Items.Cast<NativeMenuItem>().Select(static item => item.Header));
            Assert.NotEqual("Open PrusaLink", Strings.MenuOpenPrusaLink);
            Assert.Equal(TrayMenuFormatter.PrinterHeader("MK4", default), tray.Item(0).Header);
            return Task.CompletedTask;
        });

    [Fact]
    public Task LongNameIsShortenedWithTheFullNameInTheToolTip() =>
        session.RunAsync(static scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);
            string name = "Original Prusa MK4S workshop printer no. 2";

            tray.ViewModel.SetPrinters([TestPrinters.Definition("mk4", name)]);
            Dispatcher.UIThread.RunJobs();

            NativeMenuItem printer = tray.Item(0);
            Assert.Equal(TrayMenuFormatter.PrinterHeader(name, default), printer.Header);
            Assert.StartsWith(name[..31] + "…", printer.Header, StringComparison.Ordinal);
            Assert.Equal(name, printer.ToolTip);
            return Task.CompletedTask;
        });

    [Fact]
    public Task AvaloniaMenuShowsTheAccessKeysOfTheResourcesAndNoneForPrinterNames() =>
        session.RunAsync(static scope =>
        {
            TrayMenu tray = TrayMenu.Create(scope);
            new UiLanguage().Apply("cs");
            tray.ViewModel.RefreshTexts();
            tray.ViewModel.SetPrinters([TestPrinters.Definition("mk4", "_MK4_dílna")]);
            Dispatcher.UIThread.RunJobs();

            // What Avalonia's Windows tray menu draws for these headers: menu items in a menu flyout presenter, whose header
            // presenter recognizes access keys (an AccessText).
            NativeMenuItem printer = tray.Item(0);
            string[] headers = [printer.Header!, tray.ItemWithHeader(Strings.MenuQuit).Header!, tray.ItemWithHeader(Strings.MenuDiagnostics).Header!];
            MenuItem[] items = [.. headers.Select(static header => new MenuItem { Header = header })];
            Window window = scope.Track(new Window { Width = 400, Height = 300, Content = new MenuFlyoutPresenter { ItemsSource = items } });
            window.Show();
            window.Settle();

            AccessText[] texts = [.. items.Select(static item => item.GetVisualDescendants().OfType<AccessText>().Single())];
            // The printer name: only the invisible word joiner, no key anyone can type (Avalonia 12 takes the character after
            // the first "_", even of "__").
            Assert.Equal(NativeMenuText.NoAccessKey.ToString(), texts[0].AccessKey);
            Assert.Equal("k", texts[1].AccessKey);
            Assert.Equal("D", texts[2].AccessKey);
            Assert.Equal(printer.Header, texts[0].Text);
            Assert.StartsWith("_MK4_dílna: ", NativeMenuText.DisplayTextOf(texts[0].Text!), StringComparison.Ordinal);

            // What AccessText really lays out (markers removed) is what DisplayTextOf predicts, apart from the invisible joiner.
            Assert.All(texts, static text => Assert.Equal(
                NativeMenuText.DisplayTextOf(text.Text!),
                RenderedText(text).Replace(NativeMenuText.NoAccessKey.ToString(), string.Empty, StringComparison.Ordinal)));
            Assert.Equal("Ukončit", RenderedText(texts[1]));
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

    /// <summary>The characters <paramref name="text"/> has laid out (and draws), without line ends.</summary>
    private static string RenderedText(AccessText text) =>
        string.Concat(text.TextLayout.TextLines.SelectMany(static line => line.TextRuns).Select(static run => run.Text.ToString()))
            .TrimEnd('\r', '\n', (char)0x2029);

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
