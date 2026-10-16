using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

/// <summary>Real-time guard so that a broken test fails instead of hanging.</summary>
internal static class TestTimeouts
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
}

/// <summary>Sets the current (UI) culture of the calling thread and restores the previous one on dispose.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string name)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}

/// <summary>Queues posted actions until the test runs them, like a busy UI thread would.</summary>
internal sealed class ManualDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _queue = new();
    private readonly Lock _lock = new();

    public int PostCount { get; private set; }

    public int Pending
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    public void Post(Action action)
    {
        lock (_lock)
        {
            PostCount++;
            _queue.Enqueue(action);
        }
    }

    public void RunAll()
    {
        while (true)
        {
            Action action;
            lock (_lock)
            {
                if (!_queue.TryDequeue(out action!))
                {
                    return;
                }
            }

            action();
        }
    }
}

/// <summary>Monitor whose snapshots and run outcome are driven by the test.</summary>
internal sealed class FakePrinterMonitor : IPrinterMonitor
{
    private readonly TaskCompletionSource<(IReadOnlyList<PrinterDefinition> Printers, TimeSpan Interval)> _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event EventHandler<PrinterSnapshot>? SnapshotUpdated;

    public int SubscriberCount => SnapshotUpdated?.GetInvocationList().Length ?? 0;

    public IReadOnlyList<PrinterSnapshot> Snapshots => [];

    /// <summary>What <see cref="RunAsync"/> does after recording its arguments; defaults to waiting for cancellation.</summary>
    public Func<CancellationToken, Task>? RunBehavior { get; set; }

    public Task<(IReadOnlyList<PrinterDefinition> Printers, TimeSpan Interval)> Started => _started.Task;

    public bool TryGetSnapshot(string printerId, [NotNullWhen(true)] out PrinterSnapshot? snapshot)
    {
        snapshot = null;
        return false;
    }

    public async Task RunAsync(IReadOnlyList<PrinterDefinition> printers, TimeSpan pollingInterval, CancellationToken cancellationToken)
    {
        _started.TrySetResult((printers, pollingInterval));
        if (RunBehavior is not null)
        {
            await RunBehavior(cancellationToken);
            return;
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Raise(PrinterSnapshot snapshot) => SnapshotUpdated?.Invoke(this, snapshot);
}

internal sealed class FakeSettingsStore(Func<CancellationToken, Task<SettingsLoadResult>> load) : ISettingsStore
{
    public static FakeSettingsStore Returning(AppSettings settings) =>
        new(_ => Task.FromResult(new SettingsLoadResult { Settings = settings, Status = SettingsLoadStatus.Loaded }));

    public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) => load(cancellationToken);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The tray never saves settings.");
}

internal static class TestPrinters
{
    public static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public static PrinterDefinition Definition(string id = "mk4", string name = "MK4") =>
        new() { Id = id, Name = name, ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.10" };

    public static PrinterSnapshot Snapshot(PrinterState state, PrintJob? job = null, string printerId = "mk4") =>
        new() { PrinterId = printerId, State = state, Timestamp = Now, Job = job };

    public static PrintJob Job(double progress, TimeSpan? remaining = null, string? fileName = "benchy.bgcode") =>
        new() { ProgressPercent = progress, Remaining = remaining, FileName = fileName };

    public static TrayPrinter Printer(PrinterState? state, PrintJob? job = null, string id = "mk4", string name = "MK4") =>
        new(Definition(id, name), state is { } s ? Snapshot(s, job, id) : null);
}

/// <summary>Records log entries so tests can assert what was (not) logged.</summary>
internal sealed class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    private readonly Lock _lock = new();
    private readonly List<(Microsoft.Extensions.Logging.LogLevel Level, string Message, Exception? Exception)> _entries = [];

    public IReadOnlyList<(Microsoft.Extensions.Logging.LogLevel Level, string Message, Exception? Exception)> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_lock)
        {
            _entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}

/// <summary>A logger whose provider is gone (e.g. disposed during quit): every write throws.</summary>
internal sealed class ThrowingLogger : Microsoft.Extensions.Logging.ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        throw new ObjectDisposedException("LoggerFactory");
}
