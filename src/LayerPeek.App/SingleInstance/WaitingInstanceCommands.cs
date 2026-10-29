using Microsoft.Extensions.Logging;

namespace LayerPeek.App.SingleInstance;

/// <summary>
/// The provisional handler of <see cref="InstanceCommandQueue"/> while the services run before the UI (T-099, macOS waiting
/// for a display). A click on a notification opens the printer's web like a click that started the app (no UI needed).
/// A later start of the app ("activate") is ignored: there is nothing to show yet, and a settings window popping up
/// whenever the display wakes, maybe hours later, would surprise. Debug test mode: "quit" ends the wait; other test
/// commands are ignored. Runs on the main thread (through the startup dispatcher).
/// </summary>
/// <param name="activateToast">Opens the printer of a notification's launch argument (waits for the services when needed).</param>
/// <param name="quit">Ends the wait like SIGTERM (test mode only).</param>
/// <param name="testCommands">Whether test commands are accepted (the isolated test mode of Debug builds, T-044).</param>
/// <param name="logger">The startup log.</param>
internal sealed partial class WaitingInstanceCommands(Func<string, Task> activateToast, Action quit, bool testCommands, ILogger logger)
{
    private readonly Func<string, Task> _activateToast = activateToast ?? throw new ArgumentNullException(nameof(activateToast));
    private readonly Action _quit = quit ?? throw new ArgumentNullException(nameof(quit));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public void Handle(InstanceCommand command)
    {
        if (command.Kind == InstanceCommandKind.Toast && command.Argument is { } launchArgument)
        {
            LogToastWhileWaiting();
            _ = _activateToast(launchArgument);
            return;
        }

#if DEBUG
        if (testCommands && command.Kind == InstanceCommandKind.Quit)
        {
            _quit();
            return;
        }
#else
        _ = testCommands;
        _ = _quit;
#endif

        LogIgnored(command.Kind);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A click on a notification while waiting for an active display: opening the printer's web.")]
    private partial void LogToastWhileWaiting();

    [LoggerMessage(Level = LogLevel.Information, Message = "Ignored the instance command {Kind}: the menu bar item starts once a display is active.")]
    private partial void LogIgnored(InstanceCommandKind kind);
}
