using Avalonia.Media;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Tray;
using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Previews;

/// <summary>
/// The decoded G-code thumbnails of the camera windows (T-087), one instance for the app. A
/// <see cref="Request">request</see> asks the preview source (the in-memory <see cref="PrintPreviewCache"/> of T-086)
/// for the large preview of a job and decodes it off the caller's thread to the pixel size the card needs.
/// <list type="bullet">
/// <item>Only <see cref="PrintPreviewSize.Large"/> is used: stock Prusa profiles embed the 16 × 16 icon only as QOI, so
/// the small one is a 404 on a real printer (docs/api/prusalink.md). The 380 × 285 PNG is scaled down once while
/// decoding, never kept at full size.</item>
/// <item>Requests of the same preview (the cache returns the same instance for a job) and pixel size share one decoded
/// image, so several camera windows of a printer decode once. It is disposed when its last request is disposed.</item>
/// <item>No preview (none in the file, not fetched yet, a transient failure) is reported as <see langword="null"/> and
/// asked for again after the cache's retry delay while the request lives. A PNG that cannot be decoded is reported as
/// <see langword="null"/> once and not decoded again.</item>
/// <item>Updates run on the UI thread (posted, never inline) and never after the request was disposed.</item>
/// </list>
/// </summary>
public sealed partial class PrintThumbnails
{
    /// <summary>The preview the thumbnails are made from.</summary>
    public const PrintPreviewSize PreviewSize = PrintPreviewSize.Large;

    private readonly IPrintPreviewSource _source;
    private readonly IPrintThumbnailDecoder _decoder;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly ILogger<PrintThumbnails> _logger;
    private readonly TimeSpan _retryDelay;
    private readonly Lock _lock = new();
    private readonly Dictionary<DecodeKey, Decoded> _decoded = [];

    /// <param name="retryDelay">How long to wait before asking again for a missing preview; default: the cache's retry delay.</param>
    internal PrintThumbnails(
        IPrintPreviewSource source,
        IPrintThumbnailDecoder decoder,
        IUiDispatcher dispatcher,
        TimeProvider time,
        ILogger<PrintThumbnails> logger,
        TimeSpan? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _retryDelay = retryDelay ?? PrintPreviewCacheOptions.Default.RetryDelay;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_retryDelay, TimeSpan.Zero, nameof(retryDelay));
        _source = source;
        _decoder = decoder;
        _dispatcher = dispatcher;
        _time = time;
        _logger = logger;
    }

    /// <summary>Decoded (or decoding) images that a request still holds; for tests.</summary>
    internal int DecodedCount
    {
        get
        {
            lock (_lock)
            {
                return _decoded.Count;
            }
        }
    }

    /// <summary>
    /// UI thread: starts getting the thumbnail of <paramref name="job"/> (pass the job of the printer's latest snapshot)
    /// with its longer side at most <paramref name="maxSide"/> pixels. <paramref name="update"/> gets the image, or
    /// <see langword="null"/> while there is none, on the UI thread; the image stays valid until the returned request is
    /// disposed. Never blocks. Dispose the request on the UI thread when the thumbnail is no longer shown.
    /// </summary>
    public IDisposable Request(PrinterDefinition printer, PrintJob job, int maxSide, Action<IImage?> update)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSide, 1);
        ArgumentNullException.ThrowIfNull(update);
        ThumbnailRequest request = new(this, printer, job, maxSide, update);
        request.Start();
        return request;
    }

    private static void DisposeImage(IImage? image) => (image as IDisposable)?.Dispose();

    /// <summary>
    /// The shared decoding of <paramref name="preview"/> at <paramref name="maxSide"/>, now held by
    /// <paramref name="request"/> instead of what it held before; <see langword="null"/> when the request is already released.
    /// </summary>
    private Decoded? Acquire(ThumbnailRequest request, PrintPreview preview, int maxSide)
    {
        Decoded? dropped = null;
        Decoded decoded;
        lock (_lock)
        {
            if (request.IsReleased)
            {
                return null;
            }

            if (request.Lease is { } held && ReferenceEquals(held.Key.Preview, preview))
            {
                return held;
            }

            DecodeKey key = new(preview, maxSide);
            if (!_decoded.TryGetValue(key, out decoded!))
            {
                // Task.Run: decoding a PNG takes milliseconds and must run on neither the UI nor the monitor thread.
                decoded = new Decoded(key, Task.Run(() => Decode(preview, maxSide)));
                _decoded.Add(key, decoded);
            }

            decoded.References++;
            dropped = Unreference(request.Lease);
            request.Lease = decoded;
        }

        DisposeWhenDecoded(dropped);
        return decoded;
    }

    /// <summary>The request is gone: its image is disposed when no other request holds it.</summary>
    private void Release(ThumbnailRequest request)
    {
        Decoded? dropped;
        lock (_lock)
        {
            request.IsReleased = true;
            dropped = Unreference(request.Lease);
            request.Lease = null;
        }

        DisposeWhenDecoded(dropped);
    }

    /// <summary>Under the lock: one reference less; the decoding when that was the last one (then it is forgotten).</summary>
    private Decoded? Unreference(Decoded? decoded)
    {
        if (decoded is null || --decoded.References > 0)
        {
            return null;
        }

        _decoded.Remove(decoded.Key);
        return decoded;
    }

    private static void DisposeWhenDecoded(Decoded? decoded)
    {
        if (decoded is null)
        {
            return;
        }

        // Decode never throws (failures are null), so the task always has a result.
        if (decoded.Image.IsCompleted)
        {
            DisposeImage(decoded.Image.Result);
        }
        else
        {
            decoded.Image.ContinueWith(static task => DisposeImage(task.Result), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private IImage? Decode(PrintPreview preview, int maxSide)
    {
        try
        {
            return _decoder.Decode(preview, maxSide);
        }
        catch (Exception ex)
        {
            // A corrupt or truncated PNG (only its header was checked): no thumbnail. The type is enough, the data is the printer's.
            string failure = ex.GetType().Name;
            LogDecodeFailed(preview.Width, preview.Height, failure);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "A G-code preview of {Width} × {Height} px could not be decoded ({Failure}); the card shows no thumbnail.")]
    private partial void LogDecodeFailed(int width, int height, string failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Getting a G-code thumbnail failed unexpectedly ({Failure}); the card shows no thumbnail.")]
    private partial void LogRequestFailed(string failure);

    /// <summary>One decoding: the preview instance (reference equality) and the pixel size.</summary>
    private readonly record struct DecodeKey(PrintPreview Preview, int MaxSide);

    /// <summary>A shared decoded image and how many requests hold it (under the lock).</summary>
    private sealed class Decoded(DecodeKey key, Task<IImage?> image)
    {
        public DecodeKey Key { get; } = key;

        public Task<IImage?> Image { get; } = image;

        public int References { get; set; }
    }

    private sealed class ThumbnailRequest(PrintThumbnails owner, PrinterDefinition printer, PrintJob job, int maxSide, Action<IImage?> update) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();

        // UI thread.
        private bool _disposed;

        /// <summary>Under the owner's lock: the decoding this request holds.</summary>
        public Decoded? Lease { get; set; }

        /// <summary>Under the owner's lock: disposed; takes no decoding any more.</summary>
        public bool IsReleased { get; set; }

        public void Start() => _ = RunAsync(_cancellation.Token);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellation.Cancel();
            owner.Release(this);
            _cancellation.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                bool reportedNone = false;
                while (true)
                {
                    // Non-blocking by contract; the cache fetches on the thread pool.
                    PrintPreview? preview = await owner._source.GetAsync(printer, job, PreviewSize, cancellationToken).ConfigureAwait(false);
                    if (preview is not null)
                    {
                        if (owner.Acquire(this, preview, maxSide) is not { } decoded)
                        {
                            return;
                        }

                        IImage? image = await decoded.Image.WaitAsync(cancellationToken).ConfigureAwait(false);
                        if (image is not null || !reportedNone)
                        {
                            Deliver(image);
                        }

                        // An image, or a PNG that cannot be decoded: asking again would give the same preview.
                        return;
                    }

                    // Started before the update is posted, so that a clock moved after the update is not missed.
                    Task retry = Task.Delay(owner._retryDelay, owner._time, cancellationToken);
                    if (!reportedNone)
                    {
                        reportedNone = true;
                        Deliver(null);
                    }

                    await retry.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Disposed.
            }
            catch (Exception ex)
            {
                // The source never throws by contract; a broken one must not take the window down.
                string failure = ex.GetType().Name;
                owner.LogRequestFailed(failure);
            }
        }

        private void Deliver(IImage? image) => owner._dispatcher.Post(() =>
        {
            if (!_disposed)
            {
                update(image);
            }
        });
    }
}
