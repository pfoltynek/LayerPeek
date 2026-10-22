using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Logging;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Tests.Logging;

/// <summary>Detailed logging (T-047): the saved setting switches the level of the file log without a restart.</summary>
public sealed class DebugLoggingObserverTests
{
    private readonly LogLevelSwitch _level = new(LogLevel.Information);
    private readonly RecordingLogger _log = new();

    [Fact]
    public void LoadedSettingsAndEverySavedChangeSwitchTheLevel()
    {
        DebugLoggingObserver observer = new DebugLoggingObserver(_level, fixedLevel: false, _log);

        // Loaded settings (a new run) with detailed logging on.
        observer.ApplySettings(new AppSettings { DebugLogging = true });
        Assert.Equal(LogLevel.Debug, _level.MinimumLevel);

        // Saved without restarting the monitoring (only the setting changed).
        observer.UpdateSettings(new AppSettings());
        Assert.Equal(LogLevel.Information, _level.MinimumLevel);

        Assert.Equal(["Detailed logging (Debug) turned on.", "Detailed logging (Debug) turned off."], _log.Messages);
        Assert.All(_log.Levels, static level => Assert.Equal(LogLevel.Information, level));
    }

    [Fact]
    public void UnchangedSettingIsNotLoggedAgain()
    {
        DebugLoggingObserver observer = new DebugLoggingObserver(_level, fixedLevel: false, _log);

        observer.ApplySettings(new AppSettings());
        observer.UpdateSettings(new AppSettings { PollingInterval = TimeSpan.FromSeconds(10) });

        Assert.Equal(LogLevel.Information, _level.MinimumLevel);
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public void TestModeStaysAtDebugWhateverTheSetting()
    {
        LogLevelSwitch testLevel = new(LogLevel.Debug);
        DebugLoggingObserver observer = new DebugLoggingObserver(testLevel, fixedLevel: true, _log);

        observer.ApplySettings(new AppSettings());
        observer.UpdateSettings(new AppSettings { DebugLogging = true });
        observer.ReportMonitoringFailed();

        Assert.Equal(LogLevel.Debug, testLevel.MinimumLevel);
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public void MonitoringFailureKeepsTheLevel()
    {
        DebugLoggingObserver observer = new DebugLoggingObserver(_level, fixedLevel: false, _log);
        observer.ApplySettings(new AppSettings { DebugLogging = true });

        observer.ReportMonitoringFailed();

        Assert.Equal(LogLevel.Debug, _level.MinimumLevel);
    }

    private sealed class RecordingLogger : ILogger<DebugLoggingObserver>
    {
        public List<string> Messages { get; } = [];

        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
            Messages.Add(formatter(state, exception));
        }
    }
}
