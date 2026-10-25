using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
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
/// The Print name widget (T-085, design T-076 2.4) in the real camera window with the production registration: S / M / L,
/// the parts and their AutomationIds, the drawn fonts and colours, emphasis, very long names shortened in the middle (L: 2
/// lines), chips left out from the right, the longest translations without overflow, visibility after the print, and the
/// compact row prepared for the mini preview (T-090).
/// </summary>
public sealed class CameraWindowPrintNameWidgetUiTests(HeadlessSession session)
{
    private const uint Grey = 0xFF606060;

    private const string CardId = OverlayWidgetViewModel.AutomationIdPrefix + OverlayWidgetIds.PrintName;

    private const string TitleId = CardId + "-Title";

    private const string MaterialId = CardId + "-Material";

    private const string LayerHeightId = CardId + "-LayerHeight";

    private const string NozzleId = CardId + "-NozzleDiameter";

    private const string EstimateId = CardId + "-Estimate";

    private const string Foldable = "foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode";

    /// <summary>A title far longer than any card (about 230 characters) that ends with a version.</summary>
    private static readonly string LongTitle = string.Concat(Enumerable.Repeat("Articulated dragon with extra long wings ", 6)) + "remix v2";

    private static readonly CameraWindowDefaults Normal = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    public static TheoryData<string, OverlayWidgetSize> LanguagesAndSizes =>
        [.. AppLanguages.Supported.SelectMany(static language => new[] { OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large }.Select(size => (language, size)))];

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static CameraOverlayLayout Layout(OverlayWidgetSize size, bool emphasized = true, OverlayAnchor anchor = OverlayAnchor.TopCenter) =>
        new([new OverlayWidgetPlacement(OverlayWidgetIds.PrintName, anchor, size, emphasized)]);

    private static PrinterSnapshot Job(string? fileName = Foldable, PrinterState state = PrinterState.Printing) => new()
    {
        PrinterId = "a",
        State = state,
        Timestamp = Now,
        Job = new PrintJob
        {
            Id = 1,
            FileName = fileName,
            ProgressPercent = 42.7,
            Elapsed = TimeSpan.FromMinutes(130),
            Remaining = TimeSpan.FromMinutes(65),
        },
    };

    private static async Task<CameraWindow> OpenAsync(CameraHarness harness, PrinterSnapshot snapshot, double width = 960, double height = 540)
    {
        (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Grey);
        window.Width = width;
        window.Height = height;
        harness.Monitor.Raise(snapshot);
        await Ui.UntilAsync(() => Widget(window).IsShown, "Print name card shown");
        window.Settle();
        return window;
    }

    private static CameraHarness Harness(CameraOverlayLayout layout) =>
        new(window: Normal, overlay: layout, widgets: OverlayWidgets.CreateRegistry());

    private static async Task RaiseAsync(CameraHarness harness, CameraWindow window, PrinterSnapshot snapshot, Func<bool> until, string description)
    {
        harness.Monitor.Raise(snapshot);
        await Ui.UntilAsync(until, description);
        window.Settle();
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

    private static PrintNameWidgetViewModel Widget(CameraWindow window) =>
        (PrintNameWidgetViewModel)ViewModel(window).Overlay.Widgets.Single(static widget => widget.Id == OverlayWidgetIds.PrintName);

    private static Border Card(CameraWindow window) => window.Find<Border>(CardId);

    private static Rect BoundsInWindow(Visual control, Visual window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    /// <summary>A plain window with the overlay styles (parts, colours, icons) around <paramref name="content"/>.</summary>
    private static Window OverlayHost(UiTestScope scope, double width, double height, Control content)
    {
        Window host = scope.Track(new Window { Width = width, Height = height });
        // Before the content: the parts look up their themes when they enter the tree.
        host.Styles.Add(new StyleInclude(new Uri("avares://LayerPeek/")) { Source = new Uri("avares://LayerPeek/Cameras/Overlay/OverlayStyles.axaml") });
        host.Content = content;
        host.Show();
        host.Settle();
        return host;
    }

    /// <summary>The text a <see cref="TextBlock"/> draws on its line <paramref name="line"/> (after shortening).</summary>
    private static string DrawnText(TextBlock block, int line = 0) => DrawnLine.Of(block, line);

    /// <summary>The text of a part (chip) as drawn: the part's template TextBlock.</summary>
    private static TextBlock PartText(Control part) => part.GetVisualDescendants().OfType<TextBlock>().Single();

    /// <summary>A chip left out from the right lies beyond the clipped edge of its row.</summary>
    private static bool IsDrawn(OverlayChip chip) =>
        chip.IsEffectivelyVisible && chip.GetVisualParent() is OverlayChipsPanel panel && chip.Bounds.Right <= panel.Bounds.Width + 0.01;

    private static (double Min, double Max) Widths(OverlayWidgetSize size) => size switch
    {
        OverlayWidgetSize.Small => (0, 280),
        OverlayWidgetSize.Medium => (240, 420),
        _ => (300, 480),
    };

    /// <summary>
    /// The card stays in its widths and in the window; every drawn text lies inside the card; only the title may be
    /// shortened (one line in S / M, at most 2 lines in L); chips and the estimate are never cut.
    /// </summary>
    private static void AssertFits(CameraWindow window, OverlayWidgetSize size)
    {
        Border card = Card(window);
        Rect bounds = BoundsInWindow(card, window);
        (double min, double max) = Widths(size);
        Assert.InRange(bounds.Width, min - 0.5, max + 0.5);
        Assert.True(new Rect(window.ClientSize).Contains(bounds), $"card {bounds} outside {window.ClientSize}");
        TextBlock title = window.Find<TextBlock>(TitleId);
        Assert.InRange(title.TextLayout.TextLines.Count, 1, size == OverlayWidgetSize.Large ? 2 : 1);
        Assert.True(bounds.Contains(BoundsInWindow(title, window)), $"title outside the card {bounds}");
        foreach (OverlayChip chip in card.GetVisualDescendants().OfType<OverlayChip>().Where(IsDrawn))
        {
            TextBlock text = PartText(chip);
            Assert.All(text.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, $"chip '{text.Text}' is cut"));
            Assert.True(bounds.Contains(BoundsInWindow(chip, window)), $"chip '{text.Text}' outside the card {bounds}");
        }

        TextBlock estimate = window.Find<TextBlock>(EstimateId);
        if (estimate.IsEffectivelyVisible)
        {
            Assert.All(estimate.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, $"'{estimate.Text}' is cut"));
            Assert.True(bounds.Contains(BoundsInWindow(estimate, window)), $"'{estimate.Text}' outside the card {bounds}");
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
            CameraWindow window = await OpenAsync(harness, Job());

            Assert.Equal(size, Widget(window).RenderedSize);
            Assert.True(Card(window).IsEffectivelyVisible);
            Assert.Equal("Foldable book holder v1, PLA, layer height 0.2 mm, nozzle 0.4 mm, slicer estimate 2\u00A0h 51\u00A0min", Card(window).GetValue(AutomationProperties.NameProperty));
            TextBlock title = window.Find<TextBlock>(TitleId);
            Assert.Equal("Foldable book holder v1", title.Text);
            Assert.Equal("Foldable book holder v1", DrawnText(title));
            bool chips = size != OverlayWidgetSize.Small;
            Assert.Equal(chips, window.IsShown<OverlayChip>(MaterialId));
            Assert.Equal(chips, window.IsShown<OverlayChip>(LayerHeightId));
            Assert.Equal(chips, window.IsShown<OverlayChip>(NozzleId));
            Assert.Equal(size == OverlayWidgetSize.Large, window.IsShown<TextBlock>(EstimateId));
            Assert.Equal(chips, Ui.ShowsText(window, "PLA"));
            Assert.Equal(chips, Ui.ShowsText(window, "0.2 mm"));
            Assert.Equal(chips, Ui.ShowsText(window, "nozzle 0.4"));
            AssertFits(window, size);

            // T-076 1.2 / 2.4, as drawn: title 13 / 14 / 16 semi-bold.
            Assert.Equal(size switch { OverlayWidgetSize.Small => 13, OverlayWidgetSize.Medium => 14, _ => 16 }, title.FontSize);
            Assert.Equal(FontWeight.SemiBold, title.FontWeight);
            if (chips)
            {
                // Chips 20 / 22 high with 11 / 12 semi-bold #E4E4E7 text on white 10 %.
                OverlayChip material = window.Find<OverlayChip>(MaterialId);
                Assert.Equal(size == OverlayWidgetSize.Large ? 22 : 20, material.Bounds.Height, 0.5);
                TextBlock chipText = PartText(material);
                Assert.Equal(size == OverlayWidgetSize.Large ? 12 : 11, chipText.FontSize);
                Assert.Equal(FontWeight.SemiBold, chipText.FontWeight);
                Assert.Equal(Color.Parse("#E4E4E7"), ((ISolidColorBrush)chipText.Foreground!).Color);
                Assert.Equal(Color.Parse("#1AFFFFFF"), ((ISolidColorBrush)material.Background!).Color);
                // The layer height chip has the Layer icon, 12 DIP.
                OverlayIcon layer = window.Find<OverlayChip>(LayerHeightId).GetVisualDescendants().OfType<OverlayIcon>().Single();
                Assert.True(layer.IsEffectivelyVisible);
                Assert.Same(window.FindResource("IconLayer20Regular"), layer.Data);
                Assert.Equal(12, layer.Bounds.Width, 0.5);
                Assert.False(window.Find<OverlayChip>(NozzleId).GetVisualDescendants().OfType<OverlayIcon>().Single().IsEffectivelyVisible);
            }

            if (size == OverlayWidgetSize.Large)
            {
                // The estimate 13 secondary, not the 15 of the large card's rows.
                TextBlock estimate = window.Find<TextBlock>(EstimateId);
                Assert.Equal("slicer estimate 2\u00A0h 51\u00A0min", estimate.Text);
                Assert.Equal(13, estimate.FontSize);
                Assert.Equal(Color.Parse("#C9C9CE"), ((ISolidColorBrush)estimate.Foreground!).Color);
                Assert.Contains(estimate.FontFeatures!, static feature => feature.Tag == "tnum");
            }
        });

    [Theory]
    [InlineData(OverlayWidgetSize.Small, 36)]
    [InlineData(OverlayWidgetSize.Medium, 68)]
    [InlineData(OverlayWidgetSize.Large, 102)]
    public Task CardHasTheDesignedHeight(OverlayWidgetSize size, double height) =>
        session.RunAsync(async scope =>
        {
            // T-076 2.4: S 36 (8 + 20 + 8), M 68 (12 + 20 + 4 + 20 + 12), L 102 (14 + 22 + 6 + 22 + 6 + 18 + 14).
            await using CameraHarness harness = Harness(Layout(size));
            CameraWindow window = await OpenAsync(harness, Job());

            Assert.Equal(height, Card(window).Bounds.Height, 0.5);
        });

    [Fact]
    public Task SmallCardIsTheCubeAndTheTitle() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Small));
            CameraWindow window = await OpenAsync(harness, Job());

            OverlayIcon cube = Card(window).GetVisualDescendants().OfType<OverlayIcon>().Single(static icon => icon.IsEffectivelyVisible);
            Assert.Same(window.FindResource("IconCube16Regular"), cube.Data);
            Assert.Equal(Color.Parse("#C9C9CE"), ((ISolidColorBrush)cube.Foreground!).Color);
            Rect icon = BoundsInWindow(cube, window);
            Rect title = BoundsInWindow(window.Find<TextBlock>(TitleId), window);
            Assert.Equal(icon.Right + 6, title.X, 0.5);
        });

    [Fact]
    public Task NameWithoutTheTemplateHasNoChipRow() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Large));
            CameraWindow window = await OpenAsync(harness, Job("BENCHY~1.BGC"));

            Assert.Equal("BENCHY~1", window.Find<TextBlock>(TitleId).Text);
            Assert.False(window.IsShown<OverlayChip>(MaterialId));
            Assert.False(window.IsShown<TextBlock>(EstimateId));
            // Only the title: 14 + 22 + 14.
            Assert.Equal(50, Card(window).Bounds.Height, 0.5);
            Assert.Equal("BENCHY~1", Card(window).GetValue(AutomationProperties.NameProperty));
            AssertFits(window, OverlayWidgetSize.Large);
        });

    [Fact]
    public Task NameWithoutAnEstimateLeavesOutItsRow() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Large));
            CameraWindow window = await OpenAsync(harness, Job("vase_0.2mm_PLA_XL+.bgcode"));

            Assert.True(window.IsShown<OverlayChip>(MaterialId));
            Assert.True(window.IsShown<OverlayChip>(LayerHeightId));
            Assert.False(window.IsShown<OverlayChip>(NozzleId));
            Assert.False(window.IsShown<TextBlock>(EstimateId));
            Assert.Equal(78, Card(window).Bounds.Height, 0.5);
        });

    [Fact]
    public Task SavedSizeChangesTheOpenCard() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Job());
            Assert.True(window.IsShown<OverlayChip>(MaterialId));

            Save(harness, Layout(OverlayWidgetSize.Large));
            Assert.True(window.IsShown<TextBlock>(EstimateId));

            Save(harness, Layout(OverlayWidgetSize.Small));
            Assert.Equal(36, Card(window).Bounds.Height, 0.5);
            Assert.False(window.IsShown<OverlayChip>(MaterialId));
        });

    // ---- When it is shown ----

    [Fact]
    public Task CardStaysAfterThePrintUntilTheNextOne() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Job());

            harness.Monitor.Raise(Job(state: PrinterState.Finished));
            window.Settle();
            Assert.True(Card(window).IsEffectivelyVisible);
            Assert.Equal("Foldable book holder v1", window.Find<TextBlock>(TitleId).Text);

            await RaiseAsync(harness, window, Job("shape-box_0.4n_0.15mm_PETG_MK4S_45m.gcode"), () => Widget(window).Title == "Shape box", "next print shown");
            Assert.Equal("Shape box", window.Find<TextBlock>(TitleId).Text);
            Assert.Equal("PETG", window.Find<OverlayChip>(MaterialId).Text);
        });

    [Theory]
    [InlineData(PrinterState.Idle)]
    [InlineData(PrinterState.Offline)]
    public Task CardDisappearsWithoutAJob(PrinterState state) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Job(state: PrinterState.Stopped));

            await RaiseAsync(harness, window, Job(state: state) with { Job = null }, () => !Widget(window).IsShown, "Print name card hidden");

            Assert.False(Card(window).IsEffectivelyVisible);
        });

    // ---- Look ----

    [Fact]
    public Task EmphasisDrawsTheTitleInTheAccentColour() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Large, emphasized: true));
            CameraWindow window = await OpenAsync(harness, Job());

            Assert.Equal(Color.Parse("#FA6831"), ((ISolidColorBrush)window.Find<TextBlock>(TitleId).Foreground!).Color);
            // Only the key value: the chips and the estimate keep their colours (as drawn, not only the part's property).
            Assert.Equal(Color.Parse("#E4E4E7"), ((ISolidColorBrush)PartText(window.Find<OverlayChip>(MaterialId)).Foreground!).Color);
            Assert.Equal(Color.Parse("#C9C9CE"), ((ISolidColorBrush)window.Find<TextBlock>(EstimateId).Foreground!).Color);

            Save(harness, Layout(OverlayWidgetSize.Large, emphasized: false));

            Assert.Equal(Color.Parse("#F4F4F5"), ((ISolidColorBrush)window.Find<TextBlock>(TitleId).Foreground!).Color);
        });

    // ---- Long names ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small)]
    [InlineData(OverlayWidgetSize.Medium)]
    public Task VeryLongTitleIsShortenedInTheMiddle(OverlayWidgetSize size) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(size));
            CameraWindow window = await OpenAsync(harness, Job(LongTitle.Replace(' ', '_') + "_0.4n_0.2mm_PLA_MK4S_1h0m.bgcode"));

            TextBlock title = window.Find<TextBlock>(TitleId);
            Assert.Equal(LongTitle, title.Text);
            Assert.Single(title.TextLayout.TextLines);
            Assert.True(title.TextLayout.TextLines[0].HasCollapsed);
            // The start, "…" and the end with the version (T-036 rule: start + … + end).
            string drawn = DrawnText(title);
            Assert.StartsWith(LongTitle[..OverlayMiddleTrimming.DefaultPrefixLength], drawn, StringComparison.Ordinal);
            Assert.Contains('…', drawn);
            Assert.EndsWith("remix v2", drawn, StringComparison.Ordinal);
            Assert.True(drawn.Length < LongTitle.Length);
            // The card takes its widest width (less what the shortened line leaves free, under one character); the screen
            // reader gets the whole name.
            Assert.InRange(Card(window).Bounds.Width, Widths(size).Max - 12, Widths(size).Max + 0.5);
            Assert.StartsWith(LongTitle + ", PLA", (string?)Card(window).GetValue(AutomationProperties.NameProperty), StringComparison.Ordinal);
            AssertFits(window, size);
        });

    [Fact]
    public Task VeryLongTitleTakesTwoLinesInLarge() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Large));
            CameraWindow window = await OpenAsync(harness, Job(LongTitle.Replace(' ', '_') + "_0.4n_0.2mm_PLA_MK4S_1h0m.bgcode"));

            TextBlock title = window.Find<TextBlock>(TitleId);
            Assert.Equal(2, title.TextLayout.TextLines.Count);
            Assert.False(title.TextLayout.TextLines[0].HasCollapsed);
            Assert.True(title.TextLayout.TextLines[1].HasCollapsed);
            Assert.EndsWith("…", DrawnText(title, 1), StringComparison.Ordinal);
            // 14 + 2 × 22 + 6 + 22 + 6 + 18 + 14.
            Assert.Equal(124, Card(window).Bounds.Height, 0.5);
            AssertFits(window, OverlayWidgetSize.Large);
        });

    [Fact]
    public Task TitleNarrowerThanItsPrefixIsCutAtTheEnd() =>
        session.RunAsync(async scope =>
        {
            // A single word longer than the card: the middle shortening degrades to an end cut, never past the card.
            string word = new('W', 80);
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Job(word + ".gcode"));

            TextBlock title = window.Find<TextBlock>(TitleId);
            Assert.True(title.TextLayout.TextLines[0].HasCollapsed);
            Assert.Contains('…', DrawnText(title));
            AssertFits(window, OverlayWidgetSize.Medium);
        });

    /// <summary>
    /// Titles with characters drawn in another font (emoji, CJK: the line has several runs) or made of several UTF-16 code
    /// units (surrogate pairs, combining accents, ZWJ emoji sequences), across the 14th code unit of the kept start and
    /// along the kept end.
    /// </summary>
    public static TheoryData<string> TitlesWithLongCharacters
    {
        get
        {
            string grin = char.ConvertFromUtf32(0x1F600);
            string rocket = char.ConvertFromUtf32(0x1F680);
            string accented = "e" + (char)0x0301;
            string zwj = ((char)0x200D).ToString();
            string family = string.Join(zwj, char.ConvertFromUtf32(0x1F468), char.ConvertFromUtf32(0x1F469), char.ConvertFromUtf32(0x1F467));
            string rockets = string.Concat(Enumerable.Repeat(rocket, 6));
            return
            [
                "Benchy remix calibration cube with a very long tail of plain words v2",
                // An emoji whose surrogate pair spans code units 13 and 14, and more of them in the end.
                "Benchy remix " + grin + " calibration cube " + rockets + " v2",
                // Emoji only in the end.
                "Benchy remix calibration cube " + rockets + " v2",
                // An emoji in the start before code unit 14, a plain end.
                "Benchy " + grin + " remix calibration cube with a long plain tail v2",
                // A letter with a combining accent spanning code units 13 and 14; combining accents in the end.
                "Benchy remix " + accented + " calibration cube " + string.Concat(Enumerable.Repeat(accented, 9)) + " v2",
                // A ZWJ family emoji (11 code units) from code unit 12; ZWJ sequences in the end.
                "Benchy remix" + family + zwj + char.ConvertFromUtf32(0x1F466) + " calibration cube " + family + family + family + " v2",
                // Cyrillic and CJK in the end.
                "Benchy remix calibration cube держатель книг v2",
                "Benchy remix calibration cube 打印打印打印打印 v2",
                // CJK in the start (drawn in a fallback font).
                "\u6253\u5370 Benchy remix calibration cube with a long plain tail v2",
                // Right-to-left text (Hebrew, Arabic) in the end, in the start and in the whole title.
                "Benchy remix calibration cube \u05de\u05d7\u05d6\u05d9\u05e7 \u05e1\u05e4\u05e8\u05d9\u05dd \u05de\u05ea\u05e7\u05e4\u05dc v2",
                "\u05de\u05d7\u05d6\u05d9\u05e7 \u05e1\u05e4\u05e8\u05d9\u05dd Benchy remix calibration cube with a long plain tail v2",
                "\u062d\u0627\u0645\u0644 \u0643\u062a\u0628 \u0642\u0627\u0628\u0644 \u0644\u0644\u0637\u064a \u0645\u0639 \u063a\u0637\u0627\u0621 \u0637\u0648\u064a\u0644 \u062c\u062f\u0627 v2",
            ];
        }
    }

    /// <summary>
    /// Reads the DRAWN line at many widths: exactly one "…", a start and an end made of whole characters of the title, the
    /// end really being the end of the title (Avalonia's own leading-prefix ellipsis draws pieces of several runs there,
    /// see <see cref="OverlayMiddleTrimming"/>), and the version kept once the line is wide enough.
    /// </summary>
    [Theory]
    [MemberData(nameof(TitlesWithLongCharacters))]
    public Task MiddleShorteningKeepsWholeCharacters(string text) =>
        session.RunAsync(scope =>
        {
            TextBlock title = new()
            {
                Text = text,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = OverlayMiddleTrimming.Middle,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            };
            Window host = OverlayHost(scope, 600, 60, title);
            string[] characters = DrawnLine.TextElements(text);
            bool rightToLeft = text.Any(static c => c is >= (char)0x0590 and <= (char)0x08FF);
            int collapsed = 0;
            for (double width = 40; width <= 420; width += 3)
            {
                title.Width = width;
                host.Settle();
                if (!title.TextLayout.TextLines[0].HasCollapsed)
                {
                    continue;
                }

                collapsed++;
                // In logical order (also right-to-left text): one "…", a start and an end of whole characters, the end
                // really being the end of the title.
                (_, string end) = DrawnLine.AssertMiddleShortened(title, $"{width}");
                if (width >= 200)
                {
                    Assert.EndsWith(" v2", end, StringComparison.Ordinal);
                }

                if (!rightToLeft)
                {
                    // Left-to-right only: the runs in visual order are the same text.
                    string drawn = DrawnText(title);
                    Assert.True(drawn.Count(static c => c == '…') == 1, $"'{drawn}' at {width}: not exactly one '…'");
                    int ellipsis = drawn.IndexOf('…', StringComparison.Ordinal);
                    Assert.True(DrawnLine.IsWholeCharacterPrefix(characters, drawn[..ellipsis]), $"'{drawn}' at {width}: the start is not whole characters of the title");
                    Assert.True(DrawnLine.IsWholeCharacterSuffix(characters, drawn[(ellipsis + 1)..]), $"'{drawn}' at {width}: the end is not whole characters of the title's end");
                }
            }

            Assert.True(collapsed > 10, $"only {collapsed} widths shortened the title");
            return Task.CompletedTask;
        });

    [Fact]
    public Task ChipsThatDoNotFitAreLeftOutFromTheRight() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Job("x_0.4n_0.2mm_PLA,PETG,PVA,ASA,TPU_XL5IS_1h0m.bgcode"));
            PrintNameWidgetViewModel widget = Widget(window);

            // The same chips in a row of a given width (the card's row is at least 212 wide and fits all three).
            OverlayChipsPanel row = new() { Spacing = 4, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            OverlayChip material = new() { Text = widget.MaterialChip };
            OverlayChip layer = new() { Text = widget.LayerHeightChip, Icon = (Geometry)window.FindResource("IconLayer20Regular")!, IconCanvasSize = 20 };
            OverlayChip nozzle = new() { Text = widget.NozzleChip };
            row.Children.AddRange([material, layer, nozzle]);
            Window host = OverlayHost(scope, 400, 60, row);
            host.Settle();
            double all = nozzle.Bounds.Right;
            Assert.Equal(3, row.ShownCount);
            Assert.Equal(all, row.Bounds.Width, 0.5);

            List<int> shown = [];
            foreach (double width in new[] { all, all - 1, layer.Bounds.Right, layer.Bounds.Right - 1, material.Bounds.Right, material.Bounds.Right - 1 })
            {
                row.Width = width;
                host.Settle();
                shown.Add(row.ShownCount);
                Assert.True(row.ClipToBounds);
                // Drawn chips are whole and inside the row; the others lie beyond its clipped right edge.
                foreach (OverlayChip chip in new[] { material, layer, nozzle }.Take(row.ShownCount))
                {
                    Assert.True(chip.Bounds.Right <= width + 0.01, $"{chip.Text} at {chip.Bounds} in {width}");
                    Assert.False(PartText(chip).TextLayout.TextLines[0].HasCollapsed);
                }

                foreach (OverlayChip chip in new[] { material, layer, nozzle }.Skip(row.ShownCount))
                {
                    Assert.True(chip.Bounds.X >= width, $"{chip.Text} at {chip.Bounds} in {width}");
                }

                // Screen readers get only the drawn chips (the card's name says everything anyway).
                Assert.Equal(
                    [.. new[] { material, layer, nozzle }.Take(row.ShownCount).Select(ControlAutomationPeer.CreatePeerForElement)],
                    ControlAutomationPeer.CreatePeerForElement(row).GetChildren());
            }

            // Nozzle first, then the layer height, then the material.
            Assert.Equal([3, 2, 2, 1, 1, 0], shown);

            // Hidden chips take no room.
            row.Width = double.NaN;
            layer.IsVisible = false;
            host.Settle();
            Assert.Equal(2, row.ShownCount);
            Assert.Equal(material.Bounds.Right + 4, nozzle.Bounds.X, 0.5);
            Assert.Equal([ControlAutomationPeer.CreatePeerForElement(material), ControlAutomationPeer.CreatePeerForElement(nozzle)], ControlAutomationPeer.CreatePeerForElement(row).GetChildren());
        });

    // ---- Languages ----

    /// <summary>
    /// The longest texts in every language and size: a long title, all chips with several materials and a slicer estimate of
    /// days (the longest estimate text).
    /// </summary>
    [Theory]
    [MemberData(nameof(LanguagesAndSizes))]
    public Task LongestTextsFitInEveryLanguageAndSize(string language, OverlayWidgetSize size) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            await using CameraHarness harness = Harness(Layout(size));
            CameraWindow window = await OpenAsync(harness, Job(LongTitle.Replace(' ', '_') + "_0.25n_0.075mm_PLA-CF,PETG-CF,ASA_XL5IS_4d23h59m.bgcode"));

            Assert.True(Widget(window).HasEstimate);
            AssertFits(window, size);
            if (size != OverlayWidgetSize.Small)
            {
                // Every chip of the longest language fits in the row of the card.
                Assert.Equal(3, Card(window).GetVisualDescendants().OfType<OverlayChip>().Count(IsDrawn));
            }
        });

    [Theory]
    [MemberData(nameof(Languages))]
    public Task CardFollowsALanguageSwitch(string language) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Large));
            CameraWindow window = await OpenAsync(harness, Job());

            Assert.True(new UiLanguage().Apply(language));
            ViewModel(window).RefreshTexts();
            window.Settle();

            string nozzle = PrintNameFormat.NozzleChip(0.4);
            Assert.Equal(nozzle, window.Find<OverlayChip>(NozzleId).Text);
            Assert.True(Ui.ShowsText(window, nozzle), nozzle);
            Assert.Equal(PrintNameFormat.Estimate(TimeSpan.FromMinutes(171)), window.Find<TextBlock>(EstimateId).Text);
            Assert.Equal(PrintNameFormat.AccessibleText("Foldable book holder v1", PrintFileNameInfo.Parse(Foldable)), Card(window).GetValue(AutomationProperties.NameProperty));
            Assert.Contains(Strings.CameraOverlayLayerHeightName.Replace("{0}", PrintNameFormat.Number(0.2), StringComparison.Ordinal), (string?)Card(window).GetValue(AutomationProperties.NameProperty), StringComparison.Ordinal);
            AssertFits(window, OverlayWidgetSize.Large);
        });

    // ---- Compact row (T-090) ----

    [Fact]
    public Task CompactRowShowsTheTitleAndTheFirstChips() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(Layout(OverlayWidgetSize.Medium));
            CameraWindow window = await OpenAsync(harness, Job());
            Window host = OverlayHost(scope, 320, 34, new ContentControl { Content = Widget(window), ContentTemplate = PrintNameCompactView.RowTemplate, Margin = new Thickness(10, 0) });
            host.Background = new SolidColorBrush(Color.Parse("#141416"));
            host.Settle();
            TextBlock materialText = PartText(host.GetVisualDescendants().OfType<OverlayChip>().First());
            Assert.Equal("PLA", materialText.Text);
            Assert.Equal(11, materialText.FontSize);
            Assert.Equal(Color.Parse("#E4E4E7"), ((ISolidColorBrush)materialText.Foreground!).Color);

            PrintNameCompactView row = host.GetVisualDescendants().OfType<PrintNameCompactView>().Single();
            TextBlock title = row.GetVisualDescendants().OfType<TextBlock>().First(static block => block.Text == "Foldable book holder v1");
            Assert.Equal(13, title.FontSize);
            Assert.Equal(FontWeight.SemiBold, title.FontWeight);
            Assert.Equal(Color.Parse("#F4F4F5"), ((ISolidColorBrush)title.Foreground!).Color);
            OverlayChip[] chips = [.. row.GetVisualDescendants().OfType<OverlayChip>()];
            Assert.Equal(["PLA", "0.2 mm"], chips.Select(static chip => chip.Text));
            Assert.All(chips, static chip => Assert.Equal(18, chip.Bounds.Height, 0.5));
            Assert.True(BoundsInWindow(row, host).Right <= host.ClientSize.Width);

            // A long title is shortened in the middle and leaves the chips room.
            await RaiseAsync(harness, window, Job(LongTitle.Replace(' ', '_') + "_0.4n_0.2mm_PLA_MK4S_1h0m.bgcode"), () => Widget(window).Title == LongTitle, "long title shown");
            host.Settle();
            TextBlock longTitle = row.GetVisualDescendants().OfType<TextBlock>().First(static block => block.Text == LongTitle);
            Assert.True(longTitle.TextLayout.TextLines[0].HasCollapsed);
            Assert.EndsWith("remix v2", DrawnText(longTitle), StringComparison.Ordinal);
            Assert.Equal(2, row.GetVisualDescendants().OfType<OverlayChipsPanel>().Single().ShownCount);
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
            CameraWindow window = await OpenAsync(harness, Job(), 720, 405);

            SnapshotAssert.Matches(window, "camera-widget-print-name-" + suffix);
        });
}
