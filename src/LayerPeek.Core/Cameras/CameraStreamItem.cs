namespace LayerPeek.Core.Cameras;

/// <summary>What a <see cref="CameraStreamItem"/> carries.</summary>
public enum CameraStreamItemKind
{
    /// <summary>A connection was opened: <see cref="CameraStreamItem.Info"/> describes its video stream (codec parameters).</summary>
    StreamStarted,

    /// <summary>An encoded packet as read from the stream, before decoding (<see cref="CameraStreamItem.Packet"/>).</summary>
    Packet,

    /// <summary>A decoded frame (<see cref="CameraStreamItem.Frame"/>).</summary>
    Frame,
}

/// <summary>
/// One item of a camera stream read with <see cref="ICameraFeed.ReadAsync"/>: the stream description after connecting,
/// an encoded packet or a decoded frame. Packet and frame <b>borrow</b> the reader's buffers like <see cref="CameraFrame"/>:
/// they are valid only until the enumerator advances or is disposed.
/// </summary>
public readonly struct CameraStreamItem
{
    private CameraStreamItem(CameraStreamItemKind kind, CameraStreamInfo? info, CameraPacket packet, CameraFrame frame)
    {
        Kind = kind;
        Info = info;
        Packet = packet;
        Frame = frame;
    }

    public CameraStreamItemKind Kind { get; }

    /// <summary>Set for <see cref="CameraStreamItemKind.StreamStarted"/>; owned by the receiver (immutable).</summary>
    public CameraStreamInfo? Info { get; }

    /// <summary>Set for <see cref="CameraStreamItemKind.Packet"/>; borrowed.</summary>
    public CameraPacket Packet { get; }

    /// <summary>Set for <see cref="CameraStreamItemKind.Frame"/>; borrowed.</summary>
    public CameraFrame Frame { get; }

    public static CameraStreamItem Started(CameraStreamInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new(CameraStreamItemKind.StreamStarted, info, default, default);
    }

    public static CameraStreamItem FromPacket(in CameraPacket packet) => new(CameraStreamItemKind.Packet, null, packet, default);

    public static CameraStreamItem FromFrame(in CameraFrame frame) => new(CameraStreamItemKind.Frame, null, default, frame);
}

/// <summary>Time base of packet timestamps: one tick is <see cref="Numerator"/> / <see cref="Denominator"/> seconds.</summary>
public readonly record struct CameraTimeBase(int Numerator, int Denominator)
{
    /// <summary>The RTP clock of H.264 video (90 kHz).</summary>
    public static CameraTimeBase Rtp90kHz { get; } = new(1, 90_000);
}

/// <summary>
/// An encoded video packet (for H.264: one access unit in Annex B or as delivered by the demuxer), exactly as read from
/// the stream, without decoding. <see cref="Data"/> is borrowed: valid only until the enumerator that produced it advances.
/// </summary>
/// <param name="Data">The packet's bytes; borrowed.</param>
/// <param name="Pts">Presentation timestamp in <paramref name="TimeBase"/> ticks; <see langword="null"/> when unknown.</param>
/// <param name="Dts">Decoding timestamp in <paramref name="TimeBase"/> ticks; <see langword="null"/> when unknown.</param>
/// <param name="Duration">Duration in <paramref name="TimeBase"/> ticks; 0 when unknown.</param>
/// <param name="IsKeyFrame">The packet starts a key frame (H.264: IDR or recovery point); a recording starts here.</param>
/// <param name="TimeBase">Unit of the timestamps (the stream's, e.g. <see cref="CameraTimeBase.Rtp90kHz"/>).</param>
public readonly record struct CameraPacket(
    ReadOnlyMemory<byte> Data,
    long? Pts,
    long? Dts,
    long Duration,
    bool IsKeyFrame,
    CameraTimeBase TimeBase);

/// <summary>Video codecs a camera stream may carry (the feed refuses others).</summary>
public enum CameraVideoCodec
{
    H264,
}

/// <summary>
/// The video stream of one connection, as needed to write its packets into a file without decoding (T-144): codec,
/// codec parameters and time base. Width and height may be 0 when the stream description does not state them (they are
/// then in the SPS of the codec parameters or of the first key frame).
/// </summary>
public sealed record CameraStreamInfo
{
    public CameraStreamInfo(CameraVideoCodec codec, int width, int height, CameraTimeBase timeBase, ReadOnlyMemory<byte> extradata)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (timeBase.Numerator <= 0 || timeBase.Denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeBase), timeBase, "The time base must be positive.");
        }

        Codec = codec;
        Width = width;
        Height = height;
        TimeBase = timeBase;
        Extradata = extradata;
    }

    public CameraVideoCodec Codec { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Unit of the timestamps of this connection's packets.</summary>
    public CameraTimeBase TimeBase { get; }

    /// <summary>
    /// Codec parameters of the stream description (H.264: SPS/PPS, from the SDP <c>sprop-parameter-sets</c>, Annex B or
    /// avcC as the demuxer provides them); empty when there are none (they then come in-band with the key frames). Owned
    /// by this record (never a borrowed buffer).
    /// </summary>
    public ReadOnlyMemory<byte> Extradata { get; }
}
