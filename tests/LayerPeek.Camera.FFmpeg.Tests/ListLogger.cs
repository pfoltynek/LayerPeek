using Microsoft.Extensions.Logging;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>Collects the log lines of the code under test (thread-safe).</summary>
internal sealed class ListLogger : ILogger
{
    private readonly Lock _gate = new();
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public IReadOnlyList<string> At(LogLevel level) => [.. Entries.Where(entry => entry.Level == level).Select(static entry => entry.Message)];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
