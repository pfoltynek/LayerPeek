using Microsoft.Extensions.Logging;

namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// One dashcam (T-148, <see cref="ICameraDashcam"/>): reads a packet subscription of the shared camera connection on its own
/// task into a <see cref="DashcamBuffer"/>, and on <see cref="Save"/> writes the buffer and the following stream through the
/// same <see cref="RecordingFiles"/> as a full recording (fragmented MP4, T-144) until the end of the save. A new connection
/// clears the buffer (its packets do not continue the old ones) and, during a save, continues it in the next file
/// (<c>-2</c> …, like T-144). The steady state allocates nothing; a save allocates its file objects only.
/// </summary>
/// <remarks>
/// <para>
/// Requests (<see cref="Save"/>, <see cref="EndAfterSave"/>) and the end of a save come from other threads: they are noted
/// under the lock and the reader is woken by cancelling its current read (a fresh token per wake, so the subscription is
/// read on), and acts on them before its next packet. The end of a save is checked with every packet and by a one-shot
/// timer that exists only while a save runs (a stalled camera still ends the save on time). Nothing runs while idle but the
/// subscription's wait for the next packet.
/// </para>
/// <para>A failed save (disk full, I/O error) is reported and the dashcam goes on buffering; only the end of its subscription ends it.</para>
/// </remarks>
internal sealed partial class CameraDashcam : ICameraDashcam
{
    private readonly CameraPacketSubscription? _packets;
    private readonly ICameraRecordingWriterFactory? _writers;
    private readonly TimeProvider _time;
    private readonly CameraDashcamOptions _options;
    private readonly Action<ICameraDashcam>? _changed;
    private readonly Action<CameraDashcamSave>? _saved;
    private readonly ILogger _logger;
    private readonly DashcamBuffer _buffer;
    private readonly long _postEventTicks;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private Task _completion = Task.CompletedTask;

    // Guarded by _gate.
    private CameraDashcamState _state;
    private CameraRecordingFailure _failure;
    private string? _failureMessage;
    private string? _pendingSave;
    private bool _endRequested;
    private bool _stopping;
    private CancellationTokenSource _wake;
    private ITimer? _deadlineTimer;

    // Written under _gate (or by the reader alone), read anywhere.
    private long _deadline;
    private int _requested;
    private RecordingFiles? _saveFiles;

    // The reader only (_itemsRead is read by tests).
    private long _itemsRead;
    private CameraStreamInfo? _info;
    private RecordingFiles? _save;
    private bool _ending;

    private CameraDashcam(
        string printerId,
        CameraPacketSubscription? packets,
        ICameraRecordingWriterFactory? writers,
        TimeProvider time,
        CameraDashcamOptions options,
        Action<ICameraDashcam>? changed,
        Action<CameraDashcamSave>? saved,
        ILogger logger)
    {
        PrinterId = printerId;
        _packets = packets;
        _writers = writers;
        _time = time;
        _options = options;
        _changed = changed;
        _saved = saved;
        _logger = logger;
        _buffer = new DashcamBuffer(options.BufferDuration, options.MaxBufferBytes, options.MaxBufferPackets, time.TimestampFrequency);
        _postEventTicks = (long)(options.PostEventDuration.TotalSeconds * time.TimestampFrequency);
        _wake = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
    }

    public string PrinterId { get; }

    public CameraDashcamState State
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

    public TimeSpan BufferedDuration => _buffer.Duration;

    public long BufferedBytes => _buffer.MemoryBytes;

    public IReadOnlyList<string> SaveFiles => Volatile.Read(ref _saveFiles)?.Files ?? [];

    public long SaveBytesWritten => Volatile.Read(ref _saveFiles)?.BytesWritten ?? 0;

    public Task Completion => _completion;

    /// <summary>Stream items the reader took so far (descriptions and packets); tests pace a fake clock with it.</summary>
    internal long ItemsRead => Volatile.Read(ref _itemsRead);

    /// <summary>Starts reading <paramref name="packets"/> (owned from now on: disposed when the dashcam ends).</summary>
    internal static CameraDashcam Start(
        string printerId,
        CameraPacketSubscription packets,
        ICameraRecordingWriterFactory writers,
        TimeProvider time,
        CameraDashcamOptions options,
        Action<ICameraDashcam>? changed,
        Action<CameraDashcamSave>? saved,
        ILogger logger)
    {
        CameraDashcam dashcam = new(printerId, packets, writers, time, options, changed, saved, logger);
        dashcam.LogStarted(printerId, (int)options.BufferDuration.TotalSeconds, options.MaxBufferBytes);
        // On the thread pool; Completion is set before the task runs (an observer of its first steps sees the real one).
        Task<Task> run = new(dashcam.RunAsync, CancellationToken.None);
        dashcam._completion = run.Unwrap();
        run.Start(TaskScheduler.Default);
        return dashcam;
    }

    /// <summary>A dashcam that could not start (e.g. no muxer); already <see cref="CameraDashcamState.Ended"/>.</summary>
    internal static CameraDashcam FailedAtOnce(string printerId, CameraDashcamOptions options, CameraRecordingFailure failure, string message, ILogger logger)
    {
        CameraDashcam dashcam = new(printerId, null, null, TimeProvider.System, options, null, null, logger);
        dashcam._state = CameraDashcamState.Ended;
        dashcam._failure = failure;
        dashcam._failureMessage = message;
        dashcam._buffer.Release();
        dashcam.LogFailed(printerId, failure, message);
        return dashcam;
    }

    public bool Save(string filePath)
    {
        CameraRecorder.ValidatePath(filePath);
        bool started;
        lock (_gate)
        {
            if (_state == CameraDashcamState.Ended || _stopping || (_endRequested && _state != CameraDashcamState.Saving))
            {
                return false;
            }

            Volatile.Write(ref _deadline, _time.GetTimestamp() + _postEventTicks);
            started = _state != CameraDashcamState.Saving;
            if (started)
            {
                _pendingSave = filePath;
                Volatile.Write(ref _requested, 1);
                _state = CameraDashcamState.Saving;
                _deadlineTimer = _time.CreateTimer(static s => ((CameraDashcam)s!).OnDeadlineTimer(), this, _options.PostEventDuration, Timeout.InfiniteTimeSpan);
                _wake.Cancel();
            }
            else
            {
                // Another event during the save: the same file goes on until the post-event time after this one.
                _deadlineTimer?.Change(_options.PostEventDuration, Timeout.InfiniteTimeSpan);
            }
        }

        if (started)
        {
            NotifyChanged();
        }
        else
        {
            LogExtended(PrinterId);
        }

        return true;
    }

    public void EndAfterSave()
    {
        lock (_gate)
        {
            if (_endRequested || _state == CameraDashcamState.Ended)
            {
                return;
            }

            _endRequested = true;
            Volatile.Write(ref _requested, 1);
            _wake.Cancel();
        }
    }

    public async Task StopAsync()
    {
        lock (_gate)
        {
            _stopping = true;
        }

        try
        {
            await _stop.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A cancellation callback threw (the subscription's own never does); the dashcam still ends.
            LogUnexpected(ex, PrinterId);
        }

        await Completion.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    /// <summary>
    /// The deadline timer fired: wakes the reader to end the save, or, when it fired before the deadline by the timestamps
    /// (timers run on a coarser clock, on Windows ~15.6 ms), waits the rest, so that a stalled camera still ends the save.
    /// </summary>
    private void OnDeadlineTimer()
    {
        lock (_gate)
        {
            if (_state == CameraDashcamState.Ended || _deadlineTimer is not { } timer)
            {
                // Ended or left meanwhile (a late callback of a disposed timer).
                return;
            }

            long remaining = Volatile.Read(ref _deadline) - _time.GetTimestamp();
            if (remaining > 0)
            {
                // At least 1 ms: rounding down to 0 would fire again at once.
                double milliseconds = Math.Max(1, Math.Ceiling(remaining * 1000.0 / _time.TimestampFrequency));
                timer.Change(TimeSpan.FromMilliseconds(milliseconds), Timeout.InfiniteTimeSpan);
                return;
            }

            // Cancelled under the lock: the reader replaces (and disposes) the source only under it, never one in use here;
            // once ended (the source is disposed), a late callback does nothing (above).
            _wake.Cancel();
        }
    }

    private async Task RunAsync()
    {
        CameraPacketSubscription packets = _packets!;
        CameraRecordingFailure failure = CameraRecordingFailure.None;
        string? message = null;
        try
        {
            bool ended = false;
            while (!ended)
            {
                CancellationToken token;
                lock (_gate)
                {
                    token = _wake.Token;
                }

                try
                {
                    await foreach (CameraStreamItem item in packets.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (HandleRequests() || Step(item))
                        {
                            ended = true;
                            break;
                        }
                    }

                    if (!ended)
                    {
                        // The subscription ended.
                        (failure, message) = packets.EndReason switch
                        {
                            CameraStreamEndReason.CameraUnavailable => (CameraRecordingFailure.CameraUnavailable, "The printer has no enabled camera."),
                            CameraStreamEndReason.Overflow => (CameraRecordingFailure.TooSlow, "The dashcam did not keep up with the camera."),
                            CameraStreamEndReason.Failed => (CameraRecordingFailure.StreamFailed, "The camera stream failed."),
                            // Disposed (only by this dashcam) or HubStopped (the application quits): a normal end.
                            _ => (CameraRecordingFailure.None, null),
                        };
                        ended = true;
                    }
                }
                catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
                {
                    // Woken: a request or the end of the save. Read on with a fresh token.
                    RenewWake();
                    ended = HandleRequests();
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // Stopped by the owner.
        }
        catch (Exception ex)
        {
            LogUnexpected(ex, PrinterId);
            (failure, message) = (CameraRecordingFailure.StreamFailed, $"The dashcam failed unexpectedly ({ex.GetType().Name}).");
        }
        finally
        {
            packets.Dispose();
        }

        // A save still running is complete with what it has; nothing is held any more.
        EndSave(CameraRecordingFailure.None, null, final: true);
        _buffer.Release();
        CancellationTokenSource wake;
        lock (_gate)
        {
            _deadlineTimer?.Dispose();
            _deadlineTimer = null;
            _pendingSave = null;
            _state = CameraDashcamState.Ended;
            _failure = failure;
            _failureMessage = message;
            wake = _wake;
        }

        // _stop is not disposed: StopAsync may still be called (it has no timer, nothing to free once the wake source is gone).
        wake.Dispose();
        if (failure == CameraRecordingFailure.None)
        {
            LogEnded(PrinterId);
        }
        else
        {
            LogFailed(PrinterId, failure, message!);
        }

        NotifyChanged();
    }

    private void RenewWake()
    {
        CancellationTokenSource old;
        lock (_gate)
        {
            if (!_wake.IsCancellationRequested)
            {
                return;
            }

            old = _wake;
            _wake = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        }

        old.Dispose();
    }

    /// <summary>Acts on the requests and the end of the save; <see langword="true"/> when the dashcam ends now.</summary>
    private bool HandleRequests()
    {
        if (Volatile.Read(ref _requested) == 0 && _save is null && !_ending)
        {
            // The steady state: nothing asked, nothing saving.
            return false;
        }

        string? path;
        bool end;
        lock (_gate)
        {
            path = _pendingSave;
            _pendingSave = null;
            end = _endRequested;
            Volatile.Write(ref _requested, 0);
        }

        if (path is not null)
        {
            BeginSave(path);
        }

        if (end && !_ending)
        {
            // The print ended or the full recording took over: nothing is buffered any more, a running save goes on.
            _ending = true;
            _buffer.Release();
        }

        TryEndSaveOnTime();
        if (!_ending)
        {
            return false;
        }

        lock (_gate)
        {
            return _state != CameraDashcamState.Saving;
        }
    }

    /// <summary>One item of the stream; <see langword="true"/> when the dashcam ends now.</summary>
    private bool Step(in CameraStreamItem item)
    {
        Volatile.Write(ref _itemsRead, _itemsRead + 1);
        if (item.Kind == CameraStreamItemKind.StreamStarted)
        {
            // A new connection: the buffered packets do not continue into its packets, and a save goes on in its next file.
            _buffer.Clear();
            CloseSaveFile();
            _info = item.Info;
            return false;
        }

        if (item.Kind != CameraStreamItemKind.Packet || _info is null)
        {
            // Packets before the first stream description (e.g. of an old connection right after an address change).
            return false;
        }

        if (_save is not null)
        {
            WriteSave(item.Packet);
        }

        long now = _time.GetTimestamp();
        if (!_ending)
        {
            _buffer.Add(item.Packet, now);
        }

        if (_save is not null && now >= Volatile.Read(ref _deadline))
        {
            TryEndSaveOnTime();
            if (_ending)
            {
                lock (_gate)
                {
                    return _state != CameraDashcamState.Saving;
                }
            }
        }

        return false;
    }

    /// <summary>Writes the buffer into the new save's first file; the next packets follow.</summary>
    private void BeginSave(string path)
    {
        RecordingFiles save = new(PrinterId, path, _writers!, _logger);
        _save = save;
        Volatile.Write(ref _saveFiles, save);
        string fileName = Path.GetFileName(path);
        int seconds = (int)_buffer.Duration.TotalSeconds;
        LogSaving(PrinterId, fileName, seconds);
        if (_info is not { } info)
        {
            return;
        }

        try
        {
            for (int i = 0; i < _buffer.Count; i++)
            {
                save.Write(info, _buffer[i]);
            }
        }
        catch (Exception ex)
        {
            FailSave(ex);
        }
    }

    private void WriteSave(in CameraPacket packet)
    {
        try
        {
            _save!.Write(_info!, packet);
        }
        catch (Exception ex)
        {
            FailSave(ex);
        }
    }

    /// <summary>A save that cannot be written ends now with the failure; buffering goes on.</summary>
    private void FailSave(Exception exception)
    {
        (CameraRecordingFailure failure, string message) = exception is CameraRecordingException recording
            ? (recording.Failure, recording.Message)
            : (CameraRecordingFailure.WriteFailed, $"Saving the dashcam failed unexpectedly ({exception.GetType().Name}).");
        if (exception is not CameraRecordingException)
        {
            LogUnexpected(exception, PrinterId);
        }

        lock (_gate)
        {
            LeaveSaving();
        }

        EndSave(failure, message, final: false);
    }

    /// <summary>A stream restart: the save's current file is complete, the next key frame starts the next one.</summary>
    private void CloseSaveFile()
    {
        if (_save is null)
        {
            return;
        }

        try
        {
            _save.Close();
        }
        catch (Exception ex)
        {
            FailSave(ex);
        }
    }

    /// <summary>Ends the save when its time is up (and no later event extended it meanwhile).</summary>
    private void TryEndSaveOnTime()
    {
        if (_save is null)
        {
            return;
        }

        lock (_gate)
        {
            // Checked and left in one step under the lock: an event that extends the save takes the lock too, so it either
            // extends it in time or (once it is left) starts the next save; it is never lost.
            if (_time.GetTimestamp() < _deadline && !_stopping)
            {
                return;
            }

            LeaveSaving();
        }

        EndSave(CameraRecordingFailure.None, null, final: false);
    }

    /// <summary>Under <see cref="_gate"/>: no save runs from now on (a new <see cref="Save"/> starts the next one).</summary>
    private void LeaveSaving()
    {
        _deadlineTimer?.Dispose();
        _deadlineTimer = null;
        if (_state == CameraDashcamState.Saving && _pendingSave is null)
        {
            _state = CameraDashcamState.Buffering;
        }
    }

    /// <summary>
    /// Completes the running save and reports it (the state was left before, see <see cref="LeaveSaving"/>);
    /// <paramref name="final"/>: the dashcam ends right after.
    /// </summary>
    private void EndSave(CameraRecordingFailure failure, string? message, bool final)
    {
        RecordingFiles? save = _save;
        if (save is null)
        {
            return;
        }

        _save = null;
        try
        {
            save.Close();
        }
        catch (CameraRecordingException ex) when (failure == CameraRecordingFailure.None)
        {
            (failure, message) = (ex.Failure, ex.Message);
        }
        catch (CameraRecordingException ex)
        {
            // Closing after a failure (e.g. the disk is still full): the first failure is the one that counts.
            LogCloseAfterFailureFailed(PrinterId, ex.Failure, ex.Message);
        }
        catch (Exception ex)
        {
            LogUnexpected(ex, PrinterId);
            if (failure == CameraRecordingFailure.None)
            {
                (failure, message) = (CameraRecordingFailure.WriteFailed, $"Saving the dashcam failed unexpectedly ({ex.GetType().Name}).");
            }
        }

        Volatile.Write(ref _saveFiles, null);
        CameraDashcamSave result = new(PrinterId, save.Files, failure, message, save.BytesWritten, save.Duration);
        if (failure == CameraRecordingFailure.None)
        {
            LogSaved(PrinterId, result.Files.Count, (int)result.Duration.TotalSeconds);
        }
        else
        {
            LogSaveFailed(PrinterId, failure, message!);
        }

        try
        {
            _saved?.Invoke(result);
        }
        catch (Exception ex)
        {
            LogObserverFailed(ex, PrinterId);
        }

        if (!final)
        {
            NotifyChanged();
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Dashcam of printer '{PrinterId}' keeps the last {Seconds} s in memory (at most {MaxBytes} bytes).")]
    private partial void LogStarted(string printerId, int seconds, long maxBytes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dashcam of printer '{PrinterId}' saves {Seconds} s from before the event into '{FileName}'.")]
    private partial void LogSaving(string printerId, string fileName, int seconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dashcam of printer '{PrinterId}' goes on saving after another event.")]
    private partial void LogExtended(string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dashcam of printer '{PrinterId}' saved {Seconds} s into {Files} file(s).")]
    private partial void LogSaved(string printerId, int files, int seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dashcam of printer '{PrinterId}' could not save ({Failure}): {Reason}")]
    private partial void LogSaveFailed(string printerId, CameraRecordingFailure failure, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dashcam of printer '{PrinterId}' could not complete its file after the failure either ({Failure}): {Reason}")]
    private partial void LogCloseAfterFailureFailed(string printerId, CameraRecordingFailure failure, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dashcam of printer '{PrinterId}' ended.")]
    private partial void LogEnded(string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dashcam of printer '{PrinterId}' failed ({Failure}): {Reason}")]
    private partial void LogFailed(string printerId, CameraRecordingFailure failure, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dashcam of printer '{PrinterId}' failed unexpectedly.")]
    private partial void LogUnexpected(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "An observer of the dashcam of printer '{PrinterId}' failed.")]
    private partial void LogObserverFailed(Exception exception, string printerId);
}
