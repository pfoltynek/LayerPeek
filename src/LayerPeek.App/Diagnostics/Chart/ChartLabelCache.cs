namespace LayerPeek.App.Diagnostics.Chart;

/// <summary>
/// Keeps chart labels (formatted text) per key, e.g. per axis value or tick time, so that a redraw creates only labels
/// that were not shown before. Bounded: when <paramref name="capacity"/> is exceeded, everything is dropped and labels are
/// created again on demand (ticks move slowly, so that happens rarely). UI thread only.
/// </summary>
/// <param name="create">Creates the label of a key; called once per key until <see cref="Clear"/>.</param>
internal sealed class ChartLabelCache<TKey, TLabel>(Func<TKey, TLabel> create, int capacity)
    where TKey : notnull
{
    private readonly Dictionary<TKey, TLabel> _labels = new(capacity);

    public int Count => _labels.Count;

    public TLabel Get(TKey key)
    {
        if (_labels.TryGetValue(key, out TLabel? label))
        {
            return label;
        }

        if (_labels.Count >= capacity)
        {
            _labels.Clear();
        }

        label = create(key);
        _labels.Add(key, label);
        return label;
    }

    /// <summary>Drops every label, e.g. after the brush or the culture changed.</summary>
    public void Clear() => _labels.Clear();
}
