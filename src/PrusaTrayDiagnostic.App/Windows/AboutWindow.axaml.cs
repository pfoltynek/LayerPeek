using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>Small About window; closing it never quits the app (<c>ShutdownMode.OnExplicitShutdown</c>).</summary>
public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        // DataContext (AboutViewModel) is set by the creator.
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
