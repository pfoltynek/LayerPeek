using Avalonia;
using Avalonia.Controls;

using PrusaTrayDiagnostic.App.Platform;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>
/// Every auxiliary window passes through <see cref="Track"/> right after it is created, before it is shown (T-043): the
/// OS shell gets its properties (Windows: only "Close window" in its jump list) and the window joins
/// <see cref="Recent"/>, which a second start of the app uses. UI thread only.
/// </summary>
internal sealed class AuxiliaryWindowTracker
{
    private readonly IWindowShell _shell;

    public AuxiliaryWindowTracker(IWindowShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    public RecentWindows Recent { get; } = new();

    /// <summary>Call before the window is shown (its native handle exists from its creation).</summary>
    public void Track(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Recent.Track(new AvaloniaRecentWindow(window, _shell));
    }
}

/// <summary>Adapter of an Avalonia <see cref="Window"/> for <see cref="RecentWindows"/>.</summary>
internal sealed class AvaloniaRecentWindow : IRecentWindow
{
    private readonly Window _window;
    private readonly IWindowShell _shell;
    private readonly nint _handle;

    // The state a minimized window returns to (e.g. full screen of a camera window).
    private WindowState _restoreState;

    public AvaloniaRecentWindow(Window window, IWindowShell shell)
    {
        _window = window;
        _shell = shell;
        _restoreState = window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState;
        // Captured now: the handle is gone from the window once it closed, but the shell must still be told.
        _handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        _shell.Prepare(_handle);
        window.Activated += OnActivated;
        window.Closed += OnClosed;
        window.PropertyChanged += OnPropertyChanged;
    }

    public event EventHandler? Activated;

    public event EventHandler? Closed;

    public void BringToFront()
    {
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = _restoreState;
        }

        _window.Activate();
        _shell.RequestAttention(_handle);
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && e.GetNewValue<WindowState>() is var state && state != WindowState.Minimized)
        {
            _restoreState = state;
        }
    }

    private void OnActivated(object? sender, EventArgs e) => Activated?.Invoke(this, EventArgs.Empty);

    private void OnClosed(object? sender, EventArgs e)
    {
        _window.Activated -= OnActivated;
        _window.Closed -= OnClosed;
        _window.PropertyChanged -= OnPropertyChanged;
        _shell.Release(_handle);
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
