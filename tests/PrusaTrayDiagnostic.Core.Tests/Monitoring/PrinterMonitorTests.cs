using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Tests.Monitoring;

public sealed class PrinterMonitorTests : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new();
    private readonly FakePrinterClientFactory _factory;
    private readonly ListLogger<PrinterMonitor> _logger = new();
    private readonly PrinterMonitor _monitor;
    private readonly SnapshotRecorder _recorder;
    private readonly CancellationTokenSource _cts = new();
    private Task? _run;

    public PrinterMonitorTests()
    {
        _factory = new FakePrinterClientFactory(_time);
        _monitor = new PrinterMonitor(_factory, _time, _logger);
        _recorder = new SnapshotRecorder(_monitor);
    }

    private DateTimeOffset Start => _time.Start;

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_run is not null)
        {
            await _run.WaitAsync(TestTimeouts.Wait);
        }

        _cts.Dispose();
    }

    [Fact]
    public async Task PollsAllPrintersImmediatelyAndPublishesSnapshots()
    {
        _factory["xl"].Returns(PrinterState.Printing);

        Run("mk4", "xl");

        Assert.Equal(PrinterState.Idle, (await _recorder.NextAsync("mk4")).State);
        Assert.Equal(PrinterState.Printing, (await _recorder.NextAsync("xl")).State);
        Assert.Equal(Start, await _factory["mk4"].NextCallAsync());
        Assert.Equal(Start, await _factory["xl"].NextCallAsync());
    }

    [Fact]
    public async Task PollsAgainExactlyAfterInterval()
    {
        Run("mk4");
        await _recorder.NextAsync("mk4");

        _time.Advance(Interval);
        await _recorder.NextAsync("mk4");

        FakePrinterClient client = _factory["mk4"];
        Assert.Equal(Start, await client.NextCallAsync());
        Assert.Equal(Start + Interval, await client.NextCallAsync());
    }

    [Fact]
    public async Task SlowPollDoesNotShiftCadence()
    {
        FakePrinterClient client = _factory["mk4"];
        TaskCompletionSource<PrinterSnapshot> slow = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Behavior = _ => slow.Task;
        Run("mk4");
        await client.NextCallAsync();

        _time.Advance(TimeSpan.FromSeconds(3));
        client.Returns(PrinterState.Idle);
        slow.SetResult(client.Snapshot(PrinterState.Idle));
        await _recorder.NextAsync("mk4");
        _time.Advance(TimeSpan.FromSeconds(2));
        await _recorder.NextAsync("mk4");

        Assert.Equal(Start + Interval, await client.NextCallAsync());
    }

    [Fact]
    public async Task FailingPrinterDoesNotAffectOthers()
    {
        _factory["bad"].Throws(new HttpRequestException("connection to secret-host failed"));

        Run("bad", "good");

        PrinterSnapshot bad = await _recorder.NextAsync("bad");
        Assert.Equal(PrinterState.Unknown, bad.State);
        Assert.Contains(nameof(HttpRequestException), bad.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-host", bad.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(PrinterState.Idle, (await _recorder.NextAsync("good")).State);

        _time.Advance(Interval);
        Assert.Equal(PrinterState.Unknown, (await _recorder.NextAsync("bad")).State);
        Assert.Equal(PrinterState.Idle, (await _recorder.NextAsync("good")).State);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Exception is HttpRequestException);
    }

    [Fact]
    public async Task UnreachablePrinterIsPolledWithExponentialBackoffUpToOneMinute()
    {
        FakePrinterClient client = _factory["mk4"];
        client.Returns(PrinterState.Offline);
        Run("mk4");

        await _recorder.NextAsync("mk4");
        Assert.Equal(Start, await client.NextCallAsync());

        int[] expectedSeconds = [5, 15, 35, 75, 135, 195];
        int previous = 0;
        foreach (int seconds in expectedSeconds)
        {
            // Stop one second early first: no poll may happen before the backoff delay elapsed.
            _time.Advance(TimeSpan.FromSeconds(seconds - previous - 1));
            _time.Advance(TimeSpan.FromSeconds(1));
            await _recorder.NextAsync("mk4");

            Assert.Equal(Start.AddSeconds(seconds), await client.NextCallAsync());
            Assert.False(client.HasPendingCall);
            previous = seconds;
        }
    }

    [Fact]
    public async Task RecoveryRestoresNormalInterval()
    {
        FakePrinterClient client = _factory["mk4"];
        client.Returns(PrinterState.Offline);
        Run("mk4");
        await _recorder.NextAsync("mk4"); // t0 offline
        _time.Advance(Interval);
        await _recorder.NextAsync("mk4"); // t5 offline, next in 10 s

        client.Returns(PrinterState.Idle);
        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(PrinterState.Idle, (await _recorder.NextAsync("mk4")).State); // t15
        _time.Advance(Interval);
        await _recorder.NextAsync("mk4");

        DateTimeOffset[] calls = [await client.NextCallAsync(), await client.NextCallAsync(), await client.NextCallAsync(), await client.NextCallAsync()];
        Assert.Equal([Start, Start.AddSeconds(5), Start.AddSeconds(15), Start.AddSeconds(20)], calls);
    }

    [Fact]
    public async Task HungClientTimesOutAsOffline()
    {
        FakePrinterClient client = _factory["mk4"];
        client.Hangs();
        Run("mk4");
        await client.NextCallAsync();

        _time.Advance(PrinterMonitorOptions.Default.PollTimeout);
        PrinterSnapshot snapshot = await _recorder.NextAsync("mk4");

        Assert.Equal(PrinterState.Offline, snapshot.State);
        Assert.Equal(Start + PrinterMonitorOptions.Default.PollTimeout, snapshot.Timestamp);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.StatusMessage));
    }

    [Fact]
    public async Task ClientIgnoringCancellationStillTimesOut()
    {
        FakePrinterClient client = _factory["mk4"];
        client.HangsIgnoringCancellation();
        Run("mk4");
        await client.NextCallAsync();

        _time.Advance(PrinterMonitorOptions.Default.PollTimeout);

        Assert.Equal(PrinterState.Offline, (await _recorder.NextAsync("mk4")).State);
    }

    [Fact]
    public async Task TimeoutIsRecoveredAndNextPollWorks()
    {
        FakePrinterClient client = _factory["mk4"];
        client.HangsOnceThenReturns(PrinterState.Idle);
        Run("mk4");
        await client.NextCallAsync();

        _time.Advance(PrinterMonitorOptions.Default.PollTimeout); // t10: timeout; the overdue t5 tick is dropped
        Assert.Equal(PrinterState.Offline, (await _recorder.NextAsync("mk4")).State);

        _time.Advance(Interval);
        Assert.Equal(PrinterState.Idle, (await _recorder.NextAsync("mk4")).State);
        Assert.Equal(Start + PrinterMonitorOptions.Default.PollTimeout + Interval, await client.NextCallAsync());
    }

    [Fact]
    public async Task CancellationCompletesRunCleanlyAndDisposesClients()
    {
        _factory["mk4"].Hangs();
        Run("mk4", "xl");
        await _factory["mk4"].NextCallAsync();
        await _recorder.NextAsync("xl");

        await _cts.CancelAsync();
        await _run!.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(_run.IsCompletedSuccessfully);
        Assert.True(_factory["mk4"].IsDisposed);
        Assert.True(_factory["xl"].IsDisposed);
    }

    [Fact]
    public async Task CancellationCompletesEvenWhenClientIgnoresIt()
    {
        _factory["mk4"].HangsIgnoringCancellation();
        Run("mk4");
        await _factory["mk4"].NextCallAsync();

        await _cts.CancelAsync();
        await _run!.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(_run.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task LateFaultOfCallIgnoringStopIsLogged()
    {
        FakePrinterClient client = _factory["mk4"];
        TaskCompletionSource<PrinterSnapshot> stuck = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Behavior = _ => stuck.Task;
        Run("mk4");
        await client.NextCallAsync();

        await _cts.CancelAsync();
        await _run!.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        stuck.SetException(new ObjectDisposedException("client"));

        await WaitUntilAsync(() => _logger.Entries.Any(static e => e.Exception is ObjectDisposedException));
    }

    [Fact]
    public async Task NoPollsAfterCancellation()
    {
        Run("mk4");
        await _recorder.NextAsync("mk4");
        await _factory["mk4"].NextCallAsync();

        await _cts.CancelAsync();
        await _run!.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(_factory["mk4"].HasPendingCall);
    }

    [Fact]
    public async Task ThrowingSubscriberDoesNotStopMonitoring()
    {
        _monitor.SnapshotUpdated += static (_, _) => throw new InvalidOperationException("subscriber bug");
        Run("mk4");
        await _recorder.NextAsync("mk4");

        _time.Advance(Interval);

        await _recorder.NextAsync("mk4");
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task FactoryFailureIsReportedAndRetried()
    {
        _factory.FailingIds.Add("bad");
        Run("bad", "good");

        PrinterSnapshot bad = await _recorder.NextAsync("bad");
        Assert.Equal(PrinterState.Unknown, bad.State);
        Assert.Equal(PrinterState.Idle, (await _recorder.NextAsync("good")).State);

        _factory.FailingIds.Clear();
        _time.Advance(Interval);

        Assert.Equal(PrinterState.Idle, (await _recorder.NextAsync("bad")).State);
    }

    [Fact]
    public async Task InvalidClientResultsAreNormalized()
    {
        FakePrinterClient client = _factory["mk4"];
        client.Behavior = _ => Task.FromResult<PrinterSnapshot>(null!);
        Run("mk4");
        Assert.Equal(PrinterState.Unknown, (await _recorder.NextAsync("mk4")).State);

        client.Behavior = _ => Task.FromResult(new PrinterSnapshot { PrinterId = "other", State = PrinterState.Idle, Timestamp = Start });
        _time.Advance(Interval);

        PrinterSnapshot snapshot = await _recorder.NextAsync("mk4");
        Assert.Equal("mk4", snapshot.PrinterId);
        Assert.Equal(PrinterState.Idle, snapshot.State);
    }

    [Fact]
    public async Task SnapshotsAreKeptInConfigurationOrderAndLookupIgnoresCase()
    {
        Run("b", "a", "c");
        await _recorder.NextAsync("b");
        await _recorder.NextAsync("a");
        await _recorder.NextAsync("c");

        Assert.Equal(["b", "a", "c"], _monitor.Snapshots.Select(static s => s.PrinterId));
        Assert.True(_monitor.TryGetSnapshot("A", out PrinterSnapshot? snapshot));
        Assert.Equal("a", snapshot.PrinterId);
        Assert.False(_monitor.TryGetSnapshot("missing", out _));
    }

    [Fact]
    public async Task NewRunReplacesSnapshotsOfPreviousRun()
    {
        Run("old");
        await _recorder.NextAsync("old");
        await _cts.CancelAsync();
        await _run!.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        using CancellationTokenSource second = new();
        Task run = _monitor.RunAsync([Printer("new")], Interval, second.Token);
        await _recorder.NextAsync("new");

        Assert.False(_monitor.TryGetSnapshot("old", out _));
        Assert.Equal(["new"], _monitor.Snapshots.Select(static s => s.PrinterId));
        await second.CancelAsync();
        await run.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HangingPrinterBacksOffFromTheEndOfEachTimedOutPoll()
    {
        FakePrinterClient client = _factory["mk4"];
        client.Hangs();
        TimeSpan timeout = PrinterMonitorOptions.Default.PollTimeout;
        Run("mk4");
        Assert.Equal(Start, await client.NextCallAsync());

        // Poll start + 10 s timeout + backoff delay (5, 10, 20, 40, 60 s).
        int[] delays = [5, 10, 20, 40, 60, 60];
        DateTimeOffset pollStart = Start;
        foreach (int delay in delays)
        {
            _time.Advance(timeout);
            Assert.Equal(PrinterState.Offline, (await _recorder.NextAsync("mk4")).State);

            _time.Advance(TimeSpan.FromSeconds(delay - 1));
            _time.Advance(TimeSpan.FromSeconds(1));
            pollStart = pollStart + timeout + TimeSpan.FromSeconds(delay);
            Assert.Equal(pollStart, await client.NextCallAsync());
        }
    }

    [Fact]
    public async Task UnauthorizedStateSurvivesBackoffLongerThanPollTimeout()
    {
        // Regression: an idle timeout source must not cancel the next poll before it starts.
        FakePrinterClient client = _factory["mk4"];
        client.Returns(PrinterState.Unauthorized);
        Run("mk4");
        Assert.Equal(PrinterState.Unauthorized, (await _recorder.NextAsync("mk4")).State);

        foreach (int delay in new[] { 5, 10, 20, 40, 60, 60 })
        {
            _time.Advance(TimeSpan.FromSeconds(delay));
            Assert.Equal(PrinterState.Unauthorized, (await _recorder.NextAsync("mk4")).State);
        }
    }

    [Fact]
    public async Task MaxBackoffBelowIntervalKeepsTheInterval()
    {
        PrinterMonitor monitor = new(_factory, _time, _logger, new PrinterMonitorOptions { MaxBackoff = TimeSpan.Zero });
        SnapshotRecorder recorder = new(monitor);
        FakePrinterClient client = _factory["mk4"];
        client.Returns(PrinterState.Offline);
        Task run = monitor.RunAsync([Printer("mk4")], Interval, _cts.Token);

        await recorder.NextAsync("mk4");
        for (int i = 0; i < 4; i++)
        {
            _time.Advance(Interval);
            await recorder.NextAsync("mk4");
        }

        DateTimeOffset[] calls = [.. await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.NextCallAsync()))];
        Assert.Equal([.. Enumerable.Range(0, 5).Select(i => Start + (Interval * i))], calls);
        await _cts.CancelAsync();
        await run.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ClientIgnoringCancellationIsNotCalledAgainUntilItFinishes()
    {
        FakePrinterClient client = _factory["mk4"];
        TaskCompletionSource<PrinterSnapshot> stuck = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Behavior = _ => stuck.Task;
        Run("mk4");
        await client.NextCallAsync();

        _time.Advance(PrinterMonitorOptions.Default.PollTimeout); // t10 timeout
        Assert.Equal(PrinterState.Offline, (await _recorder.NextAsync("mk4")).State);
        _time.Advance(Interval); // t15: previous call still running, client must not be called
        Assert.Equal(PrinterState.Offline, (await _recorder.NextAsync("mk4")).State);
        Assert.False(client.HasPendingCall);

        client.Returns(PrinterState.Idle);
        stuck.SetResult(client.Snapshot(PrinterState.Idle));
        _time.Advance(TimeSpan.FromSeconds(10)); // t25: second failure doubled the delay

        Assert.Equal(PrinterState.Idle, (await _recorder.NextAsync("mk4")).State);
        Assert.Equal(Start.AddSeconds(25), await client.NextCallAsync());
    }

    [Fact]
    public async Task TimeoutWorksRepeatedlyWithSystemTimeProvider()
    {
        // Exercises the TryReset reuse path, which only TimeProvider.System supports.
        PrinterMonitor monitor = new(_factory, TimeProvider.System, _logger, new PrinterMonitorOptions { PollTimeout = TimeSpan.FromMilliseconds(200) });
        SnapshotRecorder recorder = new(monitor);
        int calls = 0;
        FakePrinterClient client = _factory["mk4"];
        client.Behavior = async ct =>
        {
            int call = Interlocked.Increment(ref calls);
            if (call is 3 or 5)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            return client.Snapshot(PrinterState.Idle);
        };
        using CancellationTokenSource cts = new();
        Task run = monitor.RunAsync([Printer("mk4")], TimeSpan.FromMilliseconds(20), cts.Token);

        PrinterState[] states = new PrinterState[7];
        for (int i = 0; i < states.Length; i++)
        {
            states[i] = (await recorder.NextAsync("mk4")).State;
        }

        Assert.Equal([PrinterState.Idle, PrinterState.Idle, PrinterState.Offline, PrinterState.Idle, PrinterState.Offline, PrinterState.Idle, PrinterState.Idle], states);
        await cts.CancelAsync();
        await run.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ThrowingSubscriberDoesNotStarveLaterSubscribers()
    {
        PrinterMonitor monitor = new(_factory, _time, _logger);
        monitor.SnapshotUpdated += static (_, _) => throw new InvalidOperationException("first subscriber bug");
        SnapshotRecorder recorder = new(monitor);
        Task run = monitor.RunAsync([Printer("mk4")], Interval, _cts.Token);

        Assert.Equal(PrinterState.Idle, (await recorder.NextAsync("mk4")).State);
        await _cts.CancelAsync();
        await run.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UnsubscribedHandlerIsNotCalled()
    {
        int calls = 0;
        EventHandler<PrinterSnapshot> handler = (_, _) => Interlocked.Increment(ref calls);
        _monitor.SnapshotUpdated += handler;
        _monitor.SnapshotUpdated -= handler;
        _monitor.SnapshotUpdated -= handler; // removing twice is harmless

        Run("mk4");
        await _recorder.NextAsync("mk4");

        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposableOnlyClientIsDisposedAndDisposeFailureIsLogged(bool throwOnDispose)
    {
        DisposableOnlyClient? client = null;
        _factory.Custom = printer => client = new DisposableOnlyClient(printer.Id, _time, throwOnDispose);
        Run("mk4");
        await _recorder.NextAsync("mk4");

        await _cts.CancelAsync();
        await _run!.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(_run.IsCompletedSuccessfully);
        Assert.True(client!.IsDisposed);
        Assert.Equal(throwOnDispose, _logger.Entries.Any(static e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException));
    }

    [Fact]
    public async Task AlreadyCancelledTokenCompletesWithoutPolling()
    {
        await _cts.CancelAsync();

        Run("mk4");
        await _run!.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(_run.IsCompletedSuccessfully);
        Assert.False(_factory["mk4"].HasPendingCall);
    }

    [Fact]
    public async Task ConcurrentRunIsRejected()
    {
        Run("mk4");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _monitor.RunAsync([Printer("xl")], Interval, CancellationToken.None));
    }

    [Fact]
    public async Task EmptyPrinterListWaitsForCancellation()
    {
        Run();
        Assert.False(_run!.IsCompleted);

        await _cts.CancelAsync();
        await _run.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.True(_run.IsCompletedSuccessfully);
        Assert.Empty(_monitor.Snapshots);
    }

    [Fact]
    public async Task InvalidArgumentsAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _monitor.RunAsync([Printer("a"), Printer("A")], Interval, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _monitor.RunAsync([Printer("a")], TimeSpan.Zero, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _monitor.RunAsync([Printer("a")], PrinterMonitor.MinPollingInterval - TimeSpan.FromTicks(1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _monitor.RunAsync([Printer("a")], PrinterMonitor.MaxPollingInterval + TimeSpan.FromTicks(1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _monitor.RunAsync(null!, Interval, CancellationToken.None));
    }

    [Fact]
    public void OptionsRejectInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrinterMonitorOptions { PollTimeout = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrinterMonitorOptions { PollTimeout = PrinterMonitorOptions.MinPollTimeout - TimeSpan.FromTicks(1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrinterMonitorOptions { MaxBackoff = TimeSpan.FromSeconds(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrinterMonitorOptions { PollTimeout = PrinterMonitorOptions.MaxPollTimeout + TimeSpan.FromTicks(1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrinterMonitorOptions { MaxBackoff = PrinterMonitorOptions.MaxBackoffLimit + TimeSpan.FromTicks(1) });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TestTimeouts.Wait);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private void Run(params string[] printerIds) =>
        _run = _monitor.RunAsync([.. printerIds.Select(Printer)], Interval, _cts.Token);

    private static PrinterDefinition Printer(string id) =>
        new() { Id = id, Name = id, ConnectionType = ConnectionType.PrusaLink, Address = "10.0.0.1" };
}
