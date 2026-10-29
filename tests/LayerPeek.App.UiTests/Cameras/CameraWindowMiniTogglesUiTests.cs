using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Mini;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Diagnostics.Chart;
using LayerPeek.App.Localization;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The collapsible print name and temperature chart rows of the mini preview strip and their toggle buttons (T-090, design
/// T-076 4) in the real camera window on the fake monitors of <see cref="FakeScreenCatalog.ThreeMonitors"/> (primary
/// 2560 × 1440 at 100 % with a 48 px taskbar; the default mini preview is in the bottom right corner, 16 px from it).
/// </summary>
public sealed class CameraWindowMiniTogglesUiTests(HeadlessSession session)
{
    private const uint Grey = 0xFF406080;
    private const string LongName = "foldable_book_holder_with_a_really_long_name_for_the_strip_v12_0.4n_0.2mm_PLA,PETG_COREONE_2h51m.bgcode";
    private const string TemplateName = "foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode";

    private static readonly CameraWindowDefaults MiniFirst = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };

    private static readonly TimeSpan HistoryInterval = TimeSpan.FromSeconds(20);

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root) ?? throw new InvalidOperationException("Not in the window."), control.Bounds.Size);

    private static ToggleButton NameToggle(CameraWindow window) => window.Find<ToggleButton>("CameraMiniToggleName");

    private static ToggleButton ChartToggle(CameraWindow window) => window.Find<ToggleButton>("CameraMiniToggleChart");

    private static CameraMiniStrip StripOf(CameraWindow window) => window.Find<CameraMiniStrip>("CameraMiniInfoStrip");

    private static Image Image(CameraWindow window) => window.GetVisualDescendants().OfType<Image>().Single(static image => image.Name == "Video");

    private static ToggleState StateFor(ToggleButton toggle) =>
        ControlAutomationPeer.CreatePeerForElement(toggle).GetProvider<IToggleProvider>()!.ToggleState;

    private static PrinterSnapshot Printing(string fileName = TemplateName, double nozzle = 214.6) =>
        Snapshot(PrinterState.Printing, Job(19.9, TimeSpan.FromMinutes(154.2), fileName), printerId: "a") with
        {
            Telemetry = new Telemetry { Nozzle = new Temperature(nozzle, 215), Bed = new Temperature(59.8, 60) },
        };

    private static void Show(CameraHarness harness, CameraWindow window, PrinterSnapshot snapshot)
    {
        harness.Monitor.Raise(snapshot with { PrinterId = "a" });
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static async Task<(CameraHarness Harness, CameraWindow Window)> OpenAsync(ICameraWindowStateStore? states = null, PrinterSnapshot? snapshot = null)
    {
        CameraHarness harness = new(states, FakeScreenCatalog.ThreeMonitors(), MiniFirst);
        // The clock is at "now" before the stream starts: moving it later would make the frame stale ("No signal").
        harness.Time.SetUtcNow(Now);
        harness.History.Configure(harness.Printers.Current.Printers!, HistoryInterval);
        (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Grey);
        Show(harness, window, snapshot ?? Printing());
        return (harness, window);
    }

    /// <summary>The window and its parts after a change of the strip (the window follows it right away).</summary>
    private static void AssertWindow(CameraWindow window, double stripHeight)
    {
        window.Settle();
        double height = 180 + stripHeight;
        Assert.Equal(new Size(320, height), window.ClientSize);
        // Grows and shrinks away from the taskbar: the bottom edge stays 16 px above it.
        Assert.Equal(new PixelPoint(2224, 1376 - (int)height), window.Position);
        // The image keeps 16:9, the strip right below it fills the rest.
        Rect strip = BoundsIn(StripOf(window), window);
        Assert.Equal(180, strip.Top, 0.01);
        if (ViewModel(window).HasFrame)
        {
            Rect image = BoundsIn(Image(window), window);
            Assert.Equal(180, image.Height, 1.0);
            Assert.Equal(image.Bottom, strip.Top, 1.0);
        }

        Assert.Equal(stripHeight, strip.Height, 0.01);
        Assert.Equal(window.ClientSize.Height, strip.Bottom, 0.01);
    }

    /// <summary>
    /// A sample every 20 s over the 70 minutes before <c>Now</c> of a printer holding its temperatures: the nozzle
    /// 215 °C ± 1.5 °C, the bed 60 °C ± 0.5 °C (T-106).
    /// </summary>
    private static void RecordSteadyHistory(CameraHarness harness)
    {
        for (DateTimeOffset time = Now - TimeSpan.FromMinutes(70); time <= Now; time += HistoryInterval)
        {
            double ago = (Now - time).TotalMinutes;
            harness.History.Record(new PrinterSnapshot
            {
                PrinterId = "a",
                State = PrinterState.Printing,
                Timestamp = time,
                Telemetry = new Telemetry
                {
                    Nozzle = new Temperature(215 + (1.5 * Math.Sin(ago / 3)), 215),
                    Bed = new Temperature(60 + (0.5 * Math.Sin(ago / 5)), 60),
                },
            });
        }
    }

    /// <summary>A sample every 20 s over the 70 minutes before <c>Now</c> (the harness clock, set on opening).</summary>
    private static void RecordHistory(CameraHarness harness)
    {
        for (DateTimeOffset time = Now - TimeSpan.FromMinutes(70); time <= Now; time += HistoryInterval)
        {
            double ago = (Now - time).TotalMinutes;
            harness.History.Record(new PrinterSnapshot
            {
                PrinterId = "a",
                State = PrinterState.Printing,
                Timestamp = time,
                Telemetry = new Telemetry
                {
                    Nozzle = new Temperature(ago > 50 ? 25 + (19 * (60 - Math.Min(ago, 60))) : 215 + (2 * Math.Sin(ago)), 215),
                    Bed = new Temperature(ago > 58 ? 25 : 60, 60),
                },
            });
        }
    }

    [Fact]
    public Task BothRowsAreCollapsedByDefaultAndEveryCombinationSizesTheWindow() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync();
            await using CameraHarness _ = harness;

            Assert.False(NameToggle(window).IsChecked);
            Assert.False(ChartToggle(window).IsChecked);
            Assert.False(window.IsShown<Border>("CameraMiniNameRow"));
            Assert.False(window.IsShown<Border>("CameraMiniChartRow"));
            AssertWindow(window, 33);

            // With the mouse: name 320 × 247, both 320 × 311, chart 320 × 277, none 320 × 213.
            window.Click(NameToggle(window));
            Assert.True(window.IsShown<Border>("CameraMiniNameRow"));
            AssertWindow(window, 67);

            window.Click(ChartToggle(window));
            Assert.True(window.IsShown<Border>("CameraMiniChartRow"));
            AssertWindow(window, 131);
            // Rows from the top: summary, name, chart.
            Assert.Equal(BoundsIn(StripOf(window), window).Top + 33, BoundsIn(window.Find<Border>("CameraMiniNameRow"), window).Top, 0.01);
            Assert.Equal(BoundsIn(StripOf(window), window).Top + 67, BoundsIn(window.Find<Border>("CameraMiniChartRow"), window).Top, 0.01);

            window.Click(NameToggle(window));
            Assert.False(window.IsShown<Border>("CameraMiniNameRow"));
            AssertWindow(window, 97);
            Assert.Equal(BoundsIn(StripOf(window), window).Top + 33, BoundsIn(window.Find<Border>("CameraMiniChartRow"), window).Top, 0.01);

            window.Click(ChartToggle(window));
            AssertWindow(window, 33);
            Assert.Equal(default, ViewModel(window).MiniStrip.Rows);
        });

    [Fact]
    public Task KeyboardTogglesWithSpaceAndEnterAndScreenReadersGetTheState() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync();
            await using CameraHarness _ = harness;
            ToggleButton name = NameToggle(window);
            ToggleButton chart = ChartToggle(window);
            Assert.Equal(ToggleState.Off, StateFor(name));

            Assert.True(name.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.Settle();

            Assert.True(name.IsChecked);
            Assert.Equal(ToggleState.On, StateFor(name));
            AssertWindow(window, 67);
            // The focus is visible (frameless window): the double frame of the toggle.
            Assert.NotNull(name.FocusAdorner);
            Assert.True(name.IsKeyboardFocusWithin);

            // Tab goes to the chart toggle, Enter toggles it.
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.Settle();
            Assert.Same(chart, window.FocusManager?.GetFocusedElement());
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.Settle();

            Assert.True(chart.IsChecked);
            Assert.Equal(ToggleState.On, StateFor(chart));
            AssertWindow(window, 131);
            // Still the mini preview: neither key reached the window.
            Assert.True(ViewModel(window).IsMini);
        });

    [Fact]
    public Task TogglesAreAboveTheGripsAndAPressNeitherDragsNorResizes() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync();
            await using CameraHarness _ = harness;
            ToggleButton name = NameToggle(window);
            Rect toggle = BoundsIn(name, window);
            // Collapsed: the toggles reach the bottom edge of the window, over the bottom grip.
            Assert.Equal(window.ClientSize.Height, toggle.Bottom, 0.01);
            Assert.Equal(320 - 6, BoundsIn(ChartToggle(window), window).Right, 0.01);

            // The bottom 6 DIP under a toggle belong to the toggle, not to the grip.
            Point bottomOfToggle = new(toggle.Center.X, window.ClientSize.Height - 2);
            Assert.Same(name, HitControl(window, bottomOfToggle)?.FindAncestorOfType<ToggleButton>(includeSelf: true));
            // The rest of the edges along the strip still resize: bottom, left, right, and the corners.
            foreach (Point edge in new Point[] { new(100, window.ClientSize.Height - 2), new(2, window.ClientSize.Height - 15), new(318, window.ClientSize.Height - 15), new(2, window.ClientSize.Height - 2), new(318, window.ClientSize.Height - 2), new(2, 181) })
            {
                Assert.True(HitControl(window, edge) is Border border && border.Classes.Contains("grip"), $"{edge}: {HitControl(window, edge)}");
            }

            // Inside the strip a press drags the window (the strip itself), as in T-089.
            Assert.Same(StripOf(window), HitControl(window, new Point(150, window.ClientSize.Height - 15))?.FindAncestorOfType<CameraMiniStrip>(includeSelf: true));

            // Clicking the bottom of the toggle toggles it; the window is not moved (no drag) and grows by the row.
            Ui.Click(window, bottomOfToggle);
            window.Settle();
            Assert.True(name.IsChecked);
            AssertWindow(window, 67);

            // A double-click on a toggle is two toggles, not "back to the normal window".
            Point centre = Ui.CentreOf(ChartToggle(window), window);
            Ui.Click(window, centre);
            Ui.Click(window, centre);
            window.Settle();
            Assert.True(ViewModel(window).IsMini);
            Assert.False(ChartToggle(window).IsChecked);
            AssertWindow(window, 67);
        });

    [Fact]
    public Task TogglesHaveTheirAutomationIdsNamesAndTooltips() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync();
            await using CameraHarness _ = harness;
            ToggleButton name = NameToggle(window);
            ToggleButton chart = ChartToggle(window);

            Assert.Equal(new Size(30, 30), name.Bounds.Size);
            Assert.Equal(new Size(30, 30), chart.Bounds.Size);
            Assert.Equal(2, BoundsIn(chart, window).Left - BoundsIn(name, window).Right, 0.01);
            Assert.Same(window.Find<StackPanel>("CameraMiniToggles"), name.GetVisualParent());
            Assert.Equal("Print name in the strip", AutomationProperties.GetName(name));
            Assert.Equal("Temperature chart in the strip", AutomationProperties.GetName(chart));
            Assert.Equal("Show print name", ToolTip.GetTip(name));
            Assert.Equal("Show temperature chart", ToolTip.GetTip(chart));

            window.Click(name);
            window.Click(chart);

            // The names stay, the state comes from the Toggle pattern; the tooltips say what a click does.
            Assert.Equal("Print name in the strip", AutomationProperties.GetName(name));
            Assert.Equal("Hide print name", ToolTip.GetTip(name));
            Assert.Equal("Hide temperature chart", ToolTip.GetTip(chart));
            Assert.Equal("CameraMiniChart", AutomationProperties.GetAutomationId(window.Find<TemperatureChart>("CameraMiniChart")));
            Assert.StartsWith("Foldable book holder v1", AutomationProperties.GetName(window.Find<Border>("CameraMiniNameRow")), StringComparison.Ordinal);
        });

    [Fact]
    public Task WithoutAJobTheNameRowDisappearsAndTheWindowShrinks() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync();
            await using CameraHarness _ = harness;
            window.Click(NameToggle(window));
            AssertWindow(window, 67);

            Show(harness, window, Snapshot(PrinterState.Idle));

            Assert.False(window.IsShown<Border>("CameraMiniNameRow"));
            Assert.True(NameToggle(window).IsChecked);
            Assert.Equal("The print name appears during a print", ToolTip.GetTip(NameToggle(window)));
            AssertWindow(window, 33);

            // The next print brings it back.
            Show(harness, window, Printing());
            Assert.True(window.IsShown<Border>("CameraMiniNameRow"));
            AssertWindow(window, 67);
        });

    [Fact]
    public Task ChartWithoutHistorySaysNoDataAndListensOnlyWhileExpanded() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync();
            await using CameraHarness _ = harness;
            TemperatureChart chart = window.Find<TemperatureChart>("CameraMiniChart");
            Assert.False(chart.IsSubscribedToHistory);

            window.Click(ChartToggle(window));

            Assert.True(chart.IsSubscribedToHistory);
            Assert.True(chart.ShowsNoData);
            Assert.Equal("No data", chart.NoDataText);
            Assert.True(chart.IsCompact);
            Assert.Equal(52, chart.Bounds.Height, 0.01);
            Assert.Equal(320 - 20 - 64 - 8, chart.Bounds.Width, 0.01);
            Assert.True(Ui.ShowsText(window, "60\u00A0min"));
            Assert.True(Ui.ShowsText(window, "215°"));
            Assert.True(Ui.ShowsText(window, "60°"));

            // With history: drawn.
            RecordHistory(harness);
            await Ui.UntilAsync(() => { window.Settle(); return !chart.ShowsNoData; }, "history drawn");

            // Collapsed, in the normal window and closed: not listening.
            window.Click(ChartToggle(window));
            Assert.False(chart.IsSubscribedToHistory);
            window.Click(ChartToggle(window));
            Assert.True(chart.IsSubscribedToHistory);
            ViewModel(window).ShowNormalCommand.Execute(null);
            window.Settle();
            Assert.False(chart.IsSubscribedToHistory);
            ViewModel(window).ShowMiniCommand.Execute(null);
            window.Settle();
            Assert.True(chart.IsSubscribedToHistory);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(chart.IsSubscribedToHistory);
        });

    [Fact]
    public Task ChartRowWithoutTemperaturesHasNoValues() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync(snapshot: PrinterSnapshot.Unavailable("a", PrinterState.Offline, Now, "unreachable"));
            await using CameraHarness _ = harness;

            window.Click(ChartToggle(window));

            Assert.True(window.Find<TemperatureChart>("CameraMiniChart").ShowsNoData);
            Assert.DoesNotContain(Ui.VisibleTexts(window.Find<Border>("CameraMiniChartRow")), static text => text.EndsWith('°'));
            AssertWindow(window, 97);
        });

    [Fact]
    public Task RowsSurviveClosingAndOpeningAgain() =>
        session.RunAsync(async scope =>
        {
            MemoryCameraWindowStateStore states = new();
            (CameraHarness harness, CameraWindow window) = await OpenAsync(states);
            await using CameraHarness _ = harness;
            window.Click(NameToggle(window));
            window.Click(ChartToggle(window));
            AssertWindow(window, 131);
            // Remembered on the toggle already.
            Assert.Equal(new CameraMiniRows(true, true), states.Get("a").MiniRows);

            window.Close();
            Dispatcher.UIThread.RunJobs();
            CameraWindow again = harness.Open();
            again.Settle();

            Assert.True(NameToggle(again).IsChecked);
            Assert.True(ChartToggle(again).IsChecked);
            Assert.True(again.IsShown<Border>("CameraMiniNameRow"));
            AssertWindow(again, 131);
        });

    [Theory]
    [InlineData(240, 0)]
    [InlineData(320, 1)]
    [InlineData(400, 2)]
    public Task NameRowShowsItsChipsByTheWindowWidth(double width, int chips) =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync();
            await using CameraHarness _ = harness;
            window.Click(NameToggle(window));
            CameraHarness.ResizeMiniWithGrip(window, width, 400);

            Border row = window.Find<Border>("CameraMiniNameRow");
            OverlayChipsPanel panel = row.GetVisualDescendants().OfType<OverlayChipsPanel>().Single();
            Assert.Equal(chips, panel.ShownCount);
            Assert.Equal(34, row.Bounds.Height);
            Assert.Equal(width * 9 / 16 + 67, window.ClientSize.Height, 0.6);
        });

    [Theory]
    [InlineData(240)]
    [InlineData(320)]
    public Task RowsFitInEveryLanguage(double width) =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync(snapshot: Printing(LongName, 1234));
            await using CameraHarness _ = harness;
            window.Click(NameToggle(window));
            window.Click(ChartToggle(window));
            CameraHarness.ResizeMiniWithGrip(window, width, 400);

            foreach (string language in AppLanguages.Supported)
            {
                Assert.True(new UiLanguage().Apply(language));
                harness.Windows.RefreshTexts();
                window.Settle();
                Assert.Equal(width * 9 / 16 + 131, window.ClientSize.Height, 0.6);
                Rect strip = BoundsIn(StripOf(window), window);
                foreach (TextBlock text in StripOf(window).GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text) && IsDrawn(text, window)))
                {
                    Rect bounds = BoundsIn(text, window);
                    string context = $"{language} {width}: '{text.Text}' {bounds}";
                    Assert.True(bounds.Left >= strip.Left + 10 - 0.01 && bounds.Right <= strip.Right - 6 + 0.01, context);
                    Assert.Single(text.TextLayout.TextLines);
                    Assert.True(text.TextLayout.WidthIncludingTrailingWhitespace <= bounds.Width + 0.5, context);
                }

                // The chart's left column keeps its 64.
                foreach (TextBlock text in window.Find<Border>("CameraMiniChartRow").GetVisualDescendants().OfType<TextBlock>().Where(static text => text.IsEffectivelyVisible))
                {
                    Assert.True(BoundsIn(text, window).Right <= strip.Left + 10 + 64 + 0.01, $"{language}: '{text.Text}'");
                }

                Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(NameToggle(window)) as string));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(ChartToggle(window))));
            }
        });

    [Fact]
    public Task SnapshotOfBothRowsExpanded() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync();
            await using CameraHarness _ = harness;
            RecordHistory(harness);
            window.Click(NameToggle(window));
            window.Click(ChartToggle(window));
            await Ui.UntilAsync(() => { window.Settle(); return !window.Find<TemperatureChart>("CameraMiniChart").ShowsNoData; }, "history drawn");
            HideWindowButtons(window);

            // Streaming: no status card ("No signal") over the image.
            Assert.False(ViewModel(window).IsStatusVisible);
            SnapshotAssert.Matches(window, "camera-mini-strip-rows");
        });

    [Fact]
    public Task ChartRowShowsAFluctuationOfSteadyTemperatures() =>
        session.RunAsync(async scope =>
        {
            // T-106: ±1.5 °C on the nozzle and ±0.5 °C on the bed are visible in the 52 px row (two bands of 24 px).
            (CameraHarness harness, CameraWindow window) = await OpenAsync();
            await using CameraHarness _ = harness;
            RecordSteadyHistory(harness);
            window.Click(ChartToggle(window));
            TemperatureChart chart = window.Find<TemperatureChart>("CameraMiniChart");
            await Ui.UntilAsync(() => { window.Settle(); return !chart.ShowsNoData; }, "history drawn");

            Rect nozzleBand = chart.ClipOf(TemperatureSeries.NozzleActual);
            Rect bedBand = chart.ClipOf(TemperatureSeries.BedActual);
            Assert.Equal(0, nozzleBand.Top);
            Assert.Equal(24, nozzleBand.Height, 6);
            Assert.Equal(28, bedBand.Top, 6);
            Assert.Equal(24, bedBand.Height, 6);
            Rect nozzle = chart.GeometryOf(TemperatureSeries.NozzleActual)!.Bounds;
            Rect bed = chart.GeometryOf(TemperatureSeries.BedActual)!.Bounds;
            Assert.True(nozzle.Height >= 10, $"The nozzle line spans {nozzle.Height:0.#} px.");
            Assert.True(bed.Height >= 3, $"The bed line spans {bed.Height:0.#} px.");
            Assert.True(nozzleBand.Contains(nozzle), $"{nozzle} outside {nozzleBand}");
            Assert.True(bedBand.Contains(bed), $"{bed} outside {bedBand}");
        });

    [Fact]
    public Task SnapshotOfTheChartRowWithSteadyTemperatures() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync();
            await using CameraHarness _ = harness;
            RecordSteadyHistory(harness);
            window.Click(ChartToggle(window));
            await Ui.UntilAsync(() => { window.Settle(); return !window.Find<TemperatureChart>("CameraMiniChart").ShowsNoData; }, "history drawn");
            HideWindowButtons(window);

            Assert.False(ViewModel(window).IsStatusVisible);
            SnapshotAssert.Matches(window, "camera-mini-strip-chart-steady");
        });

    [Fact]
    public Task SnapshotOfTheNarrowestStripWithBothRowsInGerman() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync(snapshot: Printing(LongName));
            await using CameraHarness _ = harness;
            Assert.True(new UiLanguage().Apply("de"));
            harness.Windows.RefreshTexts();
            window.Click(NameToggle(window));
            window.Click(ChartToggle(window));
            CameraHarness.ResizeMiniWithGrip(window, 240, 400);
            HideWindowButtons(window);

            // Streaming: no status card ("No signal") over the image.
            Assert.False(ViewModel(window).IsStatusVisible);
            SnapshotAssert.Matches(window, "camera-mini-strip-rows-240-de");
        });

    [Fact]
    public Task SnapshotOfTheKeyboardFocusOnAToggle() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync();
            await using CameraHarness _ = harness;
            window.Click(NameToggle(window));
            HideWindowButtons(window);
            Assert.True(ChartToggle(window).Focus(NavigationMethod.Tab));
            window.Settle();

            // SnapshotAssert drops the focus before it captures: the frame is captured here.
            Assert.True(ChartToggle(window).IsFocused);
            using Avalonia.Media.Imaging.WriteableBitmap frame = window.CaptureRenderedFrame()!;
            Rect bounds = BoundsIn(ChartToggle(window), window);
            // The white outer frame 1 inside the button (2 thick), the black inner one right inside it.
            Assert.Equal((255, 255, 255), Ui.Pixel(frame, (int)bounds.Center.X, (int)bounds.Top + 1));
            Assert.Equal((0, 0, 0), Ui.Pixel(frame, (int)bounds.Center.X, (int)bounds.Top + 3));
            Assert.Equal((255, 255, 255), Ui.Pixel(frame, (int)bounds.Center.X, (int)bounds.Bottom - 2));
        });

    /// <summary>The window buttons fade in while the pointer is over the window; hidden for stable snapshots.</summary>
    private static void HideWindowButtons(CameraWindow window)
    {
        StackPanel buttons = window.Find<StackPanel>("CameraWindowButtons");
        buttons.Transitions = null;
        window.MouseMove(new Point(-10, -10));
        window.Settle();
    }

    /// <summary>Not a chip the chips panel left out (placed beyond its clipped right edge).</summary>
    private static bool IsDrawn(TextBlock text, CameraWindow window) =>
        (text.FindAncestorOfType<OverlayChipsPanel>() is not { } panel || BoundsIn(text, window).Left < BoundsIn(panel, window).Right)
        && MiniStripParts.IsDrawnInSummary(text);

    private static Control? HitControl(CameraWindow window, Point point) => window.InputHitTest(point) as Control;
}
