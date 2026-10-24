using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LayerPeek.App.Windows;

/// <summary>Third-party licences (T-014), opened from the About window and owned by it.</summary>
public sealed partial class LicensesWindow : Window
{
    public LicensesWindow()
    {
        InitializeComponent();
        // DataContext (LicensesViewModel) is set by the creator.
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
