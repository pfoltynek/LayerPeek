using System.Collections.Concurrent;

using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>Starts <see cref="FakeTimelapse"/>s that the test drives; no camera, no FFmpeg.</summary>
internal sealed class FakeCameraTimelapseRecorder : ICameraTimelapseRecorder
{
    private readonly ConcurrentQueue<FakeTimelapse> _started = new();

    public CameraRecorderAvailability Availability { get; set; } = CameraRecorderAvailability.Available;

    public FakeTimelapse[] Started => [.. _started];

    public ICameraTimelapse Start(string printerId, string filePath, Action<ICameraTimelapse>? changed = null)
    {
        CameraRecorder.ValidatePath(filePath);
        FakeTimelapse timelapse = new(printerId, filePath, changed);
        _started.Enqueue(timelapse);
        return timelapse;
    }
}

/// <summary>
/// A timelapse that counts the frames asked for, writes a real file once the test lets the "first key frame" arrive
/// (<see cref="BeginFile"/>), and ends when stopped or when the test ends it. Its completion runs continuations
/// asynchronously, like the real one.
/// </summary>
internal sealed class FakeTimelapse(string printerId, string path, Action<ICameraTimelapse>? changed) : ICameraTimelapse
{
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private CameraRecordingState _state;
    private CameraRecordingFailure _failure;
    private string? _message;
    private string[] _files = [];
    private long _bytes;
    private int _requests;
    private int _stops;

    public string PrinterId { get; } = printerId;

    public string Path { get; } = path;

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
                return _message;
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

    public long BytesWritten
    {
        get => Volatile.Read(ref _bytes);
        set => Volatile.Write(ref _bytes, value);
    }

    /// <summary>Every request is taken as a frame (the fake has no key frames to wait for).</summary>
    public long FrameCount => Requests;

    public TimeSpan Duration => TimeSpan.FromSeconds(FrameCount / (double)ICameraTimelapseRecorder.FramesPerSecond);

    public Task Completion => _done.Task;

    /// <summary>How often a frame was asked for while it ran.</summary>
    public int Requests => Volatile.Read(ref _requests);

    public int StopCalls => Volatile.Read(ref _stops);

    public bool IsEnded => _done.Task.IsCompleted;

    public bool RequestFrame()
    {
        if (IsEnded)
        {
            return false;
        }

        Interlocked.Increment(ref _requests);
        return true;
    }

    /// <summary>The first key frame arrived: the file exists (with a few bytes) and the state is Recording.</summary>
    public void BeginFile()
    {
        File.WriteAllBytes(Path, [1, 2, 3, 4]);
        lock (_gate)
        {
            _files = [Path];
            _state = CameraRecordingState.Recording;
        }

        BytesWritten = 4;
        changed?.Invoke(this);
    }

    /// <summary>Ends on its own: failed, or completed (e.g. the hub stopped).</summary>
    public void End(CameraRecordingFailure failure, string? message = null)
    {
        lock (_gate)
        {
            if (_done.Task.IsCompleted)
            {
                return;
            }

            _state = failure == CameraRecordingFailure.None ? CameraRecordingState.Completed : CameraRecordingState.Failed;
            _failure = failure;
            _message = failure == CameraRecordingFailure.None ? null : message ?? $"{failure} (test)";
        }

        changed?.Invoke(this);
        _done.TrySetResult();
    }

    public Task StopAsync()
    {
        Interlocked.Increment(ref _stops);
        End(CameraRecordingFailure.None);
        return _done.Task;
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}

/// <summary>A trigger that records what the service tells it (the plug-in point of T-159).</summary>
internal sealed class RecordingTrigger : ICameraTimelapseTrigger
{
    private readonly ConcurrentQueue<PrinterState> _snapshots = new();
    private readonly ConcurrentQueue<bool> _capturing = new();
    private int _disposed;

    public PrinterState[] Snapshots => [.. _snapshots];

    public bool[] Capturing => [.. _capturing];

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void SetCapturing(bool capturing) => _capturing.Enqueue(capturing);

    public void OnSnapshot(PrinterSnapshot snapshot) => _snapshots.Enqueue(snapshot.State);

    public void Dispose() => Volatile.Write(ref _disposed, 1);
}
