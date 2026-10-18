using Avalonia.Controls;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>Thin adapter of an Avalonia <see cref="Window"/> for <see cref="AuxiliaryWindowSlot"/>.</summary>
internal sealed class AvaloniaAuxiliaryWindow : IAuxiliaryWindow
{
    private readonly Window _window;

    public AvaloniaAuxiliaryWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _window.Closed += OnWindowClosed;
    }

    public event EventHandler? Closed;

    public void Show() => _window.Show();

    public void Activate() => _window.Activate();

    public void Close() => _window.Close();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _window.Closed -= OnWindowClosed;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
