using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Settings;

/// <summary>Log messages of the settings window view models. Never log addresses or credentials here.</summary>
internal static partial class SettingsLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The camera preview could not be drawn ({ExceptionType}: {ExceptionMessage}); the test result is shown without it.")]
    public static partial void CameraPreviewNotDrawn(ILogger logger, Exception exception, string exceptionType, string exceptionMessage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The start after logon could not be read; it is shown as off.")]
    public static partial void AutostartUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The start after logon could not be changed (wanted on: {Enabled}).")]
    public static partial void AutostartNotChanged(ILogger logger, Exception exception, bool enabled);
}
