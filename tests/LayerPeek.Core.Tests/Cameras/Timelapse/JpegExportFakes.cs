using System.Runtime.CompilerServices;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>
/// The decoder of a timelapse file for the JPEG export (T-165): every feed plays <see cref="Frames"/> frames of
/// <see cref="Width"/> × <see cref="Height"/>, frame <i>n</i> filled with the byte <i>n</i> (from 1), as packets (the count
/// pass) or frames (the decoding pass), synchronously and from one reused buffer like the FFmpeg feed.
/// </summary>
internal sealed class ScriptedVideoFiles : ICameraFeedFactory
{
    private int _created;

    public int Frames { get; set; } = 5;

    public int Width { get; set; } = 8;

    public int Height { get; set; } = 4;

    /// <summary>The decoding pass fails after this many frames (a damaged end); <see langword="null"/>: never.</summary>
    public int? FailAfterFrames { get; set; }

    /// <summary>The count pass fails after this many packets as well; <see langword="null"/>: never.</summary>
    public int? FailAfterPackets { get; set; }

    /// <summary>Thrown by <see cref="Create"/>; <see langword="null"/>: none.</summary>
    public Exception? CreateFailure { get; set; }

    /// <summary>Awaited before the decoding pass yields this frame (1-based), so that a test can act meanwhile.</summary>
    public (int Frame, Task Gate)? Hold { get; set; }

    public int CreateCount => Volatile.Read(ref _created);

    public Uri? LastUri { get; private set; }

    public ICameraFeed Create(Uri streamUri)
    {
        if (CreateFailure is { } failure)
        {
            throw failure;
        }

        Interlocked.Increment(ref _created);
        LastUri = streamUri;
        return new Feed(this);
    }

    private sealed class Feed(ScriptedVideoFiles owner) : ICameraFeed
    {
        private readonly byte[] _buffer = new byte[owner.Width * owner.Height * CameraFrame.BytesPerPixel];

        public async IAsyncEnumerable<CameraFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (int n = 1; n <= owner.Frames; n++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (owner.FailAfterFrames is { } fail && n > fail)
                {
                    throw new CameraFeedException("Reading the camera stream failed: Invalid data found when processing input.");
                }

                if (owner.Hold is { } hold && hold.Frame == n)
                {
                    await hold.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                _buffer.AsSpan().Fill((byte)n);
                yield return new CameraFrame(owner.Width, owner.Height, owner.Width * CameraFrame.BytesPerPixel, _buffer);
            }
        }

        public async IAsyncEnumerable<CameraStreamItem> ReadAsync(CameraStreamDemand demand, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return CameraStreamItem.Started(new CameraStreamInfo(CameraVideoCodec.H264, owner.Width, owner.Height, CameraTimeBase.Rtp90kHz, ReadOnlyMemory<byte>.Empty));
            for (int n = 1; n <= owner.Frames; n++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (owner.FailAfterPackets is { } fail && n > fail)
                {
                    throw new CameraFeedException("Reading the camera stream failed: Invalid data found when processing input.");
                }

                yield return CameraStreamItem.FromPacket(new CameraPacket(_buffer, n * 3000, n * 3000, 3000, IsKeyFrame: true, CameraTimeBase.Rtp90kHz));
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// A JPEG encoder that writes what it was given instead of a JPEG: <c>FAKE</c>, width, height, first pixel byte and the
/// quality (so a test can read the order and the size back). Records the bytes allocated on the encoding thread at every
/// call (the export's steady state per frame), and can fail.
/// </summary>
internal sealed class FakeJpegSequenceEncoders : ICameraJpegSequenceEncoderFactory
{
    private readonly List<long> _allocated = [];
    private int _created;
    private int _disposed;

    public int Created => Volatile.Read(ref _created);

    public int Disposed => Volatile.Read(ref _disposed);

    public int? LastQuality { get; private set; }

    /// <summary>Thrown at this frame (1-based); <see langword="null"/>: never.</summary>
    public int? FailAtFrame { get; set; }

    /// <summary><see cref="GC.GetAllocatedBytesForCurrentThread"/> at every call, in order.</summary>
    public IReadOnlyList<long> AllocatedAtCalls
    {
        get
        {
            lock (_allocated)
            {
                return [.. _allocated];
            }
        }
    }

    public ICameraJpegSequenceEncoder Create(int quality)
    {
        Interlocked.Increment(ref _created);
        LastQuality = quality;
        return new Encoder(this, quality);
    }

    /// <summary>Reads a file written by the fake: width, height, frame number.</summary>
    public static (int Width, int Height, int Frame) Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal("FAKE"u8.ToArray(), bytes[..4]);
        return (BitConverter.ToInt32(bytes, 4), BitConverter.ToInt32(bytes, 8), bytes[12]);
    }

    private sealed class Encoder(FakeJpegSequenceEncoders owner, int quality) : ICameraJpegSequenceEncoder
    {
        private readonly byte[] _header = new byte[14];
        private int _calls;

        public void Encode(in CameraFrame frame, Stream destination)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            lock (owner._allocated)
            {
                owner._allocated.Add(allocated);
            }

            _calls++;
            if (owner.FailAtFrame == _calls)
            {
                throw new InvalidOperationException("The encoder failed.");
            }

            "FAKE"u8.CopyTo(_header);
            BitConverter.TryWriteBytes(_header.AsSpan(4), frame.Width);
            BitConverter.TryWriteBytes(_header.AsSpan(8), frame.Height);
            _header[12] = frame.Pixels.Span[0];
            _header[13] = (byte)quality;
            destination.Write(_header);
        }

        public void Dispose() => Interlocked.Increment(ref owner._disposed);
    }
}

/// <summary>Records the holds of the export (T-165) and whether they were released.</summary>
internal sealed class RecordingFileGuard : ICameraRecordingFileGuard
{
    private readonly List<(string[] Paths, Handle Handle)> _holds = [];

    public IReadOnlyList<string> Held
    {
        get
        {
            lock (_holds)
            {
                return [.. _holds.Where(static h => !h.Handle.Released).SelectMany(static h => h.Paths)];
            }
        }
    }

    public int Holds
    {
        get
        {
            lock (_holds)
            {
                return _holds.Count;
            }
        }
    }

    public IDisposable Hold(IReadOnlyList<string> paths)
    {
        Handle handle = new();
        lock (_holds)
        {
            _holds.Add(([.. paths], handle));
        }

        return handle;
    }

    internal sealed class Handle : IDisposable
    {
        public bool Released { get; private set; }

        public void Dispose() => Released = true;
    }
}
