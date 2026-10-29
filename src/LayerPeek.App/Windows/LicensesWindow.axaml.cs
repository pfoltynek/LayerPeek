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
        MacShortcuts.HandleCloseKeys(this, () => UsesMacShortcuts, Close, Close);
    }

    /// <summary>
    /// The macOS shortcuts (T-100): ⌘W closes the window, ⌘. is Close like Esc. Default: on macOS only; tests set it.
    /// </summary>
    internal bool UsesMacShortcuts { get; set; } = MacShortcuts.IsPlatformDefault;

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
