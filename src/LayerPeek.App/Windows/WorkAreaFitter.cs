using Avalonia;
using Avalonia.Controls;

namespace LayerPeek.App.Windows;

/// <summary>
/// Opens an auxiliary window (settings T-033, diagnostics T-041) at its preferred size, shrunk to the work area of its
/// screen with <see cref="AuxiliaryWindowPlacement"/>, and remembers the size and maximized state the user gives it
/// (T-094, <see cref="Remember"/>). Create it in the window's constructor after <c>InitializeComponent</c> (the XAML
/// <c>Width</c> / <c>Height</c> are the designed size) and call <see cref="CorrectAfterOpening"/> from <c>OnOpened</c>.
/// UI thread only.
/// </summary>
internal sealed class WorkAreaFitter
{
    private readonly Window _window;
    private AuxiliaryWindowSizeMemory _memory = new(AuxiliaryWindowState.Default);
    private IAuxiliaryWindowStateStore? _store;
    private string? _key;

    /// <summary>Remembers the designed size and fits the window before it is shown.</summary>
    public WorkAreaFitter(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        DesignedSize = new Size(window.Width, window.Height);
        PreferredSize = DesignedSize;
        FitBeforeShowing();
        _window.Resized += OnResized;
        _window.PropertyChanged += OnPropertyChanged;
        _window.Closed += OnClosed;
    }

    /// <summary>Client size from the XAML, before any fitting.</summary>
    public Size DesignedSize { get; }

    /// <summary>The size the window wants before fitting: the remembered one (T-094), else <see cref="DesignedSize"/>.</summary>
    public Size PreferredSize { get; private set; }

    /// <summary>What would be remembered if the window closed now.</summary>
    public AuxiliaryWindowState CurrentState => _memory.Current;

    private Size MinSize => new(_window.MinWidth, _window.MinHeight);

    /// <summary>
    /// Before the window is shown (T-094): opens it with the size and maximized state remembered under
    /// <paramref name="key"/> in <paramref name="store"/> (the size fitted to the work area like the designed one), and
    /// remembers them there again when the window closes, however it closes (also "Cancel" in the settings).
    /// </summary>
    public void Remember(IAuxiliaryWindowStateStore store, string key)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _store = store;
        _key = key;
        AuxiliaryWindowState remembered = store.Get(key);
        _memory = new AuxiliaryWindowSizeMemory(remembered);
        PreferredSize = AuxiliaryWindowPlacement.PreferredClientSize(remembered.ClientSize, DesignedSize, MinSize);
        FitBeforeShowing();
        if (remembered.Maximized)
        {
            // Maximized on the monitor it opens on; restoring returns to the fitted size above.
            _window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// After opening, with the real screen and the measured frame: only when the window as shown does not fit (a thicker
    /// frame than assumed, another screen) it shrinks and is centred again; otherwise nothing moves.
    /// </summary>
    public void CorrectAfterOpening()
    {
        if (_window.WindowState != WindowState.Normal || (_window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary) is not { } screen)
        {
            return;
        }

        if (AuxiliaryWindowPlacement.CorrectionAfterOpening(
                new Size(_window.Width, _window.Height),
                PreferredSize,
                MinSize,
                screen.WorkingArea,
                screen.Scaling,
                AuxiliaryWindowPlacement.ChromeOf(_window.FrameSize, _window.ClientSize)) is { } bounds)
        {
            SetSize(bounds.ClientSize);
            _window.Position = bounds.Position;
        }
    }

    /// <summary>
    /// Before the window is shown: a work area smaller than the window (high scaling, a small screen, a large taskbar)
    /// shrinks it, so that it appears already fitted and <c>CenterScreen</c> centres the fitted size (no visible jump).
    /// The screen is the one <c>CenterScreen</c> picks for a window without an owner (the one under the window's
    /// position, else the primary); the frame is not known yet, so the assumed title bar is used.
    /// </summary>
    private void FitBeforeShowing()
    {
        if (_window.Screens.ScreenCount == 0)
        {
            SetSize(PreferredSize);
            return;
        }

        Avalonia.Platform.Screen? screen = _window.Screens.ScreenFromPoint(_window.Position) ?? _window.Screens.Primary;
        SetSize(AuxiliaryWindowPlacement.SizeBeforeShowing(PreferredSize, MinSize, screen?.WorkingArea, screen?.Scaling ?? 1.0));
    }

    /// <summary>A size the app chose, not the user: never remembered as the user's preference.</summary>
    private void SetSize(Size size)
    {
        _memory.OnAutomaticSize(size);
        _window.Width = size.Width;
        _window.Height = size.Height;
    }

    /// <summary>
    /// The platform reported a new client size of the window and why. Called by the window's <c>Resized</c> event; tests
    /// call it directly to report a resize by the user, which a headless window cannot produce.
    /// </summary>
    internal void OnResized(Size clientSize, WindowResizeReason reason) =>
        _memory.OnResized(clientSize, reason is WindowResizeReason.User or WindowResizeReason.Unspecified, _window.WindowState == WindowState.Normal);

    private void OnResized(object? sender, WindowResizedEventArgs e) => OnResized(e.ClientSize, e.Reason);

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            _memory.OnStateChanged(_window.WindowState);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _window.Resized -= OnResized;
        _window.PropertyChanged -= OnPropertyChanged;
        _window.Closed -= OnClosed;
        // Unchanged since opening: nothing to remember. This also keeps a state the store read only after the window
        // opened (a slow windows.json at startup) from being replaced by the defaults the window opened with.
        if (_store is not null && _key is not null && _memory.Current != _memory.Remembered)
        {
            _store.Set(_key, _memory.Current);
        }
    }
}
