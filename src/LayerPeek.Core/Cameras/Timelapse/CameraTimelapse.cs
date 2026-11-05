using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>
/// One timelapse (T-158): reads a packet subscription of the shared camera connection on its own task and writes the first
/// key frame after each <see cref="RequestFrame"/> through the same <see cref="RecordingFiles"/> as a recording (fragmented
/// MP4, T-144), at a constant <see cref="ICameraTimelapseRecorder.FramesPerSecond"/>: frame <i>n</i> is at <i>n</i>/30 s,
/// whatever the camera's timestamps. Every other packet is dropped without a copy (the subscription takes key frames only).
/// A key frame (IDR with its SPS/PPS, as the Buddy3D sends it) decodes on its own, so the file plays without re-encoding.
/// </summary>
/// <remarks>
/// <para>
/// A new connection (outage, reconnect) continues the same file when its codec parameters (stream description and in-band
/// SPS/PPS) and image size are the same, compared at its first written key frame; otherwise that frame starts the next file
/// (<c>-2</c> …, like T-144).
/// </para>
/// <para>
/// A request only sets a flag; the reader takes it with the next key frame, so requesting costs nothing and nothing runs in
/// between but the subscription's wait for the next key frame. The steady state allocates nothing. Writing never runs on
/// the camera's streaming thread, and a failure ends only this timelapse.
/// </para>
/// </remarks>
internal sealed partial class CameraTimelapse : ICameraTimelapse
{
    private readonly CameraPacketSubscription? _packets;
    private readonly RecordingFiles? _files;
    private readonly Action<ICameraTimelapse>? _changed;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private Task _completion = Task.CompletedTask;

    // Guarded by _gate.
    private CameraRecordingState _state;
    private CameraRecordingFailure _failure;
    private string? _failureMessage;

    // Any thread.
    private int _requested;
    private int _ended;
    private long _frames;

    // The reader only (_itemsRead is read by tests).
    private long _itemsRead;
    private CameraStreamInfo? _info;
    private bool _newConnection;
    private CameraStreamInfo? _fileInfo;
    private byte[] _fileParameterSets = [];

    private CameraTimelapse(
        string printerId,
        string? filePath,
        CameraPacketSubscription? packets,
        ICameraRecordingWriterFactory? writers,
        Action<ICameraTimelapse>? changed,
        ILogger logger)
    {
        PrinterId = printerId;
        _packets = packets;
        _files = writers is null ? null : new RecordingFiles(printerId, filePath!, writers, logger, ICameraTimelapseRecorder.FramesPerSecond);
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

    public long FrameCount => Volatile.Read(ref _frames);

    public TimeSpan Duration => _files?.Duration ?? TimeSpan.Zero;

    public Task Completion => _completion;

    /// <summary>Stream items the reader took so far (descriptions and packets); tests wait for it.</summary>
    internal long ItemsRead => Volatile.Read(ref _itemsRead);

    /// <summary>Starts reading <paramref name="packets"/> (owned from now on: disposed when the timelapse ends).</summary>
    internal static CameraTimelapse Start(
        string printerId,
        string filePath,
        CameraPacketSubscription packets,
        ICameraRecordingWriterFactory writers,
        Action<ICameraTimelapse>? changed,
        ILogger logger)
    {
        CameraTimelapse timelapse = new(printerId, filePath, packets, writers, changed, logger);
        string fileName = Path.GetFileName(filePath);
        timelapse.LogStarting(printerId, fileName);
        // On the thread pool; Completion is set before the task runs (an observer of its first steps sees the real one).
        Task<Task> run = new(timelapse.RunAsync, CancellationToken.None);
        timelapse._completion = run.Unwrap();
        run.Start(TaskScheduler.Default);
        return timelapse;
    }

    /// <summary>A timelapse that could not start (e.g. no muxer); already <see cref="CameraRecordingState.Failed"/>.</summary>
    internal static CameraTimelapse FailedAtOnce(string printerId, CameraRecordingFailure failure, string message, ILogger logger)
    {
        CameraTimelapse timelapse = new(printerId, null, null, null, null, logger);
        timelapse._ended = 1;
        timelapse._state = CameraRecordingState.Failed;
        timelapse._failure = failure;
        timelapse._failureMessage = message;
        timelapse.LogFailed(printerId, failure, message);
        return timelapse;
    }

    public bool RequestFrame()
    {
        if (Volatile.Read(ref _ended) != 0)
        {
            return false;
        }

        Volatile.Write(ref _requested, 1);
        return true;
    }

    /// <remarks>
    /// The reader takes a request with <see cref="Interlocked.Exchange(ref int, int)"/>, so the two are ordered: either the
    /// exchange sees this write and the key frame is dropped, or the reader took the request first and that key frame,
    /// already being written, is the only one written after the call.
    /// </remarks>
    public void CancelFrameRequest() => Volatile.Write(ref _requested, 0);

    public async Task StopAsync()
    {
        Volatile.Write(ref _ended, 1);
        try
        {
            await _stop.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A cancellation callback threw (the subscription's own never does); the timelapse still ends.
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
                    // Whether its frames continue the current file is decided at its first written key frame.
                    _info = item.Info;
                    _newConnection = true;
                }
                else if (item.Kind == CameraStreamItemKind.Packet && item.Packet.IsKeyFrame && _info is not null)
                {
                    TakeIfRequested(item.Packet);
                }

                Volatile.Write(ref _itemsRead, _itemsRead + 1);
            }

            (failure, message) = packets.EndReason switch
            {
                CameraStreamEndReason.CameraUnavailable => (CameraRecordingFailure.CameraUnavailable, "The printer has no enabled camera."),
                CameraStreamEndReason.Overflow => (CameraRecordingFailure.TooSlow, "Writing the timelapse did not keep up with the camera."),
                CameraStreamEndReason.Failed => (CameraRecordingFailure.StreamFailed, "The camera stream failed."),
                // Disposed (only by this timelapse) or HubStopped (the application quits): a normal end.
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
            (failure, message) = (CameraRecordingFailure.WriteFailed, $"The timelapse failed unexpectedly ({ex.GetType().Name}).");
        }
        finally
        {
            Volatile.Write(ref _ended, 1);
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
            LogCompleted(PrinterId, FrameCount, _files!.Files.Count);
        }
        else
        {
            LogFailed(PrinterId, failure, message!);
        }

        NotifyChanged();
    }

    /// <summary>Writes <paramref name="keyFrame"/> when a frame was requested since the last one; drops it otherwise.</summary>
    private void TakeIfRequested(in CameraPacket keyFrame)
    {
        if (Volatile.Read(ref _requested) == 0 || Interlocked.Exchange(ref _requested, 0) == 0)
        {
            return;
        }

        RecordingFiles files = _files!;
        if (_newConnection)
        {
            _newConnection = false;
            if (files.IsOpen && !ContinuesFile(_info!, keyFrame.Data.Span))
            {
                LogStreamChanged(PrinterId);
                files.Close();
            }
        }

        bool opened = files.Write(_info!, keyFrame);
        if (opened)
        {
            _fileInfo = _info;
            _fileParameterSets = H264ParameterSets.Extract(keyFrame.Data.Span);
        }

        Volatile.Write(ref _frames, _frames + 1);
        if (opened)
        {
            lock (_gate)
            {
                _state = CameraRecordingState.Recording;
            }

            NotifyChanged();
        }
    }

    /// <summary>Whether the connection <paramref name="info"/> can go on in the open file: same codec parameters and size.</summary>
    private bool ContinuesFile(CameraStreamInfo info, ReadOnlySpan<byte> keyFrame)
    {
        CameraStreamInfo file = _fileInfo!;
        return info.Codec == file.Codec
            && info.Width == file.Width
            && info.Height == file.Height
            && info.Extradata.Span.SequenceEqual(file.Extradata.Span)
            && H264ParameterSets.Extract(keyFrame).AsSpan().SequenceEqual(_fileParameterSets);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Timelapse of the camera of printer '{PrinterId}' into '{FileName}'.")]
    private partial void LogStarting(string printerId, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Timelapse of printer '{PrinterId}': the camera reconnected with other stream parameters, the next file starts.")]
    private partial void LogStreamChanged(string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Timelapse of printer '{PrinterId}' ended ({Frames} frame(s), {Files} file(s)).")]
    private partial void LogCompleted(string printerId, long frames, int files);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Timelapse of printer '{PrinterId}' failed ({Failure}): {Reason}")]
    private partial void LogFailed(string printerId, CameraRecordingFailure failure, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Timelapse of printer '{PrinterId}' could not complete its file after the failure either ({Failure}): {Reason}")]
    private partial void LogCloseAfterFailureFailed(string printerId, CameraRecordingFailure failure, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Timelapse of printer '{PrinterId}' failed unexpectedly.")]
    private partial void LogUnexpected(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "An observer of the timelapse of printer '{PrinterId}' failed.")]
    private partial void LogObserverFailed(Exception exception, string printerId);
}
