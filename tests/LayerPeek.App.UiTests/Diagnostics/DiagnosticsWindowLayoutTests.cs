using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

using LayerPeek.App.Diagnostics;
using LayerPeek.App.Diagnostics.Chart;
using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.PrusaLink;

namespace LayerPeek.App.UiTests.Diagnostics;

/// <summary>
/// Layout of the diagnostics window (T-041): the size of the settings window, only a close button in the title bar, no
/// scroll bar in any language and state, nothing cut, the chart fills the remaining height (at least 120 DIP), the footer
/// shows either the printer information or its status, Tab moves in reading order and the texts follow a language switch.
/// </summary>
public sealed class DiagnosticsWindowLayoutTests(HeadlessSession session)
{
    /// <summary>Title bar assumed by the design (Windows 11 at 100 %); the smallest supported screen leaves 780 × 540.</summary>
    private const double TitleBar = 32;

    /// <summary>The chart never gets lower than this (T-041 design).</summary>
    private const double MinChartHeight = 120;

    /// <summary>Space between the chart and the footer (T-041 design).</summary>
    private const double FooterGap = 12;

    private static readonly PrusaLinkPrinterInfo Mk4SInfo = new(
        Hostname: "prusa-mk4s",
        Firmware: "6.2.1+8896",
        ApiVersion: "2.0.0",
        PrinterModel: "1.4.0",
        NozzleDiameter: 0.4,
        HasMmu: false);

    private static readonly PrusaLinkPrinterInfo LongestInfo = new(
        Hostname: "prusa-core-one-l-workshop-second-floor.local",
        Firmware: "6.4.0-RC1+15456",
        ApiVersion: "2.0.0",
        PrinterModel: "7.0.0",
        NozzleDiameter: 0.25,
        HasMmu: true);

    /// <summary>Details the clients and the monitor report with a state; technical English, shown as is.</summary>
    private static readonly string[] Details =
    [
        "The printer address is not valid.",
        "The printer is not reachable.",
        "The printer did not respond within 10 s.",
        "The secure (https) connection to the printer failed.",
        "The printer rejected the user name or password.",
        "The printer denied access (HTTP 403).",
        "The printer requires an unsupported authentication method.",
        "Monitoring stopped because of an internal error.",
        "Polling failed (HttpRequestException).",
    ];

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    private static string LongestDetail => Details.MaxBy(static detail => detail.Length)!;

    // ---- Size and title bar ----

    [Fact]
    public Task WindowOpensAtTheSizeOfTheSettingsWindowWithOnlyACloseButton() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = OpenPrinting(scope);

            Assert.Equal(new Size(780, 508), ui.Window.ClientSize);
            Assert.True(ui.Window.ClientSize.Height + TitleBar <= 540);
            Assert.Equal(AuxiliaryWindowPlacement.DefaultClientSize, new Size(ui.Window.Width, ui.Window.Height));
            Assert.Equal(AuxiliaryWindowPlacement.MinClientSize, new Size(ui.Window.MinWidth, ui.Window.MinHeight));
            Assert.Equal(WindowStartupLocation.CenterScreen, ui.Window.WindowStartupLocation);
            // T-041 like T-040: no minimize, no maximize; the window can still be resized.
            Assert.False(ui.Window.CanMinimize);
            Assert.False(ui.Window.CanMaximize);
            Assert.True(ui.Window.CanResize);
            return Task.CompletedTask;
        });

    // ---- No scroll bar, nothing cut, chart fills the rest (every language × state) ----

    [Theory]
    [MemberData(nameof(Languages))]
    public Task EverythingFitsWithoutScrollingInEveryState(string language) =>
        session.RunAsync(scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            List<string> problems = [];
            foreach ((string state, Func<UiTestScope, DiagnosticsWindowHarness> open) in States())
            {
                DiagnosticsWindowHarness ui = open(scope);
                ui.Window.Settle();
                Assert.Equal(new Size(780, 508), ui.Window.ClientSize);
                string context = $"{language} / {state}";
                problems.AddRange(LayoutProblems(ui.Window, context));
                ui.Window.Close();
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
            return Task.CompletedTask;
        });

    [Fact]
    public Task ChecksOfTheLayoutTestDetectProblems() =>
        session.RunAsync(static scope =>
        {
            // The checks above pass only because the layout fits, not because they cannot fail.
            Assert.True(new UiLanguage().Apply("fr"));
            DiagnosticsWindowHarness ui = OpenWorst(scope, loadedInfo: true);
            Assert.Empty(LayoutProblems(ui.Window, "designed size"));

            // Below the designed height the fallback scroll viewer scrolls (the chart keeps its minimum height).
            ui.Window.Height = 400;
            ui.Window.Settle();
            Assert.NotEmpty(ScrollingViewers(ui.Window, "small"));
            Assert.Empty(ChartProblems(ui.Window, "small"));

            // Without the minimum the chart would shrink below it.
            TemperatureChart chart = ui.Find<TemperatureChart>("DiagnosticsChart");
            chart.MinHeight = 0;
            ui.Window.Settle();
            Assert.Contains(ChartProblems(ui.Window, "no minimum"), static problem => problem.Contains("chart is", StringComparison.Ordinal));

            // A chart that does not reach the footer.
            chart.MinHeight = MinChartHeight;
            ui.Window.Height = 508;
            chart.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            chart.Height = 130;
            ui.Window.Settle();
            Assert.Contains(ChartProblems(ui.Window, "short chart"), static problem => problem.Contains("ends", StringComparison.Ordinal));

            // A cut single-line text (a label of the values).
            Ui.Find<TextBlock>(ui.Window, static block => block.Text == Strings.DiagnosticsHotendFan).Width = 40;
            ui.Window.Settle();
            Assert.Contains(CutTexts(ui.Window, "narrow"), static problem => problem.Contains(Strings.DiagnosticsHotendFan, StringComparison.Ordinal));
            return Task.CompletedTask;
        });

    [Fact]
    public Task ChartFillsTheRemainingHeightOfALargerWindow() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = OpenPrinting(scope);
            TemperatureChart chart = ui.Find<TemperatureChart>("DiagnosticsChart");
            double designed = chart.Bounds.Height;
            Assert.True(designed >= 200, $"The chart is {designed:0.#} DIP high.");
            Assert.Equal(780 - 32, chart.Bounds.Width, 0.01);

            ui.Window.Height = 608;
            ui.Window.Settle();

            Assert.Equal(designed + 100, chart.Bounds.Height, 0.01);
            Assert.Empty(LayoutProblems(ui.Window, "taller"));
            return Task.CompletedTask;
        });

    [Fact]
    public Task SwitchingPrintersKeepsTheLayout() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(
                scope,
                [Link("a", "MK4S workshop"), Link("b", "Core One")],
                Printing("a"));
            ui.CompleteInfo(new PrusaLinkInfoResult(PrinterState.Idle, null, Mk4SInfo));
            TemperatureChart chart = ui.Find<TemperatureChart>("DiagnosticsChart");
            Rect before = chart.Bounds;

            // The other printer has no values yet ("–") and its information is loading.
            ui.ViewModel.SelectedPrinter = ui.ViewModel.Printers[1];
            ui.Window.Settle();

            Assert.Equal(before, chart.Bounds);
            Assert.Empty(LayoutProblems(ui.Window, "switched"));
            return Task.CompletedTask;
        });

    // ---- Header ----

    [Fact]
    public Task SinglePrinterShowsItsNameAndSeveralPrintersASelector() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness single = OpenPrinting(scope);
            Assert.True(single.Window.IsShown<TextBlock>("DiagnosticsPrinterName"));
            Assert.False(single.Window.IsShown<ComboBox>("DiagnosticsPrinterSelector"));
            Assert.Equal("MK4S workshop", single.Find<TextBlock>("DiagnosticsPrinterName").Text);
            single.Window.Close();

            DiagnosticsWindowHarness several = DiagnosticsWindowHarness.Open(scope, [Link("a", "Alpha"), Link("b", "Beta")], Printing("a"));
            ComboBox selector = several.Find<ComboBox>("DiagnosticsPrinterSelector");
            Assert.True(selector.IsEffectivelyVisible);
            Assert.False(several.Window.IsShown<TextBlock>("DiagnosticsPrinterName"));
            // The selector sits in the header row of the 48 DIP image, where the name would be.
            Assert.True(selector.Bounds.Height <= 48);
            return Task.CompletedTask;
        });

    [Fact]
    public Task LongNameAndHostNameAreShortenedWithTheFullTextInATooltip() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = OpenLongName(scope);

            TextBlock name = ui.Find<TextBlock>("DiagnosticsPrinterName");
            Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
            Assert.True(name.TextLayout.TextLines.Any(static line => line.HasCollapsed), "The long name is not shortened.");
            Assert.Equal(LongName, ToolTip.GetTip(name));

            TextBlock host = ui.Find<TextBlock>("DiagnosticsHostname");
            Assert.True(host.Bounds.Width <= 200 + 0.01);
            Assert.True(host.TextLayout.TextLines.Any(static line => line.HasCollapsed), "The long host name is not shortened.");
            Assert.Equal(LongestInfo.Hostname, ToolTip.GetTip(host));
            return Task.CompletedTask;
        });

    [Fact]
    public Task DetailIsShownOnlyWithAReason() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness printing = OpenPrinting(scope);
            Assert.False(printing.Window.IsShown<TextBlock>("DiagnosticsDetail"));
            printing.Window.Close();

            DiagnosticsWindowHarness offline = OpenOffline(scope);
            Assert.True(offline.Window.IsShown<TextBlock>("DiagnosticsDetail"));
            Assert.Equal(LongestDetail, offline.Find<TextBlock>("DiagnosticsDetail").Text);
            return Task.CompletedTask;
        });

    [Fact]
    public Task ScreenReadersGetTheNamesOfTheSelectorsDetailAndPrinterInformation() =>
        session.RunAsync(static scope =>
        {
            // Through the automation peers, i.e. what UI Automation really reports (an attached name alone is not enough:
            // a text block reports its text, a panel has no peer of its own).
            DiagnosticsWindowHarness ui = OpenWorst(scope, loadedInfo: true);

            // The printer image in the header: an image with its localized name (T-069 Q3).
            AutomationPeer image = PeerOf(ui.Find<Control>("DiagnosticsPrinterImage"));
            Assert.True(image.IsControlElement());
            Assert.Equal(AutomationControlType.Image, image.GetAutomationControlType());
            Assert.Equal(Strings.DiagnosticsPrinterImage, image.GetName());

            AutomationPeer selector = PeerOf(ui.Find<ComboBox>("DiagnosticsPrinterSelector"));
            Assert.Equal(Strings.DiagnosticsPrinter, selector.GetName());
            Assert.Equal(Strings.DiagnosticsChart, PeerOf(ui.Find<ComboBox>("DiagnosticsChartRange")).GetName());

            AutomationPeer detailGroup = PeerOf(ui.Find<Control>("DiagnosticsDetailGroup"));
            Assert.True(detailGroup.IsControlElement());
            Assert.Equal(AutomationControlType.Group, detailGroup.GetAutomationControlType());
            Assert.Equal(Strings.DiagnosticsDetail, detailGroup.GetName());
            AutomationPeer detail = PeerOf(ui.Find<TextBlock>("DiagnosticsDetail"));
            Assert.Equal(LongestDetail, detail.GetName());
            Assert.Same(detailGroup, detail.GetParent());

            AutomationPeer info = PeerOf(ui.Find<Control>("DiagnosticsPrinterInfo"));
            Assert.True(info.IsControlElement());
            Assert.Equal(AutomationControlType.Group, info.GetAutomationControlType());
            Assert.Equal(Strings.DiagnosticsPrinterInfo, info.GetName());
            // The pairs are inside the group (through peers of panels that UI Automation skips).
            Assert.Contains(Descendants(info), static peer => peer.GetName() == Strings.DiagnosticsFirmware);
            Assert.Contains(Descendants(info), static peer => peer.GetName() == LongestInfo.Firmware);
            return Task.CompletedTask;
        });

    // ---- Footer: printer information or its status ----

    [Fact]
    public Task FooterShowsTheStatusUntilTheInformationIsLoaded() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(scope, [Link("a", "MK4S workshop")], Printing("a"));

            Assert.Equal(Strings.DiagnosticsInfoLoading, ShownText(ui.Window, "DiagnosticsInfoStatus"));
            Assert.False(ui.Window.IsShown<Panel>("DiagnosticsInfoValues"));

            ui.CompleteInfo(new PrusaLinkInfoResult(PrinterState.Idle, null, Mk4SInfo));

            Assert.Null(ShownText(ui.Window, "DiagnosticsInfoStatus"));
            Assert.True(ui.Window.IsShown<Panel>("DiagnosticsInfoValues"));
            List<string> texts = Ui.VisibleTexts(ui.Find<Panel>("DiagnosticsInfoValues"));
            Assert.Equal(
                [
                    Strings.DiagnosticsFirmware, "6.2.1+8896", Strings.DiagnosticsModel, "1.4.0", Strings.DiagnosticsHostname, "prusa-mk4s",
                    Strings.DiagnosticsNozzleDiameter, "0.40 mm", Strings.DiagnosticsMmu, Strings.DiagnosticsNo,
                ],
                texts);
            // Close shares the footer row, right aligned.
            Button close = ui.Find<Button>("DiagnosticsClose");
            Assert.Equal(780 - 16, close.TranslatePoint(new Point(close.Bounds.Width, 0), ui.Window)!.Value.X, 0.5);
            return Task.CompletedTask;
        });

    [Fact]
    public Task WithoutAPrinterOnlyTheNoticeAndCloseAreShown() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(scope, []);

            Assert.Equal(Strings.ToolTipNoPrinter, ShownText(ui.Window, "DiagnosticsNotice"));
            Assert.True(ui.Window.IsShown<Button>("DiagnosticsClose"));
            foreach (string id in (ReadOnlySpan<string>)["DiagnosticsHeader", "DiagnosticsValues", "DiagnosticsChartHeader", "DiagnosticsPrinterInfo"])
            {
                Assert.False(ui.Window.IsShown<Control>(id), id);
            }

            Assert.False(ui.Window.IsShown<TemperatureChart>("DiagnosticsChart"));
            return Task.CompletedTask;
        });

    // ---- Keyboard ----

    [Fact]
    public Task TabMovesFromThePrinterSelectorToTheRangeToClose() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(scope, [Link("a", "Alpha"), Link("b", "Beta")], Printing("a"));
            ui.CompleteInfo(new PrusaLinkInfoResult(PrinterState.Idle, null, Mk4SInfo));
            Assert.True(ui.Find<ComboBox>("DiagnosticsPrinterSelector").Focus());

            List<string> order = TabStops(ui.Window);

            Assert.Equal(["DiagnosticsChartRange", "DiagnosticsClose"], order);
            return Task.CompletedTask;
        });

    [Fact]
    public Task EscapeClosesTheWindow() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = OpenPrinting(scope);
            Assert.True(ui.Find<ComboBox>("DiagnosticsChartRange").Focus());
            bool closed = false;
            ui.Window.Closed += (_, _) => closed = true;

            ui.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(closed);
            return Task.CompletedTask;
        });

    // ---- Language ----

    [Fact]
    public Task TextsFollowALanguageSwitchAndStillFit() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = OpenWorst(scope, loadedInfo: true);
            Assert.True(Ui.ShowsText(ui.Window, "Fans and motion"));

            // As App.OnLanguageChanged: the resources switch, then the open window formats its texts again.
            Assert.True(new UiLanguage().Apply("it"));
            ui.Slot.RefreshTexts();
            ui.Window.Settle();

            Assert.True(Ui.ShowsText(ui.Window, Strings.DiagnosticsMotionAndFans));
            Assert.False(Ui.ShowsText(ui.Window, "Fans and motion"));
            Assert.Equal(Strings.ToolTipMonitoringFailed, ShownText(ui.Window, "DiagnosticsNotice"));
            Assert.Equal(Strings.DiagnosticsClose, ui.Find<Button>("DiagnosticsClose").Content);
            Assert.Equal(Strings.DiagnosticsPrinterImage, PeerOf(ui.Find<Control>("DiagnosticsPrinterImage")).GetName());
            Assert.NotEqual("Printer image", Strings.DiagnosticsPrinterImage);
            Assert.Empty(LayoutProblems(ui.Window, "it"));
            return Task.CompletedTask;
        });

    // ---- States of the no-scroll-bar test (T-041, "Testy pro implementaci") ----

    /// <summary>A name longer than the header (about 80 characters; the design measured 60).</summary>
    private const string LongName = "Original Prusa Core One L – workshop, second floor, left rack next to the window";

    private static IEnumerable<(string Name, Func<UiTestScope, DiagnosticsWindowHarness> Open)> States()
    {
        yield return ("printing", OpenPrinting);
        yield return ("idle, dark theme", static scope =>
        {
            DiagnosticsWindowHarness ui = OpenIdle(scope);
            ui.Window.RequestedThemeVariant = ThemeVariant.Dark;
            return ui;
        });
        yield return ("offline with the longest detail and a date", OpenOffline);
        yield return ("connecting, information loading", static scope => DiagnosticsWindowHarness.Open(scope, [Link("a", "MK4S workshop")]));
        yield return ("Prusa Connect, information not available", static scope =>
        {
            DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(scope, [Cloud("c", "Cloud XL")], Printing("c"));
            ui.CompleteInfo(null);
            return ui;
        });
        yield return ("long name and host name", OpenLongName);
        yield return ("worst: selector, notice, longest state, detail, failed information", static scope => OpenWorst(scope, loadedInfo: false));
        yield return ("worst with loaded information", static scope => OpenWorst(scope, loadedInfo: true));
        yield return ("no printer", static scope => DiagnosticsWindowHarness.Open(scope, []));
        yield return ("starting", static scope => DiagnosticsWindowHarness.Open(scope, printers: null));
    }

    internal static DiagnosticsWindowHarness OpenPrinting(UiTestScope scope)
    {
        DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(scope, [Link("a", "MK4S workshop")], Printing("a"));
        ui.CompleteInfo(new PrusaLinkInfoResult(PrinterState.Printing, null, Mk4SInfo));
        return ui;
    }

    internal static DiagnosticsWindowHarness OpenIdle(UiTestScope scope)
    {
        PrinterSnapshot idle = Printing("a") with
        {
            State = PrinterState.Idle,
            Telemetry = new Telemetry { Nozzle = new Temperature(27.4, 0), Bed = new Temperature(25.1, 0), HotendFanRpm = 0, PrintFanRpm = 0, SpeedPercent = 100, FlowPercent = 100, AxisZ = 5 },
        };
        DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(scope, [Link("a", "MK4S workshop")], idle);
        ui.CompleteInfo(new PrusaLinkInfoResult(PrinterState.Idle, null, Mk4SInfo));
        return ui;
    }

    internal static DiagnosticsWindowHarness OpenOffline(UiTestScope scope)
    {
        // Seen yesterday: the last update shows the date as well.
        DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(
            scope,
            [Link("a", "MK4S workshop")],
            PrinterSnapshot.Unavailable("a", PrinterState.Offline, TestPrinters.Now.AddDays(-1).AddMinutes(-17), LongestDetail));
        ui.CompleteInfo(new PrusaLinkInfoResult(PrinterState.Offline, "The printer is not reachable.", null));
        return ui;
    }

    internal static DiagnosticsWindowHarness OpenLongName(UiTestScope scope)
    {
        DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(scope, [Link("a", LongName)], Printing("a"));
        ui.CompleteInfo(new PrusaLinkInfoResult(PrinterState.Printing, null, LongestInfo));
        return ui;
    }

    /// <summary>
    /// Three printers (a selector with long names), monitoring failed (notice), the state with the longest name in the
    /// current language with the longest detail, and the information either loaded (longest values) or failed with the
    /// longest reason.
    /// </summary>
    internal static DiagnosticsWindowHarness OpenWorst(UiTestScope scope, bool loadedInfo)
    {
        PrinterState longest = Enum.GetValues<PrinterState>().MaxBy(static state => TrayText.StateName(state).Length);
        PrinterSnapshot snapshot = Printing("a") with { State = longest, StatusMessage = LongestDetail, Timestamp = TestPrinters.Now.AddDays(-1) };
        DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(
            scope,
            [Link("a", LongName), Link("b", "MK4S workshop"), Cloud("c", "Cloud XL")],
            snapshot);
        ui.FailMonitoring();
        PrinterState longestReason = Enum.GetValues<PrinterState>().MaxBy(static state => DiagnosticsFormatter.InfoUnavailable(state).Length);
        ui.CompleteInfo(loadedInfo ? new PrusaLinkInfoResult(longest, null, LongestInfo) : new PrusaLinkInfoResult(longestReason, "failed", null));
        Assert.True(ui.ViewModel.HasNotice);
        Assert.True(ui.ViewModel.HasPrinterSelector);
        Assert.True(ui.ViewModel.HasDetail);
        return ui;
    }

    /// <summary>A print with the widest realistic values.</summary>
    private static PrinterSnapshot Printing(string printerId) => new()
    {
        PrinterId = printerId,
        State = PrinterState.Printing,
        Timestamp = TestPrinters.Now.AddSeconds(-3),
        Telemetry = new Telemetry
        {
            Nozzle = new Temperature(288.8, 290),
            Bed = new Temperature(110.2, 110),
            HotendFanRpm = 12500,
            PrintFanRpm = 10800,
            SpeedPercent = 200,
            FlowPercent = 150,
            AxisZ = 288.88,
        },
    };

    /// <summary>A PrusaLink printer whose model (MK4S) was detected: the header shows its image (T-039).</summary>
    private static PrinterDefinition Link(string id, string name) => TestPrinters.Definition(id, name) with { DetectedModel = new PrinterModelCode(1, 4, 1) };

    private static PrinterDefinition Cloud(string id, string name) =>
        TestPrinters.Definition(id, name) with { ConnectionType = ConnectionType.PrusaConnect, Address = "c0ffee00-0000-4000-8000-000000000001" };

    // ---- Checks ----

    internal static IEnumerable<string> LayoutProblems(Window window, string context) =>
        [.. ScrollingViewers(window, context), .. CutTexts(window, context), .. ChartProblems(window, context)];

    /// <summary>Own scroll viewers (not the ones inside a combo box) that would need to scroll.</summary>
    private static IEnumerable<string> ScrollingViewers(Window window, string context)
    {
        List<ScrollViewer> viewers = [.. window.GetVisualDescendants().OfType<ScrollViewer>().Where(static viewer => viewer.TemplatedParent is null && viewer.IsEffectivelyVisible)];
        if (viewers.Count == 0)
        {
            yield return $"{context}: no scroll viewer of the content is shown.";
        }

        foreach (ScrollViewer viewer in viewers)
        {
            if (viewer.Extent.Height > viewer.Viewport.Height + 0.01)
            {
                yield return $"{context}: the content is {viewer.Extent.Height:0.#} DIP high, {viewer.Extent.Height - viewer.Viewport.Height:0.#} more than the {viewer.Viewport.Height:0.#} visible.";
            }

            if (viewer.Extent.Width > viewer.Viewport.Width + 0.01)
            {
                yield return $"{context}: the content is {viewer.Extent.Width:0.#} DIP wide, more than the {viewer.Viewport.Width:0.#} visible.";
            }
        }
    }

    /// <summary>Visible single-line texts that are narrower than the text they show (cut off by their container).</summary>
    private static IEnumerable<string> CutTexts(Window window, string context)
    {
        foreach (TextBlock block in window.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!block.IsEffectivelyVisible || block.TextWrapping != TextWrapping.NoWrap || block.TextTrimming != TextTrimming.None || string.IsNullOrEmpty(block.Text))
            {
                continue;
            }

            TextBlock probe = new()
            {
                Text = block.Text,
                FontFamily = block.FontFamily,
                FontSize = block.FontSize,
                FontWeight = block.FontWeight,
                FontStyle = block.FontStyle,
                FontStretch = block.FontStretch,
            };
            probe.Measure(Size.Infinity);
            if (probe.DesiredSize.Width > block.Bounds.Width + 0.5)
            {
                yield return $"{context}: '{block.Text}' needs {probe.DesiredSize.Width:0.#} DIP but has {block.Bounds.Width:0.#}.";
            }
        }
    }

    /// <summary>A shown chart lower than its minimum, or one that does not reach the footer while nothing scrolls.</summary>
    private static IEnumerable<string> ChartProblems(Window window, string context)
    {
        TemperatureChart chart = window.Find<TemperatureChart>("DiagnosticsChart");
        if (!chart.IsEffectivelyVisible)
        {
            yield break;
        }

        if (chart.Bounds.Height < MinChartHeight - 0.01)
        {
            yield return $"{context}: the chart is {chart.Bounds.Height:0.#} DIP high, less than {MinChartHeight}.";
        }

        ScrollViewer viewer = window.Find<ScrollViewer>("DiagnosticsScroll");
        if (viewer.Extent.Height > viewer.Viewport.Height + 0.01)
        {
            yield break;
        }

        double chartBottom = chart.TranslatePoint(new Point(0, chart.Bounds.Height), window)!.Value.Y;
        double footerTop = window.Find<Control>("DiagnosticsFooter").TranslatePoint(default, window)!.Value.Y;
        if (Math.Abs(footerTop - FooterGap - chartBottom) > 0.5)
        {
            yield return $"{context}: the chart ends at {chartBottom:0.#} DIP, the footer starts at {footerTop:0.#} (gap {FooterGap} expected).";
        }
    }

    private static AutomationPeer PeerOf(Control control) => ControlAutomationPeer.CreatePeerForElement(control);

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer) =>
        peer.GetChildren().SelectMany(static child => (IEnumerable<AutomationPeer>)[child, .. Descendants(child)]);

    private static string? ShownText(Window window, string automationId) =>
        window.IsShown<TextBlock>(automationId) ? window.Find<TextBlock>(automationId).Text : null;

    // ---- Keyboard helpers ----

    /// <summary>Presses Tab until the focus comes back (at most 20 times); the automation ids of the focused controls in order.</summary>
    private static List<string> TabStops(Window window)
    {
        List<string> order = [];
        object? first = window.FocusManager?.GetFocusedElement();
        for (int i = 0; i < 20; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.Settle();
            object? focused = window.FocusManager?.GetFocusedElement();
            if (focused is null || ReferenceEquals(focused, first))
            {
                break;
            }

            order.Add((focused as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
                .Select(AutomationProperties.GetAutomationId)
                .FirstOrDefault(static id => !string.IsNullOrEmpty(id)) ?? focused.GetType().Name);
        }

        return order;
    }
}
