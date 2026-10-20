using System.Collections.Concurrent;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Tests.Monitoring;

/// <summary>Model detection at the start of monitoring (T-039): <see cref="IPrinterMonitor.ModelDetected"/>.</summary>
public sealed class PrinterMonitorModelDetectionTests : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly PrinterModelCode Mk4S = new(1, 4, 1);

    private readonly FakeTimeProvider _time = new();
    private readonly FakePrinterClientFactory _factory;
    private readonly ConcurrentDictionary<string, ModelClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ListLogger<PrinterMonitor> _logger = new();
    private readonly PrinterMonitor _monitor;
    private readonly SnapshotRecorder _recorder;
    private readonly Channel<PrinterModelDetected> _detections = Channel.CreateUnbounded<PrinterModelDetected>();
    private readonly CancellationTokenSource _cts = new();
    private Task? _run;

    public PrinterMonitorModelDetectionTests()
    {
        _factory = new FakePrinterClientFactory(_time) { Custom = printer => Client(printer.Id) };
        _monitor = new PrinterMonitor(_factory, _time, _logger);
        _recorder = new SnapshotRecorder(_monitor);
        _monitor.ModelDetected += (_, detected) => _detections.Writer.TryWrite(detected);
    }

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
    public async Task ModelIsReportedOnceAfterTheFirstPoll()
    {
        ModelClient client = Client("mk4");
        client.Detection = _ => Task.FromResult(PrinterModelDetection.Detected(Mk4S));

        Run("mk4");

        PrinterModelDetected detected = await NextDetectionAsync();
        Assert.Equal(new PrinterModelDetected("mk4", Mk4S), detected);
        Assert.Equal(PrinterModel.Mk4S, detected.Model);
        await PollAsync("mk4", times: 3);
        Assert.Equal(1, client.DetectionCalls);
        Assert.False(_detections.Reader.TryRead(out _));
    }

    [Fact]
    public async Task DetectionNeverOverlapsAPoll()
    {
        ModelClient client = Client("mk4");
        client.Detection = _ =>
        {
            Assert.Equal(0, client.ActivePolls);
            return Task.FromResult(PrinterModelDetection.Detected(Mk4S));
        };

        Run("mk4");

        await NextDetectionAsync();
        Assert.Equal(1, client.DetectionCalls);
    }

    [Fact]
    public async Task UnreachablePrinterIsNotAsked()
    {
        ModelClient client = Client("mk4");
        client.Inner.Returns(PrinterState.Offline);
        client.Detection = _ => Task.FromResult(PrinterModelDetection.Detected(Mk4S));
        Run("mk4");
        await _recorder.NextAsync("mk4");
        Assert.Equal(0, client.DetectionCalls);

        client.Inner.Returns(PrinterState.Idle);
        _time.Advance(Interval);
        await _recorder.NextAsync("mk4");

        Assert.Equal(Mk4S, (await NextDetectionAsync()).Code);
        Assert.Equal(1, client.DetectionCalls);
    }

    [Fact]
    public async Task FailedDetectionIsRetriedAtMostThreeTimesPerRun()
    {
        ModelClient client = Client("mk4");
        client.Detection = _ => Task.FromResult(PrinterModelDetection.Failed);

        Run("mk4");
        await PollAsync("mk4", times: 6);

        Assert.Equal(PrinterMonitor.MaxModelDetectionAttempts, client.DetectionCalls);
        Assert.False(_detections.Reader.TryRead(out _));
    }

    [Fact]
    public async Task RetryAfterFailureReportsTheModel()
    {
        ModelClient client = Client("mk4");
        int calls = 0;
        client.Detection = _ => Task.FromResult(Interlocked.Increment(ref calls) == 1 ? PrinterModelDetection.Failed : PrinterModelDetection.Detected(Mk4S));

        Run("mk4");
        await _recorder.NextAsync("mk4");
        _time.Advance(Interval);

        Assert.Equal(Mk4S, (await NextDetectionAsync()).Code);
        Assert.Equal(2, client.DetectionCalls);
    }

    [Fact]
    public async Task NotReportedModelEndsTheAskingWithoutEvent()
    {
        ModelClient client = Client("mk4");
        client.Detection = _ => Task.FromResult(PrinterModelDetection.NotReported);

        Run("mk4");
        await PollAsync("mk4", times: 4);

        Assert.Equal(1, client.DetectionCalls);
        Assert.False(_detections.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ExceptionCountsAsFailedAttemptAndIsLogged()
    {
        ModelClient client = Client("mk4");
        client.Detection = _ => Task.FromException<PrinterModelDetection>(new InvalidOperationException("boom"));

        Run("mk4");
        await PollAsync("mk4", times: 5);

        Assert.Equal(PrinterMonitor.MaxModelDetectionAttempts, client.DetectionCalls);
        Assert.Contains(_logger.Entries, static e => e.Level == LogLevel.Warning && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task HangingDetectionTimesOutAndPollingGoesOn()
    {
        ModelClient client = Client("mk4");
        TaskCompletionSource asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Detection = async ct =>
        {
            asked.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        };
        Run("mk4");
        await _recorder.NextAsync("mk4");
        await asked.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        // The poll timeout (10 s) ends the request; the next tick (already due) polls again.
        _time.Advance(PrinterMonitorOptions.Default.PollTimeout);

        Assert.Equal(PrinterState.Idle, (await _recorder.NextAsync("mk4")).State);
    }

    [Fact]
    public async Task ClientWithoutModelSourceIsNeverAsked()
    {
        _factory.Custom = null;

        Run("mk4");
        await PollAsync("mk4", times: 2);

        Assert.False(_detections.Reader.TryRead(out _));
    }

    [Fact]
    public async Task FailingHandlerIsLoggedAndOthersStillRun()
    {
        Client("mk4").Detection = _ => Task.FromResult(PrinterModelDetected());
        _monitor.ModelDetected += static (_, _) => throw new InvalidOperationException("handler");
        Channel<PrinterModelDetected> second = Channel.CreateUnbounded<PrinterModelDetected>();
        _monitor.ModelDetected += (_, detected) => second.Writer.TryWrite(detected);

        Run("mk4");

        await NextDetectionAsync();
        await second.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        Assert.Contains(_logger.Entries, static e => e.Level == LogLevel.Error && e.Message.Contains("ModelDetected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemovedHandlerIsNotCalled()
    {
        Client("mk4").Detection = _ => Task.FromResult(PrinterModelDetected());
        int calls = 0;
        EventHandler<PrinterModelDetected> handler = (_, _) => Interlocked.Increment(ref calls);
        _monitor.ModelDetected += handler;
        _monitor.ModelDetected -= handler;

        Run("mk4");
        await NextDetectionAsync();

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task EveryRunAsksAgain()
    {
        ModelClient client = Client("mk4");
        client.Detection = _ => Task.FromResult(PrinterModelDetection.Detected(Mk4S));
        CancellationTokenSource first = new();
        Task run = _monitor.RunAsync([Printer("mk4")], Interval, first.Token);
        await NextDetectionAsync();
        await first.CancelAsync();
        await run.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        first.Dispose();

        Run("mk4");

        await NextDetectionAsync();
        Assert.Equal(2, client.DetectionCalls);
    }

    [Fact]
    public void DefaultInterfaceEventIsANoOp()
    {
        IPrinterMonitor monitor = new SilentMonitor();
        EventHandler<PrinterModelDetected> handler = static (_, _) => { };

        monitor.ModelDetected += handler;
        monitor.ModelDetected -= handler;
    }

    private static PrinterModelDetection PrinterModelDetected() => PrinterModelDetection.Detected(Mk4S);

    private ModelClient Client(string id) => _clients.GetOrAdd(id, key => new ModelClient(_factory[key]));

    private void Run(params string[] ids) =>
        _run = _monitor.RunAsync([.. ids.Select(Printer)], Interval, _cts.Token);

    /// <summary>
    /// Waits for <paramref name="times"/> snapshots, advancing the clock by one interval between them. The loop asks for
    /// the model between a snapshot and the next poll, so once snapshot N is out, every request after snapshots
    /// 1…N-1 has completed (a tick that falls due meanwhile is kept by the timer).
    /// </summary>
    private async Task PollAsync(string id, int times)
    {
        await _recorder.NextAsync(id);
        for (int i = 1; i < times; i++)
        {
            _time.Advance(Interval);
            await _recorder.NextAsync(id);
        }
    }

    private async Task<PrinterModelDetected> NextDetectionAsync() =>
        await _detections.Reader.ReadAsync().AsTask().WaitAsync(TestTimeouts.Wait);

    private static PrinterDefinition Printer(string id) =>
        new() { Id = id, Name = id, ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" };

    /// <summary>Wraps the scriptable client and adds model detection.</summary>
    private sealed class ModelClient(FakePrinterClient inner) : IPrinterClient, IPrinterModelSource
    {
        private int _detectionCalls;
        private int _activePolls;

        public FakePrinterClient Inner { get; } = inner;

        public Func<CancellationToken, Task<PrinterModelDetection>> Detection { get; set; } =
            static _ => Task.FromResult(PrinterModelDetection.NotReported);

        public int DetectionCalls => Volatile.Read(ref _detectionCalls);

        public int ActivePolls => Volatile.Read(ref _activePolls);


        public async Task<PrinterSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _activePolls);
            try
            {
                return await Inner.GetSnapshotAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _activePolls);
            }
        }

        public async Task<PrinterModelDetection> DetectModelAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _detectionCalls);
            return await Detection(cancellationToken);
        }
    }

    /// <summary>A monitor that relies on the default (no-op) <see cref="IPrinterMonitor.ModelDetected"/>.</summary>
    private sealed class SilentMonitor : IPrinterMonitor
    {
        public event EventHandler<PrinterSnapshot>? SnapshotUpdated
        {
            add { }
            remove { }
        }

        public IReadOnlyList<PrinterSnapshot> Snapshots => [];

        public bool TryGetSnapshot(string printerId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PrinterSnapshot? snapshot)
        {
            snapshot = null;
            return false;
        }

        public Task RunAsync(IReadOnlyList<PrinterDefinition> printers, TimeSpan pollingInterval, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
