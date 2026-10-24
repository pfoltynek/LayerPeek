using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Logging;

/// <summary>
/// The minimum level of the file log, changeable while the app runs (T-047: detailed logging from the settings) without a
/// new logger factory. Read on every log call, so it is a single volatile field.
/// </summary>
internal sealed class LogLevelSwitch(LogLevel minimumLevel)
{
    private volatile int _minimumLevel = (int)minimumLevel;

    public LogLevel MinimumLevel
    {
        get => (LogLevel)_minimumLevel;
        set => _minimumLevel = (int)value;
    }

    /// <summary>Whether a message of <paramref name="level"/> is written (<see cref="LogLevel.None"/> never is).</summary>
    public bool IsEnabled(LogLevel level) => level != LogLevel.None && (int)level >= _minimumLevel;
}
