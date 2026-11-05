using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Cameras.Recording;

/// <summary>Records into <see cref="FakeRecording"/>s that the test drives; no camera, no FFmpeg.</summary>
internal sealed class FakeCameraRecorder : ICameraRecorder
{
    private readonly ConcurrentQueue<FakeRecording> _started = new();

    public CameraRecorderAvailability Availability { get; set; } = CameraRecorderAvailability.Available;

    private readonly ConcurrentQueue<FakeDashcam> _dashcams = new();

    public FakeRecording[] Started => [.. _started];

    /// <summary>The dashcams started so far (T-148), oldest first.</summary>
    public FakeDashcam[] Dashcams => [.. _dashcams];

    public ICameraRecording Start(string printerId, string filePath, Action<ICameraRecording>? changed = null)
    {
        CameraRecorder.ValidatePath(filePath);
        FakeRecording recording = new(printerId, filePath, changed);
        _started.Enqueue(recording);
        return recording;
    }

    public ICameraDashcam StartDashcam(string printerId, CameraDashcamOptions options, Action<ICameraDashcam>? changed = null, Action<CameraDashcamSave>? saved = null)
    {
        FakeDashcam dashcam = new(printerId, options, changed, saved);
        _dashcams.Enqueue(dashcam);
        return dashcam;
    }
}

/// <summary>
/// A dashcam (T-148) that the test drives: a save writes a real file once the test ends it (<see cref="EndSave"/>), and it
/// ends when stopped, when it is asked to end after its save, or when the test ends it. Completion runs continuations
/// asynchronously, like the real one.
/// </summary>
internal sealed class FakeDashcam(string printerId, CameraDashcamOptions options, Action<ICameraDashcam>? changed, Action<CameraDashcamSave>? saved)
    : ICameraDashcam
{
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private readonly List<string> _saves = [];
    private CameraDashcamState _state;
    private CameraRecordingFailure _failure;
    private bool _endRequested;
    private int _extensions;
    private int _stops;
    private string? _saving;

    public string PrinterId { get; } = printerId;

    public CameraDashcamOptions Options { get; } = options;

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

    public string? FailureMessage => Failure == CameraRecordingFailure.None ? null : $"{Failure} (test)";

    public TimeSpan BufferedDuration => State == CameraDashcamState.Ended ? TimeSpan.Zero : TimeSpan.FromSeconds(42);

    public long BufferedBytes => State == CameraDashcamState.Ended ? 0 : 1000;

    public IReadOnlyList<string> SaveFiles
    {
        get
        {
            lock (_gate)
            {
                return _saving is { } path && File.Exists(path) ? [path] : [];
            }
        }
    }

    public long SaveBytesWritten { get; set; }

    public Task Completion => _done.Task;

    /// <summary>The paths of the saves started, in order.</summary>
    public string[] Saves
    {
        get
        {
            lock (_gate)
            {
                return [.. _saves];
            }
        }
    }

    /// <summary>How often a running save was extended by another event.</summary>
    public int Extensions => Volatile.Read(ref _extensions);

    public bool EndRequested
    {
        get
        {
            lock (_gate)
            {
                return _endRequested;
            }
        }
    }

    public int StopCalls => Volatile.Read(ref _stops);

    public bool IsEnded => _done.Task.IsCompleted;

    public bool Save(string filePath)
    {
        CameraRecorder.ValidatePath(filePath);
        lock (_gate)
        {
            if (_state == CameraDashcamState.Ended || (_endRequested && _state != CameraDashcamState.Saving))
            {
                return false;
            }

            if (_state == CameraDashcamState.Saving)
            {
                _extensions++;
                return true;
            }

            _state = CameraDashcamState.Saving;
            _saving = filePath;
            _saves.Add(filePath);
        }

        changed?.Invoke(this);
        return true;
    }

    /// <summary>The running save starts writing: its file exists (a few bytes) and is listed.</summary>
    public void BeginFile()
    {
        string path;
        lock (_gate)
        {
            path = _saving ?? throw new InvalidOperationException("No save runs.");
        }

        File.WriteAllBytes(path, [1, 2, 3, 4]);
        SaveBytesWritten = 4;
    }

    /// <summary>The running save ended (its file is written unless it failed before); then it ends when asked to.</summary>
    public void EndSave(CameraRecordingFailure failure = CameraRecordingFailure.None, bool writeFile = true)
    {
        string path;
        bool end;
        lock (_gate)
        {
            path = _saving ?? throw new InvalidOperationException("No save runs.");
            _saving = null;
            _state = CameraDashcamState.Buffering;
            end = _endRequested;
        }

        if (writeFile && !File.Exists(path))
        {
            File.WriteAllBytes(path, [1, 2, 3, 4]);
        }

        saved?.Invoke(new CameraDashcamSave(PrinterId, writeFile ? [path] : [], failure, failure == CameraRecordingFailure.None ? null : $"{failure} (test)", 4, TimeSpan.FromSeconds(150)));
        changed?.Invoke(this);
        if (end)
        {
            End(CameraRecordingFailure.None);
        }
    }

    public void EndAfterSave()
    {
        bool end;
        lock (_gate)
        {
            if (_endRequested || _state == CameraDashcamState.Ended)
            {
                return;
            }

            _endRequested = true;
            end = _state != CameraDashcamState.Saving;
        }

        if (end)
        {
            End(CameraRecordingFailure.None);
        }
    }

    /// <summary>Ends on its own (failed), or as asked.</summary>
    public void End(CameraRecordingFailure failure)
    {
        lock (_gate)
        {
            if (_done.Task.IsCompleted)
            {
                return;
            }

            _state = CameraDashcamState.Ended;
            _failure = failure;
        }

        changed?.Invoke(this);
        _done.TrySetResult();
    }

    public Task StopAsync()
    {
        Interlocked.Increment(ref _stops);
        bool saving;
        lock (_gate)
        {
            saving = _saving is not null;
        }

        if (saving)
        {
            EndSave();
        }

        End(CameraRecordingFailure.None);
        return _done.Task;
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}

/// <summary>
/// A recording that writes a real file once the test lets the "first key frame" arrive (<see cref="BeginFile"/>), and ends
/// when stopped or when the test ends it. Its completion runs continuations asynchronously, like the real one.
/// </summary>
internal sealed class FakeRecording(string printerId, string path, Action<ICameraRecording>? changed) : ICameraRecording
{
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private CameraRecordingState _state;
    private CameraRecordingFailure _failure;
    private string? _message;
    private string[] _files = [];
    private long _bytes;
    private int _stops;
    private Task? _stopDelay;

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

    public TimeSpan Duration => TimeSpan.FromSeconds(3);

    public Task Completion => _done.Task;

    public int StopCalls => Volatile.Read(ref _stops);

    public bool IsEnded => _done.Task.IsCompleted;

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

    /// <summary>When set, <see cref="StopAsync"/> completes the file only once this task completes (a slow last fragment).</summary>
    public Task? StopDelay
    {
        get => Volatile.Read(ref _stopDelay);
        set => Volatile.Write(ref _stopDelay, value);
    }

    public Task StopAsync()
    {
        Interlocked.Increment(ref _stops);
        if (StopDelay is { } delay)
        {
            return EndAfterAsync(delay);
        }

        End(CameraRecordingFailure.None);
        return _done.Task;
    }

    private async Task EndAfterAsync(Task delay)
    {
        await delay.ConfigureAwait(false);
        End(CameraRecordingFailure.None);
        await _done.Task.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}

/// <summary>The real file system with a simulated free space and folder failure.</summary>
internal sealed class FakeRecordingStorage : ICameraRecordingStorage
{
    private int _finds;

    /// <summary><see langword="null"/>: unknown (no check).</summary>
    public long? FreeBytes { get; set; } = 100_000_000_000;

    public Exception? PrepareFailure { get; set; }

    public int FindCalls => Volatile.Read(ref _finds);

    public void PrepareFolder(string folder)
    {
        if (PrepareFailure is { } failure)
        {
            throw failure;
        }

        CameraRecordingStorage.Instance.PrepareFolder(folder);
    }

    public long? GetAvailableFreeSpace(string folder) => FreeBytes;

    public IReadOnlyList<CameraRecordingFile> FindRecordings(string root)
    {
        Interlocked.Increment(ref _finds);
        return CameraRecordingStorage.Instance.FindRecordings(root);
    }

    public bool TryDelete(string path) => CameraRecordingStorage.Instance.TryDelete(path);
}

/// <summary>Monitor that raises snapshots on demand, synchronously on the calling thread.</summary>
internal sealed class SnapshotMonitor : IPrinterMonitor
{
    public event EventHandler<PrinterSnapshot>? SnapshotUpdated;

    public int SubscriberCount => SnapshotUpdated?.GetInvocationList().Length ?? 0;

    public IReadOnlyList<PrinterSnapshot> Snapshots => [];

    public bool TryGetSnapshot(string printerId, [NotNullWhen(true)] out PrinterSnapshot? snapshot)
    {
        snapshot = null;
        return false;
    }

    public Task RunAsync(IReadOnlyList<PrinterDefinition> printers, TimeSpan pollingInterval, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public void Raise(PrinterSnapshot snapshot) => SnapshotUpdated?.Invoke(this, snapshot);
}

/// <summary>A <see cref="FakeTimeProvider"/> that counts the timers that exist (created and not yet disposed).</summary>
internal sealed class CountingTimeProvider(FakeTimeProvider time) : TimeProvider
{
    private int _active;

    public FakeTimeProvider Fake { get; } = time;

    public int ActiveTimers => Volatile.Read(ref _active);

    public override DateTimeOffset GetUtcNow() => Fake.GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => Fake.LocalTimeZone;

    public override long TimestampFrequency => Fake.TimestampFrequency;

    public override long GetTimestamp() => Fake.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _active);
        return new CountedTimer(this, Fake.CreateTimer(callback, state, dueTime, period));
    }

    private sealed class CountedTimer(CountingTimeProvider owner, ITimer inner) : ITimer
    {
        private int _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref owner._active);
            }

            inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
