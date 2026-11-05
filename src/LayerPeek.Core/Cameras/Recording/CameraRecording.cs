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
    private readonly CameraPacketSubscription? _packets;
    private readonly RecordingFiles? _files;
    private readonly Action<ICameraRecording>? _changed;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private Task _completion = Task.CompletedTask;

    // Guarded by _gate.
    private CameraRecordingState _state;
    private CameraRecordingFailure _failure;
    private string? _failureMessage;

    // Recording task only.
    private CameraStreamInfo? _info;

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
        _files = writers is null ? null : new RecordingFiles(printerId, filePath, writers, logger);
        _changed = changed;
        _logger = logger;
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

    public IReadOnlyList<string> Files => _files?.Files ?? [];

    public long BytesWritten => _files?.BytesWritten ?? 0;

    public TimeSpan Duration => _files?.Duration ?? TimeSpan.Zero;

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
                    _files!.Close();
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
            _files!.Close();
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
            LogCompleted(PrinterId, _files!.Files.Count);
        }
        else
        {
            LogFailed(PrinterId, failure, message!);
        }

        NotifyChanged();
    }

    private void Write(in CameraPacket packet)
    {
        if (_files!.Write(_info!, packet))
        {
            // The first packet of a file (a key frame): the recording writes.
            lock (_gate)
            {
                _state = CameraRecordingState.Recording;
            }

            NotifyChanged();
        }
    }

    /// <summary>
    /// ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL (Windows: HRESULT of the Win32 error) or ENOSPC (Unix: .NET puts the raw
    /// errno into the HRESULT; 28 on macOS and Linux).
    /// </summary>
    internal static bool IsDiskFull(Exception exception) =>
        exception is IOException && (OperatingSystem.IsWindows()
            ? (exception.HResult & 0xFFFF) is 0x70 or 0x27
            : exception.HResult == 28);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording of printer '{PrinterId}' ended ({Files} file(s)).")]
    private partial void LogCompleted(string printerId, int files);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording of printer '{PrinterId}' failed ({Failure}): {Reason}")]
    private partial void LogFailed(string printerId, CameraRecordingFailure failure, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recording of printer '{PrinterId}' could not complete its file after the failure either ({Failure}): {Reason}")]
    private partial void LogCloseAfterFailureFailed(string printerId, CameraRecordingFailure failure, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording of printer '{PrinterId}' failed unexpectedly.")]
    private partial void LogUnexpected(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "An observer of the recording of printer '{PrinterId}' failed.")]
    private partial void LogObserverFailed(Exception exception, string printerId);
}
