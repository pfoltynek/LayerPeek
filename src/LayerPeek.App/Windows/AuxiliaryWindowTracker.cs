using Avalonia;
using Avalonia.Controls;

using LayerPeek.App.Platform;

namespace LayerPeek.App.Windows;

/// <summary>
/// Every auxiliary window passes through <see cref="Track"/> right after it is created, before it is shown (T-043): the
/// OS shell gets its properties (Windows: only "Close window" in its jump list) and the window joins
/// <see cref="Recent"/>, which a second start of the app uses; while it is open and not on top it counts for the Dock
/// icon of the app (<see cref="AppPresence"/>, macOS, T-102). UI thread only.
/// </summary>
internal sealed class AuxiliaryWindowTracker
{
    private readonly IWindowShell _shell;
    private readonly Func<Window, nint> _handleOf;
    private readonly AppPresence? _presence;

    /// <param name="handleOf">The native handle of a window; default: <see cref="TopLevel.TryGetPlatformHandle"/>.</param>
    /// <param name="presence">Counts the windows that are not on top (T-102); <see langword="null"/>: nothing is counted.</param>
    public AuxiliaryWindowTracker(IWindowShell shell, Func<Window, nint>? handleOf = null, AppPresence? presence = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        _handleOf = handleOf ?? DefaultHandleOf;
        _presence = presence;
    }

    public RecentWindows Recent { get; } = new();

    /// <summary>
    /// Call before the window is shown (its native handle exists from its creation), with <c>ShowActivated</c> already set:
    /// a window shown without activation counts as used only once it is activated (T-040).
    /// </summary>
    /// <returns>Untracks the window again, e.g. when showing it failed (closing it untracks it too).</returns>
    public IDisposable Track(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        // Counted before it is shown (T-102): on macOS the app is in the Dock when the window comes to the front.
        AvaloniaRecentWindow tracked = new(window, _shell, _handleOf(window), _presence);
        // Shown without the focus (T-040: Apply opens the camera window): used only once activated.
        Recent.Track(tracked, used: window.ShowActivated);
        return tracked;
    }

    private static nint DefaultHandleOf(Window window) => window.TryGetPlatformHandle()?.Handle ?? 0;
}

/// <summary>Adapter of an Avalonia <see cref="Window"/> for <see cref="RecentWindows"/>; disposing it untracks the window.</summary>
internal sealed class AvaloniaRecentWindow : IRecentWindow, IDisposable
{
    private readonly IWindowShell _shell;
    private readonly nint _handle;
    private readonly AppPresence? _presence;
    private bool _released;

    // The state a minimized window returns to (e.g. full screen of a camera window).
    private WindowState _restoreState;

    /// <param name="handle">
    /// Captured at creation: once the window closed it no longer reports its handle, but the shell must still be told.
    /// </param>
    /// <param name="presence">Counts the window while it is open and not on top (T-102); <see langword="null"/>: not counted.</param>
    public AvaloniaRecentWindow(Window window, IWindowShell shell, nint handle, AppPresence? presence = null)
    {
        Window = window;
        _shell = shell;
        _handle = handle;
        _presence = presence;
        _restoreState = RestoreStateOf(window);
        _shell.Prepare(_handle);
        _presence?.Set(this, window.Topmost);
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
        else if (e.Property == Window.TopmostProperty)
        {
            // A mode switch of a camera window (mini preview ↔ normal) or a saved "always on top" option (T-037).
            _presence?.Set(this, e.GetNewValue<bool>());
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
        _presence?.Remove(this);
        // RecentWindows drops it.
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
