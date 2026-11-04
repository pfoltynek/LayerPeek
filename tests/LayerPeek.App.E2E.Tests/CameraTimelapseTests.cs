using FlaUI.Core.AutomationElements;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Camera;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The timelapse of the real app (T-161, experimental) against the simulated printer and RTSP camera (T-045): a manual
/// timelapse from the printer submenu of the tray menu writes a <c>_timelapse.mp4</c> file, and the timelapse button of the
/// camera window ("TL") shows it in the normal window and in the mini preview and goes when it is stopped from the camera
/// window; a print of a printer with "Take a timelapse of prints" gets a timelapse without a camera window. The test app
/// writes into the Recordings folder of its own data folder (T-145, test mode), deleted at the end of the run.
/// </summary>
public sealed class CameraTimelapseTests(E2EFixture fixture)
{
    // The menu PrinterSubmenuAsync opened last (an invoked item closes it by itself).
    private TrayMenuWindow? _lastMenu;

    private const string PrinterId = "e2e-timelapse";
    private const string PrinterName = "Core One";
    private const string Start = "Start timelapse (experimental)";
    private const string Stop = "Stop timelapse (experimental)";

    /// <summary>The first frame is taken at once (T-160) and needs the camera's first key frame.</summary>
    private static readonly TimeSpan FileTimeout = TimeSpan.FromSeconds(60);

    /// <summary>An automatic timelapse ends a minute after the print, like a recording, plus a poll and the file end.</summary>
    private static readonly TimeSpan PrintEndTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task AManualTimelapseFromTheTrayMenuWritesAFileAndTheCameraWindowShowsItAndStopsIt()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "timelapse-manual",
            TestSettings.Json("en", [new TestPrinter(PrinterId, PrinterName, "127.0.0.1:1", Camera: camera.StreamUri)]));
        string recordings = Path.Combine(app.DataDirectory, "Recordings");

        nint window = await app.OpenAsync($"camera:{PrinterId}", AppWindowKind.Camera);
        AutomationElement element = app.Element(window);
        AutomationElement button = await Ui.FindAsync(element, "CameraTimelapseButton");
        Assert.Equal(Start, button.Name);

        AutomationElement start = Assert.Single(await PrinterSubmenuAsync(app), static item => item.Name == Start);
        Assert.Equal("T", TrayMenu.AccessKeyOf(start));
        start.Patterns.Invoke.Pattern.Invoke();

        string file = await WaitForTimelapseAsync(recordings);
        await Wait.UntilAsync(() => button.Name == Stop, TimeSpan.FromSeconds(30), "the timelapse button of the camera window");
        Assert.NotNull(await Ui.FindAsync(element, "CameraTimelapseIndicator"));
        Assert.Single(await PrinterSubmenuAsync(app), static item => item.Name == Stop);
        await TrayMenu.CloseAsync(_lastMenu!);

        // The mini preview shows it too.
        await Ui.InvokeAsync(element, "CameraMiniButton");
        await Wait.UntilAsync(() => Ui.TryFind(element, "CameraTimelapseIndicator") is not null && button.Name == Stop, TimeSpan.FromSeconds(30), "the timelapse indicator in the mini preview");

        // Stopped from the camera window: both places show "Start" again; the recording was never touched.
        button.Patterns.Invoke.Pattern.Invoke();
        await Wait.UntilAsync(() => button.Name == Start, TimeSpan.FromSeconds(30), "the timelapse indicator to go");
        AutomationElement[] submenu = await PrinterSubmenuAsync(app);
        Assert.Single(submenu, static item => item.Name == Start);
        Assert.Single(submenu, static item => item.Name == "Start recording");
        Assert.True(Assert.Single(submenu, static item => item.Name == "Play last timelapse").IsEnabled);
        await TrayMenu.CloseAsync(_lastMenu!);
        Assert.Equal([file], TimelapseFiles(recordings));
        Assert.Equal(PrinterName, Path.GetFileName(Path.GetDirectoryName(file)));
        Assert.True(new FileInfo(file).Length > 0);
    }

    [Fact]
    public async Task APrintGetsATimelapseAutomaticallyWithoutACameraWindow()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        await using SimulatedPrinter printer = await Simulators.StartPrinterAsync(PrinterScenario.Idle);
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "timelapse-print",
            TestSettings.Json("en", [new TestPrinter(PrinterId, PrinterName, printer.Address(), Camera: camera.StreamUri, TimelapsePrints: true)]),
            Simulators.Secrets(PrinterId, printer));
        string recordings = Path.Combine(app.DataDirectory, "Recordings");

        await Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Empty(TimelapseFiles(recordings));

        printer.Simulation.SetScenario(PrinterScenario.Printing);
        string file = await WaitForTimelapseAsync(recordings);
        Assert.Single(await PrinterSubmenuAsync(app), static item => item.Name == Stop);
        await TrayMenu.CloseAsync(_lastMenu!);
        Assert.DoesNotContain(app.Windows(), hwnd => Native.IsWindowVisible(hwnd) && app.KindOf(hwnd) == AppWindowKind.Camera);

        printer.Simulation.SetScenario(PrinterScenario.Finished);
        await Wait.UntilAsync(
            async () =>
            {
                bool started = (await PrinterSubmenuAsync(app)).Any(static item => item.Name == Start);
                await TrayMenu.CloseAsync(_lastMenu!);
                return started;
            },
            PrintEndTimeout,
            "the automatic timelapse to end after the print");
        Assert.Equal([file], TimelapseFiles(recordings));
        Assert.True(new FileInfo(file).Length > 0);
    }

    /// <summary>The timelapses the app wrote (T-160: <c>…_timelapse.mp4</c> in the printer subfolders).</summary>
    private static string[] TimelapseFiles(string root) =>
        Directory.Exists(root) ? [.. Directory.EnumerateFiles(root, "*_timelapse*.mp4", SearchOption.AllDirectories).Order(StringComparer.Ordinal)] : [];

    private static async Task<string> WaitForTimelapseAsync(string root)
    {
        string? found = null;
        await Wait.UntilAsync(
            () =>
            {
                found = TimelapseFiles(root).FirstOrDefault(static path => new FileInfo(path).Length > 0);
                return found is not null;
            },
            FileTimeout,
            "the timelapse file");
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
}
