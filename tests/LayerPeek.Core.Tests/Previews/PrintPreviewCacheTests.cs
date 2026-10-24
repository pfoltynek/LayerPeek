using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Tests.Monitoring;

namespace LayerPeek.Core.Tests.Previews;

public sealed class PrintPreviewCacheTests : IDisposable
{
    private static readonly TestPreviewReference Benchy = new("/usb/BENCHY~1.BGC");
    private static readonly TestPreviewReference Cube = new("/usb/CUBE~1.BGC");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly FakePreviewFetcher _fetcher = new();
    private readonly ListLogger<PrintPreviewCache> _logger = new();
    private readonly PrintPreviewCache _cache;

    public PrintPreviewCacheTests() => _cache = new PrintPreviewCache(_fetcher, _time, _logger);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task PreviewIsFetchedOncePerJobAndSize()
    {
        PrintJob job = Job(42, Benchy);

        PrintPreview? first = await _cache.GetAsync(Printer(), job, PrintPreviewSize.Large, Ct);
        PrintPreview? second = await _cache.GetAsync(Printer(), job with { ProgressPercent = 50 }, PrintPreviewSize.Large, Ct);
        PrintPreview? small = await _cache.GetAsync(Printer(), job, PrintPreviewSize.Small, Ct);
        await _cache.GetAsync(Printer(), job, PrintPreviewSize.Small, Ct);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.NotNull(small);
        Assert.NotSame(first, small);
        Assert.Equal(2, _fetcher.Count);
        Assert.Equal([PrintPreviewSize.Large, PrintPreviewSize.Small], _fetcher.Calls.Select(static c => c.Size));
        Assert.All(_fetcher.Calls, static c => Assert.Null(c.ETag));
        Assert.All(_fetcher.Calls, static c => Assert.Same(Benchy, c.Reference));
    }

    [Fact]
    public async Task CacheHitCompletesSynchronously()
    {
        PrintJob job = Job(42, Benchy);
        await _cache.GetAsync(Printer(), job, PrintPreviewSize.Large, Ct);

        Task<PrintPreview?> hit = _cache.GetAsync(Printer(), job, PrintPreviewSize.Large, Ct);

        Assert.True(hit.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ConcurrentRequestsForTheSameJobShareOneFetch()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<PrintPreviewFetchResult> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _fetcher.Handler = _ =>
        {
            started.TrySetResult();
            return answer.Task;
        };
        PrintJob job = Job(42, Benchy);

        Task<PrintPreview?>[] requests = [.. Enumerable.Range(0, 8).Select(_ => Task.Run(() => _cache.GetAsync(Printer(), job, PrintPreviewSize.Large, Ct), Ct))];
        await started.Task.WaitAsync(Ct);
        PrintPreview preview = FakePreviewFetcher.NewPreview();
        answer.SetResult(PrintPreviewFetchResult.Downloaded(preview, null));
        PrintPreview?[] results = await Task.WhenAll(requests);

        Assert.Equal(1, _fetcher.Count);
        Assert.All(results, r => Assert.Same(preview, r));
    }

    [Fact]
    public async Task JobWithoutReferenceHasNoPreviewAndFetchesNothing()
    {
        PrintPreview? preview = await _cache.GetAsync(Printer(), new PrintJob { Id = 42, FileName = "benchy.bgcode" }, PrintPreviewSize.Large, Ct);

        Assert.Null(preview);
        Assert.Equal(0, _fetcher.Count);
        Assert.Equal(0, _cache.Count);
    }

    [Fact]
    public async Task PreviewStaysAfterThePrintFinishedOrStoppedUntilTheNextPrint()
    {
        PrintJob printing = Job(42, Benchy);
        PrintPreview? during = await _cache.GetAsync(Printer(), printing, PrintPreviewSize.Large, Ct);

        // The client keeps reporting the last job after the end (/api/v1/job answers 204 then), later none at all.
        PrintJob finished = printing with { ProgressPercent = 100, Remaining = TimeSpan.Zero };
        _cache.Observe(Snapshot(PrinterState.Finished, finished));
        PrintPreview? afterFinish = await _cache.GetAsync(Printer(), finished, PrintPreviewSize.Large, Ct);
        _cache.Observe(Snapshot(PrinterState.Stopped, printing with { Remaining = null }));
        _cache.Observe(Snapshot(PrinterState.Idle, null));
        _cache.Observe(PrinterSnapshot.Unavailable("core-one", PrinterState.Offline, _time.GetUtcNow()));
        PrintPreview? cooling = await _cache.GetAsync(Printer(), finished, PrintPreviewSize.Large, Ct);

        Assert.NotNull(during);
        Assert.Same(during, afterFinish);
        Assert.Same(during, cooling);
        Assert.Equal(1, _fetcher.Count);
        Assert.Equal(1, _cache.Count);
    }

    [Fact]
    public async Task NextPrintReleasesTheOldPreviewAndFetchesTheNewOne()
    {
        PrintPreview? old = await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        _cache.Observe(Snapshot(PrinterState.Printing, Job(43, Cube)));
        int countAfterNextJob = _cache.Count;
        PrintPreview? next = await _cache.GetAsync(Printer(), Job(43, Cube), PrintPreviewSize.Large, Ct);

        Assert.NotNull(old);
        Assert.Equal(0, countAfterNextJob);
        Assert.NotNull(next);
        Assert.NotSame(old, next);
        Assert.Equal(2, _fetcher.Count);
        Assert.Same(Cube, _fetcher.Calls.Last().Reference);
        Assert.Null(_fetcher.Calls.Last().ETag);
    }

    [Fact]
    public async Task RequestForTheNextJobReplacesTheOldOneWithoutObserve()
    {
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        await _cache.GetAsync(Printer(), Job(43, Cube), PrintPreviewSize.Large, Ct);

        // The old job is gone: asking for it again downloads it again.
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Equal(3, _fetcher.Count);
        Assert.Equal(1, _cache.Count);
    }

    [Fact]
    public async Task SnapshotOfTheSameJobWithoutReferenceKeepsThePreview()
    {
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        // E.g. a restarted client whose first /api/v1/job request failed.
        _cache.Observe(Snapshot(PrinterState.Printing, new PrintJob { Id = 42 }));
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Equal(1, _fetcher.Count);
    }

    [Fact]
    public async Task ReprintOfTheSameFileRevalidatesWithTheETag()
    {
        PrintPreview? first = await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        _cache.Observe(Snapshot(PrinterState.Printing, Job(43, Benchy)));
        _fetcher.Handler = static call => Task.FromResult(PrintPreviewFetchResult.NotModified(call.ETag));

        PrintPreview? second = await _cache.GetAsync(Printer(), Job(43, Benchy), PrintPreviewSize.Large, Ct);
        PrintPreview? again = await _cache.GetAsync(Printer(), Job(43, Benchy), PrintPreviewSize.Large, Ct);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Same(first, again);
        Assert.Equal(2, _fetcher.Count);
        Assert.Equal("\"1\"", _fetcher.Calls.Last().ETag);
    }

    [Fact]
    public async Task ReprintOfAChangedFileDownloadsTheNewPreview()
    {
        PrintPreview? first = await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        PrintPreview replaced = FakePreviewFetcher.NewPreview(32, 24);
        _fetcher.Handler = _ => Task.FromResult(PrintPreviewFetchResult.Downloaded(replaced, "\"2\""));

        PrintPreview? second = await _cache.GetAsync(Printer(), Job(43, Benchy), PrintPreviewSize.Large, Ct);

        Assert.NotSame(first, second);
        Assert.Same(replaced, second);
        Assert.Equal("\"1\"", _fetcher.Calls.Last().ETag);
    }

    [Fact]
    public async Task NotModifiedWithoutAnEarlierPreviewIsATransientFailure()
    {
        _fetcher.Handler = static _ => Task.FromResult(PrintPreviewFetchResult.NotModified("\"1\""));

        PrintPreview? preview = await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        _time.Advance(PrintPreviewCacheOptions.Default.RetryDelay);
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Null(preview);
        Assert.Equal(2, _fetcher.Count);
    }

    [Fact]
    public async Task MissingPreviewIsRememberedForTheJob()
    {
        _fetcher.Handler = static _ => Task.FromResult(PrintPreviewFetchResult.NotAvailable);

        PrintPreview? first = await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        _time.Advance(TimeSpan.FromHours(1));
        PrintPreview? second = await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, _fetcher.Count);
    }

    [Fact]
    public async Task TransientFailureIsRetriedOnlyAfterTheDelay()
    {
        _fetcher.Handler = static _ => Task.FromResult(PrintPreviewFetchResult.Failed);
        PrintJob job = Job(42, Benchy);

        PrintPreview? failed = await _cache.GetAsync(Printer(), job, PrintPreviewSize.Large, Ct);
        await _cache.GetAsync(Printer(), job, PrintPreviewSize.Large, Ct);
        _time.Advance(PrintPreviewCacheOptions.Default.RetryDelay - TimeSpan.FromSeconds(1));
        await _cache.GetAsync(Printer(), job, PrintPreviewSize.Large, Ct);
        int beforeDelay = _fetcher.Count;
        _fetcher.Handler = static _ => Task.FromResult(PrintPreviewFetchResult.Downloaded(FakePreviewFetcher.NewPreview(), null));
        _time.Advance(TimeSpan.FromSeconds(1));
        PrintPreview? retried = await _cache.GetAsync(Printer(), job, PrintPreviewSize.Large, Ct);

        Assert.Null(failed);
        Assert.Equal(1, beforeDelay);
        Assert.NotNull(retried);
        Assert.Equal(2, _fetcher.Count);
    }

    [Fact]
    public async Task FetcherExceptionDegradesToNoPreview()
    {
        _fetcher.Handler = static _ => throw new InvalidOperationException("broken fetcher");

        PrintPreview? preview = await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Null(preview);
        (LogLevel level, Exception? exception, string message) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("core-one", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FaultedFetcherTaskDegradesToNoPreview()
    {
        _fetcher.Handler = static _ => Task.FromException<PrintPreviewFetchResult>(new HttpRequestException("down"));

        Assert.Null(await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct));
    }

    [Fact]
    public async Task CancellingOneCallerDoesNotCancelTheSharedFetch()
    {
        TaskCompletionSource<PrintPreviewFetchResult> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<FetchCall> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _fetcher.Handler = call =>
        {
            started.TrySetResult(call);
            return answer.Task;
        };
        using CancellationTokenSource callerGivesUp = new();

        Task<PrintPreview?> cancelled = _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, callerGivesUp.Token);
        Task<PrintPreview?> waiting = _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        FetchCall call = await started.Task.WaitAsync(Ct);
        await callerGivesUp.CancelAsync();
        PrintPreview preview = FakePreviewFetcher.NewPreview();
        answer.SetResult(PrintPreviewFetchResult.Downloaded(preview, null));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Same(preview, await waiting);
        Assert.False(call.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CancelledTokenReturnsACancelledTaskWithoutFetching()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        Task<PrintPreview?> request = _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, cancelled.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal(0, _fetcher.Count);
    }

    [Fact]
    public async Task RemovingThePrinterReleasesItsPreviewAndCancelsTheFetch()
    {
        TaskCompletionSource<FetchCall> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _fetcher.Handler = async call =>
        {
            started.TrySetResult(call);
            await Task.Delay(Timeout.InfiniteTimeSpan, call.CancellationToken);
            return PrintPreviewFetchResult.Failed;
        };

        Task<PrintPreview?> pending = _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        FetchCall call = await started.Task.WaitAsync(Ct);
        _cache.Remove("CORE-ONE");

        Assert.Null(await pending.WaitAsync(Ct));
        Assert.True(call.CancellationToken.IsCancellationRequested);
        Assert.Equal(0, _cache.Count);
    }

    [Fact]
    public async Task RemovedPrinterIsFetchedAgainWhenAskedLater()
    {
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        _cache.Remove("core-one");
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Equal(2, _fetcher.Count);
    }

    [Fact]
    public async Task RetainOnlyDropsRemovedPrintersAndChangedAddresses()
    {
        PrinterDefinition kept = Printer("kept", "192.168.1.20");
        PrinterDefinition moved = Printer("moved", "192.168.1.21");
        PrinterDefinition removed = Printer("removed", "192.168.1.22");
        foreach (PrinterDefinition printer in new[] { kept, moved, removed })
        {
            await _cache.GetAsync(printer, Job(42, Benchy), PrintPreviewSize.Large, Ct);
        }

        _cache.RetainOnly([kept with { Name = "Renamed" }, moved with { Address = "192.168.1.99" }]);
        int count = _cache.Count;
        await _cache.GetAsync(kept, Job(42, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Equal(1, count);
        Assert.Equal(3, _fetcher.Count);
    }

    [Fact]
    public async Task ChangedAddressFetchesAgainWithoutTheOldValidator()
    {
        await _cache.GetAsync(Printer(address: "192.168.1.20"), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        await _cache.GetAsync(Printer(address: "192.168.1.30"), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Equal(2, _fetcher.Count);
        Assert.Null(_fetcher.Calls.Last().ETag);
        Assert.Equal("192.168.1.30", _fetcher.Calls.Last().Printer.Address);
        Assert.Equal(1, _cache.Count);
    }

    [Fact]
    public async Task CacheKeepsAtMostMaxPrintersAndDropsTheLeastRecentlyUsed()
    {
        using PrintPreviewCache cache = new(_fetcher, _time, NullLogger<PrintPreviewCache>.Instance, new PrintPreviewCacheOptions { MaxPrinters = 2 });
        await cache.GetAsync(Printer("a"), Job(1, Benchy), PrintPreviewSize.Large, Ct);
        await cache.GetAsync(Printer("b"), Job(1, Benchy), PrintPreviewSize.Large, Ct);
        await cache.GetAsync(Printer("a"), Job(1, Benchy), PrintPreviewSize.Large, Ct);

        await cache.GetAsync(Printer("c"), Job(1, Benchy), PrintPreviewSize.Large, Ct);
        int count = cache.Count;
        await cache.GetAsync(Printer("a"), Job(1, Benchy), PrintPreviewSize.Large, Ct);
        int fetchesBeforeB = _fetcher.Count;
        await cache.GetAsync(Printer("b"), Job(1, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Equal(2, count);
        Assert.Equal(3, fetchesBeforeB);
        Assert.Equal(4, _fetcher.Count);
    }

    [Fact]
    public async Task NothingIsPersistedANewCacheStartsEmpty()
    {
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        // As after an app restart: the only state was in memory.
        using PrintPreviewCache restarted = new(_fetcher, _time, NullLogger<PrintPreviewCache>.Instance);
        int countAfterRestart = restarted.Count;
        await restarted.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Equal(0, countAfterRestart);
        Assert.Equal(2, _fetcher.Count);
        Assert.Null(_fetcher.Calls.Last().ETag);
    }

    [Fact]
    public async Task ClearDropsEverything()
    {
        await _cache.GetAsync(Printer("a"), Job(1, Benchy), PrintPreviewSize.Large, Ct);
        await _cache.GetAsync(Printer("b"), Job(1, Benchy), PrintPreviewSize.Large, Ct);

        _cache.Clear();

        Assert.Equal(0, _cache.Count);
    }

    [Fact]
    public async Task DisposeCancelsFetchesAndLaterRequestsHaveNoPreview()
    {
        TaskCompletionSource<FetchCall> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _fetcher.Handler = async call =>
        {
            started.TrySetResult(call);
            await Task.Delay(Timeout.InfiniteTimeSpan, call.CancellationToken);
            return PrintPreviewFetchResult.Failed;
        };
        Task<PrintPreview?> pending = _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        FetchCall call = await started.Task.WaitAsync(Ct);

        _cache.Dispose();

        Assert.Null(await pending.WaitAsync(Ct));
        Assert.True(call.CancellationToken.IsCancellationRequested);
        Assert.Null(await _cache.GetAsync(Printer(), Job(43, Cube), PrintPreviewSize.Large, Ct));
        Assert.Equal(1, _fetcher.Count);
    }

    [Fact]
    public async Task FetcherThatBlocksDoesNotBlockTheCaller()
    {
        using ManualResetEventSlim release = new();
        _fetcher.Handler = _ =>
        {
            // A fetcher that blocks synchronously before returning its task.
            release.Wait(TimeSpan.FromSeconds(30));
            return Task.FromResult(PrintPreviewFetchResult.NotAvailable);
        };

        Task<PrintPreview?> request = _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        bool returnedWhileBlocked = !request.IsCompleted;
        release.Set();

        Assert.True(returnedWhileBlocked);
        Assert.Null(await request.WaitAsync(Ct));
    }

    [Fact]
    public async Task NextJobCancelsTheRunningFetchOfTheOldOneAndDiscardsItsLateResult()
    {
        TaskCompletionSource<FetchCall> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<PrintPreviewFetchResult> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _fetcher.Handler = call =>
        {
            started.TrySetResult(call);
            // A fetcher that ignores cancellation and answers anyway.
            return answer.Task;
        };
        Task<PrintPreview?> old = _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        FetchCall call = await started.Task.WaitAsync(Ct);

        _cache.Observe(Snapshot(PrinterState.Printing, Job(43, Cube)));
        bool cancelled = call.CancellationToken.IsCancellationRequested;
        answer.SetResult(PrintPreviewFetchResult.Downloaded(FakePreviewFetcher.NewPreview(), "\"1\""));
        PrintPreview? late = await old.WaitAsync(Ct);
        _fetcher.Handler = static _ => Task.FromResult(PrintPreviewFetchResult.Downloaded(FakePreviewFetcher.NewPreview(), "\"2\""));
        PrintPreview? next = await _cache.GetAsync(Printer(), Job(43, Cube), PrintPreviewSize.Large, Ct);

        Assert.True(cancelled);
        Assert.Null(late);
        Assert.NotNull(next);
        Assert.Equal(2, _fetcher.Count);
        Assert.Same(Cube, _fetcher.Calls.Last().Reference);
    }

    [Fact]
    public async Task NextJobWithoutReferenceReleasesThePreview()
    {
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        _cache.Observe(Snapshot(PrinterState.Printing, new PrintJob { Id = 43 }));

        Assert.Equal(0, _cache.Count);
    }

    [Fact]
    public async Task SnapshotsOfOtherPrintersDoNotTouchTheEntry()
    {
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        _cache.Observe(Snapshot(PrinterState.Printing, Job(43, Cube)) with { PrinterId = "other" });
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Equal(1, _cache.Count);
        Assert.Equal(1, _fetcher.Count);
    }

    [Fact]
    public async Task ValidatorSurvivesATransientFailureOfTheReprint()
    {
        PrintPreview? first = await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        _cache.Observe(Snapshot(PrinterState.Printing, Job(43, Benchy)));
        _fetcher.Handler = static _ => Task.FromResult(PrintPreviewFetchResult.Failed);
        PrintPreview? failed = await _cache.GetAsync(Printer(), Job(43, Benchy), PrintPreviewSize.Large, Ct);

        _time.Advance(PrintPreviewCacheOptions.Default.RetryDelay);
        _fetcher.Handler = static call => Task.FromResult(PrintPreviewFetchResult.NotModified(call.ETag));
        PrintPreview? retried = await _cache.GetAsync(Printer(), Job(43, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Null(failed);
        Assert.Same(first, retried);
        Assert.Equal(["\"1\"", "\"1\""], _fetcher.Calls.Skip(1).Select(static c => c.ETag));
    }

    [Fact]
    public async Task ValidatorIsCarriedOverSeveralReprintsWithoutARequest()
    {
        PrintPreview? first = await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        // Job 43 prints the same file but nobody asks for its preview; job 44 again the same file.
        _cache.Observe(Snapshot(PrinterState.Printing, Job(43, Benchy)));
        _cache.Observe(Snapshot(PrinterState.Printing, Job(44, Benchy)));
        _fetcher.Handler = static call => Task.FromResult(PrintPreviewFetchResult.NotModified(call.ETag));
        PrintPreview? third = await _cache.GetAsync(Printer(), Job(44, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Same(first, third);
        Assert.Equal("\"1\"", _fetcher.Calls.Last().ETag);
    }

    [Fact]
    public async Task DownloadWithoutETagLeavesNoValidator()
    {
        _fetcher.Handler = static _ => Task.FromResult(PrintPreviewFetchResult.Downloaded(FakePreviewFetcher.NewPreview(), null));
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        await _cache.GetAsync(Printer(), Job(43, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Null(_fetcher.Calls.Last().ETag);
    }

    [Fact]
    public async Task ChangedConnectionTypeDropsThePrinter()
    {
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        PrinterDefinition viaConnect = Printer() with { ConnectionType = ConnectionType.PrusaConnect };

        _cache.RetainOnly([viaConnect]);
        int afterRetain = _cache.Count;
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        await _cache.GetAsync(viaConnect, Job(42, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Equal(0, afterRetain);
        Assert.Equal(3, _fetcher.Count);
        Assert.Null(_fetcher.Calls.Last().ETag);
    }

    [Fact]
    public async Task RetainOnlyMatchesPrinterIdsIgnoringCase()
    {
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);

        _cache.RetainOnly([Printer(id: "CORE-ONE")]);

        Assert.Equal(1, _cache.Count);
    }

    [Fact]
    public async Task CancellationSourceIsDisposedWhenTheReleasedFetchEnds()
    {
        TaskCompletionSource<FetchCall> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _fetcher.Handler = async call =>
        {
            started.TrySetResult(call);
            await Task.Delay(Timeout.InfiniteTimeSpan, call.CancellationToken);
            return PrintPreviewFetchResult.Failed;
        };
        Task<PrintPreview?> pending = _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        FetchCall call = await started.Task.WaitAsync(Ct);

        _cache.Remove("core-one");
        await pending.WaitAsync(Ct);

        // CancellationToken.WaitHandle throws once the source is disposed.
        Assert.Throws<ObjectDisposedException>(() => call.CancellationToken.WaitHandle);
    }

    [Fact]
    public async Task CancellationSourceIsDisposedWhenAnIdleEntryIsReleased()
    {
        await _cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct);
        FetchCall call = _fetcher.Calls.Single();
        _ = call.CancellationToken.WaitHandle;

        _cache.Remove("core-one");

        Assert.Throws<ObjectDisposedException>(() => call.CancellationToken.WaitHandle);
    }

    [Fact]
    public async Task EvictedPrinterHasItsFetchCancelled()
    {
        using PrintPreviewCache cache = new(_fetcher, _time, NullLogger<PrintPreviewCache>.Instance, new PrintPreviewCacheOptions { MaxPrinters = 1 });
        TaskCompletionSource<FetchCall> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _fetcher.Handler = async call =>
        {
            started.TrySetResult(call);
            await Task.Delay(Timeout.InfiniteTimeSpan, call.CancellationToken);
            return PrintPreviewFetchResult.Failed;
        };
        Task<PrintPreview?> pending = cache.GetAsync(Printer("a"), Job(1, Benchy), PrintPreviewSize.Large, Ct);
        FetchCall call = await started.Task.WaitAsync(Ct);

        _fetcher.Handler = static _ => Task.FromResult(PrintPreviewFetchResult.NotAvailable);
        await cache.GetAsync(Printer("b"), Job(1, Benchy), PrintPreviewSize.Large, Ct);

        Assert.Null(await pending.WaitAsync(Ct));
        Assert.True(call.CancellationToken.IsCancellationRequested);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task ThrowingLoggerStillCompletesTheRequest()
    {
        using PrintPreviewCache cache = new(_fetcher, _time, new ThrowingLogger());
        _fetcher.Handler = static _ => throw new InvalidOperationException("broken fetcher");

        PrintPreview? preview = await cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct).WaitAsync(TimeSpan.FromSeconds(30), Ct);
        _time.Advance(PrintPreviewCacheOptions.Default.RetryDelay);
        _fetcher.Handler = static _ => Task.FromResult(PrintPreviewFetchResult.Downloaded(FakePreviewFetcher.NewPreview(), null));
        PrintPreview? retried = await cache.GetAsync(Printer(), Job(42, Benchy), PrintPreviewSize.Large, Ct).WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Null(preview);
        Assert.NotNull(retried);
    }

    [Fact]
    public async Task ConcurrentUseNeitherThrowsNorLeavesRequestsHanging()
    {
        using PrintPreviewCache cache = new(_fetcher, _time, NullLogger<PrintPreviewCache>.Instance, new PrintPreviewCacheOptions { MaxPrinters = 3 });
        _fetcher.Handler = static async call =>
        {
            await Task.Yield();
            call.CancellationToken.ThrowIfCancellationRequested();
            return call.Size == PrintPreviewSize.Small
                ? PrintPreviewFetchResult.NotAvailable
                : PrintPreviewFetchResult.Downloaded(FakePreviewFetcher.NewPreview(), "\"1\"");
        };
        string[] ids = ["a", "b", "c", "d", "e"];
        TestPreviewReference[] files = [Benchy, Cube];

        Task[] workers = [.. Enumerable.Range(0, 8).Select(worker => Task.Run(
            async () =>
            {
                Random random = new(worker);
                for (int i = 0; i < 300; i++)
                {
                    string id = ids[random.Next(ids.Length)];
                    long jobId = random.Next(40, 44);
                    PrintJob job = Job(jobId, files[jobId % 2]);
                    switch (random.Next(6))
                    {
                        case 0:
                            cache.Observe(Snapshot(PrinterState.Printing, job) with { PrinterId = id });
                            break;
                        case 1:
                            cache.Remove(id);
                            break;
                        case 2:
                            cache.RetainOnly(ids.Take(random.Next(ids.Length + 1)).Select(static printerId => Printer(printerId)));
                            break;
                        case 3:
                            cache.Clear();
                            break;
                        default:
                            await cache.GetAsync(Printer(id), job, (PrintPreviewSize)random.Next(2), Ct);
                            break;
                    }
                }
            },
            Ct))];

        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(60), Ct);

        Assert.InRange(cache.Count, 0, 3);
    }

    [Fact]
    public async Task InvalidArgumentsAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _cache.GetAsync(Printer(), Job(42, Benchy), (PrintPreviewSize)7, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _cache.GetAsync(null!, Job(42, Benchy), PrintPreviewSize.Large, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _cache.GetAsync(Printer(), null!, PrintPreviewSize.Large, Ct));
        Assert.Throws<ArgumentOutOfRangeException>(static () => new PrintPreviewCacheOptions { MaxPrinters = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(static () => new PrintPreviewCacheOptions { RetryDelay = TimeSpan.Zero });
    }

    private static PrinterDefinition Printer(string id = "core-one", string address = "192.168.1.20") => new()
    {
        Id = id,
        Name = "Core One",
        ConnectionType = ConnectionType.PrusaLink,
        Address = address,
    };

    private static PrintJob Job(long id, PrintPreviewReference reference) => new()
    {
        Id = id,
        FileName = "benchy.bgcode",
        ProgressPercent = 37,
        PreviewReference = reference,
    };

    private PrinterSnapshot Snapshot(PrinterState state, PrintJob? job) => new()
    {
        PrinterId = "core-one",
        State = state,
        Timestamp = _time.GetUtcNow(),
        Job = job,
    };

    /// <summary>A logger whose provider fails, as Microsoft.Extensions.Logging reports it (AggregateException).</summary>
    private sealed class ThrowingLogger : ILogger<PrintPreviewCache>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            throw new AggregateException(new IOException("log file locked"));
    }
}
