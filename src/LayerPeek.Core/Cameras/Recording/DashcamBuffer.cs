namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// The last minutes of one camera connection in memory (T-148): copies of the encoded packets, whole groups of pictures
/// (GOPs, each from a key frame up to the next), so that the buffer always starts with a key frame and its content can be
/// written into a playable file. Bounded three ways: the time between its first and last packet never exceeds
/// <c>maxDuration</c> (whole GOPs drop off the front), the chunks holding its packets never exceed <c>maxBytes</c> (plus at
/// most <see cref="MaxFreeChunks"/> free ones kept for reuse) and the number of packets never <c>maxPackets</c>; a camera
/// that sends no key frame for that long only loses the GOP that grew too big (the buffer starts over at the next key
/// frame) and never holds more.
/// </summary>
/// <remarks>
/// <para>
/// Packets are copied into chunks of <see cref="ChunkSize"/> bytes, back to back; a chunk freed by a dropped GOP is reused
/// for the next packets, so once the buffer is full, adding a packet allocates nothing (a packet bigger than a chunk gets a
/// bigger one, which is reused the same way). The packet list is a ring that grows by doubling up to <c>maxPackets</c>.
/// <see cref="Release"/> gives every byte back to the garbage collector.
/// </para>
/// <para>
/// Used by one thread at a time (the dashcam's reader); <see cref="Duration"/>, <see cref="DataBytes"/> and
/// <see cref="MemoryBytes"/> may be read from any thread. Time is the arrival time of the packets
/// (<see cref="TimeProvider.GetTimestamp"/> ticks), which a fake time provider controls in tests.
/// </para>
/// </remarks>
internal sealed class DashcamBuffer
{
    /// <summary>Size of the memory chunks the packets are copied into.</summary>
    internal const int ChunkSize = 256 * 1024;

    /// <summary>Free chunks kept for reuse; the steady state needs one or two (a GOP of the Buddy3D is ~300 kB).</summary>
    internal const int MaxFreeChunks = 4;

    private const int InitialPackets = 256;

    private readonly long _maxDuration;
    private readonly long _maxBytes;
    private readonly int _maxPackets;
    private readonly long _frequency;

    // The packets, oldest first: a ring of _count entries from _head.
    private Entry[] _entries = [];
    private int _head;
    private int _count;
    private int _keyFrames;
    private bool _waitingForKeyFrame = true;

    // The chunks in use, oldest first: a ring of _chunkCount from _chunkHead; the last one is filled from _tailUsed on.
    private byte[]?[] _chunks = [];
    private int _chunkHead;
    private int _chunkCount;
    private long _firstChunk;
    private int _tailUsed;
    private readonly byte[]?[] _free = new byte[MaxFreeChunks][];
    private int _freeCount;
    private long _heldBytes;
    private long _freeBytes;

    // Published for other threads.
    private long _dataBytes;
    private long _memoryBytes;
    private long _durationTicks;

    /// <param name="maxDuration">Longest time between the first and the last packet.</param>
    /// <param name="maxBytes">Most memory held by packet data (chunks, including the unused end of the last one).</param>
    /// <param name="maxPackets">Most packets held.</param>
    /// <param name="timestampFrequency">Ticks per second of the timestamps passed to <see cref="Add"/>.</param>
    public DashcamBuffer(TimeSpan maxDuration, long maxBytes, int maxPackets, long timestampFrequency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, ChunkSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPackets, 2);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timestampFrequency, 0);
        _frequency = timestampFrequency;
        _maxDuration = ToTimestamp(maxDuration, timestampFrequency);
        _maxBytes = maxBytes;
        _maxPackets = maxPackets;
    }

    /// <summary>Packets held; the first one is a key frame.</summary>
    public int Count => _count;

    /// <summary>Time between the arrival of the first and the last packet held.</summary>
    public TimeSpan Duration => TimeSpan.FromTicks(Volatile.Read(ref _durationTicks));

    /// <summary>Bytes of the packets held.</summary>
    public long DataBytes => Volatile.Read(ref _dataBytes);

    /// <summary>Bytes of memory held for them (the chunks in use and the free ones kept for reuse).</summary>
    public long MemoryBytes => Volatile.Read(ref _memoryBytes);

    /// <summary>The <paramref name="index"/>-th packet held, oldest first; its data stays valid until the next <see cref="Add"/>.</summary>
    public CameraPacket this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
            return _entries[(_head + index) % _entries.Length].Packet;
        }
    }

    /// <summary>
    /// Copies <paramref name="packet"/> (borrowed) into the buffer; packets before the first key frame (and after the buffer
    /// started over) are dropped. Then drops whole GOPs off the front until every bound holds.
    /// </summary>
    /// <param name="timestamp">Arrival time in ticks of the timestamp frequency.</param>
    /// <returns><see langword="false"/> when the packet was dropped.</returns>
    public bool Add(in CameraPacket packet, long timestamp)
    {
        bool key = packet.IsKeyFrame;
        if (key)
        {
            _waitingForKeyFrame = false;
        }
        else if (_waitingForKeyFrame)
        {
            return false;
        }

        int length = packet.Data.Length;
        if (!MakeRoom(length, key))
        {
            return false;
        }

        byte[] chunk = TailChunkFor(length);
        int offset = _tailUsed;
        packet.Data.Span.CopyTo(chunk.AsSpan(offset));
        _tailUsed += length;

        if (_count == _entries.Length)
        {
            Grow();
        }

        _entries[(_head + _count) % _entries.Length] = new Entry(
            packet with { Data = new ReadOnlyMemory<byte>(chunk, offset, length) },
            timestamp,
            _firstChunk + _chunkCount - 1);
        _count++;
        if (key)
        {
            _keyFrames++;
        }

        _dataBytes += length;

        // At most the set length: whole GOPs drop off the front. A single GOP longer than that (no key frame for minutes)
        // cannot be shortened and still start with a key frame: the buffer starts over at the next one.
        while (_count > 0 && timestamp - OldestTimestamp > _maxDuration)
        {
            if (_keyFrames > 1)
            {
                DropOldestGop();
            }
            else
            {
                Clear();
                _waitingForKeyFrame = true;
                break;
            }
        }

        Publish();
        return _count > 0;
    }

    /// <summary>
    /// Drops every packet (a new connection: its packets do not continue these); up to <see cref="MaxFreeChunks"/> chunks are
    /// kept for reuse.
    /// </summary>
    public void Clear()
    {
        while (_chunkCount > 0)
        {
            FreeOldestChunk();
        }

        Array.Clear(_entries);
        _head = 0;
        _count = 0;
        _keyFrames = 0;
        _tailUsed = 0;
        _dataBytes = 0;
        _waitingForKeyFrame = true;
        Publish();
    }

    /// <summary>Drops every packet and gives all memory back (idle: nothing is held).</summary>
    public void Release()
    {
        Clear();
        Array.Clear(_free);
        _freeCount = 0;
        _freeBytes = 0;
        _chunks = [];
        _chunkHead = 0;
        _entries = [];
        Publish();
    }

    private long OldestTimestamp => _entries[_head].Timestamp;

    /// <summary>
    /// Drops GOPs until <paramref name="length"/> more bytes and one more packet fit. Only the current GOP (the one the packet
    /// continues) is never dropped; when it alone is too big, the buffer starts over (with this packet when it is a key frame).
    /// </summary>
    private bool MakeRoom(int length, bool key)
    {
        while (true)
        {
            bool fitsBytes = FitsInTail(length) || _heldBytes + ChunkSizeFor(length) <= _maxBytes;
            if (fitsBytes && _count < _maxPackets)
            {
                return true;
            }

            // A key frame starts a GOP of its own: every GOP held may go. Otherwise the newest (current) one stays.
            if (_keyFrames > (key ? 0 : 1))
            {
                DropOldestGop();
                continue;
            }

            Clear();
            if (!key)
            {
                // The current GOP alone exceeds the bounds (a camera without key frames): drop it, wait for the next one.
                return false;
            }

            _waitingForKeyFrame = false;
            if (ChunkSizeFor(length) > _maxBytes)
            {
                // A single packet bigger than the whole buffer: never held.
                _waitingForKeyFrame = true;
                Publish();
                return false;
            }

            return true;
        }
    }

    private bool FitsInTail(int length) => _chunkCount > 0 && _chunks[(_chunkHead + _chunkCount - 1) % _chunks.Length]!.Length - _tailUsed >= length;

    /// <summary>A chunk for a packet of <paramref name="length"/> bytes: the standard size, or a bigger power of two (reusable).</summary>
    private static int NewChunkSize(int length) =>
        length <= ChunkSize ? ChunkSize : (int)Math.Min(Array.MaxLength, System.Numerics.BitOperations.RoundUpToPowerOf2((uint)length));

    /// <summary>The chunk the next <paramref name="length"/> bytes go into (a new tail when the current one is too full).</summary>
    private byte[] TailChunkFor(int length)
    {
        if (FitsInTail(length))
        {
            return _chunks[(_chunkHead + _chunkCount - 1) % _chunks.Length]!;
        }

        byte[] chunk = RentChunk(length);
        if (_chunkCount == _chunks.Length)
        {
            byte[]?[] grown = new byte[Math.Max(4, _chunks.Length * 2)][];
            for (int i = 0; i < _chunkCount; i++)
            {
                grown[i] = _chunks[(_chunkHead + i) % _chunks.Length];
            }

            _chunks = grown;
            _chunkHead = 0;
        }

        _chunks[(_chunkHead + _chunkCount) % _chunks.Length] = chunk;
        _chunkCount++;
        _heldBytes += chunk.Length;
        _tailUsed = 0;
        return chunk;
    }

    /// <summary>Size of the chunk <see cref="RentChunk"/> gives for <paramref name="length"/> bytes.</summary>
    private int ChunkSizeFor(int length) => FindFree(length) is >= 0 and var index ? _free[index]!.Length : NewChunkSize(length);

    /// <summary>
    /// The free chunk for <paramref name="length"/> bytes: one of the standard size for a small packet, the smallest big
    /// enough for a bigger one; -1 when none fits.
    /// </summary>
    private int FindFree(int length)
    {
        int best = -1;
        for (int i = 0; i < _freeCount; i++)
        {
            int size = _free[i]!.Length;
            if (length <= ChunkSize ? size == ChunkSize : size >= length && (best < 0 || size < _free[best]!.Length))
            {
                best = i;
                if (length <= ChunkSize)
                {
                    break;
                }
            }
        }

        return best;
    }

    /// <summary>A free chunk for <paramref name="length"/> bytes (<see cref="FindFree"/>), or a new one.</summary>
    private byte[] RentChunk(int length)
    {
        int index = FindFree(length);
        if (index < 0)
        {
            return GC.AllocateUninitializedArray<byte>(NewChunkSize(length));
        }

        byte[] chunk = _free[index]!;
        _free[index] = _free[--_freeCount];
        _free[_freeCount] = null;
        _freeBytes -= chunk.Length;
        return chunk;
    }

    private void Grow()
    {
        int size = (int)Math.Min(_maxPackets, Math.Max(InitialPackets, (long)_entries.Length * 2));
        Entry[] grown = new Entry[size];
        for (int i = 0; i < _count; i++)
        {
            grown[i] = _entries[(_head + i) % _entries.Length];
        }

        _entries = grown;
        _head = 0;
    }

    /// <summary>Drops the oldest GOP: the key frame at the front and everything up to the next key frame.</summary>
    private void DropOldestGop()
    {
        do
        {
            ref Entry entry = ref _entries[_head];
            _dataBytes -= entry.Packet.Data.Length;
            if (entry.Packet.IsKeyFrame)
            {
                _keyFrames--;
            }

            entry = default;
            _head = (_head + 1) % _entries.Length;
            _count--;
        }
        while (_count > 0 && !_entries[_head].Packet.IsKeyFrame);

        // Chunks before the one of the new first packet hold nothing any more (all of them when nothing is left).
        long firstInUse = _count > 0 ? _entries[_head].Chunk : _firstChunk + _chunkCount;
        while (_chunkCount > 0 && _firstChunk < firstInUse)
        {
            FreeOldestChunk();
        }
    }

    private void FreeOldestChunk()
    {
        byte[] chunk = _chunks[_chunkHead]!;
        _chunks[_chunkHead] = null;
        _chunkHead = (_chunkHead + 1) % _chunks.Length;
        _chunkCount--;
        _firstChunk++;
        _heldBytes -= chunk.Length;
        if (_chunkCount == 0)
        {
            _chunkHead = 0;
            _tailUsed = 0;
        }

        // More free chunks than are ever needed go back to the garbage collector.
        if (_freeCount < MaxFreeChunks)
        {
            _free[_freeCount++] = chunk;
            _freeBytes += chunk.Length;
        }
    }

    private void Publish()
    {
        Volatile.Write(ref _dataBytes, _dataBytes);
        Volatile.Write(ref _memoryBytes, _heldBytes + _freeBytes);
        long span = _count > 1 ? _entries[(_head + _count - 1) % _entries.Length].Timestamp - OldestTimestamp : 0;
        Volatile.Write(ref _durationTicks, ToTimeSpanTicks(span, _frequency));
    }

    private static long ToTimestamp(TimeSpan time, long frequency) => (long)(time.Ticks * (frequency / (double)TimeSpan.TicksPerSecond));

    private static long ToTimeSpanTicks(long timestamp, long frequency) => (long)(timestamp * (TimeSpan.TicksPerSecond / (double)frequency));

    /// <param name="Packet">The packet with its data in a chunk.</param>
    /// <param name="Timestamp">Arrival time.</param>
    /// <param name="Chunk">Sequence number of the chunk its data is in.</param>
    private readonly record struct Entry(CameraPacket Packet, long Timestamp, long Chunk);
}
