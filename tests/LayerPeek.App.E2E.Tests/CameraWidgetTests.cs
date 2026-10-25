using System.Drawing;
using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The overlay widgets of the camera window on the real app against the simulator (T-093; T-081 to T-088): the cards in
/// their anchors in the normal window and in full screen, the G-code thumbnail of the print name (T-087) from the
/// simulator's previews (T-086) and without them, and the cool-down widget through a finished and a stopped print to the
/// next print. A report of the cards and their bounds goes to the results.
/// </summary>
public sealed class CameraWidgetTests(E2EFixture fixture)
{
    private static readonly TimeSpan WidgetTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The thumbnail sizes of the print name card in DIP: M and L (none in S, T-087).</summary>
    internal static readonly int[] ThumbnailSizes = [40, 72];

    private readonly StringBuilder _report = new();

    /// <summary>
    /// One print cycle of the simulator: the cards of a running print in their default anchors (normal window and full
    /// screen), the thumbnail left of the print name, kept from memory when the window is opened again, through the
    /// finish, "Cooling down…" and "Cooled down" (threshold 100 °C) until the next print, which drops the cool-down card
    /// and brings the next file's thumbnail.
    /// </summary>
    [Fact]
    public async Task WidgetsFollowAPrintThroughCooldownToTheNextPrint()
    {
        E2EFixture.Require();
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "widgets-cycle", new WidgetRunOptions { Timing = WidgetRun.ShortCycle, CooldownThresholdCelsius = 100 });
        try
        {
            await run.GuardAsync("widgets-cycle", async () =>
            {
                nint window = await run.OpenCameraAsync();
                AutomationElement element = run.App.Element(window);
                await WaitForPrintAsync(element);
                await ExpectThumbnailAsync(window, element, "normal");
                await ExpectLayoutAsync(window, element, CameraWidgets.ImageArea(window, element), "widgets-normal");

                // Full screen shares the layout of the normal window (T-076 Q4).
                await Ui.InvokeAsync(element, "CameraFullScreenButton");
                await Wait.UntilAsync(() => WindowRect(window).Equals(Native.MonitorOf(window).Monitor), TimeSpan.FromSeconds(10), "full screen");
                Native.Rect monitor = Native.MonitorOf(window).Monitor;
                await ExpectLayoutAsync(window, element, new Rectangle(monitor.Left, monitor.Top, monitor.Width, monitor.Height), "widgets-full-screen");
                await Ui.InvokeAsync(element, "CameraNormalButton");
                await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == (960, 540), TimeSpan.FromSeconds(10), "the normal window again");

                // Closed and opened again: the thumbnail comes from memory, without a new download (T-087).
                string firstTitle = CameraWidgets.Part(element, CameraWidgets.PrintName, "Title");
                int downloads = run.Printer.PreviewRequestCount;
                await run.App.CloseWindowAsync(window);
                window = await run.OpenCameraAsync();
                element = run.App.Element(window);
                await Wait.UntilAsync(() => Ui.IsShown(element, "CameraOverlayWidget-print-name-Thumbnail"), TimeSpan.FromSeconds(10), "the thumbnail in the opened again window");
                await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
                _report.AppendLine(CultureInfo.InvariantCulture, $"Preview requests before closing {downloads}, after opening again {run.Printer.PreviewRequestCount}");
                Assert.Equal(downloads, run.Printer.PreviewRequestCount);

                // The print ends: cooling down, then cooled down; the thumbnail stays (T-087, T-088).
                await Wait.UntilAsync(() => CameraWidgets.Part(element, CameraWidgets.Cooldown, "Title") == AppStrings.Get("en", "CameraOverlayCooling"), TimeSpan.FromSeconds(150), "\"Cooling down…\" after the print");
                Assert.True(Ui.IsShown(element, "CameraOverlayWidget-print-name-Thumbnail"), "The thumbnail is gone while the printer cools down.");
                Assert.Equal("BottomLeft", CameraWidgets.AnchorOf(element, CameraWidgets.Cooldown));
                Save(window, element, "widgets-cooling");
                await Wait.UntilAsync(() => CameraWidgets.Part(element, CameraWidgets.Cooldown, "Title") == AppStrings.Get("en", "CameraOverlayCooled"), TimeSpan.FromSeconds(120), "\"Cooled down\" (nozzle below 100 °C)");
                Assert.True(Ui.IsShown(element, "CameraOverlayWidget-print-name-Thumbnail"), "The thumbnail is gone after the printer cooled down.");
                Save(window, element, "widgets-cooled");

                // The next print: no cool-down card, the next file's name and thumbnail.
                await Wait.UntilAsync(
                    () => !CameraWidgets.IsShown(element, CameraWidgets.Cooldown) && CameraWidgets.Part(element, CameraWidgets.PrintName, "Title") is { Length: > 0 } title && title != firstTitle,
                    TimeSpan.FromSeconds(120),
                    $"the next print (a title other than '{firstTitle}') without the cool-down card");
                await ExpectThumbnailAsync(window, element, "next print");
                Assert.True(run.Printer.PreviewRequestCount > downloads, "The thumbnail of the next file was not downloaded.");
                Save(window, element, "widgets-next-print");
            });
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "widgets-cycle-report");
        }
    }

    /// <summary>A print stopped by the user (simulator scenario <c>stopped</c>): cooling down, cooled down, the thumbnail kept; the next print drops the card.</summary>
    [Fact]
    public async Task StoppedPrintCoolsDownAndKeepsTheThumbnail()
    {
        E2EFixture.Require();
        SimulationTiming longPrint = new() { HeatUp = TimeSpan.FromSeconds(10), Print = TimeSpan.FromMinutes(30) };
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "widgets-stopped", new WidgetRunOptions { Timing = longPrint, CooldownThresholdCelsius = 100 });
        try
        {
            await run.GuardAsync("widgets-stopped", async () =>
            {
                nint window = await run.OpenCameraAsync();
                AutomationElement element = run.App.Element(window);
                await WaitForPrintAsync(element);
                await ExpectThumbnailAsync(window, element, "printing");
                await Wait.UntilAsync(() => run.Printer.Simulation.GetStatus().Job?.Progress > 0, TimeSpan.FromSeconds(30), "the print past heating");
                string title = CameraWidgets.Part(element, CameraWidgets.PrintName, "Title");

                run.Printer.Simulation.SetScenario(PrinterScenario.Stopped);
                await Wait.UntilAsync(() => CameraWidgets.Part(element, CameraWidgets.Cooldown, "Title") == AppStrings.Get("en", "CameraOverlayCooling"), TimeSpan.FromSeconds(30), "\"Cooling down…\" after the stop");
                Assert.True(Ui.IsShown(element, "CameraOverlayWidget-print-name-Thumbnail"), "The thumbnail is gone after the stop.");
                _report.AppendLine("Cooling: " + CameraWidgets.Part(element, CameraWidgets.Cooldown, "Detail"));
                Save(window, element, "widgets-stopped-cooling");
                await Wait.UntilAsync(() => CameraWidgets.Part(element, CameraWidgets.Cooldown, "Title") == AppStrings.Get("en", "CameraOverlayCooled"), TimeSpan.FromSeconds(120), "\"Cooled down\" after the stop");
                Assert.True(Ui.IsShown(element, "CameraOverlayWidget-print-name-Thumbnail"), "The thumbnail is gone after cooling down.");
                Assert.Equal(title, CameraWidgets.Part(element, CameraWidgets.PrintName, "Title"));
                _report.AppendLine("Cooled: " + CameraWidgets.Part(element, CameraWidgets.Cooldown, "Detail"));
                Save(window, element, "widgets-stopped-cooled");

                run.Printer.Simulation.SetScenario(PrinterScenario.Printing);
                await Wait.UntilAsync(
                    () => !CameraWidgets.IsShown(element, CameraWidgets.Cooldown) && CameraWidgets.Part(element, CameraWidgets.PrintName, "Title") is { Length: > 0 } next && next != title,
                    TimeSpan.FromSeconds(30),
                    "the next print without the cool-down card");
                await ExpectThumbnailAsync(window, element, "next print");
            });
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "widgets-stopped-report");
        }
    }

    /// <summary>
    /// Job files without a G-code preview (simulator <c>--no-previews</c>): the print name card without a thumbnail and
    /// without an empty frame in its place (T-087, T-076 2.4).
    /// </summary>
    [Fact]
    public async Task PrintWithoutPreviewsHasNoThumbnailAndNoEmptyFrame()
    {
        E2EFixture.Require();
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "widgets-no-previews", new WidgetRunOptions { Timing = WidgetRun.ShortCycle, Previews = false });
        await run.GuardAsync("widgets-no-previews", async () =>
        {
            nint window = await run.OpenCameraAsync();
            AutomationElement element = run.App.Element(window);
            await WaitForPrintAsync(element);
            // The app asked for the preview and got 404 (a few polls).
            await Wait.UntilAsync(() => run.Printer.PreviewRequestCount > 0, TimeSpan.FromSeconds(30), "the app to ask for the preview");
            await Task.Delay(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken);
            Assert.False(Ui.IsShown(element, "CameraOverlayWidget-print-name-Thumbnail"), "A thumbnail (or its frame) is shown without a preview.");
            Rectangle card = CameraWidgets.Card(element, CameraWidgets.PrintName)!.BoundingRectangle;
            Rectangle title = (await Ui.FindAsync(element, "CameraOverlayWidget-print-name-Title")).BoundingRectangle;
            double scale = WindowChecks.Scaling(window);
            // Without a thumbnail the text starts at the card's padding (no 40 DIP + gap in front of it).
            Assert.True(title.Left - card.Left < 40 * scale, $"The title {title} starts {title.Left - card.Left} px into the card {card}: room for a thumbnail is kept.");
            Save(window, element, "widgets-no-previews");
        });
    }

    private static async Task WaitForPrintAsync(AutomationElement element)
    {
        await Wait.UntilAsync(
            () => CameraWidgets.IsShown(element, CameraWidgets.Status) && CameraWidgets.Part(element, CameraWidgets.PrintName, "Title").Length > 0,
            WidgetTimeout,
            "the status card and the print name of the running print");
    }

    /// <summary>The thumbnail: shown left of the title, named for screen readers, 40 (M) or 72 (L) DIP at the monitor's scale.</summary>
    private async Task ExpectThumbnailAsync(nint window, AutomationElement element, string context)
    {
        AutomationElement thumbnail = null!;
        await Wait.UntilAsync(() => (thumbnail = Ui.TryFind(element, "CameraOverlayWidget-print-name-Thumbnail")!) is not null && !thumbnail.Properties.IsOffscreen.ValueOrDefault, TimeSpan.FromSeconds(30), $"{context}: the G-code thumbnail");
        Rectangle bounds = thumbnail.BoundingRectangle;
        Rectangle title = (await Ui.FindAsync(element, "CameraOverlayWidget-print-name-Title")).BoundingRectangle;
        double scale = WindowChecks.Scaling(window);
        _report.AppendLine(CultureInfo.InvariantCulture, $"{context}: thumbnail {bounds}, title {title}, scale {scale:0.##}");
        Assert.Equal(AppStrings.Get("en", "CameraOverlayThumbnailName"), thumbnail.Properties.Name.ValueOrDefault);
        Assert.True(bounds.Width == bounds.Height, $"{context}: the thumbnail {bounds} is not square.");
        Assert.True(ThumbnailSizes.Any(size => Math.Abs(bounds.Width - (size * scale)) <= 1.5), $"{context}: the thumbnail is {bounds.Width} px, expected 40 or 72 DIP at {scale:0.##}.");
        Assert.True(bounds.Right <= title.Left, $"{context}: the thumbnail {bounds} is not left of the title {title}.");
    }

    private async Task ExpectLayoutAsync(nint window, AutomationElement element, Rectangle area, string name)
    {
        List<string> problems = [];
        try
        {
            await Wait.UntilAsync(() => (problems = CameraWidgets.LayoutProblems(element, area, CameraWidgets.DefaultPrinting)).Count == 0, TimeSpan.FromSeconds(10), $"{name}: the cards in their anchors");
        }
        catch (WaitTimeoutException)
        {
            // Reported below with the problems.
        }
        finally
        {
            Save(window, element, name);
        }

        Assert.True(problems.Count == 0, $"{name}: {string.Join("; ", problems)}");
    }

    private void Save(nint window, AutomationElement element, string name)
    {
        _report.AppendLine(name + ":").AppendLine(CameraWidgets.Describe(element));
        Screenshots.Save(window, name);
    }

    private static Native.Rect WindowRect(nint hwnd)
    {
        Assert.True(Native.GetWindowRect(hwnd, out Native.Rect rect));
        return rect;
    }
}
