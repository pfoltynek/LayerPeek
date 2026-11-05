namespace LayerPeek.Core.Cameras;

/// <summary>
/// What the reader of a camera stream needs right now (see <see cref="ICameraFeed.ReadAsync"/>): decoded frames, encoded
/// packets, both or neither. May change at any time from any thread while the stream is read; the feed checks it for
/// every packet. Decoding runs only while <see cref="Frames"/> is set (T-143: recording without a window needs no
/// decoder); when it is switched on again during a connection, frames start at the next key frame.
/// <para>
/// Without <see cref="Frames"/> a reader may still want single key frames (T-153: print failure detection samples one
/// every few seconds): once <see cref="KeyFrameDue"/>, the next key frame is decoded on its own (it needs no other frame)
/// and yielded as a frame; the frames in between are never decoded. The hub schedules the next one after every sample
/// (<see cref="RequestKeyFrame"/>).
/// </para>
/// </summary>
public sealed class CameraStreamDemand
{
    /// <summary>No key frame is wanted (<see cref="KeyFrameDue"/> is never reached).</summary>
    private const long NoKeyFrame = long.MaxValue;

    private readonly TimeProvider _timeProvider;
    private volatile bool _frames;
    private volatile bool _packets;
    private long _keyFrameDue = NoKeyFrame;

    public CameraStreamDemand(bool frames, bool packets)
        : this(frames, packets, TimeProvider.System)
    {
    }

    /// <param name="frames">See <see cref="Frames"/>.</param>
    /// <param name="packets">See <see cref="Packets"/>.</param>
    /// <param name="timeProvider">The clock of the timestamps of <see cref="RequestKeyFrame"/>.</param>
    public CameraStreamDemand(bool frames, bool packets, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _frames = frames;
        _packets = packets;
        _timeProvider = timeProvider;
    }

    /// <summary>Decode and yield <see cref="CameraStreamItemKind.Frame"/> items.</summary>
    public bool Frames
    {
        get => _frames;
        set => _frames = value;
    }

    /// <summary>Yield every video packet as a <see cref="CameraStreamItemKind.Packet"/> item, before it is decoded.</summary>
    public bool Packets
    {
        get => _packets;
        set => _packets = value;
    }

    /// <summary>
    /// Whether a single key frame is wanted now (see the type remarks): one was requested and its time has come. Checked by
    /// the reader for key frame packets while <see cref="Frames"/> is off; costs one clock read.
    /// </summary>
    public bool KeyFrameDue
    {
        get
        {
            long due = Interlocked.Read(ref _keyFrameDue);
            return due != NoKeyFrame && _timeProvider.GetTimestamp() >= due;
        }
    }

    /// <summary>Wants the first key frame at or after <paramref name="timestamp"/> (of the clock given to the constructor).</summary>
    public void RequestKeyFrame(long timestamp) => Interlocked.Exchange(ref _keyFrameDue, Math.Min(timestamp, NoKeyFrame - 1));

    /// <summary>No key frame is wanted any more.</summary>
    public void CancelKeyFrame() => Interlocked.Exchange(ref _keyFrameDue, NoKeyFrame);
}
