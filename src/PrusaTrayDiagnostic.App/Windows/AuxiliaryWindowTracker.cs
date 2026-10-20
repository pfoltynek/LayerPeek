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
    private readonly Func<Window, nint> _handleOf;

    /// <param name="handleOf">The native handle of a window; default: <see cref="TopLevel.TryGetPlatformHandle"/>.</param>
    public AuxiliaryWindowTracker(IWindowShell shell, Func<Window, nint>? handleOf = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        _handleOf = handleOf ?? DefaultHandleOf;
    }

    public RecentWindows Recent { get; } = new();

    /// <summary>Call before the window is shown (its native handle exists from its creation).</summary>
    /// <returns>Untracks the window again, e.g. when showing it failed (closing it untracks it too).</returns>
    public IDisposable Track(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        AvaloniaRecentWindow tracked = new(window, _shell, _handleOf(window));
        Recent.Track(tracked);
        return tracked;
    }

    private static nint DefaultHandleOf(Window window) => window.TryGetPlatformHandle()?.Handle ?? 0;
}

/// <summary>Adapter of an Avalonia <see cref="Window"/> for <see cref="RecentWindows"/>; disposing it untracks the window.</summary>
internal sealed class AvaloniaRecentWindow : IRecentWindow, IDisposable
{
    private readonly IWindowShell _shell;
    private readonly nint _handle;
    private bool _released;

    // The state a minimized window returns to (e.g. full screen of a camera window).
    private WindowState _restoreState;

    /// <param name="handle">
    /// Captured at creation: once the window closed it no longer reports its handle, but the shell must still be told.
    /// </param>
    public AvaloniaRecentWindow(Window window, IWindowShell shell, nint handle)
    {
        Window = window;
        _shell = shell;
        _handle = handle;
        _restoreState = RestoreStateOf(window);
        _shell.Prepare(_handle);
        window.Activated += OnActivated;
        window.Closed += OnClosed;
        window.PropertyChanged += OnPropertyChanged;
    }

    public event EventHandler? Activated;

    public event EventHandler? Closed;

    public Window Window { get; }

    /// <summary>
    /// Restores the minimized owners of an owned window first (on Windows owned windows are hidden with their owner, e.g.
    /// the licences with the About window), then the window itself to the state before minimizing, and activates it.
    /// </summary>
    public void BringToFront()
    {
        List<Window> owners = [];
        for (Window? owner = Window.Owner as Window; owner is not null && !owners.Contains(owner); owner = owner.Owner as Window)
        {
            owners.Add(owner);
        }

        // The outermost owner first.
        for (int i = owners.Count - 1; i >= 0; i--)
        {
            if (owners[i].WindowState == WindowState.Minimized)
            {
                // Owners are plain dialogs (About): never maximized or full screen.
                owners[i].WindowState = WindowState.Normal;
            }
        }

        if (Window.WindowState == WindowState.Minimized)
        {
            Window.WindowState = _restoreState;
        }

        Window.Activate();
        _shell.RequestAttention(_handle);
    }

    /// <summary>The state the window returns to from minimized.</summary>
    internal WindowState RestoreState => _restoreState;

    /// <summary>Untracks the window: the shell properties are removed and it leaves <see cref="RecentWindows"/>.</summary>
    public void Dispose() => Release();

    private static WindowState RestoreStateOf(Window window) =>
        window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState;

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && e.GetNewValue<WindowState>() is var state && state != WindowState.Minimized)
        {
            _restoreState = state;
        }
    }

    private void OnActivated(object? sender, EventArgs e) => Activated?.Invoke(this, EventArgs.Empty);

    private void OnClosed(object? sender, EventArgs e) => Release();

    private void Release()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        Window.Activated -= OnActivated;
        Window.Closed -= OnClosed;
        Window.PropertyChanged -= OnPropertyChanged;
        _shell.Release(_handle);
        // RecentWindows drops it.
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
