using System.Collections.Concurrent;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Tests.Cameras.Recording;

/// <summary>One packet as a <see cref="FakeRecordingWriter"/> received it.</summary>
internal readonly record struct WrittenPacket(byte Fill, int Length, long Pts, long Dts, long Duration, bool IsKeyFrame);

/// <summary>Creates <see cref="FakeRecordingWriter"/>s; the files themselves stay as <see cref="CameraRecording"/> reserved them.</summary>
internal sealed class FakeRecordingWriterFactory : ICameraRecordingWriterFactory
{
    private readonly ConcurrentQueue<FakeRecordingWriter> _writers = new();

    public CameraRecorderAvailability Availability { get; init; } = CameraRecorderAvailability.Available;

    /// <summary>Thrown by <see cref="Create"/>.</summary>
    public Exception? CreateFailure { get; init; }

    /// <summary>The writer fails its n-th write (1-based) with this exception.</summary>
    public (int Write, Exception Error)? WriteFailure { get; init; }

    /// <summary>Thrown by <see cref="FakeRecordingWriter.Complete"/>.</summary>
    public Exception? CompleteFailure { get; init; }

    public FakeRecordingWriter[] Writers => [.. _writers];

    public ICameraRecordingWriter Create(string path, CameraStreamInfo info, ReadOnlySpan<byte> firstKeyFrame)
    {
        Assert.True(File.Exists(path), "The recording reserves its file before the writer opens it.");
        if (CreateFailure is { } failure)
        {
            throw failure;
        }

        FakeRecordingWriter writer = new(path, info, firstKeyFrame.ToArray(), WriteFailure, CompleteFailure);
        _writers.Enqueue(writer);
        return writer;
    }
}

internal sealed class FakeRecordingWriter(string path, CameraStreamInfo info, byte[] firstKeyFrame, (int Write, Exception Error)? writeFailure, Exception? completeFailure)
    : ICameraRecordingWriter
{
    private readonly WrittenPacket[] _packets = new WrittenPacket[4096];
    private int _count;
    private long _bytes;
    private int _completed;
    private int _disposed;

    public string Path { get; } = path;

    public CameraStreamInfo Info { get; } = info;

    public byte[] FirstKeyFrame { get; } = firstKeyFrame;

    public long BytesWritten => Volatile.Read(ref _bytes);

    public int Count => Volatile.Read(ref _count);

    public WrittenPacket[] Packets => _packets.AsSpan(0, Count).ToArray();

    public bool IsCompleted => Volatile.Read(ref _completed) != 0;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Allocation-free (steady-state tests): packets go into a preallocated array.</summary>
    public void Write(ReadOnlySpan<byte> data, long pts, long dts, long duration, bool isKeyFrame)
    {
        Assert.False(IsCompleted);
        if (writeFailure is { } failure && _count + 1 == failure.Write)
        {
            throw failure.Error;
        }

        _packets[_count % _packets.Length] = new WrittenPacket(data.IsEmpty ? (byte)0 : data[0], data.Length, pts, dts, duration, isKeyFrame);
        Volatile.Write(ref _count, _count + 1);
        Volatile.Write(ref _bytes, _bytes + data.Length);
    }

    public void Complete()
    {
        Volatile.Write(ref _completed, 1);
        if (completeFailure is { } failure)
        {
            throw failure;
        }
    }

    public void Dispose() => Volatile.Write(ref _disposed, 1);
}

/// <summary>A packet subscription without a hub, filled by the test.</summary>
internal sealed class TestPackets
{
    public static readonly CameraStreamInfo Info = new(CameraVideoCodec.H264, 1280, 720, CameraTimeBase.Rtp90kHz, new byte[] { 0, 0, 0, 1, 0x67, 0x42 });

    private readonly byte[] _buffer = new byte[64];
    private long _timestamp;

    public TestPackets(int capacity = CameraPacketSubscriptionOptions.DefaultQueueCapacity)
    {
        Subscription = new CameraPacketSubscription(null, "mk4", new CameraPacketSubscriptionOptions { QueueCapacity = capacity });
    }

    public CameraPacketSubscription Subscription { get; }

    public void Started(CameraStreamInfo? info = null) => Assert.True(Subscription.TryEnqueue(info ?? Info));

    /// <summary>A packet of <paramref name="length"/> bytes filled with <paramref name="fill"/>, 1/20 s after the previous one.</summary>
    public void Packet(byte fill, bool keyFrame = false, int length = 16, long? pts = -1, long? dts = -1)
    {
        _timestamp += 4500;
        _buffer.AsSpan(0, length).Fill(fill);
        CameraPacket packet = new(
            _buffer.AsMemory(0, length),
            pts == -1 ? _timestamp : pts,
            dts == -1 ? _timestamp : dts,
            4500,
            keyFrame,
            CameraTimeBase.Rtp90kHz);
        Assert.True(Subscription.TryEnqueue(packet));
    }

    public void End(CameraStreamEndReason reason) => Subscription.TryEnd(reason, failure: null);
}
