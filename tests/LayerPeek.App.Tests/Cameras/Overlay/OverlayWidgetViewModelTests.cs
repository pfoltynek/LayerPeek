using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay;

/// <summary>
/// The base of every widget (T-081): content compared on the monitor thread, texts formatted on the UI thread only when the
/// shown content changed, nothing posted for polls that change nothing visible, a language switch and extra updates (the
/// per-snapshot path without allocations: <see cref="OverlayAllocationTests"/>).
/// </summary>
public sealed class OverlayWidgetViewModelTests : IDisposable
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
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

    private CameraOverlayViewModel Create(params string[] ids) => Create(TestOverlayWidgets.Registry(ids), ids);

    private CameraOverlayViewModel Create(OverlayWidgetRegistry registry, params string[] ids)
    {
        CameraOverlayViewModel overlay = new(
            registry,
            new OverlayWidgetContext("mk4", _time),
            _dispatcher,
            NullLogger.Instance,
            Definition(),
            new CameraOverlayLayout(ids.Select(static id => new OverlayWidgetPlacement(id, OverlayAnchor.TopLeft))),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot: null);
        _created.Add(overlay);
        return overlay;
    }

    private static PrinterSnapshot Printing(double progress = 42.7, double nozzle = 214.6) =>
        Snapshot(PrinterState.Printing, Job(progress, TimeSpan.FromMinutes(65))) with { Telemetry = new Telemetry { Nozzle = new Temperature(nozzle, 215) } };

    private static TestOverlayWidget Widget(CameraOverlayViewModel overlay, string id = "test-a") =>
        (TestOverlayWidget)overlay.Widgets.Single(widget => widget.Id == id);

    [Fact]
    public void ContentIsShownOnTheUiThreadAfterTheSnapshot()
    {
        CameraOverlayViewModel overlay = Create("test-a");
        TestOverlayWidget widget = Widget(overlay);
        Assert.Equal("MK4", widget.Value);

        overlay.OnSnapshot(Printing());
        Assert.Equal("MK4", widget.Value);
        Assert.Equal(1, _dispatcher.Pending);

        _dispatcher.RunAll();
        Assert.Equal("42%", widget.Value);
        Assert.Equal("Printing", widget.Title);
        Assert.Equal("Printing, 42%", widget.AccessibleName);
    }

    [Fact]
    public void PollThatChangesNothingVisiblePostsNothingAndFormatsNothing()
    {
        CameraOverlayViewModel overlay = Create("test-a");
        overlay.OnSnapshot(Printing(42.1));
        _dispatcher.RunAll();
        TestOverlayWidget widget = Widget(overlay);
        int posts = _dispatcher.PostCount;
        int formatted = widget.FormatCount;
        int changes = 0;
        widget.PropertyChanged += (_, _) => changes++;

        // Below the displayed precision, and a value the widget does not show.
        overlay.OnSnapshot(Printing(42.9));
        overlay.OnSnapshot(Printing(42.9, nozzle: 190) with { Timestamp = Now.AddSeconds(5) });

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(formatted, widget.FormatCount);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void OnlyTheChangedWidgetFormatsAgain()
    {
        // test-a shows the progress, test-b only the printer name.
        OverlayWidgetRegistry registry = new(
        [
            TestOverlayWidgets.Definition("test-a"),
            TestOverlayWidgets.Definition("test-b", create: _ => new TestOverlayWidget(static input => new(input.Printer.Name, null, null))),
        ]);
        CameraOverlayViewModel overlay = Create(registry, "test-a", "test-b");
        overlay.OnSnapshot(Printing(10));
        _dispatcher.RunAll();
        int formattedA = Widget(overlay, "test-a").FormatCount;
        int formattedB = Widget(overlay, "test-b").FormatCount;

        overlay.OnSnapshot(Printing(20));
        _dispatcher.RunAll();

        Assert.Equal(formattedA + 1, Widget(overlay, "test-a").FormatCount);
        Assert.Equal(formattedB, Widget(overlay, "test-b").FormatCount);
    }

    [Fact]
    public void SnapshotsWhileTheUiIsBusyPostOnceAndShowTheLatest()
    {
        CameraOverlayViewModel overlay = Create("test-a", "test-b");
        int posts = _dispatcher.PostCount;

        overlay.OnSnapshot(Printing(10));
        overlay.OnSnapshot(Printing(20));
        overlay.OnSnapshot(Printing(30));

        Assert.Equal(posts + 1, _dispatcher.PostCount);
        _dispatcher.RunAll();
        Assert.Equal("30%", Widget(overlay, "test-a").Value);
        Assert.Equal("30%", Widget(overlay, "test-b").Value);
    }

    [Fact]
    public void ContentThatChangesBackBeforeTheUiRunsFormatsNothing()
    {
        CameraOverlayViewModel overlay = Create("test-a");
        overlay.OnSnapshot(Printing(10));
        _dispatcher.RunAll();
        int formatted = Widget(overlay).FormatCount;

        overlay.OnSnapshot(Printing(20));
        overlay.OnSnapshot(Printing(10));
        _dispatcher.RunAll();

        Assert.Equal(formatted, Widget(overlay).FormatCount);
    }

    [Fact]
    public void WidgetWithoutDataHidesItsCard()
    {
        OverlayWidgetRegistry registry = new([TestOverlayWidgets.Definition("test-a", create: _ => new TestOverlayWidget(static input =>
            OverlayValues.ActiveJob(input.Snapshot) is { } job ? new(null, null, OverlayValues.ProgressPercent(job)) : default))]);
        CameraOverlayViewModel overlay = Create(registry, "test-a");
        TestOverlayWidget widget = Widget(overlay);
        Assert.False(widget.HasContent);
        Assert.False(widget.IsShown);
        Assert.Equal(0, widget.FormatCount);

        overlay.OnSnapshot(Printing());
        _dispatcher.RunAll();
        Assert.True(widget.IsShown);

        overlay.OnSnapshot(Snapshot(PrinterState.Finished));
        _dispatcher.RunAll();
        Assert.False(widget.IsShown);
        // The texts of the last content with data stay (hidden).
        Assert.Equal("42%", widget.Value);
    }

    [Fact]
    public void LanguageSwitchFormatsTheShownContentAgainWithoutComputing()
    {
        CameraOverlayViewModel overlay = Create("test-a");
        overlay.OnSnapshot(Printing());
        _dispatcher.RunAll();
        TestOverlayWidget widget = Widget(overlay);
        int computed = widget.ComputeCount;

        using (new CultureScope("cs-CZ"))
        {
            overlay.RefreshTexts();

            Assert.Equal("Tiskne", widget.Title);
            Assert.Equal("42 %", widget.Value);
        }

        Assert.Equal(computed, widget.ComputeCount);
        Assert.Equal(0, _dispatcher.Pending);
    }

    [Fact]
    public void LanguageSwitchWithoutDataFormatsNothing()
    {
        OverlayWidgetRegistry registry = new([TestOverlayWidgets.Definition("test-a", create: _ => new TestOverlayWidget(static _ => default))]);
        CameraOverlayViewModel overlay = Create(registry, "test-a");

        overlay.RefreshTexts();

        Assert.Equal(0, Widget(overlay).FormatCount);
    }

    [Fact]
    public void WidgetCanAskForAnUpdateOfItsOwn()
    {
        string extra = "first";
        OverlayWidgetRegistry registry = new([TestOverlayWidgets.Definition("test-a", create: _ => new TestOverlayWidget(_ => new(extra, null, null)))]);
        CameraOverlayViewModel overlay = Create(registry, "test-a");
        TestOverlayWidget widget = Widget(overlay);
        Assert.Equal("first", widget.Value);

        extra = "second";
        widget.Invalidate();
        Assert.Equal("first", widget.Value);
        _dispatcher.RunAll();

        Assert.Equal("second", widget.Value);
    }

    [Fact]
    public void UpdateRequestOfAWidgetThatLeftTheLayoutIsIgnored()
    {
        CameraOverlayViewModel overlay = Create("test-a");
        TestOverlayWidget widget = Widget(overlay);
        overlay.ApplySettings(Definition(), CameraOverlayLayout.Default, CameraOverlayOpacity.DefaultPercent);
        int computed = widget.ComputeCount;
        int posts = _dispatcher.PostCount;

        widget.Invalidate();

        Assert.Equal(computed, widget.ComputeCount);
        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public void DefinitionValidatesItsArguments()
    {
        Assert.Throws<ArgumentException>(() => TestOverlayWidgets.Definition(" bad"));
        Assert.Throws<ArgumentException>(() => TestOverlayWidgets.Definition("test-a", sizes: []));
        Assert.Throws<ArgumentException>(() => TestOverlayWidgets.Definition("test-a", sizes: [(OverlayWidgetSize)7]));
        Assert.Equal([OverlayWidgetSize.Small, OverlayWidgetSize.Large], TestOverlayWidgets.Definition("test-a", sizes: [OverlayWidgetSize.Large, OverlayWidgetSize.Small, OverlayWidgetSize.Large]).SupportedSizes);
        Assert.Equal(160, TestOverlayWidgets.Definition("test-a", minWidths: new(100, 160, 200)).MinWidth(OverlayWidgetSize.Medium));
    }

    [Fact]
    public void WidgetBelongsToOneOverlayOnly()
    {
        CameraOverlayViewModel overlay = Create("test-a");
        TestOverlayWidget widget = Widget(overlay);

        Assert.Throws<InvalidOperationException>(() => widget.Attach(TestOverlayWidgets.Definition("test-a"), overlay));
        Assert.Throws<InvalidOperationException>(() => new TestOverlayWidget().Definition);
    }
}
