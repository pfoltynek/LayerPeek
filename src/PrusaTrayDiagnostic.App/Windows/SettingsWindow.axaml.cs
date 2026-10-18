using Avalonia.Controls;

using PrusaTrayDiagnostic.App.Settings;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>
/// Settings window (T-015). Closing it (Save, Cancel or the title bar button) never quits the app
/// (<c>ShutdownMode.OnExplicitShutdown</c>); unsaved edits are discarded. Its view model is disposed by
/// <see cref="AuxiliaryWindowSlot"/> when it closes.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private SettingsViewModel? _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
        // DataContext (SettingsViewModel) is set by the creator.
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        _viewModel = DataContext as SettingsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested += OnCloseRequested;
        }

        base.OnDataContextChanged(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        // The title bar button (or Alt+F4) while saving: the outcome must stay visible. Quitting the app closes the window
        // programmatically and is never blocked; it awaits the save in the settings store instead.
        if (!e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing && _viewModel is { BlocksClosing: true })
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
            _viewModel = null;
        }

        base.OnClosed(e);
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
