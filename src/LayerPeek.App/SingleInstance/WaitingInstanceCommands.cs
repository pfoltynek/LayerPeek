using Microsoft.Extensions.Logging;

using LayerPeek.App.Notifications;

namespace LayerPeek.App.SingleInstance;

/// <summary>
/// The provisional handler of <see cref="InstanceCommandQueue"/> while the services run before the UI (T-099, macOS waiting
/// for a display, and the hand-over until the app's handler is attached). A click on a notification opens the printer's web
/// right away, like a click that started the app (no UI needed). Everything else is kept for the UI (user decision
/// 2026-10-29): a later start of the app ("activate") does what it always does once the UI runs (coalesced: at most one
/// waits), and so does a click on the test notification (T-101), which opens the settings window. Debug test mode: "quit" ends the wait while it lasts; after the wait it goes to the UI, which quits. Runs on the
/// main thread (through the startup dispatcher).
/// </summary>
/// <param name="activateToast">Opens the printer of a notification's launch argument (waits for the services when needed).</param>
/// <param name="tryQuit">
/// Ends the wait like SIGTERM (test mode only); <see langword="false"/> when the wait is already over (the UI quits instead).
/// </param>
/// <param name="testCommands">Whether test commands are accepted (the isolated test mode of Debug builds, T-044).</param>
/// <param name="logger">The startup log.</param>
internal sealed partial class WaitingInstanceCommands(Func<string, Task> activateToast, Func<bool> tryQuit, bool testCommands, ILogger logger)
{
    private readonly Func<string, Task> _activateToast = activateToast ?? throw new ArgumentNullException(nameof(activateToast));
    private readonly Func<bool> _tryQuit = tryQuit ?? throw new ArgumentNullException(nameof(tryQuit));
    private readonly bool _testCommands = testCommands;
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <returns><see langword="true"/> when handled here; <see langword="false"/>: kept for the UI.</returns>
    public bool Handle(InstanceCommand command)
    {
        if (command.Kind == InstanceCommandKind.Toast && command.Argument is { } launchArgument && !ToastLaunchArgument.IsSettings(launchArgument))
        {
            LogToastWhileWaiting();
            _ = _activateToast(launchArgument);
            return true;
        }

#if DEBUG
        if (_testCommands && command.Kind == InstanceCommandKind.Quit && _tryQuit())
        {
            return true;
        }
#else
        _ = _testCommands;
        _ = _tryQuit;
#endif

        LogKeptForUi(command.Kind);
        return false;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A click on a notification while waiting for an active display: handled now (a printer's web, or the update).")]
    private partial void LogToastWhileWaiting();

    [LoggerMessage(Level = LogLevel.Information, Message = "The instance command {Kind} waits for the menu bar item (it starts once a display is active).")]
    private partial void LogKeptForUi(InstanceCommandKind kind);
}
