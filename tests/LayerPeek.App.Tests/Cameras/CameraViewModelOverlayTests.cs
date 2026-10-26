using Avalonia.Media;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Tests.Cameras.Overlay;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// The widget overlay of the camera window (T-081): the printer's layout and the global card opacity, saved settings
/// applied to an open window in place (it does not move, the stream goes on), no cards in the mini preview.
/// </summary>
public sealed class CameraViewModelOverlayTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraOverlayLayout TwoCards = new(
    [
        new OverlayWidgetPlacement("test-a", OverlayAnchor.TopLeft),
        new OverlayWidgetPlacement("test-b", OverlayAnchor.BottomRight, emphasized: true),
    ]);

    private readonly FakeCameraFeedFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakePrinterMonitor _monitor = new();
    private readonly MonitoredPrinters _printers = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly MemoryCameraWindowStateStore _states = new();
    private readonly List<CameraViewModel> _created = [];
    private readonly CultureScope _culture = new("en-US");

    public async ValueTask DisposeAsync()
    {
        _culture.Dispose();
        foreach (CameraViewModel viewModel in _created)
        {
            viewModel.Dispose();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }
    }

    private static PrinterDefinition Printer(CameraOverlayLayout layout, string name = "MK4", string id = "mk4", CameraWindowDefaults? window = null) =>
        Definition(id, name) with { Camera = new CameraSettings(enabled: true, StreamUri) { Overlay = layout, Window = window ?? CameraWindowDefaults.Default } };

    private CameraViewModel Create(CameraOverlayLayout layout, CameraWindowDefaults? window = null)
    {
        ReconnectingCameraFeed feed = new(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraViewModel viewModel = new(
            CameraTarget.From(Printer(layout, window: window))!,
            feed,
            _monitor,
            _printers,
            _dispatcher,
            _states,
            _time,
            CameraFeedOptions.Default,
            NullLogger<CameraViewModel>.Instance,
            widgets: TestOverlayWidgets.Registry("test-a", "test-b"));
        _created.Add(viewModel);
        return viewModel;
    }

    private void Save(CameraOverlayLayout layout, string name = "MK4", int opacity = CameraOverlayOpacity.DefaultPercent, params PrinterDefinition[] others)
    {
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(layout, name), .. others], CameraOverlayOpacityPercent = opacity });
        _dispatcher.RunAll();
    }

    private void Raise(PrinterSnapshot snapshot)
    {
        _monitor.Raise(snapshot);
        _dispatcher.RunAll();
    }

    private static string[] Ids(IReadOnlyList<OverlayWidgetViewModel> widgets) => [.. widgets.Select(static widget => widget.Id)];

    private static TestOverlayWidget Widget(CameraViewModel viewModel, string id) =>
        (TestOverlayWidget)viewModel.Overlay.Widgets.Single(widget => widget.Id == id);

    [Fact]
    public void TargetCarriesTheLayoutOfThePrinter()
    {
        Assert.Same(TwoCards, CameraTarget.From(Printer(TwoCards))!.Overlay);
        Assert.Same(CameraOverlayLayout.Default, CameraTarget.From(Definition() with { Camera = new CameraSettings(enabled: true, StreamUri) })!.Overlay);
    }

    [Fact]
    public void WindowOpensWithTheLayoutAndTheGlobalOpacity()
    {
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(TwoCards)], CameraOverlayOpacityPercent = 40 });
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42.7)));

        CameraViewModel viewModel = Create(TwoCards);

        Assert.Equal(["test-a"], Ids(viewModel.Overlay.TopLeft));
        Assert.Equal(["test-b"], Ids(viewModel.Overlay.BottomRight));
        Assert.True(Widget(viewModel, "test-b").IsEmphasized);
        Assert.Equal("42%", Widget(viewModel, "test-a").Value);
        Assert.Equal(40, viewModel.Overlay.OpacityPercent);
        Assert.Equal(102, ((ISolidColorBrush)Widget(viewModel, "test-a").CardBackground!).Color.A);
    }

    [Fact]
    public void BeforeTheFirstMonitoringRunTheOpacityIsTheDefault() =>
        Assert.Equal(CameraOverlayOpacity.DefaultPercent, Create(TwoCards).Overlay.OpacityPercent);

    [Fact]
    public void SavedLayoutAppliesToTheOpenWindowRightAway()
    {
        CameraViewModel viewModel = Create(TwoCards);
        Raise(Snapshot(PrinterState.Printing, Job(42.7)));
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Save(TwoCards.MoveTo("test-a", OverlayAnchor.BottomCenter, 0).WithVisible("test-b", false));

        Assert.Empty(viewModel.Overlay.TopLeft);
        Assert.Empty(viewModel.Overlay.BottomRight);
        Assert.Equal(["test-a"], Ids(viewModel.Overlay.BottomCenter));
        Assert.Equal("42%", Widget(viewModel, "test-a").Value);
        // The window stays as it is: same mode, stream untouched, nothing to place again.
        Assert.True(viewModel.IsNormal);
        Assert.Empty(_factory.Uris);
        Assert.DoesNotContain(nameof(CameraViewModel.Mode), changed);
    }

    [Fact]
    public void SavedOpacityAppliesToTheOpenWindowRightAway()
    {
        CameraViewModel viewModel = Create(TwoCards);

        Save(TwoCards, opacity: 95);

        Assert.Equal(95, viewModel.Overlay.OpacityPercent);
        Assert.All(viewModel.Overlay.Widgets, widget => Assert.Equal(242, ((ISolidColorBrush)widget.CardBackground!).Color.A));
    }

    [Fact]
    public void SavedSettingsOfAnotherPrinterLeaveThisWindowAlone()
    {
        CameraViewModel viewModel = Create(TwoCards);
        Save(TwoCards);
        TestOverlayWidget a = Widget(viewModel, "test-a");
        int formatted = a.FormatCount;

        Save(TwoCards, others: Printer(CameraOverlayLayout.Default, "Other", "other"));

        Assert.Same(a, Widget(viewModel, "test-a"));
        Assert.Equal(formatted, a.FormatCount);
        Assert.Equal(["test-a"], Ids(viewModel.Overlay.TopLeft));
    }

    [Fact]
    public void RenamedPrinterReachesTheWidgets()
    {
        CameraViewModel viewModel = Create(TwoCards);

        Save(TwoCards, name: "Workshop MK4");

        Assert.Equal("Workshop MK4", Widget(viewModel, "test-a").Value);
        Assert.Equal("Workshop MK4 – Camera", viewModel.Title);
    }

    [Fact]
    public void MiniPreviewHasNoCardsAndFullScreenSharesTheLayout()
    {
        CameraViewModel viewModel = Create(TwoCards);

        viewModel.ToggleFullScreenCommand.Execute(null);
        Assert.True(viewModel.Overlay.AreCardsVisible);
        Assert.Equal(["test-a"], Ids(viewModel.Overlay.TopLeft));

        viewModel.ShowMiniCommand.Execute(null);
        Assert.False(viewModel.Overlay.AreCardsVisible);

        viewModel.ShowNormalCommand.Execute(null);
        Assert.True(viewModel.Overlay.AreCardsVisible);
    }

    [Fact]
    public void WindowOpeningInTheMiniPreviewHasNoCards()
    {
        CameraViewModel viewModel = Create(TwoCards, CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini });

        Assert.True(viewModel.IsMini);
        Assert.False(viewModel.Overlay.AreCardsVisible);
    }

    [Fact]
    public void LanguageSwitchFormatsTheCardsAgain()
    {
        CameraViewModel viewModel = Create(TwoCards);
        Raise(Snapshot(PrinterState.Printing, Job(42.7)));

        using (new UiCultureScope("cs"))
        {
            viewModel.RefreshTexts();

            Assert.Equal("Tiskne", Widget(viewModel, "test-a").Title);
        }
    }

    [Fact]
    public void SettingsAndSnapshotsAfterDisposeDoNothing()
    {
        CameraViewModel viewModel = Create(TwoCards);
        Raise(Snapshot(PrinterState.Printing, Job(10)));
        TestOverlayWidget a = Widget(viewModel, "test-a");
        viewModel.Dispose();
        int posts = _dispatcher.PostCount;

        Save(CameraOverlayLayout.Default, opacity: 30);
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(50)));
        _dispatcher.RunAll();

        Assert.True(a.IsDisposed);
        Assert.Equal("10%", a.Value);
        Assert.Equal(CameraOverlayOpacity.DefaultPercent, viewModel.Overlay.OpacityPercent);
        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, _monitor.SubscriberCount);
    }
}
