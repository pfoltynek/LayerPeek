namespace LayerPeek.Core.Cameras;

/// <summary>
/// An owned copy of a camera frame (BGRA, or 8-bit grayscale for a subscription that asked for it, T-153; rows tightly
/// packed). Reused for later frames: its pixels are reallocated only when a frame needs more room than any before. Valid
/// until it is handed back to the <see cref="CameraFrameExchange"/> (the next <see cref="CameraFrameExchange.TakeLatest"/>).
/// </summary>
public sealed class CameraFrameBuffer
{
    private byte[] _pixels = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Width of the camera's image the frame was scaled from (T-184, <see cref="CameraFrame.SourceWidth"/>).</summary>
    public int SourceWidth { get; private set; }

    /// <summary>Height of the camera's image the frame was scaled from (T-184, <see cref="CameraFrame.SourceHeight"/>).</summary>
    public int SourceHeight { get; private set; }

    /// <summary><see cref="CameraPixelFormat.Bgra32"/> unless the exchange converts to grayscale.</summary>
    public CameraPixelFormat Format { get; private set; }

    /// <summary>Which stream run produced the frame (see <see cref="CameraFrameExchange.Publish"/>).</summary>
    public int Generation { get; internal set; }

    public int Stride => Width * (Format == CameraPixelFormat.Gray8 ? 1 : CameraFrame.BytesPerPixel);

    /// <summary>Exactly <see cref="Stride"/> × <see cref="Height"/> bytes.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels.AsSpan(0, Stride * Height);

    /// <summary>An owned copy of the frame that stays valid after the buffer is reused.</summary>
    /// <exception cref="InvalidOperationException">No frame was copied into the buffer yet, or it is grayscale (an image is BGRA).</exception>
    public CameraImage ToImage()
    {
        if (Width == 0 || Format != CameraPixelFormat.Bgra32)
        {
            throw new InvalidOperationException("Only a BGRA frame can be copied into an image.");
        }

        return new CameraImage(Width, Height, Pixels.ToArray());
    }

    /// <summary>Converts the borrowed <paramref name="frame"/> (valid only now) into grayscale of <paramref name="size"/> in this buffer.</summary>
    internal void CopyGrayscaleFrom(in CameraFrame frame, CameraGrayscaleSize size, GrayscaleScaler scaler)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            throw new ArgumentException("An empty frame cannot be copied.", nameof(frame));
        }

        if (_pixels.Length < size.Length)
        {
            _pixels = GC.AllocateUninitializedArray<byte>(size.Length);
        }

        scaler.Scale(frame, size, _pixels);
        Width = size.Width;
        Height = size.Height;
        SourceWidth = frame.SourceWidth;
        SourceHeight = frame.SourceHeight;
        Format = CameraPixelFormat.Gray8;
    }

    /// <summary>Bytes allocated for pixels (for tests: proves that buffers are reused).</summary>
    internal int Capacity => _pixels.Length;

    /// <summary>Copies the borrowed <paramref name="frame"/> (valid only now) into this buffer.</summary>
    public void CopyFrom(in CameraFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            throw new ArgumentException("An empty frame cannot be copied.", nameof(frame));
        }

        int rowBytes = frame.Width * CameraFrame.BytesPerPixel;
        int required = checked(rowBytes * frame.Height);
        if (_pixels.Length < required)
        {
            // A display frame grows step by step while its window is resized (T-184): with some headroom that is a few
            // allocations, not one per size.
            _pixels = GC.AllocateUninitializedArray<byte>(_pixels.Length == 0 ? required : Math.Max(required, (int)Math.Min(Array.MaxLength, _pixels.Length * 3L / 2)));
        }

        ReadOnlySpan<byte> source = frame.Pixels.Span;
        Span<byte> target = _pixels.AsSpan(0, required);
        if (frame.Stride == rowBytes)
        {
            source[..required].CopyTo(target);
        }
        else
        {
            for (int row = 0; row < frame.Height; row++)
            {
                source.Slice(row * frame.Stride, rowBytes).CopyTo(target.Slice(row * rowBytes, rowBytes));
            }
        }

        Width = frame.Width;
        Height = frame.Height;
        SourceWidth = frame.SourceWidth;
        SourceHeight = frame.SourceHeight;
        Format = CameraPixelFormat.Bgra32;
    }
}

/// <summary>
/// Hands the newest frame from the streaming thread to the UI thread without blocking either side and without
/// allocating per frame (triple buffering): the stream copies each borrowed frame into its back buffer and swaps it into
/// the "ready" slot; the UI swaps the ready slot into its front buffer when it gets to it. A frame the UI did not take
/// before the next one arrived is dropped, so a busy UI never builds a queue (and never adds latency). The stream asks
/// for a UI notification only when none is pending, so normally at most one UI callback is queued. Right after
/// <see cref="ResetNotification"/> a callback requested by the previous run may still be queued as well, so two can
/// briefly be queued; that is harmless, the later one finds no new frame and does nothing.
/// One producer (the streaming loop) and one consumer (the UI thread).
/// </summary>
public sealed class CameraFrameExchange
{
    private readonly Lock _gate = new();
    private readonly CameraGrayscaleSize _grayscale;
    private readonly GrayscaleScaler? _scaler;

    // _back belongs to the producer and _front to the consumer; _ready is swapped under _gate.
    private CameraFrameBuffer _back = new();
    private CameraFrameBuffer _ready = new();
    private CameraFrameBuffer _front = new();
    private bool _readyIsNew;
    private bool _notificationPending;
    private long _dropped;

    /// <summary>Copies frames as they are (BGRA).</summary>
    public CameraFrameExchange()
    {
    }

    /// <summary>Copies frames as 8-bit grayscale of <paramref name="grayscale"/> (T-153), converted while copying.</summary>
    public CameraFrameExchange(CameraGrayscaleSize grayscale)
    {
        if (!grayscale.IsValid)
        {
            throw new ArgumentException("The grayscale size is empty.", nameof(grayscale));
        }

        _grayscale = grayscale;
        _scaler = new GrayscaleScaler();
    }

    /// <summary>Frames replaced before the UI took them.</summary>
    public long DroppedFrames => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Streaming thread: copies <paramref name="frame"/> (borrowed, valid only during this call) and makes it the newest.
    /// </summary>
    /// <returns><see langword="true"/> when the UI must be notified (it has no notification pending).</returns>
    public bool Publish(in CameraFrame frame, int generation = 0)
    {
        if (_scaler is null)
        {
            _back.CopyFrom(frame);
        }
        else
        {
            _back.CopyGrayscaleFrom(frame, _grayscale, _scaler);
        }

        _back.Generation = generation;
        lock (_gate)
        {
            (_back, _ready) = (_ready, _back);
            if (_readyIsNew)
            {
                Interlocked.Increment(ref _dropped);
            }

            _readyIsNew = true;
            if (_notificationPending)
            {
                return false;
            }

            _notificationPending = true;
            return true;
        }
    }

    /// <summary>
    /// Streaming thread, before a new run publishes: forgets the pending notification, so that the new run notifies
    /// again even if the last request of an earlier run was never queued (its run ended because queueing it failed).
    /// When that request was queued after all, a second callback may briefly be queued too (see the type remarks).
    /// </summary>
    public void ResetNotification()
    {
        lock (_gate)
        {
            _notificationPending = false;
        }
    }

    /// <summary>
    /// UI thread: the newest frame since the last call, or <see langword="null"/> when there is none. The buffer stays
    /// valid until the next call. Clears the pending notification, so the next published frame notifies again.
    /// </summary>
    public CameraFrameBuffer? TakeLatest()
    {
        lock (_gate)
        {
            _notificationPending = false;
            if (!_readyIsNew)
            {
                return null;
            }

            (_front, _ready) = (_ready, _front);
            _readyIsNew = false;
            return _front;
        }
    }
}
