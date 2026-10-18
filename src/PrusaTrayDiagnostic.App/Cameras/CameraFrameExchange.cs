using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>
/// An owned copy of a camera frame (BGRA, rows tightly packed). Reused for later frames: its pixels are reallocated only
/// when a frame needs more room than any before. Valid until it is handed back to the <see cref="CameraFrameExchange"/>
/// (the next <see cref="CameraFrameExchange.TakeLatest"/>).
/// </summary>
internal sealed class CameraFrameBuffer
{
    private byte[] _pixels = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Stride => Width * CameraFrame.BytesPerPixel;

    /// <summary>Exactly <see cref="Stride"/> × <see cref="Height"/> bytes.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels.AsSpan(0, Stride * Height);

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
            _pixels = GC.AllocateUninitializedArray<byte>(required);
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
    }
}

/// <summary>
/// Hands the newest frame from the streaming thread to the UI thread without blocking either side and without
/// allocating per frame (triple buffering): the stream copies each borrowed frame into its back buffer and swaps it into
/// the "ready" slot; the UI swaps the ready slot into its front buffer when it gets to it. A frame the UI did not take
/// before the next one arrived is dropped, so a busy UI never builds a queue (and never adds latency). The stream asks
/// for a UI notification only when none is pending, so at most one UI callback is queued at any time.
/// One producer (the streaming loop) and one consumer (the UI thread).
/// </summary>
internal sealed class CameraFrameExchange
{
    private readonly Lock _gate = new();

    // _back belongs to the producer and _front to the consumer; _ready is swapped under _gate.
    private CameraFrameBuffer _back = new();
    private CameraFrameBuffer _ready = new();
    private CameraFrameBuffer _front = new();
    private bool _readyIsNew;
    private bool _notificationPending;
    private long _dropped;

    /// <summary>Frames replaced before the UI took them.</summary>
    public long DroppedFrames => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Streaming thread: copies <paramref name="frame"/> (borrowed, valid only during this call) and makes it the newest.
    /// </summary>
    /// <returns><see langword="true"/> when the UI must be notified (it has no notification pending).</returns>
    public bool Publish(in CameraFrame frame)
    {
        _back.CopyFrom(frame);
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
