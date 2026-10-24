using Avalonia.Threading;

using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Logging;

/// <summary>
/// Writes unhandled exceptions into the log before the process ends (T-046), so that a crash leaves its stack trace
/// behind. The behaviour stays as before: nothing is marked handled or observed, the app crashes as it did and nothing is
/// reported at the next start. Every handler writes the log queue to the file (at most <see cref="FlushTimeout"/>, a
/// hanging disk does not hold the end up) and never throws itself.
/// </summary>
internal sealed partial class UnhandledExceptionLogger
{
    public const string CategoryName = "LayerPeek.App.UnhandledExceptions";

    /// <summary>How long a crash handler waits for the log to reach the file.</summary>
    internal static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger _logger;
    private readonly Func<TimeSpan, bool> _flush;

    // An exception of the UI thread is reported again by the process handler when it ends the app: logged once.
    private object? _reportedByDispatcher;

    /// <param name="flush">Writes the queued log records within the given time (<see cref="AppLogging.Flush"/>).</param>
    public UnhandledExceptionLogger(ILoggerFactory loggerFactory, Func<TimeSpan, bool> flush)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(flush);
        _logger = loggerFactory.CreateLogger(CategoryName);
        _flush = flush;
    }

    /// <summary>
    /// Unhandled exceptions of every thread and unobserved exceptions of tasks, for the life of the process (registered in
    /// <c>Program.Main</c> right after the logging, so also for a second start).
    /// </summary>
    public void AttachToProcess()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>Unhandled exceptions of the UI thread (delegates run by the Avalonia dispatcher, <c>async void</c> handlers).</summary>
    public void AttachToDispatcher(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        dispatcher.UnhandledException += OnDispatcherUnhandledException;
    }

    /// <summary>An unhandled exception that ends the process (or, rarely, does not: <paramref name="isTerminating"/>).</summary>
    internal void ReportUnhandled(object? exceptionObject, bool isTerminating)
    {
        try
        {
            if (!ReferenceEquals(exceptionObject, Volatile.Read(ref _reportedByDispatcher)))
            {
                if (exceptionObject is Exception exception)
                {
                    LogUnhandled(exception, isTerminating);
                }
                else
                {
                    // Only other languages throw non-exceptions; the type is all there is.
                    LogUnhandledObject(exceptionObject?.GetType().FullName ?? "null", isTerminating);
                }
            }

            _flush(FlushTimeout);
        }
        catch (Exception)
        {
            // A crash handler must not throw: the original exception has to end the process as it would without it.
        }
    }

    /// <summary>An unhandled exception of the UI thread; it still ends the app (not marked handled).</summary>
    internal void ReportDispatcherUnhandled(Exception exception)
    {
        try
        {
            Volatile.Write(ref _reportedByDispatcher, exception);
            LogDispatcherUnhandled(exception);
            _flush(FlushTimeout);
        }
        catch (Exception)
        {
            // See ReportUnhandled.
        }
    }

    /// <summary>A faulted task nobody awaited, found by the finalizer; the app goes on (not marked observed).</summary>
    internal void ReportUnobservedTask(Exception exception)
    {
        try
        {
            LogUnobservedTask(exception);
        }
        catch (Exception)
        {
            // Runs on the finalizer thread: it must never throw.
        }
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) => ReportUnhandled(e.ExceptionObject, e.IsTerminating);

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e) => ReportDispatcherUnhandled(e.Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) => ReportUnobservedTask(e.Exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception (the process ends: {IsTerminating}).")]
    private partial void LogUnhandled(Exception exception, bool isTerminating);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled non-exception object of type {Type} (the process ends: {IsTerminating}).")]
    private partial void LogUnhandledObject(string type, bool isTerminating);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception on the UI thread.")]
    private partial void LogDispatcherUnhandled(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A task failed and nobody observed its exception; the app goes on.")]
    private partial void LogUnobservedTask(Exception exception);
}
