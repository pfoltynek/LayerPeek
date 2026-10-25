using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Camera;
using LayerPeek.Simulator.Printers;

using Wait = LayerPeek.App.E2E.Tests.Infrastructure.Wait;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The overlay widgets and the mini preview at the display scales of T-076 (T-093): 100, 125, 150 and 200 % of the primary
/// monitor (the cards in their anchors clear of the window buttons, the G-code thumbnail 40 DIP and sharp, i.e. decoded at
/// the monitor's pixels, the mini preview 320 × 213 DIP inside the work area), and a second monitor with another scale (the
/// window moved there with Win+Shift+→ decodes the thumbnail again, the mini preview grows inside its work area). Display
/// scale changes go through the journal of T-065/T-066 and are restored.
/// </summary>
public sealed class CameraWidgetScaleTests(E2EFixture fixture)
{
    private static readonly int[] Percents = [100, 125, 150, 200];

    private static readonly SimulationTiming LongPrint = new() { HeatUp = TimeSpan.FromSeconds(5), Print = TimeSpan.FromMinutes(30) };

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly StringBuilder _report = new();

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public async Task WidgetsThumbnailAndMiniPreviewAtEveryScale()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TestMonitor primary = TestMonitors.Primary(TestMonitors.All());
        Assert.False(string.IsNullOrEmpty(primary.DevicePath), $"No device path of {primary}.");
        await using SimulatedPrinter printer = await SimulatedPrinter.StartAsync(new SimulatedPrinterOptions { Timing = LongPrint }, cancellationToken);
        await using RtspCamera camera = Simulators.StartCamera();
        _report.Append(DisplayConfig.Report());
        try
        {
            await using SystemChange<DisplayScaleState> scale = await SystemChanges.DisplayScaleAsync(cancellationToken);
            foreach (int percent in Percents)
            {
                uint dpi = await scale.SetPercentAsync(primary.DevicePath, percent, cancellationToken);
                _report.AppendLine(CultureInfo.InvariantCulture, $"{percent} % ({dpi} dpi):");
                await using AppInstance app = await AppInstance.StartAsync(
                    fixture,
                    $"widgets-scale-{percent}",
                    WidgetRun.SettingsJson(printer.Address(), camera.StreamUri, new WidgetRunOptions()),
                    Simulators.Secrets(WidgetRun.PrinterId, printer));
                await FailureScreenshots.GuardAsync(app, $"widgets-scale-{percent}", async () =>
                {
                    nint window = await app.OpenAsync($"camera:{WidgetRun.PrinterId}", AppWindowKind.Camera);
                    AutomationElement element = app.Element(window);
                    Assert.Equal(dpi, Native.GetDpiForWindow(window));
                    await ExpectNormalWindowAsync(window, element, printer, $"scale-{percent}");

                    // Full screen: the same cards in the corners of the monitor.
                    await Ui.InvokeAsync(element, "CameraFullScreenButton");
                    await Wait.UntilAsync(() => Native.GetWindowRect(window, out Native.Rect rect) && rect.Equals(Native.MonitorOf(window).Monitor), Timeout, $"full screen at {percent} %");
                    Native.Rect monitor = Native.MonitorOf(window).Monitor;
                    List<string> problems = [];
                    try
                    {
                        await Wait.UntilAsync(() => (problems = CameraWidgets.LayoutProblems(element, new Rectangle(monitor.Left, monitor.Top, monitor.Width, monitor.Height), CameraWidgets.DefaultPrinting)).Count == 0, TimeSpan.FromSeconds(10), $"the cards in full screen at {percent} %");
                    }
                    catch (WaitTimeoutException)
                    {
                        // Reported below.
                    }

                    _report.AppendLine(CultureInfo.InvariantCulture, $"  full screen {monitor}:").AppendLine(CameraWidgets.Describe(element));
                    Screenshots.Save(window, $"widgets-scale-{percent}-full-screen");
                    Assert.True(problems.Count == 0, $"full screen at {percent} %: {string.Join("; ", problems)}");

                    // The mini preview: the 320 × 180 image and the strip, in DIP of this scale, inside the work area.
                    await Ui.InvokeAsync(element, "CameraMiniButton");
                    await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) is (320, var h) && Math.Abs(h - 213) <= 1, Timeout, $"the mini preview at {percent} % (now {WindowChecks.ClientSizeDip(window)})");
                    AssertInsideTheWorkArea(window, $"mini preview at {percent} %");
                    Rectangle strip = (await Ui.FindAsync(element, "CameraMiniInfoStrip")).BoundingRectangle;
                    Native.Rect client = Native.VisibleClientRect(window);
                    _report.AppendLine(CultureInfo.InvariantCulture, $"  mini: client {client}, strip {strip}");
                    Assert.True(Math.Abs(strip.Top - (client.Top + (180 * dpi / 96.0))) <= 1.5 && Math.Abs(strip.Bottom - client.Bottom) <= 1, $"The strip {strip} is not below the image of {client} at {percent} %.");
                    Assert.Empty(CameraWidgets.ShownCards(element));
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                    Screenshots.Save(window, $"widgets-scale-{percent}-mini");
                });
            }
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "widgets-scale-report");
        }
    }

    /// <summary>
    /// A second monitor at 150 % next to the primary at 100 %: the normal window moved there with the real keyboard
    /// (Win+Shift+→) shows the thumbnail at 60 px, decoded again for the new scale; the mini preview opened there grows inside
    /// that monitor's work area when a row is expanded. Skipped without a second monitor.
    /// </summary>
    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public async Task ThumbnailAndMiniPreviewOnASecondMonitorWithAnotherScale()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        List<TestMonitor> monitors = TestMonitors.All();
        TestMonitor primary = TestMonitors.Primary(monitors);
        TestMonitor? second = monitors.FirstOrDefault(static monitor => !monitor.Primary);
        Assert.SkipWhen(second is null, "Needs a second monitor.");
        await using SimulatedPrinter printer = await SimulatedPrinter.StartAsync(new SimulatedPrinterOptions { Timing = LongPrint }, cancellationToken);
        await using RtspCamera camera = Simulators.StartCamera();
        try
        {
            await using SystemChange<DisplayScaleState> scale = await SystemChanges.DisplayScaleAsync(cancellationToken);
            uint primaryDpi = await scale.SetPercentAsync(primary.DevicePath, 100, cancellationToken);
            uint secondDpi = await scale.SetPercentAsync(second!.DevicePath, 150, cancellationToken);
            _report.Append(DisplayConfig.Report());
            await using AppInstance app = await AppInstance.StartAsync(
                fixture,
                "widgets-second-monitor",
                WidgetRun.SettingsJson(printer.Address(), camera.StreamUri, new WidgetRunOptions { CameraWindow = CameraPlacement.Window(null, "Normal", normalWidth: 800, normalHeight: 450) }),
                Simulators.Secrets(WidgetRun.PrinterId, printer));
            await FailureScreenshots.GuardAsync(app, "widgets-second-monitor", async () =>
            {
                nint window = await app.OpenAsync($"camera:{WidgetRun.PrinterId}", AppWindowKind.Camera);
                AutomationElement element = app.Element(window);
                Assert.Equal(primaryDpi, Native.GetDpiForWindow(window));
                await ExpectNormalWindowAsync(window, element, printer, "second-monitor-before");

                // Dragged by its title bar with the real mouse onto the middle of the other monitor, like a user moves it.
                Native.Rect frame = Native.FrameBounds(window);
                Point grip = new(frame.Left + (frame.Width / 2), frame.Top + 10);
                Assert.Equal(window, Native.TopLevelWindowAt(grip));
                Point drop = new(second.Work.Left + (second.Work.Width / 2), second.Work.Top + (second.Work.Height / 3));
                Mouse.Position = grip;
                Mouse.Down(MouseButton.Left);
                try
                {
                    for (int step = 1; step <= 20; step++)
                    {
                        Mouse.Position = new Point(grip.X + ((drop.X - grip.X) * step / 20), grip.Y + ((drop.Y - grip.Y) * step / 20));
                        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                    }
                }
                finally
                {
                    Mouse.Up(MouseButton.Left);
                }

                await Wait.UntilAsync(() => Native.GetDpiForWindow(window) == secondDpi, Timeout, $"the window on {second} ({secondDpi} dpi)");
                _report.AppendLine(CultureInfo.InvariantCulture, $"Moved to {Native.MonitorOf(window).Monitor} (expected {second.Bounds})");
                await ExpectNormalWindowAsync(window, element, printer, "second-monitor-after");

                // The mini preview there: a row expanded grows it inside the work area of that monitor.
                await Ui.InvokeAsync(element, "CameraMiniButton");
                await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) is (320, var h) && Math.Abs(h - 213) <= 1, Timeout, $"the mini preview at 150 % (now {WindowChecks.ClientSizeDip(window)})");
                Assert.Equal(secondDpi, Native.GetDpiForWindow(window));
                Rectangle toggle = (await Ui.FindAsync(element, "CameraMiniToggleChart")).BoundingRectangle;
                Mouse.Click(new Point(toggle.Left + (toggle.Width / 2), toggle.Top + (toggle.Height / 2)));
                await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) is (320, var h) && Math.Abs(h - 277) <= 1, Timeout, $"the chart row at 150 % (now {WindowChecks.ClientSizeDip(window)})");
                AssertInsideTheWorkArea(window, "mini preview on the second monitor");
                Assert.Equal(secondDpi, Native.GetDpiForWindow(window));
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                Screenshots.Save(window, "widgets-second-monitor-mini");
            });
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "widgets-second-monitor-report");
        }
    }

    /// <summary>
    /// The normal window: the cards of the running print in their anchors clear of the window buttons, and the thumbnail
    /// 40 DIP in pixels of the window's scale and sharp.
    /// </summary>
    private async Task ExpectNormalWindowAsync(nint window, AutomationElement element, SimulatedPrinter printer, string name)
    {
        double scale = WindowChecks.Scaling(window);
        AutomationElement thumbnail = null!;
        await Wait.UntilAsync(
            () => (thumbnail = Ui.TryFind(element, "CameraOverlayWidget-print-name-Thumbnail")!) is not null && !thumbnail.Properties.IsOffscreen.ValueOrDefault && Math.Abs(thumbnail.BoundingRectangle.Width - (40 * scale)) <= 1.5,
            Timeout,
            $"{name}: the thumbnail of 40 DIP at {scale:0.##}");
        List<string> problems = [];
        try
        {
            await Wait.UntilAsync(() => (problems = CameraWidgets.LayoutProblems(element, CameraWidgets.ImageArea(window, element), CameraWidgets.DefaultPrinting)).Count == 0, TimeSpan.FromSeconds(10), $"{name}: the cards in their anchors");
        }
        catch (WaitTimeoutException)
        {
            // Reported below.
        }

        // The animation of a card coming in, and the latest frame of the video, settle first.
        await Task.Delay(TimeSpan.FromMilliseconds(800), TestContext.Current.CancellationToken);
        Rectangle box = thumbnail.BoundingRectangle;
        (double captured, double blurred) = Sharpness(window, box, printer.Simulation.GetStatus().Job?.File, name);
        _report.AppendLine(CultureInfo.InvariantCulture, $"  {name}: scale {scale:0.##}, thumbnail {box}, difference from the sharp preview {captured:0.0} (a 2× upscaled one {blurred:0.0})")
            .AppendLine(CameraWidgets.Describe(element));
        Screenshots.Save(window, $"widgets-{name}");
        Assert.True(problems.Count == 0, $"{name}: {string.Join("; ", problems)}");
        // Sharp: clearly closer to the preview decoded at these pixels than a blurred one (measured 0.35–0.5 of it).
        Assert.True(captured < 0.75 * blurred, $"{name}: the thumbnail is not sharp: its edges differ from the preview resized to {box.Width} px by {captured:0.0}, not clearly less than an upscaled one ({blurred:0.0}).");
    }

    /// <summary>
    /// How far the drawn thumbnail is from the simulator's preview resized straight to the drawn pixels, and how far a
    /// preview decoded at half the pixels and scaled up would be from the same reference: the mean absolute difference of the
    /// channels (0–255) at the edges of the reference's picture (where blur shows; the flat areas match anyway). A thumbnail
    /// decoded at the monitor's pixels is closer than the blurred one. The image is centred in its square, which at some
    /// scales puts it half a pixel down: both whole-pixel places are tried.
    /// </summary>
    private static (double Captured, double Blurred) Sharpness(nint window, Rectangle box, SimulatedFile? file, string name)
    {
        Assert.NotNull(file);
        using Bitmap capture = Screenshots.Capture(window);
        Assert.True(Native.GetWindowRect(window, out Native.Rect frame));
        int width = box.Width;
        int height = (int)Math.Round(width * (double)PreviewImages.LargeHeight / PreviewImages.LargeWidth);
        using Bitmap preview = new(new MemoryStream(PreviewImages.For(file!, large: true)));
        using Bitmap reference = Resize(preview, width, height);
        using Bitmap half = Resize(preview, Math.Max(1, width / 2), Math.Max(1, height / 2));
        using Bitmap upscaled = Resize(half, width, height);
        bool[,] edges = Edges(reference);
        double captured = double.MaxValue;
        // A fractional height (e.g. 37.5 px of a 50 px square at 125 %) is drawn over 37 or 38 rows.
        double exact = width * (double)PreviewImages.LargeHeight / PreviewImages.LargeWidth;
        foreach (int rows in new[] { (int)Math.Floor(exact), (int)Math.Ceiling(exact) }.Distinct())
        {
            using Bitmap expected = Resize(preview, width, rows);
            bool[,] expectedEdges = Edges(expected);
            foreach (int offset in new[] { (box.Height - rows) / 2, (box.Height - rows + 1) / 2 }.Distinct())
            {
                Rectangle image = new(box.Left - frame.Left, box.Top - frame.Top + offset, width, rows);
                using Bitmap drawn = capture.Clone(image, PixelFormat.Format32bppArgb);
                double difference = EdgeDifference(drawn, expected, expectedEdges);
                if (difference < captured)
                {
                    captured = difference;
                    Screenshots.Save(drawn, $"widgets-{name}-thumbnail");
                }
            }
        }

        Screenshots.Save(reference, $"widgets-{name}-thumbnail-reference");
        return (captured, EdgeDifference(upscaled, reference, edges));
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        Bitmap result = new(width, height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        using ImageAttributes attributes = new();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return result;
    }

    /// <summary>The pixels of <paramref name="image"/> on an edge of its picture (a clear change between neighbours), 2 px inside its border.</summary>
    private static bool[,] Edges(Bitmap image)
    {
        bool[,] edges = new bool[image.Width, image.Height];
        for (int y = 2; y < image.Height - 2; y++)
        {
            for (int x = 2; x < image.Width - 2; x++)
            {
                edges[x, y] = Distance(image.GetPixel(x - 1, y), image.GetPixel(x + 1, y)) + Distance(image.GetPixel(x, y - 1), image.GetPixel(x, y + 1)) > 60;
            }
        }

        return edges;
    }

    private static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    /// <summary>Mean absolute difference of the RGB channels at <paramref name="edges"/>.</summary>
    private static double EdgeDifference(Bitmap first, Bitmap second, bool[,] edges)
    {
        long sum = 0;
        long count = 0;
        for (int y = 0; y < first.Height; y++)
        {
            for (int x = 0; x < first.Width; x++)
            {
                if (edges[x, y])
                {
                    sum += Distance(first.GetPixel(x, y), second.GetPixel(x, y));
                    count += 3;
                }
            }
        }

        Assert.True(count > 0, "The preview has no edges to compare.");
        return (double)sum / count;
    }

    private static void AssertInsideTheWorkArea(nint window, string context)
    {
        Assert.True(Native.GetWindowRect(window, out Native.Rect frame));
        Native.Rect work = Native.MonitorOf(window).Work;
        Assert.True(frame.Left >= work.Left && frame.Top >= work.Top && frame.Right <= work.Right && frame.Bottom <= work.Bottom, $"{context}: the window {frame} is not inside the work area {work}.");
    }
}
