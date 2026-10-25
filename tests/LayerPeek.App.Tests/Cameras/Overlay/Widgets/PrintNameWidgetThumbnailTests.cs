using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Previews;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Previews;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay.Widgets;

/// <summary>
/// The G-code thumbnail of the Print name widget (T-087, design T-076 2.4): asked for only while the card is drawn in M
/// (40 px) or L (72 px) and only when the job changes, decoded at the size × the monitor scaling, shared between windows,
/// kept after the print until the next one, replaced by the next job, released when the window closes; no preview or a
/// broken one = the card without the thumbnail column.
/// </summary>
public sealed class PrintNameWidgetThumbnailTests : IDisposable
{
    private const string Foldable = PrintNameWidgetTests.Foldable;

    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly CultureScope _culture = new("en-US");
    private readonly FakePreviewSource _source = new();
    private readonly FakeThumbnailDecoder _decoder = new();
    private readonly PrintThumbnails _thumbnails;
    private readonly List<CameraOverlayViewModel> _created = [];

    public PrintNameWidgetThumbnailTests()
    {
        _thumbnails = new PrintThumbnails(_source, _decoder, _dispatcher, _time, NullLogger<PrintThumbnails>.Instance);
        _source.Previews["usb/FOLDAB~1.BGC"] = FakePreviewSource.Png();
        _source.Previews["usb/BOX~1.BGC"] = FakePreviewSource.Png(red: 0x20);
    }

    public void Dispose()
    {
        foreach (CameraOverlayViewModel overlay in _created)
        {
            overlay.Dispose();
        }

        _culture.Dispose();
    }

    /// <summary>A snapshot of <paramref name="state"/> with a job that has a G-code preview at <paramref name="path"/>.</summary>
    internal static PrinterSnapshot Job(
        string fileName = Foldable,
        PrinterState state = PrinterState.Printing,
        long id = 1,
        string? path = "usb/FOLDAB~1.BGC",
        double progress = 42.7) => new()
        {
            PrinterId = "mk4",
            State = state,
            Timestamp = Now,
            Job = new PrintJob
            {
                Id = id,
                FileName = fileName,
                ProgressPercent = progress,
                PreviewReference = path is null ? null : new TestPreviewReference(path),
            },
        };

    private CameraOverlayViewModel Create(OverlayWidgetSize size = OverlayWidgetSize.Medium, CameraWindowMode mode = CameraWindowMode.Normal, PrintThumbnails? thumbnails = null)
    {
        CameraOverlayViewModel overlay = new(
            OverlayWidgets.CreateRegistry(),
            new OverlayWidgetContext("mk4", _time, Thumbnails: thumbnails ?? _thumbnails),
            _dispatcher,
            NullLogger.Instance,
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.PrintName, OverlayAnchor.TopCenter, size)]),
            CameraOverlayOpacity.DefaultPercent,
            mode,
            snapshot: null);
        _created.Add(overlay);
        PrintNameWidgetViewModel widget = Widget(overlay);
        widget.RenderedSize = size;
        return overlay;
    }

    private static PrintNameWidgetViewModel Widget(CameraOverlayViewModel overlay) => (PrintNameWidgetViewModel)Assert.Single(overlay.Widgets);

    /// <summary>Shows <paramref name="snapshot"/> and runs the UI thread until the thumbnail work is done.</summary>
    private async Task<PrintNameWidgetViewModel> ShowAsync(CameraOverlayViewModel overlay, PrinterSnapshot snapshot, int expectedRequests)
    {
        overlay.OnSnapshot(snapshot);
        _dispatcher.RunAll();
        await SettleAsync(expectedRequests);
        return Widget(overlay);
    }

    /// <summary>
    /// Waits until <paramref name="expectedRequests"/> previews were asked for and the UI thread got every resulting update
    /// (decoding runs on the thread pool): until no request is fetching or decoding and nothing is posted any more. Waits
    /// for that state, not for a quiet period, so a busy thread pool cannot end it early.
    /// </summary>
    private async Task SettleAsync(int expectedRequests)
    {
        await Waiting.UntilAsync(() => _source.RequestCount >= expectedRequests, $"{expectedRequests} preview request(s)");
        do
        {
            await Waiting.UntilAsync(() => _thumbnails.BusyCount == 0, "thumbnail requests idle");
            _dispatcher.RunAll();
        }
        while (_thumbnails.BusyCount != 0 || _dispatcher.Pending != 0);
    }

    // ---- Which preview, which size ----

    [Theory]
    [InlineData(OverlayWidgetSize.Medium, 1.0, 40)]
    [InlineData(OverlayWidgetSize.Large, 1.0, 72)]
    [InlineData(OverlayWidgetSize.Medium, 1.25, 50)]
    [InlineData(OverlayWidgetSize.Large, 1.5, 108)]
    [InlineData(OverlayWidgetSize.Medium, 2.0, 80)]
    [InlineData(OverlayWidgetSize.Large, 2.0, 144)]
    public async Task ThumbnailIsTheLargePreviewDecodedAtTheCardSizeTimesTheScaling(OverlayWidgetSize size, double scaling, int pixels)
    {
        CameraOverlayViewModel overlay = Create(size);
        Widget(overlay).RenderScaling = scaling;

        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(), 1);

        (_, PrintJob job, PrintPreviewSize asked) = Assert.Single(_source.Requests);
        Assert.Equal(PrintPreviewSize.Large, asked);
        Assert.Equal(1, job.Id);
        Assert.Equal(pixels, Assert.Single(_decoder.Decoded).MaxSide);
        Assert.Same(Assert.Single(_decoder.Images), widget.Thumbnail);
        Assert.True(widget.ShowsThumbnail);
    }

    [Fact]
    public async Task SmallCardAsksForNoPreview()
    {
        CameraOverlayViewModel overlay = Create(OverlayWidgetSize.Small);

        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(), 0);

        Assert.True(widget.IsShown);
        Assert.Equal(0, _source.RequestCount);
        Assert.False(widget.ShowsThumbnail);
        Assert.Null(widget.Thumbnail);
    }

    [Fact]
    public async Task MiniPreviewAsksForNoPreviewAndNormalWindowDoes()
    {
        CameraOverlayViewModel overlay = Create(mode: CameraWindowMode.Mini);
        await ShowAsync(overlay, Job(), 0);
        Assert.Equal(0, _source.RequestCount);

        overlay.SetMode(CameraWindowMode.Normal);
        await SettleAsync(1);

        Assert.Equal(1, _source.RequestCount);
        Assert.True(Widget(overlay).ShowsThumbnail);
    }

    [Fact]
    public async Task HiddenCardAsksForNoPreviewAndReleasesTheShownOne()
    {
        CameraOverlayViewModel overlay = Create();
        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(state: PrinterState.Idle), 0);
        Assert.False(widget.IsShown);
        Assert.Equal(0, _source.RequestCount);

        widget = await ShowAsync(overlay, Job(), 1);
        FakeImage image = Assert.IsType<FakeImage>(widget.Thumbnail);
        widget = await ShowAsync(overlay, Job(state: PrinterState.Offline), 1);

        Assert.False(widget.IsShown);
        Assert.Null(widget.Thumbnail);
        Assert.True(image.IsDisposed);
    }

    [Fact]
    public async Task JobWithoutAPreviewReferenceAsksForNothing()
    {
        CameraOverlayViewModel overlay = Create();

        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(path: null), 0);

        Assert.True(widget.IsShown);
        Assert.Equal(0, _source.RequestCount);
        Assert.False(widget.ShowsThumbnail);
    }

    [Fact]
    public async Task WithoutThePreviewServiceTheCardHasNoThumbnail()
    {
        CameraOverlayViewModel overlay = new(
            OverlayWidgets.CreateRegistry(),
            new OverlayWidgetContext("mk4", _time),
            _dispatcher,
            NullLogger.Instance,
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.PrintName, OverlayAnchor.TopCenter)]),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot: null);
        _created.Add(overlay);

        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(), 0);

        Assert.True(widget.IsShown);
        Assert.False(widget.ShowsThumbnail);
    }

    // ---- Only when the job changes ----

    [Fact]
    public async Task PollsOfTheSameJobAskOnce()
    {
        CameraOverlayViewModel overlay = Create();
        await ShowAsync(overlay, Job(), 1);
        int posts = _dispatcher.PostCount;

        overlay.OnSnapshot(Job(progress: 50));
        overlay.OnSnapshot(Job(fileName: new string(Foldable.AsSpan()), state: PrinterState.Paused));
        overlay.OnSnapshot(Job(progress: 51) with { Job = Job().Job! with { PreviewReference = new TestPreviewReference("usb/FOLDAB~1.BGC") } });

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, _dispatcher.Pending);
        Assert.Equal(1, _source.RequestCount);
        Assert.Single(_decoder.Decoded);
    }

    [Fact]
    public void ContentComparesTheJobByItsIdentityNotByItsInstance()
    {
        PrintFileNameInfo info = PrintFileNameInfo.Parse(Foldable);
        PrinterDefinition printer = Definition();
        PrintJob job = Job().Job!;
        PrintNameContent content = new(Foldable, info, job, printer, CardsVisible: true);

        // Every poll brings a new job instance (and progress) of the same job.
        Assert.Equal(content, content with { Job = job with { ProgressPercent = 99, PreviewReference = new TestPreviewReference("usb/FOLDAB~1.BGC") } });
        Assert.Equal(content.GetHashCode(), (content with { Job = job with { ProgressPercent = 99 } }).GetHashCode());
        Assert.NotEqual(content, content with { Job = job with { Id = 2 } });
        Assert.NotEqual(content, content with { Job = job with { PreviewReference = new TestPreviewReference("usb/BOX~1.BGC") } });
        Assert.NotEqual(content, content with { Job = job with { PreviewReference = null } });
        Assert.NotEqual(content, content with { Job = null });
        // Saved settings bring a new printer definition (e.g. another address): asked for again.
        Assert.NotEqual(content, content with { Printer = printer with { Address = "192.168.1.99" } });
        Assert.NotEqual(content, content with { CardsVisible = false });
    }

    [Fact]
    public async Task ThumbnailStaysAfterThePrintFinishesOrStops()
    {
        CameraOverlayViewModel overlay = Create();
        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(), 1);
        FakeImage image = Assert.IsType<FakeImage>(widget.Thumbnail);

        widget = await ShowAsync(overlay, Job(state: PrinterState.Finished, progress: 100), 1);
        Assert.Same(image, widget.Thumbnail);
        widget = await ShowAsync(overlay, Job(state: PrinterState.Stopped), 1);

        Assert.Same(image, widget.Thumbnail);
        Assert.False(image.IsDisposed);
        Assert.Equal(1, _source.RequestCount);
    }

    [Fact]
    public async Task NextJobReplacesTheThumbnailAndReleasesTheOldOne()
    {
        CameraOverlayViewModel overlay = Create();
        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(), 1);
        FakeImage first = Assert.IsType<FakeImage>(widget.Thumbnail);
        await ShowAsync(overlay, Job(state: PrinterState.Finished), 1);

        _source.Hold = true;
        PrinterSnapshot next = Job(fileName: "box_0.4n_0.2mm_PETG_MK4S_45m.bgcode", id: 2, path: "usb/BOX~1.BGC");
        overlay.OnSnapshot(next);
        _dispatcher.RunAll();
        await Waiting.UntilAsync(() => _source.RequestCount == 2, "next job asked for");

        // The old thumbnail does not belong to the new print: it goes right away, the new one comes when decoded.
        Assert.Null(widget.Thumbnail);
        Assert.True(first.IsDisposed);
        Assert.Same(next.Job, _source.Requests[1].Job);
        _source.Release();
        await SettleAsync(2);

        FakeImage second = Assert.IsType<FakeImage>(widget.Thumbnail);
        Assert.Same(_source.Previews["usb/BOX~1.BGC"], second.Preview);
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task ReprintOfTheSameFileAsksForThePreviewOfTheNewJob()
    {
        CameraOverlayViewModel overlay = Create();
        await ShowAsync(overlay, Job(state: PrinterState.Finished), 1);

        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(id: 2), 2);

        Assert.Equal(2, _source.Requests[1].Job.Id);
        Assert.Equal(1, widget.ParseCount);
        Assert.True(widget.ShowsThumbnail);
    }

    [Fact]
    public async Task SizeChangeKeepsTheShownThumbnailUntilTheNewSizeIsDecoded()
    {
        CameraOverlayViewModel overlay = Create(OverlayWidgetSize.Medium);
        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(), 1);
        FakeImage medium = Assert.IsType<FakeImage>(widget.Thumbnail);

        _decoder.Gate.Reset();
        widget.RenderedSize = OverlayWidgetSize.Large;
        await Waiting.UntilAsync(() => _source.RequestCount == 2, "large size asked for");
        Assert.Same(medium, widget.Thumbnail);
        _decoder.Gate.Set();
        await SettleAsync(2);

        FakeImage large = Assert.IsType<FakeImage>(widget.Thumbnail);
        Assert.Equal(72, large.MaxSide);
        Assert.True(medium.IsDisposed);
    }

    [Fact]
    public async Task SavedPrinterSettingsAskAgainAndKeepTheThumbnailMeanwhile()
    {
        CameraOverlayViewModel overlay = Create();
        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(), 1);
        FakeImage image = Assert.IsType<FakeImage>(widget.Thumbnail);

        // Another address: the cache drops the printer, so the preview is asked for with the new definition.
        overlay.ApplySettings(
            Definition() with { Address = "192.168.1.99" },
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.PrintName, OverlayAnchor.TopCenter, OverlayWidgetSize.Medium)]),
            CameraOverlayOpacity.DefaultPercent);
        Assert.Same(image, widget.Thumbnail);
        await SettleAsync(2);

        Assert.Equal("192.168.1.99", _source.Requests[1].Printer.Address);
        // The same preview at the same size: the shared image, never released in between.
        Assert.Same(image, widget.Thumbnail);
        Assert.False(image.IsDisposed);
        Assert.Single(_decoder.Decoded);
    }

    [Fact]
    public async Task SmallerRenderedSizeDropsTheThumbnail()
    {
        CameraOverlayViewModel overlay = Create(OverlayWidgetSize.Medium);
        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(), 1);
        FakeImage image = Assert.IsType<FakeImage>(widget.Thumbnail);

        widget.RenderedSize = OverlayWidgetSize.Small;

        Assert.False(widget.ShowsThumbnail);
        Assert.Null(widget.Thumbnail);
        Assert.True(image.IsDisposed);
    }

    [Fact]
    public async Task ScalingChangeDecodesForTheNewScaling()
    {
        CameraOverlayViewModel overlay = Create(OverlayWidgetSize.Medium);
        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(), 1);

        widget.RenderScaling = 1.5;
        await SettleAsync(2);
        widget.RenderScaling = 1.5;

        Assert.Equal([40, 60], _decoder.Decoded.Select(static decoded => decoded.MaxSide));
        Assert.Equal(60, Assert.IsType<FakeImage>(widget.Thumbnail).MaxSide);
        Assert.Equal(2, _source.RequestCount);
    }

    // ---- Failures ----

    [Fact]
    public async Task MissingPreviewLeavesTheCardWithoutAThumbnail()
    {
        CameraOverlayViewModel overlay = Create();

        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(path: "usb/NOPREV~1.BGC"), 1);

        Assert.True(widget.IsShown);
        Assert.False(widget.ShowsThumbnail);
        Assert.Null(widget.Thumbnail);
    }

    [Fact]
    public async Task CorruptPreviewLeavesTheCardWithoutAThumbnail()
    {
        _decoder.Corrupt.Add(_source.Previews["usb/FOLDAB~1.BGC"]!);
        CameraOverlayViewModel overlay = Create();

        PrintNameWidgetViewModel widget = await ShowAsync(overlay, Job(), 1);

        Assert.True(widget.IsShown);
        Assert.False(widget.IsFaulted);
        Assert.False(widget.ShowsThumbnail);
    }

    // ---- Sharing and release ----

    [Fact]
    public async Task TwoWindowsOfThePrinterDecodeOnce()
    {
        CameraOverlayViewModel first = Create();
        CameraOverlayViewModel second = Create();

        PrintNameWidgetViewModel a = await ShowAsync(first, Job(), 1);
        PrintNameWidgetViewModel b = await ShowAsync(second, Job(), 2);

        Assert.Single(_decoder.Decoded);
        Assert.NotNull(a.Thumbnail);
        Assert.Same(a.Thumbnail, b.Thumbnail);
    }

    [Fact]
    public async Task ClosingTheLastWindowReleasesTheThumbnail()
    {
        CameraOverlayViewModel first = Create();
        CameraOverlayViewModel second = Create();
        await ShowAsync(first, Job(), 1);
        await ShowAsync(second, Job(), 2);
        FakeImage image = Assert.Single(_decoder.Images);

        first.Dispose();
        Assert.False(image.IsDisposed);
        Assert.Null(Widget(first).Thumbnail);
        second.Dispose();

        Assert.True(image.IsDisposed);
        Assert.Equal(0, _thumbnails.DecodedCount);
    }

    [Fact]
    public async Task ClosingTheWindowWhileDecodingDeliversNothing()
    {
        CameraOverlayViewModel overlay = Create();
        _decoder.Gate.Reset();
        overlay.OnSnapshot(Job());
        _dispatcher.RunAll();
        await Waiting.UntilAsync(() => _thumbnails.DecodedCount == 1, "decoding started");

        overlay.Dispose();
        _decoder.Gate.Set();
        await Waiting.UntilAsync(() => _decoder.Images.Count == 1 && _decoder.Images[0].IsDisposed, "late image released");
        _dispatcher.RunAll();

        Assert.Null(Widget(overlay).Thumbnail);
    }

    // ---- Texts ----

    [Fact]
    public async Task ThumbnailHasItsScreenReaderName()
    {
        PrintNameWidgetViewModel widget = await ShowAsync(Create(), Job(), 1);

        Assert.Equal("Print preview", widget.ThumbnailName);
        Assert.Equal(Strings.CameraOverlayThumbnailName, widget.ThumbnailName);
    }

    [Fact]
    public void EveryLanguageNamesTheThumbnail()
    {
        foreach (string language in LayerPeek.Core.Settings.AppLanguages.Supported)
        {
            string? name = Strings.ResourceManager.GetString(nameof(Strings.CameraOverlayThumbnailName), System.Globalization.CultureInfo.GetCultureInfo(language));
            Assert.False(string.IsNullOrWhiteSpace(name), language);
        }

        Assert.Equal("Náhled tisku", Strings.ResourceManager.GetString(nameof(Strings.CameraOverlayThumbnailName), System.Globalization.CultureInfo.GetCultureInfo("cs")));
    }
}
