using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Logging;

namespace PrusaTrayDiagnostic.App.Tests.Logging;

/// <summary>Unhandled exceptions in the log (T-046): what is written, the flush, and that a handler never throws.</summary>
public sealed class UnhandledExceptionLoggerTests : IDisposable
{
    private readonly string _folder = TestDirectories.NewDirectoryPath();
    private readonly RecordingLoggerFactory _records = new();
    private readonly List<TimeSpan> _flushes = [];

    public void Dispose() => TestDirectories.Delete(_folder);

    [Fact]
    public void ProcessCrashIsLoggedCriticalWithTheExceptionAndFlushed()
    {
        UnhandledExceptionLogger crashes = new(_records, Flush);
        InvalidOperationException exception = Thrown();

        crashes.ReportUnhandled(exception, isTerminating: true);

        Record record = Assert.Single(_records.Records);
        Assert.Equal(UnhandledExceptionLogger.CategoryName, record.Category);
        Assert.Equal(LogLevel.Critical, record.Level);
        Assert.Equal("Unhandled exception (the process ends: True).", record.Message);
        Assert.Same(exception, record.Exception);
        Assert.Equal([UnhandledExceptionLogger.FlushTimeout], _flushes);
        Assert.Equal(TimeSpan.FromSeconds(2), UnhandledExceptionLogger.FlushTimeout);
    }

    [Fact]
    public void UiThreadCrashIsLoggedOnceAlthoughTheProcessHandlerSeesItAgain()
    {
        UnhandledExceptionLogger crashes = new(_records, Flush);
        InvalidOperationException exception = Thrown();

        crashes.ReportDispatcherUnhandled(exception);
        // The dispatcher does not mark it handled, so the same exception ends the process.
        crashes.ReportUnhandled(exception, isTerminating: true);

        Record record = Assert.Single(_records.Records);
        Assert.Equal("Unhandled exception on the UI thread.", record.Message);
        Assert.Equal(LogLevel.Critical, record.Level);
        // Written before the app ends, and again by the process handler (cheap: nothing new queued).
        Assert.Equal(2, _flushes.Count);
    }

    [Fact]
    public void AnotherExceptionAfterAUiThreadCrashIsStillLogged()
    {
        UnhandledExceptionLogger crashes = new(_records, Flush);
        InvalidOperationException ui = Thrown();
        InvalidOperationException other = Thrown();

        crashes.ReportDispatcherUnhandled(ui);
        crashes.ReportUnhandled(other, isTerminating: true);

        Assert.Equal([ui, other], _records.Records.Select(static record => record.Exception));
    }

    [Fact]
    public void NonExceptionObjectIsNamedByItsType()
    {
        UnhandledExceptionLogger crashes = new(_records, Flush);

        crashes.ReportUnhandled("thrown by another language", isTerminating: false);

        Assert.Equal("Unhandled non-exception object of type System.String (the process ends: False).", Assert.Single(_records.Records).Message);
    }

    [Fact]
    public void UnobservedTaskIsAnErrorAndNotFlushed()
    {
        UnhandledExceptionLogger crashes = new(_records, Flush);
        AggregateException exception = new(Thrown());

        crashes.ReportUnobservedTask(exception);

        Record record = Assert.Single(_records.Records);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Same(exception, record.Exception);
        Assert.Empty(_flushes);
    }

    [Fact]
    public void HandlersNeverThrow()
    {
        UnhandledExceptionLogger failingLog = new(new RecordingLoggerFactory { Throws = true }, Flush);
        UnhandledExceptionLogger failingFlush = new(_records, static _ => throw new IOException("disk gone"));

        failingLog.ReportUnhandled(Thrown(), isTerminating: true);
        failingLog.ReportDispatcherUnhandled(Thrown());
        failingLog.ReportUnobservedTask(new AggregateException(Thrown()));
        failingFlush.ReportUnhandled(Thrown(), isTerminating: true);
        failingFlush.ReportDispatcherUnhandled(Thrown());
    }

    [Fact]
    public void CrashReachesTheFileWithItsStackTraceBeforeTheHandlerReturns()
    {
        LogFile file = new(_folder);
        using FileLogProvider provider = new(file, new LogLevelSwitch(LogLevel.Information));
        using LoggerFactory factory = new([provider]);
        UnhandledExceptionLogger crashes = new(factory, provider.Flush);

        crashes.ReportUnhandled(Thrown(), isTerminating: true);

        // No wait: the handler flushed, the process could end right now.
        string log = File.ReadAllText(LogFile.PathOf(_folder, 0));
        Assert.Contains($" CRT [pid {Environment.ProcessId}] {UnhandledExceptionLogger.CategoryName}: Unhandled exception (the process ends: True).", log, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: test crash", log, StringComparison.Ordinal);
        Assert.Contains(nameof(Thrown), log, StringComparison.Ordinal);
    }

    [Fact]
    public void HandlerAfterTheLoggingWasReleasedWritesNothingAndDoesNotThrow()
    {
        LogFile file = new(_folder);
        FileLogProvider provider = new(file, new LogLevelSwitch(LogLevel.Information));
        using LoggerFactory factory = new([provider]);
        UnhandledExceptionLogger crashes = new(factory, provider.Flush);
        provider.Dispose();

        crashes.ReportUnhandled(Thrown(), isTerminating: true);
        crashes.ReportDispatcherUnhandled(Thrown());
        crashes.ReportUnobservedTask(new AggregateException(Thrown()));

        Assert.False(File.Exists(LogFile.PathOf(_folder, 0)));
    }

    [Fact]
    public async Task UnobservedTaskOfTheFinalizerIsReported()
    {
        // The real event: a faulted task nobody awaited is reported when the garbage collector finalizes it.
        UnhandledExceptionLogger crashes = new(_records, Flush);
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.InnerException?.Message == UnobservedMessage)
            {
                crashes.ReportUnobservedTask(e.Exception);
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            await StartUnobservedFaultAsync();
            for (int attempt = 0; attempt < 50 && _records.Records.Count == 0; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        Record record = Assert.Single(_records.Records);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal(UnobservedMessage, record.Exception!.InnerException!.Message);
    }

    private const string UnobservedMessage = "T-046 unobserved task";

    /// <summary>The faulted task is no longer referenced once this returns.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task StartUnobservedFaultAsync()
    {
        Task faulted = Task.Run(static () => throw new InvalidOperationException(UnobservedMessage));
        await Task.WhenAny(faulted);
    }

    private bool Flush(TimeSpan timeout)
    {
        _flushes.Add(timeout);
        return true;
    }

    private static InvalidOperationException Thrown()
    {
        try
        {
            throw new InvalidOperationException("test crash");
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    private sealed record Record(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public bool Throws { get; init; }

        public List<Record> Records { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

        public void Dispose()
        {
        }

        private sealed class Logger(RecordingLoggerFactory owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (owner.Throws)
                {
                    throw new InvalidOperationException("The log is broken.");
                }

                lock (owner.Records)
                {
                    owner.Records.Add(new Record(category, logLevel, formatter(state, exception), exception));
                }
            }
        }
    }
}
