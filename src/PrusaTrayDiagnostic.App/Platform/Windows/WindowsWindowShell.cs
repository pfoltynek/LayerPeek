using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>
/// Windows (T-043): auxiliary windows share the app's AppUserModelID and cannot be pinned (their jump list offers only
/// "Close window"); a window that could not take the foreground flashes its taskbar button instead.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsWindowShell : IWindowShell
{
    private readonly ILogger<WindowsWindowShell> _logger;
    private readonly string _appUserModelId;

    /// <param name="appUserModelId">The id of the windows: <see cref="WindowsAppIdentity.AppUserModelId"/>, or the test id (T-044).</param>
    public WindowsWindowShell(ILogger<WindowsWindowShell> logger, string appUserModelId = WindowsAppIdentity.AppUserModelId)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        _appUserModelId = appUserModelId;
    }

    public void Prepare(nint handle)
    {
        if (handle == 0)
        {
            return;
        }

        try
        {
            TaskbarWindowProperties.ApplyToWindow(handle, _appUserModelId);
        }
        catch (Exception ex)
        {
            // Only the jump list would offer pinning again; the window works.
            LogPrepareFailed(ex);
        }
    }

    public void Release(nint handle)
    {
        if (handle == 0)
        {
            return;
        }

        try
        {
            TaskbarWindowProperties.ClearFromWindow(handle);
        }
        catch (Exception ex)
        {
            // E.g. the window is already destroyed: the shell released its store with it.
            LogReleaseFailed(ex);
        }
    }

    public void RequestAttention(nint handle)
    {
        if (handle == 0)
        {
            return;
        }

        try
        {
            if (!WindowShellNative.IsForeground(handle))
            {
                WindowShellNative.Flash(handle);
            }
        }
        catch (Exception ex)
        {
            LogAttentionFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Setting the taskbar properties of a window failed.")]
    private partial void LogPrepareFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removing the taskbar properties of a closed window failed.")]
    private partial void LogReleaseFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Flashing the taskbar button of a window failed.")]
    private partial void LogAttentionFailed(Exception exception);
}
