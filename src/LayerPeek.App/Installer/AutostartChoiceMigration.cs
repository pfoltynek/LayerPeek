using Microsoft.Extensions.Logging;

using LayerPeek.App.Platform;

namespace LayerPeek.App.Installer;

/// <summary>
/// One-time migration of the start after logon (T-201, user decision 2026-11-12): before T-201 the settings stored no
/// choice, so an autostart the user switched off in 0.x would come back with the first reinstall (<c>winget upgrade</c>,
/// <see cref="InstallerHooks.AfterInstall"/>). At the start of the installed app on Windows with the production identity
/// (<see cref="Composition.AppServices.CreateAutostartChoiceMigration"/>): while no choice is stored and the autostart is
/// definitely off (no shortcut, <see cref="IAutostartState"/>), "off" is stored once. An autostart that is on, or whose state
/// cannot be told, stores nothing (a reinstall switches it on, as the user had it; an unknown state is asked again at the
/// next start). The shortcut is never touched; every failure only goes to the log (the next start tries again).
/// </summary>
internal sealed partial class AutostartChoiceMigration
{
    private readonly IAutostartState _autostart;
    private readonly IAutostartChoiceStore _choices;
    private readonly ILogger<AutostartChoiceMigration> _logger;

    public AutostartChoiceMigration(IAutostartState autostart, IAutostartChoiceStore choices, ILogger<AutostartChoiceMigration> logger)
    {
        ArgumentNullException.ThrowIfNull(autostart);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(logger);
        _autostart = autostart;
        _choices = choices;
        _logger = logger;
    }

    /// <summary>Runs the migration; off the UI thread (it reads a file and the shortcut). Never throws.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            // The cheap check first: once a choice is stored (every start after the first), nothing else is read.
            // Only "definitely off" stores anything: an unknown state (an unreadable shortcut, one of another copy) waits
            // for the next start.
            if (_choices.Read() is not null || _autostart.QueryState() != AutostartState.Off)
            {
                return;
            }

            if (await _choices.StoreIfUnsetAsync(false, cancellationToken).ConfigureAwait(false))
            {
                LogStoredOff();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Quitting.
        }
        catch (Exception ex)
        {
            LogFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Autostart is off and no choice was stored yet: \"off\" is stored, so that a reinstall keeps it off.")]
    private partial void LogStoredOff();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The autostart choice could not be checked or stored; the next start tries again.")]
    private partial void LogFailed(Exception exception);
}
