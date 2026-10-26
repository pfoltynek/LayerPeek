using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Localization;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Cameras.Overlay;
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
/// The widget overlay of the real camera window (T-081, design T-076) with the test widget: cards in the six anchors of
/// the normal window and full screen (none in the mini preview), stacking from the window edge inward, clear of the window
/// buttons, the global card opacity, emphasis in the accent colour, tabular figures, the small-window rules, and saved
/// settings that change an open window in place.
/// </summary>
public sealed class CameraWindowOverlayUiTests(HeadlessSession session)
{
    private const uint Grey = 0xFF606060;

    /// <summary>Distance of the anchors from the edges of the client area.</summary>
    private const double Margin = 12;

    private static readonly CameraWindowDefaults Normal = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };

    /// <summary>One test widget per anchor (test-a top left … test-f bottom right), test-a emphasized.</summary>
    private static readonly CameraOverlayLayout AllAnchors = new(
        TestOverlayWidgets.Ids.Select((id, index) => new OverlayWidgetPlacement(id, OverlayAnchors.All[index], emphasized: index == 0)));

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static PrinterSnapshot Printing(double progress = 42.7) => new()
    {
        PrinterId = "a",
        State = PrinterState.Printing,
        Timestamp = Now,
        Job = new PrintJob { FileName = "benchy.bgcode", ProgressPercent = progress, Elapsed = TimeSpan.FromMinutes(130), Remaining = TimeSpan.FromMinutes(65) },
    };

    private static void Run(CameraWindow window, Action action)
    {
        action();
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static async Task<CameraWindow> OpenPrintingAsync(CameraHarness harness, double width = 960, double height = 540, double progress = 42.7)
    {
        (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Grey);
        if (ViewModel(window).IsNormal)
        {
            window.Width = width;
            window.Height = height;
        }

        harness.Monitor.Raise(Printing(progress));
        await Ui.UntilAsync(() => ViewModel(window).Overlay.Widgets.All(static widget => widget.IsShown) && Cards(window).Count > 0, "cards shown");
        window.Settle();
        return window;
    }

    private static void Save(CameraHarness harness, CameraOverlayLayout layout, int opacity = CameraOverlayOpacity.DefaultPercent, CameraWindowDefaults? window = null)
    {
        // What saving the settings does for an open window: the monitoring session applies the new settings.
        ((IMonitoringObserver)harness.Printers).ApplySettings(new AppSettings
        {
            Printers = [CameraHarness.CameraPrinter("a", "Alpha", window ?? Normal, layout), CameraHarness.CameraPrinter("b", "Beta")],
            CameraOverlayOpacityPercent = opacity,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static Rect BoundsInWindow(Control control, CameraWindow window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    /// <summary>The drawn cards (inside the client area), by widget id.</summary>
    private static Dictionary<string, Border> Cards(CameraWindow window) =>
        window.GetVisualDescendants().OfType<Border>()
            .Where(static border => border.Classes.Contains("overlayCard") && border.IsEffectivelyVisible)
            .Where(border => new Rect(window.ClientSize).Contains(BoundsInWindow(border, window)))
            .ToDictionary(static border => ((OverlayWidgetViewModel)border.DataContext!).Id);

    private static Border Card(CameraWindow window, string id) => window.Find<Border>(OverlayWidgetViewModel.AutomationIdPrefix + id);

    private static StackPanel WindowButtons(CameraWindow window) => window.Find<StackPanel>("CameraWindowButtons");

    private static TextBlock Value(Border card) => card.GetVisualDescendants().OfType<TextBlock>().Single(static block => block.Name == "Value");

    /// <summary>Asserts the place of every card of <see cref="AllAnchors"/> in <paramref name="window"/>.</summary>
    private static void AssertAllAnchors(CameraWindow window)
    {
        Size client = window.ClientSize;
        Dictionary<string, Border> cards = Cards(window);
        Assert.Equal(6, cards.Count);
        foreach ((string id, int index) in TestOverlayWidgets.Ids.Select(static (id, index) => (id, index)))
        {
            OverlayAnchor anchor = OverlayAnchors.All[index];
            Rect card = BoundsInWindow(cards[id], window);
            string context = $"{anchor} in {client}: {card}";

            // The card is inside the anchor element with its AutomationId.
            ItemsControl anchorItems = window.Find<ItemsControl>("CameraOverlayAnchor" + anchor);
            Assert.True(BoundsInWindow(anchorItems, window).Contains(card), context);

            if (OverlayAnchors.IsLeft(anchor))
            {
                Assert.True(Math.Abs(card.X - Margin) < 0.5, context);
            }
            else if (OverlayAnchors.IsRight(anchor))
            {
                Assert.True(Math.Abs(client.Width - Margin - card.Right) < 0.5, context);
            }
            else
            {
                Assert.True(Math.Abs((card.X + (card.Width / 2)) - (client.Width / 2)) < 0.5, context);
            }

            double expectedTop = anchor == OverlayAnchor.TopRight ? 44 : Margin;
            Assert.True(OverlayAnchors.IsTop(anchor) ? Math.Abs(card.Y - expectedTop) < 0.5 : Math.Abs(client.Height - Margin - card.Bottom) < 0.5, context);
        }
    }

    // ---- Anchors ----

    [Fact]
    public Task CardsSitInAllSixAnchorsOfTheNormalWindowAndOfFullScreen() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: Normal, overlay: AllAnchors);
            CameraWindow window = await OpenPrintingAsync(harness);

            AssertAllAnchors(window);
            Assert.Equal("CameraOverlayWidget-test-a", Card(window, "test-a").GetValue(Avalonia.Automation.AutomationProperties.AutomationIdProperty));
            Assert.Equal("Printing, 42%", Card(window, "test-a").GetValue(Avalonia.Automation.AutomationProperties.NameProperty));

            Run(window, () => ViewModel(window).ToggleFullScreenCommand.Execute(null));
            Assert.True(ViewModel(window).IsFullScreen);
            // Full screen shares the layout of the normal window.
            AssertAllAnchors(window);
        });

    /// <summary>
    /// T-093 (found by the E2E test on the real window): a card is an element of the UI Automation content view with its
    /// AutomationId and its full name (T-076 3.1 / 5.7), not only its parts; a plain border has no peer in that view, so
    /// screen readers and UI Automation never saw the card.
    /// </summary>
    [Fact]
    public Task CardsAreUiAutomationElementsWithTheirFullName() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: Normal, overlay: AllAnchors);
            CameraWindow window = await OpenPrintingAsync(harness);

            Avalonia.Automation.Peers.AutomationPeer peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(Card(window, "test-a"));
            Assert.True(peer.IsContentElement(), "The card is not in the content view of UI Automation.");
            Assert.True(peer.IsControlElement(), "The card is not in the control view of UI Automation.");
            Assert.Equal("CameraOverlayWidget-test-a", peer.GetAutomationId());
            Assert.Equal("Printing, 42%", peer.GetName());
        });

    [Fact]
    public Task MiniPreviewDrawsNoCards() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: Normal, overlay: AllAnchors);
            CameraWindow window = await OpenPrintingAsync(harness);

            Run(window, () => ViewModel(window).ShowMiniCommand.Execute(null));

            Assert.True(ViewModel(window).IsMini);
            Assert.False(window.Find<Panel>("CameraOverlay").IsEffectivelyVisible);
            Assert.Empty(Cards(window));

            Run(window, () => ViewModel(window).ShowNormalCommand.Execute(null));
            Assert.Equal(6, Cards(window).Count);
        });

    [Fact]
    public Task WindowOpeningInTheMiniPreviewDrawsNoCards() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini }, overlay: AllAnchors);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Grey);
            harness.Monitor.Raise(Printing());
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.True(ViewModel(window).IsMini);
            Assert.Empty(Cards(window));
        });

    [Fact]
    public Task TopAnchorsKeepClearOfTheWindowButtons() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: Normal, overlay: AllAnchors);
            CameraWindow window = await OpenPrintingAsync(harness);
            Rect buttons = BoundsInWindow(WindowButtons(window), window);

            Rect topRight = BoundsInWindow(Card(window, "test-c"), window);
            Rect topCenter = BoundsInWindow(Card(window, "test-b"), window);

            Assert.True(topRight.Y >= buttons.Bottom + 8, $"top right card {topRight}, buttons {buttons}");
            Assert.False(topRight.Intersects(buttons));
            Assert.False(topCenter.Intersects(buttons));
            // The buttons are drawn above the cards.
            Panel root = (Panel)WindowButtons(window).GetVisualParent()!;
            Assert.True(root.Children.IndexOf(WindowButtons(window)) > root.Children.IndexOf(window.Find<Panel>("CameraOverlay")));
        });

    [Fact]
    public Task CardsStackFromTheWindowEdgeInward() =>
        session.RunAsync(async scope =>
        {
            CameraOverlayLayout layout = new(
            [
                new OverlayWidgetPlacement("test-a", OverlayAnchor.TopLeft, OverlayWidgetSize.Small),
                new OverlayWidgetPlacement("test-b", OverlayAnchor.TopLeft, OverlayWidgetSize.Small),
                new OverlayWidgetPlacement("test-c", OverlayAnchor.BottomRight, OverlayWidgetSize.Small),
                new OverlayWidgetPlacement("test-d", OverlayAnchor.BottomRight, OverlayWidgetSize.Medium),
            ]);
            await using CameraHarness harness = new(window: Normal, overlay: layout);
            CameraWindow window = await OpenPrintingAsync(harness);
            Size client = window.ClientSize;

            Rect a = BoundsInWindow(Card(window, "test-a"), window);
            Rect b = BoundsInWindow(Card(window, "test-b"), window);
            Rect c = BoundsInWindow(Card(window, "test-c"), window);
            Rect d = BoundsInWindow(Card(window, "test-d"), window);

            // Top: the first card at the edge, the next below it, 8 apart, all aligned left.
            Assert.Equal(Margin, a.Y, 0.5);
            Assert.Equal(a.Bottom + 8, b.Y, 0.5);
            Assert.Equal(a.X, b.X, 0.5);
            // Bottom: the first card at the bottom edge, the next above it; right anchors align the cards right.
            Assert.Equal(client.Height - Margin, c.Bottom, 0.5);
            Assert.Equal(c.Y - 8, d.Bottom, 0.5);
            Assert.Equal(client.Width - Margin, c.Right, 0.5);
            Assert.Equal(client.Width - Margin, d.Right, 0.5);
            Assert.NotEqual(c.Width, d.Width);
            // Text inside cards of right anchors stays aligned left (T-076 3.1).
            Assert.All(Card(window, "test-d").GetVisualDescendants().OfType<TextBlock>(), static block => Assert.Contains(block.TextAlignment, new[] { TextAlignment.Left, TextAlignment.Start }));
        });

    [Fact]
    public Task WidgetWithoutDataTakesNoRoomInItsAnchor() =>
        session.RunAsync(async scope =>
        {
            OverlayWidgetRegistry registry = new(
            [
                TestOverlayWidgets.Definition("test-a", create: _ => new TestOverlayWidget(static _ => default)),
                TestOverlayWidgets.Definition("test-b"),
            ]);
            CameraOverlayLayout layout = new([new("test-a", OverlayAnchor.TopLeft), new("test-b", OverlayAnchor.TopLeft)]);
            await using CameraHarness harness = new(window: Normal, overlay: layout, widgets: registry);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Grey);
            window.Width = 960;
            window.Height = 540;
            harness.Monitor.Raise(Printing());
            await Ui.UntilAsync(() => Widget(window, "test-b").Value == "42%", "card shown");
            window.Settle();

            Assert.False(Card(window, "test-a").IsEffectivelyVisible);
            Assert.Equal(Margin, BoundsInWindow(Card(window, "test-b"), window).Y, 0.5);
        });

    // ---- Look ----

    [Theory]
    [InlineData(30, 77)]
    [InlineData(72, 184)]
    [InlineData(95, 242)]
    public Task CardBackgroundHasTheGlobalOpacityAndFollowsItsChange(int percent, byte alpha) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: Normal, overlay: AllAnchors, opacity: percent);
            CameraWindow window = await OpenPrintingAsync(harness);

            Assert.All(Cards(window).Values, card => Assert.Equal(Color.FromArgb(alpha, 0x0E, 0x0E, 0x10), ((ISolidColorBrush)card.Background!).Color));

            int other = percent == 72 ? 30 : 72;
            Save(harness, AllAnchors, other);
            window.Settle();

            byte otherAlpha = (byte)Math.Round(other * 2.55, MidpointRounding.AwayFromZero);
            Assert.All(Cards(window).Values, card => Assert.Equal(otherAlpha, ((ISolidColorBrush)card.Background!).Color.A));
            // The text stays opaque: the opacity is the background's only.
            Assert.Equal(1, Value(Card(window, "test-a")).Opacity);
        });

    [Fact]
    public Task EmphasisDrawsTheKeyValueInTheAccentColour() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: Normal, overlay: AllAnchors);
            CameraWindow window = await OpenPrintingAsync(harness);

            Assert.Equal(Color.Parse("#FA6831"), ((ISolidColorBrush)Value(Card(window, "test-a")).Foreground!).Color);
            Assert.Equal(Color.Parse("#F4F4F5"), ((ISolidColorBrush)Value(Card(window, "test-b")).Foreground!).Color);

            Save(harness, AllAnchors.WithEmphasized("test-a", false).WithEmphasized("test-b", true));
            window.Settle();

            Assert.Equal(Color.Parse("#F4F4F5"), ((ISolidColorBrush)Value(Card(window, "test-a")).Foreground!).Color);
            Assert.Equal(Color.Parse("#FA6831"), ((ISolidColorBrush)Value(Card(window, "test-b")).Foreground!).Color);
        });

    [Fact]
    public Task IconRowDrawsItsTextAndIconInTheSecondaryColour() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: Normal, overlay: AllAnchors);
            CameraWindow window = await OpenPrintingAsync(harness);
            OverlayIconText row = Card(window, "test-a").GetVisualDescendants().OfType<OverlayIconText>().Single();

            // T-076 1.3: OverlayTextSecondaryBrush #C9C9CE for the rows with an icon (text and icon), also on an emphasized card.
            Color secondary = Color.Parse("#C9C9CE");
            Assert.Equal(secondary, ((ISolidColorBrush)row.GetVisualDescendants().OfType<TextBlock>().Single().Foreground!).Color);
            Assert.Equal(secondary, ((ISolidColorBrush)row.GetVisualDescendants().OfType<OverlayIcon>().Single().Foreground!).Color);
        });

    [Theory]
    [InlineData(OverlayWidgetSize.Small)]
    [InlineData(OverlayWidgetSize.Medium)]
    [InlineData(OverlayWidgetSize.Large)]
    public Task PartsDrawTheirTextInTheirOwnColourSizeAndWeight(OverlayWidgetSize size) =>
        session.RunAsync(async scope =>
        {
            CameraOverlayLayout layout = new([new OverlayWidgetPlacement("test-a", OverlayAnchor.TopLeft, size)]);
            await using CameraHarness harness = new(window: Normal, overlay: layout);
            CameraWindow window = await OpenPrintingAsync(harness);
            Border card = Card(window, "test-a");
            Assert.Equal(size, Widget(window, "test-a").RenderedSize);
            bool small = size == OverlayWidgetSize.Small;
            bool large = size == OverlayWidgetSize.Large;

            // The drawn text of each part, not just the part's properties: the card's TextBlock styles also reach the
            // template parts and must not override them (T-076 1.2, 1.3).
            static TextBlock DrawnText(Control part) => part.GetVisualDescendants().OfType<TextBlock>().Single();
            static void AssertText(TextBlock text, string colour, double fontSize, FontWeight weight, string part)
            {
                Assert.True(Color.Parse(colour) == ((ISolidColorBrush)text.Foreground!).Color, $"{part} colour {((ISolidColorBrush)text.Foreground!).Color}");
                Assert.True(fontSize == text.FontSize, $"{part} font size {text.FontSize}");
                Assert.True(weight == text.FontWeight, $"{part} font weight {text.FontWeight}");
            }

            AssertText(
                DrawnText(card.GetVisualDescendants().OfType<OverlayIconText>().Single()),
                "#C9C9CE",
                large ? 15 : 13,
                small ? FontWeight.SemiBold : FontWeight.Normal,
                "icon row");
            if (!small)
            {
                // Pill and chip exist in M and L only (T-076 1.2).
                AssertText(
                    DrawnText(card.GetVisualDescendants().OfType<OverlayStatusPill>().Single()),
                    "#F4F4F5",
                    large ? 13 : 12,
                    FontWeight.SemiBold,
                    "pill");
                AssertText(
                    DrawnText(card.GetVisualDescendants().OfType<OverlayChip>().Single()),
                    "#E4E4E7",
                    large ? 12 : 11,
                    FontWeight.SemiBold,
                    "chip");
            }
        });

    [Fact]
    public Task NumbersUseTabularFigures() =>
        session.RunAsync(async scope =>
        {
            CameraOverlayLayout layout = new([new("test-a", OverlayAnchor.TopLeft)]);
            await using CameraHarness harness = new(window: Normal, overlay: layout);
            CameraWindow window = await OpenPrintingAsync(harness, progress: 11);
            TextBlock value = Value(Card(window, "test-a"));
            double eleven = value.Bounds.Width;

            harness.Monitor.Raise(Printing(88));
            await Ui.UntilAsync(() => value.Text == "88%", "88% shown");
            window.Settle();

            // Avalonia 12 applies FontFeatures "tnum" (the bundled Inter has proportional figures by default).
            Assert.Contains(value.FontFeatures!, static feature => feature.Tag == "tnum" && feature.Value == 1);
            Assert.Equal(eleven, value.Bounds.Width, 0.01);
            TextBlock proportional = new() { Text = "11%", FontSize = value.FontSize, FontWeight = value.FontWeight };
            TextBlock proportionalWide = new() { Text = "88%", FontSize = value.FontSize, FontWeight = value.FontWeight };
            proportional.Measure(Size.Infinity);
            proportionalWide.Measure(Size.Infinity);
            Assert.NotEqual(proportional.DesiredSize.Width, proportionalWide.DesiredSize.Width);
        });

    // ---- Small windows (T-076 1.4) ----

    [Theory]
    [InlineData(960, 540, OverlayWidgetSize.Large, OverlayWidgetSize.Medium)]
    [InlineData(600, 300, OverlayWidgetSize.Medium, OverlayWidgetSize.Small)]
    [InlineData(400, 200, OverlayWidgetSize.Small, null)]
    [InlineData(300, 170, null, null)]
    public Task SmallWindowsShrinkOrLeaveOutCards(double width, double height, OverlayWidgetSize? corner, OverlayWidgetSize? center) =>
        session.RunAsync(async scope =>
        {
            CameraOverlayLayout layout = new(
            [
                new OverlayWidgetPlacement("test-a", OverlayAnchor.TopLeft, OverlayWidgetSize.Large),
                new OverlayWidgetPlacement("test-b", OverlayAnchor.BottomCenter, OverlayWidgetSize.Medium),
            ]);
            await using CameraHarness harness = new(window: Normal, overlay: layout);
            CameraWindow window = await OpenPrintingAsync(harness);
            window.Width = width;
            window.Height = height;
            window.Settle();
            Dictionary<string, Border> cards = Cards(window);

            Assert.Equal(corner is not null, cards.ContainsKey("test-a"));
            Assert.Equal(center is not null, cards.ContainsKey("test-b"));
            if (corner is { } cornerSize)
            {
                Assert.Equal(cornerSize, Widget(window, "test-a").RenderedSize);
                Assert.Contains(cornerSize.ToString().ToLowerInvariant(), cards["test-a"].Classes);
            }

            if (center is { } centerSize)
            {
                Assert.Equal(centerSize, Widget(window, "test-b").RenderedSize);
            }

            // The layout itself does not change.
            Assert.Equal(OverlayWidgetSize.Large, Widget(window, "test-a").Size);

            window.Width = 960;
            window.Height = 540;
            window.Settle();
            Assert.Equal(OverlayWidgetSize.Large, Widget(window, "test-a").RenderedSize);
            Assert.Equal(2, Cards(window).Count);
        });

    [Fact]
    public Task OverlappingCardsOfAColumnLeaveOutTheLowerPriority() =>
        session.RunAsync(async scope =>
        {
            OverlayWidgetRegistry registry = new(
            [
                TestOverlayWidgets.Definition("test-a", priority: 50),
                TestOverlayWidgets.Definition("test-b", priority: 40),
                TestOverlayWidgets.Definition("test-c", priority: 10),
            ]);
            CameraOverlayLayout layout = new(
            [
                new OverlayWidgetPlacement("test-a", OverlayAnchor.TopLeft),
                new OverlayWidgetPlacement("test-b", OverlayAnchor.BottomLeft),
                new OverlayWidgetPlacement("test-c", OverlayAnchor.BottomLeft),
            ]);
            await using CameraHarness harness = new(window: Normal, overlay: layout, widgets: registry);
            CameraWindow window = await OpenPrintingAsync(harness, 720, 405);

            Dictionary<string, Border> cards = Cards(window);
            Assert.True(Widget(window, "test-c").IsCollapsed);
            Assert.Equal(["test-a", "test-b"], cards.Keys.Order());
            Rect a = BoundsInWindow(cards["test-a"], window);
            Rect b = BoundsInWindow(cards["test-b"], window);
            Assert.True(b.Y - a.Bottom >= 8, $"{a} and {b} overlap");

            // A taller window has room for all three.
            window.Height = 900;
            window.Settle();
            Assert.Equal(3, Cards(window).Count);
        });

    // ---- Saved settings ----

    [Fact]
    public Task SavedLayoutChangesTheOpenWindowWithoutMovingIt() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: Normal, overlay: AllAnchors);
            CameraWindow window = await OpenPrintingAsync(harness);
            window.Position = new PixelPoint(123, 45);
            window.Settle();
            PixelPoint position = window.Position;
            Size size = window.ClientSize;
            FakeCameraFeed feed = harness.Factory.Feeds[^1];
            int feeds = harness.Factory.Feeds.Count;

            Save(harness, AllAnchors.MoveTo("test-a", OverlayAnchor.BottomRight, 0).WithVisible("test-f", false));
            window.Settle();

            Dictionary<string, Border> cards = Cards(window);
            Assert.Equal(5, cards.Count);
            Rect a = BoundsInWindow(cards["test-a"], window);
            Assert.Equal(window.ClientSize.Width - Margin, a.Right, 0.5);
            Assert.Equal(window.ClientSize.Height - Margin, a.Bottom, 0.5);
            Assert.Equal(position, window.Position);
            Assert.Equal(size, window.ClientSize);
            Assert.True(ViewModel(window).IsNormal);
            // The stream goes on: no new connection.
            Assert.Equal(feeds, harness.Factory.Feeds.Count);
            Assert.True(ViewModel(window).IsStreaming);
            Assert.NotNull(feed);
        });

    [Fact]
    public Task SavedSettingsOfAnotherPrinterLeaveThisWindowAlone() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: Normal, overlay: AllAnchors);
            CameraWindow window = await OpenPrintingAsync(harness);
            Rect before = BoundsInWindow(Card(window, "test-a"), window);

            ((IMonitoringObserver)harness.Printers).ApplySettings(new AppSettings
            {
                Printers = [CameraHarness.CameraPrinter("a", "Alpha", Normal, AllAnchors), CameraHarness.CameraPrinter("b", "Beta", overlay: CameraOverlayLayout.Default)],
            });
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.Equal(before, BoundsInWindow(Card(window, "test-a"), window));
            Assert.Equal(6, Cards(window).Count);
        });

    [Fact]
    public Task CardsFollowALanguageSwitch() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: Normal, overlay: AllAnchors);
            CameraWindow window = await OpenPrintingAsync(harness);

            Assert.True(new UiLanguage().Apply("de"));
            ViewModel(window).RefreshTexts();
            window.Settle();

            Assert.Equal("Druckt", Widget(window, "test-a").Title);
            Assert.True(Ui.ShowsText(window, "Druckt"));
        });

    [Fact]
    public Task OldOverlayIsGone() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: Normal);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Grey);
            harness.Monitor.Raise(Printing());
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            // The default layout without registered widgets of this version draws nothing (T-082 and later add them).
            Assert.False(window.IsShown<Control>("CameraPrintOverlay"));
            foreach (OverlayAnchor anchor in OverlayAnchors.All)
            {
                Assert.Empty(window.Find<ItemsControl>("CameraOverlayAnchor" + anchor).Items);
            }
        });

    // ---- Snapshots ----

    [Fact]
    public Task SnapshotOfCardsInAllAnchors() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: Normal, overlay: AllAnchors);
            CameraWindow window = await OpenPrintingAsync(harness);

            SnapshotAssert.Matches(window, "camera-widgets-anchors");
        });

    [Fact]
    public Task SnapshotOfTheSizesAndTheLowestOpacity() =>
        session.RunAsync(async scope =>
        {
            CameraOverlayLayout layout = new(
            [
                new OverlayWidgetPlacement("test-a", OverlayAnchor.TopLeft, OverlayWidgetSize.Small, emphasized: true),
                new OverlayWidgetPlacement("test-b", OverlayAnchor.TopLeft, OverlayWidgetSize.Medium),
                new OverlayWidgetPlacement("test-c", OverlayAnchor.BottomRight, OverlayWidgetSize.Large, emphasized: true),
            ]);
            await using CameraHarness harness = new(window: Normal, overlay: layout, opacity: CameraOverlayOpacity.MinPercent);
            CameraWindow window = await OpenPrintingAsync(harness);

            SnapshotAssert.Matches(window, "camera-widgets-sizes-opacity-30");
        });

    private static TestOverlayWidget Widget(CameraWindow window, string id) =>
        (TestOverlayWidget)ViewModel(window).Overlay.Widgets.Single(widget => widget.Id == id);
}
