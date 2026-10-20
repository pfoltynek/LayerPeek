using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>
/// Small About window; closing it never quits the app (<c>ShutdownMode.OnExplicitShutdown</c>). It opens at most one
/// third-party licences window, owned by it (closed together with it).
/// </summary>
public sealed partial class AboutWindow : Window
{
    private AboutViewModel? _viewModel;
    private LicensesWindow? _licenses;

    public AboutWindow()
    {
        InitializeComponent();
        // DataContext (AboutViewModel) is set by the creator.
    }

    /// <summary>Called with the licences window before it is shown (T-043: taskbar properties, recently used windows).</summary>
    internal Action<Window>? ChildWindowCreated { get; init; }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LicensesRequested -= OnLicensesRequested;
        }

        _viewModel = DataContext as AboutViewModel;
        if (_viewModel is not null)
        {
            _viewModel.LicensesRequested += OnLicensesRequested;
        }

        base.OnDataContextChanged(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LicensesRequested -= OnLicensesRequested;
            _viewModel = null;
        }

        base.OnClosed(e);
    }

    private void OnLicensesRequested(object? sender, EventArgs e)
    {
        if (_licenses is not null)
        {
            _licenses.Activate();
            return;
        }

        if (_viewModel is null)
        {
            return;
        }

        Licenses.LicensesViewModel viewModel = _viewModel.CreateLicensesViewModel();
        _licenses = new LicensesWindow { DataContext = viewModel };
        _licenses.Closed += (_, _) => _licenses = null;
        ChildWindowCreated?.Invoke(_licenses);
        _licenses.Show(this);
        // Reads the FFmpeg version off the UI thread; never throws.
        _ = viewModel.LoadAsync();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
