using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Logging;

/// <summary>
/// Detailed logging (T-047): the loaded settings and every saved change set the minimum level of the file log, without a
/// restart. Until the settings are loaded, and always in a second start, the log stays at <see cref="LogLevel.Information"/>;
/// the test mode (T-044) stays at <see cref="LogLevel.Debug"/> whatever the setting says.
/// </summary>
internal sealed partial class DebugLoggingObserver : IMonitoringObserver
{
    private readonly LogLevelSwitch _level;
    private readonly bool _fixedLevel;
    private readonly ILogger<DebugLoggingObserver> _logger;
    private readonly Lock _gate = new();

    /// <param name="level">The minimum level of the file log of the process.</param>
    /// <param name="fixedLevel">The test mode: the level never changes.</param>
    public DebugLoggingObserver(LogLevelSwitch level, bool fixedLevel, ILogger<DebugLoggingObserver> logger)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(logger);
        _level = level;
        _fixedLevel = fixedLevel;
        _logger = logger;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_fixedLevel)
        {
            return;
        }

        LogLevel wanted = settings.DebugLogging ? LogLevel.Debug : LogLevel.Information;
        lock (_gate)
        {
            if (_level.MinimumLevel == wanted)
            {
                return;
            }

            _level.MinimumLevel = wanted;
        }

        // Information: written at both levels, so the log shows where its detail changes.
        if (settings.DebugLogging)
        {
            LogDebugLoggingOn();
        }
        else
        {
            LogDebugLoggingOff();
        }
    }

    /// <summary>The level stays as it is.</summary>
    public void ReportMonitoringFailed()
    {
    }

    public void UpdateSettings(AppSettings settings) => ApplySettings(settings);

    [LoggerMessage(Level = LogLevel.Information, Message = "Detailed logging (Debug) turned on.")]
    private partial void LogDebugLoggingOn();

    [LoggerMessage(Level = LogLevel.Information, Message = "Detailed logging (Debug) turned off.")]
    private partial void LogDebugLoggingOff();
}
