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
/// <para>
/// T-184: each decoded frame is converted to BGRA only in the sizes that are wanted: in the camera's resolution while
/// <see cref="FullResolutionDue"/> (by default always; the hub limits it to the times a full-resolution reader takes a
/// frame), and scaled down to fit <see cref="DisplaySize"/> when one is set (T-185: only while <see cref="DisplayDue"/>,
/// which the hub limits to the times a display reader with a frame rate limit takes a frame).
/// </para>
/// <para>
/// T-189: while <see cref="GrayscaleDue"/> (never by default) the frame is also scaled into 8-bit luma of
/// <see cref="GrayscaleSize"/> straight from its Y plane (<see cref="CameraStreamItem.GrayscaleFrame"/>), for print failure
/// detection, without a BGRA conversion. A single key frame decoded on its own is converted only in the forms that are due
/// then. A feed that limits its full-resolution conversion to <see cref="FullResolutionDue"/> must yield the grayscale
/// frame as well; a feed that ignores the demand yields full frames, which the hub scales to grayscale itself.
/// </para>
/// </summary>
public sealed class CameraStreamDemand
{
    /// <summary>A due time that is never reached (no key frame, no frame in full resolution).</summary>
    private const long Never = long.MaxValue;

    private readonly TimeProvider _timeProvider;
    private volatile bool _frames;
    private volatile bool _packets;
    private long _keyFrameDue = Never;
    private long _fullResolutionDue = long.MinValue;
    private long _displaySize;
    private long _displayDue = long.MinValue;
    private long _grayscaleDue = Never;
    private long _grayscaleSize;

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
            return due != Never && _timeProvider.GetTimestamp() >= due;
        }
    }

    /// <summary>Wants the first key frame at or after <paramref name="timestamp"/> (of the clock given to the constructor).</summary>
    public void RequestKeyFrame(long timestamp) => Interlocked.Exchange(ref _keyFrameDue, Math.Min(timestamp, Never - 1));

    /// <summary>No key frame is wanted any more.</summary>
    public void CancelKeyFrame() => Interlocked.Exchange(ref _keyFrameDue, Never);

    /// <summary>
    /// The largest frame a display reader wants (T-184): every decoded frame is also converted scaled down to fit it, see
    /// <see cref="CameraStreamItem.DisplayFrame"/>; <see langword="null"/> (the default): no frames for display. Read by
    /// the reader for every frame; a change applies from the next frame on, without interrupting the stream.
    /// </summary>
    public CameraFrameSize? DisplaySize
    {
        get => CameraFrameSize.Unpack(Interlocked.Read(ref _displaySize));
        set => Interlocked.Exchange(ref _displaySize, CameraFrameSize.Pack(value));
    }

    /// <summary>
    /// Whether the frame for display (<see cref="DisplaySize"/>) is wanted now (T-185): always unless the hub limited it with
    /// <see cref="RequestDisplay"/> (the mini preview takes a few frames per second). A frame that is converted in full
    /// resolution anyway is also converted for display, so that a display reader never gets a full frame in its place.
    /// Checked by the reader for every frame while a display size is set; costs one clock read only while a later time is
    /// requested.
    /// </summary>
    public bool DisplayDue
    {
        get
        {
            long due = Interlocked.Read(ref _displayDue);
            return due == long.MinValue || _timeProvider.GetTimestamp() >= due;
        }
    }

    /// <summary>
    /// Wants frames for display from <paramref name="timestamp"/> on (of the clock given to the constructor);
    /// <see cref="long.MinValue"/>: every frame (the default).
    /// </summary>
    public void RequestDisplay(long timestamp) => Interlocked.Exchange(ref _displayDue, timestamp);

    /// <summary>
    /// Whether the decoded frame is wanted in the camera's resolution now (<see cref="CameraStreamItem.Frame"/>): always
    /// unless the hub limited it with <see cref="RequestFullResolution"/> or <see cref="CancelFullResolution"/>. Checked by
    /// the reader for every frame; costs one clock read only while a later time is requested.
    /// </summary>
    public bool FullResolutionDue
    {
        get
        {
            long due = Interlocked.Read(ref _fullResolutionDue);
            return due == long.MinValue || (due != Never && _timeProvider.GetTimestamp() >= due);
        }
    }

    /// <summary>
    /// Wants frames in full resolution from <paramref name="timestamp"/> on (of the clock given to the constructor);
    /// <see cref="long.MinValue"/>: every frame (the default).
    /// </summary>
    public void RequestFullResolution(long timestamp) => Interlocked.Exchange(ref _fullResolutionDue, Math.Min(timestamp, Never - 1));

    /// <summary>No frame is wanted in full resolution (only for display, if a <see cref="DisplaySize"/> is set).</summary>
    public void CancelFullResolution() => Interlocked.Exchange(ref _fullResolutionDue, Never);

    /// <summary>
    /// The size of the grayscale frame (T-189) while <see cref="GrayscaleDue"/>; <see langword="null"/> (the default): none.
    /// Read by the reader for a frame only when the grayscale frame is due.
    /// </summary>
    public CameraGrayscaleSize? GrayscaleSize => CameraGrayscaleSize.Unpack(Interlocked.Read(ref _grayscaleSize));

    /// <summary>
    /// Whether the decoded frame is wanted as grayscale of <see cref="GrayscaleSize"/> now: only after
    /// <see cref="RequestGrayscale"/>, from its time on. Checked by the reader for every frame; costs one clock read only while
    /// requested.
    /// </summary>
    public bool GrayscaleDue
    {
        get
        {
            long due = Interlocked.Read(ref _grayscaleDue);
            return due != Never && (due == long.MinValue || _timeProvider.GetTimestamp() >= due);
        }
    }

    /// <summary>
    /// Wants frames as grayscale of <paramref name="size"/> from <paramref name="timestamp"/> on (of the clock given to the
    /// constructor); <see cref="long.MinValue"/>: every frame. The size is written first, so a reader that sees the new
    /// time also sees its size.
    /// </summary>
    public void RequestGrayscale(CameraGrayscaleSize size, long timestamp)
    {
        if (!size.IsValid)
        {
            throw new ArgumentException("The grayscale size is empty.", nameof(size));
        }

        Interlocked.Exchange(ref _grayscaleSize, CameraGrayscaleSize.Pack(size));
        Interlocked.Exchange(ref _grayscaleDue, Math.Min(timestamp, Never - 1));
    }

    /// <summary>No frame is wanted as grayscale.</summary>
    public void CancelGrayscale()
    {
        Interlocked.Exchange(ref _grayscaleDue, Never);
        Interlocked.Exchange(ref _grayscaleSize, 0);
    }
}
