using System.Drawing;
using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;

using LayerPeek.App.E2E.Tests.Infrastructure;

using static LayerPeek.App.E2E.Tests.Infrastructure.CameraWindowChecks;

using Wait = LayerPeek.App.E2E.Tests.Infrastructure.Wait;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The outline of the borderless mini preview on the real app (T-118; manual check of T-114 replaced by T-120): a 1 DIP
/// line of white at 45 % (<c>#737373</c> over the black image) round the image and the strip, only in the mini preview,
/// brighter (<c>#BFBFBF</c>) while the pointer is over the image, the strip or a grip; it stands out from a light and from a
/// dark background behind the window (windows of the test process, never the user's desktop); it is drawn in whole device
/// pixels at 100, 125 and 150 % (1, 1 and 2 px, no blurred half pixel); the resize grips under it still show the sizing
/// cursors. The camera address answers nothing, so the image is black. Only the area of the window and a few pixels around
/// it are captured from the screen (<see cref="ScreenArea"/>); display scale changes go through the journal of T-065/T-066.
/// </summary>
public sealed class CameraMiniOutlineTests(E2EFixture fixture)
{
    /// <summary>The captured margin round the client area: the background right next to the outline.</summary>
    private const int Margin = 6;

    /// <summary>White at 45 % over black, and at 75 % while the pointer is over the window.</summary>
    private const int Normal = 0x73;
    private const int Hover = 0xBF;

    /// <summary>How far a pixel of the outline may be from its colour (the compositor and the capture keep it exact in practice).</summary>
    private const int Tolerance = 10;

    /// <summary>The outline must differ from what lies next to it outside by at least this much (0–255), on either background.</summary>
    private const int MinContrast = 40;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly StringBuilder _report = new();

    private enum Edge
    {
        Left,
        Top,
        Right,
        Bottom,
    }

    /// <summary>
    /// The mini preview over a white and over a dark background: the outline on all four edges in its colour, clearly apart
    /// from the background outside and the black image inside; brighter with the pointer over the image, over the strip and
    /// over the left grip, dark again after the pointer left. The normal window and full screen have no outline.
    /// </summary>
    [Fact]
    public async Task OutlineIsVisibleOnALightAndADarkBackgroundAndBrightensUnderThePointer()
    {
        E2EFixture.Require();
        await using AppInstance app = await CameraWindowSettings.StartAsync(fixture, "mini-outline", CameraPlacement.Window(null, "Mini", miniCorner: "Center"));
        await FailureScreenshots.GuardAsync(app, "mini-outline", async () =>
        {
            nint window = await CameraWindowSettings.OpenCameraAsync(app);
            AutomationElement element = app.Element(window);
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) is (320, var h) && Math.Abs(h - 213) <= 1, Timeout, "the mini preview 320 × 213");
            List<string> problems = [];
            foreach ((string name, Color background) in new[] { ("light", LightBackground), ("dark", DarkBackground) })
            {
                using CoverWindow cover = await CoverWindow.CreateAsync(CoverWindow.BoundsFor(Native.FrameBounds(window), Native.MonitorOf(window).Monitor, BackgroundMargin), background);
                // Activated like a user switching to it: the mini preview (always on top) stays in front of it.
                await cover.BringToFrontAsync();
                Assert.True(CoverWindow.IsAbove(window, cover.Handle), $"{name}: the mini preview is not in front of the background window.");
                await MovePointerBesideAsync(window, cover);
                problems.AddRange(await ExpectOutlineAsync(window, background, Normal, $"{name}-pointer-away"));

                await MovePointerOntoAsync(window, ImageMiddle(window, mini: true));
                problems.AddRange(await ExpectOutlineAsync(window, background, Hover, $"{name}-over-image"));

                // Captured before the strip's tooltip (0.4 s) could cover the bottom edge; the outline fades in 0.15 s.
                Rectangle strip = (await Ui.FindAsync(element, "CameraMiniSummary")).BoundingRectangle;
                await MovePointerOntoAsync(window, new Point(strip.Left + (int)Math.Round(30 * WindowChecks.Scaling(window)), strip.Top + (strip.Height / 2)), TimeSpan.FromMilliseconds(250));
                problems.AddRange(await ExpectOutlineAsync(window, background, Hover, $"{name}-over-strip"));

                Native.Rect client = Native.VisibleClientRect(window);
                await MovePointerOntoAsync(window, new Point(client.Left + 1, client.Top + (client.Height / 3)));
                problems.AddRange(await ExpectOutlineAsync(window, background, Hover, $"{name}-over-left-grip"));

                await MovePointerBesideAsync(window, cover);
                problems.AddRange(await ExpectOutlineAsync(window, background, Normal, $"{name}-pointer-left"));
            }

            // The normal window and full screen: no outline, the black image reaches the edges of the client area.
            await Ui.InvokeAsync(element, "CameraNormalButton");
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == (960, 540), Timeout, "the normal window");
            problems.AddRange(await ExpectNoOutlineAsync(window, "normal"));
            await Ui.InvokeAsync(element, "CameraFullScreenButton");
            await Wait.UntilAsync(() => Native.GetWindowRect(window, out Native.Rect rect) && rect.Equals(Native.MonitorOf(window).Monitor), Timeout, "full screen");
            problems.AddRange(await ExpectNoOutlineAsync(window, "full-screen"));
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });
        Screenshots.SaveText(_report.ToString(), "mini-outline-report");
    }

    /// <summary>
    /// The resize grips under the outline: the pointer on each edge and corner of the mini preview (on the outline) shows the
    /// system sizing cursor of that edge, over the image the arrow. (Resizing with a grip and moving by the image and the
    /// strip: <see cref="CameraMiniPreviewTests.ResizeGripCorrectsTheHeightOnceAfterTheReleaseAndTheStripMovesTheWindow"/>.)
    /// </summary>
    [Fact]
    public async Task GripsUnderTheOutlineShowTheSizingCursors()
    {
        E2EFixture.Require();
        await using AppInstance app = await CameraWindowSettings.StartAsync(fixture, "mini-outline-grips", CameraPlacement.Window(null, "Mini", miniCorner: "Center"));
        await FailureScreenshots.GuardAsync(app, "mini-outline-grips", async () =>
        {
            nint window = await CameraWindowSettings.OpenCameraAsync(app);
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) is (320, var h) && Math.Abs(h - 213) <= 1, Timeout, "the mini preview 320 × 213");
            using CoverWindow background = await BackgroundBelowAsync(window, DarkBackground);
            Native.Rect c = Native.VisibleClientRect(window);
            int midX = c.Left + (c.Width / 2);
            int midY = c.Top + (c.Height / 3);
            (string Name, Point Point, int Cursor)[] spots =
            [
                // The outermost pixels (where the outline lies), the corners 2 px in; the bottom ones over the strip.
                ("left", new Point(c.Left, midY), Native.IdcSizeWe),
                ("right", new Point(c.Right - 1, midY), Native.IdcSizeWe),
                ("top", new Point(midX, c.Top), Native.IdcSizeNs),
                ("bottom", new Point(midX, c.Bottom - 1), Native.IdcSizeNs),
                ("top-left", new Point(c.Left + 2, c.Top + 2), Native.IdcSizeNwse),
                ("bottom-right", new Point(c.Right - 1, c.Bottom - 1), Native.IdcSizeNwse),
                ("top-right", new Point(c.Right - 3, c.Top + 2), Native.IdcSizeNesw),
                ("bottom-left", new Point(c.Left, c.Bottom - 1), Native.IdcSizeNesw),
                ("image", ImageMiddle(window, mini: true), 32512),
            ];
            List<string> problems = [];
            foreach ((string name, Point point, int cursor) in spots)
            {
                await MovePointerOntoAsync(window, point);
                nint expected = Native.SystemCursor(cursor);
                bool shown = await TryWaitAsync(() => Native.CurrentCursor() == expected, TimeSpan.FromSeconds(3));
                _report.AppendLine(CultureInfo.InvariantCulture, $"{name} at {point.X},{point.Y}: cursor 0x{Native.CurrentCursor():X}, expected IDC {cursor} 0x{expected:X}: {(shown ? "ok" : "WRONG")}");
                if (!shown)
                {
                    problems.Add($"{name}: the pointer at {point.X},{point.Y} of the client area {c} shows the cursor 0x{Native.CurrentCursor():X}, not the system cursor {cursor} (0x{expected:X}).");
                }

                await MovePointerBesideAsync(window, background);
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });
        Screenshots.SaveText(_report.ToString(), "mini-outline-grips-report");
    }

    /// <summary>
    /// The outline at 100, 125 and 150 % of the primary monitor over a white background: exactly 1, 1 and 2 device pixels of
    /// its colour on every edge, then the black image (or the strip) at once – no blurred half pixel inside or outside; the
    /// checked pin is drawn dimmed in the corner at every scale (its look is in the results for a person to judge).
    /// </summary>
    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public async Task OutlineIsCrispInWholeDevicePixelsAtEveryScale()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TestMonitor primary = TestMonitors.Primary(TestMonitors.All());
        Assert.False(string.IsNullOrEmpty(primary.DevicePath), $"No device path of {primary}.");
        _report.Append(DisplayConfig.Report());
        List<string> problems = [];
        try
        {
            await using SystemChange<DisplayScaleState> scale = await SystemChanges.DisplayScaleAsync(cancellationToken);
            foreach ((int percent, int pixels) in new[] { (100, 1), (125, 1), (150, 2) })
            {
                uint dpi = await scale.SetPercentAsync(primary.DevicePath, percent, cancellationToken);
                _report.AppendLine(CultureInfo.InvariantCulture, $"{percent} % ({dpi} dpi), expected {pixels} px:");
                await using AppInstance app = await CameraWindowSettings.StartAsync(fixture, $"mini-outline-{percent}", CameraPlacement.Window(null, "Mini", miniCorner: "Center"));
                await FailureScreenshots.GuardAsync(app, $"mini-outline-{percent}", async () =>
                {
                    nint window = await CameraWindowSettings.OpenCameraAsync(app);
                    AutomationElement element = app.Element(window);
                    await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) is (320, var h) && Math.Abs(h - 213) <= 1, Timeout, $"the mini preview 320 × 213 at {percent} %");
                    Assert.Equal(dpi, Native.GetDpiForWindow(window));
                    using CoverWindow cover = await CoverWindow.CreateAsync(CoverWindow.BoundsFor(Native.FrameBounds(window), Native.MonitorOf(window).Monitor, BackgroundMargin), LightBackground);
                    await cover.BringToFrontAsync();
                    await MovePointerBesideAsync(window, cover);
                    problems.AddRange(await ExpectOutlineAsync(window, LightBackground, Normal, $"scale-{percent}", pixels));

                    ButtonLook look = Look(window, element);
                    Rectangle pin = Pin(element).Bounds;
                    using (Bitmap pinCapture = ScreenArea.CaptureBitmap(pin, 4))
                    {
                        Screenshots.Save(pinCapture, $"mini-outline-{percent}-pin");
                    }

                    _report.AppendLine(CultureInfo.InvariantCulture, $"  pin {pin}: {look}");
                    if (!look.PinDimmed || !ButtonLook.IsHidden(look.Others))
                    {
                        problems.Add($"{percent} %: the checked pin is not drawn dimmed alone without the pointer ({look}).");
                    }
                });
            }
        }
        finally
        {
            Screenshots.SaveText(_report.ToString(), "mini-outline-scale-report");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// Captures the client area and <see cref="Margin"/> px round it from the screen once the outline has its colour
    /// (it fades in 0.15 s) and checks the middle of every edge; returns the problems (empty: as expected).
    /// </summary>
    private async Task<List<string>> ExpectOutlineAsync(nint window, Color background, int colour, string label, int? pixels = null)
    {
        Native.Rect client = Native.VisibleClientRect(window);
        Rectangle area = Rectangle.FromLTRB(client.Left - Margin, client.Top - Margin, client.Right + Margin, client.Bottom + Margin);
        double scale = WindowChecks.Scaling(window);
        List<string> problems = [];
        Pixels shot = ScreenArea.Capture(area);
        try
        {
            await Wait.UntilAsync(() => (problems = OutlineProblems(shot = ScreenArea.Capture(area), client, scale, background, colour, pixels)).Count == 0, TimeSpan.FromSeconds(2), $"{label}: the outline");
        }
        catch (WaitTimeoutException)
        {
            // Reported below with the pixels.
        }

        string file = shot.Save($"mini-outline-{label}");
        _report.AppendLine(CultureInfo.InvariantCulture, $"{label}: client {client}, scale {scale:0.##}, capture {Path.GetFileName(file)}");
        foreach (Edge edge in Enum.GetValues<Edge>())
        {
            _report.AppendLine(CultureInfo.InvariantCulture, $"  {edge}: {string.Join(" ", Walk(shot, client, scale, edge).Select(static c => ((c.R + c.G + c.B) / 3).ToString("X2", CultureInfo.InvariantCulture)))}");
        }

        return [.. problems.Select(problem => $"{label}: {problem}")];
    }

    /// <summary>
    /// What is wrong with the outline in <paramref name="shot"/> at the middle of each edge: the pixels from the outside in
    /// (<see cref="Walk"/>) must be the background, then the outline in <paramref name="colour"/> (exactly
    /// <paramref name="pixels"/> of them when given, else at least one), then at once the black image (the strip's dark
    /// background at the bottom); outside, the pixel next to the outline is the background, not a blend with the outline.
    /// </summary>
    private static List<string> OutlineProblems(Pixels shot, Native.Rect client, double scale, Color background, int colour, int? pixels)
    {
        List<string> problems = [];
        int backgroundLevel = Level(background);
        foreach (Edge edge in Enum.GetValues<Edge>())
        {
            Color[] walk = Walk(shot, client, scale, edge);
            int[] levels = [.. walk.Select(Level)];
            string shown = string.Join(" ", levels.Select(static level => level.ToString("X2", CultureInfo.InvariantCulture)));
            // The strip (#141416) under the bottom edge lifts the outline a little: 0x73 + 0.55 × 0x14 ≈ 0x7E.
            int expected = edge == Edge.Bottom ? colour + (int)Math.Round(0x14 * (1 - (colour / 255.0))) : colour;
            if (Math.Abs(levels[0] - backgroundLevel) > Tolerance)
            {
                problems.Add($"{edge}: {Margin} px outside the window is {levels[0]:X2}, not the background {backgroundLevel:X2} (something else is behind the window): {shown}.");
                continue;
            }

            int count = 0;
            while (Margin + count < levels.Length && Math.Abs(levels[Margin + count] - expected) <= Tolerance)
            {
                count++;
            }

            int outside = levels[Margin - 1];
            int inner = Margin + count < levels.Length ? levels[Margin + count] : 255;
            if (count == 0)
            {
                problems.Add($"{edge}: no outline of {expected:X2} at the edge of the client area: {shown}.");
            }
            else if (pixels is { } exact && count != exact)
            {
                problems.Add($"{edge}: the outline is {count} px wide, expected {exact} px at {scale:0.##}: {shown}.");
            }

            if (count > 0 && inner > (edge == Edge.Bottom ? 0x30 : 0x10))
            {
                problems.Add($"{edge}: after {count} px of outline comes {inner:X2}, not the black image / the strip (a blurred half pixel): {shown}.");
            }

            if (Math.Abs(outside - backgroundLevel) >= Math.Abs(outside - expected))
            {
                problems.Add($"{edge}: the pixel outside next to the outline is {outside:X2}, closer to the outline {expected:X2} than to the background {backgroundLevel:X2} (blurred outward): {shown}.");
            }

            if (count > 0 && Math.Abs(levels[Margin] - outside) < MinContrast)
            {
                problems.Add($"{edge}: the outline {levels[Margin]:X2} hardly differs from the background next to it {outside:X2} (at least {MinContrast} needed): {shown}.");
            }
        }

        return problems;
    }

    /// <summary>
    /// The pixels across the middle of <paramref name="edge"/> from the outside in: <see cref="Margin"/> pixels of the
    /// background, then the client area's outermost pixels (the outline) and 6 more. The left, right and top edges are taken
    /// across the image (at a third of its height / at half the width, clear of the window buttons and the status), the
    /// bottom edge across the strip 60 DIP from the left.
    /// </summary>
    private static Color[] Walk(Pixels shot, Native.Rect client, double scale, Edge edge)
    {
        int width = client.Width;
        int height = client.Height;
        int imageHeight = (int)Math.Round(width * 9.0 / 16);
        Color[] walk = new Color[Margin + 6];
        for (int i = 0; i < walk.Length; i++)
        {
            int inward = i - Margin;
            (int x, int y) = edge switch
            {
                Edge.Left => (inward, imageHeight / 3),
                Edge.Right => (width - 1 - inward, imageHeight / 3),
                Edge.Top => (width / 2, inward),
                _ => ((int)(60 * scale), height - 1 - inward),
            };
            walk[i] = shot[x + Margin, y + Margin];
        }

        return walk;
    }

    private static int Level(Color c) => (c.R + c.G + c.B) / 3;

    /// <summary>The normal window or full screen: the client area's edges are the black image (no outline), from a PrintWindow capture.</summary>
    private async Task<List<string>> ExpectNoOutlineAsync(nint window, string label)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        using Bitmap capture = Screenshots.Capture(window);
        Screenshots.Save(capture, $"mini-outline-none-{label}");
        Native.GetWindowRect(window, out Native.Rect rect);
        Native.Rect client = Native.VisibleClientRect(window);
        int left = client.Left - rect.Left;
        int top = client.Top - rect.Top;
        int y = top + (client.Height / 3);
        int x = left + (client.Width / 2);
        (string Edge, Color Pixel)[] edges =
        [
            ("left", capture.GetPixel(left, y)),
            ("right", capture.GetPixel(left + client.Width - 1, y)),
            ("top", capture.GetPixel(x, top)),
            ("bottom", capture.GetPixel(x, top + client.Height - 1)),
        ];
        _report.AppendLine(CultureInfo.InvariantCulture, $"{label}: client {client}, edges {string.Join(", ", edges.Select(static e => $"{e.Edge} {Level(e.Pixel):X2}"))}");
        return [.. edges.Where(static e => Level(e.Pixel) > 0x10).Select(e => $"{label}: the {e.Edge} edge of the client area is {Level(e.Pixel):X2}, not the black image (an outline outside the mini preview?).")];
    }

    private static async Task<bool> TryWaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        try
        {
            await Wait.UntilAsync(condition, timeout, "the condition");
            return true;
        }
        catch (WaitTimeoutException)
        {
            return false;
        }
    }
}
