using PrusaTrayDiagnostic.App.Diagnostics.Chart;

namespace PrusaTrayDiagnostic.App.Tests.Diagnostics;

public sealed class ChartLabelCacheTests
{
    private readonly List<long> _created = [];
    private readonly ChartLabelCache<long, string> _cache;

    public ChartLabelCacheTests() => _cache = new ChartLabelCache<long, string>(Create, capacity: 4);

    [Fact]
    public void LabelIsCreatedOncePerKey()
    {
        string first = _cache.Get(10);
        string again = _cache.Get(10);
        _cache.Get(20);

        Assert.Same(first, again);
        Assert.Equal([10, 20], _created);
    }

    [Fact]
    public void ClearRecreatesLabels()
    {
        _cache.Get(10);

        _cache.Clear();
        _cache.Get(10);

        Assert.Equal([10, 10], _created);
    }

    [Fact]
    public void CacheNeverGrowsBeyondItsCapacity()
    {
        for (long key = 0; key < 100; key++)
        {
            _cache.Get(key);
            Assert.InRange(_cache.Count, 1, 4);
        }

        Assert.Equal(100, _created.Count);
    }

    private string Create(long key)
    {
        _created.Add(key);
        return "label " + key;
    }
}
