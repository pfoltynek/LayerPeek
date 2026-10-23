using System.Runtime.CompilerServices;
using System.Threading.Channels;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

/// <summary>Creates <see cref="FakeCameraFeed"/>s (no network, no decoder) and lets the test drive each of them.</summary>
internal sealed class FakeCameraFeedFactory : ICameraFeedFactory
{
    private readonly Lock _gate = new();
    private readonly List<FakeCameraFeed> _feeds = [];
    private readonly List<Uri> _uris = [];
    private readonly List<(int Count, TaskCompletionSource Done)> _waiters = [];

    /// <summary>When set, <see cref="Create"/> throws it.</summary>
    public Exception? CreateFailure { get; set; }

    public IReadOnlyList<FakeCameraFeed> Feeds
    {
        get
        {
            lock (_gate)
            {
                return [.. _feeds];
            }
        }
    }

    public IReadOnlyList<Uri> Uris
    {
        get
        {
            lock (_gate)
            {
                return [.. _uris];
            }
        }
    }

    public ICameraFeed Create(Uri streamUri)
    {
        lock (_gate)
        {
            _uris.Add(streamUri);
            if (CreateFailure is not null)
            {
                throw CreateFailure;
            }

            FakeCameraFeed feed = new();
            _feeds.Add(feed);
            foreach ((int count, TaskCompletionSource done) in _waiters)
            {
                if (_feeds.Count >= count)
                {
                    done.TrySetResult();
                }
            }

            return feed;
        }
    }

    /// <summary>The <paramref name="number"/>-th feed (1-based) once it was created.</summary>
    public async Task<FakeCameraFeed> WaitForFeedAsync(int number)
    {
        Task wait;
        lock (_gate)
        {
            if (_feeds.Count >= number)
            {
                return _feeds[number - 1];
            }

            TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((number, done));
            wait = done.Task;
        }

        await wait.WaitAsync(TestTimeouts.Wait);
        return Feeds[number - 1];
    }
}

/// <summary>One fake connection: the test sends frames, failures or the end of the stream; disposal is recorded.</summary>
internal sealed class FakeCameraFeed : ICameraFeed
{
    private readonly Channel<(int Width, int Height, uint Pixel, Exception? Failure)> _items = Channel.CreateUnbounded<(int, int, uint, Exception?)>();
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _enumerationEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the connection was closed (disposed).</summary>
    public Task Disposed => _disposed.Task;

    /// <summary>Completes when the frame enumerator was disposed.</summary>
    public Task EnumerationEnded => _enumerationEnded.Task;

    private int _consumed;

    public bool IsDisposed => _disposed.Task.IsCompleted;

    /// <summary>Frames the consumer finished with (it asked for the next one).</summary>
    public int Consumed => Volatile.Read(ref _consumed);

    public void Send(int width = 4, int height = 2, byte fill = 1) => _items.Writer.TryWrite((width, height, fill * 0x01010101u, null));

    /// <summary>A frame of one colour: <paramref name="bgra"/> = 0xAARRGGBB (stored B, G, R, A in memory).</summary>
    public void SendColor(int width, int height, uint bgra) => _items.Writer.TryWrite((width, height, bgra, null));

    public void Fail(Exception failure) => _items.Writer.TryWrite((0, 0, 0, failure));

    public void End() => _items.Writer.TryComplete();

    public async IAsyncEnumerable<CameraFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Like a decoder: one buffer, reused (borrowed by every frame).
        byte[] buffer = [];
        try
        {
            await foreach ((int width, int height, uint pixel, Exception? failure) in _items.Reader.ReadAllAsync(cancellationToken))
            {
                if (failure is not null)
                {
                    throw failure;
                }

                int length = width * height * CameraFrame.BytesPerPixel;
                if (buffer.Length != length)
                {
                    buffer = new byte[length];
                }

                System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(buffer.AsSpan()).Fill(pixel);
                yield return new CameraFrame(width, height, width * CameraFrame.BytesPerPixel, buffer);
                // Runs when the consumer asks for the next frame, i.e. it is done with this one.
                Interlocked.Increment(ref _consumed);
            }
        }
        finally
        {
            _enumerationEnded.TrySetResult();
        }
    }

    /// <summary>When set, closing the connection waits for it (like a decoder that needs time to release its resources).</summary>
    public Task? DisposeGate { get; set; }

    /// <summary>Completes when <see cref="DisposeAsync"/> was entered (it may still wait for <see cref="DisposeGate"/>).</summary>
    public Task DisposeStarted => _disposeStarted.Task;

    private readonly TaskCompletionSource _disposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        _disposeStarted.TrySetResult();
        _items.Writer.TryComplete();
        if (DisposeGate is { } gate)
        {
            await gate;
        }

        _disposed.TrySetResult();
    }
}

/// <summary>Records what the camera window would draw.</summary>
internal sealed class RecordingFrameSink : ICameraFrameSink
{
    public List<(int Width, int Height, byte First)> Frames { get; } = [];

    /// <summary>The next <see cref="Present"/> calls throw this many times (like a broken bitmap).</summary>
    public int FailNext { get; set; }

    public int ClearCount { get; private set; }

    public void Present(CameraFrameBuffer frame)
    {
        if (FailNext > 0)
        {
            FailNext--;
            throw new InvalidOperationException("The bitmap is broken.");
        }

        Frames.Add((frame.Width, frame.Height, frame.Pixels[0]));
    }

    public void Clear() => ClearCount++;
}

internal static class Waiting
{
    /// <summary>Polls <paramref name="condition"/> (work runs on the thread pool) until it holds or the test timeout passes.</summary>
    public static async Task UntilAsync(Func<bool> condition, string description)
    {
        DateTime deadline = DateTime.UtcNow + TestTimeouts.Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for: {description}");
            }

            await Task.Delay(5);
        }
    }
}

/// <summary>Records the requested camera monitor path updates (user decision 2026-10-23) instead of saving them.</summary>
internal sealed class RecordingMonitorPathUpdater : ICameraMonitorPathUpdater
{
    private readonly Lock _gate = new();
    private readonly List<(string PrinterId, CameraMonitorId Saved, CameraMonitorId Updated)> _requests = [];

    public IReadOnlyList<(string PrinterId, CameraMonitorId Saved, CameraMonitorId Updated)> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public void Request(string printerId, CameraMonitorId saved, CameraMonitorId updated)
    {
        lock (_gate)
        {
            _requests.Add((printerId, saved, updated));
        }
    }
}
