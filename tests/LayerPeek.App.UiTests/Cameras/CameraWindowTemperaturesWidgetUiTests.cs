using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Diagnostics.Chart;
using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Tray;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The Temperatures widget (T-084, design T-076 2.3) in the real camera window with the production registration: S / M / L
/// with their parts, sizes and AutomationIds, the drawn text properties, the emphasis of the nozzle value, and the compact
/// <see cref="TemperatureChart"/> over the last 60 minutes of the window's history: its look, breaks, "No data", and that it
/// listens to the history only while it is shown.
/// </summary>
public sealed class CameraWindowTemperaturesWidgetUiTests(HeadlessSession session)
{
    private const uint Grey = 0xFF606060;

    private const string CardId = OverlayWidgetViewModel.AutomationIdPrefix + OverlayWidgetIds.Temperatures;

    private const string NozzleId = CardId + "-Nozzle";

    private const string BedId = CardId + "-Bed";

    private const string ChartId = CardId + "-Chart";

    private static readonly CameraWindowDefaults Normal = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };

    private static readonly TimeSpan HistoryInterval = TimeSpan.FromSeconds(20);

    /// <summary>The column of the band range labels of the L chart (T-106).</summary>
    private const double RangeColumn = 80;

    private static readonly Color Accent = Color.Parse("#FA6831");

    private static readonly Color Primary = Color.Parse("#F4F4F5");

    private static readonly Color Secondary = Color.Parse("#C9C9CE");

    private static readonly Color Tertiary = Color.Parse("#A1A1AA");

    private static readonly Color NozzleColor = Color.Parse("#FF8A3D");

    private static readonly Color BedColor = Color.Parse("#6CB4FF");

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    public static TheoryData<string, OverlayWidgetSize> LanguagesAndSizes =>
        [.. AppLanguages.Supported.SelectMany(static language => new[] { OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large }.Select(size => (language, size)))];

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static CameraOverlayLayout Layout(OverlayWidgetSize size, bool emphasized = true, bool visible = true) =>
        new([new OverlayWidgetPlacement(OverlayWidgetIds.Temperatures, OverlayAnchor.BottomRight, size, emphasized, visible)]);

    private static PrinterSnapshot Heating(double nozzle = 214.6, double? nozzleTarget = 215, double bed = 59.8, double? bedTarget = 60, bool withBed = true) => new()
    {
        PrinterId = "a",
        State = PrinterState.Printing,
        Timestamp = Now,
        Telemetry = new Telemetry
        {
            Nozzle = new Temperature(nozzle, nozzleTarget),
            Bed = withBed ? new Temperature(bed, bedTarget) : null,
        },
    };

    private static CameraHarness Harness(CameraOverlayLayout layout)
    {
        CameraHarness harness = new(window: Normal, overlay: layout, widgets: OverlayWidgets.CreateRegistry());
        harness.Time.SetUtcNow(Now);
        harness.History.Configure(harness.Printers.Current.Printers!, HistoryInterval);
        return harness;
    }

    /// <summary>
    /// A sample every 20 s over the last 2 h before <see cref="TestPrinters.Now"/>: the nozzle heats up an hour ago and holds
    /// 215 °C, the bed 60 °C; without samples between <paramref name="gapFrom"/> and <paramref name="gapTo"/> minutes ago.
    /// </summary>
    private static void RecordHistory(CameraHarness harness, string printerId = "a", int gapFrom = 0, int gapTo = 0)
    {
        DateTimeOffset start = Now - TimeSpan.FromMinutes(120);
        for (DateTimeOffset time = start; time <= Now; time += HistoryInterval)
        {
            double ago = (Now - time).TotalMinutes;
            if (ago < gapFrom && ago > gapTo)
            {
                continue;
            }

            double nozzle = ago > 60 ? 25 : ago > 50 ? 25 + (19 * (60 - ago)) : 215 + (2 * Math.Sin(ago));
            harness.History.Record(new PrinterSnapshot
            {
                PrinterId = printerId,
                State = PrinterState.Printing,
                Timestamp = time,
                Telemetry = new Telemetry { Nozzle = new Temperature(nozzle, ago > 60 ? 0 : 215), Bed = new Temperature(ago > 58 ? 25 : 60, ago > 60 ? 0 : 60) },
            });
        }
    }

    /// <summary>
    /// A sample every 20 s over the 70 minutes before <see cref="TestPrinters.Now"/> of a printer holding its
    /// temperatures: the nozzle 215 °C ± 1.5 °C, the bed 60 °C ± 0.5 °C (without a bed when <paramref name="withBed"/> is false).
    /// </summary>
    private static void RecordSteadyHistory(CameraHarness harness, bool withBed = true)
    {
        for (DateTimeOffset time = Now - TimeSpan.FromMinutes(70); time <= Now; time += HistoryInterval)
        {
            double ago = (Now - time).TotalMinutes;
            harness.History.Record(Heating(nozzle: 215 + (1.5 * Math.Sin(ago / 3)), bed: 60 + (0.5 * Math.Sin(ago / 5)), withBed: withBed) with { Timestamp = time });
        }
    }

    private static async Task<CameraWindow> OpenAsync(CameraHarness harness, PrinterSnapshot snapshot, double width = 960, double height = 540)
    {
        (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Grey);
        window.Width = width;
        window.Height = height;
        harness.Monitor.Raise(snapshot);
        await Ui.UntilAsync(() => Widget(window).IsShown, "Temperatures card shown");
        window.Settle();
        return window;
    }

    /// <summary>What saving the settings does for an open window: the monitoring session applies the new layout.</summary>
    private static void Save(CameraHarness harness, CameraOverlayLayout layout)
    {
        ((IMonitoringObserver)harness.Printers).ApplySettings(new AppSettings
        {
            Printers = [CameraHarness.CameraPrinter("a", "Alpha", Normal, layout), CameraHarness.CameraPrinter("b", "Beta")],
        });
        Dispatcher.UIThread.RunJobs();
        foreach (CameraWindow window in harness.Windows.OpenWindows.Values)
        {
            window.Settle();
        }
    }

    private static TemperaturesWidgetViewModel Widget(CameraWindow window) =>
        (TemperaturesWidgetViewModel)ViewModel(window).Overlay.Widgets.Single(static widget => widget.Id == OverlayWidgetIds.Temperatures);

    private static Border Card(CameraWindow window) => window.Find<Border>(CardId);

    private static TemperatureChart Chart(Visual root) => root.GetVisualDescendants().OfType<TemperatureChart>().Single();

    private static Rect BoundsInWindow(Visual control, CameraWindow window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    /// <summary>The drawn text blocks of a part, left to right.</summary>
    private static TextBlock[] Texts(Control part) =>
        [.. part.GetVisualDescendants().OfType<TextBlock>().Where(static block => block.IsEffectivelyVisible)];

    private static Color ColorOf(IBrush? brush) => Assert.IsType<ISolidColorBrush>(brush, exactMatch: false).Color;

    private static Color PenColor(TemperatureChart chart, TemperatureSeries series) => ColorOf(chart.SeriesPenOf(series)!.Brush);

    /// <summary>No text of the card is cut and the card has its width (T-076 2.3) and stays in the window.</summary>
    private static void AssertFits(CameraWindow window, OverlayWidgetSize size)
    {
        Border card = Card(window);
        Rect bounds = BoundsInWindow(card, window);
        switch (size)
        {
            case OverlayWidgetSize.Small:
                Assert.InRange(bounds.Width, 100, 300);
                break;
            case OverlayWidgetSize.Medium:
                Assert.Equal(284, bounds.Width, 0.5);
                break;
            default:
                Assert.Equal(392, bounds.Width, 0.5);
                break;
        }

        Assert.True(new Rect(window.ClientSize).Contains(bounds), $"card {bounds} outside {window.ClientSize}");
        foreach (TextBlock block in card.GetVisualDescendants().OfType<TextBlock>().Where(static block => block.IsEffectivelyVisible && !string.IsNullOrEmpty(block.Text)))
        {
            Assert.All(block.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, $"'{block.Text}' is trimmed ({bounds.Width} wide)"));
            Assert.Single(block.TextLayout.TextLines);
            Rect text = BoundsInWindow(block, window);
            Assert.True(bounds.Contains(text), $"'{block.Text}' {text} outside the card {bounds}");
            Assert.True(block.DesiredSize.Width - block.Margin.Left - block.Margin.Right <= block.Bounds.Width + 0.5, $"'{block.Text}' needs {block.DesiredSize.Width}, has {block.Bounds.Width}");
        }
    }

    // ---- Sizes and parts ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small, 36)]
    [InlineData(OverlayWidgetSize.Medium, 134)]
    [InlineData(OverlayWidgetSize.Large, 216)]
    public Task CardShowsThePartsOfItsSize(OverlayWidgetSize size, double height) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(size));
            RecordHistory(harness);
            CameraWindow window = await OpenAsync(harness, Heating());

            Assert.Equal(size, Widget(window).RenderedSize);
            Assert.Equal(height, Card(window).Bounds.Height, 0.5);
            AssertFits(window, size);
            bool chart = size != OverlayWidgetSize.Small;
            Assert.True(window.IsShown<Control>(NozzleId));
            Assert.True(window.IsShown<Control>(BedId));
            Assert.Equal(chart, window.IsShown<TemperatureChart>(ChartId));
            Assert.Equal(chart, Ui.ShowsText(window, "−60\u00A0min"));
            Assert.Equal(chart, Ui.ShowsText(window, "now"));
            // The words only in L (T-076 2.3), the icons stand for them in S and M.
            Assert.Equal(size == OverlayWidgetSize.Large, Ui.ShowsText(window, "Nozzle"));
            Assert.Equal(size == OverlayWidgetSize.Large, Ui.ShowsText(window, "Bed"));
            string name = chart
                ? "Nozzle 215 / 215 °C, Bed 60 / 60 °C, chart of the last 60 minutes"
                : "Nozzle 215 / 215 °C, Bed 60 / 60 °C";
            Assert.Equal(name, Card(window).GetValue(AutomationProperties.NameProperty));

            // The drawn values: S 13, M 14, L 16, semi-bold (T-076 2.3); the emphasized nozzle value at least 15 bold
            // (T-076 1.3: the accent has only 2.7 : 1 over white video).
            double valueSize = size switch { OverlayWidgetSize.Small => 13, OverlayWidgetSize.Medium => 14, _ => 16 };
            TextBlock[] nozzle = Texts(window.Find<Control>(NozzleId));
            TextBlock[] bed = Texts(window.Find<Control>(BedId));
            TextBlock[] values = size == OverlayWidgetSize.Large ? [.. nozzle[1..], .. bed[1..]] : [.. nozzle, .. bed];
            Assert.Equal(["215", " / 215 °C", "60 / 60 °C"], values.Select(static block => block.Text));
            Assert.Equal(Math.Max(15, valueSize), values[0].FontSize);
            Assert.Equal(FontWeight.Bold, values[0].FontWeight);
            Assert.All(values[1..], block =>
            {
                Assert.Equal(valueSize, block.FontSize);
                Assert.Equal(FontWeight.SemiBold, block.FontWeight);
            });
            if (size == OverlayWidgetSize.Large)
            {
                // The words: 16 regular, secondary; the values start in one column.
                foreach (TextBlock word in new[] { nozzle[0], bed[0] })
                {
                    Assert.Equal(16, word.FontSize);
                    Assert.Equal(FontWeight.Normal, word.FontWeight);
                    Assert.Equal(Secondary, ColorOf(word.Foreground));
                }

                Assert.Equal(BoundsInWindow(nozzle[1], window).X, BoundsInWindow(bed[1], window).X, 0.5);
            }

            if (chart)
            {
                TemperatureChart sparkline = window.Find<TemperatureChart>(ChartId);
                Assert.Equal(size == OverlayWidgetSize.Medium ? new Size(256, 64) : new Size(360, 110), sparkline.Bounds.Size);
                TextBlock start = Ui.Find<TextBlock>(Card(window), static block => block.Text == "−60\u00A0min");
                TextBlock now = Ui.Find<TextBlock>(Card(window), static block => block.Text == "now");
                foreach (TextBlock axis in new[] { start, now })
                {
                    Assert.Equal(size == OverlayWidgetSize.Medium ? 11 : 12, axis.FontSize);
                    Assert.Equal(FontWeight.Normal, axis.FontWeight);
                    Assert.Equal(Tertiary, ColorOf(axis.Foreground));
                }

                // "−60 min" at the left edge of the plot, "now" at its right edge (in L left of the range labels, T-106).
                Rect plot = BoundsInWindow(sparkline, window);
                Assert.Equal(plot.Left, BoundsInWindow(start, window).Left, 0.5);
                Assert.Equal(plot.Right - sparkline.BandRangeWidth, BoundsInWindow(now, window).Right, 0.5);
                Assert.Equal(size == OverlayWidgetSize.Large ? RangeColumn : 0, sparkline.BandRangeWidth);
            }
        });

    [Fact]
    public Task IconsHaveTheColoursOfTheChartLines() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Heating());

            OverlayIcon nozzle = window.Find<Control>(NozzleId).GetVisualDescendants().OfType<OverlayIcon>().Single();
            OverlayIcon bed = window.Find<Control>(BedId).GetVisualDescendants().OfType<OverlayIcon>().Single();
            Assert.Same(window.FindResource("IconCone16Filled"), nozzle.Data);
            Assert.Same(window.FindResource("IconLineHorizontal1_16Filled"), bed.Data);
            Assert.Equal(NozzleColor, ColorOf(nozzle.Foreground));
            Assert.Equal(BedColor, ColorOf(bed.Foreground));
            // The cone points down, like a nozzle.
            Assert.Equal(180, Assert.IsType<RotateTransform>(nozzle.RenderTransform).Angle);
        });

    [Fact]
    public Task LargeCardUsesTheLargeBedIcon() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Large));
            CameraWindow window = await OpenAsync(harness, Heating());

            OverlayIcon bed = window.Find<Control>(BedId).GetVisualDescendants().OfType<OverlayIcon>().Single();
            OverlayIcon nozzle = window.Find<Control>(NozzleId).GetVisualDescendants().OfType<OverlayIcon>().Single();
            Assert.Same(window.FindResource("IconLineHorizontal1_20Filled"), bed.Data);
            Assert.Equal(new Size(20, 20), bed.Bounds.Size);
            // The cone exists only at 16 and is drawn larger.
            Assert.Equal(new Size(20, 20), nozzle.Bounds.Size);
        });

    [Fact]
    public Task MissingBedLeavesOnlyTheNozzle() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Heating(withBed: false));

            Assert.True(window.IsShown<Control>(NozzleId));
            Assert.False(window.IsShown<Control>(BedId));
            Assert.Equal("Nozzle 215 / 215 °C, chart of the last 60 minutes", Card(window).GetValue(AutomationProperties.NameProperty));
        });

    [Fact]
    public Task HeaterOffShowsOnlyTheMeasuredTemperature() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Small));
            CameraWindow window = await OpenAsync(harness, Heating(27.6, 0, 24.2, 0));

            Assert.Equal(["28", " °C"], Texts(window.Find<Control>(NozzleId)).Select(static block => block.Text));
            Assert.Equal(["24 °C"], Texts(window.Find<Control>(BedId)).Select(static block => block.Text));
        });

    [Fact]
    public Task CardDisappearsWhenThePrinterGoesOffline() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Heating());

            harness.Monitor.Raise(PrinterSnapshot.Unavailable("a", PrinterState.Offline, Now, "unreachable"));
            await Ui.UntilAsync(() => !Widget(window).IsShown, "Temperatures card hidden");
            window.Settle();

            Assert.False(Card(window).IsEffectivelyVisible);
        });

    // ---- Look ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small, 13, 15, 36)]
    [InlineData(OverlayWidgetSize.Medium, 14, 15, 134)]
    [InlineData(OverlayWidgetSize.Large, 16, 16, 216)]
    public Task EmphasisDrawsOnlyTheNozzleValueInTheAccentColour(OverlayWidgetSize size, double plainSize, double emphasizedSize, double height) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(size, emphasized: true));
            CameraWindow window = await OpenAsync(harness, Heating());
            // The last two texts of the nozzle row are its value (L starts with the word "Nozzle").
            TextBlock[] nozzle = Texts(window.Find<Control>(NozzleId))[^2..];
            TextBlock bed = Texts(window.Find<Control>(BedId))[^1];

            Assert.Equal(Accent, ColorOf(nozzle[0].Foreground));
            // Only "215" of the nozzle (T-076 2.3), not "/ 215 °C" and not the bed.
            Assert.Equal(Primary, ColorOf(nozzle[1].Foreground));
            Assert.Equal(Primary, ColorOf(bed.Foreground));
            // The accent reaches only 2.7 : 1 over white video, so the emphasized value is at least 15 bold (T-076 1.3),
            // within the card's designed height and width.
            Assert.Equal(emphasizedSize, nozzle[0].FontSize);
            Assert.Equal(FontWeight.Bold, nozzle[0].FontWeight);
            Assert.Equal(plainSize, nozzle[1].FontSize);
            Assert.Equal(FontWeight.SemiBold, nozzle[1].FontWeight);
            Assert.Equal(height, Card(window).Bounds.Height, 0.5);
            AssertFits(window, size);

            Save(harness, Layout(size, emphasized: false));

            TextBlock plain = Texts(window.Find<Control>(NozzleId))[^2];
            Assert.Equal(Primary, ColorOf(plain.Foreground));
            Assert.Equal(plainSize, plain.FontSize);
            Assert.Equal(FontWeight.SemiBold, plain.FontWeight);
            Assert.Equal(height, Card(window).Bounds.Height, 0.5);
        });

    [Fact]
    public Task ValuesUseTabularFigures() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Heating(111, 111, 11, 11));

            Assert.All(Texts(window.Find<Control>(NozzleId)).Concat(Texts(window.Find<Control>(BedId))), static block =>
                Assert.Contains(block.FontFeatures!, static feature => feature.Tag == "tnum" && feature.Value == 1));
        });

    // ---- Sparkline ----

    [Theory]
    [InlineData(OverlayWidgetSize.Medium, 1.5)]
    [InlineData(OverlayWidgetSize.Large, 2)]
    public Task SparklineIsTheCompactChart(OverlayWidgetSize size, double thickness) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(size));
            RecordHistory(harness);
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);

            Assert.True(chart.IsCompact);
            Assert.False(chart.HasAxes);
            Assert.False(chart.ShowsNoData);
            Assert.Same(harness.History, chart.History);
            Assert.Equal("a", chart.PrinterId);
            // Lines in the overlay colours (T-076 2.3): actual 1.5 (L 2), targets dashed 1 with 70 % alpha.
            Assert.Equal(NozzleColor, PenColor(chart, TemperatureSeries.NozzleActual));
            Assert.Equal(BedColor, PenColor(chart, TemperatureSeries.BedActual));
            Assert.Equal(thickness, chart.SeriesPenOf(TemperatureSeries.NozzleActual)!.Thickness);
            Assert.Null(chart.SeriesPenOf(TemperatureSeries.NozzleActual)!.DashStyle);
            foreach (TemperatureSeries target in new[] { TemperatureSeries.NozzleTarget, TemperatureSeries.BedTarget })
            {
                IPen pen = chart.SeriesPenOf(target)!;
                Assert.Equal(1, pen.Thickness);
                Assert.Equal([4, 3], pen.DashStyle!.Dashes!);
                Assert.Equal(0.7, ((ISolidColorBrush)pen.Brush!).Opacity, 3);
            }

            Assert.Equal(NozzleColor, PenColor(chart, TemperatureSeries.NozzleTarget));
        });

    [Fact]
    public Task SparklineDrawsTheLastSixtyMinutesOfTheHistory() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            RecordHistory(harness);
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);

            Assert.Equal(Now, chart.Data.To);
            Assert.Equal(TimeSpan.FromMinutes(60), chart.Data.To - chart.Data.From);
            Assert.Equal(harness.History.CountSamples("a", Now.AddMinutes(-60), Now), chart.Data.SampleCount);
            Assert.Equal(256, chart.Data.Columns);
            // One column per pixel: 181 samples on 256 columns, the nozzle line has no break.
            ReadOnlySpan<ChartColumn> nozzle = chart.Data.GetColumns(TemperatureSeries.NozzleActual);
            Assert.Equal(181, nozzle.Length);
            Assert.Equal(1, Count(nozzle, static column => column.StartsLine));
            // Targets only while heating: the nozzle heats from 60 minutes ago.
            Assert.NotEmpty(chart.Data.GetColumns(TemperatureSeries.NozzleTarget).ToArray());
            // The buffer holds the hour, not the 24 h capacity of the history.
            Assert.True(chart.Data.SampleBufferLength < harness.History.GetCapacity("a"));
        });

    [Fact]
    public Task SparklineBreaksWhereDataIsMissing() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            // No samples between 30 and 20 minutes ago (offline, computer asleep).
            RecordHistory(harness, gapFrom: 30, gapTo: 20);
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);

            Assert.Equal(2, Count(chart.Data.GetColumns(TemperatureSeries.NozzleActual), static column => column.StartsLine));
            Assert.Equal(2, Count(chart.Data.GetColumns(TemperatureSeries.BedActual), static column => column.StartsLine));
        });

    [Fact]
    public Task SparklineWithoutHistorySaysNoData() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);

            Assert.True(chart.ShowsNoData);
            Assert.Equal("No data", chart.NoDataText);
            Assert.Equal(Tertiary, ColorOf(chart.AxisBrush));
            // The axis labels stay (T-076 2.3).
            Assert.True(Ui.ShowsText(window, "−60\u00A0min"));
            Assert.True(Ui.ShowsText(window, "now"));

            // The first sample comes in: the chart draws it.
            harness.History.Record(Heating() with { Timestamp = Now });
            await Ui.UntilAsync(() => { window.Settle(); return !chart.ShowsNoData; }, "sample drawn");
        });

    [Fact]
    public Task SparklineRedrawsOnlyForTheHistoryOfItsPrinter() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            RecordHistory(harness);
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);
            long version = chart.Data.Version;
            int rebuilds = chart.SeriesRebuilds;

            // Another printer's sample: nothing is redrawn.
            harness.History.Record(Heating() with { PrinterId = "b", Timestamp = Now.AddSeconds(20) });
            window.Settle();
            window.Settle();
            Assert.Equal(version, chart.Data.Version);
            Assert.Equal(rebuilds, chart.SeriesRebuilds);

            // A redraw without new data (e.g. the card moved) reuses the lines.
            chart.InvalidateVisual();
            window.Settle();
            Assert.Equal(rebuilds, chart.SeriesRebuilds);

            // A sample of its printer: the window slides to it, the lines are rebuilt once.
            harness.Time.Advance(TimeSpan.FromSeconds(20));
            harness.History.Record(Heating() with { Timestamp = Now.AddSeconds(20) });
            window.Settle();
            window.Settle();
            Assert.Equal(harness.History.GetVersion("a"), chart.Data.Version);
            Assert.Equal(Now.AddSeconds(20), chart.Data.To);
            Assert.Equal(rebuilds + 1, chart.SeriesRebuilds);
        });

    // ---- Listening to the history only while shown ----

    [Fact]
    public Task ChartStopsListeningWhenTheWindowCloses() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);
            Assert.True(chart.IsSubscribedToHistory);

            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.False(chart.IsSubscribedToHistory);
        });

    [Fact]
    public Task ChartStopsListeningWhileTheCardIsHidden() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);
            Assert.True(chart.IsSubscribedToHistory);

            // No temperatures: the card hides.
            harness.Monitor.Raise(PrinterSnapshot.Unavailable("a", PrinterState.Offline, Now, "unreachable"));
            await Ui.UntilAsync(() => !Widget(window).IsShown, "Temperatures card hidden");
            window.Settle();
            Assert.False(chart.IsSubscribedToHistory);

            // Back online: listening again, with the latest history.
            RecordHistory(harness);
            harness.Monitor.Raise(Heating());
            await Ui.UntilAsync(() => Widget(window).IsShown, "Temperatures card shown");
            window.Settle();
            Assert.True(chart.IsSubscribedToHistory);
            Assert.False(chart.ShowsNoData);
        });

    [Fact]
    public Task ChartStopsListeningWhenTheLayoutHidesTheWidget() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);
            Assert.True(chart.IsSubscribedToHistory);

            Save(harness, Layout(OverlayWidgetSize.Medium, visible: false));

            Assert.False(chart.IsSubscribedToHistory);
            Assert.Empty(window.GetVisualDescendants().OfType<TemperatureChart>());
        });

    [Fact]
    public Task SmallCardHasNoChartListening() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);

            Save(harness, Layout(OverlayWidgetSize.Small));

            Assert.False(chart.IsEffectivelyVisible);
            Assert.False(chart.IsSubscribedToHistory);
        });

    [Fact]
    public Task MiniPreviewHasNoChartListening() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);

            ViewModel(window).ShowMiniCommand.Execute(null);
            window.Settle();
            Assert.False(chart.IsSubscribedToHistory);

            ViewModel(window).ShowNormalCommand.Execute(null);
            window.Settle();
            Assert.True(chart.IsSubscribedToHistory);
        });

    // ---- Languages ----

    /// <summary>The longest texts: thousands, the words of L and the axis labels, in every language and size.</summary>
    [Theory]
    [MemberData(nameof(LanguagesAndSizes))]
    public Task LongestTextsFitInEveryLanguageAndSize(string language, OverlayWidgetSize size) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            await using CameraHarness harness = Harness(Layout(size));
            CameraWindow window = await OpenAsync(harness, Heating(1215, 1200, 120, 120));

            AssertFits(window, size);
            if (size != OverlayWidgetSize.Small)
            {
                Assert.True(Ui.ShowsText(window, Strings.CameraOverlayChartNow));
                Assert.Equal(Strings.CameraOverlayChartNoData, window.Find<TemperatureChart>(ChartId).NoDataText);
            }
        });

    [Theory]
    [MemberData(nameof(Languages))]
    public Task CardFollowsALanguageSwitch(string language) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Large));
            CameraWindow window = await OpenAsync(harness, Heating());

            Assert.True(new UiLanguage().Apply(language));
            ViewModel(window).RefreshTexts();
            window.Settle();

            Assert.True(Ui.ShowsText(window, Strings.DiagnosticsNozzle), Strings.DiagnosticsNozzle);
            Assert.True(Ui.ShowsText(window, Strings.CameraOverlayChartNow), Strings.CameraOverlayChartNow);
            Assert.Equal(Strings.CameraOverlayChartNoData, window.Find<TemperatureChart>(ChartId).NoDataText);
            Assert.EndsWith(Strings.CameraOverlayChartName, (string?)Card(window).GetValue(AutomationProperties.NameProperty), StringComparison.Ordinal);
            AssertFits(window, OverlayWidgetSize.Large);
        });

    // ---- Bands of the compact chart (T-106) ----

    [Fact]
    public Task SparklineGivesEachSeriesItsOwnBandWhereAFluctuationIsVisible() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            RecordSteadyHistory(harness);
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);

            // Nozzle on top, bed below, 4 px apart, the 64 px split evenly.
            Rect nozzleBand = chart.ClipOf(TemperatureSeries.NozzleActual);
            Rect bedBand = chart.ClipOf(TemperatureSeries.BedActual);
            Assert.Equal(new Rect(0, 0, 256, 30), nozzleBand);
            Assert.Equal(new Rect(0, 34, 256, 30), bedBand);
            Assert.Equal(nozzleBand, chart.ClipOf(TemperatureSeries.NozzleTarget));
            Assert.Equal(bedBand, chart.ClipOf(TemperatureSeries.BedTarget));

            // ±1.5 °C around 215 and ±0.5 °C around 60 fill a good part of their bands (on the axis from 0 °C they were
            // under 1 px), each line inside its own band.
            Rect nozzle = chart.GeometryOf(TemperatureSeries.NozzleActual)!.Bounds;
            Rect bed = chart.GeometryOf(TemperatureSeries.BedActual)!.Bounds;
            Assert.True(nozzle.Height >= 12, $"The nozzle line spans {nozzle.Height:0.#} px.");
            Assert.True(bed.Height >= 4, $"The bed line spans {bed.Height:0.#} px.");
            Assert.True(nozzleBand.Contains(nozzle), $"{nozzle} outside {nozzleBand}");
            Assert.True(bedBand.Contains(bed), $"{bed} outside {bedBand}");
            // The range is the minimum span centred on the data.
            ValueBand nozzleScale = chart.BandOf(TemperatureSeries.NozzleActual)!.Value;
            Assert.Equal(CompactBands.MinimumSpan, nozzleScale.High - nozzleScale.Low, 3);
            Assert.Equal(215, (nozzleScale.High + nozzleScale.Low) / 2, 1);
            // The target (215) is the dashed line in the middle of the nozzle band.
            Assert.Equal(nozzleScale.ToY(215), chart.GeometryOf(TemperatureSeries.NozzleTarget)!.Bounds.Center.Y, 1);
        });

    [Fact]
    public Task HeatingRampScalesTheBandToTheWholeRange() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            RecordHistory(harness);
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);

            ValueBand nozzle = chart.BandOf(TemperatureSeries.NozzleActual)!.Value;
            Assert.Equal(chart.Data.GetRange(TemperatureSeries.NozzleActual).Min, nozzle.Low, 3);
            Assert.Equal(chart.Data.GetRange(TemperatureSeries.NozzleActual).Max, nozzle.High, 3);
            Assert.True(nozzle.High - nozzle.Low > 150);
            ValueBand bed = chart.BandOf(TemperatureSeries.BedActual)!.Value;
            Assert.Equal(25, bed.Low, 3);
            Assert.Equal(60, bed.High, 3);
        });

    [Fact]
    public Task OnlySeriesWithValuesGetABand() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            RecordSteadyHistory(harness, withBed: false);
            CameraWindow window = await OpenAsync(harness, Heating(withBed: false));
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);

            // Without a bed the nozzle has the whole height.
            Assert.Equal(new Rect(0, 0, 256, 64), chart.ClipOf(TemperatureSeries.NozzleActual));
            Assert.Null(chart.BandOf(TemperatureSeries.BedActual));
            Assert.Null(chart.GeometryOf(TemperatureSeries.BedActual));
            Assert.False(chart.ShowsNoData);
        });

    [Fact]
    public Task BandsStayPutWhileTheSameSeriesHaveValues() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            RecordSteadyHistory(harness);
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);
            Rect nozzleBand = chart.ClipOf(TemperatureSeries.NozzleActual);
            Rect bedBand = chart.ClipOf(TemperatureSeries.BedActual);
            int rebuilds = chart.SeriesRebuilds;

            // A new sample without a bed value: the bed still has values in the hour, the layout does not change.
            harness.Time.Advance(TimeSpan.FromSeconds(20));
            harness.History.Record(Heating(nozzle: 215.4, withBed: false) with { Timestamp = Now.AddSeconds(20) });
            window.Settle();
            window.Settle();
            Assert.Equal(rebuilds + 1, chart.SeriesRebuilds);

            Assert.Equal(nozzleBand, chart.ClipOf(TemperatureSeries.NozzleActual));
            Assert.Equal(bedBand, chart.ClipOf(TemperatureSeries.BedActual));
        });

    [Fact]
    public Task TargetFarFromTheActualValuesIsClippedToTheBand() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            // Heating has just been switched on: the nozzle is still at 25 °C, the target 215 °C.
            for (DateTimeOffset time = Now - TimeSpan.FromMinutes(5); time <= Now; time += HistoryInterval)
            {
                harness.History.Record(Heating(nozzle: 25, nozzleTarget: 215, bed: 25, bedTarget: 0) with { Timestamp = time });
            }

            CameraWindow window = await OpenAsync(harness, Heating(nozzle: 25, bed: 25, bedTarget: 0));
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);

            // The band follows the actual values; the target lies above it and is cut away by the band's clip.
            ValueBand nozzle = chart.BandOf(TemperatureSeries.NozzleActual)!.Value;
            Assert.Equal(25, (nozzle.Low + nozzle.High) / 2, 3);
            Rect target = chart.GeometryOf(TemperatureSeries.NozzleTarget)!.Bounds;
            Assert.True(target.Bottom < chart.ClipOf(TemperatureSeries.NozzleTarget).Top, $"{target} should lie above the band.");
        });

        [Theory]
    [InlineData(OverlayWidgetSize.Small)]
    [InlineData(OverlayWidgetSize.Medium)]
    [InlineData(OverlayWidgetSize.Large)]
    public Task BandRangesAreShownOnlyInTheLargeCard(OverlayWidgetSize size) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(size));
            RecordSteadyHistory(harness);
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);
            window.Settle();

            bool large = size == OverlayWidgetSize.Large;
            foreach (TemperatureSeries series in Enum.GetValues<TemperatureSeries>())
            {
                bool expected = large && series is TemperatureSeries.NozzleActual or TemperatureSeries.BedActual;
                Assert.Equal(expected, chart.BandRangeTextOf(series) is not null);
            }

            if (!large)
            {
                return;
            }

            // The whole degrees of each band's range: the minimum span of 6 °C around 215 and 60.
            Assert.Equal("212–218\u00A0°C", chart.BandRangeTextOf(TemperatureSeries.NozzleActual));
            Assert.Equal("57–63\u00A0°C", chart.BandRangeTextOf(TemperatureSeries.BedActual));
            Assert.Equal(new Size(360 - RangeColumn, 110), new Size(chart.Data.Columns, chart.Bounds.Height));
            foreach (TemperatureSeries series in new[] { TemperatureSeries.NozzleActual, TemperatureSeries.BedActual })
            {
                ValueBand band = chart.BandOf(series)!.Value;
                Assert.Equal($"{Math.Round(band.Low, MidpointRounding.AwayFromZero)}–{Math.Round(band.High, MidpointRounding.AwayFromZero)}\u00A0°C", chart.BandRangeTextOf(series));
                // Secondary colour, 12 like the axis labels of L.
                Assert.Equal(Secondary, ColorOf(chart.BandRangeBrush));
                Assert.Equal(12, chart.BandRangeFontSize);
                // In the column right of the plot, centred on its band, never over a line.
                Rect bounds = chart.BandRangeBoundsOf(series);
                Rect slot = chart.ClipOf(series);
                Assert.True(bounds.Left >= chart.Bounds.Width - RangeColumn + 8, $"{bounds} reaches into the plot.");
                Assert.Equal(chart.Bounds.Width, bounds.Right, 0.5);
                Assert.Equal(slot.Center.Y, bounds.Center.Y, 0.5);
            }
        });

    [Fact]
    public Task BandRangeOfAHeatingRampShowsTheWholeRange() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Large));
            RecordHistory(harness);
            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);

            ValueBand nozzle = chart.BandOf(TemperatureSeries.NozzleActual)!.Value;
            Assert.Equal(
                $"{Math.Round(nozzle.Low, MidpointRounding.AwayFromZero)}–{Math.Round(nozzle.High, MidpointRounding.AwayFromZero)}\u00A0°C",
                chart.BandRangeTextOf(TemperatureSeries.NozzleActual));
            Assert.Equal("25–60\u00A0°C", chart.BandRangeTextOf(TemperatureSeries.BedActual));
        });

    [Theory]
    [MemberData(nameof(Languages))]
    public Task WidestBandRangeFitsItsColumnInEveryLanguage(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Large));
            // Three-digit bounds with the widest digits on both bands (a heating ramp from 100 to 888 °C is not real, but
            // it is the widest text the format can produce).
            for (DateTimeOffset time = Now - TimeSpan.FromMinutes(50); time <= Now; time += HistoryInterval)
            {
                double ago = (Now - time).TotalMinutes;
                double value = ago > 25 ? 800 : 888;
                harness.History.Record(Heating(nozzle: value, bed: value) with { Timestamp = time });
            }

            CameraWindow window = await OpenAsync(harness, Heating());
            TemperatureChart chart = window.Find<TemperatureChart>(ChartId);
            Assert.Equal(Strings.CameraOverlayChartRange, chart.BandRangeFormat);

            foreach (TemperatureSeries series in new[] { TemperatureSeries.NozzleActual, TemperatureSeries.BedActual })
            {
                Assert.Equal("800–888\u00A0°C", chart.BandRangeTextOf(series));
                Rect bounds = chart.BandRangeBoundsOf(series);
                Assert.True(bounds.Width <= RangeColumn - 8, $"{language}: the label is {bounds.Width:0.#} px wide.");
                Assert.True(new Rect(chart.Bounds.Size).Contains(bounds), $"{language}: {bounds} is cut off.");
            }

            AssertFits(window, OverlayWidgetSize.Large);
        });

    // ---- Snapshots ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small, "s")]
    [InlineData(OverlayWidgetSize.Medium, "m")]
    [InlineData(OverlayWidgetSize.Large, "l")]
    public Task SnapshotOfTheCard(OverlayWidgetSize size, string suffix) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(size));
            RecordHistory(harness, gapFrom: 30, gapTo: 25);
            CameraWindow window = await OpenAsync(harness, Heating(), 720, 405);

            SnapshotAssert.Matches(window, "camera-widget-temperatures-" + suffix);
        });

    [Theory]
    [InlineData(OverlayWidgetSize.Medium, "m")]
    [InlineData(OverlayWidgetSize.Large, "l")]
    public Task SnapshotOfSteadyTemperatures(OverlayWidgetSize size, string suffix) =>
        session.RunAsync(async scope =>
        {
            // T-106: a fluctuation of ±1.5 °C (nozzle) and ±0.5 °C (bed) is visible in the bands; L shows their ranges.
            await using CameraHarness harness = Harness(Layout(size));
            RecordSteadyHistory(harness);
            CameraWindow window = await OpenAsync(harness, Heating(), 720, 405);

            SnapshotAssert.Matches(window, "camera-widget-temperatures-steady-" + suffix);
        });

    [Fact]
    public Task SnapshotWithoutHistory() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Heating(), 720, 405);

            SnapshotAssert.Matches(window, "camera-widget-temperatures-no-data");
        });

    private static int Count(ReadOnlySpan<ChartColumn> columns, Func<ChartColumn, bool> predicate)
    {
        int count = 0;
        foreach (ChartColumn column in columns)
        {
            count += predicate(column) ? 1 : 0;
        }

        return count;
    }
}
