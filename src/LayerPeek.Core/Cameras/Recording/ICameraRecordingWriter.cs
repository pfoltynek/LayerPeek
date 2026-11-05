namespace LayerPeek.Core.Cameras.Recording;

/// <summary>Whether recording works in this build (T-144: the container's muxer must be in the video library).</summary>
/// <param name="IsAvailable">Recordings can be written.</param>
/// <param name="Reason">Why not (English, for the log and diagnostics); <see langword="null"/> when available.</param>
public readonly record struct CameraRecorderAvailability(bool IsAvailable, string? Reason)
{
    public static CameraRecorderAvailability Available { get; } = new(true, null);

    public static CameraRecorderAvailability Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(false, reason);
    }
}

/// <summary>
/// Writes the packets of one camera connection into one file, without decoding or encoding (remux, T-144). Implemented
/// by Camera.FFmpeg (fragmented MP4); <see cref="CameraRecording"/> decides when a file starts and ends and prepares the
/// timestamps.
/// </summary>
public interface ICameraRecordingWriterFactory
{
    /// <summary>Checked once (cheap to read again).</summary>
    CameraRecorderAvailability Availability { get; }

    /// <summary>
    /// Creates the file at <paramref name="path"/> (an existing empty file is overwritten; <see cref="CameraRecording"/>
    /// reserves the name before) and writes the container header. <paramref name="firstKeyFrame"/> is the packet the file
    /// starts with (it is written by the next <see cref="ICameraRecordingWriter.Write"/>, not here); it supplies the codec
    /// parameters and image size when <paramref name="info"/> lacks them.
    /// </summary>
    /// <exception cref="CameraRecordingException">The file cannot be created or the stream cannot be stored.</exception>
    ICameraRecordingWriter Create(string path, CameraStreamInfo info, ReadOnlySpan<byte> firstKeyFrame);
}

/// <summary>
/// One open recording file. Used by one thread at a time; <see cref="BytesWritten"/> may be read from any thread.
/// <see cref="IDisposable.Dispose"/> closes the file without <see cref="Complete"/> (after a failure).
/// </summary>
public interface ICameraRecordingWriter : IDisposable
{
    /// <summary>Bytes written to the file so far (what the container still buffers is not counted).</summary>
    long BytesWritten { get; }

    /// <summary>
    /// Writes one packet. Timestamps are in the time base of the stream the writer was created for, start at 0 with the
    /// first (key frame) packet, <paramref name="dts"/> increases strictly and <paramref name="pts"/> is never below it.
    /// </summary>
    /// <param name="duration">In the same time base; 0 when unknown.</param>
    /// <exception cref="CameraRecordingException">Writing failed (e.g. the disk is full or was removed).</exception>
    void Write(ReadOnlySpan<byte> data, long pts, long dts, long duration, bool isKeyFrame);

    /// <summary>Writes the end of the container and closes the file.</summary>
    /// <exception cref="CameraRecordingException">Writing failed; what was written before stays readable.</exception>
    void Complete();
}

/// <summary>A recording file could not be written. The message never contains the path.</summary>
public sealed class CameraRecordingException : Exception
{
    public CameraRecordingException()
        : this(CameraRecordingFailure.WriteFailed, "Writing the recording failed.")
    {
    }

    public CameraRecordingException(string message)
        : this(CameraRecordingFailure.WriteFailed, message)
    {
    }

    public CameraRecordingException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failure = CameraRecordingFailure.WriteFailed;
    }

    public CameraRecordingException(CameraRecordingFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public CameraRecordingFailure Failure { get; }
}
