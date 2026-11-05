using System.Globalization;

using Microsoft.Extensions.Logging;

namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// The files of one recording (T-144), shared by the full recording (<see cref="CameraRecording"/>) and the dashcam save
/// (<see cref="CameraDashcam"/>, T-148): a file starts at a key frame (packets before are dropped), its timestamps start at
/// 0 (<see cref="RecordingClock"/>), and every <see cref="Close"/> completes it, so that the next packet of a key frame
/// starts the next file, named <c>name-2.ext</c>, <c>name-3.ext</c> … (an existing file is never overwritten). Used by
/// one thread at a time; <see cref="Files"/>, <see cref="BytesWritten"/> and <see cref="Duration"/> may be read anywhere.
/// Writing a packet allocates nothing. With a constant frame rate (timelapse, T-158) the packets' own timestamps are
/// ignored and packet <i>n</i> of a file is at <i>n</i> frames (<see cref="RecordingClock.AtFrameRate"/>).
/// </summary>
internal sealed partial class RecordingFiles
{
    /// <summary>Upper bound of suffixes tried for a free file name.</summary>
    private const int MaxSegments = 10_000;

    private readonly string _printerId;
    private readonly ICameraRecordingWriterFactory _writers;
    private readonly ILogger _logger;
    private readonly string _folder;
    private readonly string _name;
    private readonly string _extension;
    private readonly int _framesPerSecond;

    // Published for other threads.
    private string[] _files = [];
    private long _bytes;
    private long _durationTicks;

    // The writing thread only.
    private ICameraRecordingWriter? _writer;
    private string _fileName = "";
    private RecordingClock _clock;
    private long _closedBytes;
    private TimeSpan _closedDuration;
    private int _nextSegment = 1;

    /// <param name="filePath">Absolute path of the first file (the folder must exist); later files add a suffix.</param>
    /// <param name="framesPerSecond">A constant frame rate for the files' timestamps; 0: the stream's timestamps.</param>
    public RecordingFiles(string printerId, string filePath, ICameraRecordingWriterFactory writers, ILogger logger, int framesPerSecond = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(framesPerSecond);
        _framesPerSecond = framesPerSecond;
        _printerId = printerId;
        _writers = writers;
        _logger = logger;
        _folder = Path.GetDirectoryName(filePath) ?? throw new ArgumentException("The recording path needs a folder.", nameof(filePath));
        _name = Path.GetFileNameWithoutExtension(filePath);
        _extension = Path.GetExtension(filePath);
    }

    /// <summary>The files created so far, in order (full paths).</summary>
    public IReadOnlyList<string> Files => Volatile.Read(ref _files);

    /// <summary>Bytes written to all files so far.</summary>
    public long BytesWritten => Volatile.Read(ref _bytes);

    /// <summary>Recorded video time of all files so far (by the stream's timestamps).</summary>
    public TimeSpan Duration => TimeSpan.FromTicks(Volatile.Read(ref _durationTicks));

    /// <summary>A file is open (a key frame came since the last <see cref="Close"/>).</summary>
    public bool IsOpen => _writer is not null;

    /// <summary>
    /// Writes <paramref name="packet"/> of the connection described by <paramref name="info"/>; without an open file, a key
    /// frame opens the next one and any other packet is dropped.
    /// </summary>
    /// <returns><see langword="true"/> when this packet opened a new file.</returns>
    /// <exception cref="CameraRecordingException">The file cannot be created or written.</exception>
    public bool Write(CameraStreamInfo info, in CameraPacket packet)
    {
        bool opened = false;
        if (_writer is null)
        {
            if (!packet.IsKeyFrame)
            {
                return false;
            }

            Open(info, packet.Data.Span);
            opened = true;
        }

        _clock.Next(packet, out long pts, out long dts, out long duration);
        _writer!.Write(packet.Data.Span, pts, dts, duration, packet.IsKeyFrame);
        Volatile.Write(ref _bytes, _closedBytes + _writer.BytesWritten);
        Volatile.Write(ref _durationTicks, (_closedDuration + _clock.Elapsed).Ticks);
        return opened;
    }

    /// <summary>Completes the current file, if any; the next key frame starts the next one.</summary>
    /// <exception cref="CameraRecordingException">Completing failed; what was written before stays readable.</exception>
    public void Close()
    {
        if (_writer is not { } writer)
        {
            return;
        }

        _writer = null;
        try
        {
            writer.Complete();
        }
        finally
        {
            writer.Dispose();
            _closedBytes += writer.BytesWritten;
            _closedDuration += _clock.Elapsed;
            Volatile.Write(ref _bytes, _closedBytes);
            Volatile.Write(ref _durationTicks, _closedDuration.Ticks);
            LogFileCompleted(_printerId, _fileName);
        }
    }

    private void Open(CameraStreamInfo info, ReadOnlySpan<byte> firstKeyFrame)
    {
        string path = Reserve();
        try
        {
            _writer = _writers.Create(path, info, firstKeyFrame);
        }
        catch
        {
            // Nothing was recorded into it: the reserved empty file goes.
            TryDelete(path);
            throw;
        }

        _fileName = Path.GetFileName(path);
        _clock = _framesPerSecond > 0 ? RecordingClock.AtFrameRate(info.TimeBase, _framesPerSecond) : new RecordingClock(info.TimeBase);
        Volatile.Write(ref _files, [.. _files, path]);
        LogFileStarted(_printerId, _fileName);
    }

    /// <summary>
    /// Creates the next file empty (never overwriting one): <c>name.ext</c>, then <c>name-2.ext</c>, <c>name-3.ext</c> …;
    /// a taken name is skipped.
    /// </summary>
    private string Reserve()
    {
        for (int attempt = 0; attempt < MaxSegments; attempt++)
        {
            int segment = _nextSegment++;
            string fileName = segment == 1
                ? _name + _extension
                : string.Create(CultureInfo.InvariantCulture, $"{_name}-{segment}{_extension}");
            string path = Path.Combine(_folder, fileName);
            try
            {
                new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose();
                return path;
            }
            catch (IOException) when (File.Exists(path) || Directory.Exists(path))
            {
                // Taken: the next suffix.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new CameraRecordingException(
                    CameraRecording.IsDiskFull(ex) ? CameraRecordingFailure.DiskFull : CameraRecordingFailure.WriteFailed,
                    ex is DirectoryNotFoundException
                        ? "The recording folder does not exist."
                        : ex is UnauthorizedAccessException
                            ? "The recording folder cannot be written to."
                            : $"The recording file cannot be created ({ex.GetType().Name}).",
                    ex);
            }
        }

        throw new CameraRecordingException("No free file name for the recording.");
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string fileName = Path.GetFileName(path);
            LogDeleteFailed(_printerId, fileName);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording of printer '{PrinterId}' writes '{FileName}'.")]
    private partial void LogFileStarted(string printerId, string fileName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recording of printer '{PrinterId}' completed '{FileName}'.")]
    private partial void LogFileCompleted(string printerId, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording of printer '{PrinterId}' could not delete its empty file '{FileName}'.")]
    private partial void LogDeleteFailed(string printerId, string fileName);
}
