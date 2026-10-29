using Avalonia.Controls;

using LayerPeek.App.Localization;

namespace LayerPeek.App.Windows;

/// <summary>Thin adapter of an Avalonia <see cref="Window"/> for <see cref="AuxiliaryWindowSlot"/>.</summary>
internal sealed class AvaloniaAuxiliaryWindow : IAuxiliaryWindow
{
    private readonly Window _window;
    private readonly Action? _bringAppToFront;
    private IDisposable? _tracking;

    /// <param name="track">
    /// Tracks the window before it is shown (T-043, <see cref="AuxiliaryWindowTracker.Track"/>); the result untracks it
    /// again when showing fails. Closing the window untracks it on its own.
    /// </param>
    /// <param name="bringAppToFront">
    /// Activates the app before the window (macOS, T-019: an agent app without a Dock icon is not active when its menu bar
    /// extra opens a window, so the window would stay behind the active app); <see langword="null"/>: the window alone.
    /// </param>
    public AvaloniaAuxiliaryWindow(Window window, Func<Window, IDisposable>? track = null, Action? bringAppToFront = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _bringAppToFront = bringAppToFront;
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
        _window.Activate();
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
