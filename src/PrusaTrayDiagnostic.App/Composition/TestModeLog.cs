#if DEBUG
using Microsoft.Extensions.Logging;

namespace PrusaTrayDiagnostic.App.Composition;

/// <summary>
/// Debug builds only: in the isolated test mode (T-044) every log message goes to <see cref="FileName"/> in the test data
/// folder, so that an end-to-end test can show what the app did. The app logs no secrets (CLAUDE.md), and the file lives
/// and dies with the test folder.
/// </summary>
internal sealed class TestModeLogProvider : ILoggerProvider
{
    public const string FileName = "test-log.txt";

    private readonly string _path;
    private readonly Lock _gate = new();

    public TestModeLogProvider(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _path = Path.Combine(dataDirectory, FileName);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A test log must never break the app.
            }
        }
    }

    private sealed class FileLogger(TestModeLogProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);
            string text = $"{DateTime.Now:HH:mm:ss.fff} {logLevel} {category}: {formatter(state, exception)}";
            provider.Write(exception is null ? text : $"{text}{Environment.NewLine}  {exception.GetType().FullName}: {exception.Message}");
        }
    }
}
#endif
