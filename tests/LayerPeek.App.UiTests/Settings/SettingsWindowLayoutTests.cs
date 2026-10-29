using System.Globalization;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Printers;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Images;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// Layout of the settings window (T-033): at its designed size everything fits without a scroll bar in every language and
/// state, headers stay on one row, single-line texts are not cut, the printer sub-tabs behave as designed, Tab moves in
/// reading order and the tab shortcuts work.
/// </summary>
public sealed class SettingsWindowLayoutTests(HeadlessSession session)
{
    /// <summary>Title bar assumed by the design (Windows 11 at 100 %); the smallest common laptop work area is about 1093 × 580 DIP (T-094).</summary>
    private const double TitleBar = 32;

    private static readonly Color LightErrorColor = Color.Parse("#C42B1C");
    private static readonly Color LightSuccessColor = Color.Parse("#0F7B0F");

    private static readonly CameraSettings WorkingCamera = new(enabled: true, new Uri("rtsp://192.168.0.227/live"));

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    /// <summary>The views of the window: main tab and, on the Printers tab, the printer sub-tab.</summary>
    private static readonly (string Name, int Tab, int PrinterTab)[] Views =
    [
        ("Printer", SettingsViewModel.PrintersTab, SettingsViewModel.ConnectionPrinterTab),
        ("Camera", SettingsViewModel.PrintersTab, SettingsViewModel.CameraPrinterTab),
        ("Camera window", SettingsViewModel.PrintersTab, SettingsViewModel.CameraWindowPrinterTab),
        ("Widgets", SettingsViewModel.WidgetsTab, SettingsViewModel.ConnectionPrinterTab),
        ("General", SettingsViewModel.GeneralTab, SettingsViewModel.ConnectionPrinterTab),
        ("Startup and updates", SettingsViewModel.StartupUpdatesTab, SettingsViewModel.ConnectionPrinterTab),
    ];

    // ---- Size ----

    [Fact]
    public Task WindowOpensAtTheDesignedSizeThatFitsTheSmallestScreen() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));

            Assert.Equal(new Size(780, 532), ui.Window.ClientSize);
            Assert.True(ui.Window.ClientSize.Width <= 780);
            Assert.True(ui.Window.ClientSize.Height + TitleBar <= 580);
            Assert.Equal(AuxiliaryWindowPlacement.DefaultClientSize, new Size(ui.Window.Width, ui.Window.Height));
            Assert.Equal(AuxiliaryWindowPlacement.MinClientSize, new Size(ui.Window.MinWidth, ui.Window.MinHeight));
        });

    // ---- No scroll bar, headers on one row, nothing cut (every language × state × view) ----

    [Theory]
    [MemberData(nameof(Languages))]
    public Task EverythingFitsWithoutScrollingInEveryState(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            List<string> problems = [];
            foreach ((string state, Func<UiTestScope, Task<SettingsWindowHarness>> open) in States())
            {
                SettingsWindowHarness ui = await open(scope);
                Assert.Equal(new Size(780, 532), ui.Window.ClientSize);
                Assert.False(ui.ViewModel.HasNotice);
                foreach ((string view, int tab, int printerTab) in Views)
                {
                    ui.ViewModel.SelectedTabIndex = tab;
                    ui.ViewModel.SelectedPrinterTabIndex = printerTab;
                    ui.Window.Settle();
                    string context = $"{language} / {state} / {view}";
                    problems.AddRange(ScrollingViewers(ui.Window, context));
                    problems.AddRange(CutTexts(ui.Window, context));
                    problems.AddRange(HeadersNotOnOneRow(ui.Window, context));
                }

                ui.Window.Close();
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });

    /// <summary>
    /// T-017: the longest texts of the Startup and updates tab, without an installation (both reasons shown) and with the
    /// failed autostart in the status line next to Save / Cancel, in every language.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public Task StartupAndUpdatesTabFitsInEveryLanguage(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            List<string> problems = [];

            SettingsWindowHarness notInstalled = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")), autostartNeedsInstallation: true);
            notInstalled.ViewModel.SelectedTabIndex = SettingsViewModel.StartupUpdatesTab;
            notInstalled.Window.Settle();
            problems.AddRange(ScrollingViewers(notInstalled.Window, $"{language} / not installed"));
            problems.AddRange(CutTexts(notInstalled.Window, $"{language} / not installed"));
            notInstalled.Window.Close();

            SettingsWindowHarness failed = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")),
                autostart: new MemoryAutostart { Failure = new UnauthorizedAccessException("denied") });
            failed.ViewModel.SelectedTabIndex = SettingsViewModel.StartupUpdatesTab;
            failed.ViewModel.StartWithWindows = true;
            await failed.ViewModel.SaveCommand.ExecuteAsync(null);
            failed.Window.Settle();
            Assert.Equal(Strings.SettingsSavedAutostartFailed, failed.ViewModel.StatusText);
            problems.AddRange(ScrollingViewers(failed.Window, $"{language} / autostart failed"));
            problems.AddRange(CutTexts(failed.Window, $"{language} / autostart failed"));
            failed.Window.Close();

            // T-019: macOS ("Open at Login", its own reason, no pre-release option).
            SettingsWindowHarness mac = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")), autostartNeedsInstallation: true, macTerms: true);
            mac.ViewModel.SelectedTabIndex = SettingsViewModel.StartupUpdatesTab;
            mac.Window.Settle();
            Assert.Equal(Strings.SettingsOpenAtLogin, mac.Find<CheckBox>("SettingsStartWithWindows").Content);
            Assert.Equal(Strings.SettingsNeedsAppBundle, mac.Find<TextBlock>("SettingsStartWithWindowsNotInstalled").Text);
            Assert.False(mac.Find<CheckBox>("SettingsOfferPrereleaseUpdates").IsEffectivelyVisible);
            problems.AddRange(ScrollingViewers(mac.Window, $"{language} / macOS not in LayerPeek.app"));
            problems.AddRange(CutTexts(mac.Window, $"{language} / macOS not in LayerPeek.app"));
            mac.Window.Close();

            // T-019 (user decisions 2026-10-29): switched off in System Settings. Opening the window shows the hint and the button
            // under the option; Save says so in a macOS status of its own; the button opens System Settings (here only counted).
            MemoryApprovalAutostart approval = new()
            {
                RequiresApproval = true,
                Failure = new LayerPeek.App.Platform.AutostartNeedsApprovalException("requires approval"),
            };
            SettingsWindowHarness needsApproval = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")), autostart: approval, macTerms: true);
            needsApproval.ViewModel.SelectedTabIndex = SettingsViewModel.StartupUpdatesTab;
            needsApproval.Window.Settle();
            TextBlock hint = needsApproval.Find<TextBlock>("SettingsOpenAtLoginNeedsApproval");
            Button open = needsApproval.Find<Button>("SettingsOpenLoginItems");
            Assert.True(hint.IsEffectivelyVisible);
            Assert.True(open.IsEffectivelyVisible);
            Assert.Equal(Strings.SettingsOpenAtLoginNeedsApproval, hint.Text);
            Assert.Equal(Strings.SettingsOpenLoginItems, open.Content);
            Assert.Equal(Strings.SettingsOpenLoginItemsHint, AutomationProperties.GetHelpText(open));
            problems.AddRange(ApprovalHintProblems(needsApproval, hint, open, $"{language} / macOS needs approval"));

            needsApproval.ViewModel.StartWithWindows = true;
            await needsApproval.ViewModel.SaveCommand.ExecuteAsync(null);
            needsApproval.Window.Settle();
            Assert.Equal(Strings.SettingsSavedOpenAtLoginNeedsApproval, needsApproval.ViewModel.StatusText);
            Assert.True(open.IsEffectivelyVisible);
            string context = $"{language} / macOS needs approval after Save";
            problems.AddRange(ApprovalHintProblems(needsApproval, hint, open, context));
            problems.AddRange(StatusRowProblems(needsApproval, context));
            Ui.Click(needsApproval.Window, Ui.CentreOf(open, needsApproval.Window));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, approval.Opened);
            needsApproval.Window.Close();

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });

    /// <summary>
    /// T-020 (macOS, user decision 2026-10-29): notifications turned off in System Settings put a warning and a button into the
    /// heading row of the notifications on the General tab; in every language nothing is cut, nothing scrolls, the warning is
    /// readable next to the heading and the button opens System Settings (here only counted). Also maximized.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public Task GeneralTabWithNotificationsTurnedOffFitsInEveryLanguage(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            List<string> problems = [];
            MemoryNotificationSettings system = new() { TurnedOff = true };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")), autostartNeedsInstallation: true, macTerms: true, notificationSettings: system);
            ui.Click("SettingsTabGeneral");
            ui.Window.Settle();

            TextBlock warning = ui.Find<TextBlock>("SettingsNotificationsTurnedOff");
            Button open = ui.Find<Button>("SettingsOpenNotificationSettings");
            Assert.True(warning.IsEffectivelyVisible);
            Assert.True(open.IsEffectivelyVisible);
            Assert.Equal(Strings.SettingsNotificationsTurnedOff, warning.Text);
            Assert.Equal(Strings.SettingsOpenNotificationSettings, open.Content);
            Assert.Equal(Strings.SettingsOpenNotificationSettingsHint, AutomationProperties.GetHelpText(open));
            foreach (Size size in new[] { new Size(780, 532), new Size(1366, 705) })
            {
                ui.Window.Width = size.Width;
                ui.Window.Height = size.Height;
                ui.Window.Settle();
                string context = $"{language} / notifications turned off / {size}";
                problems.AddRange(ScrollingViewers(ui.Window, context));
                problems.AddRange(CutTexts(ui.Window, context));
                if (warning.TextLayout.TextLines.Count > 2)
                {
                    problems.Add($"{context}: the warning '{warning.Text}' takes {warning.TextLayout.TextLines.Count} lines.");
                }

                if (warning.Bounds.Width < 160)
                {
                    problems.Add($"{context}: the warning is only {warning.Bounds.Width:0.#} DIP wide.");
                }

                Point buttonCorner = open.TranslatePoint(new Point(open.Bounds.Width, open.Bounds.Height), ui.Window) ?? default;
                if (buttonCorner.X > ui.Window.ClientSize.Width + 0.5)
                {
                    problems.Add($"{context}: the button ends at {buttonCorner.X:0.#} DIP, outside the window.");
                }
            }

            Ui.Click(ui.Window, Ui.CentreOf(open, ui.Window));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, system.Opened);

            // Allowed again (the window became active again): warning and button go.
            system.TurnedOff = false;
            await ui.ViewModel.RefreshNotificationPermissionAsync();
            ui.Window.Settle();
            Assert.False(warning.IsEffectivelyVisible);
            Assert.False(open.IsEffectivelyVisible);
            ui.Window.Close();

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });

    /// <summary>
    /// T-094: maximized (here the client of a maximized window on 1366 × 768 and on 1920 × 1080 at 100 %), the content
    /// stretches with its * columns, nothing scrolls, nothing is cut and the headers stay on one row.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public Task MaximizedWindowStretchesWithoutScrollingOrCutTexts(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            List<string> problems = [];
            SettingsWindowHarness ui = await OpenWithAllErrorsAsync(scope, SettingsWindowHarness.Supported, refusedTest: true);
            ui.Window.WindowState = WindowState.Maximized;
            foreach (Size maximized in new[] { new Size(1366, 705), new Size(1920, 1017) })
            {
                ui.Window.Width = maximized.Width;
                ui.Window.Height = maximized.Height;
                ui.Window.Settle();
                Assert.Equal(maximized, ui.Window.ClientSize);
                foreach ((string view, int tab, int printerTab) in Views)
                {
                    ui.ViewModel.SelectedTabIndex = tab;
                    ui.ViewModel.SelectedPrinterTabIndex = printerTab;
                    ui.Window.Settle();
                    string context = $"{language} / maximized {maximized} / {view}";
                    problems.AddRange(ScrollingViewers(ui.Window, context));
                    problems.AddRange(CutTexts(ui.Window, context));
                    problems.AddRange(HeadersNotOnOneRow(ui.Window, context));
                    TabControl tabs = ui.Find<TabControl>("SettingsTabs");
                    if (tabs.Bounds.Width < maximized.Width - 64)
                    {
                        problems.Add($"{context}: the tabs are {tabs.Bounds.Width:0.#} DIP wide, they do not stretch.");
                    }
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });

    [Fact]
    public Task ChecksOfTheLayoutTestDetectProblems() =>
        session.RunAsync(static async scope =>
        {
            // The checks above pass only because the layout fits, not because they cannot fail.
            Assert.True(new UiLanguage().Apply("fr"));
            SettingsWindowHarness ui = await OpenWithAllErrorsAsync(scope, SettingsWindowHarness.Supported, refusedTest: true);
            // At the designed size the four sub-tab headers fit on one row; at the minimum size (640, a fallback) they may wrap.
            Assert.Empty(HeadersNotOnOneRow(ui.Window, "headers"));
            ui.Window.Width = 640;
            ui.Window.Height = 400;
            ui.Window.Settle();

            Assert.NotEmpty(ScrollingViewers(ui.Window, "small"));

            ui.Find<Button>("SettingsAddPrinter").Width = 40;
            ui.Window.Settle();

            Assert.Contains(CutTexts(ui.Window, "narrow"), static problem => problem.Contains(Strings.SettingsAddPrinter, StringComparison.Ordinal));

            ui.Find<TabControl>("SettingsPrinterTabs").Width = 120;
            ui.Window.Settle();

            Assert.Contains(HeadersNotOnOneRow(ui.Window, "narrow tabs"), static problem => problem.Contains("SettingsPrinterTabs", StringComparison.Ordinal));
        });

    [Theory]
    [MemberData(nameof(Languages))]
    public Task MarkersOnAllSubTabHeadersStillFitOnOneRow(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            SettingsWindowHarness ui = await OpenWithAllErrorsAsync(scope, SettingsWindowHarness.Supported, refusedTest: false);

            Assert.True(ui.Window.IsShown<TextBlock>("PrinterTabConnectionMarker"));
            Assert.True(ui.Window.IsShown<TextBlock>("PrinterTabCameraMarker"));
            Assert.True(ui.Window.IsShown<TextBlock>("PrinterTabCameraWindowMarker"));
            // Three headers (Printer | Camera | Camera window; the Overlay sub-tab gave way to the Widgets tab, T-092).
            Assert.Equal(SettingsViewModel.PrinterTabCount, ui.Find<TabControl>("SettingsPrinterTabs").ItemCount);
            Assert.Equal(3, SettingsViewModel.PrinterTabCount);
            List<string> problems = [.. HeadersNotOnOneRow(ui.Window, language), .. CutTexts(ui.Window, language)];
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });

    [Theory]
    [MemberData(nameof(Languages))]
    public Task DebugLoggingHintTakesAtMostTwoLines(string language) =>
        session.RunAsync(async scope =>
        {
            // T-047: the last row of the General tab; a third line would push the tab past its height (T-033 fit).
            Assert.True(new UiLanguage().Apply(language));
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            ui.Click("SettingsTabGeneral");
            ui.Window.Settle();

            TextBlock hint = ui.Find<TextBlock>("SettingsDebugLoggingHint");
            Assert.True(hint.IsEffectivelyVisible);
            Assert.Equal(Strings.SettingsDebugLoggingHint, hint.Text);
            Assert.True(hint.TextLayout.TextLines.Count <= 2, $"{language}: '{hint.Text}' has {hint.TextLayout.TextLines.Count} lines.");
            Assert.Equal(Strings.SettingsDebugLogging, ui.Find<CheckBox>("SettingsDebugLogging").Content);
        });

    [Theory]
    [MemberData(nameof(Languages))]
    public Task AlwaysOnTopHeaderFitsItsColumnInAtMostTwoLines(string language) =>
        session.RunAsync(async scope =>
        {
            // T-033 design / T-037: the header of the 88 DIP column wraps to at most 2 lines and no word is cut.
            Assert.True(new UiLanguage().Apply(language));
            SettingsWindowHarness ui = await OpenWithCameraWindowAsync(scope, cameraEnabled: true);
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraWindowPrinterTab;
            ui.Window.Settle();
            TextBlock header = ui.Find<TextBlock>("PrinterCameraWindowTopmostHeader");
            Assert.True(header.IsEffectivelyVisible);
            Assert.Equal(Strings.SettingsCameraWindowTopmost, header.Text);

            Assert.True(header.Bounds.Width <= 88 + 0.01, $"{language}: header is {header.Bounds.Width:0.#} DIP wide.");
            Assert.True(header.TextLayout.TextLines.Count <= 2, $"{language}: '{header.Text}' has {header.TextLayout.TextLines.Count} lines.");
            Assert.False(header.TextLayout.TextLines.Any(static line => line.HasCollapsed), $"{language}: '{header.Text}' is trimmed.");
            foreach (string word in header.Text!.Split(' '))
            {
                TextBlock probe = new() { Text = word, FontFamily = header.FontFamily, FontSize = header.FontSize, FontWeight = header.FontWeight };
                probe.Measure(Size.Infinity);
                Assert.True(probe.DesiredSize.Width <= 88 + 0.5, $"{language}: '{word}' needs {probe.DesiredSize.Width:0.#} DIP.");
            }

            foreach (string id in (ReadOnlySpan<string>)["PrinterCameraWindowNormalTopmost", "PrinterCameraWindowMiniTopmost"])
            {
                CheckBox box = ui.Find<CheckBox>(id);
                Assert.True(box.Bounds.Width <= 88 + 0.01, $"{language}: {id} is {box.Bounds.Width:0.#} DIP wide.");
                Assert.True(box.IsEffectivelyVisible);
            }
        });

    // ---- Printer sub-tabs ----

    [Fact]
    public Task SubTabStaysWhenAnotherPrinterOrTabIsSelected() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { Camera = WorkingCamera }, LinkPrinter("b", "Beta", "192.168.1.21") with { Camera = new CameraSettings(enabled: false, new Uri("rtsp://10.0.0.9/live")) }));

            ui.Click("PrinterTabCamera");
            Assert.Equal("rtsp://192.168.0.227/live", ui.Find<TextBox>("PrinterCameraUrl").Text);
            ListBox list = ui.Find<ListBox>("SettingsPrinterList");
            ui.Window.Click(Assert.IsType<ListBoxItem>(list.ContainerFromIndex(1)));

            Assert.Equal(SettingsViewModel.CameraPrinterTab, ui.ViewModel.SelectedPrinterTabIndex);
            Assert.True(ui.Find<TabItem>("PrinterTabCamera").IsSelected);
            Assert.Equal("rtsp://10.0.0.9/live", ui.Find<TextBox>("PrinterCameraUrl").Text);

            ui.Click("SettingsTabGeneral");
            ui.Click("SettingsTabPrinters");

            Assert.True(ui.Find<TabItem>("PrinterTabCamera").IsSelected);
            Assert.Equal("rtsp://10.0.0.9/live", ui.Find<TextBox>("PrinterCameraUrl").Text);
        });

    [Fact]
    public Task SaveWithACameraErrorOpensTheCameraSubTabWithItsMarker() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha"));
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
            ui.ViewModel.SelectedPrinter!.CameraEnabled = true;
            ui.Click("SettingsTabGeneral");

            await ui.SaveAsync();

            Assert.Empty(store.Saves);
            Assert.True(ui.Find<TabItem>("SettingsTabPrinters").IsSelected);
            TabItem camera = ui.Find<TabItem>("PrinterTabCamera");
            Assert.True(camera.IsSelected);
            Assert.Equal(Strings.SettingsCameraErrorMissing, ui.ShownText("PrinterCameraUrlError"));
            Assert.True(ui.Window.IsShown<TextBlock>("PrinterTabCameraMarker"));
            Assert.False(ui.Window.IsShown<TextBlock>("PrinterTabConnectionMarker"));
            Assert.Equal("Camera – contains errors", AutomationProperties.GetName(camera));
            Assert.Equal("Printer", AutomationProperties.GetName(ui.Find<TabItem>("PrinterTabConnection")));

            // Fixing the field removes the marker right away.
            ui.Window.TypeOver(ui.Find<TextBox>("PrinterCameraUrl"), "rtsp://192.168.0.227/live");
            Assert.False(ui.Window.IsShown<TextBlock>("PrinterTabCameraMarker"));
            Assert.Equal("Camera", AutomationProperties.GetName(camera));
        });

    [Fact]
    public Task ErrorReplacesTheHintOfItsFieldOnly() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            Assert.Equal(Strings.SettingsAddressHint, ui.ShownText("PrinterAddressHint"));

            ui.Window.TypeOver(ui.AddressBox, "ftp://printer");

            Assert.Null(ui.ShownText("PrinterAddressHint"));
            Assert.Equal(Strings.SettingsErrorAddressInvalid, ui.ShownText("PrinterAddressError"));
            Assert.Equal(Strings.SettingsUserNameHint, ui.ShownText("PrinterUserNameHint"));

            ui.Window.TypeOver(ui.AddressBox, "192.168.1.30");

            Assert.Equal(Strings.SettingsAddressHint, ui.ShownText("PrinterAddressHint"));
            Assert.Null(ui.ShownText("PrinterAddressError"));
        });

    [Fact]
    public Task PollingIntervalErrorReplacesItsHint() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            ui.Click("SettingsTabGeneral");
            Assert.Equal(Strings.SettingsPollingIntervalHint, ui.ShownText("SettingsPollingIntervalHint"));

            ui.ViewModel.PollingIntervalSeconds = null;
            ui.Window.Settle();

            Assert.Null(ui.ShownText("SettingsPollingIntervalHint"));
            Assert.NotNull(ui.ShownText("SettingsPollingIntervalError"));
        });

    // ---- Test results ----

    [Fact]
    public Task SuccessfulTestResultsAreGreenAndFailuresRed() =>
        session.RunAsync(static async scope =>
        {
            FakeConnectionTester tester = new() { Immediate = new ConnectionTestResult(ConnectionTestOutcome.Success, "7.0.0", "6.2.0") };
            FakeCameraTester cameraTester = new() { Immediate = new CameraTestResult(CameraTestOutcome.Success, 1920, 1080) };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { Camera = WorkingCamera }),
                tester,
                cameraTester: cameraTester);

            ui.Click("PrinterTestConnection");
            Assert.Equal(LightSuccessColor, ForegroundOf(ui.Find<TextBlock>("PrinterTestResult")));

            ui.Click("PrinterTabCamera");
            ui.Click("PrinterCameraTest");
            Assert.Equal("The camera works (1920\u00A0×\u00A01080\u00A0px).", ui.ShownText("PrinterCameraTestResult"));
            Assert.Equal(LightSuccessColor, ForegroundOf(ui.Find<TextBlock>("PrinterCameraTestResult")));

            cameraTester.Immediate = new CameraTestResult(CameraTestOutcome.Failed);
            ui.Click("PrinterCameraTest");
            Assert.Equal(LightErrorColor, ForegroundOf(ui.Find<TextBlock>("PrinterCameraTestResult")));
        });

    [Fact]
    public Task RunningTestIsNeitherGreenNorRed() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { Camera = WorkingCamera }));
            ui.Click("PrinterTabCamera");

            ui.Click("PrinterCameraTest");

            TextBlock result = ui.Find<TextBlock>("PrinterCameraTestResult");
            Assert.Equal(Strings.SettingsCameraTesting, result.Text);
            Assert.NotEqual(LightSuccessColor, ForegroundOf(result));
            Assert.NotEqual(LightErrorColor, ForegroundOf(result));
        });

    // ---- Keyboard ----

    [Fact]
    public Task TabMovesThroughThePrinterSubTabInReadingOrder() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            Assert.True(ui.Find<TabItem>("SettingsTabPrinters").Focus());

            List<string> order = TabStops(ui.Window);

            Assert.Equal(
                [
                    "SettingsPrinterList", "SettingsAddPrinter", "SettingsRemovePrinter", "PrinterTabConnection", "PrinterImageButton", "PrinterName",
                    "PrinterConnectionType", "PrinterAddress", "PrinterUserName", "PrinterPassword", "PrinterTestConnection",
                    "SettingsSave", "SettingsCancel",
                ],
                order.Where(static id => id is not ("SettingsTabPrinters" or "SettingsTabs")).Distinct());
        });

    [Fact]
    public Task TabMovesThroughTheCameraSubTabInReadingOrder() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            ui.Click("PrinterTabCamera");
            Assert.True(ui.Find<TabItem>("PrinterTabCamera").Focus());

            List<string> order = TabStops(ui.Window);

            int start = order.IndexOf("PrinterCameraEnabled");
            Assert.True(start >= 0, string.Join(", ", order));
            Assert.Equal(["PrinterCameraEnabled", "PrinterCameraUrl", "PrinterCameraTest", "SettingsSave", "SettingsCancel"], order.Skip(start).Take(5));
        });

    [Fact]
    public Task TabMovesThroughTheGeneralTabByRows() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            ui.Click("SettingsTabGeneral");
            Assert.True(ui.Find<TabItem>("SettingsTabGeneral").Focus());

            List<string> order = TabStops(ui.Window);

            string[] expected = ["SettingsPollingInterval", "SettingsLanguage", "SettingsNotificationsEnabled", "SettingsSound", "SettingsRemainingTime", "SettingsDebugLogging", "SettingsSave", "SettingsCancel"];
            Assert.Equal(expected, order.Where(id => expected.Contains(id)).Distinct());
        });

    [Fact]
    public Task CtrlTabSwitchesTheMainTabsAndCtrlPageDownTheSubTabs() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            Window window = ui.Window;
            Assert.True(ui.NameBox.Focus());

            Press(window, PhysicalKey.PageDown, RawInputModifiers.Control);
            Assert.True(ui.Find<TabItem>("PrinterTabCamera").IsSelected);
            Assert.Equal("PrinterTabCamera", FocusedId(window));

            Press(window, PhysicalKey.PageUp, RawInputModifiers.Control);
            Assert.True(ui.Find<TabItem>("PrinterTabConnection").IsSelected);
            Assert.Equal("PrinterTabConnection", FocusedId(window));

            Assert.True(ui.AddressBox.Focus());
            Press(window, PhysicalKey.Tab, RawInputModifiers.Control);
            Assert.True(ui.Find<TabItem>("SettingsTabWidgets").IsSelected);
            Assert.Equal("SettingsTabWidgets", FocusedId(window));

            // On the Widgets tab there are no sub-tabs: the keys do nothing.
            Press(window, PhysicalKey.PageDown, RawInputModifiers.Control);
            Assert.Equal(SettingsViewModel.ConnectionPrinterTab, ui.ViewModel.SelectedPrinterTabIndex);

            Press(window, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(ui.Find<TabItem>("SettingsTabPrinters").IsSelected);
            Assert.Equal("SettingsTabPrinters", FocusedId(window));
        });

    // ---- Language ----

    [Fact]
    public Task SubTabHeadersFollowALanguageSwitch() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            ui.Window.TypeOver(ui.NameBox, " ");
            Assert.True(Ui.ShowsText(ui.Window, "Printer"));
            Assert.True(Ui.ShowsText(ui.Window, "Camera"));

            Assert.True(new UiLanguage().Apply("de"));
            ui.Slot.RefreshTexts();
            ui.Window.Settle();

            Assert.True(Ui.ShowsText(ui.Window, "Drucker"));
            Assert.True(Ui.ShowsText(ui.Window, "Kamera"));
            Assert.Equal("Drucker – enthält Fehler", AutomationProperties.GetName(ui.Find<TabItem>("PrinterTabConnection")));
            Assert.Equal("Kamera", AutomationProperties.GetName(ui.Find<TabItem>("PrinterTabCamera")));
        });

    // ---- States of the no-scroll-bar test (T-033, "Headless test bez posuvníku") ----

    private static IEnumerable<(string Name, Func<UiTestScope, Task<SettingsWindowHarness>> Open)> States()
    {
        yield return ("PrusaLink with a saved password", static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4S workshop", "192.168.1.30") with { Camera = WorkingCamera }, CloudPrinter("c", "Cloud XL"));
            store.Data = store.Data with { PrintersWithPassword = Ids("a") };
            return await SettingsWindowHarness.OpenAsync(scope, store);
        });

        yield return ("Prusa Connect", static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha"), CloudPrinter("c", "Cloud XL")));
            ui.ViewModel.SelectedPrinter = ui.ViewModel.Printers[1];
            return ui;
        });

        yield return ("all errors", static scope => OpenWithAllErrorsAsync(scope, SettingsWindowHarness.Supported, refusedTest: false));
        yield return ("all errors + refused test", static scope => OpenWithAllErrorsAsync(scope, SettingsWindowHarness.Supported, refusedTest: true));
        yield return ("passwords unsupported + all errors + refused test", static scope => OpenWithAllErrorsAsync(scope, PasswordPolicy.Unsupported, refusedTest: true));

        yield return ("name error + longest failed connection test", static scope => OpenWithNameErrorAndTestAsync(scope, LongestConnectionFailure()));
        yield return ("name error + successful connection test", static scope =>
            OpenWithNameErrorAndTestAsync(scope, new ConnectionTestResult(ConnectionTestOutcome.Success, "7.0.0", "6.4.0-RC1+15456")));

        yield return ("camera address error + refused test", static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            printer.CameraEnabled = true;
            printer.CameraUrl = LongestCameraAddressProblem();
            await printer.TestCameraCommand.ExecuteAsync(null);
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            return ui;
        });

        yield return ("camera test running", static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { Camera = WorkingCamera }));
            _ = ui.ViewModel.SelectedPrinter!.TestCameraCommand.ExecuteAsync(null);
            Assert.True(ui.ViewModel.SelectedPrinter.IsTestingCamera);
            return ui;
        });

        yield return ("longest failed camera test", static scope => OpenWithCameraTestAsync(scope, LongestCameraFailure(), removedCredentials: false));
        yield return ("successful camera test", static scope => OpenWithCameraTestAsync(scope, new CameraTestResult(CameraTestOutcome.Success, 1920, 1080), removedCredentials: false));
        yield return ("successful camera test + preview", static async scope =>
        {
            SettingsWindowHarness ui = await OpenWithCameraTestAsync(scope, new CameraTestResult(CameraTestOutcome.Success, 1920, 1080, Image: TestCameraImages.Solid(160, 90, 0xFFFF0000)), removedCredentials: false);
            Assert.True(ui.ViewModel.SelectedPrinter!.HasCameraPreview);
            return ui;
        });

        // Credentials removed + preview cannot happen (a successful test answers the notice, T-033 Q3); proven here.
        yield return ("camera credentials removed + successful camera test + preview", static async scope =>
        {
            CameraTestResult success = new(CameraTestOutcome.Success, 1920, 1080, Image: TestCameraImages.Solid(160, 90, 0xFFFF0000));
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { Camera = new CameraSettings(enabled: false, WorkingCamera.StreamUri) });
            store.Data = store.Data with { CamerasWithRemovedCredentials = Ids("a") };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store, cameraTester: new FakeCameraTester { Immediate = success });
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            Assert.True(printer.CameraCredentialsRemoved);
            await printer.TestCameraCommand.ExecuteAsync(null);
            Assert.True(printer.HasCameraPreview);
            Assert.False(printer.CameraCredentialsRemoved);
            ui.Window.Settle();
            return ui;
        });
        yield return ("camera credentials removed + longest failed camera test", static scope => OpenWithCameraTestAsync(scope, LongestCameraFailure(), removedCredentials: true));

        // Camera window (T-035): the longest monitor label is a disconnected monitor (the combo trims a longer name).
        yield return ("camera window: camera on, size errors", static async scope =>
        {
            SettingsWindowHarness ui = await OpenWithCameraWindowAsync(scope, cameraEnabled: true);
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            printer.NormalSizeText = "1 × 1";
            printer.MiniSizeText = "big";
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            Assert.NotNull(printer.NormalSizeError);
            Assert.NotNull(printer.MiniSizeError);
            ui.Window.Settle();
            return ui;
        });

        yield return ("camera window: camera off + invalid size", static async scope =>
        {
            SettingsWindowHarness ui = await OpenWithCameraWindowAsync(scope, cameraEnabled: false);
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            printer.MiniSizeText = "big";
            Assert.Null(printer.MiniSizeError);
            ui.Window.Settle();
            return ui;
        });

        // Widgets (T-092, design 3.3–3.7): the Cool-down panel with the longest message under the threshold, the camera off,
        // a single printer (both "… for all printers" disabled) and no printer at all.
        yield return ("widgets: cool-down selected, single printer", static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4S workshop") with { Camera = WorkingCamera }));
            SelectCooldown(ui);
            return ui;
        });

        yield return ("widgets: cool-down threshold error", static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4S workshop") with { Camera = WorkingCamera }, LinkPrinter("b", "Beta", "192.168.1.21")));
            ui.ViewModel.Printers[0].CooldownThresholdCelsius = 120;
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            Assert.Equal(SettingsViewModel.WidgetsTab, ui.ViewModel.SelectedTabIndex);
            Assert.NotNull(ui.ViewModel.Printers[0].CooldownThresholdError);
            ui.Window.Settle();
            return ui;
        });

        yield return ("widgets: camera off, cool-down selected", static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4S workshop") with { Camera = new CameraSettings(enabled: false, WorkingCamera.StreamUri) }, CloudPrinter("c", "Cloud XL")));
            SelectCooldown(ui);
            return ui;
        });

        // T-039: the longest import error of the printer image, in the status line, next to every field error.
        yield return ("all errors + longest image import error", static async scope =>
        {
            SettingsWindowHarness ui = await OpenWithAllErrorsAsync(scope, SettingsWindowHarness.Supported, refusedTest: true);
            ui.ViewModel.SelectedPrinter = ui.ViewModel.Printers[0];
            ui.ViewModel.ImageFilePicker = LongestImageError();
            await ui.ViewModel.Printers[0].ChooseCustomImageCommand.ExecuteAsync(null);
            Assert.Equal(ui.ViewModel.Printers[0].ImageError, ui.ViewModel.StatusText);
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.PrintersTab;
            ui.Window.Settle();
            return ui;
        });

        yield return ("General with errors", static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            ui.ViewModel.PollingIntervalSeconds = null;
            ui.ViewModel.RemainingTimeMinutes = null;
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            Assert.Equal(SettingsViewModel.GeneralTab, ui.ViewModel.SelectedTabIndex);
            return ui;
        });
    }

    /// <summary>The Cool-down widget on the Widgets tab.</summary>
    private static void SelectCooldown(SettingsWindowHarness ui)
    {
        ui.ViewModel.SelectedWidget = ui.ViewModel.Widgets.Single(static widget => widget.Id == Core.Cameras.Overlay.OverlayWidgetIds.Cooldown);
        ui.Window.Settle();
    }

    /// <summary>The longest message of every field of both sub-tabs (in the current language), after a save attempt.</summary>
    private static async Task<SettingsWindowHarness> OpenWithAllErrorsAsync(UiTestScope scope, PasswordPolicy policy, bool refusedTest)
    {
        FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4S workshop", "192.168.1.30"), LinkPrinter("b", "Beta", "192.168.1.21"));
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store, policy: policy);
        PrinterEditorViewModel printer = ui.ViewModel.Printers[0];
        printer.Name = Longest(
            ("", Strings.SettingsErrorNameRequired),
            (new string('n', SettingsViewModel.MaxNameLength + 1), Format(Strings.SettingsErrorNameTooLong, SettingsViewModel.MaxNameLength)),
            ("Beta", Strings.SettingsErrorNameDuplicate));
        printer.Address = "ftp://printer";
        printer.UserName = Longest(
            ("", Strings.SettingsErrorUserNameRequired),
            (new string('u', PrinterUserName.MaxLength + 1), Format(Strings.SettingsErrorUserNameTooLong, PrinterUserName.MaxLength)),
            ("a\u0001b", Strings.SettingsErrorUserNameControlCharacter),
            ("a:b", Strings.SettingsErrorUserNameColon),
            ("mäker", Strings.SettingsErrorUserNameUnsupported));
        printer.Password = Longest(
            (new string('p', SettingsWindowHarness.Supported.MaxBytes + 1), Format(Strings.SettingsErrorPasswordTooLong, SettingsWindowHarness.Supported.MaxBytes)),
            ("\uD800", Strings.SettingsErrorPasswordInvalid));
        printer.CameraEnabled = true;
        printer.CameraUrl = LongestCameraAddressProblem();
        printer.NormalSizeText = "1 × 1";
        printer.MiniSizeText = "big";
        if (refusedTest)
        {
            await printer.TestConnectionCommand.ExecuteAsync(null);
            Assert.Equal(Strings.SettingsFixErrors, printer.TestResultText);
        }

        await ui.ViewModel.SaveCommand.ExecuteAsync(null);
        Assert.Empty(store.Saves);
        Assert.True(printer.ShowsConnectionErrorMarker);
        Assert.True(printer.ShowsCameraErrorMarker);
        Assert.True(printer.ShowsCameraWindowErrorMarker);
        ui.Window.Settle();
        return ui;
    }

    /// <summary>A printer with a saved monitor that is not connected and the longest corner names, on three monitors.</summary>
    private static Task<SettingsWindowHarness> OpenWithCameraWindowAsync(UiTestScope scope, bool cameraEnabled)
    {
        CameraWindowDefaults window = new()
        {
            StartMode = CameraWindowStartMode.Last,
            Monitor = new CameraMonitorId("BenQ PD2700U", 2560, 1440),
            Normal = new CameraWindowLayout(new CameraWindowSize(1920, 1080), CameraWindowCorner.TopLeft),
            Mini = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.BottomLeft),
        };
        CameraSettings camera = new(cameraEnabled, WorkingCamera.StreamUri) { Window = window };
        return SettingsWindowHarness.OpenAsync(
            scope,
            SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4S workshop") with { Camera = camera }),
            screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors());
    }

    private static async Task<SettingsWindowHarness> OpenWithNameErrorAndTestAsync(UiTestScope scope, ConnectionTestResult result)
    {
        FakeConnectionTester tester = new() { Immediate = result };
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4S workshop")), tester);
        PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
        printer.Name = new string('n', SettingsViewModel.MaxNameLength + 1);
        await printer.TestConnectionCommand.ExecuteAsync(null);
        Assert.True(printer.HasTestResult);
        ui.Window.Settle();
        return ui;
    }

    private static async Task<SettingsWindowHarness> OpenWithCameraTestAsync(UiTestScope scope, CameraTestResult result, bool removedCredentials)
    {
        // The loader turns a camera with removed credentials off.
        CameraSettings camera = removedCredentials ? new CameraSettings(enabled: false, WorkingCamera.StreamUri) : WorkingCamera;
        FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { Camera = camera });
        if (removedCredentials)
        {
            store.Data = store.Data with { CamerasWithRemovedCredentials = Ids("a") };
        }

        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store, cameraTester: new FakeCameraTester { Immediate = result });
        PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
        await printer.TestCameraCommand.ExecuteAsync(null);
        Assert.True(printer.HasCameraTestResult);
        Assert.Equal(removedCredentials, printer.CameraCredentialsRemoved);
        ui.Window.Settle();
        return ui;
    }

    private static ConnectionTestResult LongestConnectionFailure()
    {
        ConnectionTestOutcome[] outcomes = [.. Enum.GetValues<ConnectionTestOutcome>().Where(static outcome => outcome != ConnectionTestOutcome.Success)];
        Dictionary<ConnectionTestOutcome, string> texts = [];
        foreach (ConnectionTestOutcome outcome in outcomes)
        {
            texts[outcome] = outcome switch
            {
                ConnectionTestOutcome.InvalidAddress => Strings.SettingsErrorAddressInvalid,
                ConnectionTestOutcome.NotReachable => Strings.SettingsTestNotReachable,
                ConnectionTestOutcome.Timeout => Strings.SettingsTestTimeout,
                ConnectionTestOutcome.SecureConnectionFailed => Strings.SettingsTestSecureConnection,
                ConnectionTestOutcome.PasswordRejected => Strings.SettingsTestPasswordRejected,
                ConnectionTestOutcome.AccessDenied => Strings.SettingsTestAccessDenied,
                ConnectionTestOutcome.PasswordMissing => Strings.SettingsTestPasswordMissing,
                ConnectionTestOutcome.PasswordUnavailable => Strings.SettingsTestPasswordUnavailable,
                ConnectionTestOutcome.UnsupportedAuthentication => Strings.SettingsTestUnsupportedAuthentication,
                ConnectionTestOutcome.NotSupported => Strings.SettingsTestNotSupported,
                _ => Strings.SettingsTestUnexpectedResponse,
            };
        }

        return new ConnectionTestResult(texts.MaxBy(static pair => pair.Value.Length).Key);
    }

    private static CameraTestResult LongestCameraFailure()
    {
        // A realistic long technical reason from the decoder.
        const string Reason = "Connection to tcp://192.168.0.227:554?timeout=5000000 failed: Connection timed out";
        string failed = Format(Strings.SettingsCameraTestFailed, Reason);
        string timeout = Format(Strings.SettingsCameraTestTimeout, 15);
        return failed.Length >= timeout.Length
            ? new CameraTestResult(CameraTestOutcome.Failed, Reason: Reason)
            : new CameraTestResult(CameraTestOutcome.Timeout);
    }

    private static string LongestCameraAddressProblem() => Longest(
        ("", Strings.SettingsCameraErrorMissing),
        ("http://192.168.0.227/live", Strings.SettingsCameraErrorScheme),
        ("rtsp://", Strings.SettingsCameraErrorHost),
        ("rtsp://admin:secret@192.168.0.227/live", Strings.SettingsCameraErrorCredentials),
        ("rtsp://[", Strings.SettingsCameraErrorInvalid));

    private static string Longest(params (string Input, string Message)[] candidates) =>
        candidates.MaxBy(static candidate => candidate.Message.Length).Input;

    /// <summary>A file dialog whose file gives the longest import error of the printer image (T-039) in the current language.</summary>
    private static FakeImagePicker LongestImageError()
    {
        (FakeImagePicker Picker, string Message)[] candidates =
        [
            (new FakeImagePicker { Content = new byte[PrinterImageFormats.MaxSourceBytes + 1] }, Format(Strings.SettingsImageErrorTooLarge, PrinterImageFormats.MaxSourceBytes / (1024 * 1024))),
            (new FakeImagePicker { Content = "GIF89a"u8.ToArray() }, Strings.SettingsImageErrorFormat),
            (new FakeImagePicker { Content = [.. TestImages.PngHeader(10, 10), .. new byte[64]] }, Strings.SettingsImageErrorUndecodable),
            (new FakeImagePicker { Throw = new IOException("locked") }, Strings.SettingsImageErrorUnreadable),
        ];
        return candidates.MaxBy(static candidate => candidate.Message.Length).Picker;
    }

    private static string Format(string format, object arg) => string.Format(CultureInfo.CurrentCulture, format, arg);

    // ---- Checks ----

    /// <summary>Own scroll viewers (not the ones inside a text box, list or combo box) that would need to scroll.</summary>
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
    /// <summary>
    /// T-019: the hint and the button under "Open at login" (macOS, needs approval): nothing cut, nothing scrolls, the hint in
    /// at most two lines and the button inside the window, with its whole text.
    /// </summary>
    private static IEnumerable<string> ApprovalHintProblems(SettingsWindowHarness ui, TextBlock hint, Button open, string context)
    {
        foreach (string problem in ScrollingViewers(ui.Window, context).Concat(CutTexts(ui.Window, context)))
        {
            yield return problem;
        }

        if (hint.TextLayout.TextLines.Count > 2)
        {
            yield return $"{context}: the hint '{hint.Text}' takes {hint.TextLayout.TextLines.Count} lines.";
        }

        foreach (string problem in ButtonProblems(ui, open, "SettingsOpenLoginItems", context))
        {
            yield return problem;
        }
    }

    /// <summary>
    /// T-019: the status line keeps room to read (no column of single words) and the buttons stay inside the window.
    /// </summary>
    private static IEnumerable<string> StatusRowProblems(SettingsWindowHarness ui, string context)
    {
        TextBlock status = ui.Find<TextBlock>("SettingsStatus");
        if (status.Bounds.Width < 240)
        {
            yield return $"{context}: the status has only {status.Bounds.Width:0.#} DIP.";
        }

        foreach (string id in (string[])["SettingsSave", "SettingsCancel"])
        {
            foreach (string problem in ButtonProblems(ui, ui.Find<Button>(id), id, context))
            {
                yield return problem;
            }
        }
    }

    /// <summary>A button outside the window, or narrower than its text.</summary>
    private static IEnumerable<string> ButtonProblems(SettingsWindowHarness ui, Button button, string id, string context)
    {
        Point? corner = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), ui.Window);
        if (corner is not { } end || end.X > ui.Window.ClientSize.Width + 0.5 || end.Y > ui.Window.ClientSize.Height + 0.5)
        {
            yield return $"{context}: {id} is outside the window.";
        }

        TextBlock? text = button.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
        if (text is not null)
        {
            TextBlock probe = new() { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight };
            probe.Measure(Size.Infinity);
            if (probe.DesiredSize.Width > text.Bounds.Width + 0.5)
            {
                yield return $"{context}: '{text.Text}' of {id} needs {probe.DesiredSize.Width:0.#} DIP but has {text.Bounds.Width:0.#}.";
            }
        }
    }

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

    /// <summary>Headers of a tab control that wrapped to a second row.</summary>
    private static IEnumerable<string> HeadersNotOnOneRow(Window window, string context)
    {
        foreach (TabControl tabs in window.GetVisualDescendants().OfType<TabControl>().Where(static tabs => tabs.IsEffectivelyVisible))
        {
            List<double> rows = [.. tabs.Items.OfType<TabItem>().Select(static item => item.Bounds.Y).Distinct()];
            if (rows.Count != 1)
            {
                yield return $"{context}: the headers of {AutomationProperties.GetAutomationId(tabs)} are on {rows.Count} rows.";
            }
        }
    }

    private static Color? ForegroundOf(TextBlock block) => (block.Foreground as ISolidColorBrush)?.Color;

    // ---- Keyboard helpers ----

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        window.Settle();
    }

    /// <summary>Presses Tab until the focus comes back (at most 40 times); the automation ids of the focused controls in order.</summary>
    private static List<string> TabStops(Window window)
    {
        List<string> order = [];
        object? first = window.FocusManager?.GetFocusedElement();
        for (int i = 0; i < 40; i++)
        {
            Press(window, PhysicalKey.Tab, RawInputModifiers.None);
            object? focused = window.FocusManager?.GetFocusedElement();
            if (focused is null || ReferenceEquals(focused, first))
            {
                break;
            }

            order.Add(FocusedId(window) ?? focused.GetType().Name);
        }

        return order;
    }

    /// <summary>The automation id of the focused control or of its nearest ancestor that has one (a list item: the list).</summary>
    private static string? FocusedId(Window window) =>
        (window.FocusManager?.GetFocusedElement() as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(AutomationProperties.GetAutomationId)
            .FirstOrDefault(static id => !string.IsNullOrEmpty(id));
}
