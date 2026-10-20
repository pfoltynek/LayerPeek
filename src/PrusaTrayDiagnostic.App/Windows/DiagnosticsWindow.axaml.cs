using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>
/// Diagnostics of the configured printers (T-011). Closing it never quits the app (<c>ShutdownMode.OnExplicitShutdown</c>);
/// its view model is disposed by <see cref="AuxiliaryWindowSlot"/> when it closes, and the chart unsubscribes when detached.
/// Opens at the size of the settings window, shrunk to the work area of its screen like it (T-041,
/// <see cref="AuxiliaryWindowPlacement"/>).
/// </summary>
public sealed partial class DiagnosticsWindow : Window
{
    private readonly WorkAreaFitter _fitter;

    public DiagnosticsWindow()
    {
        InitializeComponent();
        _fitter = new WorkAreaFitter(this);
        // DataContext (DiagnosticsViewModel) is set by the creator.
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _fitter.CorrectAfterOpening();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
