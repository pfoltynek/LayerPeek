using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Previews;
using LayerPeek.App.Tests.Previews;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Tray;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Shared;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The G-code thumbnail of the Print name card (T-087, design T-076 2.4) in the real camera window with the real PNG
/// decoder: M 40 / L 72 DIP with radius 6 / 8 and gap 10 / 12 before the text, decoded at the size × the monitor scaling,
/// without a preview (or with a broken one) the card of T-085 unchanged, kept after the print and replaced by the next
/// one, none in S; reference snapshots.
/// </summary>
public sealed class CameraWindowPrintThumbnailUiTests(HeadlessSession session)
{
    private const uint Grey = 0xFF606060;

    private const string CardId = OverlayWidgetViewModel.AutomationIdPrefix + OverlayWidgetIds.PrintName;

    private const string ThumbnailId = CardId + "-Thumbnail";

    private const string TitleId = CardId + "-Title";

    private const string Foldable = "foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode";

    private const string FoldablePath = "usb/FOLDAB~1.BGC";

    private const string BoxPath = "usb/BOX~1.BGC";

    private static readonly CameraWindowDefaults Normal = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };

    private static CameraOverlayLayout Layout(OverlayWidgetSize size) =>
        new([new OverlayWidgetPlacement(OverlayWidgetIds.PrintName, OverlayAnchor.TopCenter, size, emphasized: true)]);

    private static PrinterSnapshot Job(string fileName = Foldable, PrinterState state = PrinterState.Printing, long id = 1, string? path = FoldablePath) => new()
    {
        PrinterId = "a",
        State = state,
        Timestamp = Now,
        Job = new PrintJob
        {
            Id = id,
            FileName = fileName,
            ProgressPercent = 42.7,
            Elapsed = TimeSpan.FromMinutes(130),
            Remaining = TimeSpan.FromMinutes(65),
            PreviewReference = path is null ? null : new TestPreviewReference(path),
        },
    };

    /// <summary>A preview whose header is a valid PNG but whose image data is garbage (only the header is checked before decoding).</summary>
    private static PrintPreview Corrupt()
    {
        byte[] png = TestPng.Create(380, 285);
        // Signature (8) + IHDR chunk (25); then a chunk of garbage instead of the image data.
        byte[] corrupt = [.. png.AsSpan(0, 33), 0, 0, 0, 8, (byte)'I', (byte)'D', (byte)'A', (byte)'T', 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 9];
        return PrintPreview.FromPng(corrupt)!;
    }

    /// <summary>Camera windows whose Print name card gets its previews from <paramref name="source"/> and decodes them for real.</summary>
    private static CameraHarness Harness(OverlayWidgetSize size, out FakePreviewSource source)
    {
        source = new FakePreviewSource();
        source.Previews[FoldablePath] = FakePreviewSource.Png();
        // A square preview of another colour: the next job's thumbnail is told apart by its decoded size.
        source.Previews[BoxPath] = FakePreviewSource.Png(300, 300, 0x31, 0x68, 0xFA);
        PrintThumbnails thumbnails = new(source, new AvaloniaPrintThumbnailDecoder(), new AvaloniaUiDispatcher(), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now), NullLogger<PrintThumbnails>.Instance);
        return new CameraHarness(window: Normal, overlay: Layout(size), widgets: OverlayWidgets.CreateRegistry(), thumbnails: thumbnails);
    }

    private static PrintNameWidgetViewModel Widget(CameraWindow window) =>
        (PrintNameWidgetViewModel)((CameraViewModel)window.DataContext!).Overlay.Widgets.Single(static widget => widget.Id == OverlayWidgetIds.PrintName);

    private static Border Card(CameraWindow window) => window.Find<Border>(CardId);

    private static Border Thumbnail(CameraWindow window) => window.Find<Border>(ThumbnailId);

    private static Bitmap ThumbnailBitmap(CameraWindow window) =>
        Assert.IsType<Bitmap>(Thumbnail(window).GetVisualDescendants().OfType<Image>().Single().Source);

    private static Rect BoundsInWindow(Visual control, Visual window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static async Task<CameraWindow> OpenAsync(CameraHarness harness, PrinterSnapshot snapshot, Func<CameraWindow, bool> until, double width = 960, double height = 540, double scaling = 1)
    {
        (CameraWindow window, _) = await harness.OpenStreamingAsync(160, 90, Grey);
        window.Width = width;
        window.Height = height;
        if (scaling != 1)
        {
            window.SetRenderScaling(scaling);
        }

        window.Settle();
        harness.Monitor.Raise(snapshot);
        await Ui.UntilAsync(() => Widget(window).IsShown && until(window), "Print name card shown");
        window.Settle();
        return window;
    }

    private static bool HasThumbnail(CameraWindow window) => Widget(window).ShowsThumbnail;

    // ---- Size, rounding, placement ----

    [Theory]
    [InlineData(OverlayWidgetSize.Medium, 40, 6, 10, 68)]
    [InlineData(OverlayWidgetSize.Large, 72, 8, 12, 102)]
    public Task ThumbnailHasTheDesignedSizeRadiusAndGap(OverlayWidgetSize size, double side, double radius, double gap, double cardHeight) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(size, out _);
            CameraWindow window = await OpenAsync(harness, Job(), HasThumbnail);

            Border thumbnail = Thumbnail(window);
            Assert.True(thumbnail.IsEffectivelyVisible);
            Assert.Equal(side, thumbnail.Bounds.Width, 0.5);
            Assert.Equal(side, thumbnail.Bounds.Height, 0.5);
            Assert.Equal(new CornerRadius(radius), thumbnail.CornerRadius);
            Assert.True(thumbnail.ClipToBounds);
            Assert.Equal(Color.Parse("#14FFFFFF"), ((ISolidColorBrush)thumbnail.Background!).Color);
            Image image = thumbnail.GetVisualDescendants().OfType<Image>().Single();
            Assert.Equal(Stretch.Uniform, image.Stretch);
            // 380 × 285 fitted into the square: the full width, 3 : 4 of the height, centred.
            Assert.Equal(side, image.Bounds.Width, 0.5);
            Assert.Equal(side * 285 / 380, image.Bounds.Height, 1);

            // Left of the title, aligned with its top, with the designed gap; the card keeps its T-085 height.
            Rect thumbnailBounds = BoundsInWindow(thumbnail, window);
            Rect title = BoundsInWindow(window.Find<TextBlock>(TitleId), window);
            Assert.Equal(gap, title.Left - thumbnailBounds.Right, 0.5);
            Assert.Equal(title.Top, thumbnailBounds.Top, 0.5);
            Assert.Equal(cardHeight, Card(window).Bounds.Height, 0.5);
            Assert.True(BoundsInWindow(Card(window), window).Contains(thumbnailBounds));

            Assert.Equal("Print preview", thumbnail.GetValue(AutomationProperties.NameProperty));
        });

    [Theory]
    [InlineData(OverlayWidgetSize.Medium, 1.0, 40, 30)]
    [InlineData(OverlayWidgetSize.Large, 1.0, 72, 54)]
    [InlineData(OverlayWidgetSize.Medium, 2.0, 80, 60)]
    [InlineData(OverlayWidgetSize.Large, 1.5, 108, 81)]
    public Task ThumbnailIsDecodedForTheMonitorScaling(OverlayWidgetSize size, double scaling, int width, int height) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(size, out _);
            CameraWindow window = await OpenAsync(harness, Job(), HasThumbnail, scaling: scaling);

            // Decoded once at the drawn size in physical pixels, not at the 380 × 285 of the preview.
            Assert.Equal(new PixelSize(width, height), ThumbnailBitmap(window).PixelSize);
        });

    [Fact]
    public Task SmallCardHasNoThumbnail() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(OverlayWidgetSize.Small, out FakePreviewSource source);
            CameraWindow window = await OpenAsync(harness, Job(), static _ => true);

            Assert.False(window.IsShown<Border>(ThumbnailId));
            Assert.Equal(0, source.RequestCount);
            Assert.Equal(36, Card(window).Bounds.Height, 0.5);
        });

    // ---- Without a preview ----

    [Theory]
    [InlineData(OverlayWidgetSize.Medium, 68)]
    [InlineData(OverlayWidgetSize.Large, 102)]
    public Task WithoutAPreviewTheCardIsTheOneOfT085(OverlayWidgetSize size, double cardHeight) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(size, out FakePreviewSource source);
            CameraWindow window = await OpenAsync(harness, Job(path: "usb/NOPREV~1.BGC"), _ => source.RequestCount == 1);
            await Task.Delay(50, TestContext.Current.CancellationToken);
            window.Settle();

            // No empty frame, no placeholder: the title starts at the card's padding (M 14, L 16).
            Assert.False(window.IsShown<Border>(ThumbnailId));
            Rect card = BoundsInWindow(Card(window), window);
            Rect title = BoundsInWindow(window.Find<TextBlock>(TitleId), window);
            Assert.Equal(size == OverlayWidgetSize.Large ? 16 : 14, title.Left - card.Left, 0.5);
            Assert.Equal(cardHeight, card.Height, 0.5);
        });

    [Fact]
    public Task CorruptPreviewShowsNoThumbnailAndTheCardStays() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(OverlayWidgetSize.Medium, out FakePreviewSource source);
            source.Previews[FoldablePath] = Corrupt();
            CameraWindow window = await OpenAsync(harness, Job(), _ => source.RequestCount == 1);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            window.Settle();

            Assert.False(window.IsShown<Border>(ThumbnailId));
            Assert.True(Card(window).IsEffectivelyVisible);
            Assert.False(Widget(window).IsFaulted);
            Assert.Equal("Foldable book holder v1", window.Find<TextBlock>(TitleId).Text);
        });

    // ---- Across prints ----

    [Fact]
    public Task ThumbnailStaysAfterThePrintAndTheNextPrintReplacesIt() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(OverlayWidgetSize.Medium, out FakePreviewSource source);
            CameraWindow window = await OpenAsync(harness, Job(), HasThumbnail);
            Bitmap first = ThumbnailBitmap(window);

            harness.Monitor.Raise(Job(state: PrinterState.Finished));
            await Ui.UntilAsync(() => Widget(window).IsShown, "finished print shown");
            window.Settle();
            Assert.Same(first, ThumbnailBitmap(window));
            harness.Monitor.Raise(Job(state: PrinterState.Stopped));
            window.Settle();
            Assert.Same(first, ThumbnailBitmap(window));
            Assert.Equal(1, source.RequestCount);

            harness.Monitor.Raise(Job(fileName: "box_0.4n_0.2mm_PETG_MK4S_45m.bgcode", id: 2, path: BoxPath));
            await Ui.UntilAsync(() => HasThumbnail(window) && Widget(window).Title == "Box", "next print's thumbnail shown");
            window.Settle();

            Bitmap next = ThumbnailBitmap(window);
            Assert.NotSame(first, next);
            Assert.Equal(new PixelSize(40, 40), next.PixelSize);
        });

    // ---- Snapshots ----

    [Theory]
    [InlineData(OverlayWidgetSize.Medium, "m")]
    [InlineData(OverlayWidgetSize.Large, "l")]
    public Task SnapshotOfTheCardWithThumbnail(OverlayWidgetSize size, string suffix) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(size, out _);
            CameraWindow window = await OpenAsync(harness, Job(), HasThumbnail, 720, 405);

            SnapshotAssert.Matches(window, "camera-widget-print-name-thumbnail-" + suffix);
        });
}
