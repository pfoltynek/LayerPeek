using Avalonia.Controls;
using Avalonia.Interactivity;

using PrusaTrayDiagnostic.App.Support;

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

    /// <summary>
    /// Tracks the licences window before it is shown (T-043: taskbar properties, recently used windows); the result
    /// untracks it when showing fails.
    /// </summary>
    internal Func<Window, IDisposable>? TrackChild { get; init; }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LicensesRequested -= OnLicensesRequested;
            _viewModel.BundlePicker = null;
            _viewModel.Clipboard = null;
        }

        _viewModel = DataContext as AboutViewModel;
        if (_viewModel is not null)
        {
            _viewModel.LicensesRequested += OnLicensesRequested;
            // The save dialog of the diagnostic bundle belongs to this window (T-048).
            _viewModel.BundlePicker = new StorageProviderBundleFilePicker(this);
            _viewModel.Clipboard = new TopLevelSupportClipboard(this);
        }

        base.OnDataContextChanged(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LicensesRequested -= OnLicensesRequested;
            _viewModel.BundlePicker = null;
            _viewModel.Clipboard = null;
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
        IDisposable? tracking = TrackChild?.Invoke(_licenses);
        try
        {
            _licenses.Show(this);
        }
        catch
        {
            tracking?.Dispose();
            throw;
        }

        // Reads the FFmpeg version off the UI thread; never throws.
        _ = viewModel.LoadAsync();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
