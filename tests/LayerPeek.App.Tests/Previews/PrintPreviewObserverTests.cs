using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Previews;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Previews;

/// <summary>
/// The app wiring of the preview cache (T-087 over T-086): every published snapshot goes to
/// <see cref="PrintPreviewCache.Observe"/> in order (a finished job's preview goes when the next job starts, and an older
/// job is never fetched again), saved settings drop removed or changed printers, and disposing stops listening.
/// </summary>
public sealed class PrintPreviewObserverTests : IDisposable
{
    private readonly FakePrinterMonitor _monitor = new();
    private readonly CountingFetcher _fetcher = new();
    private readonly PrintPreviewCache _cache;

    public PrintPreviewObserverTests()
    {
        _cache = new PrintPreviewCache(_fetcher, new FakeTimeProvider(Now), NullLogger<PrintPreviewCache>.Instance);
    }

    public void Dispose() => _cache.Dispose();

    private static PrintJob Job(long id, string path) =>
        new() { Id = id, FileName = path, PreviewReference = new TestPreviewReference(path) };

    [Fact]
    public async Task SnapshotsReachTheCacheInOrderSoAnOlderJobIsNeverFetched()
    {
        using PrintPreviewObserver observer = new(_monitor, _cache);
        PrinterDefinition printer = Definition();
        PrintJob first = Job(1, "a.bgcode");
        PrintJob second = Job(2, "b.bgcode");
        Assert.NotNull(await _cache.GetAsync(printer, first, PrintPreviewSize.Large, TestContext.Current.CancellationToken));

        _monitor.Raise(Snapshot(PrinterState.Printing, first));
        _monitor.Raise(Snapshot(PrinterState.Printing, second));

        // The next job released the old preview; a stale snapshot of the first job gets nothing and fetches nothing.
        Assert.Null(await _cache.GetAsync(printer, first, PrintPreviewSize.Large, TestContext.Current.CancellationToken));
        Assert.Equal(1, _fetcher.Count);
        Assert.NotNull(await _cache.GetAsync(printer, second, PrintPreviewSize.Large, TestContext.Current.CancellationToken));
        Assert.Equal(2, _fetcher.Count);
    }

    [Fact]
    public async Task SettingsOfANewRunDropRemovedPrinters()
    {
        using PrintPreviewObserver observer = new(_monitor, _cache);
        await _cache.GetAsync(Definition("mk4"), Job(1, "a.bgcode"), PrintPreviewSize.Large, TestContext.Current.CancellationToken);
        await _cache.GetAsync(Definition("xl"), Job(1, "b.bgcode"), PrintPreviewSize.Large, TestContext.Current.CancellationToken);
        Assert.Equal(2, _cache.Count);

        ((IMonitoringObserver)observer).ApplySettings(new AppSettings { Printers = [Definition("xl")] });

        Assert.Equal(1, _cache.Count);
    }

    [Fact]
    public async Task SavedSettingsThatKeepMonitoringRunningAlsoDropRemovedPrinters()
    {
        using PrintPreviewObserver observer = new(_monitor, _cache);
        await _cache.GetAsync(Definition("mk4"), Job(1, "a.bgcode"), PrintPreviewSize.Large, TestContext.Current.CancellationToken);

        ((IMonitoringObserver)observer).UpdateSettings(new AppSettings { Printers = [] });

        Assert.Equal(0, _cache.Count);
    }

    [Fact]
    public async Task ChangedAddressDropsThePrinter()
    {
        using PrintPreviewObserver observer = new(_monitor, _cache);
        await _cache.GetAsync(Definition(), Job(1, "a.bgcode"), PrintPreviewSize.Large, TestContext.Current.CancellationToken);

        ((IMonitoringObserver)observer).ApplySettings(new AppSettings { Printers = [Definition() with { Address = "192.168.1.99" }] });

        Assert.Equal(0, _cache.Count);
    }

    [Fact]
    public void MonitoringFailureKeepsThePreviews()
    {
        using PrintPreviewObserver observer = new(_monitor, _cache);

        ((IMonitoringObserver)observer).ReportMonitoringFailed();

        Assert.Equal(1, _monitor.SubscriberCount);
    }

    [Fact]
    public void DisposingStopsListening()
    {
        PrintPreviewObserver observer = new(_monitor, _cache);
        Assert.Equal(1, _monitor.SubscriberCount);

        observer.Dispose();
        observer.Dispose();

        Assert.Equal(0, _monitor.SubscriberCount);
    }

    private sealed class CountingFetcher : IPrintPreviewFetcher
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task<PrintPreviewFetchResult> FetchAsync(PrinterDefinition printer, PrintPreviewReference reference, PrintPreviewSize size, string? etag, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(PrintPreviewFetchResult.Downloaded(FakePreviewSource.Png(), etag: null));
        }
    }
}
