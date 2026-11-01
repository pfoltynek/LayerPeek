using Avalonia.Controls;

using LayerPeek.App.Platform.Windows;

using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Windows;

/// <summary>
/// Activates an auxiliary window that the user asked to come to the front (T-119): <see cref="AvaloniaAuxiliaryWindow.Activate"/>
/// (a tray menu item of an open window, "Bring camera to front") and <see cref="AvaloniaRecentWindow.BringToFront"/>
/// (a second start of the app). Minimized windows are restored by the callers before. Never throws; UI thread.
/// </summary>
internal interface IWindowActivator
{
    void Activate(Window window);
}

/// <summary>Avalonia's own <see cref="Window.Activate"/>: macOS (with the app activation of T-019 around it) and other OSes.</summary>
internal sealed class AvaloniaWindowActivator : IWindowActivator
{
    public static readonly AvaloniaWindowActivator Instance = new();

    private AvaloniaWindowActivator()
    {
    }

    public void Activate(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Activate();
    }
}

/// <summary>
/// Windows (T-119): <see cref="ForegroundRaiser"/> instead of Avalonia's plain <c>SetForegroundWindow</c>, which the
/// foreground lock refuses while another app is in front. <see cref="Window.Topmost"/> is never touched. A window without
/// a native handle (not shown) or a failure of the native calls falls back to <see cref="Window.Activate"/>.
/// </summary>
internal sealed partial class WindowsWindowActivator : IWindowActivator
{
    private readonly ILogger<WindowsWindowActivator> _logger;
    private readonly IForegroundWindowApi _api;
    private readonly Func<Window, nint> _handleOf;

    /// <param name="api">The window manager; default <see cref="ForegroundNative"/> (a fake in tests).</param>
    /// <param name="handleOf">The HWND of a window; default <see cref="TopLevel.TryGetPlatformHandle"/>.</param>
    public WindowsWindowActivator(ILogger<WindowsWindowActivator> logger, IForegroundWindowApi? api = null, Func<Window, nint>? handleOf = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _api = api ?? (OperatingSystem.IsWindows() ? ForegroundNative.Instance : throw new PlatformNotSupportedException("The native window manager exists only on Windows."));
        _handleOf = handleOf ?? DefaultHandleOf;
    }

    /// <summary>
    /// The activator of the current OS: <see cref="WindowsWindowActivator"/> on Windows, otherwise
    /// <see cref="AvaloniaWindowActivator"/> (macOS keeps <see cref="Window.Activate"/> and its app activation, T-019).
    /// </summary>
    public static IWindowActivator ForCurrentPlatform(Func<ILogger<WindowsWindowActivator>> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return OperatingSystem.IsWindows() ? new WindowsWindowActivator(logger()) : AvaloniaWindowActivator.Instance;
    }

    public void Activate(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        nint handle = _handleOf(window);
        if (handle == 0)
        {
            window.Activate();
            return;
        }

        try
        {
            if (ForegroundRaiser.Raise(_api, handle) == ForegroundRaiseResult.RaisedOnly)
            {
                // The taskbar button flashes (the shell, T-043) where the caller asks for it.
                LogForegroundRefused();
            }
        }
        catch (Exception ex)
        {
            LogRaiseFailed(ex);
            window.Activate();
        }
    }

    private static nint DefaultHandleOf(Window window) => window.TryGetPlatformHandle()?.Handle ?? 0;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Windows refused the foreground to a window brought to the front; it was moved to the top of the window order only.")]
    private partial void LogForegroundRefused();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Bringing a window to the front through the window manager failed; activated through Avalonia.")]
    private partial void LogRaiseFailed(Exception exception);
}
