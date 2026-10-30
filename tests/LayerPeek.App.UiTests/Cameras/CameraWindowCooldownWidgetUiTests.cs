using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Layout;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Tray;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Tests.Shared;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The Cool-down widget (T-088, design T-076 2.5) in the real camera window with the production registration and the
/// real cool-down monitor on the window's fake clock: "Cooling down…" and "Cooled down" in S / M / L with their parts,
/// drawn text properties, icons, the green outline, the emphasis, AutomationIds, the fit in every language, the layout
/// mode card and the reference snapshots.
/// </summary>
public sealed class CameraWindowCooldownWidgetUiTests(HeadlessSession session)
{
    private const uint Grey = 0xFF606060;

    private const string CardId = OverlayWidgetViewModel.AutomationIdPrefix + OverlayWidgetIds.Cooldown;

    private const string TitleId = CardId + "-Title";

    private const string NozzleId = CardId + "-Nozzle";

    private const string BedId = CardId + "-Bed";

    private const string ProgressId = CardId + "-Progress";

    private const string DetailId = CardId + "-Detail";

    private const double PollSeconds = 5;

    private const string Nb = " ";

    private static readonly CameraWindowDefaults Normal = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };

    private static readonly Color Accent = Color.Parse("#FA6831");

    private static readonly Color Primary = Color.Parse("#F4F4F5");

    private static readonly Color Secondary = Color.Parse("#C9C9CE");

    private static readonly Color Cooling = Color.Parse("#6CB4FF");

    private static readonly Color Cooled = Color.Parse("#6CCB5F");

    private static readonly Color NozzleColor = Color.Parse("#FF8A3D");

    private static readonly Color BedColor = Color.Parse("#6CB4FF");

    public static TheoryData<string, OverlayWidgetSize> LanguagesAndSizes =>
        [.. AppLanguages.Supported.SelectMany(static language => new[] { OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large }.Select(size => (language, size)))];

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static CameraOverlayLayout Layout(OverlayWidgetSize size, bool emphasized = true) =>
        new([new OverlayWidgetPlacement(OverlayWidgetIds.Cooldown, OverlayAnchor.BottomLeft, size, emphasized)]);

    /// <summary>The nozzle after a print: 215 °C cooling to a 23 °C room with τ = 60 s (the simulator's curve).</summary>
    private static double Newton(double seconds) => 23 + (192 * Math.Exp(-seconds / 60));

    private static PrinterSnapshot Printing(double seconds = 0) => new()
    {
        PrinterId = "a",
        State = PrinterState.Printing,
        Timestamp = Now.AddSeconds(seconds),
        Job = Job(50, TimeSpan.FromMinutes(30)),
        Telemetry = new Telemetry { Nozzle = new Temperature(215, 215), Bed = new Temperature(60, 60) },
    };

    private static PrinterSnapshot Ended(double seconds, double nozzle, PrinterState state = PrinterState.Finished, bool withBed = true, double bed = 40.4) => new()
    {
        PrinterId = "a",
        State = state,
        Timestamp = Now.AddSeconds(seconds),
        Job = Job(100),
        Telemetry = new Telemetry { Nozzle = new Temperature(nozzle, 0), Bed = withBed ? new Temperature(bed, 0) : null },
    };

    private static CameraHarness Harness(CameraOverlayLayout layout, int threshold = 50)
    {
        CameraHarness harness = new(window: Normal, overlay: layout, widgets: OverlayWidgets.CreateRegistry());
        harness.Time.SetUtcNow(Now);
        harness.Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        // The nozzle decides (T-109): the bed of these snapshots (40.4 °C) stays below the bed threshold of 45 °C.
        harness.Cooldown.Configure(
            [.. harness.Printers.Current.Printers!.Select(printer => printer with { CooldownThresholds = new(threshold, 45) })],
            TimeSpan.FromSeconds(PollSeconds));

        return harness;
    }

    /// <summary>
    /// A poll of printer "a". The window's clock stays (the cool-down monitor goes by the snapshot times; a clock moved by
    /// minutes would also make the camera report "No signal" over the snapshots); tests of the countdown move it themselves.
    /// </summary>
    private static void Raise(CameraHarness harness, PrinterSnapshot snapshot) => harness.Monitor.Raise(snapshot);

    private static async Task<CameraWindow> OpenAsync(CameraHarness harness, double width = 960, double height = 540)
    {
        (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Grey);
        window.Width = width;
        window.Height = height;
        window.Settle();
        return window;
    }

    /// <summary>The print, its end and readings every 5 s until the monitor has an estimate; then the card shows it.</summary>
    private static async Task CoolUntilEstimateAsync(CameraHarness harness, CameraWindow window, PrinterState ended = PrinterState.Finished, bool withBed = true)
    {
        Raise(harness, Printing());
        for (double t = PollSeconds; harness.Cooldown.GetState("a").EstimatedRemaining is null; t += PollSeconds)
        {
            Assert.True(t < 100, "no estimate");
            Raise(harness, Ended(t, Newton(t - PollSeconds), ended, withBed));
        }

        await Ui.UntilAsync(() => Widget(window).IsShown && Widget(window).ShownEstimateMinutes is not null, "Cool-down card with the estimate");
        window.Settle();
    }

    /// <summary>The print, its end and readings every 5 s along the curve until it cooled down (200 s).</summary>
    private static async Task CoolDownAsync(CameraHarness harness, CameraWindow window, PrinterState ended = PrinterState.Finished, bool withBed = true)
    {
        Raise(harness, Printing());
        for (double t = PollSeconds; t <= 200; t += PollSeconds)
        {
            Raise(harness, Ended(t, Newton(t - PollSeconds), ended, withBed));
        }

        Assert.Equal(CooldownPhase.CooledDown, harness.Cooldown.GetState("a").Phase);
        await Ui.UntilAsync(() => Widget(window).IsShown && Widget(window).IsCooled, "Cooled down card");
        window.Settle();
    }

    private static CooldownWidgetViewModel Widget(CameraWindow window) =>
        (CooldownWidgetViewModel)ViewModel(window).Overlay.Widgets.Single(static widget => widget.Id == OverlayWidgetIds.Cooldown);

    private static Border Card(CameraWindow window) => window.Find<Border>(CardId);

    private static Rect BoundsInWindow(Visual control, CameraWindow window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    /// <summary>The drawn text blocks of a part, left to right.</summary>
    private static TextBlock[] Texts(Control part) =>
        [.. part.GetSelfAndVisualDescendants().OfType<TextBlock>().Where(static block => block.IsEffectivelyVisible)];

    private static TextBlock Text(CameraWindow window, string id) => Assert.Single(Texts(window.Find<Control>(id)));

    private static OverlayIcon[] Icons(Control part) =>
        [.. part.GetVisualDescendants().OfType<OverlayIcon>().Where(static icon => icon.IsEffectivelyVisible)];

    private static Color ColorOf(IBrush? brush) => Assert.IsType<ISolidColorBrush>(brush, exactMatch: false).Color;

    /// <summary>
    /// No text of the card is cut (the title and the detail may wrap to two lines, T-076 1.4), the card has a width of its
    /// size (T-076 2.5) and it stays in the window.
    /// </summary>
    private static void AssertFits(CameraWindow window, OverlayWidgetSize size)
    {
        Border card = Card(window);
        Rect bounds = BoundsInWindow(card, window);
        (double min, double max) = size switch
        {
            OverlayWidgetSize.Small => (0, 260),
            OverlayWidgetSize.Medium => (240, 300),
            _ => (280, 360),
        };
        Assert.InRange(bounds.Width, min - 0.5, max + 0.5);
        Assert.True(new Rect(window.ClientSize).Contains(bounds), $"card {bounds} outside {window.ClientSize}");
        foreach (TextBlock block in card.GetVisualDescendants().OfType<TextBlock>().Where(static block => block.IsEffectivelyVisible && !string.IsNullOrEmpty(block.Text)))
        {
            Assert.All(block.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, $"'{block.Text}' is trimmed ({bounds.Width} wide)"));
            string? id = AutomationProperties.GetAutomationId(block);
            int maxLines = size != OverlayWidgetSize.Small && (id == TitleId || id == DetailId) ? 2 : 1;
            Assert.InRange(block.TextLayout.TextLines.Count, 1, maxLines);
            Rect text = BoundsInWindow(block, window);
            Assert.True(bounds.Contains(text), $"'{block.Text}' {text} outside the card {bounds}");
        }
    }

    // ---- Cooling down ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small)]
    [InlineData(OverlayWidgetSize.Medium)]
    [InlineData(OverlayWidgetSize.Large)]
    public Task CoolingCardShowsThePartsOfItsSize(OverlayWidgetSize size) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(size));
            CameraWindow window = await OpenAsync(harness);
            await CoolUntilEstimateAsync(harness, window);
            CooldownWidgetViewModel widget = Widget(window);
            bool small = size == OverlayWidgetSize.Small;
            bool large = size == OverlayWidgetSize.Large;
            long minutes = widget.ShownEstimateMinutes!.Value;

            Assert.Equal(size, widget.RenderedSize);
            AssertFits(window, size);
            Assert.False(widget.IsOutlined);
            Assert.Equal(0, Card(window).BorderThickness.Left);
            Assert.Equal(!small, window.IsShown<TextBlock>(TitleId));
            Assert.True(window.IsShown<Control>(NozzleId));
            Assert.Equal(!small, window.IsShown<Control>(BedId));
            Assert.True(window.IsShown<OverlayProgressBar>(ProgressId));
            Assert.True(window.IsShown<TextBlock>(DetailId));

            // The snowflake: S 16, M 20, L 24 (its 24 variant), in the cooling blue.
            OverlayIcon state = Icons(Card(window))[0];
            double iconSize = small ? 16 : large ? 24 : 20;
            Assert.Equal(new Size(iconSize, iconSize), state.Bounds.Size);
            Assert.Same(window.FindResource(large ? "IconWeatherSnowflake24Regular" : "IconWeatherSnowflake20Regular"), state.Data);
            Assert.Equal(Cooling, ColorOf(state.Foreground));

            // The nozzle: the key value, bold (S 15, M 22, L 28), accent when emphasized.
            TextBlock nozzle = Text(window, NozzleId);
            Assert.Equal(small ? 15 : large ? 28 : 22, nozzle.FontSize);
            Assert.Equal(FontWeight.Bold, nozzle.FontWeight);
            Assert.Equal(Accent, ColorOf(nozzle.Foreground));
            Assert.Contains(nozzle.FontFeatures!, static feature => feature.Tag == "tnum" && feature.Value == 1);

            // The bar towards the threshold: 4 / 6 / 8 high, cooling blue on the track, at the progress.
            OverlayProgressBar bar = window.Find<OverlayProgressBar>(ProgressId);
            Assert.Equal(small ? 4 : large ? 8 : 6, bar.Bounds.Height);
            Assert.Equal(Cooling, ColorOf(bar.Foreground));
            Assert.Equal(widget.Progress, bar.Value);
            Assert.InRange(bar.Value, 1, 99);

            TextBlock detail = Text(window, DetailId);
            if (small)
            {
                // One row "❄ 87 °C · about 2 min": 46 high with the bar (T-076 2.5).
                Assert.Equal($" · about {minutes}\u00A0min", detail.Text);
                Assert.Equal(13, detail.FontSize);
                Assert.Equal(FontWeight.SemiBold, detail.FontWeight);
                Assert.Equal(46, Card(window).Bounds.Height, 0.5);
                Assert.Equal(BoundsInWindow(nozzle, window).Center.Y, BoundsInWindow(detail, window).Center.Y, 1);

                // T-109 Q3 B (ui-designer review 2026-10-30): the cone of the nozzle, 12 in its colour, 2 before the number.
                AssertSmallSensorIcon(window, nozzle, "IconCone16Filled", NozzleColor);
            }
            else
            {
                TextBlock title = window.Find<TextBlock>(TitleId);
                Assert.Equal("Cooling down…", title.Text);
                Assert.Equal(large ? 16 : 14, title.FontSize);
                Assert.Equal(FontWeight.SemiBold, title.FontWeight);
                Assert.Equal(Primary, ColorOf(title.Foreground));
                TextBlock bed = Texts(window.Find<Control>(BedId)).Single();
                Assert.Equal($"40{Nb}°C", bed.Text);
                Assert.Equal(large ? 16 : 14, bed.FontSize);
                Assert.Equal(FontWeight.SemiBold, bed.FontWeight);
                Assert.Equal(Secondary, ColorOf(bed.Foreground));
                Assert.Equal($"safe to turn off in about {minutes}\u00A0min (nozzle below 50{Nb}°C, bed below 45{Nb}°C)", detail.Text);
                Assert.Equal(large ? 13 : 12, detail.FontSize);
                Assert.Equal(FontWeight.Normal, detail.FontWeight);
                Assert.Equal(Secondary, ColorOf(detail.Foreground));

                // The cone under the snowflake, the bed line next to the nozzle, in the colours of the Temperatures widget.
                OverlayIcon cone = Icons(window.Find<Control>(NozzleId)).Single();
                OverlayIcon line = Icons(window.Find<Control>(BedId)).Single();
                Assert.Equal(NozzleColor, ColorOf(cone.Foreground));
                Assert.Equal(BedColor, ColorOf(line.Foreground));
                Assert.Equal(180, Assert.IsType<RotateTransform>(cone.RenderTransform).Angle);
                // The centre stays where it is when the cone is turned (its corners do not).
                Point coneCentre = cone.TranslatePoint(new Point(cone.Bounds.Width / 2, cone.Bounds.Height / 2), window)!.Value;
                Assert.Equal(BoundsInWindow(state, window).Center.X, coneCentre.X, 0.5);
                Assert.Equal(BoundsInWindow(title, window).Left, BoundsInWindow(nozzle, window).Left, 0.5);
                Assert.Equal(BoundsInWindow(nozzle, window).Right + 16, BoundsInWindow(line, window).Left, 0.5);

                // M (T-076 2.5): 12 + title 20 + 8 + nozzle 28 + 8 + bar 6 + 8 + detail (1–2 lines of 16) + 12; L larger.
                int lines = detail.TextLayout.TextLines.Count;
                double height = large ? 14 + 24 + 8 + 34 + 8 + 8 + 8 + (lines * 18) + 14 : 12 + 20 + 8 + 28 + 8 + 6 + 8 + (lines * 16) + 12;
                Assert.Equal(height, Card(window).Bounds.Height, 0.5);
            }

            // The screen reader gets every value in words, also those S does not draw.
            Assert.Equal(
                $"Cooling down…, Nozzle {widget.ShownNozzleCelsius}{Nb}°C, Bed 40{Nb}°C, safe to turn off in about {minutes}\u00A0min (nozzle below 50{Nb}°C, bed below 45{Nb}°C)",
                Card(window).GetValue(AutomationProperties.NameProperty));
        });

    [Fact]
    public Task EveryPartHasItsAutomationIdOnce() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness);
            await CoolUntilEstimateAsync(harness, window);

            foreach (string id in new[] { TitleId, NozzleId, BedId, ProgressId, DetailId })
            {
                Control part = window.Find<Control>(id);
                Assert.True(part.IsEffectivelyVisible, id);
            }

            Assert.True(Card(window).IsEffectivelyVisible);
        });

    [Fact]
    public Task WithoutEmphasisNothingIsInTheAccentColour() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium, emphasized: false));
            CameraWindow window = await OpenAsync(harness);
            await CoolUntilEstimateAsync(harness, window);

            TextBlock nozzle = Text(window, NozzleId);
            Assert.Equal(Primary, ColorOf(nozzle.Foreground));
            Assert.Equal(22, nozzle.FontSize);
            Assert.Equal(FontWeight.Bold, nozzle.FontWeight);

            await CoolDownAsync(harness, window);
            TextBlock title = window.Find<TextBlock>(TitleId);
            Assert.Equal(Primary, ColorOf(title.Foreground));
            Assert.Equal(14, title.FontSize);
            Assert.Equal(FontWeight.SemiBold, title.FontWeight);
        });

    [Fact]
    public Task MissingBedShowsOnlyTheNozzle() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness);
            await CoolUntilEstimateAsync(harness, window, withBed: false);

            Assert.True(window.IsShown<Control>(NozzleId));
            Assert.False(window.IsShown<Control>(BedId));
            AssertFits(window, OverlayWidgetSize.Medium);
        });

    [Fact]
    public Task EstimateCountsDownWithTheClockAndThenNamesOnlyTheThreshold() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium), threshold: 30);
            CameraWindow window = await OpenAsync(harness);
            await CoolUntilEstimateAsync(harness, window);
            // The clock at the last poll, as in the app.
            harness.Time.SetUtcNow(harness.Cooldown.GetState("a").UpdatedAt!.Value);
            Dispatcher.UIThread.RunJobs();
            long minutes = Widget(window).ShownEstimateMinutes!.Value;
            Assert.True(minutes >= 2, $"{minutes} min");

            // A minute without snapshots: the estimate counts down (the monitor's clock is the window's).
            harness.Time.Advance(TimeSpan.FromMinutes(1));
            await Ui.UntilAsync(() => Widget(window).ShownEstimateMinutes == minutes - 1, "estimate counted down");
            window.Settle();
            Assert.Equal($"safe to turn off in about {minutes - 1}\u00A0min (nozzle below 30{Nb}°C, bed below 45{Nb}°C)", Text(window, DetailId).Text);

            // Past the estimate: never "0 min".
            harness.Time.Advance(TimeSpan.FromMinutes(minutes));
            await Ui.UntilAsync(() => Widget(window).ShownEstimateMinutes is null, "estimate gone");
            window.Settle();
            Assert.Equal($"safe to turn off once the nozzle is below 30{Nb}°C and the bed below 45{Nb}°C", Text(window, DetailId).Text);
        });

    /// <summary>The single sensor icon in front of the S temperature: its geometry, 12 × 12, its colour, 2 before the text.</summary>
    private static void AssertSmallSensorIcon(CameraWindow window, TextBlock value, string geometry, Color color)
    {
        OverlayIcon icon = Assert.Single(Icons(window.Find<Control>(NozzleId)));
        Assert.Same(window.FindResource(geometry), icon.Data);
        Assert.Equal(new Size(12, 12), icon.Bounds.Size);
        Assert.Equal(color, ColorOf(icon.Foreground));
        // Layout bounds in their shared row (the cone is drawn turned by 180°, which a translated corner would mirror).
        Assert.Same(value.GetVisualParent(), icon.GetVisualParent());
        Assert.Equal(2, value.Bounds.Left - icon.Bounds.Right, 0.5);
        Assert.Equal(value.Bounds.Center.Y, icon.Bounds.Center.Y, 1);
    }

    /// <summary>
    /// T-109 Q3 B (ui-designer review 2026-10-30): while the bed holds the cool-down up, the S card shows the bed with the
    /// heated bed icon in its colour in front of it (not the cone, not the bare line of M / L: T-109 QS1), in every language without a cut text; snapshot in English.
    /// </summary>
    [Fact]
    public Task SmallCardShowsTheBedIconWhileTheBedLimits() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Small));
            CameraWindow window = await OpenAsync(harness, 720, 405);

            // The nozzle is below its 50 °C at once, the bed (threshold 45 °C) cools slowly from 60 °C.
            Raise(harness, Printing());
            for (double t = PollSeconds; harness.Cooldown.GetState("a").EstimatedRemaining is null; t += PollSeconds)
            {
                Assert.True(t < 200, "no estimate");
                Raise(harness, Ended(t, 35 - (t / 100), bed: 45 + (15 * Math.Exp(-t / 120))));
            }

            await Ui.UntilAsync(() => Widget(window).IsShown && Widget(window).ShownEstimateMinutes is not null, "Cool-down card with the estimate");
            window.Settle();
            Assert.Equal(CooldownSensor.Bed, harness.Cooldown.GetState("a").Limiting);
            TextBlock value = Text(window, NozzleId);
            Assert.Equal($"{Widget(window).ShownLimitingCelsius}{Nb}°C", value.Text);
            // QS1: the heated bed (plate + heat waves), not the bare line, which read as a minus sign ("−57 °C").
            AssertSmallSensorIcon(window, value, "IconHeatedBed16Filled", BedColor);
            SnapshotAssert.Matches(window, "camera-widget-cooldown-cooling-s-bed");

            foreach (string language in AppLanguages.Supported)
            {
                Assert.True(new UiLanguage().Apply(language));
                ViewModel(window).RefreshTexts();
                window.Settle();
                AssertFits(window, OverlayWidgetSize.Small);
            }
        });

    // ---- Cooled down ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small)]
    [InlineData(OverlayWidgetSize.Medium)]
    [InlineData(OverlayWidgetSize.Large)]
    public Task CooledCardHasTheGreenOutlinePowerIconAndTimes(OverlayWidgetSize size) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(size));
            CameraWindow window = await OpenAsync(harness);
            await CoolDownAsync(harness, window);
            CooldownWidgetViewModel widget = Widget(window);
            bool small = size == OverlayWidgetSize.Small;
            bool large = size == OverlayWidgetSize.Large;
            CooldownState state = harness.Cooldown.GetState("a");
            long cooledIn = (long)Math.Ceiling((state.CooledDownAt!.Value - state.PrintEndedAt!.Value).TotalMinutes);

            AssertFits(window, size);
            Border card = Card(window);
            Assert.Equal(new Thickness(1.5), card.BorderThickness);
            Assert.Equal(Cooled, ColorOf(card.BorderBrush));
            // The outline takes its width from the padding: the content stays where it was.
            double padding = small ? 10 : large ? 16 : 14;
            Assert.Equal(padding, card.BorderThickness.Left + card.Padding.Left, 0.01);

            OverlayIcon power = Icons(card)[0];
            double iconSize = small ? 16 : large ? 24 : 20;
            Assert.Equal(new Size(iconSize, iconSize), power.Bounds.Size);
            Assert.Same(window.FindResource(large ? "IconPower24Regular" : "IconPower20Regular"), power.Data);
            Assert.Equal(Cooled, ColorOf(power.Foreground));
            Assert.False(window.IsShown<OverlayProgressBar>(ProgressId));

            // The state text is the key value: accent, at least 15 bold (L 16), in the line height of the title.
            TextBlock title = window.Find<TextBlock>(TitleId);
            Assert.Equal(Accent, ColorOf(title.Foreground));
            Assert.Equal(large ? 16 : 15, title.FontSize);
            Assert.Equal(FontWeight.Bold, title.FontWeight);
            if (small)
            {
                // "⏻ Cooled down · 48 °C": one row, 36 high.
                Assert.Equal($"Cooled down · {widget.ShownNozzleCelsius}{Nb}°C", title.Text);
                Assert.False(window.IsShown<Control>(NozzleId));
                Assert.False(window.IsShown<Control>(BedId));
                Assert.False(window.IsShown<TextBlock>(DetailId));
                Assert.Equal(36, card.Bounds.Height, 0.5);
            }
            else
            {
                Assert.Equal("Cooled down – safe to turn off", title.Text);
                TextBlock nozzle = Text(window, NozzleId);
                Assert.Equal(large ? 16 : 14, nozzle.FontSize);
                Assert.Equal(FontWeight.SemiBold, nozzle.FontWeight);
                Assert.Equal(Primary, ColorOf(nozzle.Foreground));
                TextBlock detail = Text(window, DetailId);
                Assert.Equal(CultureTime.EnUs($"print finished at 12:00 PM · cooled down in {cooledIn}\u00A0min"), detail.Text);
                // M: 96 with one detail line (T-076 2.5: 96–112).
                int lines = detail.TextLayout.TextLines.Count;
                double height = large ? 14 + 24 + 8 + 22 + 8 + (lines * 18) + 14 : 12 + 20 + 8 + 20 + 8 + (lines * 16) + 12;
                Assert.Equal(height, card.Bounds.Height, 0.5);
            }

            Assert.Equal(
                CultureTime.EnUs($"Cooled down – safe to turn off, Nozzle {widget.ShownNozzleCelsius}{Nb}°C, Bed 40{Nb}°C, print finished at 12:00 PM, cooled down in {cooledIn}\u00A0min"),
                card.GetValue(AutomationProperties.NameProperty));
        });

    [Fact]
    public Task StoppedPrintSaysStopped() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness);
            await CoolDownAsync(harness, window, PrinterState.Stopped);

            Assert.StartsWith(CultureTime.EnUs("print stopped at 12:00 PM · cooled down in "), Text(window, DetailId).Text, StringComparison.Ordinal);
        });

    [Fact]
    public Task UnknownEndOfThePrintHasNoTimesRow() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness);

            // Seen printing, unreadable while the print ended (user decision 10).
            Raise(harness, Printing());
            Raise(harness, PrinterSnapshot.Unavailable("a", PrinterState.Offline, Now.AddSeconds(5), "unreachable"));
            for (double t = 10; t <= 200; t += PollSeconds)
            {
                Raise(harness, Ended(t, Newton(t - 10)));
            }

            await Ui.UntilAsync(() => Widget(window).IsShown && Widget(window).IsCooled, "Cooled down card");
            window.Settle();

            Assert.Null(harness.Cooldown.GetState("a").PrintEndedAt);
            Assert.True(window.IsShown<TextBlock>(TitleId));
            Assert.True(window.IsShown<Control>(NozzleId));
            Assert.False(window.IsShown<TextBlock>(DetailId));
            // 12 + title 20 + 8 + temperatures 20 + 12.
            Assert.Equal(72, Card(window).Bounds.Height, 0.5);
        });

    [Fact]
    public Task PrintEndedBelowTheThresholdShowsOnlyWhenItEnded() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness);

            Raise(harness, Printing());
            Raise(harness, Ended(5, 45));
            Raise(harness, Ended(10, 44));
            await Ui.UntilAsync(() => Widget(window).IsShown && Widget(window).IsCooled, "Cooled down card");
            window.Settle();

            Assert.Equal(CultureTime.EnUs("print finished at 12:00 PM"), Text(window, DetailId).Text);
        });

    [Fact]
    public Task CardDisappearsAtTheNextPrint() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness);
            await CoolDownAsync(harness, window);

            Raise(harness, Printing(205));
            await Ui.UntilAsync(() => !Widget(window).IsShown, "Cool-down card hidden");
            window.Settle();

            Assert.False(Card(window).IsEffectivelyVisible);
        });

    // ---- Layout mode ----

    [Fact]
    public Task LayoutModeShowsTheCooldownCard() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(CameraOverlayLayout.Default);
            CameraWindow window = await OpenAsync(harness);
            Raise(harness, Ended(0, 30) with { State = PrinterState.Idle });

            // Without a cool-down: the placeholder card (its name and "no data right now").
            window.Click(window.Find<Button>("CameraLayoutButton"));
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            Assert.True(ViewModel(window).LayoutMode.IsActive);
            CameraLayoutCard placeholder = window.Find<CameraLayoutCard>(CameraLayoutCardViewModel.AutomationIdPrefix + OverlayWidgetIds.Cooldown);
            Assert.True(Ui.ShowsText(placeholder, "Cool-down"));
            Assert.True(Ui.ShowsText(placeholder, "no data right now"));
            Border placeholderCard = placeholder.GetVisualDescendants().OfType<Border>().First(static border => border.Classes.Contains("overlayCard"));
            Assert.Equal(0, placeholderCard.BorderThickness.Left);
            ViewModel(window).LayoutMode.CancelCommand.Execute(null);
            window.Settle();

            // Cooled down: the real card with its outline.
            await CoolDownAsync(harness, window);
            window.Click(window.Find<Button>("CameraLayoutButton"));
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            CameraLayoutCard card = window.Find<CameraLayoutCard>(CameraLayoutCardViewModel.AutomationIdPrefix + OverlayWidgetIds.Cooldown);
            Assert.True(Ui.ShowsText(card, "Cooled down – safe to turn off"));
            Assert.False(Ui.ShowsText(card, "no data right now"));
            Border overlayCard = card.GetVisualDescendants().OfType<Border>().First(static border => border.Classes.Contains("overlayCard"));
            Assert.Equal(new Thickness(1.5), overlayCard.BorderThickness);
            Assert.Equal(Cooled, ColorOf(overlayCard.BorderBrush));
        });

    // ---- Languages ----

    /// <summary>The longest texts (a three-digit nozzle, the threshold 100, stopped, hours) in every language and size.</summary>
    [Theory]
    [MemberData(nameof(LanguagesAndSizes))]
    public Task LongestTextsFitInEveryLanguageAndSize(string language, OverlayWidgetSize size) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            await using CameraHarness harness = Harness(Layout(size), threshold: 100);
            CameraWindow window = await OpenAsync(harness);

            // Cooling without an estimate yet (the longer text).
            Raise(harness, Printing());
            Raise(harness, Ended(5, 215, PrinterState.Stopped));
            Raise(harness, Ended(10, 214, PrinterState.Stopped));
            await Ui.UntilAsync(() => Widget(window).IsShown, "Cool-down card");
            window.Settle();
            AssertFits(window, size);
            Assert.Equal(size != OverlayWidgetSize.Small, Ui.ShowsText(window, Strings.CameraOverlayCooling));

            // Cooling with an estimate.
            double t = 15;
            for (; harness.Cooldown.GetState("a").EstimatedRemaining is null; t += PollSeconds)
            {
                Assert.True(t < 100, "no estimate");
                Raise(harness, Ended(t, Newton(t - 10), PrinterState.Stopped));
            }

            await Ui.UntilAsync(() => Widget(window).ShownEstimateMinutes is not null, "estimate shown");
            window.Settle();
            AssertFits(window, size);

            // Cooled down after a stopped print (polls go on every 5 s: a longer gap would end the cool-down).
            for (; harness.Cooldown.GetState("a").Phase != CooldownPhase.CooledDown; t += PollSeconds)
            {
                Assert.True(t < 300, "not cooled down");
                Raise(harness, Ended(t, Newton(t - 10), PrinterState.Stopped));
            }

            await Ui.UntilAsync(() => Widget(window).IsCooled, "cooled down");
            window.Settle();
            AssertFits(window, size);
            Assert.True(Ui.ShowsText(window, size == OverlayWidgetSize.Small ? Widget(window).HeaderText : Strings.CameraOverlayCooled));
        });

    [Theory]
    [InlineData("cs")]
    [InlineData("de")]
    public Task CardFollowsALanguageSwitch(string language) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness);
            await CoolDownAsync(harness, window);

            Assert.True(new UiLanguage().Apply(language));
            ViewModel(window).RefreshTexts();
            window.Settle();

            Assert.Equal(Strings.CameraOverlayCooled, window.Find<TextBlock>(TitleId).Text);
            Assert.StartsWith(Strings.CameraOverlayCooled + ", ", (string?)Card(window).GetValue(AutomationProperties.NameProperty), StringComparison.Ordinal);
            AssertFits(window, OverlayWidgetSize.Medium);
        });

    // ---- Snapshots: "Cooling down…" → "Cooled down" with the fake clock ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small, "s")]
    [InlineData(OverlayWidgetSize.Medium, "m")]
    [InlineData(OverlayWidgetSize.Large, "l")]
    public Task SnapshotsOfTheCoolingAndCooledCard(OverlayWidgetSize size, string suffix) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(size));
            CameraWindow window = await OpenAsync(harness, 720, 405);

            await CoolUntilEstimateAsync(harness, window);
            SnapshotAssert.Matches(window, "camera-widget-cooldown-cooling-" + suffix);

            await CoolDownAsync(harness, window);
            SnapshotAssert.Matches(window, "camera-widget-cooldown-cooled-" + suffix);
        });

    [Fact]
    public Task SnapshotOfTheCoolingCardInGerman() =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply("de"));
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, 720, 405);

            await CoolUntilEstimateAsync(harness, window);

            SnapshotAssert.Matches(window, "camera-widget-cooldown-cooling-m-de");
        });
}
