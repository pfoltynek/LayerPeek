using Microsoft.Extensions.Logging;

namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// One recording (T-144): reads a packet subscription of the shared camera connection on its own task and hands the
/// packets to a <see cref="ICameraRecordingWriter"/>. Packets before the first stream description and before the first
/// key frame are dropped (a file always starts with a key frame; right after an address change a packet of the old
/// connection may even arrive first). Every new connection (stream restart) completes the current file; the next one
/// starts with the next key frame, named with the next suffix (user decision 2026-11-03). Steady state allocates nothing.
/// </summary>
/// <remarks>
/// Writing never runs on the camera's streaming thread: the subscription queues the packets (bounded), so a slow disk
/// only fails this recording (<see cref="CameraRecordingFailure.TooSlow"/>) and never the live picture. Logs name files
/// only by their file name (the folder may contain the user name; user decision 2026-11-03).
/// </remarks>
internal sealed partial class CameraRecording : ICameraRecording
{
    /// <summary>Upper bound of suffixes tried for a free file name.</summary>
    private const int MaxSegments = 10_000;

    private readonly CameraPacketSubscription? _packets;
    private readonly ICameraRecordingWriterFactory? _writers;
    private readonly Action<ICameraRecording>? _changed;
    private readonly ILogger _logger;
    private readonly string _folder;
    private readonly string _name;
    private readonly string _extension;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private Task _completion = Task.CompletedTask;

    // Guarded by _gate.
    private string[] _files = [];
    private CameraRecordingState _state;
    private CameraRecordingFailure _failure;
    private string? _failureMessage;

    // Written by the recording task, read anywhere.
    private long _bytes;
    private long _durationTicks;

    // Recording task only.
    private CameraStreamInfo? _info;
    private ICameraRecordingWriter? _writer;
    private string _fileName = "";
    private RecordingClock _clock;
    private long _closedBytes;
    private TimeSpan _closedDuration;
    private int _nextSegment = 1;

    private CameraRecording(
        string printerId,
        string filePath,
        CameraPacketSubscription? packets,
        ICameraRecordingWriterFactory? writers,
        Action<ICameraRecording>? changed,
        ILogger logger)
    {
        PrinterId = printerId;
        _packets = packets;
        _writers = writers;
        _changed = changed;
        _logger = logger;
        _folder = Path.GetDirectoryName(filePath) ?? throw new ArgumentException("The recording path needs a folder.", nameof(filePath));
        _name = Path.GetFileNameWithoutExtension(filePath);
        _extension = Path.GetExtension(filePath);
    }

    public string PrinterId { get; }

    public CameraRecordingState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public CameraRecordingFailure Failure
    {
        get
        {
            lock (_gate)
            {
                return _failure;
            }
        }
    }

    public string? FailureMessage
    {
        get
        {
            lock (_gate)
            {
                return _failureMessage;
            }
        }
    }

    public IReadOnlyList<string> Files
    {
        get
        {
            lock (_gate)
            {
                return _files;
            }
        }
    }

    public long BytesWritten => Volatile.Read(ref _bytes);

    public TimeSpan Duration => TimeSpan.FromTicks(Volatile.Read(ref _durationTicks));

    public Task Completion => _completion;

    /// <summary>Starts reading <paramref name="packets"/> (owned from now on: disposed when the recording ends).</summary>
    internal static CameraRecording Start(
        string printerId,
        string filePath,
        CameraPacketSubscription packets,
        ICameraRecordingWriterFactory writers,
        Action<ICameraRecording>? changed,
        ILogger logger)
    {
        CameraRecording recording = new(printerId, filePath, packets, writers, changed, logger);
        string fileName = Path.GetFileName(filePath);
        recording.LogStarting(printerId, fileName);
        // On the thread pool: nothing of the recording runs on the caller's thread. Completion is set before the task
        // runs, so that an observer called from its first steps (e.g. an already ended lease) already sees the real one.
        Task<Task> run = new(recording.RunAsync, CancellationToken.None);
        recording._completion = run.Unwrap();
        run.Start(TaskScheduler.Default);
        return recording;
    }

    /// <summary>A recording that could not start (e.g. no muxer); already <see cref="CameraRecordingState.Failed"/>.</summary>
    internal static CameraRecording FailedAtOnce(string printerId, string filePath, CameraRecordingFailure failure, string message, ILogger logger)
    {
        CameraRecording recording = new(printerId, filePath, null, null, null, logger);
        recording._state = CameraRecordingState.Failed;
        recording._failure = failure;
        recording._failureMessage = message;
        recording.LogFailed(printerId, failure, message);
        return recording;
    }

    public async Task StopAsync()
    {
        try
        {
            await _stop.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A cancellation callback threw (the subscription's own never does); the recording still ends.
            LogUnexpected(ex, PrinterId);
        }

        await Completion.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task RunAsync()
    {
        CameraPacketSubscription packets = _packets!;
        CameraRecordingFailure failure = CameraRecordingFailure.None;
        string? message = null;
        try
        {
            await foreach (CameraStreamItem item in packets.ReadAsync(_stop.Token).ConfigureAwait(false))
            {
                if (item.Kind == CameraStreamItemKind.StreamStarted)
                {
                    // A new connection: the file of the previous one is complete, the next starts at a key frame.
                    CloseFile();
                    _info = item.Info;
                }
                else if (item.Kind == CameraStreamItemKind.Packet && _info is not null)
                {
                    Write(item.Packet);
                }
            }

            (failure, message) = packets.EndReason switch
            {
                CameraStreamEndReason.CameraUnavailable => (CameraRecordingFailure.CameraUnavailable, "The printer has no enabled camera."),
                CameraStreamEndReason.Overflow => (CameraRecordingFailure.TooSlow, "Writing the recording did not keep up with the camera."),
                CameraStreamEndReason.Failed => (CameraRecordingFailure.StreamFailed, "The camera stream failed."),
                // Disposed (only by this recording) or HubStopped (the application quits): a normal end.
                _ => (CameraRecordingFailure.None, null),
            };
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // Stopped by the owner.
        }
        catch (CameraRecordingException ex)
        {
            (failure, message) = (ex.Failure, ex.Message);
        }
        catch (Exception ex)
        {
            LogUnexpected(ex, PrinterId);
            (failure, message) = (CameraRecordingFailure.WriteFailed, $"Recording failed unexpectedly ({ex.GetType().Name}).");
        }
        finally
        {
            packets.Dispose();
        }

        try
        {
            CloseFile();
        }
        catch (CameraRecordingException ex) when (failure == CameraRecordingFailure.None)
        {
            (failure, message) = (ex.Failure, ex.Message);
        }
        catch (CameraRecordingException ex)
        {
            // Closing after a failure (e.g. the disk is still full): expected, and the first failure is the one that counts.
            LogCloseAfterFailureFailed(PrinterId, ex.Failure, ex.Message);
        }
        catch (Exception ex)
        {
            // Closing after a failure: the first failure is the one that counts.
            LogUnexpected(ex, PrinterId);
        }

        lock (_gate)
        {
            _state = failure == CameraRecordingFailure.None ? CameraRecordingState.Completed : CameraRecordingState.Failed;
            _failure = failure;
            _failureMessage = message;
        }

        if (failure == CameraRecordingFailure.None)
        {
            LogCompleted(PrinterId, _files.Length);
        }
        else
        {
            LogFailed(PrinterId, failure, message!);
        }

        NotifyChanged();
    }

    private void Write(in CameraPacket packet)
    {
        if (_writer is null)
        {
            if (!packet.IsKeyFrame)
            {
                return;
            }

            OpenFile(packet.Data.Span);
        }

        _clock.Next(packet, out long pts, out long dts, out long duration);
        _writer!.Write(packet.Data.Span, pts, dts, duration, packet.IsKeyFrame);
        Volatile.Write(ref _bytes, _closedBytes + _writer.BytesWritten);
        Volatile.Write(ref _durationTicks, (_closedDuration + _clock.Elapsed).Ticks);
    }

    private void OpenFile(ReadOnlySpan<byte> firstKeyFrame)
    {
        CameraStreamInfo info = _info!;
        string path = ReserveFile();
        try
        {
            _writer = _writers!.Create(path, info, firstKeyFrame);
        }
        catch
        {
            // Nothing was recorded into it: the reserved empty file goes.
            TryDelete(path);
            throw;
        }

        string fileName = Path.GetFileName(path);
        _fileName = fileName;
        _clock = new RecordingClock(info.TimeBase);
        lock (_gate)
        {
            _files = [.. _files, path];
            _state = CameraRecordingState.Recording;
        }

        LogFileStarted(PrinterId, fileName);
        NotifyChanged();
    }

    /// <summary>Completes the current file, if any.</summary>
    private void CloseFile()
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
            LogFileCompleted(PrinterId, _fileName);
        }
    }

    /// <summary>
    /// Creates the next file of this recording empty (never overwriting one): <c>name.ext</c>, then <c>name-2.ext</c>,
    /// <c>name-3.ext</c> …; a taken name is skipped.
    /// </summary>
    private string ReserveFile()
    {
        for (int attempt = 0; attempt < MaxSegments; attempt++)
        {
            int segment = _nextSegment++;
            string fileName = segment == 1
                ? _name + _extension
                : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{_name}-{segment}{_extension}");
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
                    IsDiskFull(ex) ? CameraRecordingFailure.DiskFull : CameraRecordingFailure.WriteFailed,
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

    /// <summary>
    /// ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL (Windows: HRESULT of the Win32 error) or ENOSPC (Unix: .NET puts the raw
    /// errno into the HRESULT; 28 on macOS and Linux).
    /// </summary>
    internal static bool IsDiskFull(Exception exception) =>
        exception is IOException && (OperatingSystem.IsWindows()
            ? (exception.HResult & 0xFFFF) is 0x70 or 0x27
            : exception.HResult == 28);

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string fileName = Path.GetFileName(path);
            LogDeleteFailed(PrinterId, fileName);
        }
    }

    private void NotifyChanged()
    {
        try
        {
            _changed?.Invoke(this);
        }
        catch (Exception ex)
        {
            LogObserverFailed(ex, PrinterId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording the camera of printer '{PrinterId}' into '{FileName}'.")]
    private partial void LogStarting(string printerId, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording of printer '{PrinterId}' writes '{FileName}'.")]
    private partial void LogFileStarted(string printerId, string fileName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recording of printer '{PrinterId}' completed '{FileName}'.")]
    private partial void LogFileCompleted(string printerId, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording of printer '{PrinterId}' ended ({Files} file(s)).")]
    private partial void LogCompleted(string printerId, int files);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording of printer '{PrinterId}' failed ({Failure}): {Reason}")]
    private partial void LogFailed(string printerId, CameraRecordingFailure failure, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recording of printer '{PrinterId}' could not complete its file after the failure either ({Failure}): {Reason}")]
    private partial void LogCloseAfterFailureFailed(string printerId, CameraRecordingFailure failure, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording of printer '{PrinterId}' could not delete its empty file '{FileName}'.")]
    private partial void LogDeleteFailed(string printerId, string fileName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording of printer '{PrinterId}' failed unexpectedly.")]
    private partial void LogUnexpected(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "An observer of the recording of printer '{PrinterId}' failed.")]
    private partial void LogObserverFailed(Exception exception, string printerId);
}
