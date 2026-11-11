using System.Runtime.CompilerServices;
using System.Threading.Channels;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>Camera targets of test printers (a target exists only for a printer with an enabled camera).</summary>
internal static class CameraTargets
{
    public static CameraTarget For(string id, string name, Uri streamUri, CameraWindowDefaults? window = null) =>
        CameraTarget.From(TestPrinters.Definition(id, name) with
        {
            Camera = new CameraSettings(enabled: true, streamUri) { Window = window ?? CameraWindowDefaults.Default },
        })!;
}

/// <summary>Shared camera connections for camera window tests (T-143).</summary>
internal static class CameraHubs
{
    /// <summary>
    /// A hub that closes a connection as soon as its last lease ends (no linger), like the camera window's own connection
    /// before T-143; the linger time itself is tested in Core (<c>CameraStreamHubTests</c>).
    /// </summary>
    public static CameraStreamHub Immediate(ReconnectingCameraFeed feed) =>
        new(feed, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<CameraStreamHub>.Instance, TimeSpan.Zero);
}

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

    /// <summary>The display size of the demand when the last frame was converted (T-184); <see langword="null"/>: full resolution.</summary>
    public CameraFrameSize? LastDisplaySize
    {
        get
        {
            long packed = Interlocked.Read(ref _lastDisplaySize);
            return packed == 0 ? null : new CameraFrameSize((int)(packed >> 32), (int)(uint)packed);
        }
    }

    private long _lastDisplaySize;

    /// <summary>
    /// T-184: the frames converted like the decoder does: scaled down to the demand's display size (same fill, the
    /// source size kept), in full resolution when no display size is wanted, a full frame or a grayscale sample is due, or the
    /// frame fits.
    /// </summary>
    public async IAsyncEnumerable<CameraStreamItem> ReadAsync(CameraStreamDemand demand, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(demand);
        byte[] display = [];
        await foreach (CameraFrame frame in ReadFramesAsync(cancellationToken))
        {
            CameraFrame scaled = default;
            CameraFrameSize? wanted = demand.DisplaySize;
            Interlocked.Exchange(ref _lastDisplaySize, wanted is { } w ? ((long)w.Width << 32) | (uint)w.Height : 0);
            if (wanted is { } size && size.Fit(frame.Width, frame.Height) is { } fit && (fit.Width < frame.Width || fit.Height < frame.Height))
            {
                // Rows padded to 64 bytes, like the decoder's aligned buffers.
                int stride = ((fit.Width * CameraFrame.BytesPerPixel) + 63) / 64 * 64;
                int length = (stride * (fit.Height - 1)) + (fit.Width * CameraFrame.BytesPerPixel);
                if (display.Length < length)
                {
                    display = new byte[length];
                }

                // One colour, like every frame of this feed.
                uint pixel = System.Runtime.InteropServices.MemoryMarshal.Read<uint>(frame.Pixels.Span);
                for (int row = 0; row < fit.Height; row++)
                {
                    System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(display.AsSpan(row * stride, fit.Width * CameraFrame.BytesPerPixel)).Fill(pixel);
                }

                scaled = new CameraFrame(fit.Width, fit.Height, stride, display.AsMemory(0, length), frame.Width, frame.Height);
            }

            // T-189: no grayscale conversion here; a due grayscale sample is served from the full frame by the hub.
            bool full = scaled.Width == 0 || demand.FullResolutionDue || demand.GrayscaleDue;
            yield return CameraStreamItem.FromFrames(full ? frame : default, scaled);
        }
    }

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

/// <summary>
/// Records what the camera window would draw: <see cref="Write"/> (streaming thread) replaces the "bitmap", <see cref="Show"/>
/// (UI thread) adds what it holds to <see cref="Frames"/>, like the window putting its bitmap on screen.
/// </summary>
internal sealed class RecordingFrameSink : ICameraFrameSink
{
    private readonly Lock _gate = new();
    private (int Width, int Height, byte First)? _written;
    private int _writes;

    /// <summary>The frames shown, in order.</summary>
    public List<(int Width, int Height, byte First)> Frames { get; } = [];

    /// <summary>The next <see cref="Show"/> calls throw this many times (like a broken bitmap).</summary>
    public int FailNext { get; set; }

    public int ClearCount { get; private set; }

    /// <summary>Frames written (copied on the streaming thread).</summary>
    public int Writes => Volatile.Read(ref _writes);

    /// <summary>Called on the streaming thread after a frame was written (e.g. to block it).</summary>
    public Action? Written { get; set; }

    public void Write(in CameraFrame frame)
    {
        lock (_gate)
        {
            _written = (frame.Width, frame.Height, frame.Pixels.Span[0]);
        }

        Interlocked.Increment(ref _writes);
        Written?.Invoke();
    }

    public bool Show()
    {
        if (FailNext > 0)
        {
            FailNext--;
            throw new InvalidOperationException("The bitmap is broken.");
        }

        lock (_gate)
        {
            if (_written is not { } frame)
            {
                return false;
            }

            Frames.Add(frame);
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _written = null;
        }

        ClearCount++;
    }
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

    /// <summary>The paths <see cref="SavedDevicePath"/> reports, by printer id (case-insensitive) and saved monitor.</summary>
    public Dictionary<(string PrinterId, CameraMonitorId Saved), string> SavedPaths { get; } = [];

    public string? SavedDevicePath(string printerId, CameraMonitorId monitor)
    {
        foreach (((string id, CameraMonitorId saved), string path) in SavedPaths)
        {
            if (string.Equals(id, printerId, StringComparison.OrdinalIgnoreCase) && saved.HasSameIdentity(monitor))
            {
                return path;
            }
        }

        return null;
    }

    public Task<bool> WaitForPendingAsync(TimeSpan timeout) => Task.FromResult(true);
}

/// <summary>
/// Records the "always on top" options saved by the pin of camera windows (T-117); nothing on disk. A written option is
/// announced like the real writer does (<see cref="ICameraWindowTopmostWriter.TopmostSaved"/>, on the saving thread).
/// </summary>
internal sealed class FakeTopmostWriter : ICameraWindowTopmostWriter
{
    private readonly Lock _gate = new();
    private readonly List<(string PrinterId, CameraWindowMode Mode, bool Topmost)> _saves = [];
    private TaskCompletionSource<bool>? _held;

    public event EventHandler<CameraWindowTopmostSavedEventArgs>? TopmostSaved;

    /// <summary>What a save returns (unless held or failing).</summary>
    public bool Result { get; set; } = true;

    /// <summary>Thrown by the next saves (until reset to <see langword="null"/>).</summary>
    public Exception? Failure { get; set; }

    public IReadOnlyList<(string PrinterId, CameraWindowMode Mode, bool Topmost)> Saves
    {
        get
        {
            lock (_gate)
            {
                return [.. _saves];
            }
        }
    }

    /// <summary>The next saves wait until <see cref="Release"/>.</summary>
    public void Hold()
    {
        lock (_gate)
        {
            _held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Completes the held saves with <paramref name="result"/>.</summary>
    public void Release(bool result)
    {
        TaskCompletionSource<bool>? held;
        lock (_gate)
        {
            held = _held;
            _held = null;
        }

        held?.TrySetResult(result);
    }

    public async Task<bool> SaveAsync(string printerId, CameraWindowMode mode, bool topmost, CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? held;
        lock (_gate)
        {
            _saves.Add((printerId, mode, topmost));
            held = _held;
        }

        bool written = held is null ? Result : await held.Task.ConfigureAwait(false);
        if (Failure is { } failure)
        {
            throw failure;
        }

        if (written)
        {
            TopmostSaved?.Invoke(this, new CameraWindowTopmostSavedEventArgs(printerId, mode, topmost));
        }

        return written;
    }
}
