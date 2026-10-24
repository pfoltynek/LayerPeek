using Microsoft.Extensions.Logging;

using LayerPeek.App.Composition;

namespace LayerPeek.App.Logging;

/// <summary>
/// The logging of the process (T-024): one logger factory writing into the log folder of the environment, created first in
/// <c>Program.Main</c> and disposed there last, so that the second start and a session end of the OS still get their
/// records into the file. Nothing in it throws: without a log folder the app simply logs nowhere.
/// </summary>
internal sealed class AppLogging : IDisposable
{
    private readonly FileLogProvider? _file;

    private AppLogging(FileLogProvider? file, LogLevelSwitch level, string? directory)
    {
        _file = file;
        Level = level;
        Directory = directory;
        // The minimum level is decided by the switch alone, per call (T-047 changes it while the app runs).
        Factory = new LoggerFactory(file is null ? [] : [file], new LoggerFilterOptions { MinLevel = LogLevel.Trace });
    }

    /// <summary>Creates the loggers of the app; owned by this object (callers never dispose it).</summary>
    public ILoggerFactory Factory { get; }

    /// <summary>Minimum level of the file: <see cref="LogLevel.Information"/>, <see cref="LogLevel.Debug"/> in the test mode.</summary>
    public LogLevelSwitch Level { get; }

    /// <summary>The log folder, or <see langword="null"/> when the OS has none (then nothing is logged).</summary>
    public string? Directory { get; }

    /// <summary>The logging of <paramref name="environment"/>: its log folder, the test mode (T-044) at <see cref="LogLevel.Debug"/>.</summary>
    public static AppLogging Create(AppEnvironment environment, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        LogLevelSwitch level = new(environment.IsIsolated ? LogLevel.Debug : LogLevel.Information);
        try
        {
            string directory = environment.LogDirectory();
            return new AppLogging(new FileLogProvider(new LogFile(directory), level, time), level, directory);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // No per-user folder or no mutex for the file: the app works the same, only without a log.
            return Disabled();
        }
    }

    /// <summary>Logging that writes nowhere (designer, tests).</summary>
    public static AppLogging Disabled() => new(null, new LogLevelSwitch(LogLevel.None), null);

    /// <summary>The primary instance rotates the files; a second start only appends (T-043).</summary>
    public void EnableRotation() => _file?.EnableRotation();

    /// <summary>Waits at most <paramref name="timeout"/> until everything logged so far is in the file. Never throws.</summary>
    public bool Flush(TimeSpan timeout) => _file?.Flush(timeout) ?? true;

    /// <summary>
    /// Writes what is queued and stops logging; loggers stay usable and drop their records. The factory itself stays
    /// usable too (it holds nothing but the provider, which it does not own): work still running while the process ends
    /// may create loggers without an <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void Dispose() => _file?.Dispose();
}
