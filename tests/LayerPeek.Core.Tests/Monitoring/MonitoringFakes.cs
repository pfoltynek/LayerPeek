using System.Collections.Concurrent;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Monitoring;

/// <summary>Real-time guard so that a broken test fails instead of hanging.</summary>
internal static class TestTimeouts
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Scriptable <see cref="IPrinterClient"/>; records the (fake) time of every call.
/// </summary>
internal sealed class FakePrinterClient(string printerId, TimeProvider timeProvider) : IPrinterClient, IAsyncDisposable
{
    private readonly Channel<DateTimeOffset> _calls = Channel.CreateUnbounded<DateTimeOffset>();

    public string PrinterId { get; } = printerId;

    /// <summary>What a call does; defaults to an idle snapshot (honouring cancellation like a real client).</summary>
    public Func<CancellationToken, Task<PrinterSnapshot>> Behavior { get; set; } = null!;

    public bool IsDisposed { get; private set; }

    public Task<PrinterSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        _calls.Writer.TryWrite(timeProvider.GetUtcNow());
        return Behavior is null ? Respond(PrinterState.Idle, cancellationToken) : Behavior(cancellationToken);
    }

    public PrinterSnapshot Snapshot(PrinterState state) =>
        new() { PrinterId = PrinterId, State = state, Timestamp = timeProvider.GetUtcNow() };

    public void Returns(PrinterState state) => Behavior = ct => Respond(state, ct);

    public void Throws(Exception exception) => Behavior = _ => Task.FromException<PrinterSnapshot>(exception);

    /// <summary>Never completes unless cancelled.</summary>
    public void Hangs() => Behavior = static async ct =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new InvalidOperationException("unreachable");
    };

    /// <summary>The first call hangs until cancelled, later calls return <paramref name="state"/>.</summary>
    public void HangsOnceThenReturns(PrinterState state)
    {
        int calls = 0;
        Behavior = async ct =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            return Snapshot(state);
        };
    }

    /// <summary>Never completes and ignores cancellation (a misbehaving client).</summary>
    public void HangsIgnoringCancellation() => Behavior = static _ => new TaskCompletionSource<PrinterSnapshot>().Task;

    private Task<PrinterSnapshot> Respond(PrinterState state, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<PrinterSnapshot>(cancellationToken)
            : Task.FromResult(Snapshot(state));

    public async Task<DateTimeOffset> NextCallAsync() =>
        await _calls.Reader.ReadAsync().AsTask().WaitAsync(TestTimeouts.Wait);

    public bool HasPendingCall => _calls.Reader.TryPeek(out _);

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakePrinterClientFactory(TimeProvider timeProvider) : IPrinterClientFactory
{
    private readonly ConcurrentDictionary<string, FakePrinterClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Printer ids for which <see cref="Create"/> throws.</summary>
    public HashSet<string> FailingIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int CreatedCount;

    /// <summary>Overrides the client created for every printer.</summary>
    public Func<PrinterDefinition, IPrinterClient>? Custom { get; set; }

    public FakePrinterClient this[string printerId] =>
        _clients.GetOrAdd(printerId, id => new FakePrinterClient(id, timeProvider));

    public IPrinterClient Create(PrinterDefinition printer)
    {
        Interlocked.Increment(ref CreatedCount);
        if (FailingIds.Contains(printer.Id))
        {
            throw new NotSupportedException($"No client for {printer.Id}.");
        }

        return Custom?.Invoke(printer) ?? this[printer.Id];
    }
}

/// <summary>Collects published snapshots per printer.</summary>
internal sealed class SnapshotRecorder
{
    private readonly ConcurrentDictionary<string, Channel<PrinterSnapshot>> _channels = new(StringComparer.OrdinalIgnoreCase);

    public SnapshotRecorder(IPrinterMonitor monitor) => monitor.SnapshotUpdated += (_, snapshot) => Channel(snapshot.PrinterId).Writer.TryWrite(snapshot);

    public async Task<PrinterSnapshot> NextAsync(string printerId) =>
        await Channel(printerId).Reader.ReadAsync().AsTask().WaitAsync(TestTimeouts.Wait);

    private Channel<PrinterSnapshot> Channel(string printerId) =>
        _channels.GetOrAdd(printerId, static _ => System.Threading.Channels.Channel.CreateUnbounded<PrinterSnapshot>());
}

/// <summary>Client implementing only <see cref="IDisposable"/>.</summary>
internal sealed class DisposableOnlyClient(string printerId, TimeProvider timeProvider, bool throwOnDispose) : IPrinterClient, IDisposable
{
    public bool IsDisposed { get; private set; }

    public Task<PrinterSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterSnapshot { PrinterId = printerId, State = PrinterState.Idle, Timestamp = timeProvider.GetUtcNow() });

    public void Dispose()
    {
        IsDisposed = true;
        if (throwOnDispose)
        {
            throw new InvalidOperationException("dispose failed");
        }
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Enqueue((logLevel, exception, formatter(state, exception)));
}
