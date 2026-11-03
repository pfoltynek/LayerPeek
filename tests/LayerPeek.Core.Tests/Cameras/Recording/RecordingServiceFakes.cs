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

    public FakeRecording[] Started => [.. _started];

    public ICameraRecording Start(string printerId, string filePath, Action<ICameraRecording>? changed = null)
    {
        CameraRecorder.ValidatePath(filePath);
        FakeRecording recording = new(printerId, filePath, changed);
        _started.Enqueue(recording);
        return recording;
    }
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

    public Task StopAsync()
    {
        Interlocked.Increment(ref _stops);
        End(CameraRecordingFailure.None);
        return _done.Task;
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
