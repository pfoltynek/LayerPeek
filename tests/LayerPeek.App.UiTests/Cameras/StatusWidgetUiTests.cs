using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Localization;
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
/// The Status widget in the real camera window (T-082, design T-076 2.1): its parts and heights in S / M / L, with and
/// without a job, the emphasized percent, the dot colours of every state, automation ids and names, long printer names and
/// the longest translations trimmed inside the card, and reference snapshots.
/// </summary>
public sealed class StatusWidgetUiTests(HeadlessSession session)
{
    private const uint Grey = 0xFF606060;

    private const string CardId = OverlayWidgetViewModel.AutomationIdPrefix + OverlayWidgetIds.Status;

    private static readonly CameraWindowDefaults Normal = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };

    private static readonly Color Accent = Color.Parse("#FA6831");

    private static readonly Color Primary = Color.Parse("#F4F4F5");

    /// <summary>The registered Status widget alone (as the app registers it).</summary>
    private static OverlayWidgetRegistry Registry() =>
        new([OverlayWidgets.CreateRegistry().Find(OverlayWidgetIds.Status) ?? throw new InvalidOperationException("Status is not registered.")]);

    private static CameraOverlayLayout Layout(OverlayWidgetSize size, bool emphasized = true, OverlayAnchor anchor = OverlayAnchor.TopLeft) =>
        new([new OverlayWidgetPlacement(OverlayWidgetIds.Status, anchor, size, emphasized)]);

    private static PrinterSnapshot Printing(PrinterState state = PrinterState.Printing, double progress = 42.7) =>
        Snapshot(state, Job(progress, TimeSpan.FromMinutes(65)), printerId: "a");

    private static CameraHarness Harness(OverlayWidgetSize size, bool emphasized = true) =>
        new(window: Normal, overlay: Layout(size, emphasized), widgets: Registry());

    /// <summary>What saving the settings does for the open window: the printer's name and layout apply in place.</summary>
    private static void Save(CameraHarness harness, CameraWindow window, string name = "Alpha", CameraOverlayLayout? layout = null)
    {
        layout ??= ((CameraViewModel)window.DataContext!).Overlay.Layout;
        ((IMonitoringObserver)harness.Printers).ApplySettings(new AppSettings
        {
            Printers = [CameraHarness.CameraPrinter("a", name, Normal, layout)],
        });
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static StatusWidgetViewModel Widget(CameraWindow window) =>
        (StatusWidgetViewModel)((CameraViewModel)window.DataContext!).Overlay.Widgets.Single();

    private static Border Card(CameraWindow window) => window.Find<Border>(CardId);

    private static T Part<T>(CameraWindow window, string part)
        where T : Control => window.Find<T>(CardId + "-" + part);

    private static async Task<CameraWindow> OpenAsync(CameraHarness harness)
    {
        (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Grey);
        window.Settle();
        return window;
    }

    private static async Task ShowAsync(CameraHarness harness, CameraWindow window, PrinterSnapshot snapshot)
    {
        harness.Monitor.Raise(snapshot);
        string expected = OverlayFormat.StateName(snapshot.State);
        await Ui.UntilAsync(() => Widget(window).StateText == expected, $"{snapshot.State} shown");
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static Rect BoundsInWindow(Visual control, CameraWindow window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    // ---- Sizes and parts ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small, 46, 36)]
    [InlineData(OverlayWidgetSize.Medium, 126, 70)]
    [InlineData(OverlayWidgetSize.Large, 158, 84)]
    public Task CardHasTheDesignedPartsAndHeights(OverlayWidgetSize size, double printingHeight, double idleHeight) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(size);
            CameraWindow window = await OpenAsync(harness);
            await ShowAsync(harness, window, Printing());

            Border card = Card(window);
            Assert.Equal(printingHeight, card.Bounds.Height, 0.5);
            Assert.True(window.IsShown<TextBlock>(CardId + "-Percent"));
            Assert.True(window.IsShown<OverlayProgressBar>(CardId + "-Progress"));
            Assert.Equal("42%", Part<TextBlock>(window, "Percent").Text);
            Assert.Equal(42, Part<OverlayProgressBar>(window, "Progress").Value);
            Assert.Equal("Printing", Part<OverlayStatusPill>(window, "State").Text);
            // S has no printer name (it is in the window title); the percent sits right of the state.
            Assert.Equal(size != OverlayWidgetSize.Small, window.IsShown<TextBlock>(CardId + "-Name"));
            Rect state = BoundsInWindow(Part<OverlayStatusPill>(window, "State"), window);
            Rect percent = BoundsInWindow(Part<TextBlock>(window, "Percent"), window);
            Rect bar = BoundsInWindow(Part<OverlayProgressBar>(window, "Progress"), window);
            if (size == OverlayWidgetSize.Small)
            {
                Assert.True(percent.X >= state.Right + 8 - 0.5, $"percent {percent} right of the state {state}");
                Assert.Equal(state.Center.Y, percent.Center.Y, 0.5);
            }
            else
            {
                Assert.True(percent.Y >= state.Bottom, $"percent {percent} below the state {state}");
            }

            Assert.True(bar.Y >= percent.Bottom, $"bar {bar} below the percent {percent}");
            Assert.Equal(size switch { OverlayWidgetSize.Small => 4, OverlayWidgetSize.Medium => 6, _ => 8 }, bar.Height, 0.5);
            // Card widths (T-076 2.1): S max 240, M 180–280, L 220–340.
            (double min, double max) = size switch { OverlayWidgetSize.Small => (0, 240), OverlayWidgetSize.Medium => (180, 280), _ => (220, 340) };
            Assert.InRange(card.Bounds.Width, min - 0.5, max + 0.5);

            // Without a job only the name and the pill.
            await ShowAsync(harness, window, Snapshot(PrinterState.Idle, printerId: "a"));
            Assert.Equal(idleHeight, card.Bounds.Height, 0.5);
            Assert.False(window.IsShown<TextBlock>(CardId + "-Percent"));
            Assert.False(window.IsShown<OverlayProgressBar>(CardId + "-Progress"));
            Assert.Equal("Ready", Part<OverlayStatusPill>(window, "State").Text);
        });

    [Fact]
    public Task CardIsShownBeforeTheFirstPollAndWhileOffline() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(OverlayWidgetSize.Medium);
            CameraWindow window = await OpenAsync(harness);

            Assert.True(Card(window).IsEffectivelyVisible);
            Assert.Equal("Connecting…", Part<OverlayStatusPill>(window, "State").Text);
            Assert.Equal("Alpha", Part<TextBlock>(window, "Name").Text);

            await ShowAsync(harness, window, Snapshot(PrinterState.Offline, printerId: "a"));
            Assert.True(Card(window).IsEffectivelyVisible);
            Assert.Equal("Offline", Part<OverlayStatusPill>(window, "State").Text);
        });

    [Theory]
    [InlineData(OverlayWidgetSize.Small, 13, 15)]
    [InlineData(OverlayWidgetSize.Medium, 12, 30)]
    [InlineData(OverlayWidgetSize.Large, 13, 40)]
    public Task DrawnTextsHaveTheDesignedTypography(OverlayWidgetSize size, double stateSize, double percentSize) =>
        session.RunAsync(async scope =>
        {
            // The drawn text blocks, not the control properties: the card's text styles must not win over the pill's own
            // size and weight (T-076 1.2: pill M 12 / L 13 semi-bold, S row 13 semi-bold; name M 13 / L 16 semi-bold).
            await using CameraHarness harness = Harness(size, emphasized: false);
            CameraWindow window = await OpenAsync(harness);
            await ShowAsync(harness, window, Printing());

            TextBlock state = Part<OverlayStatusPill>(window, "State").GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(stateSize, state.FontSize);
            Assert.Equal(FontWeight.SemiBold, state.FontWeight);
            Assert.Equal(Primary, ColorOf(state.Foreground));

            TextBlock percent = Part<TextBlock>(window, "Percent");
            Assert.Equal(percentSize, percent.FontSize);
            Assert.Equal(FontWeight.Bold, percent.FontWeight);
            Assert.Equal(Primary, ColorOf(percent.Foreground));

            if (size != OverlayWidgetSize.Small)
            {
                TextBlock name = Part<TextBlock>(window, "Name");
                Assert.Equal(size == OverlayWidgetSize.Large ? 16 : 13, name.FontSize);
                Assert.Equal(FontWeight.SemiBold, name.FontWeight);
                Assert.Equal(Primary, ColorOf(name.Foreground));
            }
        });

    // ---- Emphasis ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small)]
    [InlineData(OverlayWidgetSize.Medium)]
    [InlineData(OverlayWidgetSize.Large)]
    public Task EmphasisColoursOnlyThePercent(OverlayWidgetSize size) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(size, emphasized: true);
            CameraWindow window = await OpenAsync(harness);
            await ShowAsync(harness, window, Printing());

            Assert.Equal(Accent, ColorOf(Part<TextBlock>(window, "Percent").Foreground));
            Assert.Equal(FontWeight.Bold, Part<TextBlock>(window, "Percent").FontWeight);
            Assert.Equal(Accent, ColorOf(Part<OverlayProgressBar>(window, "Progress").Foreground));
            Assert.Equal(Primary, ColorOf(Part<OverlayStatusPill>(window, "State").Foreground));

            // Saved without emphasis: the open window follows; the bar stays in the accent colour.
            Save(harness, window, layout: Layout(size, emphasized: false));

            Assert.Equal(Primary, ColorOf(Part<TextBlock>(window, "Percent").Foreground));
            Assert.Equal(Accent, ColorOf(Part<OverlayProgressBar>(window, "Progress").Foreground));
        });

    // ---- State dot ----

    [Fact]
    public Task DotHasTheTrayColourOfEveryState() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(OverlayWidgetSize.Medium);
            CameraWindow window = await OpenAsync(harness);
            Ellipse Dot() => Part<OverlayStatusPill>(window, "State").GetVisualDescendants().OfType<Ellipse>().Single();
            Assert.Equal(Color.Parse("#9E9E9E"), ColorOf(Dot().Fill));

            (PrinterState State, string Colour)[] states =
            [
                (PrinterState.Printing, "#1E88E5"),
                (PrinterState.Busy, "#1E88E5"),
                (PrinterState.Paused, "#FDD835"),
                (PrinterState.Finished, "#43A047"),
                (PrinterState.Error, "#E53935"),
                (PrinterState.Attention, "#FB8C00"),
                (PrinterState.Unauthorized, "#FB8C00"),
                (PrinterState.Idle, "#8A8D96"),
                (PrinterState.Stopped, "#8A8D96"),
                (PrinterState.Offline, "#9E9E9E"),
                (PrinterState.Unknown, "#9E9E9E"),
            ];
            foreach ((PrinterState state, string colour) in states)
            {
                await ShowAsync(harness, window, Printing(state));

                Assert.Equal(Color.Parse(colour), ColorOf(Dot().Fill));
                // The dot is never the only carrier: the pill always names the state.
                Assert.Equal(OverlayFormat.StateName(state), Part<OverlayStatusPill>(window, "State").Text);
            }
        });

    // ---- Accessibility ----

    [Fact]
    public Task ScreenReaderGetsTheNameStateAndProgress() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(OverlayWidgetSize.Medium);
            CameraWindow window = await OpenAsync(harness);
            await ShowAsync(harness, window, Printing());

            Border card = Card(window);
            Assert.Equal(CardId, AutomationProperties.GetAutomationId(card));
            Assert.Equal("Alpha: Printing, 42%", AutomationProperties.GetName(card));
            Assert.Equal("Printing", AutomationProperties.GetName(Part<OverlayStatusPill>(window, "State")));
            Assert.Equal("42%", AutomationProperties.GetName(Part<OverlayProgressBar>(window, "Progress")));
            // Every part exists once (one element tree for all sizes).
            Assert.NotNull(Part<TextBlock>(window, "Name"));
            Assert.NotNull(Part<TextBlock>(window, "Percent"));

            await ShowAsync(harness, window, Snapshot(PrinterState.Idle, printerId: "a"));
            Assert.Equal("Alpha: Ready", AutomationProperties.GetName(card));
        });

    // ---- Long texts ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small)]
    [InlineData(OverlayWidgetSize.Medium)]
    [InlineData(OverlayWidgetSize.Large)]
    public Task LongNameAndTheLongestTranslationsStayInsideTheCard(OverlayWidgetSize size) =>
        session.RunAsync(async scope =>
        {
            // Wide letters: even shortened to 40 characters the name is far wider than the card.
            await using CameraHarness harness = Harness(size);
            CameraWindow window = await OpenAsync(harness);
            Save(harness, window, name: new string('W', 60));
            (double min, double max) = size switch { OverlayWidgetSize.Small => (0, 240), OverlayWidgetSize.Medium => (180, 280), _ => (220, 340) };

            foreach (string language in AppLanguages.Supported)
            {
                Assert.True(new UiLanguage().Apply(language));
                harness.Windows.RefreshTexts();
                foreach (PrinterSnapshot snapshot in new[] { Printing(PrinterState.Attention), Printing(PrinterState.Unauthorized), Printing(PrinterState.Printing, 100) })
                {
                    await ShowAsync(harness, window, snapshot);
                    string context = $"{language}, {snapshot.State}, {size}";

                    Border card = Card(window);
                    Rect cardBounds = BoundsInWindow(card, window);
                    Assert.InRange(cardBounds.Width, min - 0.5, max + 0.5);
                    Rect inner = cardBounds.Deflate(card.Padding);
                    foreach (TextBlock text in card.GetVisualDescendants().OfType<TextBlock>().Where(static block => block.IsEffectivelyVisible))
                    {
                        Rect bounds = BoundsInWindow(text, window);
                        Assert.True(inner.Inflate(0.5).Contains(bounds), $"{context}: '{text.Text}' {bounds} outside {inner}");
                        // Trimmed instead of cut off: the laid-out text fits its box.
                        Assert.True(text.TextLayout.WidthIncludingTrailingWhitespace <= bounds.Width + 0.5, $"{context}: '{text.Text}' overflows {bounds.Width}");
                    }
                }

                if (size != OverlayWidgetSize.Small)
                {
                    TextBlock name = Part<TextBlock>(window, "Name");
                    Assert.Equal(OverlayFormat.MaxNameLength, name.Text!.Length);
                    // Shortened in the middle by width: the start and the end of the name stay.
                    Assert.True(name.TextLayout.TextLines[0].HasCollapsed, $"{language}: the name is trimmed by width");
                }
            }
        });

    [Theory]
    [InlineData(OverlayWidgetSize.Medium, "Prusa CO")]
    [InlineData(OverlayWidgetSize.Large, "Prusa CO")]
    [InlineData(OverlayWidgetSize.Medium, "Prusa C\U0001F5A8")] // a surrogate pair across the 8th character
    [InlineData(OverlayWidgetSize.Large, "Prusa C\U0001F5A8")]
    [InlineData(OverlayWidgetSize.Medium, "Prusa Cé")] // a combining accent after the 8th character
    [InlineData(OverlayWidgetSize.Large, "Prusa Cé")]
    public Task NameLongerThan40CharactersAndTheCardShowsOneEllipsisAndItsEnd(OverlayWidgetSize size, string head) =>
        session.RunAsync(async scope =>
        {
            // T-076 1.4 / 2.1: shortened in the middle (start + "…" + end), also when the 40-character cut and the width
            // trimming both apply; never "start…middle…", also when the 8th character is not a single UTF-16 code unit.
            const string Tail = "druga od okna";
            await using CameraHarness harness = Harness(size);
            CameraWindow window = await OpenAsync(harness);
            Save(harness, window, name: head + "RE One WWWWWWWWWWWWWWWWWWWWWWWWWWWWWW na piętrze – " + Tail);

            TextBlock name = Part<TextBlock>(window, "Name");
            TextLine line = Assert.Single(name.TextLayout.TextLines);
            Assert.True(line.HasCollapsed, "the name is trimmed by width");
            string drawn = string.Concat(line.TextRuns.Select(static run => run.Text.ToString()));
            Assert.Equal(1, drawn.Count(static c => c == '…'));
            Assert.StartsWith(head + "…", drawn, StringComparison.Ordinal);
            Assert.EndsWith(Tail, drawn, StringComparison.Ordinal);
        });

    /// <summary>
    /// Names whose end is drawn in several runs (emoji and CJK in a fallback font, Cyrillic, Hebrew), shorter and longer
    /// than <see cref="OverlayFormat.MaxNameLength"/>.
    /// </summary>
    public static TheoryData<OverlayWidgetSize, string> NamesWithSeveralRunsInTheEnd
    {
        get
        {
            string rockets = string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F680), 6));
            string[] names =
            [
                "Prusa CORE One 打印机打印机打印机打印机 v2",
                "Prusa CORE One remix cube 打印打印打印打印 v2",
                "Prusa CORE One in the workshop upstairs 打印机打印机 v2",
                "Prusa CORE One WWW " + rockets + " v2",
                "Prusa CORE One upstairs " + rockets + " WWWWWWW v2",
                "Prusa CORE One держатель книг на полке v2",
                "Prusa CORE One WWWW 打印 держатель WWWW 打印 v2",
                "Prusa CORE One WWWWWWWW מחזיק ספרים v2",
            ];
            TheoryData<OverlayWidgetSize, string> data = [];
            foreach (OverlayWidgetSize size in new[] { OverlayWidgetSize.Medium, OverlayWidgetSize.Large })
            {
                foreach (string name in names)
                {
                    data.Add(size, name);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(NamesWithSeveralRunsInTheEnd))]
    public Task ShortenedNameEndsWithTheRealEndOfTheName(OverlayWidgetSize size, string printerName) =>
        session.RunAsync(async scope =>
        {
            // The DRAWN line: one "…", the first 8 characters, and an end that really is the end of the name (Avalonia's own
            // leading-prefix ellipsis draws pieces of earlier runs there, see OverlayMiddleTrimming), whole characters only.
            await using CameraHarness harness = Harness(size);
            CameraWindow window = await OpenAsync(harness);
            Save(harness, window, name: printerName);

            TextBlock name = Part<TextBlock>(window, "Name");
            TextLine line = Assert.Single(name.TextLayout.TextLines);
            Assert.True(line.HasCollapsed, $"'{name.Text}' is trimmed by width");
            // In logical order (the runs of a line are in visual order, right-to-left text comes reversed).
            (string start, string end) = DrawnLine.AssertMiddleShortened(name, size.ToString());
            Assert.Equal(printerName[..OverlayFormat.NameHeadLength], start);
            Assert.EndsWith(" v2", end, StringComparison.Ordinal);
            Assert.EndsWith(end, printerName, StringComparison.Ordinal);
            if (!printerName.Any(static c => c is >= (char)0x0590 and <= (char)0x08FF))
            {
                // Left-to-right only: the runs in visual order are the same text.
                Assert.Equal(start + "…" + end, DrawnLine.Of(name));
            }
        });

    // ---- Snapshots ----

    [Fact]
    public Task SnapshotOfTheMediumCardWhilePrinting() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(OverlayWidgetSize.Medium);
            CameraWindow window = await OpenAsync(harness);
            await ShowAsync(harness, window, Printing(progress: 19.4));

            SnapshotAssert.Matches(window, "camera-widget-status-medium");
        });

    [Fact]
    public Task SnapshotOfTheSmallCardWhilePausedWithoutEmphasis() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(OverlayWidgetSize.Small, emphasized: false);
            CameraWindow window = await OpenAsync(harness);
            await ShowAsync(harness, window, Printing(PrinterState.Paused, 63));

            SnapshotAssert.Matches(window, "camera-widget-status-small");
        });

    [Fact]
    public Task SnapshotOfTheLargeCardWithALongNameInPolishWithoutAJob() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(OverlayWidgetSize.Large);
            CameraWindow window = await OpenAsync(harness);
            Save(harness, window, name: "Prusa CORE One w pracowni na piętrze – druga od okna");
            Assert.True(new UiLanguage().Apply("pl"));
            harness.Windows.RefreshTexts();
            await ShowAsync(harness, window, Snapshot(PrinterState.Unauthorized, printerId: "a"));

            SnapshotAssert.Matches(window, "camera-widget-status-large-pl");
        });
}
