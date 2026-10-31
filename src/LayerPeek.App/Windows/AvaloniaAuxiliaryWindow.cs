using Avalonia.Controls;

using LayerPeek.App.Localization;

namespace LayerPeek.App.Windows;

/// <summary>Thin adapter of an Avalonia <see cref="Window"/> for <see cref="AuxiliaryWindowSlot"/>.</summary>
internal sealed class AvaloniaAuxiliaryWindow : IAuxiliaryWindow
{
    private readonly Window _window;
    private readonly Action? _bringAppToFront;
    private readonly IWindowActivator _activator;
    private IDisposable? _tracking;

    /// <param name="track">
    /// Tracks the window before it is shown (T-043, <see cref="AuxiliaryWindowTracker.Track"/>); the result untracks it
    /// again when showing fails. Closing the window untracks it on its own.
    /// </param>
    /// <param name="bringAppToFront">
    /// Activates the app before the window (macOS, T-019: an agent app without a Dock icon is not active when its menu bar
    /// extra opens a window, so the window would stay behind the active app); <see langword="null"/>: the window alone.
    /// </param>
    /// <param name="activator">
    /// Activates the window (T-119: on Windows past the foreground lock, <see cref="WindowsWindowActivator"/>);
    /// <see langword="null"/>: <see cref="Window.Activate"/>.
    /// </param>
    public AvaloniaAuxiliaryWindow(Window window, Func<Window, IDisposable>? track = null, Action? bringAppToFront = null, IWindowActivator? activator = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _bringAppToFront = bringAppToFront;
        _activator = activator ?? AvaloniaWindowActivator.Instance;
        _window.Closed += OnWindowClosed;
        _tracking = track?.Invoke(window);
    }

    public event EventHandler? Closed;

    public void Show()
    {
        try
        {
            _window.Show();
        }
        catch
        {
            // Never shown: a second start must not try to bring it to the front.
            _tracking?.Dispose();
            _tracking = null;
            throw;
        }
    }

    public void Activate()
    {
        _bringAppToFront?.Invoke();
        _activator.Activate(_window);
    }

    public void Close() => _window.Close();

    public void RefreshTexts() => (_window.DataContext as ILanguageAware)?.RefreshTexts();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _window.Closed -= OnWindowClosed;
        _tracking = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
