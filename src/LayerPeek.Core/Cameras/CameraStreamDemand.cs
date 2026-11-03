namespace LayerPeek.Core.Cameras;

/// <summary>
/// What the reader of a camera stream needs right now (see <see cref="ICameraFeed.ReadAsync"/>): decoded frames, encoded
/// packets, both or neither. May change at any time from any thread while the stream is read; the feed checks it for
/// every packet. Decoding runs only while <see cref="Frames"/> is set (T-143: recording without a window needs no
/// decoder); when it is switched on again during a connection, frames start at the next key frame.
/// </summary>
public sealed class CameraStreamDemand
{
    private volatile bool _frames;
    private volatile bool _packets;

    public CameraStreamDemand(bool frames, bool packets)
    {
        _frames = frames;
        _packets = packets;
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
}
