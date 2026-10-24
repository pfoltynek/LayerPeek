using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Tests.Logging;

/// <summary>Logs a ready text, like a <c>[LoggerMessage]</c> method of the app does.</summary>
internal static class TestLogging
{
    public static void Write(this ILogger logger, LogLevel level, string text, Exception? exception = null) =>
        logger.Log(level, default, text, exception, static (state, _) => state);
}
