namespace LayerPeek.Core.Detection;

/// <summary>
/// The recent scores of one detector for the "K of N" confirmation (T-153), by time: a failure is confirmed when, of the
/// samples within the window, at least <see cref="MinSamples"/> exist and the given share reached the threshold. A ring of
/// fixed size, so adding a sample allocates nothing. Not thread-safe (one detector, one worker at a time).
/// </summary>
internal sealed class ConfirmationWindow
{
    private readonly long[] _times = new long[PrintFailureDetectionOptions.ConfirmationCapacity];
    private readonly float[] _scores = new float[PrintFailureDetectionOptions.ConfirmationCapacity];
    private readonly bool[] _above = new bool[PrintFailureDetectionOptions.ConfirmationCapacity];
    private readonly long _length;
    private readonly double _ratio;
    private int _next;
    private int _count;

    /// <param name="length">The window in timestamp ticks.</param>
    public ConfirmationWindow(long length, double ratio, int minSamples)
    {
        _length = length;
        _ratio = ratio;
        MinSamples = minSamples;
    }

    public int MinSamples { get; }

    public void Clear()
    {
        _next = 0;
        _count = 0;
    }

    public void Add(long timestamp, float score, bool above)
    {
        _times[_next] = timestamp;
        _scores[_next] = score;
        _above[_next] = above;
        _next = (_next + 1) % _times.Length;
        _count = Math.Min(_count + 1, _times.Length);
    }

    /// <summary>Whether the samples within the window ending at <paramref name="now"/> confirm a failure.</summary>
    public bool IsConfirmed(long now)
    {
        int samples = 0;
        int above = 0;
        for (int i = 0; i < _count; i++)
        {
            int index = (_next - 1 - i + _times.Length) % _times.Length;
            if (now - _times[index] > _length)
            {
                // Older ones follow: added in time order.
                break;
            }

            samples++;
            above += _above[index] ? 1 : 0;
        }

        return samples >= MinSamples && above >= Math.Ceiling(_ratio * samples);
    }

    /// <summary>The scores within the window ending at <paramref name="now"/>, oldest first (allocates; only for an alarm).</summary>
    public float[] RecentScores(long now)
    {
        List<float> scores = [];
        for (int i = 0; i < _count; i++)
        {
            int index = (_next - 1 - i + _times.Length) % _times.Length;
            if (now - _times[index] > _length)
            {
                break;
            }

            scores.Add(_scores[index]);
        }

        scores.Reverse();
        return [.. scores];
    }
}
