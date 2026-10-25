using Avalonia.Media;

using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Previews;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Previews;

/// <summary>
/// The decoded G-code thumbnails shared by the camera windows (T-087): the large preview (the only PNG stock profiles
/// embed) of the job given, decoded off the caller's thread once per preview and pixel size, shared by every request,
/// disposed when the last request is gone; no preview or a corrupt PNG = none, asked again after the retry delay;
/// updates only on the UI thread and never after the request was disposed.
/// </summary>
public sealed class PrintThumbnailsTests
{
    private readonly FakePreviewSource _source = new();
    private readonly FakeThumbnailDecoder _decoder = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ListLogger<PrintThumbnails> _logger = new();

    private static PrintJob Job(long id = 1, string path = "usb/BENCHY~1.BGC") =>
        new() { Id = id, FileName = "benchy.bgcode", PreviewReference = new TestPreviewReference(path) };

    private PrintThumbnails Create() => new(_source, _decoder, _dispatcher, _time, _logger);

    /// <summary>Waits for the posted updates and runs them as the UI thread would.</summary>
    private async Task DeliverAsync(string description)
    {
        await Waiting.UntilAsync(() => _dispatcher.Pending > 0, description);
        _dispatcher.RunAll();
    }

    [Fact]
    public async Task RequestAsksForTheLargePreviewOfTheGivenJobAndDecodesItToTheSize()
    {
        PrintPreview preview = FakePreviewSource.Png();
        _source.Previews["usb/BENCHY~1.BGC"] = preview;
        PrintThumbnails thumbnails = Create();
        PrinterDefinition printer = Definition();
        PrintJob job = Job();
        List<IImage?> updates = [];

        using IDisposable request = thumbnails.Request(printer, job, 72, updates.Add);
        await DeliverAsync("thumbnail delivered");

        (PrinterDefinition asked, PrintJob askedJob, PrintPreviewSize size) = Assert.Single(_source.Requests);
        Assert.Same(printer, asked);
        Assert.Same(job, askedJob);
        // Stock Prusa profiles embed the 16 × 16 icon only as QOI, so /thumb/s is a 404; the large one is downscaled.
        Assert.Equal(PrintPreviewSize.Large, size);
        Assert.Equal((preview, 72), Assert.Single(_decoder.Decoded));
        FakeImage image = Assert.IsType<FakeImage>(Assert.Single(updates));
        Assert.Same(preview, image.Preview);
        Assert.False(image.IsDisposed);
    }

    [Fact]
    public async Task UpdateIsPostedToTheUiThreadNeverCalledInline()
    {
        _source.Previews["usb/BENCHY~1.BGC"] = FakePreviewSource.Png();
        PrintThumbnails thumbnails = Create();
        List<IImage?> updates = [];

        using IDisposable request = thumbnails.Request(Definition(), Job(), 40, updates.Add);
        await Waiting.UntilAsync(() => _dispatcher.Pending > 0, "update posted");

        Assert.Empty(updates);
        _dispatcher.RunAll();
        Assert.Single(updates);
    }

    [Fact]
    public async Task DecodingDoesNotBlockTheCaller()
    {
        _source.Previews["usb/BENCHY~1.BGC"] = FakePreviewSource.Png();
        _decoder.Gate.Reset();
        PrintThumbnails thumbnails = Create();
        List<IImage?> updates = [];

        // Returns while the decoder is still busy.
        using IDisposable request = thumbnails.Request(Definition(), Job(), 40, updates.Add);
        Assert.Empty(updates);
        Assert.Equal(0, _dispatcher.Pending);

        _decoder.Gate.Set();
        await DeliverAsync("thumbnail delivered after decoding");
        Assert.Single(updates);
    }

    [Fact]
    public async Task WindowsShowingTheSamePreviewShareOneDecodedImage()
    {
        _source.Previews["usb/BENCHY~1.BGC"] = FakePreviewSource.Png();
        PrintThumbnails thumbnails = Create();
        List<IImage?> first = [];
        List<IImage?> second = [];

        using IDisposable a = thumbnails.Request(Definition(), Job(), 72, first.Add);
        await DeliverAsync("first thumbnail");
        using IDisposable b = thumbnails.Request(Definition(), Job(), 72, second.Add);
        await DeliverAsync("second thumbnail");

        Assert.Single(_decoder.Decoded);
        Assert.Same(Assert.Single(first), Assert.Single(second));
        Assert.Equal(1, thumbnails.DecodedCount);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneDecoding()
    {
        _source.Previews["usb/BENCHY~1.BGC"] = FakePreviewSource.Png();
        _decoder.Gate.Reset();
        PrintThumbnails thumbnails = Create();
        List<IImage?> updates = [];

        using IDisposable a = thumbnails.Request(Definition(), Job(), 40, updates.Add);
        using IDisposable b = thumbnails.Request(Definition(), Job(), 40, updates.Add);
        await Waiting.UntilAsync(() => thumbnails.DecodedCount == 1 && _source.RequestCount == 2, "both requests wait for one decoding");
        _decoder.Gate.Set();
        await Waiting.UntilAsync(() => _dispatcher.Pending == 2, "both updates posted");
        _dispatcher.RunAll();

        Assert.Single(_decoder.Decoded);
        Assert.Equal(2, updates.Count);
        Assert.Same(updates[0], updates[1]);
    }

    [Fact]
    public async Task OtherPixelSizesAreDecodedSeparately()
    {
        _source.Previews["usb/BENCHY~1.BGC"] = FakePreviewSource.Png();
        PrintThumbnails thumbnails = Create();
        List<IImage?> updates = [];

        using IDisposable medium = thumbnails.Request(Definition(), Job(), 40, updates.Add);
        await DeliverAsync("medium thumbnail");
        using IDisposable large = thumbnails.Request(Definition(), Job(), 144, updates.Add);
        await DeliverAsync("large thumbnail at 200 %");

        Assert.Equal([40, 144], _decoder.Decoded.Select(static decoded => decoded.MaxSide));
        Assert.NotSame(updates[0], updates[1]);
        Assert.Equal(2, thumbnails.DecodedCount);
    }

    [Fact]
    public async Task ImageIsDisposedWhenTheLastRequestIsDisposed()
    {
        _source.Previews["usb/BENCHY~1.BGC"] = FakePreviewSource.Png();
        PrintThumbnails thumbnails = Create();
        List<IImage?> updates = [];
        IDisposable a = thumbnails.Request(Definition(), Job(), 72, updates.Add);
        await DeliverAsync("first thumbnail");
        IDisposable b = thumbnails.Request(Definition(), Job(), 72, updates.Add);
        await DeliverAsync("second thumbnail");
        FakeImage image = Assert.Single(_decoder.Images);

        a.Dispose();
        Assert.False(image.IsDisposed);
        b.Dispose();
        b.Dispose();

        Assert.Equal(1, image.DisposeCount);
        Assert.Equal(0, thumbnails.DecodedCount);
    }

    [Fact]
    public async Task ImageDecodedAfterTheRequestWasDisposedIsDisposedAndNeverDelivered()
    {
        _source.Previews["usb/BENCHY~1.BGC"] = FakePreviewSource.Png();
        _decoder.Gate.Reset();
        PrintThumbnails thumbnails = Create();
        List<IImage?> updates = [];
        IDisposable request = thumbnails.Request(Definition(), Job(), 40, updates.Add);
        await Waiting.UntilAsync(() => thumbnails.DecodedCount == 1, "decoding started");

        request.Dispose();
        _decoder.Gate.Set();
        await Waiting.UntilAsync(() => _decoder.Images.Count == 1 && _decoder.Images[0].IsDisposed, "late image disposed");
        _dispatcher.RunAll();

        Assert.Empty(updates);
        Assert.Equal(0, thumbnails.DecodedCount);
    }

    [Fact]
    public async Task UpdatePostedBeforeTheRequestWasDisposedIsDropped()
    {
        _source.Previews["usb/BENCHY~1.BGC"] = FakePreviewSource.Png();
        PrintThumbnails thumbnails = Create();
        List<IImage?> updates = [];
        IDisposable request = thumbnails.Request(Definition(), Job(), 40, updates.Add);
        await Waiting.UntilAsync(() => _dispatcher.Pending > 0, "update posted");

        request.Dispose();
        _dispatcher.RunAll();

        Assert.Empty(updates);
        Assert.True(Assert.Single(_decoder.Images).IsDisposed);
    }

    [Fact]
    public async Task DisposingWhileThePreviewIsDownloadingCancelsTheWait()
    {
        _source.Hold = true;
        _source.Previews["usb/BENCHY~1.BGC"] = FakePreviewSource.Png();
        PrintThumbnails thumbnails = Create();
        List<IImage?> updates = [];
        IDisposable request = thumbnails.Request(Definition(), Job(), 40, updates.Add);

        request.Dispose();
        _source.Release();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _dispatcher.RunAll();

        Assert.Empty(updates);
        Assert.Empty(_decoder.Decoded);
    }

    [Fact]
    public async Task JobWithoutAPreviewGetsNone()
    {
        PrintThumbnails thumbnails = Create();
        List<IImage?> updates = [];

        using IDisposable request = thumbnails.Request(Definition(), Job(), 40, updates.Add);
        await DeliverAsync("no thumbnail");

        Assert.Null(Assert.Single(updates));
        Assert.Empty(_decoder.Decoded);
    }

    [Fact]
    public async Task CorruptPngGetsNoneWithoutAnExceptionAndIsNotDecodedAgain()
    {
        PrintPreview corrupt = FakePreviewSource.Png();
        _source.Previews["usb/BENCHY~1.BGC"] = corrupt;
        _decoder.Corrupt.Add(corrupt);
        PrintThumbnails thumbnails = Create();
        List<IImage?> updates = [];

        using IDisposable request = thumbnails.Request(Definition(), Job(), 40, updates.Add);
        await DeliverAsync("no thumbnail for a corrupt PNG");
        _time.Advance(PrintPreviewCacheOptions.Default.RetryDelay * 3);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _dispatcher.RunAll();

        Assert.Null(Assert.Single(updates));
        Assert.Single(_decoder.Decoded);
        Assert.Single(_source.Requests);
        Assert.DoesNotContain(_logger.Entries, static entry => entry.Level >= Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task MissingPreviewIsAskedForAgainAfterTheRetryDelay()
    {
        PrintThumbnails thumbnails = Create();
        List<IImage?> updates = [];
        using IDisposable request = thumbnails.Request(Definition(), Job(), 40, updates.Add);
        await DeliverAsync("no thumbnail yet");
        Assert.Single(_source.Requests);

        // A transient failure (timeout, offline) ends; the cache answers again after its retry delay.
        _source.Previews["usb/BENCHY~1.BGC"] = FakePreviewSource.Png();
        _time.Advance(PrintPreviewCacheOptions.Default.RetryDelay - TimeSpan.FromSeconds(1));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(_source.Requests);
        _time.Advance(TimeSpan.FromSeconds(1));
        await DeliverAsync("thumbnail after the retry");

        Assert.Equal(2, _source.RequestCount);
        Assert.Null(updates[0]);
        Assert.IsType<FakeImage>(updates[1]);
    }

    [Fact]
    public async Task DisposedRequestIsNotRetried()
    {
        PrintThumbnails thumbnails = Create();
        IDisposable request = thumbnails.Request(Definition(), Job(), 40, static _ => { });
        await DeliverAsync("no thumbnail");

        request.Dispose();
        _time.Advance(PrintPreviewCacheOptions.Default.RetryDelay * 2);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Single(_source.Requests);
    }

    [Fact]
    public async Task NextJobDecodesItsOwnPreviewAndTheOldImageGoesWithItsRequest()
    {
        _source.Previews["usb/BENCHY~1.BGC"] = FakePreviewSource.Png();
        _source.Previews["usb/BOX~1.BGC"] = FakePreviewSource.Png(red: 0x20);
        PrintThumbnails thumbnails = Create();
        List<IImage?> updates = [];
        IDisposable first = thumbnails.Request(Definition(), Job(), 40, updates.Add);
        await DeliverAsync("first job");

        using IDisposable second = thumbnails.Request(Definition(), Job(2, "usb/BOX~1.BGC"), 40, updates.Add);
        first.Dispose();
        await DeliverAsync("second job");

        Assert.Equal(2, _decoder.Images.Count);
        Assert.True(_decoder.Images[0].IsDisposed);
        Assert.False(_decoder.Images[1].IsDisposed);
        Assert.Same(_decoder.Images[1], updates[1]);
        Assert.Equal(1, thumbnails.DecodedCount);
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        PrintThumbnails thumbnails = Create();

        Assert.Throws<ArgumentNullException>(() => thumbnails.Request(null!, Job(), 40, static _ => { }));
        Assert.Throws<ArgumentNullException>(() => thumbnails.Request(Definition(), null!, 40, static _ => { }));
        Assert.Throws<ArgumentNullException>(() => thumbnails.Request(Definition(), Job(), 40, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => thumbnails.Request(Definition(), Job(), 0, static _ => { }));
    }
}
