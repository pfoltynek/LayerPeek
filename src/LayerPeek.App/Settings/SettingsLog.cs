using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Settings;

/// <summary>Log messages of the settings window view models. Never log addresses or credentials here.</summary>
internal static partial class SettingsLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The camera preview could not be drawn ({ExceptionType}: {ExceptionMessage}); the test result is shown without it.")]
    public static partial void CameraPreviewNotDrawn(ILogger logger, Exception exception, string exceptionType, string exceptionMessage);
}
