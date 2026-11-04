namespace LayerPeek.Core.Cameras.Recording;

/// <summary>Where a <see cref="ICameraDashcam"/> is.</summary>
public enum CameraDashcamState
{
    /// <summary>The last minutes are held in memory; nothing is written.</summary>
    Buffering,

    /// <summary>A save runs: the buffer was written and the stream goes on into the file until the end of the save.</summary>
    Saving,

    /// <summary>Ended (stopped, or by <see cref="ICameraDashcam.Failure"/>); the memory is released.</summary>
    Ended,
}

/// <summary>Bounds of a dashcam (T-148); the defaults are the user's decisions of 2026-11-04.</summary>
public sealed record CameraDashcamOptions
{
    /// <summary>Default <see cref="BufferDuration"/>.</summary>
    public static readonly TimeSpan DefaultBufferDuration = TimeSpan.FromMinutes(2);

    /// <summary>Default <see cref="PostEventDuration"/>.</summary>
    public static readonly TimeSpan DefaultPostEventDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default <see cref="MaxBytesPerSecond"/>: 512 KiB/s (~4.2 Mbit/s), four times the Buddy3D (~1.06 Mbit/s, T-144), so a
    /// normal camera is bounded by time only and a misbehaving one never by more than this.
    /// </summary>
    public const int DefaultMaxBytesPerSecond = 512 * 1024;

    /// <summary>Default <see cref="MaxPacketsPerSecond"/>: far above any camera (the Buddy3D sends ~22 per second).</summary>
    public const int DefaultMaxPacketsPerSecond = 120;

    public static CameraDashcamOptions Default { get; } = new();

    /// <summary>How much of the stream is held in memory: at most this between the first and the last packet.</summary>
    public TimeSpan BufferDuration
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, TimeSpan.FromHours(1));
            field = value;
        }
    } = DefaultBufferDuration;

    /// <summary>How long a save goes on after the event (after the last one, when another event extends it).</summary>
    public TimeSpan PostEventDuration
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, TimeSpan.FromHours(1));
            field = value;
        }
    } = DefaultPostEventDuration;

    /// <summary>Memory bound per second of <see cref="BufferDuration"/>.</summary>
    public int MaxBytesPerSecond
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1024);
            field = value;
        }
    } = DefaultMaxBytesPerSecond;

    /// <summary>Packet count bound per second of <see cref="BufferDuration"/>.</summary>
    public int MaxPacketsPerSecond
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = DefaultMaxPacketsPerSecond;

    /// <summary>The memory bound of the buffer: <see cref="MaxBytesPerSecond"/> × <see cref="BufferDuration"/>, at least one chunk.</summary>
    public long MaxBufferBytes => Math.Max(DashcamBuffer.ChunkSize, (long)Math.Ceiling(BufferDuration.TotalSeconds * MaxBytesPerSecond));

    /// <summary>The packet bound of the buffer: <see cref="MaxPacketsPerSecond"/> × <see cref="BufferDuration"/>.</summary>
    public int MaxBufferPackets => (int)Math.Clamp(Math.Ceiling(BufferDuration.TotalSeconds * MaxPacketsPerSecond), 2, Array.MaxLength);
}

/// <summary>
/// What one save of a dashcam produced (<see cref="ICameraRecorder.StartDashcam"/>, reported once the save ended).
/// </summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="Files">The files written, in order (full paths; a stream restart during the save adds <c>-2</c> …); may be empty.</param>
/// <param name="Failure">Why writing failed; <see cref="CameraRecordingFailure.None"/> when the files are complete.</param>
/// <param name="FailureMessage">English, without paths; <see langword="null"/> without a failure.</param>
/// <param name="BytesWritten">Bytes of all files.</param>
/// <param name="Duration">Video time of all files.</param>
public sealed record CameraDashcamSave(
    string PrinterId,
    IReadOnlyList<string> Files,
    CameraRecordingFailure Failure,
    string? FailureMessage,
    long BytesWritten,
    TimeSpan Duration);

/// <summary>
/// The "dashcam" of one printer's camera (T-148): holds the last minutes of the shared camera connection in memory
/// (encoded packets, whole groups of pictures from a key frame; nothing is written) and, on <see cref="Save"/>, writes them
/// into a file and goes on writing the stream for <see cref="CameraDashcamOptions.PostEventDuration"/> more. Buffering
/// goes on during and after a save. Thread-safe; never throws for runtime conditions.
/// </summary>
public interface ICameraDashcam : IAsyncDisposable
{
    string PrinterId { get; }

    CameraDashcamState State { get; }

    /// <summary>Why it ended by itself; <see cref="CameraRecordingFailure.None"/> while it runs and after a stop.</summary>
    CameraRecordingFailure Failure { get; }

    /// <summary>English, without paths; <see langword="null"/> without a failure.</summary>
    string? FailureMessage { get; }

    /// <summary>Time held in memory now (between the first and the last packet).</summary>
    TimeSpan BufferedDuration { get; }

    /// <summary>Memory held for the packets now.</summary>
    long BufferedBytes { get; }

    /// <summary>The files of the running save so far (full paths); empty while no save runs.</summary>
    IReadOnlyList<string> SaveFiles { get; }

    /// <summary>Bytes the running save wrote so far; 0 while no save runs.</summary>
    long SaveBytesWritten { get; }

    /// <summary>Completes (never faults) when it ended: a running save is complete and the memory released.</summary>
    Task Completion { get; }

    /// <summary>
    /// Saves the buffer into <paramref name="filePath"/> (absolute; the folder must exist; an existing file is never
    /// overwritten, the next free suffix is used) and goes on writing the stream until
    /// <see cref="CameraDashcamOptions.PostEventDuration"/> after now. During a running save it extends that save instead
    /// (<paramref name="filePath"/> is not used). The outcome is reported once the save ended.
    /// </summary>
    /// <returns><see langword="false"/> when it ended, or ends after its save without one running.</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is not absolute or has no file name.</exception>
    bool Save(string filePath);

    /// <summary>
    /// Stops buffering and releases the memory now; a running save goes on to its end, then the dashcam ends (a print ended
    /// or the full recording took over). Idempotent.
    /// </summary>
    void EndAfterSave();

    /// <summary>Ends now: a running save completes its file with what it has. Idempotent, never throws. Same as disposing.</summary>
    Task StopAsync();
}
