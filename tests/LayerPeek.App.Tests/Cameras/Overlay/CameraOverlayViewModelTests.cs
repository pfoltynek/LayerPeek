using Avalonia.Media;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay;

/// <summary>
/// The widget overlay of a camera window (T-081): anchors and stacking order from the layout, the global card opacity,
/// emphasis, saved settings in place, the mini preview without cards, and widgets that fail without taking the others down.
/// </summary>
public sealed class CameraOverlayViewModelTests : IDisposable
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ListLogger<CameraOverlayViewModel> _logger = new();
    private readonly CultureScope _culture = new("en-US");
    private readonly List<CameraOverlayViewModel> _created = [];

    public void Dispose()
    {
        foreach (CameraOverlayViewModel overlay in _created)
        {
            overlay.Dispose();
        }

        _culture.Dispose();
    }

    private static OverlayWidgetPlacement Place(string id, OverlayAnchor anchor, OverlayWidgetSize size = OverlayWidgetSize.Medium, bool emphasized = false, bool visible = true) =>
        new(id, anchor, size, emphasized, visible);

    private static CameraOverlayLayout Layout(params OverlayWidgetPlacement[] widgets) => new(widgets);

    private CameraOverlayViewModel Create(
        CameraOverlayLayout layout,
        OverlayWidgetRegistry? registry = null,
        int opacity = CameraOverlayOpacity.DefaultPercent,
        CameraWindowMode mode = CameraWindowMode.Normal,
        PrinterSnapshot? snapshot = null)
    {
        CameraOverlayViewModel overlay = new(
            registry ?? TestOverlayWidgets.Registry([.. TestOverlayWidgets.Ids]),
            new OverlayWidgetContext("mk4", _time),
            _dispatcher,
            _logger,
            Definition(),
            layout,
            opacity,
            mode,
            snapshot);
        _created.Add(overlay);
        return overlay;
    }

    private static PrinterSnapshot Printing(double progress = 42.7) => Snapshot(PrinterState.Printing, Job(progress, TimeSpan.FromMinutes(65)));

    private void Raise(CameraOverlayViewModel overlay, PrinterSnapshot snapshot)
    {
        overlay.OnSnapshot(snapshot);
        _dispatcher.RunAll();
    }

    private static string[] Ids(IReadOnlyList<OverlayWidgetViewModel> widgets) => [.. widgets.Select(static widget => widget.Id)];

    private static TestOverlayWidget Widget(CameraOverlayViewModel overlay, string id) =>
        (TestOverlayWidget)overlay.Widgets.Single(widget => widget.Id == id);

    // ---- Anchors and order ----

    [Fact]
    public void WidgetsGoIntoTheirAnchorsInStackingOrder()
    {
        CameraOverlayViewModel overlay = Create(Layout(
            Place("test-c", OverlayAnchor.BottomLeft),
            Place("test-a", OverlayAnchor.TopLeft),
            Place("test-d", OverlayAnchor.BottomLeft),
            Place("test-b", OverlayAnchor.TopRight),
            Place("test-e", OverlayAnchor.BottomLeft),
            Place("test-f", OverlayAnchor.TopCenter)));

        Assert.Equal(["test-a"], Ids(overlay.TopLeft));
        Assert.Equal(["test-f"], Ids(overlay.TopCenter));
        Assert.Equal(["test-b"], Ids(overlay.TopRight));
        // Several widgets in one anchor: layout order = from the window edge inward.
        Assert.Equal(["test-c", "test-d", "test-e"], Ids(overlay.BottomLeft));
        Assert.Empty(overlay.BottomCenter);
        Assert.Empty(overlay.BottomRight);
        Assert.Equal(["test-a", "test-f", "test-b", "test-c", "test-d", "test-e"], Ids(overlay.Widgets));
        Assert.All(OverlayAnchors.All, anchor => Assert.All(overlay.InAnchor(anchor), widget => Assert.Equal(anchor, widget.Anchor)));
    }

    [Fact]
    public void EveryAnchorCanHoldACard()
    {
        CameraOverlayViewModel overlay = Create(new CameraOverlayLayout(
            TestOverlayWidgets.Ids.Select((id, index) => Place(id, OverlayAnchors.All[index]))));

        foreach (OverlayAnchor anchor in OverlayAnchors.All)
        {
            OverlayWidgetViewModel widget = Assert.Single(overlay.InAnchor(anchor));
            Assert.Equal("CameraOverlayWidget-" + widget.Id, widget.AutomationId);
        }
    }

    [Fact]
    public void EmptyLayoutDrawsNoCard()
    {
        CameraOverlayViewModel overlay = Create(Layout());
        Raise(overlay, Printing());

        Assert.Empty(overlay.Widgets);
        Assert.All(OverlayAnchors.All, anchor => Assert.Empty(overlay.InAnchor(anchor)));
    }

    [Fact]
    public void HiddenWidgetsAreNotCreated()
    {
        int created = 0;
        OverlayWidgetRegistry registry = new([TestOverlayWidgets.Definition("test-a", create: _ => { created++; return new TestOverlayWidget(); })]);

        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft, visible: false)), registry);

        Assert.Empty(overlay.Widgets);
        Assert.Equal(0, created);
    }

    [Fact]
    public void UnknownAndUnregisteredIdsAreSkippedWithoutAnException()
    {
        // A widget of a newer version and a widget of this version that is not implemented yet: kept, not drawn.
        CameraOverlayViewModel overlay = Create(Layout(
            Place("future-widget", OverlayAnchor.TopLeft),
            Place(OverlayWidgetIds.Status, OverlayAnchor.TopLeft),
            Place("test-a", OverlayAnchor.TopLeft)));

        Assert.Equal(["test-a"], Ids(overlay.TopLeft));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void DefaultLayoutWithoutRegisteredWidgetsDrawsNothing()
    {
        CameraOverlayViewModel overlay = Create(CameraOverlayLayout.Default, OverlayWidgetRegistry.Empty);
        Raise(overlay, Printing());

        Assert.Empty(overlay.Widgets);
        Assert.Same(CameraOverlayLayout.Default, overlay.Layout);
    }

    [Fact]
    public void UnsupportedSizeFallsBackToTheNearestSmallerOne()
    {
        OverlayWidgetRegistry registry = new(
        [
            TestOverlayWidgets.Definition("test-a", sizes: [OverlayWidgetSize.Small, OverlayWidgetSize.Medium]),
            TestOverlayWidgets.Definition("test-b", sizes: [OverlayWidgetSize.Large]),
        ]);

        CameraOverlayViewModel overlay = Create(
            Layout(Place("test-a", OverlayAnchor.TopLeft, OverlayWidgetSize.Large), Place("test-b", OverlayAnchor.TopLeft, OverlayWidgetSize.Small)),
            registry);

        Assert.Equal(OverlayWidgetSize.Medium, overlay.Widgets[0].Size);
        Assert.Equal(OverlayWidgetSize.Large, overlay.Widgets[1].Size);
    }

    // ---- Saved settings ----

    [Fact]
    public void SavedLayoutKeepsTheWidgetsThatStay()
    {
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft), Place("test-b", OverlayAnchor.BottomRight)));
        Raise(overlay, Printing());
        TestOverlayWidget a = Widget(overlay, "test-a");
        TestOverlayWidget b = Widget(overlay, "test-b");
        IReadOnlyList<OverlayWidgetViewModel> topLeft = overlay.TopLeft;
        int formatted = a.FormatCount;
        List<string?> changed = [];
        overlay.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        overlay.ApplySettings(Definition(), Layout(Place("test-a", OverlayAnchor.TopLeft, OverlayWidgetSize.Large, emphasized: true), Place("test-b", OverlayAnchor.BottomCenter)), CameraOverlayOpacity.DefaultPercent);

        Assert.Same(a, Widget(overlay, "test-a"));
        Assert.Same(b, Widget(overlay, "test-b"));
        // A new size and emphasis change the card in place: the anchor keeps its list.
        Assert.Same(topLeft, overlay.TopLeft);
        Assert.Equal(OverlayWidgetSize.Large, a.Size);
        Assert.True(a.IsEmphasized);
        Assert.Equal(["test-b"], Ids(overlay.BottomCenter));
        Assert.Empty(overlay.BottomRight);
        Assert.Equal(OverlayAnchor.BottomCenter, b.Anchor);
        Assert.Equal([nameof(CameraOverlayViewModel.BottomCenter), nameof(CameraOverlayViewModel.BottomRight)], changed.Order());
        // Nothing to format again: the content did not change.
        Assert.Equal(formatted, a.FormatCount);
    }

    [Fact]
    public void WidgetLeavingTheLayoutIsDisposedAndANewOneCreated()
    {
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft)));
        Raise(overlay, Printing());
        TestOverlayWidget a = Widget(overlay, "test-a");

        overlay.ApplySettings(Definition(), Layout(Place("test-a", OverlayAnchor.TopLeft, visible: false), Place("test-b", OverlayAnchor.TopLeft)), CameraOverlayOpacity.DefaultPercent);

        Assert.True(a.IsDisposed);
        TestOverlayWidget b = Widget(overlay, "test-b");
        // The new widget shows the latest snapshot right away, without waiting for a poll.
        Assert.Equal("42%", b.Value);
        Assert.True(b.IsShown);
        Assert.Equal(["test-b"], Ids(overlay.TopLeft));
    }

    [Fact]
    public void SameLayoutAgainChangesNothing()
    {
        CameraOverlayLayout layout = Layout(Place("test-a", OverlayAnchor.TopLeft));
        CameraOverlayViewModel overlay = Create(layout);
        Raise(overlay, Printing());
        List<string?> changed = [];
        overlay.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        int posts = _dispatcher.PostCount;
        int formatted = Widget(overlay, "test-a").FormatCount;

        overlay.ApplySettings(Definition(), Layout(Place("test-a", OverlayAnchor.TopLeft)), CameraOverlayOpacity.DefaultPercent);

        Assert.Empty(changed);
        Assert.Equal(formatted, Widget(overlay, "test-a").FormatCount);
        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public void RenamedPrinterReachesTheWidgets()
    {
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft)));
        Assert.Equal("MK4", Widget(overlay, "test-a").Value);

        overlay.ApplySettings(Definition(name: "Workshop"), overlay.Layout, CameraOverlayOpacity.DefaultPercent);

        Assert.Equal("Workshop", Widget(overlay, "test-a").Value);
    }

    // ---- Opacity ----

    [Theory]
    [InlineData(30, 77)]
    [InlineData(72, 184)]
    [InlineData(95, 242)]
    public void CardBackgroundHasTheGlobalOpacity(int percent, byte alpha)
    {
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft), Place("test-b", OverlayAnchor.BottomRight)), opacity: percent);

        Color color = Assert.IsAssignableFrom<ISolidColorBrush>(overlay.CardBackground).Color;
        Assert.Equal(Color.FromArgb(alpha, 0x0E, 0x0E, 0x10), color);
        Assert.Equal(percent, overlay.OpacityPercent);
        Assert.All(overlay.Widgets, widget => Assert.Same(overlay.CardBackground, widget.CardBackground));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(96)]
    [InlineData(0)]
    public void InvalidOpacityCountsAsTheDefault(int percent)
    {
        CameraOverlayViewModel overlay = Create(Layout(), opacity: percent);

        Assert.Equal(CameraOverlayOpacity.DefaultPercent, overlay.OpacityPercent);
    }

    [Fact]
    public void SavedOpacityChangesEveryCardRightAway()
    {
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft), Place("test-b", OverlayAnchor.TopLeft)));
        List<string?> changed = [];
        overlay.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        overlay.ApplySettings(Definition(), overlay.Layout, 95);

        Assert.Equal(95, overlay.OpacityPercent);
        Assert.Equal(242, ((ISolidColorBrush)overlay.CardBackground).Color.A);
        Assert.All(overlay.Widgets, widget => Assert.Same(overlay.CardBackground, widget.CardBackground));
        Assert.Contains(nameof(CameraOverlayViewModel.CardBackground), changed);
    }

    // ---- Emphasis ----

    [Fact]
    public void EmphasisFollowsTheLayout()
    {
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft, emphasized: true), Place("test-b", OverlayAnchor.TopLeft)));

        Assert.True(Widget(overlay, "test-a").IsEmphasized);
        Assert.False(Widget(overlay, "test-b").IsEmphasized);

        overlay.ApplySettings(Definition(), overlay.Layout.WithEmphasized("test-a", false).WithEmphasized("test-b", true), CameraOverlayOpacity.DefaultPercent);

        Assert.False(Widget(overlay, "test-a").IsEmphasized);
        Assert.True(Widget(overlay, "test-b").IsEmphasized);
    }

    // ---- Window modes ----

    [Fact]
    public void MiniPreviewDrawsNoCards()
    {
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft)));
        Assert.True(overlay.AreCardsVisible);
        List<string?> changed = [];
        overlay.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        overlay.SetMode(CameraWindowMode.Mini);
        Assert.False(overlay.AreCardsVisible);

        overlay.SetMode(CameraWindowMode.FullScreen);
        Assert.True(overlay.AreCardsVisible);

        overlay.SetMode(CameraWindowMode.Normal);
        Assert.True(overlay.AreCardsVisible);
        Assert.Equal([nameof(CameraOverlayViewModel.AreCardsVisible), nameof(CameraOverlayViewModel.AreCardsVisible)], changed);
    }

    [Fact]
    public void WindowOpeningInTheMiniPreviewDrawsNoCards() =>
        Assert.False(Create(Layout(Place("test-a", OverlayAnchor.TopLeft)), mode: CameraWindowMode.Mini).AreCardsVisible);

    [Fact]
    public void WidgetsSeeTheModeRightAway()
    {
        OverlayWidgetRegistry registry = new([TestOverlayWidgets.Definition("test-a", create: _ => new TestOverlayWidget(static input => new(input.Mode.ToString(), null, null)))]);
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft)), registry);

        overlay.SetMode(CameraWindowMode.FullScreen);

        Assert.Equal("FullScreen", Widget(overlay, "test-a").Value);
    }

    // ---- Failing widgets ----

    [Fact]
    public void WidgetThatCannotBeCreatedIsLeftOut()
    {
        OverlayWidgetRegistry registry = new(
        [
            TestOverlayWidgets.Definition("test-a", create: _ => throw new InvalidOperationException("broken")),
            TestOverlayWidgets.Definition("test-b"),
        ]);

        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft), Place("test-b", OverlayAnchor.TopLeft)), registry);
        Raise(overlay, Printing());

        Assert.Equal(["test-b"], Ids(overlay.TopLeft));
        Assert.Equal("42%", Widget(overlay, "test-b").Value);
        (LogLevel level, string message, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains("test-a", message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void WidgetThatCannotBeCreatedIsNotTriedAgainUntilTheWindowCloses()
    {
        int attempts = 0;
        OverlayWidgetRegistry registry = new(
        [
            TestOverlayWidgets.Definition("test-a", create: _ =>
            {
                attempts++;
                throw new InvalidOperationException("broken");
            }),
            TestOverlayWidgets.Definition("test-b"),
        ]);
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft), Place("test-b", OverlayAnchor.TopLeft)), registry);

        // Saved layouts that still (or again) contain the broken widget.
        overlay.ApplySettings(Definition(), Layout(Place("test-a", OverlayAnchor.BottomLeft), Place("test-b", OverlayAnchor.TopLeft)), CameraOverlayOpacity.DefaultPercent);
        overlay.ApplySettings(Definition(), Layout(Place("test-b", OverlayAnchor.TopLeft)), CameraOverlayOpacity.DefaultPercent);
        overlay.ApplySettings(Definition(), Layout(Place("test-a", OverlayAnchor.TopRight), Place("test-b", OverlayAnchor.TopLeft)), CameraOverlayOpacity.DefaultPercent);

        Assert.Equal(1, attempts);
        Assert.Single(_logger.Entries);
        Assert.Equal(["test-b"], Ids(overlay.Widgets));
    }

    [Fact]
    public void WidgetThatFailsComputingIsHiddenAndTheOthersGoOn()
    {
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft), Place("test-b", OverlayAnchor.TopLeft)));
        Raise(overlay, Printing(10));
        TestOverlayWidget a = Widget(overlay, "test-a");
        TestOverlayWidget b = Widget(overlay, "test-b");
        Assert.True(a.IsShown);

        a.FailCompute = true;
        Raise(overlay, Printing(20));
        Raise(overlay, Printing(30));

        Assert.True(a.IsFaulted);
        Assert.False(a.IsShown);
        Assert.Equal("30%", b.Value);
        // Logged once, never computed again.
        Assert.Single(_logger.Entries);
        int computed = a.ComputeCount;
        Raise(overlay, Printing(40));
        Assert.Equal(computed, a.ComputeCount);
    }

    [Fact]
    public void WidgetThatFailsFormattingIsHiddenAndTheOthersGoOn()
    {
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft), Place("test-b", OverlayAnchor.BottomLeft)));
        TestOverlayWidget a = Widget(overlay, "test-a");
        TestOverlayWidget b = Widget(overlay, "test-b");

        a.FailFormat = true;
        Raise(overlay, Printing(20));
        overlay.RefreshTexts();
        Raise(overlay, Printing(30));

        Assert.True(a.IsFaulted);
        Assert.False(a.IsShown);
        Assert.Equal("30%", b.Value);
        Assert.True(b.IsShown);
        Assert.Contains("test-a", Assert.Single(_logger.Entries).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedWidgetStaysHiddenAfterALanguageSwitch()
    {
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft)));
        Raise(overlay, Printing());
        TestOverlayWidget a = Widget(overlay, "test-a");
        a.FailFormat = true;

        overlay.RefreshTexts();
        overlay.RefreshTexts();

        Assert.True(a.IsFaulted);
        Assert.Single(_logger.Entries);
    }

    // ---- Disposal ----

    [Fact]
    public void DisposeDisposesTheWidgetsAndIgnoresLaterSnapshots()
    {
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft)));
        TestOverlayWidget a = Widget(overlay, "test-a");
        int posts = _dispatcher.PostCount;
        int computed = a.ComputeCount;

        overlay.Dispose();
        overlay.OnSnapshot(Printing());
        overlay.SetMode(CameraWindowMode.Mini);
        overlay.ApplySettings(Definition(), Layout(), 30);
        overlay.RefreshTexts();

        Assert.True(a.IsDisposed);
        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(computed, a.ComputeCount);
    }

    [Fact]
    public void UpdatePostedBeforeDisposeDoesNothing()
    {
        CameraOverlayViewModel overlay = Create(Layout(Place("test-a", OverlayAnchor.TopLeft)));
        overlay.OnSnapshot(Printing());
        TestOverlayWidget a = Widget(overlay, "test-a");

        overlay.Dispose();
        _dispatcher.RunAll();

        Assert.Equal(1, a.FormatCount);
    }
}
