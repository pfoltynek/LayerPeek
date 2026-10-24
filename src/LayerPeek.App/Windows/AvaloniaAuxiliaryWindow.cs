using Avalonia.Controls;

using LayerPeek.App.Localization;

namespace LayerPeek.App.Windows;

/// <summary>Thin adapter of an Avalonia <see cref="Window"/> for <see cref="AuxiliaryWindowSlot"/>.</summary>
internal sealed class AvaloniaAuxiliaryWindow : IAuxiliaryWindow
{
    private readonly Window _window;
    private IDisposable? _tracking;

    /// <param name="track">
    /// Tracks the window before it is shown (T-043, <see cref="AuxiliaryWindowTracker.Track"/>); the result untracks it
    /// again when showing fails. Closing the window untracks it on its own.
    /// </param>
    public AvaloniaAuxiliaryWindow(Window window, Func<Window, IDisposable>? track = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
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

    public void Activate() => _window.Activate();

    public void Close() => _window.Close();

    public void RefreshTexts() => (_window.DataContext as ILanguageAware)?.RefreshTexts();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _window.Closed -= OnWindowClosed;
        _tracking = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
