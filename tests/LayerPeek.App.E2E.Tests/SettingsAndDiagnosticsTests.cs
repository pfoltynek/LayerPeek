using FlaUI.Core.AutomationElements;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Camera;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The settings and diagnostics windows of the real app in every language and their main states (T-033, T-040, T-041,
/// T-043, T-025, T-094): close and maximize buttons, client 780×532 DIP, nothing scrolls, taskbar properties and the app icon. Every
/// state is captured into the results folder.
/// </summary>
public sealed class SettingsAndDiagnosticsTests(E2EFixture fixture)
{
    private const string PrintingId = "e2e-printing";
    private const string OfflineId = "e2e-offline";
    private const string NoCameraId = "e2e-no-camera";

    /// <summary>Printer sub-tabs of the settings window, in order.</summary>
    private static readonly string[] PrinterTabs = ["PrinterTabConnection", "PrinterTabCamera", "PrinterTabCameraWindow"];

    public static TheoryData<string> Languages => [.. TestSettings.Languages];

    /// <summary>Three printers (printing with a live camera, offline with the camera off, offline without a camera).</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public async Task ThreePrintersWithErrorsAndCamerasFitWithoutScrolling(string language)
    {
        E2EFixture.Require();
        await using SimulatedPrinter printer = await Simulators.StartPrinterAsync();
        await using RtspCamera camera = Simulators.StartCamera();
        TestPrinter[] printers =
        [
            new(PrintingId, "Core One (printing)", printer.Address(), Camera: camera.StreamUri),
            new(OfflineId, "MK4S workshop printer with a rather long name", "127.0.0.1:1", DisabledCamera: "rtsp://127.0.0.1:1/live"),
            new(NoCameraId, "XL", "127.0.0.1:1"),
        ];
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            $"three-printers-{language}",
            TestSettings.Json(language, printers),
            Simulators.Secrets(PrintingId, printer));

        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        string context = $"settings ({language})";
        AssertAuxiliaryWindow(settings, context);
        AutomationElement window = app.Element(settings);
        AutomationElement[] items = await Ui.ItemsAsync(window, "SettingsPrinterList", 3);

        // Every sub-tab of the printer with the camera, then the camera tabs of the printer whose camera is off.
        items[0].Patterns.SelectionItem.Pattern.Select();
        foreach (string tab in PrinterTabs)
        {
            await CheckTabAsync(window, settings, tab, $"settings-{language}-3p-camera-on-{tab}");
        }

        items[1].Patterns.SelectionItem.Pattern.Select();
        await CheckTabAsync(window, settings, "PrinterTabCamera", $"settings-{language}-3p-camera-off-PrinterTabCamera");
        await CheckTabAsync(window, settings, "PrinterTabCameraWindow", $"settings-{language}-3p-camera-off-PrinterTabCameraWindow");

        // Validation errors (shown at once for saved printers): empty name, invalid address, invalid camera address.
        items[0].Patterns.SelectionItem.Pattern.Select();
        await Ui.SelectAsync(window, "PrinterTabConnection");
        await Ui.SetTextAsync(window, "PrinterName", string.Empty);
        await Ui.SetTextAsync(window, "PrinterAddress", "http://");
        await Wait.UntilAsync(() => Ui.IsShown(window, "PrinterNameError") && Ui.IsShown(window, "PrinterAddressError"), TimeSpan.FromSeconds(10), "the validation errors");
        await CheckTabAsync(window, settings, "PrinterTabConnection", $"settings-{language}-3p-errors-PrinterTabConnection");
        await Ui.SelectAsync(window, "PrinterTabCamera");
        await Ui.SetTextAsync(window, "PrinterCameraUrl", "http://camera");
        await Wait.UntilAsync(() => Ui.IsShown(window, "PrinterCameraUrlError"), TimeSpan.FromSeconds(10), "the camera address error");
        await CheckTabAsync(window, settings, "PrinterTabCamera", $"settings-{language}-3p-errors-PrinterTabCamera");

        await CheckTabAsync(window, settings, "SettingsTabWidgets", $"settings-{language}-3p-widgets");
        await CheckTabAsync(window, settings, "SettingsTabGeneral", $"settings-{language}-3p-general");

        // Cancel discards the invalid edits and closes the window.
        await Ui.InvokeAsync(window, "SettingsCancel");
        await Wait.UntilAsync(() => !Native.IsWindowVisible(settings), TimeSpan.FromSeconds(10), "the settings to close");

        // Diagnostics of every printer: printing (fake PrusaLink), offline, offline without camera.
        nint diagnostics = await app.OpenAsync($"diagnostics:{PrintingId}", AppWindowKind.Diagnostics);
        AssertAuxiliaryWindow(diagnostics, $"diagnostics ({language})");
        AutomationElement diagnosticsElement = app.Element(diagnostics);
        await Wait.UntilAsync(() => Ui.IsShown(diagnosticsElement, "DiagnosticsLastUpdate") && Ui.IsShown(diagnosticsElement, "DiagnosticsHostname"), TimeSpan.FromSeconds(20), "values and printer information of the printing printer");
        await CheckDiagnosticsAsync(diagnostics, diagnosticsElement, $"diagnostics-{language}-printing");

        foreach (string id in new[] { OfflineId, NoCameraId })
        {
            await app.SendAsync($"diagnostics:{id}");
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            await CheckDiagnosticsAsync(diagnostics, diagnosticsElement, $"diagnostics-{language}-{id}");
        }

        Assert.DoesNotContain(" ERR ", app.ReadLog(), StringComparison.Ordinal);
        AssertNothingScrolled();
    }

    /// <summary>No printer: the empty settings and the diagnostics notice.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public async Task NoPrinterFitsWithoutScrolling(string language)
    {
        E2EFixture.Require();
        await using AppInstance app = await AppInstance.StartAsync(fixture, $"no-printer-{language}", TestSettings.Json(language, []));

        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        AssertAuxiliaryWindow(settings, $"settings without printers ({language})");
        AutomationElement window = app.Element(settings);
        await Ui.FindAsync(window, "SettingsAddPrinter");
        await CheckTabAsync(window, settings, "SettingsTabPrinters", $"settings-{language}-0p-printers");
        await CheckTabAsync(window, settings, "SettingsTabWidgets", $"settings-{language}-0p-widgets");
        await CheckTabAsync(window, settings, "SettingsTabGeneral", $"settings-{language}-0p-general");
        await app.CloseWindowAsync(settings);

        nint diagnostics = await app.OpenAsync("diagnostics", AppWindowKind.Diagnostics);
        AssertAuxiliaryWindow(diagnostics, $"diagnostics without printers ({language})");
        AutomationElement diagnosticsElement = app.Element(diagnostics);
        await Wait.UntilAsync(() => Ui.IsShown(diagnosticsElement, "DiagnosticsNotice"), TimeSpan.FromSeconds(10), "the notice without printers");
        await CheckDiagnosticsAsync(diagnostics, diagnosticsElement, $"diagnostics-{language}-0p");
        AssertNothingScrolled();
    }

    /// <summary>One printer (no list scrolling, no printer selector in the diagnostics).</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task OnePrinterFitsWithoutScrolling(string language)
    {
        E2EFixture.Require();
        await using SimulatedPrinter printer = await Simulators.StartPrinterAsync();
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            $"one-printer-{language}",
            TestSettings.Json(language, [new TestPrinter(PrintingId, "Core One", printer.Address())]),
            Simulators.Secrets(PrintingId, printer));

        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        AssertAuxiliaryWindow(settings, $"settings with one printer ({language})");
        AutomationElement window = app.Element(settings);
        (await Ui.ItemsAsync(window, "SettingsPrinterList", 1))[0].Patterns.SelectionItem.Pattern.Select();
        foreach (string tab in PrinterTabs)
        {
            await CheckTabAsync(window, settings, tab, $"settings-{language}-1p-{tab}");
        }

        await app.CloseWindowAsync(settings);

        nint diagnostics = await app.OpenAsync("diagnostics", AppWindowKind.Diagnostics);
        AutomationElement diagnosticsElement = app.Element(diagnostics);
        await Wait.UntilAsync(() => Ui.IsShown(diagnosticsElement, "DiagnosticsLastUpdate"), TimeSpan.FromSeconds(20), "the printer values");
        Assert.False(Ui.IsShown(diagnosticsElement, "DiagnosticsPrinterSelector"), "One printer needs no selector.");
        await CheckDiagnosticsAsync(diagnostics, diagnosticsElement, $"diagnostics-{language}-1p");
        AssertNothingScrolled();
    }

    private static void AssertAuxiliaryWindow(nint hwnd, string context)
    {
        WindowChecks.AssertCloseAndMaximizeButtons(hwnd, context);
        WindowChecks.AssertAuxiliarySize(hwnd, context);
        WindowChecks.AssertTaskbarProperties(hwnd, context);
        WindowChecks.AssertHasIcon(hwnd, context);
        Assert.True(Native.HasTaskbarButton(hwnd), $"{context}: no taskbar button.");
    }

    /// <summary>
    /// Scrolling found so far: recorded and asserted at the end of the test, so that one overflowing tab does not hide the
    /// states after it (every state is still captured).
    /// </summary>
    private readonly LayoutRecorder _layout = new();

    private void AssertNothingScrolled() => _layout.AssertNothingFound();

    /// <summary>Selects a tab, lets the layout settle, records whether anything scrolls and captures the window.</summary>
    private Task CheckTabAsync(AutomationElement window, nint hwnd, string tab, string screenshot) =>
        _layout.CheckTabAsync(window, hwnd, tab, screenshot);

    private async Task CheckDiagnosticsAsync(nint hwnd, AutomationElement window, string screenshot)
    {
        await _layout.CheckAsync(window, hwnd, screenshot);
        WindowChecks.AssertAuxiliarySize(hwnd, screenshot);
    }
}
