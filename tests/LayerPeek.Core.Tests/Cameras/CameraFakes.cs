using System.Runtime.CompilerServices;
using System.Threading.Channels;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>
/// Scriptable <see cref="ICameraFeed"/>: the test pushes frames, a failure or the end of the stream.
/// Like a real decoder it reuses one pixel buffer, so a frame is only valid until the next read.
/// Reads honour cancellation.
/// </summary>
internal sealed class FakeCameraFeed : ICameraFeed
{
    private readonly Channel<Step> _steps = Channel.CreateUnbounded<Step>();
    private readonly Channel<bool> _reads = Channel.CreateUnbounded<bool>();
    private readonly byte[] _buffer = new byte[16 * 1024];

    public bool IsDisposed { get; private set; }

    public int EnumerationCount { get; private set; }

    /// <summary>Number of frames handed out so far.</summary>
    public int FramesRead { get; private set; }

    /// <summary>Queues a <paramref name="width"/> x <paramref name="height"/> frame whose pixels are all <paramref name="fill"/>.</summary>
    public void PushFrame(byte fill, int width = 2, int height = 2) => _steps.Writer.TryWrite(new Step(fill, width, height, null, End: false));

    public void Fail(Exception exception) => _steps.Writer.TryWrite(new Step(0, 0, 0, exception, End: false));

    public void End() => _steps.Writer.TryWrite(new Step(0, 0, 0, null, End: true));

    /// <summary>Simulates the decoder writing the next frame into its reused buffer.</summary>
    public void OverwriteBuffer(byte fill) => _buffer.AsSpan().Fill(fill);

    /// <summary>Waits until the stream starts waiting for the next frame.</summary>
    public async Task NextReadAsync() => await _reads.Reader.ReadAsync().AsTask().WaitAsync(TestTimeouts.Wait);

    public async IAsyncEnumerable<CameraFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        EnumerationCount++;
        while (true)
        {
            _reads.Writer.TryWrite(true);
            Step step = await _steps.Reader.ReadAsync(cancellationToken);
            if (step.Error is not null)
            {
                throw step.Error;
            }

            if (step.End)
            {
                yield break;
            }

            int stride = step.Width * CameraFrame.BytesPerPixel;
            Memory<byte> pixels = _buffer.AsMemory(0, stride * step.Height);
            pixels.Span.Fill(step.Fill);
            FramesRead++;
            yield return new CameraFrame(step.Width, step.Height, stride, pixels);
        }
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    private sealed record Step(byte Fill, int Width, int Height, Exception? Error, bool End);
}

/// <summary>A misbehaving stream: enumeration and/or disposal throw synchronously.</summary>
internal sealed class ThrowingCameraFeed(bool throwOnRead, bool throwOnDispose) : ICameraFeed
{
    public bool DisposeCalled { get; private set; }

    public IAsyncEnumerable<CameraFrame> ReadFramesAsync(CancellationToken cancellationToken) =>
        throwOnRead ? throw new InvalidOperationException("read failed") : AsyncEnumerable.Empty<CameraFrame>();

    public ValueTask DisposeAsync()
    {
        DisposeCalled = true;
        return throwOnDispose ? throw new DisposeFailedException() : ValueTask.CompletedTask;
    }

    internal sealed class DisposeFailedException() : Exception("dispose failed");
}

/// <summary>Creates a new <see cref="FakeCameraFeed"/> per call (or runs <see cref="Behavior"/>) and records the calls.</summary>
internal sealed class FakeCameraFeedFactory : ICameraFeedFactory
{
    private readonly Channel<FakeCameraFeed?> _created = Channel.CreateUnbounded<FakeCameraFeed?>();
    private int _createCount;

    /// <summary>Replaces the default behaviour; return <see langword="null"/> to fall back to a new fake stream.</summary>
    public Func<Uri, ICameraFeed?>? Behavior { get; set; }

    public int CreateCount => Volatile.Read(ref _createCount);

    public Uri? LastUri { get; private set; }

    public ICameraFeed Create(Uri streamUri)
    {
        Interlocked.Increment(ref _createCount);
        LastUri = streamUri;
        try
        {
            if (Behavior?.Invoke(streamUri) is { } custom)
            {
                _created.Writer.TryWrite(custom as FakeCameraFeed);
                return custom;
            }
        }
        catch
        {
            _created.Writer.TryWrite(null);
            throw;
        }

        FakeCameraFeed stream = new();
        _created.Writer.TryWrite(stream);
        return stream;
    }

    /// <summary>The stream returned by the next <see cref="Create"/> call (<see langword="null"/> when that call threw).</summary>
    public async Task<FakeCameraFeed?> NextStreamAsync() => await _created.Reader.ReadAsync().AsTask().WaitAsync(TestTimeouts.Wait);

    public bool HasPendingCreate => _created.Reader.TryPeek(out _);
}

/// <summary>Records every reported status synchronously.</summary>
internal sealed class RecordingProgress : IProgress<CameraStatus>
{
    private readonly Channel<CameraStatus> _reports = Channel.CreateUnbounded<CameraStatus>();
    private readonly List<CameraStatus> _all = [];
    private readonly Lock _lock = new();

    public Action<CameraStatus>? OnReport { get; set; }

    public IReadOnlyList<CameraStatus> All
    {
        get
        {
            lock (_lock)
            {
                return [.. _all];
            }
        }
    }

    public void Report(CameraStatus value)
    {
        lock (_lock)
        {
            _all.Add(value);
        }

        _reports.Writer.TryWrite(value);
        OnReport?.Invoke(value);
    }

    public async Task<CameraStatus> NextAsync() => await _reports.Reader.ReadAsync().AsTask().WaitAsync(TestTimeouts.Wait);

    public bool HasPending => _reports.Reader.TryPeek(out _);
}
