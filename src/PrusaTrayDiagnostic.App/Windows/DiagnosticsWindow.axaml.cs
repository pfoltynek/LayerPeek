using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>
/// Diagnostics of the configured printers (T-011). Closing it never quits the app (<c>ShutdownMode.OnExplicitShutdown</c>);
/// its view model is disposed by <see cref="AuxiliaryWindowSlot"/> when it closes, and the chart unsubscribes when detached.
/// </summary>
public sealed partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        InitializeComponent();
        // DataContext (DiagnosticsViewModel) is set by the creator.
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
