using System.Globalization;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.UiTests.Settings;

/// <summary>
/// Layout of the settings window (T-033): at its designed size everything fits without a scroll bar in every language and
/// state, headers stay on one row, single-line texts are not cut, the printer sub-tabs behave as designed, Tab moves in
/// reading order and the tab shortcuts work.
/// </summary>
public sealed class SettingsWindowLayoutTests(HeadlessSession session)
{
    /// <summary>Title bar assumed by the design (Windows 11 at 100 %); the smallest supported screen leaves 780 × 540.</summary>
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
        ("General", SettingsViewModel.GeneralTab, SettingsViewModel.ConnectionPrinterTab),
    ];

    // ---- Size ----

    [Fact]
    public Task WindowOpensAtTheDesignedSizeThatFitsTheSmallestScreen() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));

            Assert.Equal(new Size(780, 508), ui.Window.ClientSize);
            Assert.True(ui.Window.ClientSize.Width <= 780);
            Assert.True(ui.Window.ClientSize.Height + TitleBar <= 540);
            Assert.Equal(SettingsWindowPlacement.DefaultClientSize, new Size(ui.Window.Width, ui.Window.Height));
            Assert.Equal(SettingsWindowPlacement.MinClientSize, new Size(ui.Window.MinWidth, ui.Window.MinHeight));
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
                Assert.Equal(new Size(780, 508), ui.Window.ClientSize);
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

    [Fact]
    public Task ChecksOfTheLayoutTestDetectProblems() =>
        session.RunAsync(static async scope =>
        {
            // The checks above pass only because the layout fits, not because they cannot fail.
            Assert.True(new UiLanguage().Apply("fr"));
            SettingsWindowHarness ui = await OpenWithAllErrorsAsync(scope, SettingsWindowHarness.Supported, refusedTest: true);
            ui.Window.Width = 640;
            ui.Window.Height = 400;
            ui.Window.Settle();

            Assert.NotEmpty(ScrollingViewers(ui.Window, "small"));

            ui.Find<Button>("SettingsAddPrinter").Width = 40;
            ui.Window.Settle();

            Assert.Contains(CutTexts(ui.Window, "narrow"), static problem => problem.Contains(Strings.SettingsAddPrinter, StringComparison.Ordinal));

            Assert.Empty(HeadersNotOnOneRow(ui.Window, "headers"));
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
            List<string> problems = [.. HeadersNotOnOneRow(ui.Window, language), .. CutTexts(ui.Window, language)];
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
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
            Assert.Equal("The camera works (1920 × 1080 px).", ui.ShownText("PrinterCameraTestResult"));
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
                    "SettingsPrinterList", "SettingsAddPrinter", "SettingsRemovePrinter", "PrinterTabConnection", "PrinterName",
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

            string[] expected = ["SettingsPollingInterval", "SettingsLanguage", "SettingsNotificationsEnabled", "SettingsSound", "SettingsRemainingTime", "SettingsSave", "SettingsCancel"];
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
            Assert.True(ui.Find<TabItem>("SettingsTabGeneral").IsSelected);
            Assert.Equal("SettingsTabGeneral", FocusedId(window));

            // On the General tab there are no sub-tabs: the keys do nothing.
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
        if (refusedTest)
        {
            await printer.TestConnectionCommand.ExecuteAsync(null);
            Assert.Equal(Strings.SettingsFixErrors, printer.TestResultText);
        }

        await ui.ViewModel.SaveCommand.ExecuteAsync(null);
        Assert.Empty(store.Saves);
        Assert.True(printer.ShowsConnectionErrorMarker);
        Assert.True(printer.ShowsCameraErrorMarker);
        ui.Window.Settle();
        return ui;
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
