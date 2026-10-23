using System.Drawing;
using System.Globalization;
using System.Net;
using System.Text;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Simulator.Camera;
using PrusaTrayDiagnostic.Simulator.Printers;

using Wait = PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure.Wait;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The manual checks of the tray menu of T-042 (points 1–6) and of its printer illustrations (T-039, 100 %) on the real
/// menu of the test instance (T-069): illustrations only on printer rows (pixel check against the repository's assets),
/// the keyboard (Win+B, arrows, Shift+F10, access keys and their underlines), the tooltip of a shortened row, "Add
/// printer…", the UI Automation name of a printer with "_" (what Narrator reads), "Diagnostics…" turning on while the menu
/// stays open, and "Open PrusaLink" from the keyboard through the proxy. Every step is captured (only the app's own
/// windows and the test's tray button).
/// </summary>
public sealed class TrayMenuDetailTests(E2EFixture fixture)
{
    /// <summary>
    /// Tolerance of an illustration in the menu against the asset composed on the measured menu background: mean of the
    /// largest channel difference per pixel, and the largest single one (rounding of the compositor, see the task notes).
    /// </summary>
    private const double IconMeanTolerance = 6;

    private const int IconMaxTolerance = 48;

    /// <summary>An empty icon column: no pixel differs from the row background by more than this.</summary>
    private const int EmptyColumnTolerance = 16;

    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(30);

    public static TheoryData<int> PrinterCounts => [1, 3];

    /// <summary>T-042 point 1, T-039 (100 %): one and three printers, every row with its model, every action without an icon.</summary>
    [Theory]
    [MemberData(nameof(PrinterCounts))]
    public async Task PrinterRowsHaveIllustrationsAndActionsHaveNone(int count)
    {
        E2EFixture.Require();
        SimulatedModel[] models = count == 1 ? [SimulatedModel.Mini] : [SimulatedModel.CoreOne, SimulatedModel.Mk4S, SimulatedModel.Xl];
        List<SimulatedPrinter> simulators = [];
        try
        {
            foreach (SimulatedModel model in models)
            {
                simulators.Add(await Simulators.StartModelAsync(model));
            }

            TestPrinter[] printers = [.. simulators.Select((printer, i) => new TestPrinter($"e2e-icon-{i}", $"{models[i].ProductName} printer", printer.Address()))];
            Dictionary<string, string> secrets = printers.Zip(simulators).ToDictionary(static p => $"printer/{p.First.Id}", static p => p.Second.Password);
            await using AppInstance app = await AppInstance.StartAsync(fixture, $"menu-icons-{count}", TestSettings.Json("en", printers), secrets);
            await Simulators.WaitForDetectedModelsAsync(app, [.. printers.Select(static p => p.Id)]);

            TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
            // The menu draws itself after it appears; and the printer rows may still be on "Connecting…" a moment.
            await Wait.UntilAsync(() => menu.Items().Take(count).All(static row => row.Name.EndsWith(": Ready", StringComparison.Ordinal)), StateTimeout, "the printers ready in the menu");
            await Task.Delay(400, TestContext.Current.CancellationToken);
            StringBuilder report = new();
            using Bitmap capture = Screenshots.Capture(menu.Handle);
            Screenshots.Save(capture, $"menu-icons-{count}-menu");
            Native.GetWindowRect(menu.Handle, out Native.Rect window);
            double scaling = Native.GetDpiForWindow(menu.Handle) / 96.0;
            int iconPixels = (int)Math.Round(16 * scaling);
            int assetPixels = PixelChecks.IllustrationPixelSize(16, scaling);
            report.AppendLine(CultureInfo.InvariantCulture, $"Menu {window}, scaling {scaling:0.##}, icon {iconPixels} px, asset {assetPixels} px");

            AutomationElement[] items = menu.Items();
            Assert.Equal([.. printers.Select(static p => p.Name), "Diagnostics…", "Settings…", "About", "Quit"], items.Select(static item => item.Name.Split(':')[0]).ToArray());
            int iconColumn = 0;
            for (int i = 0; i < count; i++)
            {
                AutomationElement image = items[i].FindFirstChild(cf => cf.ByControlType(ControlType.Image))
                    ?? throw new InvalidOperationException($"The printer row '{items[i].Name}' has no icon.");
                Rectangle bounds = Relative(image.BoundingRectangle, window);
                iconColumn = bounds.Left;
                Assert.InRange(bounds.Width, iconPixels - 1, iconPixels + 1);
                Assert.InRange(bounds.Height, iconPixels - 1, iconPixels + 1);
                Color background = RowBackground(capture, Relative(items[i].BoundingRectangle, window), bounds);
                string expectedId = PixelChecks.IllustrationIdOf(models[i].ProductName);
                Dictionary<string, PixelMatch> matches = [];
                foreach (string id in PixelChecks.IllustrationIds)
                {
                    using Bitmap asset = PixelChecks.LoadIllustration(id, assetPixels);
                    using Bitmap expected = PixelChecks.ComposeOn(asset, background);
                    matches[id] = PixelChecks.Compare(capture, bounds.Location, expected);
                }

                report.AppendLine(CultureInfo.InvariantCulture, $"'{items[i].Name}' ({models[i].ProductName} → {expectedId}) icon {bounds}, background {background}: {string.Join("; ", matches.Select(static m => $"{m.Key} {m.Value}"))}");
                if (assetPixels == iconPixels)
                {
                    PixelMatch match = matches[expectedId];
                    Assert.True(match.Mean <= IconMeanTolerance && match.Max <= IconMaxTolerance, $"'{items[i].Name}': the icon is not the {expectedId} illustration ({match}).{Environment.NewLine}{report}");
                    Assert.True(matches.Where(m => m.Key != expectedId).All(m => m.Value.Mean > match.Mean), $"'{items[i].Name}': another illustration matches better.{Environment.NewLine}{report}");
                }
            }

            // Actions and fixed items: no icon element and nothing drawn in the icon column.
            foreach (AutomationElement item in items.Skip(count))
            {
                Assert.Null(item.FindFirstChild(cf => cf.ByControlType(ControlType.Image)));
                Rectangle row = Relative(item.BoundingRectangle, window);
                Rectangle column = new(iconColumn, row.Top + ((row.Height - iconPixels) / 2), iconPixels, iconPixels);
                Color background = RowBackground(capture, row, column);
                int deviation = PixelChecks.LargestDeviation(capture, column, background);
                report.AppendLine(CultureInfo.InvariantCulture, $"'{item.Name}': icon column {column}, largest deviation {deviation}");
                Assert.True(deviation <= EmptyColumnTolerance, $"'{item.Name}' has something in the icon column.{Environment.NewLine}{report}");
            }

            // Every printer's submenu: "Open PrusaLink" with its access key, no icons.
            for (int i = 0; i < count; i++)
            {
                (AutomationElement[] submenu, nint submenuWindow) = await TrayMenu.ExpandAsync(app, menu, menu.Items()[i]);
                await Task.Delay(300, TestContext.Current.CancellationToken);
                using Bitmap submenuCapture = Screenshots.Capture(submenuWindow);
                Screenshots.Save(submenuCapture, $"menu-icons-{count}-submenu-{i + 1}");
                Assert.Equal(["Open PrusaLink"], submenu.Select(static item => item.Name).ToArray());
                Assert.Equal("O", TrayMenu.AccessKeyOf(submenu[0]));
                Assert.All(submenu, static item => Assert.Null(item.FindFirstChild(cf => cf.ByControlType(ControlType.Image))));
                // Without any icon Avalonia leaves the icon column out of the submenu (its text starts at the left padding): the
                // missing image elements are the check, the screenshot shows it.
                report.AppendLine(CultureInfo.InvariantCulture, $"Submenu {i + 1}: {string.Join(", ", submenu.Select(static item => item.Name))}, no icon elements");
                menu.Items()[i].Patterns.ExpandCollapse.Pattern.Collapse();
                await Wait.UntilAsync(() => !Native.IsWindowVisible(submenuWindow), TimeSpan.FromSeconds(5), "the submenu to close");
            }

            Screenshots.SaveText(report.ToString(), $"menu-icons-{count}-report");
            await TrayMenu.CloseAsync(menu);
        }
        finally
        {
            foreach (SimulatedPrinter simulator in simulators)
            {
                await simulator.DisposeAsync();
            }
        }
    }

    /// <summary>T-042 point 2: the menu without the mouse, access keys like the resources, underlined only with the keyboard.</summary>
    [Fact]
    public async Task MenuWorksFromTheKeyboardWithAccessKeys()
    {
        E2EFixture.Require();
        await using SimulatedPrinter printer = await Simulators.StartModelAsync(SimulatedModel.Mk4S);
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await StartWithCameraAsync("menu-keyboard", printer, camera, "MK4 workshop");
        string[] fixedKeys = ["MenuDiagnostics", "MenuSettings", "MenuAbout", "MenuQuit"];

        // With the mouse: no access keys shown.
        TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
        await Task.Delay(400, TestContext.Current.CancellationToken);
        using Bitmap withMouse = Screenshots.Capture(menu.Handle);
        Screenshots.Save(withMouse, "menu-keyboard-1-mouse");
        AssertFixedItems(menu, fixedKeys);
        await TrayMenu.CloseAsync(menu);

        // With the keyboard only: Alt shows the access keys (underlined letters).
        menu = await TrayMenu.OpenWithKeyboardAsync(app);
        Assert.Equal(menu.Handle, Native.GetForegroundWindow());
        AssertFixedItems(menu, fixedKeys);
        await Task.Delay(400, TestContext.Current.CancellationToken);
        Screenshots.Save(menu.Handle, "menu-keyboard-2-opened");
        TrayMenu.Press(app, VirtualKeyShort.ALT);
        await Task.Delay(400, TestContext.Current.CancellationToken);
        Assert.True(menu.IsOpen, "Alt closed the menu.");
        using Bitmap withKeys = Screenshots.Capture(menu.Handle);
        Screenshots.Save(withKeys, "menu-keyboard-3-alt");
        Native.GetWindowRect(menu.Handle, out Native.Rect window);
        Dictionary<string, Rectangle> rows = menu.Items().Where(static item => TrayMenu.AccessKeyOf(item).Length > 0)
            .ToDictionary(static item => item.Name, item => Relative(item.BoundingRectangle, window));
        Assert.Equal(["Diagnostics…", "Settings…", "About", "Quit"], rows.Keys.ToArray());
        (List<string> problems, List<string> lines) = PixelChecks.CheckUnderlines(withKeys, withMouse, rows);
        Screenshots.SaveText(string.Join(Environment.NewLine, [.. lines, .. problems]), "menu-keyboard-underlines");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

        // The letter alone runs the item, like in classic Windows menus and in the submenu (user decision 2026-10-22, T-069
        // Q2): S = Settings… after Alt.
        TrayMenu.Press(app, TrayMenu.KeyOf(TrayMenu.AccessKeyLetter(TrayMenu.Resource("MenuSettings"))!));
        Assert.True(await OpensAsync(app, AppWindowKind.Settings), "The letter alone (S) after Alt did not open Settings from the main menu.");
        nint settings = await app.WaitForWindowAsync(AppWindowKind.Settings);
        await Wait.UntilAsync(() => !menu.IsOpen, TimeSpan.FromSeconds(2), "the menu closed after S");
        await app.CloseWindowAsync(settings);

        // Opened with the mouse, no Alt at all: the letter alone (D = Diagnostics…).
        menu = await TrayMenu.OpenWithMouseAsync(app);
        Assert.True(menu.Item(TrayMenu.ShownText(TrayMenu.Resource("MenuDiagnostics"))).IsEnabled, "Diagnostics… is disabled with a printer.");
        TrayMenu.Press(app, TrayMenu.KeyOf(TrayMenu.AccessKeyLetter(TrayMenu.Resource("MenuDiagnostics"))!));
        Assert.True(await OpensAsync(app, AppWindowKind.Diagnostics), "The letter alone (D) did not open Diagnostics from the menu opened with the mouse.");
        nint diagnostics = await app.WaitForWindowAsync(AppWindowKind.Diagnostics);
        await Wait.UntilAsync(() => !menu.IsOpen, TimeSpan.FromSeconds(2), "the menu closed after D");
        await app.CloseWindowAsync(diagnostics);

        // Down to the printer, Right opens its submenu, C = Show camera.
        menu = await TrayMenu.OpenWithKeyboardAsync(app);
        List<nint> before = app.Windows();
        TrayMenu.Press(app, VirtualKeyShort.DOWN);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.StartsWith("MK4 workshop", app.Automation.FocusedElement()?.Name ?? string.Empty, StringComparison.Ordinal);
        TrayMenu.Press(app, VirtualKeyShort.RIGHT);
        (AutomationElement[] submenu, nint submenuWindow) = await TrayMenu.SubmenuAsync(app, menu, menu.Items()[0], before);
        Screenshots.Save(submenuWindow, "menu-keyboard-4-submenu");
        Assert.Equal([TrayMenu.ShownText(TrayMenu.Resource("MenuOpenPrusaLink")), TrayMenu.ShownText(TrayMenu.Resource("MenuShowCamera"))], submenu.Select(static item => item.Name).ToArray());
        Assert.Equal([TrayMenu.AccessKeyLetter(TrayMenu.Resource("MenuOpenPrusaLink")), TrayMenu.AccessKeyLetter(TrayMenu.Resource("MenuShowCamera"))], submenu.Select(TrayMenu.AccessKeyOf).ToArray());
        TrayMenu.Press(app, TrayMenu.KeyOf(TrayMenu.AccessKeyLetter(TrayMenu.Resource("MenuShowCamera"))!));
        nint cameraWindow = await app.WaitForWindowAsync(AppWindowKind.Camera);
        await app.CloseWindowAsync(cameraWindow);
        await NotificationArea.CloseOverflowAsync(app.Automation);
    }

    /// <summary>
    /// The keyboard path from the taskbar itself: the test icon promoted out of the overflow flyout (T-065 change, restored
    /// after the run).
    /// </summary>
    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.TrayPromotion)]
    public async Task PromotedTestIconOpensTheMenuFromTheKeyboard()
    {
        SystemChanges.Require();
        await using SimulatedPrinter printer = await Simulators.StartModelAsync(SimulatedModel.Mk4S);
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "menu-keyboard-taskbar",
            TestSettings.Json("en", [new TestPrinter("e2e-promoted", "MK4 workshop", printer.Address())]),
            Simulators.Secrets("e2e-promoted", printer));
        SystemChange<TrayIconPromotionState> tray = fixture.TrayIconPromotion
            ?? throw new InvalidOperationException("The run-wide tray icon change is missing although the warning listed TrayPromotion.");
        await tray.PromoteAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await Wait.UntilAsync(() => NotificationArea.TestIconsOnTaskbar(app.Automation).Count > 0, TimeSpan.FromSeconds(15), "the test icon on the taskbar");
        Screenshots.SaveScreenArea(NotificationArea.TestIconsOnTaskbar(app.Automation)[0].BoundingRectangle, 8, "menu-keyboard-taskbar-1-button");

        TrayMenuWindow menu = await TrayMenu.OpenWithKeyboardAsync(app, requireTaskbar: true);
        Screenshots.Save(menu.Handle, "menu-keyboard-taskbar-2-menu");
        AssertFixedItems(menu, ["MenuDiagnostics", "MenuSettings", "MenuAbout", "MenuQuit"]);
        Assert.NotNull(menu.Items()[0].FindFirstChild(cf => cf.ByControlType(ControlType.Image)));
        await TrayMenu.CloseAsync(menu);
    }

    /// <summary>T-042 point 3: the real mouse over a shortened row shows the whole name within 3 s.</summary>
    [Fact]
    public async Task TruncatedPrinterRowShowsTheFullNameInATooltip()
    {
        E2EFixture.Require();
        const string FullName = "Workshop printer with a really very long name for the menu";
        await using AppInstance app = await AppInstance.StartAsync(fixture, "menu-tooltip", TestSettings.Json("en", [new TestPrinter("e2e-long", FullName, "127.0.0.1:1")]));
        TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
        AutomationElement row = menu.Items()[0];
        Assert.StartsWith(FullName[..31] + "…: ", row.Name, StringComparison.Ordinal);
        Assert.Equal(FullName, row.Properties.HelpText.ValueOrDefault);
        Screenshots.Save(menu.Handle, "menu-tooltip-1-menu");

        List<nint> before = app.Windows();
        Rectangle bounds = row.BoundingRectangle;
        Mouse.Position = new Point(bounds.Left + (bounds.Width / 3), bounds.Top + (bounds.Height / 2));
        nint tooltip = 0;
        try
        {
            // Avalonia's tooltip is a popup window of the app with a text (UI Automation reports no ToolTip control type).
            await Wait.UntilAsync(
                () => (tooltip = app.Windows().FirstOrDefault(hwnd => !before.Contains(hwnd)
                    && app.Element(hwnd).FindFirstDescendant(cf => cf.ByControlType(ControlType.Text).And(cf.ByName(FullName))) is not null)) != 0,
                TimeSpan.FromSeconds(3),
                "the tooltip with the whole name");
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException("Finding for the user (T-042 point 3): the shortened printer row shows no tooltip with the whole name in the native tray menu.", ex);
        }

        Screenshots.Save(tooltip, "menu-tooltip-2-tooltip");
        await TrayMenu.CloseAsync(menu);
    }

    /// <summary>T-042 point 4: without printers the menu offers "Add printer…", which opens the settings with one new printer.</summary>
    [Fact]
    public async Task AddPrinterOpensSettingsWithANewPrinter()
    {
        E2EFixture.Require();
        await using AppInstance app = await AppInstance.StartAsync(fixture, "menu-add-printer", TestSettings.Json("en", []));
        TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
        Screenshots.Save(menu.Handle, "menu-add-printer-1-menu");
        AutomationElement[] items = menu.Items();
        string addPrinter = TrayMenu.ShownText(TrayMenu.Resource("MenuAddPrinter"));
        Assert.Equal([TrayMenu.Resource("ToolTipNoPrinter"), addPrinter, "Diagnostics…", "Settings…", "About", "Quit"], items.Select(static item => item.Name).ToArray());
        Assert.False(items[0].IsEnabled, "The 'no printer' text is enabled.");
        Assert.True(items[1].IsEnabled);
        Assert.Equal(TrayMenu.AccessKeyLetter(TrayMenu.Resource("MenuAddPrinter")), TrayMenu.AccessKeyOf(items[1]));
        Assert.False(menu.Item("Diagnostics…").IsEnabled, "Diagnostics… is enabled without a printer.");

        items[1].Patterns.Invoke.Pattern.Invoke();
        nint settings = await app.WaitForWindowAsync(AppWindowKind.Settings);
        AutomationElement window = app.Element(settings);
        await Ui.ItemsAsync(window, "SettingsPrinterList", 1);
        await Wait.UntilAsync(() => (Ui.TryFind(window, "SettingsTabPrinters")?.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault ?? false)
            && (Ui.TryFind(window, "PrinterTabConnection")?.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault ?? false), TimeSpan.FromSeconds(10), "the Printers tab with the printer sub-tab");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        Screenshots.Save(settings, "menu-add-printer-2-settings");

        // Again: the same window, still one (unsaved) printer.
        menu = await TrayMenu.OpenWithMouseAsync(app);
        menu.Item(addPrinter).Patterns.Invoke.Pattern.Invoke();
        await Task.Delay(1000, TestContext.Current.CancellationToken);
        Assert.Equal([settings], app.Windows().Where(hwnd => app.KindOf(hwnd) == AppWindowKind.Settings).ToArray());
        await Ui.ItemsAsync(window, "SettingsPrinterList", 1);
        Screenshots.Save(settings, "menu-add-printer-3-again");

        await Ui.InvokeAsync(window, "SettingsCancel");
        await Wait.UntilAsync(() => !Native.IsWindowVisible(settings), TimeSpan.FromSeconds(10), "the settings to close");
        Assert.Contains("\"printers\": []", app.ReadSettings(), StringComparison.Ordinal);
    }

    /// <summary>
    /// T-042 point 5: what Narrator reads for a printer with "_" in its name. The UI Automation name equals the visible
    /// text after removing U+2060 (user decision 2026-10-22); the raw name is written to the output and the results.
    /// </summary>
    [Fact]
    public async Task PrinterRowWithUnderscoreHasAPlainAutomationName()
    {
        E2EFixture.Require();
        const string Name = "MK4_dílna";
        await using SimulatedPrinter printer = await Simulators.StartModelAsync(SimulatedModel.Mk4S);
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await StartWithCameraAsync("menu-narrator", printer, camera, Name);
        TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
        AutomationElement row = menu.Items()[0];
        string visible = $"{Name}: Ready";
        await Wait.UntilAsync(() => TrayMenu.WithoutWordJoiner(row.Name) == visible, StateTimeout, $"the row '{visible}'");
        Screenshots.Save(menu.Handle, "menu-narrator-1-menu");

        string raw = row.Name;
        string rawKey = row.Properties.AccessKey.ValueOrDefault ?? string.Empty;
        (AutomationElement[] submenu, nint submenuWindow) = await TrayMenu.ExpandAsync(app, menu, row);
        Screenshots.Save(submenuWindow, "menu-narrator-2-submenu");
        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"Printer row Name (raw): {TrayMenu.CodePoints(raw)}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Printer row AccessKey (raw): {TrayMenu.CodePoints(rawKey)}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Printer row HelpText: {TrayMenu.CodePoints(row.Properties.HelpText.ValueOrDefault)}");
        foreach (AutomationElement item in submenu)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"Submenu item Name: {TrayMenu.CodePoints(item.Name)}, AccessKey: {TrayMenu.CodePoints(item.Properties.AccessKey.ValueOrDefault)}");
        }

        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
        Screenshots.SaveText(report.ToString(), "menu-narrator-names");

        // U+2060 itself is no error (decided 2026-10-22); anything else invisible, a doubled "_" or an access key is.
        string name = TrayMenu.WithoutWordJoiner(raw);
        Assert.Equal(visible, name);
        Assert.DoesNotContain(name, TrayMenu.IsHidden);
        Assert.DoesNotContain("__", name, StringComparison.Ordinal);
        Assert.Equal(string.Empty, TrayMenu.WithoutWordJoiner(TrayMenu.AccessKeyOf(row)));
        Assert.Equal(["Open PrusaLink", "Show camera"], submenu.Select(static item => item.Name).ToArray());
        Assert.Equal(["O", "C"], submenu.Select(TrayMenu.AccessKeyOf).ToArray());
        await TrayMenu.CloseAsync(menu);
    }

    /// <summary>
    /// T-042 point 6: "Diagnostics…" is disabled while the menu says "Starting…" and turns on in the open menu when
    /// monitoring starts (Debug-only start delay of T-067): same menu window, same UI Automation element, not rebuilt.
    /// </summary>
    [Fact]
    public async Task DiagnosticsTurnsOnWhileTheMenuStaysOpen()
    {
        E2EFixture.Require();
        await using SimulatedPrinter printer = await Simulators.StartModelAsync(SimulatedModel.Mk4S);
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "menu-starting",
            TestSettings.Json("en", [new TestPrinter("e2e-starting", "MK4 workshop", printer.Address())]),
            Simulators.Secrets("e2e-starting", printer),
            new Dictionary<string, string> { [AppSwitches.TestStartDelayVariable] = "15000" });
        TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
        AutomationElement[] items = menu.Items();
        Screenshots.Save(menu.Handle, "menu-starting-1-starting");
        Assert.Equal([TrayMenu.Resource("ToolTipStarting"), "Diagnostics…", "Settings…", "About", "Quit"], items.Select(static item => item.Name).ToArray());
        AutomationElement diagnostics = menu.Item("Diagnostics…");
        Assert.False(diagnostics.IsEnabled, "Diagnostics… is enabled while starting.");
        string runtimeId = RuntimeId(diagnostics);
        DateTime opened = DateTime.UtcNow;

        // The menu stays open (nobody touches it) while monitoring starts. A UI Automation call may fail while the printer
        // rows are being inserted into the open menu (COMException): read again.
        int transient = 0;
        string lastForeground = TrayMenu.DescribeWindow(app, Native.GetForegroundWindow());
        await Wait.UntilAsync(
            () =>
            {
                if (!menu.IsOpen || !app.Windows().Contains(menu.Handle))
                {
                    // For the analysis: when, which window had the foreground just before and now (titles only of the app's
                    // own windows), what the app logged.
                    string why = $"The menu closed while monitoring started, {(DateTime.UtcNow - opened).TotalSeconds:0.0} s after it opened. "
                        + $"Foreground before: {lastForeground}; now: {TrayMenu.DescribeWindow(app, Native.GetForegroundWindow())}.";
                    Screenshots.SaveText($"{why}{Environment.NewLine}{app.ReadLog()}", "menu-starting-closed");
                    Assert.Fail(why);
                }

                lastForeground = TrayMenu.DescribeWindow(app, Native.GetForegroundWindow());
                try
                {
                    AutomationElement now = menu.Item("Diagnostics…");
                    Assert.Equal(runtimeId, RuntimeId(now));
                    return now.IsEnabled;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    transient++;
                    return false;
                }
            },
            TimeSpan.FromSeconds(40),
            "Diagnostics… to turn on in the open menu");
        TestContext.Current.TestOutputHelper?.WriteLine($"Diagnostics… turned on {(DateTime.UtcNow - opened).TotalSeconds:0.0} s after the menu opened ({transient} UI Automation reads failed meanwhile).");
        await Wait.UntilAsync(() => menu.Items()[0].Name.StartsWith("MK4 workshop", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "the printer row in the open menu");
        Assert.Equal(runtimeId, RuntimeId(menu.Item("Diagnostics…")));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Screenshots.Save(menu.Handle, "menu-starting-2-started");

        menu.Item("Diagnostics…").Patterns.Invoke.Pattern.Invoke();
        await app.WaitForWindowAsync(AppWindowKind.Diagnostics);
    }

    /// <summary>"Open PrusaLink" (UI Automation alone does not run it, T-044): the access key in the submenu opens the proxy.</summary>
    [Fact]
    public async Task OpenPrusaLinkFromTheKeyboardOpensTheProxy()
    {
        E2EFixture.Require();
        await using SimulatedPrinter printer = await Simulators.StartModelAsync(SimulatedModel.Mk4S);
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "menu-open-prusalink",
            TestSettings.Json("en", [new TestPrinter("e2e-web", "MK4 workshop", printer.Address())]),
            Simulators.Secrets("e2e-web", printer));
        TrayMenuWindow menu = await TrayMenu.OpenWithKeyboardAsync(app);
        List<nint> before = app.Windows();
        TrayMenu.Press(app, VirtualKeyShort.DOWN);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        TrayMenu.Press(app, VirtualKeyShort.RIGHT);
        (_, nint submenuWindow) = await TrayMenu.SubmenuAsync(app, menu, menu.Items()[0], before);
        Screenshots.Save(submenuWindow, "menu-open-prusalink-submenu");
        TrayMenu.Press(app, TrayMenu.KeyOf(TrayMenu.AccessKeyLetter(TrayMenu.Resource("MenuOpenPrusaLink"))!));
        await NotificationArea.CloseOverflowAsync(app.Automation);

        string file = Path.Combine(app.DataDirectory, AppSwitches.OpenedUrlsFileName);
        string[] lines = [];
        await Wait.UntilAsync(() => File.Exists(file) && (lines = ReadLines(file)).Length > 0, TimeSpan.FromSeconds(10), "the opened address");
        Uri address = new(Assert.Single(lines));
        Assert.Equal("127.0.0.1", address.Host);
        Assert.DoesNotContain(printer.Password, address.ToString(), StringComparison.Ordinal);

        // The test opens the address itself (no browser in the test mode, T-067): the proxy logs in and shows the printer.
        using HttpClientHandler handler = new() { UseProxy = false, CookieContainer = new CookieContainer() };
        using HttpClient client = new(handler) { Timeout = TimeSpan.FromSeconds(20) };
        using HttpResponseMessage response = await client.GetAsync(address, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        Assert.Contains(SimulatedModel.Mk4S.Hostname, body, StringComparison.Ordinal);
    }

    private Task<AppInstance> StartWithCameraAsync(string name, SimulatedPrinter printer, RtspCamera camera, string printerName) =>
        AppInstance.StartAsync(
            fixture,
            name,
            TestSettings.Json("en", [new TestPrinter("e2e-menu", printerName, printer.Address(), Camera: camera.StreamUri)]),
            Simulators.Secrets("e2e-menu", printer));

    /// <summary>The fixed items: names and access keys exactly as in the resources of the repository.</summary>
    private static void AssertFixedItems(TrayMenuWindow menu, string[] keys)
    {
        AutomationElement[] items = menu.Items()[^keys.Length..];
        Assert.Equal(keys.Select(static key => TrayMenu.ShownText(TrayMenu.Resource(key))).ToArray(), items.Select(static item => item.Name).ToArray());
        Assert.Equal(keys.Select(static key => TrayMenu.AccessKeyLetter(TrayMenu.Resource(key))).ToArray(), items.Select(TrayMenu.AccessKeyOf).ToArray());
    }

    /// <summary>The background of a row: the median of the strip left of <paramref name="exclude"/> (no text, no icon there).</summary>
    private static Color RowBackground(Bitmap capture, Rectangle row, Rectangle exclude) =>
        PixelChecks.Median(capture, Rectangle.FromLTRB(Math.Max(row.Left + 1, exclude.Left - 6), row.Top + 3, Math.Max(row.Left + 2, exclude.Left - 2), row.Bottom - 3));

    private static Rectangle Relative(Rectangle screen, Native.Rect window) => new(screen.Left - window.Left, screen.Top - window.Top, screen.Width, screen.Height);

    /// <summary>Whether a window of <paramref name="kind"/> opens within 2 s.</summary>
    private static async Task<bool> OpensAsync(AppInstance app, AppWindowKind kind)
    {
        try
        {
            await app.WaitForWindowAsync(kind, timeout: TimeSpan.FromSeconds(2));
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static string RuntimeId(AutomationElement element) => string.Join(".", element.Properties.RuntimeId.ValueOrDefault ?? []);

    private static string[] ReadLines(string file)
    {
        using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);
        return [.. reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}
