namespace PrusaTrayDiagnostic.Core.History;

/// <summary>
/// Fixed-capacity ring of samples ordered by strictly increasing timestamp. Not thread-safe; the owner serializes access.
/// The backing array grows by doubling up to the capacity, so a printer that is monitored briefly does not hold
/// the memory of a full day. Once the capacity is reached, adding is O(1) and allocation-free.
/// </summary>
internal sealed class TelemetryRing
{
    private const int InitialLength = 256;

    private TelemetrySample[] _items = [];

    // Physical index of the oldest sample.
    private int _head;

    public TelemetryRing(int capacity, long retentionTicks, long gapTicks)
    {
        Capacity = capacity;
        RetentionTicks = retentionTicks;
        GapTicks = gapTicks;
    }

    public int Capacity { get; private set; }

    public int Count { get; private set; }

    public long RetentionTicks { get; }

    public long GapTicks { get; set; }

    /// <summary>
    /// Appends <paramref name="sample"/> as the newest sample. When the clock went backwards, samples at or after its
    /// timestamp are dropped first, so the order stays strictly increasing. Then samples older than the retention
    /// (measured from the new sample) are dropped, and the oldest one if the ring is full.
    /// </summary>
    public void Add(TelemetrySample sample)
    {
        long ticks = sample.TimestampUtcTicks;
        while (Count > 0 && ItemAt(Count - 1).TimestampUtcTicks >= ticks)
        {
            Count--;
        }

        long oldestAllowed = ticks - RetentionTicks;
        while (Count > 0 && ItemAt(0).TimestampUtcTicks < oldestAllowed)
        {
            DropOldest();
        }

        // After the retention cut the newest sample is either still there or everything is gone.
        bool startsSegment = Count == 0 || ticks - ItemAt(Count - 1).TimestampUtcTicks > GapTicks;

        if (Count == _items.Length)
        {
            if (_items.Length < Capacity)
            {
                Reallocate(Math.Min(Capacity, Math.Max(InitialLength, _items.Length * 2)));
            }
            else
            {
                DropOldest();
            }
        }

        _items[PhysicalIndex(Count)] = sample.WithStartsSegment(startsSegment);
        Count++;
    }

    /// <summary>Changes the capacity, keeping the newest samples. Returns whether the capacity changed.</summary>
    public bool Resize(int capacity)
    {
        if (capacity == Capacity)
        {
            return false;
        }

        int dropped = Math.Max(0, Count - capacity);
        for (int i = 0; i < dropped; i++)
        {
            DropOldest();
        }

        Capacity = capacity;
        if (_items.Length > capacity)
        {
            Reallocate(capacity);
        }

        return true;
    }

    /// <summary>Logical index of the first sample at or after <paramref name="ticks"/>; <see cref="Count"/> when there is none.</summary>
    public int LowerBound(long ticks)
    {
        int low = 0;
        int high = Count;
        while (low < high)
        {
            int middle = low + ((high - low) >> 1);
            if (ItemAt(middle).TimestampUtcTicks < ticks)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// Logical index of the first sample after <paramref name="ticks"/>; <see cref="Count"/> when there is none.
    /// <paramref name="ticks"/> are UTC ticks of a <see cref="DateTimeOffset"/> (at most <see cref="DateTime.MaxValue"/>), so + 1 cannot overflow.
    /// </summary>
    public int UpperBound(long ticks) => LowerBound(ticks + 1);

    /// <summary>Copies <paramref name="count"/> samples starting at logical index <paramref name="start"/>.</summary>
    public void CopyTo(int start, int count, Span<TelemetrySample> destination)
    {
        int physical = PhysicalIndex(start);
        int firstPart = Math.Min(count, _items.Length - physical);
        _items.AsSpan(physical, firstPart).CopyTo(destination);
        _items.AsSpan(0, count - firstPart).CopyTo(destination[firstPart..]);
    }

    private ref readonly TelemetrySample ItemAt(int index) => ref _items[PhysicalIndex(index)];

    private int PhysicalIndex(int index)
    {
        int physical = _head + index;
        return physical >= _items.Length ? physical - _items.Length : physical;
    }

    private void DropOldest()
    {
        _head = PhysicalIndex(1);
        Count--;
    }

    /// <summary>Moves the samples into a new array of <paramref name="length"/>, oldest first.</summary>
    private void Reallocate(int length)
    {
        TelemetrySample[] items = new TelemetrySample[length];
        CopyTo(0, Count, items);
        _items = items;
        _head = 0;
    }
}
