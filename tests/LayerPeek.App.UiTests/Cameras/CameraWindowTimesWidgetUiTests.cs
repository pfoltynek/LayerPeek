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
/// The Times widget (T-083, design T-076 2.2) in the real camera window with the production registration: S / M / L, the
/// parts and their AutomationIds, the screen reader name, emphasis, tabular figures, unknown values, and the longest
/// translations without overflow.
/// </summary>
public sealed class CameraWindowTimesWidgetUiTests(HeadlessSession session)
{
    private const uint Grey = 0xFF606060;

    private const string CardId = OverlayWidgetViewModel.AutomationIdPrefix + OverlayWidgetIds.Times;

    private const string RemainingId = CardId + "-Remaining";

    private const string FinishId = CardId + "-Finish";

    private const string ElapsedId = CardId + "-Elapsed";

    private static readonly CameraWindowDefaults Normal = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    public static TheoryData<string, OverlayWidgetSize> LanguagesAndSizes =>
        [.. AppLanguages.Supported.SelectMany(static language => new[] { OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large }.Select(size => (language, size)))];

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static CameraOverlayLayout Layout(OverlayWidgetSize size, bool emphasized = true, OverlayAnchor anchor = OverlayAnchor.BottomLeft) =>
        new([new OverlayWidgetPlacement(OverlayWidgetIds.Times, anchor, size, emphasized)]);

    /// <summary>A job at <see cref="TestPrinters.Now"/> (12:00 UTC): 65 min left (done at 1:05 PM), 130 min printed.</summary>
    private static PrinterSnapshot Printing(TimeSpan? remaining = null, TimeSpan? elapsed = null, double progress = 42.7, bool knownRemaining = true) => new()
    {
        PrinterId = "a",
        State = PrinterState.Printing,
        Timestamp = Now,
        Job = new PrintJob
        {
            FileName = "benchy.bgcode",
            ProgressPercent = progress,
            Elapsed = elapsed ?? TimeSpan.FromMinutes(130),
            Remaining = knownRemaining ? remaining ?? TimeSpan.FromMinutes(65) : null,
        },
    };

    private static async Task<CameraWindow> OpenAsync(CameraHarness harness, PrinterSnapshot snapshot, double width = 960, double height = 540)
    {
        (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Grey);
        window.Width = width;
        window.Height = height;
        harness.Monitor.Raise(snapshot);
        await Ui.UntilAsync(() => Widget(window).IsShown, "Times card shown");
        window.Settle();
        return window;
    }

    private static CameraHarness Harness(CameraOverlayLayout layout) =>
        new(window: Normal, overlay: layout, widgets: OverlayWidgets.CreateRegistry());

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

    private static TimesWidgetViewModel Widget(CameraWindow window) =>
        (TimesWidgetViewModel)ViewModel(window).Overlay.Widgets.Single(static widget => widget.Id == OverlayWidgetIds.Times);

    private static Border Card(CameraWindow window) => window.Find<Border>(CardId);

    private static Rect BoundsInWindow(Visual control, CameraWindow window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    /// <summary>No text of the card is cut (trimmed or beyond 2 lines) and the card stays in its widths and in the window.</summary>
    private static void AssertFits(CameraWindow window, OverlayWidgetSize size)
    {
        Border card = Card(window);
        Rect bounds = BoundsInWindow(card, window);
        (double min, double max) = size switch
        {
            OverlayWidgetSize.Small => (0, 200),
            OverlayWidgetSize.Medium => (180, 260),
            _ => (220, 320),
        };
        Assert.InRange(bounds.Width, min - 0.5, max + 0.5);
        Assert.True(new Rect(window.ClientSize).Contains(bounds), $"card {bounds} outside {window.ClientSize}");
        foreach (TextBlock block in card.GetVisualDescendants().OfType<TextBlock>().Where(static block => block.IsEffectivelyVisible && !string.IsNullOrEmpty(block.Text)))
        {
            Assert.All(block.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, $"'{block.Text}' is trimmed ({bounds.Width} wide)"));
            Assert.InRange(block.TextLayout.TextLines.Count, 1, 2);
            Rect text = BoundsInWindow(block, window);
            Assert.True(bounds.Contains(text), $"'{block.Text}' {text} outside the card {bounds}");
        }
    }

    // ---- Sizes and parts ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small)]
    [InlineData(OverlayWidgetSize.Medium)]
    [InlineData(OverlayWidgetSize.Large)]
    public Task CardShowsThePartsOfItsSize(OverlayWidgetSize size) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(size));
            CameraWindow window = await OpenAsync(harness, Printing());

            Assert.Equal(size, Widget(window).RenderedSize);
            Assert.True(Card(window).IsEffectivelyVisible);
            Assert.Equal("1 h 5 min remaining, Done at 1:05 PM, Print time: 2 h 10 min", Card(window).GetValue(AutomationProperties.NameProperty));
            Assert.Equal("1 h 5 min", window.Find<TextBlock>(RemainingId).Text);
            Assert.True(window.IsShown<TextBlock>(RemainingId));
            bool rows = size != OverlayWidgetSize.Small;
            Assert.Equal(rows, window.IsShown<OverlayIconText>(FinishId));
            Assert.Equal(rows, window.IsShown<OverlayIconText>(ElapsedId));
            Assert.Equal(rows, Ui.ShowsText(window, "Remaining"));
            Assert.Equal(rows, Ui.ShowsText(window, "Done at 1:05 PM"));
            Assert.Equal(rows, Ui.ShowsText(window, "2 h 10 min"));
            AssertFits(window, size);

            // Sizes of T-076 1.2: the large number 15 / 26 / 34 bold.
            TextBlock remaining = window.Find<TextBlock>(RemainingId);
            Assert.Equal(size switch { OverlayWidgetSize.Small => 15, OverlayWidgetSize.Medium => 26, _ => 34 }, remaining.FontSize);
            Assert.Equal(FontWeight.Bold, remaining.FontWeight);
            if (rows)
            {
                OverlayIconText finish = window.Find<OverlayIconText>(FinishId);
                Assert.Equal(size == OverlayWidgetSize.Large ? 20 : 16, finish.IconSize);
                Assert.Equal(size == OverlayWidgetSize.Large ? 20 : 16, finish.IconCanvasSize);
                Assert.Equal(size == OverlayWidgetSize.Large ? 15 : 13, finish.FontSize);
            }
        });

    [Fact]
    public Task SmallCardIsOneRowWithTheHourglass() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Small));
            CameraWindow window = await OpenAsync(harness, Printing());

            // T-076 2.2: S is 36 high (padding 8 + line 20 + 8).
            Assert.Equal(36, Card(window).Bounds.Height, 0.5);
            OverlayIcon hourglass = Card(window).GetVisualDescendants().OfType<OverlayIcon>().Single(static icon => icon.IsEffectivelyVisible);
            Assert.Same(window.FindResource("IconHourglassHalf16Regular"), hourglass.Data);
        });

    [Fact]
    public Task MediumCardHasTheDesignedHeight() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Printing());

            // T-076 2.2: 12 + 16 + 2 + 32 + 8 + 18 + 4 + 18 + 12 = 122.
            Assert.Equal(122, Card(window).Bounds.Height, 0.5);
            Rect label = BoundsInWindow(Ui.Find<TextBlock>(Card(window), static block => block.Text == "Remaining"), window);
            Rect value = BoundsInWindow(window.Find<TextBlock>(RemainingId), window);
            Assert.Equal(label.X, value.X, 0.5);
        });

    [Fact]
    public Task UnknownRemainingTimeStartsWithThePrintTime() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium, anchor: OverlayAnchor.TopLeft));
            CameraWindow window = await OpenAsync(harness, Printing(progress: 0, knownRemaining: false, elapsed: TimeSpan.FromMinutes(3)));

            Assert.False(window.IsShown<TextBlock>(RemainingId));
            Assert.False(window.IsShown<OverlayIconText>(FinishId));
            Assert.False(Ui.ShowsText(window, "Remaining"));
            OverlayIconText elapsed = window.Find<OverlayIconText>(ElapsedId);
            Assert.True(elapsed.IsEffectivelyVisible);
            // The first row starts right at the card padding (no gap for the missing rows).
            Assert.Equal(BoundsInWindow(Card(window), window).Y + 12, BoundsInWindow(elapsed, window).Y, 0.5);
            Assert.Equal("Print time: 3 min", Card(window).GetValue(AutomationProperties.NameProperty));
        });

    [Fact]
    public Task SmallCardWithoutARemainingTimeHides() =>
        session.RunAsync(async scope =>
        {
            // T-076 2: a card left without a row hides; S has only the remaining time.
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Small));
            CameraWindow window = await OpenAsync(harness, Printing());

            harness.Monitor.Raise(Printing(progress: 0, knownRemaining: false, elapsed: TimeSpan.FromMinutes(3)));
            await Ui.UntilAsync(() => !Widget(window).IsShown, "Times card hidden");
            window.Settle();
            Assert.False(Card(window).IsEffectivelyVisible);

            // The layout in M has the print time row: the card comes back.
            Save(harness, Layout(OverlayWidgetSize.Medium));

            Assert.True(Card(window).IsEffectivelyVisible);
            Assert.True(window.IsShown<OverlayIconText>(ElapsedId));
            Assert.Equal("3 min", window.Find<OverlayIconText>(ElapsedId).Text);
        });

    [Fact]
    public Task SavedSizeChangesTheOpenCard() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Printing());
            Assert.True(window.IsShown<OverlayIconText>(FinishId));

            Save(harness, Layout(OverlayWidgetSize.Small));

            Assert.Equal(OverlayWidgetSize.Small, Widget(window).RenderedSize);
            Assert.Equal(36, Card(window).Bounds.Height, 0.5);
            Assert.False(window.IsShown<OverlayIconText>(FinishId));
        });

    [Fact]
    public Task CardDisappearsWhenTheJobEnds() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Printing());

            harness.Monitor.Raise(Printing() with { State = PrinterState.Finished });
            await Ui.UntilAsync(() => !Widget(window).IsShown, "Times card hidden");
            window.Settle();

            Assert.False(Card(window).IsEffectivelyVisible);
        });

    // ---- Look ----

    [Fact]
    public Task EmphasisDrawsTheRemainingTimeInTheAccentColour() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium, emphasized: true));
            CameraWindow window = await OpenAsync(harness, Printing());
            TextBlock remaining = window.Find<TextBlock>(RemainingId);

            Assert.Equal(Color.Parse("#FA6831"), ((ISolidColorBrush)remaining.Foreground!).Color);
            // Only the key value: the rows stay in their secondary colour (T-076 1.3).
            // The drawn text, not just the row's property (the card's TextBlock styles also reach the template part).
            TextBlock finishText = window.Find<OverlayIconText>(FinishId).GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(Color.Parse("#C9C9CE"), ((ISolidColorBrush)finishText.Foreground!).Color);

            Save(harness, Layout(OverlayWidgetSize.Medium, emphasized: false));

            Assert.Equal(Color.Parse("#F4F4F5"), ((ISolidColorBrush)window.Find<TextBlock>(RemainingId).Foreground!).Color);
        });

    [Fact]
    public Task RemainingTimeUsesTabularFigures() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Printing(remaining: TimeSpan.FromMinutes(71)));
            TextBlock remaining = window.Find<TextBlock>(RemainingId);
            Assert.Equal("1 h 11 min", remaining.Text);
            double narrow = remaining.Bounds.Width;

            harness.Monitor.Raise(Printing(remaining: TimeSpan.FromMinutes(108)));
            await Ui.UntilAsync(() => remaining.Text == "1 h 48 min", "1 h 48 min shown");
            window.Settle();

            Assert.Contains(remaining.FontFeatures!, static feature => feature.Tag == "tnum" && feature.Value == 1);
            Assert.Equal(narrow, remaining.Bounds.Width, 0.01);
        });

    // ---- Languages ----

    /// <summary>
    /// The longest texts: a finish on another day (es "Termina el 17 de octubre a las 8:00 AM" may wrap to 2 lines) and a
    /// remaining time of days, in every language and size.
    /// </summary>
    [Theory]
    [MemberData(nameof(LanguagesAndSizes))]
    public Task LongestTextsFitInEveryLanguageAndSize(string language, OverlayWidgetSize size) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            await using CameraHarness harness = Harness(Layout(size));
            CameraWindow window = await OpenAsync(harness, Printing(remaining: TimeSpan.FromHours(20), elapsed: TimeSpan.FromHours(30)));

            Assert.True(Widget(window).HasFinish);
            AssertFits(window, size);

            // A remaining time of days (the widest large number).
            harness.Monitor.Raise(Printing(remaining: TimeSpan.FromHours(99), elapsed: TimeSpan.FromHours(30)));
            string days = OverlayFormat.Duration(99 * 60);
            await Ui.UntilAsync(() => Widget(window).Remaining == days, days + " shown");
            window.Settle();
            Assert.Equal(days, window.Find<TextBlock>(RemainingId).Text);
            AssertFits(window, size);
        });

    [Theory]
    [MemberData(nameof(Languages))]
    public Task CardFollowsALanguageSwitch(string language) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Printing());

            Assert.True(new UiLanguage().Apply(language));
            ViewModel(window).RefreshTexts();
            window.Settle();

            string label = Strings.CameraOverlayRemainingLabel;
            Assert.True(Ui.ShowsText(window, label), label);
            Assert.StartsWith(OverlayFormat.Remaining(65), (string?)Card(window).GetValue(AutomationProperties.NameProperty), StringComparison.Ordinal);
            AssertFits(window, OverlayWidgetSize.Medium);
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
            CameraWindow window = await OpenAsync(harness, Printing(), 720, 405);

            SnapshotAssert.Matches(window, "camera-widget-times-" + suffix);
        });
}
