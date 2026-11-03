using FlaUI.Core.AutomationElements;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Camera;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// Camera recording of the real app (T-146) against the simulated printer and RTSP camera (T-045): a manual recording from
/// the printer submenu of the tray menu writes a file and the indicators (the tray tooltip "Recording", the REC button of the
/// camera window) show and go; a print of a printer with "Record prints automatically" is recorded without a camera window.
/// The test app records into the Recordings folder of its own data folder (T-145, test mode), under the reserved root of
/// the run that is swept at its start and deleted at its end, so recordings never stay behind, also after a crash.
/// </summary>
public sealed class CameraRecordingTests(E2EFixture fixture)
{
    // The menu PrinterSubmenuAsync opened last (an invoked item closes it by itself).
    private TrayMenuWindow? _lastMenu;

    private const string PrinterId = "e2e-recording";
    private const string PrinterName = "Core One";

    /// <summary>The first key frame of the fake camera and the first fragment of the file (T-144) come within seconds.</summary>
    private static readonly TimeSpan FileTimeout = TimeSpan.FromSeconds(60);

    /// <summary>An automatic recording ends a minute after the print (user decision 2026-11-03) plus a poll and the file end.</summary>
    private static readonly TimeSpan PrintEndTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task AManualRecordingFromTheTrayMenuWritesAFileAndShowsTheIndicators()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "recording-manual",
            TestSettings.Json("en", [new TestPrinter(PrinterId, PrinterName, "127.0.0.1:1", Camera: camera.StreamUri)]));
        string recordings = Path.Combine(app.DataDirectory, "Recordings");

        // The camera window has the record button, hidden until the pointer comes, "Start recording" while idle.
        nint window = await app.OpenAsync($"camera:{PrinterId}", AppWindowKind.Camera);
        AutomationElement record = await Ui.FindAsync(app.Element(window), "CameraRecordButton");
        Assert.Equal("Start recording", record.Name);

        AutomationElement start = Assert.Single(await PrinterSubmenuAsync(app), static item => item.Name == "Start recording");
        Assert.Equal("R", TrayMenu.AccessKeyOf(start));
        start.Patterns.Invoke.Pattern.Invoke();

        string file = await WaitForRecordingAsync(recordings, "the manual recording");
        await Wait.UntilAsync(() => record.Name == "Stop recording", TimeSpan.FromSeconds(30), "the REC button of the camera window");
        await Wait.UntilAsync(async () => (await ToolTipAsync(app)).Contains("Recording", StringComparison.Ordinal), TimeSpan.FromSeconds(30), "\"Recording\" in the tray tooltip");

        AutomationElement stop = Assert.Single(await PrinterSubmenuAsync(app), static item => item.Name == "Stop recording");
        Assert.Equal("R", TrayMenu.AccessKeyOf(stop));
        stop.Patterns.Invoke.Pattern.Invoke();

        await Wait.UntilAsync(() => record.Name == "Start recording", TimeSpan.FromSeconds(30), "the REC button to go");
        await Wait.UntilAsync(async () => !(await ToolTipAsync(app)).Contains("Recording", StringComparison.Ordinal), TimeSpan.FromSeconds(30), "\"Recording\" to leave the tray tooltip");
        // One file in the printer's subfolder, complete and not empty; "Play last recording" is offered now.
        Assert.Equal([file], RecordingFiles(recordings));
        Assert.Equal(PrinterName, Path.GetFileName(Path.GetDirectoryName(file)));
        Assert.True(new FileInfo(file).Length > 0);
        Assert.True(Assert.Single(await PrinterSubmenuAsync(app), static item => item.Name == "Play last recording").IsEnabled);
        await TrayMenu.CloseAsync(_lastMenu!);
    }

    [Fact]
    public async Task APrintIsRecordedAutomaticallyWithoutACameraWindow()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        await using SimulatedPrinter printer = await Simulators.StartPrinterAsync(PrinterScenario.Idle);
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "recording-print",
            TestSettings.Json("en", [new TestPrinter(PrinterId, PrinterName, printer.Address(), Camera: camera.StreamUri, RecordPrints: true)]),
            Simulators.Secrets(PrinterId, printer));
        string recordings = Path.Combine(app.DataDirectory, "Recordings");

        // Idle: nothing records.
        await Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Empty(RecordingFiles(recordings));

        printer.Simulation.SetScenario(PrinterScenario.Printing);
        string file = await WaitForRecordingAsync(recordings, "the recording of the print");
        await Wait.UntilAsync(async () => (await ToolTipAsync(app)).Contains("Recording", StringComparison.Ordinal), TimeSpan.FromSeconds(30), "\"Recording\" in the tray tooltip");
        Assert.DoesNotContain(app.Windows(), hwnd => Native.IsWindowVisible(hwnd) && app.KindOf(hwnd) == AppWindowKind.Camera);

        // The print ends: the recording goes on for a minute, then the file is completed and the indicator goes.
        printer.Simulation.SetScenario(PrinterScenario.Finished);
        await Wait.UntilAsync(async () => !(await ToolTipAsync(app)).Contains("Recording", StringComparison.Ordinal), PrintEndTimeout, "the automatic recording to end after the print");
        Assert.Equal([file], RecordingFiles(recordings));
        Assert.True(new FileInfo(file).Length > 0);
    }

    /// <summary>The recordings the app wrote: <c>.mp4</c> files in the printer subfolders.</summary>
    private static string[] RecordingFiles(string root) =>
        Directory.Exists(root) ? [.. Directory.EnumerateFiles(root, "*.mp4", SearchOption.AllDirectories).Order(StringComparer.Ordinal)] : [];

    private static async Task<string> WaitForRecordingAsync(string root, string what)
    {
        string? found = null;
        await Wait.UntilAsync(
            () =>
            {
                found = RecordingFiles(root).FirstOrDefault(static path => new FileInfo(path).Length > 0);
                return found is not null;
            },
            FileTimeout,
            what);
        return found!;
    }

    /// <summary>The items of the printer submenu (the separator is no menu item); the menu stays open.</summary>
    private async Task<AutomationElement[]> PrinterSubmenuAsync(AppInstance app)
    {
        TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
        _lastMenu = menu;
        (AutomationElement[] items, _) = await TrayMenu.ExpandAsync(app, menu, menu.Items()[0]);
        return items;
    }

    /// <summary>The tooltip of the test instance's tray icon (its UI Automation name).</summary>
    private static async Task<string> ToolTipAsync(AppInstance app)
    {
        (IReadOnlyList<AutomationElement> icons, bool openedOverflow) = await NotificationArea.FindTestIconsAsync(app.Automation);
        try
        {
            return string.Join("\n", icons.Select(static icon => icon.Properties.Name.ValueOrDefault ?? string.Empty));
        }
        finally
        {
            if (openedOverflow)
            {
                await NotificationArea.CloseOverflowAsync(app.Automation);
            }
        }
    }
}
