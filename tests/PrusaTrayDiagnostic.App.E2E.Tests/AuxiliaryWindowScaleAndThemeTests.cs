using System.Drawing;
using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Simulator.Camera;
using PrusaTrayDiagnostic.Simulator.Printers;


namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The settings and diagnostics windows at 125 and 150 % of the primary monitor in every language, and in the dark theme
/// with the real keyboard (T-071, manual check of T-041 on Windows with Segoe UI; 100 % is covered by
/// <see cref="SettingsAndDiagnosticsTests"/>). Both change Windows settings through the journal of T-065/T-066.
/// </summary>
public sealed class AuxiliaryWindowScaleAndThemeTests(E2EFixture fixture)
{
    private const string PrintingId = "e2e-printing";
    private const string OfflineId = "e2e-offline";

    /// <summary>Printer sub-tabs of the settings window, in order.</summary>
    private static readonly string[] PrinterTabs = ["PrinterTabConnection", "PrinterTabCamera", "PrinterTabCameraWindow", "PrinterTabOverlay"];

    /// <summary>The Tab order of the diagnostics with more printers (T-041): printer, chart range, Close.</summary>
    private static readonly string[] DiagnosticsTabOrder = ["DiagnosticsPrinterSelector", "DiagnosticsChartRange", "DiagnosticsClose"];

    private readonly LayoutRecorder _layout = new();
    private readonly StringBuilder _report = new();

    /// <summary>
    /// No scroll bar at 125 and 150 % (8 languages): the diagnostics printing, offline and without printers, the printer
    /// sub-tabs and the general tab of the settings. On a work area too small for 780×508 DIP the window fits it instead.
    /// </summary>
    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public async Task DiagnosticsAndSettingsFitAtEveryScale()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TestMonitor primary = TestMonitors.Primary(TestMonitors.All());
        Assert.False(string.IsNullOrEmpty(primary.DevicePath), $"No device path of {primary}.");
        await using SimulatedPrinter printer = await Simulators.StartPrinterAsync();
        await using RtspCamera camera = Simulators.StartCamera();
        _report.Append(DisplayConfig.Report());
        try
        {
            await using SystemChange<DisplayScaleState> scale = await SystemChanges.DisplayScaleAsync(cancellationToken);
            foreach (int percent in new[] { 125, 150 })
            {
                uint dpi = await scale.SetPercentAsync(primary.DevicePath, percent, cancellationToken);
                Assert.Equal(DisplayScaleSteps.ExpectedDpi(percent), dpi);
                _report.AppendLine(CultureInfo.InvariantCulture, $"{percent} %:").Append(DisplayConfig.Report());
                foreach (string language in TestSettings.Languages)
                {
                    await CheckPrintersAsync(printer, camera, language, percent, dpi);
                    await CheckNoPrinterAsync(language, percent, dpi);
                }
            }
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "auxiliary-windows-scales-report");
        }

        _layout.AssertNothingFound();
    }

    /// <summary>
    /// Dark mode of apps (T-065): the diagnostics and settings have a dark background, nothing scrolls and no text leaves
    /// the window; Tab with the real keyboard goes printer → chart range → Close, Esc closes the diagnostics.
    /// </summary>
    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Theme)]
    public async Task DiagnosticsInTheDarkThemeWithTheKeyboard()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SimulatedPrinter printer = await Simulators.StartPrinterAsync();
        await using SystemChange<ThemeState> theme = await SystemChanges.ThemeAsync(cancellationToken);
        await theme.ApplyAsync(ThemeState.Dark, cancellationToken);
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "dark-diagnostics",
            TestSettings.Json("en", [new TestPrinter(PrintingId, "Core One (printing)", printer.Address()), new TestPrinter(OfflineId, "MK4S", "127.0.0.1:1")]),
            Simulators.Secrets(PrintingId, printer));

        try
        {
            nint diagnostics = await app.OpenAsync($"diagnostics:{PrintingId}", AppWindowKind.Diagnostics);
            AutomationElement element = app.Element(diagnostics);
            await Wait.UntilAsync(() => Ui.IsShown(element, "DiagnosticsLastUpdate") && Ui.IsShown(element, "DiagnosticsPrinterSelector"), TimeSpan.FromSeconds(20), "the values and the printer selector");
            await _layout.CheckAsync(element, diagnostics, "dark-diagnostics");
            _layout.CheckTextsInside(element, diagnostics, "dark-diagnostics");
            AssertDarkBackground(diagnostics, "diagnostics");

            await WindowActivation.ActivateAsync(diagnostics);
            List<string> focused = [];
            for (int i = 0; i < DiagnosticsTabOrder.Length + 1; i++)
            {
                WindowActivation.TypeInto(diagnostics, VirtualKeyShort.TAB);
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
                focused.Add(fixture.Automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault ?? "(none)");
            }

            _report.AppendLine(CultureInfo.InvariantCulture, $"Tab: {string.Join(" → ", focused)}");
            Assert.True(WindowActivation.FollowsCycle(focused, DiagnosticsTabOrder), $"Tab focused {string.Join(" → ", focused)}, expected the cycle {string.Join(" → ", DiagnosticsTabOrder)}.");
            WindowActivation.TypeInto(diagnostics, VirtualKeyShort.ESCAPE);
            await Wait.UntilAsync(() => !Native.IsWindowVisible(diagnostics), TimeSpan.FromSeconds(10), "Esc to close the diagnostics");

            nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
            AutomationElement settingsElement = app.Element(settings);
            (await Ui.ItemsAsync(settingsElement, "SettingsPrinterList", 2))[0].Patterns.SelectionItem.Pattern.Select();
            foreach (string tab in PrinterTabs)
            {
                await _layout.CheckTabAsync(settingsElement, settings, tab, $"dark-settings-{tab}");
                _layout.CheckTextsInside(settingsElement, settings, $"dark-settings-{tab}");
            }

            AssertDarkBackground(settings, "settings");
            await CameraWindowSettings.CloseAsync(settings, settingsElement, save: false);
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "dark-theme-report");
        }

        _layout.AssertNothingFound();
    }

    /// <summary>Settings and diagnostics of an app with a printing and an offline printer (the printing one with a live camera).</summary>
    private async Task CheckPrintersAsync(SimulatedPrinter printer, RtspCamera camera, string language, int percent, uint dpi)
    {
        string name = $"{percent}-{language}";
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            $"scale-printers-{name}",
            TestSettings.Json(language, [new TestPrinter(PrintingId, "Core One (printing)", printer.Address(), Camera: camera.StreamUri), new TestPrinter(OfflineId, "MK4S workshop printer with a rather long name", "127.0.0.1:1")]),
            Simulators.Secrets(PrintingId, printer));

        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        AutomationElement window = app.Element(settings);
        ExpectScale(settings, dpi, $"settings {name}");
        (await Ui.ItemsAsync(window, "SettingsPrinterList", 2))[0].Patterns.SelectionItem.Pattern.Select();
        foreach (string tab in PrinterTabs)
        {
            await _layout.CheckTabAsync(window, settings, tab, $"scale-settings-{name}-{tab}");
        }

        await _layout.CheckTabAsync(window, settings, "SettingsTabGeneral", $"scale-settings-{name}-general");
        await CameraWindowSettings.CloseAsync(settings, window, save: false);

        nint diagnostics = await app.OpenAsync($"diagnostics:{PrintingId}", AppWindowKind.Diagnostics);
        AutomationElement diagnosticsElement = app.Element(diagnostics);
        ExpectScale(diagnostics, dpi, $"diagnostics {name}");
        await Wait.UntilAsync(() => Ui.IsShown(diagnosticsElement, "DiagnosticsLastUpdate") && Ui.IsShown(diagnosticsElement, "DiagnosticsHostname"), TimeSpan.FromSeconds(20), "values and printer information of the printing printer");
        await _layout.CheckAsync(diagnosticsElement, diagnostics, $"scale-diagnostics-{name}-printing");
        await app.SendAsync($"diagnostics:{OfflineId}");
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        await _layout.CheckAsync(diagnosticsElement, diagnostics, $"scale-diagnostics-{name}-offline");
    }

    /// <summary>The diagnostics notice and the printers tab without printers.</summary>
    private async Task CheckNoPrinterAsync(string language, int percent, uint dpi)
    {
        string name = $"{percent}-{language}";
        await using AppInstance app = await AppInstance.StartAsync(fixture, $"scale-no-printer-{name}", TestSettings.Json(language, []));

        nint diagnostics = await app.OpenAsync("diagnostics", AppWindowKind.Diagnostics);
        AutomationElement diagnosticsElement = app.Element(diagnostics);
        ExpectScale(diagnostics, dpi, $"diagnostics without printers {name}");
        await Wait.UntilAsync(() => Ui.IsShown(diagnosticsElement, "DiagnosticsNotice"), TimeSpan.FromSeconds(10), "the notice without printers");
        await _layout.CheckAsync(diagnosticsElement, diagnostics, $"scale-diagnostics-{name}-0p");
        await app.CloseWindowAsync(diagnostics);

        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        AutomationElement window = app.Element(settings);
        await Ui.FindAsync(window, "SettingsAddPrinter");
        await _layout.CheckTabAsync(window, settings, "SettingsTabPrinters", $"scale-settings-{name}-0p-printers");
        await CameraWindowSettings.CloseAsync(settings, window, save: false);
    }

    /// <summary>The window is on the scaled monitor and has the designed size (or fits a smaller work area).</summary>
    private static void ExpectScale(nint hwnd, uint dpi, string context)
    {
        Assert.True(Native.GetDpiForWindow(hwnd) == dpi, $"{context}: the window has {Native.GetDpiForWindow(hwnd)} dpi, expected {dpi} (is it on the primary monitor? {Native.MonitorOf(hwnd).Monitor}).");
        WindowChecks.AssertAuxiliarySize(hwnd, context);
    }

    /// <summary>The median brightness of the client area is dark (the threshold of the dark taskbar of T-065).</summary>
    private void AssertDarkBackground(nint hwnd, string context)
    {
        Native.GetWindowRect(hwnd, out Native.Rect outer);
        Native.Rect client = Native.VisibleClientRect(hwnd);
        using Bitmap capture = Screenshots.Capture(hwnd);
        using Bitmap clientArea = capture.Clone(new Rectangle(client.Left - outer.Left, client.Top - outer.Top, client.Width, client.Height), capture.PixelFormat);
        double brightness = ThemeKind.MedianBrightness(clientArea);
        _report.AppendLine(CultureInfo.InvariantCulture, $"{context}: client {client}, median brightness {brightness:0}");
        Assert.True(brightness <= ThemeKind.DarkTaskbarMaximum, $"{context}: the window is not dark (median brightness {brightness:0} > {ThemeKind.DarkTaskbarMaximum}).");
    }
}
