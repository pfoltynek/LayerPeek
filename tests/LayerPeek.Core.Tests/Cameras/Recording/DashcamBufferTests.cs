using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Tests.Cameras.Recording;

/// <summary>T-148: the last minutes in memory: whole GOPs from a key frame, bounded by time, memory and packet count.</summary>
public sealed class DashcamBufferTests
{
    /// <summary>Timestamps in milliseconds.</summary>
    private const long Frequency = 1000;

    private readonly byte[] _data = new byte[2 * 1024 * 1024];
    private long _pts;

    private static DashcamBuffer Buffer(TimeSpan duration, long maxBytes = 64L * 1024 * 1024, int maxPackets = 100_000) =>
        new(duration, maxBytes, maxPackets, Frequency);

    /// <summary>A packet filled with <paramref name="fill"/>, 40 ms after the previous one.</summary>
    private CameraPacket Packet(byte fill, bool key, int length = 1000)
    {
        _data.AsSpan(0, length).Fill(fill);
        _pts += 3600;
        return new CameraPacket(_data.AsMemory(0, length), _pts, _pts, 3600, key, CameraTimeBase.Rtp90kHz);
    }

    /// <summary>25 fps for <paramref name="seconds"/> s from <paramref name="start"/> ms, a key frame every <paramref name="gop"/> packets.</summary>
    private long Feed(DashcamBuffer buffer, int seconds, long start = 0, int gop = 50, int length = 1000, Action<DashcamBuffer>? after = null)
    {
        long time = start;
        for (int i = 0; i < seconds * 25; i++)
        {
            buffer.Add(Packet((byte)(i % 251), key: i % gop == 0, length), time);
            after?.Invoke(buffer);
            time += 40;
        }

        return time;
    }

    private static void AssertStartsWithAKeyFrame(DashcamBuffer buffer)
    {
        Assert.True(buffer.Count > 0);
        Assert.True(buffer[0].IsKeyFrame);
    }

    [Fact]
    public void PacketsBeforeTheFirstKeyFrameAreDropped()
    {
        DashcamBuffer buffer = Buffer(TimeSpan.FromMinutes(1));

        Assert.False(buffer.Add(Packet(1, key: false), 0));
        Assert.False(buffer.Add(Packet(2, key: false), 40));
        Assert.True(buffer.Add(Packet(3, key: true), 80));
        Assert.True(buffer.Add(Packet(4, key: false), 120));

        Assert.Equal(2, buffer.Count);
        Assert.Equal(3, buffer[0].Data.Span[0]);
        Assert.True(buffer[0].IsKeyFrame);
        Assert.Equal(4, buffer[1].Data.Span[0]);
        Assert.Equal(2000, buffer.DataBytes);
        Assert.Equal(TimeSpan.FromMilliseconds(40), buffer.Duration);
    }

    [Fact]
    public void ItHoldsAtMostTheSetLengthAndAlwaysStartsWithAKeyFrame()
    {
        TimeSpan length = TimeSpan.FromSeconds(30);
        DashcamBuffer buffer = Buffer(length);

        // Two seconds per GOP (like the Buddy3D's ~2.3 s), three times the length of the buffer.
        Feed(buffer, 90, after: b =>
        {
            Assert.True(b.Duration <= length, $"{b.Duration} held");
            AssertStartsWithAKeyFrame(b);
        });

        // Never shorter than the length minus one GOP.
        Assert.InRange(buffer.Duration, length - TimeSpan.FromSeconds(2), length);
        Assert.Equal(buffer.Count * 1000L, buffer.DataBytes);
        // The packets are kept in order and intact (the last packet was number 2249).
        Assert.Equal((byte)(2249 % 251), buffer[buffer.Count - 1].Data.Span[0]);
        Assert.All(Enumerable.Range(0, buffer.Count), i => Assert.Equal(1000, buffer[i].Data.Length));
    }

    [Fact]
    public void MemoryIsBoundedByBytesAndReusedOnceFull()
    {
        long maxBytes = 2L * 1024 * 1024;
        DashcamBuffer buffer = Buffer(TimeSpan.FromMinutes(10), maxBytes);

        // 25 kB per packet: 625 kB/s, far more than the bound allows for ten minutes.
        Feed(buffer, 60, length: 25_000, after: b =>
        {
            Assert.True(b.MemoryBytes <= maxBytes + DashcamBuffer.MaxFreeChunks * DashcamBuffer.ChunkSize, $"{b.MemoryBytes} bytes held");
            AssertStartsWithAKeyFrame(b);
        });

        Assert.True(buffer.DataBytes <= maxBytes);
        Assert.True(buffer.DataBytes > maxBytes / 2, "The bound is used, not just a GOP.");
    }

    [Fact]
    public void AnAddOfAFullBufferAllocatesNothing()
    {
        DashcamBuffer buffer = Buffer(TimeSpan.FromSeconds(20));
        long time = Feed(buffer, 60);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Feed(buffer, 60, start: time);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ACameraWithoutKeyFramesNeverHoldsMoreThanTheBound()
    {
        long maxBytes = 1024 * 1024;
        DashcamBuffer buffer = Buffer(TimeSpan.FromMinutes(10), maxBytes);

        // One key frame, then none for a long time: the GOP grows until it hits the bound, then the buffer starts over.
        long time = Feed(buffer, 120, gop: int.MaxValue, length: 10_000, after: b =>
            Assert.True(b.MemoryBytes <= maxBytes + DashcamBuffer.MaxFreeChunks * DashcamBuffer.ChunkSize));
        Assert.Equal(0, buffer.Count);
        Assert.Equal(0, buffer.DataBytes);
        Assert.False(buffer.Add(Packet(1, key: false), time));

        // The next key frame starts it again.
        Assert.True(buffer.Add(Packet(2, key: true), time + 40));
        AssertStartsWithAKeyFrame(buffer);
    }

    [Fact]
    public void ASingleGopLongerThanTheLengthStartsOverAtTheNextKeyFrame()
    {
        DashcamBuffer buffer = Buffer(TimeSpan.FromSeconds(10));

        long time = Feed(buffer, 11, gop: int.MaxValue);

        Assert.Equal(0, buffer.Count);
        Assert.True(buffer.Add(Packet(1, key: true), time));
        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public void ThePacketCountIsBounded()
    {
        DashcamBuffer buffer = Buffer(TimeSpan.FromMinutes(10), maxPackets: 120);

        Feed(buffer, 30, gop: 25, length: 10, after: b =>
        {
            Assert.True(b.Count <= 120);
            AssertStartsWithAKeyFrame(b);
        });

        Assert.InRange(buffer.Count, 96, 120);
    }

    [Fact]
    public void PacketsBiggerThanAChunkAreHeldToo()
    {
        DashcamBuffer buffer = Buffer(TimeSpan.FromSeconds(10));
        int big = DashcamBuffer.ChunkSize + 1000;

        Assert.True(buffer.Add(Packet(7, key: true, length: big), 0));
        Assert.True(buffer.Add(Packet(8, key: false), 40));
        Assert.True(buffer.Add(Packet(9, key: true, length: big), 80));

        Assert.Equal(3, buffer.Count);
        Assert.Equal(big, buffer[0].Data.Length);
        Assert.True(buffer[0].Data.Span.IndexOfAnyExcept((byte)7) < 0);
        Assert.True(buffer[2].Data.Span.IndexOfAnyExcept((byte)9) < 0);
    }

    [Fact]
    public void APacketBiggerThanTheWholeBufferIsNeverHeld()
    {
        DashcamBuffer buffer = Buffer(TimeSpan.FromSeconds(10), maxBytes: DashcamBuffer.ChunkSize);

        Assert.True(buffer.Add(Packet(1, key: true), 0));
        Assert.False(buffer.Add(Packet(2, key: true, length: DashcamBuffer.ChunkSize + 1), 40));
        Assert.False(buffer.Add(Packet(3, key: false), 80));

        Assert.Equal(0, buffer.Count);
        Assert.True(buffer.Add(Packet(4, key: true), 120));
    }

    [Fact]
    public void ClearDropsThePacketsAndReleaseTheMemory()
    {
        DashcamBuffer buffer = Buffer(TimeSpan.FromSeconds(30));
        long time = Feed(buffer, 20, length: 20_000);
        Assert.True(buffer.MemoryBytes > 0);

        buffer.Clear();
        Assert.Equal(0, buffer.Count);
        Assert.Equal(0, buffer.DataBytes);
        Assert.Equal(TimeSpan.Zero, buffer.Duration);
        Assert.InRange(buffer.MemoryBytes, 1, DashcamBuffer.MaxFreeChunks * DashcamBuffer.ChunkSize);
        // A new connection starts at its first key frame.
        Assert.False(buffer.Add(Packet(1, key: false), time));

        Feed(buffer, 5, start: time);
        buffer.Release();
        Assert.Equal(0, buffer.Count);
        Assert.Equal(0, buffer.MemoryBytes);

        // Usable again afterwards.
        Assert.True(buffer.Add(Packet(1, key: true), time + 10_000));
        Assert.Equal(1, buffer.Count);
    }
}
