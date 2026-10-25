using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Printers;

using Wait = LayerPeek.App.E2E.Tests.Infrastructure.Wait;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The mini preview of the camera window on the real app against the simulator (T-093; T-089, T-090): no cards over the
/// image, the info strip below it with the stream's 16:9 image, the two row toggles with the real mouse and keyboard, the
/// window growing from the nearest work area edge, the resize grip dragged with the real mouse (Windows' modal sizing loop
/// of <c>BeginResizeDrag</c>), the remembered rows across a restart, files of version 0.1.0, and the strip's text in the
/// printer's states.
/// </summary>
public sealed partial class CameraMiniPreviewTests(E2EFixture fixture)
{
    /// <summary>The default image of the mini preview (DIP) and the collapsed strip below it (3 + 30).</summary>
    private const int ImageWidth = 320;
    private const int ImageHeight = 180;
    private const int StripCollapsed = 33;
    private const int NameRow = 34;
    private const int ChartRow = 64;

    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(15);

    /// <summary>A long print after a short heating: the job (and the name row) stays for the whole test.</summary>
    private static readonly SimulationTiming LongPrint = new() { HeatUp = TimeSpan.FromSeconds(5), Print = TimeSpan.FromMinutes(30) };

    private readonly StringBuilder _report = new();

    /// <summary>
    /// Near the bottom edge (bottom right corner): no cards, the strip below the 16:9 image, both rows collapsed; the name
    /// row with the real mouse and the chart row with Tab and Space grow the window upwards (its bottom edge stays) inside
    /// the work area; the rows are remembered across a restart although "remember the last placement" is off (T-090 Q3).
    /// </summary>
    [Fact]
    public async Task TogglesGrowTheWindowFromTheBottomEdgeAndSurviveARestart()
    {
        E2EFixture.Require();
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "mini-toggles", new WidgetRunOptions { Timing = LongPrint, CameraWindow = CameraPlacement.Window(null, "Mini", miniCorner: "BottomRight") });
        try
        {
            await run.GuardAsync("mini-toggles", async () =>
            {
                nint window = await run.OpenCameraAsync();
                AutomationElement element = run.App.Element(window);
                await ExpectSizeAsync(window, StripCollapsed, "the collapsed mini preview");
                await Wait.UntilAsync(() => PrintingSummary().IsMatch(CameraWidgets.MiniSummary(element).Visible), TimeSpan.FromSeconds(30), "the printing summary in the strip");
                AssertStripBelowTheImage(window, element, StripCollapsed);
                Assert.Empty(CameraWidgets.ShownCards(element));
                Assert.False(CameraWidgets.IsOn(element, "CameraMiniToggleName"), "The name row is expanded by default.");
                Assert.False(CameraWidgets.IsOn(element, "CameraMiniToggleChart"), "The chart row is expanded by default.");
                Save(window, "mini-collapsed");
                Native.Rect before = Native.FrameBounds(window);

                // The name row with the real mouse.
                Rectangle toggle = (await Ui.FindAsync(element, "CameraMiniToggleName")).BoundingRectangle;
                Mouse.Click(new Point(toggle.Left + (toggle.Width / 2), toggle.Top + (toggle.Height / 2)));
                await ExpectSizeAsync(window, StripCollapsed + NameRow, "the name row expanded");
                Assert.True(CameraWidgets.IsOn(element, "CameraMiniToggleName"));
                AssertGrewFrom(window, before, bottom: true, "name row");
                await Wait.UntilAsync(() => Ui.IsShown(element, "CameraMiniNameRow"), WindowTimeout, "the name row");

                // The chart row with the keyboard: Tab from the name toggle, Space.
                await FocusAsync(window, "CameraMiniToggleChart");
                WindowActivation.TypeInto(window, VirtualKeyShort.SPACE);
                await ExpectSizeAsync(window, StripCollapsed + NameRow + ChartRow, "both rows expanded");
                Assert.True(CameraWidgets.IsOn(element, "CameraMiniToggleChart"));
                AssertGrewFrom(window, before, bottom: true, "chart row");
                AssertStripBelowTheImage(window, element, StripCollapsed + NameRow + ChartRow);
                Save(window, "mini-both-rows");
                await Wait.UntilAsync(() => Regex.IsMatch(CameraWindowsFile(run.App), @"""miniNameRow""\s*:\s*true") && Regex.IsMatch(CameraWindowsFile(run.App), @"""miniChartRow""\s*:\s*true"), WindowTimeout, "both rows in camera-windows.json");

                // Restart: the rows come back expanded.
                await run.App.RestartAsync();
                window = await run.OpenCameraAsync();
                element = run.App.Element(window);
                await ExpectSizeAsync(window, StripCollapsed + NameRow + ChartRow, "both rows after the restart");
                Assert.True(CameraWidgets.IsOn(element, "CameraMiniToggleName"), "The name row is collapsed after the restart.");
                Assert.True(CameraWidgets.IsOn(element, "CameraMiniToggleChart"), "The chart row is collapsed after the restart.");
                AssertInsideTheWorkArea(window, "after the restart");
                Save(window, "mini-both-rows-restarted");
            });
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "mini-toggles-report");
        }
    }

    /// <summary>Near the top edge (top left corner): the chart row with Tab and Enter grows the window downwards; the mouse collapses it again.</summary>
    [Fact]
    public async Task ToggleNearTheTopEdgeGrowsTheWindowDownwards()
    {
        E2EFixture.Require();
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "mini-top", new WidgetRunOptions { Timing = LongPrint, CameraWindow = CameraPlacement.Window(null, "Mini", miniCorner: "TopLeft") });
        try
        {
            await run.GuardAsync("mini-top", async () =>
            {
                nint window = await run.OpenCameraAsync();
                AutomationElement element = run.App.Element(window);
                await ExpectSizeAsync(window, StripCollapsed, "the collapsed mini preview");
                Native.Rect before = Native.FrameBounds(window);

                await FocusAsync(window, "CameraMiniToggleChart");
                WindowActivation.TypeInto(window, VirtualKeyShort.RETURN);
                await ExpectSizeAsync(window, StripCollapsed + ChartRow, "the chart row expanded with Enter");
                AssertGrewFrom(window, before, bottom: false, "chart row at the top");
                await Wait.UntilAsync(() => Ui.IsShown(element, "CameraMiniChart"), WindowTimeout, "the chart");
                Save(window, "mini-top-chart");

                Rectangle toggle = (await Ui.FindAsync(element, "CameraMiniToggleChart")).BoundingRectangle;
                Mouse.Click(new Point(toggle.Left + (toggle.Width / 2), toggle.Top + (toggle.Height / 2)));
                await ExpectSizeAsync(window, StripCollapsed, "the chart row collapsed with the mouse");
                Native.Rect after = Native.FrameBounds(window);
                Assert.True(Math.Abs(after.Top - before.Top) <= 1, $"The top edge moved: {before} → {after}.");
            });
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "mini-top-report");
        }
    }

    /// <summary>
    /// The left resize grip dragged with the real mouse (Windows' modal sizing loop): the height is not corrected while
    /// dragging (no flicker) and once after the release; the width never goes below 240; dragging the strip (outside its
    /// buttons) moves the window.
    /// </summary>
    [Fact]
    public async Task ResizeGripCorrectsTheHeightOnceAfterTheReleaseAndTheStripMovesTheWindow()
    {
        E2EFixture.Require();
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "mini-resize", new WidgetRunOptions { Timing = LongPrint, CameraWindow = CameraPlacement.Window(null, "Mini", miniCorner: "Center") });
        try
        {
            await run.GuardAsync("mini-resize", async () =>
            {
                nint window = await run.OpenCameraAsync();
                AutomationElement element = run.App.Element(window);
                await ExpectSizeAsync(window, StripCollapsed, "the collapsed mini preview");
                double scale = WindowChecks.Scaling(window);

                // Wider: the left edge 160 px to the left, in steps.
                Native.Rect start = WindowRect(window);
                List<Native.Rect> during = await DragAsync(window, new Point(start.Left + (int)Math.Round(2 * scale), start.Top + (int)Math.Round(90 * scale)), -160, 0, "mini-resize-dragging");
                _report.AppendLine(CultureInfo.InvariantCulture, $"Start {start}; while dragging: {string.Join(", ", during)}");
                Assert.True(during.Last().Width > start.Width, $"The left grip did not resize the window: {string.Join(", ", during)}.");
                Assert.True(during.All(rect => rect.Height == start.Height), $"The height changed while dragging (it must be corrected only after the release): {string.Join(", ", during)}.");
                (int width, int height) wider = await ExpectCorrectedAsync(window, "after the wider drag");
                Assert.True(wider.width > ImageWidth + 100, $"The window is only {wider.width} DIP wide after the drag.");
                List<Native.Rect> settled = await SampleAsync(window, TimeSpan.FromSeconds(1.5));
                Assert.True(settled.Distinct().Count() == 1, $"The window kept changing after the correction: {string.Join(", ", settled)}.");
                Save(window, "mini-resize-wider");

                // Narrower than 240 is not possible.
                Native.Rect wide = WindowRect(window);
                await DragAsync(window, new Point(wide.Left + (int)Math.Round(2 * scale), wide.Top + (int)Math.Round(60 * scale)), wide.Width, 0, "mini-resize-narrowing");
                (int width, int height) narrow = await ExpectCorrectedAsync(window, "after the narrowing drag");
                Assert.Equal(240, narrow.width);
                Save(window, "mini-resize-240");

                // Dragging the strip (left of its summary text, outside the toggles) moves the window, keeping its size.
                Native.Rect before = WindowRect(window);
                Rectangle strip = (await Ui.FindAsync(element, "CameraMiniSummary")).BoundingRectangle;
                await DragAsync(window, new Point(strip.Left + (int)Math.Round(30 * scale), strip.Top + (strip.Height / 2)), -120, -80, "mini-strip-moving");
                Native.Rect after = await Wait.StableAsync(() => WindowRect(window), WindowTimeout, "the moved window to settle");
                _report.AppendLine(CultureInfo.InvariantCulture, $"Strip drag: {before} → {after}");
                Assert.Equal((before.Width, before.Height), (after.Width, after.Height));
                // The window follows the pointer once the drag has begun (after the first step of 12 × 8 px, like dragging the image).
                Assert.True(after.Left - before.Left is >= -122 and <= -106 && after.Top - before.Top is >= -82 and <= -70, $"Dragging the strip by (-120, -80) moved the window {before} to {after}.");
            });
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "mini-resize-report");
        }
    }

    /// <summary>
    /// A <c>camera-windows.json</c> of version 0.1.0 (the remembered mini preview without the strip): 320 × 180 in the bottom
    /// right corner grows upwards by the strip and stays on the work area; 160 × 90 at the bottom edge is widened to 240 and
    /// stays on the work area (T-089).
    /// </summary>
    [Fact]
    public async Task RememberedMiniPreviewOfVersion010GrowsByTheStripOntoTheWorkArea()
    {
        E2EFixture.Require();
        TestMonitor primary = TestMonitors.Primary(TestMonitors.All());
        double scale = primary.Dpi / 96.0;
        int margin = CameraPlacement.MarginPx(primary.Dpi);

        // 320 × 180 in the bottom right corner, 16 DIP from the edges.
        int x = primary.Work.Right - margin - Px(ImageWidth, scale);
        int y = primary.Work.Bottom - margin - Px(ImageHeight, scale);
        await using (WidgetRun run = await WidgetRun.StartAsync(fixture, "mini-old-corner", OldState(x, y, ImageWidth, ImageHeight)))
        {
            await run.GuardAsync("mini-old-corner", async () =>
            {
                nint window = await run.OpenCameraAsync();
                await ExpectSizeAsync(window, StripCollapsed, "the old 320 × 180 mini preview");
                Native.Rect frame = await Wait.StableAsync(() => WindowRect(window), WindowTimeout, "the window to settle");
                _report.AppendLine(CultureInfo.InvariantCulture, $"0.1.0 320 × 180 at {x},{y} on {primary}: {frame}");
                Assert.True(Math.Abs(frame.Bottom - (primary.Work.Bottom - margin)) <= 2, $"The bottom edge moved: {frame}, expected {primary.Work.Bottom - margin}.");
                Assert.True(Math.Abs(frame.Left - x) <= 2, $"The left edge moved: {frame}, expected {x}.");
                AssertInsideTheWorkArea(window, "old corner");
                Save(window, "mini-old-corner");
            });
        }

        // 160 × 90 touching the bottom edge.
        int narrowX = primary.Work.Left + Px(200, scale);
        int narrowY = primary.Work.Bottom - Px(90, scale);
        await using (WidgetRun run = await WidgetRun.StartAsync(fixture, "mini-old-narrow", OldState(narrowX, narrowY, 160, 90)))
        {
            await run.GuardAsync("mini-old-narrow", async () =>
            {
                nint window = await run.OpenCameraAsync();
                await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) is (240, var h) && Math.Abs(h - (135 + StripCollapsed)) <= 1, WindowTimeout, $"240 × {135 + StripCollapsed} (now {WindowChecks.ClientSizeDip(window)})");
                Native.Rect frame = await Wait.StableAsync(() => WindowRect(window), WindowTimeout, "the window to settle");
                _report.AppendLine(CultureInfo.InvariantCulture, $"0.1.0 160 × 90 at {narrowX},{narrowY}: {frame}");
                Assert.True(Math.Abs(frame.Left - narrowX) <= 2, $"The left edge moved: {frame}, expected {narrowX} (widened to the right).");
                AssertInsideTheWorkArea(window, "old narrow");
                Save(window, "mini-old-narrow");
            });
        }

        Screenshots.SaveText(_report.ToString(), "mini-old-state-report");
    }

    /// <summary>A <c>settings.json</c> of version 0.1.0 with a mini preview narrower than 240: opened at 240 (16:9) with one warning in the log.</summary>
    [Fact]
    public async Task NarrowMiniPreviewOfVersion010SettingsLoadsAs240WithOneWarning()
    {
        E2EFixture.Require();
        JsonObject window = CameraPlacement.Window(null, "Mini");
        window["mini"] = new JsonObject { ["width"] = 160, ["height"] = 90, ["corner"] = "BottomRight" };
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "mini-old-settings", new WidgetRunOptions { Timing = LongPrint, CameraWindow = window });
        await run.GuardAsync("mini-old-settings", async () =>
        {
            nint hwnd = await run.OpenCameraAsync();
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(hwnd) is (240, var h) && Math.Abs(h - (135 + StripCollapsed)) <= 1, WindowTimeout, $"240 × {135 + StripCollapsed} (now {WindowChecks.ClientSizeDip(hwnd)})");
            AssertInsideTheWorkArea(hwnd, "narrow settings");
            Save(hwnd, "mini-old-settings");
            string[] warnings = [.. run.App.ReadLog().Split('\n').Where(static line => line.Contains("is narrower than 240; it is widened to 240", StringComparison.Ordinal))];
            Assert.True(warnings.Length == 1, $"Expected one warning about the narrow mini preview, found {warnings.Length}:{Environment.NewLine}{string.Join(Environment.NewLine, warnings)}");
            Assert.Contains(" WRN ", warnings[0], StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The strip's text and its name for screen readers (<c>CameraMiniSummary</c>) in the states of the simulator: printing
    /// ("19 % · 2 h 35 min"), cooling down and cooled down after the print (threshold 100 °C), paused, idle, offline.
    /// </summary>
    [Fact]
    public async Task StripTextFollowsThePrinterStates()
    {
        E2EFixture.Require();
        SimulationTiming timing = new() { HeatUp = TimeSpan.FromSeconds(5), Print = TimeSpan.FromSeconds(100), FinishedHold = TimeSpan.FromMinutes(5), IdleHold = TimeSpan.FromMinutes(10) };
        await using WidgetRun run = await WidgetRun.StartAsync(fixture, "mini-states", new WidgetRunOptions { Timing = timing, CooldownThresholdCelsius = 100, CameraWindow = CameraPlacement.Window(null, "Mini") });
        try
        {
            await run.GuardAsync("mini-states", async () =>
            {
                nint window = await run.OpenCameraAsync();
                AutomationElement element = run.App.Element(window);
                string printing = AppStrings.Get("en", "StatePrinting");
                await ExpectSummaryAsync(window, element, "printing", visible => PrintingSummary().IsMatch(visible), accessible => accessible.StartsWith(printing + ", ", StringComparison.Ordinal) && accessible.Contains(" remaining", StringComparison.Ordinal), TimeSpan.FromSeconds(60));

                string cooling = Prefix("CameraMiniCooling");
                await ExpectSummaryAsync(window, element, "cooling", visible => visible.StartsWith(cooling, StringComparison.Ordinal), accessible => accessible.StartsWith(cooling, StringComparison.Ordinal), TimeSpan.FromSeconds(150));
                string cooled = Prefix("CameraMiniCooled");
                await ExpectSummaryAsync(window, element, "cooled", visible => visible.StartsWith(cooled, StringComparison.Ordinal), accessible => accessible.StartsWith(cooled, StringComparison.Ordinal), TimeSpan.FromSeconds(120));

                string paused = AppStrings.Get("en", "StatePaused");
                run.Printer.Simulation.SetScenario(PrinterScenario.Paused);
                await ExpectSummaryAsync(window, element, "paused", visible => Regex.IsMatch(visible, $@"^\d+\s?% · {Regex.Escape(paused)}$"), accessible => accessible.StartsWith(paused + ", ", StringComparison.Ordinal), TimeSpan.FromSeconds(30));

                string idle = AppStrings.Get("en", "StateIdle");
                run.Printer.Simulation.SetScenario(PrinterScenario.Idle);
                await ExpectSummaryAsync(window, element, "idle", visible => visible == idle, accessible => accessible == idle, TimeSpan.FromSeconds(30));

                string offline = AppStrings.Get("en", "StateOffline");
                run.Printer.Simulation.SetOffline(true);
                await ExpectSummaryAsync(window, element, "offline", visible => visible == offline, accessible => accessible == offline, TimeSpan.FromSeconds(90));
                run.Printer.Simulation.SetOffline(false);
                await ExpectSummaryAsync(window, element, "online-idle", visible => visible == idle, accessible => accessible == idle, TimeSpan.FromSeconds(60));
            });
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "mini-states-report");
        }
    }

    /// <summary>"19 % · 2 h 35 min" (percent, then the remaining time).</summary>
    [GeneratedRegex(@"^\d+\s?% · \d+\s(h|min)")]
    private static partial Regex PrintingSummary();

    /// <summary>The text of <paramref name="key"/> before its first placeholder ("Cooling down… ").</summary>
    private static string Prefix(string key)
    {
        string text = AppStrings.Get("en", key);
        return text[..text.IndexOf('{', StringComparison.Ordinal)];
    }

    private async Task ExpectSummaryAsync(nint window, AutomationElement element, string state, Func<string, bool> visible, Func<string, bool> accessible, TimeSpan timeout)
    {
        (string Visible, string Accessible) summary = default;
        await Wait.UntilAsync(() => visible((summary = CameraWidgets.MiniSummary(element)).Visible) && accessible(summary.Accessible), timeout, $"the strip in the state {state} (now \"{summary.Visible}\" / \"{summary.Accessible}\")");
        _report.AppendLine(CultureInfo.InvariantCulture, $"{state}: \"{summary.Visible}\" / screen reader \"{summary.Accessible}\"");
        Save(window, "mini-state-" + state);
    }

    private static WidgetRunOptions OldState(int x, int y, int width, int height) => new()
    {
        Timing = LongPrint,
        DataFiles = new Dictionary<string, string>
        {
            ["camera-windows.json"] = new JsonObject
            {
                ["version"] = 1,
                ["windows"] = new JsonObject
                {
                    [WidgetRun.PrinterId] = new JsonObject
                    {
                        ["mode"] = "Mini",
                        ["mini"] = new JsonObject { ["x"] = x, ["y"] = y, ["width"] = width, ["height"] = height },
                    },
                },
            }.ToJsonString(),
        },
    };

    private static int Px(int dip, double scale) => (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);

    /// <summary>The client size is the 320 DIP wide 16:9 image with a strip of <paramref name="strip"/> DIP below it.</summary>
    private static Task ExpectSizeAsync(nint window, int strip, string what) =>
        Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == (ImageWidth, ImageHeight + strip), WindowTimeout, $"{what}: {ImageWidth} × {ImageHeight + strip} (now {WindowChecks.ClientSizeDip(window)})");

    /// <summary>The height matches the width: a 16:9 image and the collapsed strip (± 1 DIP); returns the size.</summary>
    private static async Task<(int Width, int Height)> ExpectCorrectedAsync(nint window, string what)
    {
        (int width, int height) size = default;
        await Wait.UntilAsync(
            () =>
            {
                size = WindowChecks.ClientSizeDip(window);
                return Math.Abs(size.height - ((size.width * 9.0 / 16) + StripCollapsed)) <= 1;
            },
            WindowTimeout,
            $"{what}: the height corrected to the width (now {WindowChecks.ClientSizeDip(window)})");
        return size;
    }

    /// <summary>The strip lies below the image (its top 180 DIP below the client's top) and ends with the client area.</summary>
    private void AssertStripBelowTheImage(nint window, AutomationElement element, int strip)
    {
        double scale = WindowChecks.Scaling(window);
        Native.Rect client = Native.VisibleClientRect(window);
        Rectangle bounds = Ui.TryFind(element, "CameraMiniInfoStrip")?.BoundingRectangle ?? Rectangle.Empty;
        _report.AppendLine(CultureInfo.InvariantCulture, $"Client {client}, strip {bounds}, scale {scale:0.##}");
        Assert.True(Math.Abs(bounds.Top - (client.Top + (ImageHeight * scale))) <= 1.5, $"The strip {bounds} does not start below the 16:9 image of {client}.");
        Assert.True(Math.Abs(bounds.Bottom - client.Bottom) <= 1 && Math.Abs(bounds.Height - (strip * scale)) <= 1.5, $"The strip {bounds} is not the {strip} DIP at the bottom of {client}.");
    }

    /// <summary>The window grew from the nearest edge: its bottom (or top) edge stayed, and it is inside the work area.</summary>
    private static void AssertGrewFrom(nint window, Native.Rect before, bool bottom, string context)
    {
        Native.Rect after = Native.FrameBounds(window);
        Assert.True(
            bottom ? Math.Abs(after.Bottom - before.Bottom) <= 1 && after.Top < before.Top : Math.Abs(after.Top - before.Top) <= 1 && after.Bottom > before.Bottom,
            $"{context}: {before} → {after} did not grow {(bottom ? "upwards (bottom edge kept)" : "downwards (top edge kept)")}.");
        AssertInsideTheWorkArea(window, context);
    }

    private static void AssertInsideTheWorkArea(nint window, string context)
    {
        Native.Rect frame = WindowRect(window);
        Native.Rect work = Native.MonitorOf(window).Work;
        Assert.True(frame.Left >= work.Left && frame.Top >= work.Top && frame.Right <= work.Right && frame.Bottom <= work.Bottom, $"{context}: the window {frame} is not inside the work area {work}.");
    }

    /// <summary>Focuses a strip toggle with Tab (the window is activated first by a click on the image).</summary>
    private async Task FocusAsync(nint window, string automationId)
    {
        if (Native.GetForegroundWindow() != window)
        {
            Native.Rect frame = WindowRect(window);
            double scale = WindowChecks.Scaling(window);
            await WindowActivation.ActivateAsync(window, new Point(frame.Left + (frame.Width / 2), frame.Top + (int)Math.Round(60 * scale)));
        }

        List<string> seen = [];
        for (int i = 0; i < 6; i++)
        {
            string focused = fixture.Automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault ?? string.Empty;
            if (focused == automationId)
            {
                return;
            }

            seen.Add(focused);
            WindowActivation.TypeInto(window, VirtualKeyShort.TAB);
            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Tab never focused {automationId} (focused: {string.Join(" → ", seen)}).");
    }

    /// <summary>
    /// Presses the left button at <paramref name="from"/>, moves by (<paramref name="dx"/>, <paramref name="dy"/>) in 10 steps
    /// and releases it; returns the window rectangle after each step (while the button is down).
    /// </summary>
    private static async Task<List<Native.Rect>> DragAsync(nint window, Point from, int dx, int dy, string screenshot)
    {
        Assert.True(Native.TopLevelWindowAt(from) == window, $"The drag start {from} is not on the camera window.");
        List<Native.Rect> during = [];
        Mouse.Position = from;
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        Mouse.Down(MouseButton.Left);
        try
        {
            for (int step = 1; step <= 10; step++)
            {
                Mouse.Position = new Point(from.X + (dx * step / 10), from.Y + (dy * step / 10));
                await Task.Delay(TimeSpan.FromMilliseconds(60), TestContext.Current.CancellationToken);
                during.Add(WindowRect(window));
            }

            Screenshots.Save(window, screenshot);
        }
        finally
        {
            Mouse.Up(MouseButton.Left);
        }

        return during;
    }

    private static async Task<List<Native.Rect>> SampleAsync(nint window, TimeSpan duration)
    {
        List<Native.Rect> samples = [];
        DateTime end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            samples.Add(WindowRect(window));
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        return samples;
    }

    private static string CameraWindowsFile(AppInstance app)
    {
        try
        {
            using FileStream stream = new(Path.Combine(app.DataDirectory, "camera-windows.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return string.Empty;
        }
    }

    private void Save(nint window, string name)
    {
        _report.AppendLine(CultureInfo.InvariantCulture, $"{name}: window {WindowRect(window)}, client {WindowChecks.ClientSizeDip(window)} DIP");
        Screenshots.Save(window, name);
    }

    private static Native.Rect WindowRect(nint hwnd)
    {
        Assert.True(Native.GetWindowRect(hwnd, out Native.Rect rect));
        return rect;
    }
}
