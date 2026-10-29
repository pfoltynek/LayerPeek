using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// "Open at Login" on macOS (T-019, user decision 2026-10-29): the app's Login Item (<see cref="ILoginItemService"/>),
/// switched by Save in the settings window like the Startup shortcut on Windows. Off by default.
/// </summary>
/// <remarks>
/// The item counts as on only when macOS will really open the app (<see cref="LoginItemStatus.Enabled"/>). An item the user
/// switched off in System Settings (<see cref="LoginItemStatus.RequiresApproval"/>) counts as off; switching it on then
/// reports a failure, because only the user can allow it again there (the settings window shows "could not be changed").
/// </remarks>
internal sealed partial class LoginItemAutostart : IAutostart
{
    private readonly ILoginItemService _service;
    private readonly ILogger _logger;

    public LoginItemAutostart(ILoginItemService service, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(logger);
        _service = service;
        _logger = logger;
    }

    /// <summary>The Login Item behind it (composition tests).</summary>
    internal ILoginItemService Service => _service;

    public bool IsEnabled()
    {
        try
        {
            return _service.Status() == LoginItemStatus.Enabled;
        }
        catch (Exception ex)
        {
            LogUnreadable(ex);
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        LoginItemStatus status = _service.Status();
        if (enabled)
        {
            if (status != LoginItemStatus.Enabled)
            {
                if (status != LoginItemStatus.RequiresApproval)
                {
                    _service.Register();
                }

                status = _service.Status();
                if (status != LoginItemStatus.Enabled)
                {
                    // Registered, but not allowed: the user switched it off in System Settings, only they can allow it.
                    LogNeedsApproval(status);
                    throw new LoginItemException($"The Login Item is registered, but macOS reports it as {status}; allow LayerPeek in System Settings > General > Login Items.");
                }
            }
        }
        else if (status is LoginItemStatus.Enabled or LoginItemStatus.RequiresApproval)
        {
            _service.Unregister();
        }

        LogSwitched(enabled);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The state of the Login Item could not be read.")]
    private partial void LogUnreadable(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Login Item is not allowed (status {Status}); it can be allowed in System Settings > General > Login Items.")]
    private partial void LogNeedsApproval(LoginItemStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Open at Login switched on: {Enabled}.")]
    private partial void LogSwitched(bool enabled);
}
