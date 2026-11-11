using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>
/// Scriptable feed with packets (T-143): yields a stream description first, then what the test pushes, filtered by the
/// demand like a real decoder (packets only while wanted, frames only while wanted, a key frame also while one is due,
/// T-153; a skipped frame counts as not decoded). Frames are converted like the decoder does (T-184, T-189): in full
/// resolution while that is due, scaled down to the demand's display size, and into grayscale of the demand's size while
/// that is due (unless it <see cref="IgnoresConversions"/>); the scaled frame is filled with <see cref="DisplayFill"/>(fill),
/// so a test can tell which one a subscriber got; the grayscale one is the luma of the frame (the fill for a gray one). Packet
/// and frame buffers are reused, so they are only valid until the next read.
/// </summary>
internal sealed class FakeItemFeed : ICameraFeed
{
    public static readonly CameraStreamInfo Info = new(CameraVideoCodec.H264, 1280, 720, CameraTimeBase.Rtp90kHz, new byte[] { 0, 0, 0, 1, 0x67 });

    private readonly Channel<Step> _steps = Channel.CreateUnbounded<Step>();
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<CameraStreamDemand> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly byte[] _frameBuffer = new byte[64 * 64 * CameraFrame.BytesPerPixel];
    private readonly byte[] _displayBuffer = new byte[64 * 64 * CameraFrame.BytesPerPixel];
    // Enough for the 320×180 samples of print failure detection with padded rows.
    private readonly byte[] _grayscaleBuffer = new byte[64 * 1024];
    private readonly byte[] _packetBuffer = new byte[256];
    private int _decoded;
    private int _consumed;
    private int _fullConversions;
    private int _displayConversions;
    private int _grayscaleConversions;

    public Uri? Uri { get; init; }

    /// <summary>
    /// Yields every decoded frame in full resolution only, whatever the demand wants converted (like a feed with the default
    /// <see cref="ICameraFeed.ReadAsync"/>, which knows no demand): no display or grayscale frames.
    /// </summary>
    public bool IgnoresConversions { get; init; }

    public Task Disposed => _disposed.Task;

    public bool IsDisposed => _disposed.Task.IsCompleted;

    /// <summary>The demand the reader passed; set once reading started.</summary>
    public Task<CameraStreamDemand> Started => _started.Task;

    /// <summary>Frames handed out (i.e. decoded); a pushed frame that is not wanted is skipped and not counted.</summary>
    public int DecodedFrames => Volatile.Read(ref _decoded);

    /// <summary>Items the consumer finished with (it asked for the next one).</summary>
    public int Consumed => Volatile.Read(ref _consumed);

    /// <summary>Decoded frames converted in full resolution (T-184).</summary>
    public int FullConversions => Volatile.Read(ref _fullConversions);

    /// <summary>Decoded frames converted scaled down for display (T-184).</summary>
    public int DisplayConversions => Volatile.Read(ref _displayConversions);

    /// <summary>Decoded frames converted into grayscale (T-189).</summary>
    public int GrayscaleConversions => Volatile.Read(ref _grayscaleConversions);

    /// <summary>The fill of the scaled display frame of a frame pushed with <paramref name="fill"/>.</summary>
    public static byte DisplayFill(byte fill) => (byte)(fill + 100);

    public void PushFrame(byte fill, int width = 4, int height = 2, bool keyFrame = false) => _steps.Writer.TryWrite(new Step(StepKind.Frame, fill, width, height, keyFrame, null));

    /// <summary>A frame with the test's own BGRA pixels (rows packed), e.g. a pattern.</summary>
    public void PushImage(byte[] pixels, int width, int height, bool keyFrame = true) => _steps.Writer.TryWrite(new Step(StepKind.Frame, 0, width, height, keyFrame, null, pixels));

    public void PushPacket(byte fill, bool keyFrame = false, int length = 16) => _steps.Writer.TryWrite(new Step(StepKind.Packet, fill, length, 0, keyFrame, null));

    public void Fail(Exception exception) => _steps.Writer.TryWrite(new Step(StepKind.Fail, 0, 0, 0, KeyFrame: false, exception));

    public void End() => _steps.Writer.TryWrite(new Step(StepKind.End, 0, 0, 0, KeyFrame: false, null));

    public IAsyncEnumerable<CameraFrame> ReadFramesAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("The hub reads items.");

    public async IAsyncEnumerable<CameraStreamItem> ReadAsync(CameraStreamDemand demand, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _started.TrySetResult(demand);
        yield return CameraStreamItem.Started(Info);
        Interlocked.Increment(ref _consumed);
        long pts = 0;
        while (true)
        {
            Step step = await _steps.Reader.ReadAsync(cancellationToken);
            switch (step.Kind)
            {
                case StepKind.Fail:
                    throw step.Error!;
                case StepKind.End:
                    yield break;
                case StepKind.Packet when demand.Packets:
                    _packetBuffer.AsSpan(0, step.A).Fill(step.Fill);
                    pts += 4500;
                    yield return CameraStreamItem.FromPacket(new CameraPacket(_packetBuffer.AsMemory(0, step.A), pts, pts, 4500, step.KeyFrame, CameraTimeBase.Rtp90kHz));
                    Interlocked.Increment(ref _consumed);
                    break;
                case StepKind.Frame when demand.Frames || (step.KeyFrame && demand.KeyFrameDue):
                    int stride = step.A * CameraFrame.BytesPerPixel;
                    Memory<byte> pixels = step.Pixels ?? _frameBuffer.AsMemory(0, stride * step.B);
                    if (step.Pixels is null)
                    {
                        pixels.Span.Fill(step.Fill);
                    }

                    Interlocked.Increment(ref _decoded);
                    yield return Convert(demand, new CameraFrame(step.A, step.B, stride, pixels), step.Fill);
                    Interlocked.Increment(ref _consumed);
                    break;
                default:
                    // Not wanted: skipped (a real decoder never decodes it).
                    Interlocked.Increment(ref _consumed);
                    break;
            }
        }
    }

    /// <summary>Overwrites the reused buffers, like the decoder writing the next frame and packet.</summary>
    public void OverwriteBuffers(byte fill)
    {
        _frameBuffer.AsSpan().Fill(fill);
        _displayBuffer.AsSpan().Fill(fill);
        _grayscaleBuffer.AsSpan().Fill(fill);
        _packetBuffer.AsSpan().Fill(fill);
    }

    /// <summary>The conversions of <c>FFmpegDecoder.Convert</c> (T-184, T-189), without the pixel work.</summary>
    private CameraStreamItem Convert(CameraStreamDemand demand, CameraFrame decoded, byte fill)
    {
        if (IgnoresConversions)
        {
            Interlocked.Increment(ref _fullConversions);
            return CameraStreamItem.FromFrame(decoded);
        }

        bool full = demand.FullResolutionDue;
        CameraGrayscaleFrame grayscale = default;
        if (demand.GrayscaleDue && demand.GrayscaleSize is { } size)
        {
            // Rows padded like the decoder's aligned rows.
            int stride = size.Width + 3;
            Memory<byte> pixels = _grayscaleBuffer.AsMemory(0, stride * size.Height);
            Luma(decoded, size, stride, pixels.Span);
            grayscale = new CameraGrayscaleFrame(size.Width, size.Height, stride, pixels, decoded.Width, decoded.Height);
            Interlocked.Increment(ref _grayscaleConversions);
        }

        CameraFrame display = default;
        if (demand.DisplaySize is { } displaySize)
        {
            CameraFrameSize fit = displaySize.Fit(decoded.Width, decoded.Height);
            if (fit.Width < decoded.Width || fit.Height < decoded.Height)
            {
                int stride = fit.Width * CameraFrame.BytesPerPixel;
                Memory<byte> pixels = _displayBuffer.AsMemory(0, stride * fit.Height);
                pixels.Span.Fill(DisplayFill(fill));
                display = new CameraFrame(fit.Width, fit.Height, stride, pixels, decoded.Width, decoded.Height);
                Interlocked.Increment(ref _displayConversions);
            }
            else
            {
                full = true;
            }
        }

        if (full)
        {
            Interlocked.Increment(ref _fullConversions);
        }

        return CameraStreamItem.FromFrames(full ? decoded : default, display, grayscale);
    }

    /// <summary>The luma of <paramref name="frame"/> (BT.601), area averaged to <paramref name="size"/> (nearest when larger).</summary>
    private static void Luma(in CameraFrame frame, CameraGrayscaleSize size, int stride, Span<byte> target)
    {
        ReadOnlySpan<byte> source = frame.Pixels.Span;
        for (int ty = 0; ty < size.Height; ty++)
        {
            int y0 = Math.Min(ty * frame.Height / size.Height, frame.Height - 1);
            int y1 = Math.Max((ty + 1) * frame.Height / size.Height, y0 + 1);
            for (int tx = 0; tx < size.Width; tx++)
            {
                int x0 = Math.Min(tx * frame.Width / size.Width, frame.Width - 1);
                int x1 = Math.Max((tx + 1) * frame.Width / size.Width, x0 + 1);
                int sum = 0;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        int i = (y * frame.Stride) + (x * CameraFrame.BytesPerPixel);
                        sum += ((29 * source[i]) + (150 * source[i + 1]) + (77 * source[i + 2])) >> 8;
                    }
                }

                int count = (y1 - y0) * (x1 - x0);
                target[(ty * stride) + tx] = (byte)((sum + (count >> 1)) / count);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _steps.Writer.TryComplete();
        _disposed.TrySetResult();
        return ValueTask.CompletedTask;
    }

    private enum StepKind
    {
        Frame,
        Packet,
        Fail,
        End,
    }

    private sealed record Step(StepKind Kind, byte Fill, int A, int B, bool KeyFrame, Exception? Error, byte[]? Pixels = null);
}

/// <summary>Creates a <see cref="FakeItemFeed"/> per connection and lets the test wait for the n-th one.</summary>
internal sealed class FakeItemFeedFactory : ICameraFeedFactory
{
    private readonly Lock _gate = new();

    /// <summary>The feeds it creates ignore the demand's conversions (<see cref="FakeItemFeed.IgnoresConversions"/>).</summary>
    public bool IgnoresConversions { get; init; }

    private readonly List<FakeItemFeed> _feeds = [];
    private readonly List<(int Number, TaskCompletionSource<FakeItemFeed> Done)> _waiters = [];

    public int CreateCount
    {
        get
        {
            lock (_gate)
            {
                return _feeds.Count;
            }
        }
    }

    public ICameraFeed Create(Uri streamUri)
    {
        FakeItemFeed feed = new() { Uri = streamUri, IgnoresConversions = IgnoresConversions };
        lock (_gate)
        {
            _feeds.Add(feed);
            foreach ((int number, TaskCompletionSource<FakeItemFeed> done) in _waiters)
            {
                if (_feeds.Count >= number)
                {
                    done.TrySetResult(_feeds[number - 1]);
                }
            }
        }

        return feed;
    }

    /// <summary>The <paramref name="number"/>-th connection (1-based), once reading started.</summary>
    public async Task<FakeItemFeed> FeedAsync(int number)
    {
        Task<FakeItemFeed> wait;
        lock (_gate)
        {
            if (_feeds.Count >= number)
            {
                wait = Task.FromResult(_feeds[number - 1]);
            }
            else
            {
                TaskCompletionSource<FakeItemFeed> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((number, done));
                wait = done.Task;
            }
        }

        FakeItemFeed feed = await wait.WaitAsync(TestTimeouts.Wait);
        await feed.Started.WaitAsync(TestTimeouts.Wait);
        return feed;
    }
}

/// <summary>A frame subscriber that counts notifications and records every status.</summary>
internal sealed class FrameProbe
{
    private int _notified;

    public ConcurrentQueue<CameraStatus> Statuses { get; } = new();

    public ConcurrentQueue<CameraStreamSubscription> Ended { get; } = new();

    public int Notified => Volatile.Read(ref _notified);

    /// <param name="shortensReconnectWait">A camera window's lease (T-188).</param>
    public CameraFrameSubscriptionOptions Options(TimeSpan minInterval = default, bool shortensReconnectWait = false) => new()
    {
        FrameAvailable = () => Interlocked.Increment(ref _notified),
        Status = new Recorder(Statuses),
        Ended = Ended.Enqueue,
        MinFrameInterval = minInterval,
        ShortensReconnectWait = shortensReconnectWait,
    };

    private sealed class Recorder(ConcurrentQueue<CameraStatus> statuses) : IProgress<CameraStatus>
    {
        public void Report(CameraStatus value) => statuses.Enqueue(value);
    }
}

internal static class Eventually
{
    /// <summary>Polls <paramref name="condition"/> until it holds or the test timeout passes.</summary>
    public static async Task TrueAsync(Func<bool> condition, string description)
    {
        DateTime deadline = DateTime.UtcNow + TestTimeouts.Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for: {description}");
            }

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
