namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>An open auxiliary window as <see cref="RecentWindows"/> needs it (an Avalonia window in the app, a fake in tests).</summary>
internal interface IRecentWindow
{
    /// <summary>The window became the active one (the user switched to it).</summary>
    event EventHandler? Activated;

    event EventHandler? Closed;

    /// <summary>Restores the window from minimized, activates it, and draws attention to it if it cannot be activated.</summary>
    void BringToFront();
}

/// <summary>
/// The open auxiliary windows (settings, diagnostics, camera windows of all printers, About, licences) ordered by last
/// use (T-043): a window counts as used when it opens and whenever it is activated; a closed window drops out. A window
/// opened without the focus (T-040: Apply opens the camera window) counts only once it is activated. UI thread only.
/// </summary>
internal sealed class RecentWindows
{
    // Most recently used first.
    private readonly List<IRecentWindow> _windows = [];

    public int Count => _windows.Count;

    /// <summary>The most recently used open window, or <see langword="null"/> when none is open.</summary>
    public IRecentWindow? MostRecent => _windows.Count > 0 ? _windows[0] : null;

    /// <summary>
    /// A window opened: it is the most recently used one until another is used; with <paramref name="used"/>
    /// <see langword="false"/> (shown without the focus) the least recently used one until it is activated. Tracking twice
    /// changes nothing.
    /// </summary>
    public void Track(IRecentWindow window, bool used = true)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_windows.Contains(window))
        {
            return;
        }

        _windows.Insert(used ? 0 : _windows.Count, window);
        window.Activated += OnActivated;
        window.Closed += OnClosed;
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (sender is IRecentWindow window && _windows.Remove(window))
        {
            _windows.Insert(0, window);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is IRecentWindow window)
        {
            window.Activated -= OnActivated;
            window.Closed -= OnClosed;
            _windows.Remove(window);
        }
    }
}

/// <summary>
/// What the running app does when it is started again (T-043, decided 2026-10-20): the most recently used open
/// auxiliary window comes to the front; without any, the settings open. UI thread only.
/// </summary>
internal sealed class InstanceActivation
{
    private readonly RecentWindows _windows;
    private readonly Action _showSettings;

    public InstanceActivation(RecentWindows windows, Action showSettings)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(showSettings);
        _windows = windows;
        _showSettings = showSettings;
    }

    public void Activate()
    {
        if (_windows.MostRecent is { } window)
        {
            window.BringToFront();
        }
        else
        {
            _showSettings();
        }
    }
}
