using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using PrusaTrayDiagnostic.App.Settings;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>
/// Settings window (T-015). Closing it (Save, Cancel or the title bar button) never quits the app
/// (<c>ShutdownMode.OnExplicitShutdown</c>); unsaved edits are discarded. Its view model is disposed by
/// <see cref="AuxiliaryWindowSlot"/> when it closes. Opens at its designed size, shrunk to the work area of its screen
/// (<see cref="SettingsWindowPlacement"/>, T-033). Ctrl+Tab / Ctrl+Shift+Tab switch the main tabs, Ctrl+PageDown /
/// Ctrl+PageUp the sub-tabs of the printer form.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private SettingsViewModel? _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
        // DataContext (SettingsViewModel) is set by the creator.
        // Tunnelling: before a text box or the focus navigation (Tab) sees the keys.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToScreen();
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

    /// <summary>
    /// A work area smaller than the window (high scaling, a small screen, a large taskbar): shrink the window to fit and
    /// centre it again. Otherwise <c>CenterScreen</c> has already placed it and nothing changes.
    /// </summary>
    private void FitToScreen()
    {
        if (WindowState != WindowState.Normal || (Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen)
        {
            return;
        }

        Size requested = new(Width, Height);
        SettingsWindowBounds bounds = SettingsWindowPlacement.Fit(
            requested,
            new Size(MinWidth, MinHeight),
            screen.WorkingArea,
            screen.Scaling,
            SettingsWindowPlacement.ChromeOf(FrameSize, ClientSize));
        if (bounds.ClientSize != requested)
        {
            Width = bounds.ClientSize.Width;
            Height = bounds.ClientSize.Height;
            Position = bounds.Position;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { } viewModel || (e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.Control)
        {
            return;
        }

        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        e.Handled = e.Key switch
        {
            Key.Tab => viewModel.SelectAdjacentTab(forward: !shift) && FocusSelectedTab(MainTabs),
            Key.PageDown when !shift => viewModel.SelectAdjacentPrinterTab(forward: true) && FocusSelectedTab(PrinterTabs),
            Key.PageUp when !shift => viewModel.SelectAdjacentPrinterTab(forward: false) && FocusSelectedTab(PrinterTabs),
            _ => false,
        };
    }

    /// <summary>
    /// The focused field may have been on the tab that was just hidden: the focus moves to the header of the selected tab,
    /// from where Tab continues into its content. Always true (the key was used).
    /// </summary>
    private static bool FocusSelectedTab(TabControl tabs)
    {
        tabs.UpdateLayout();
        if (tabs.ContainerFromIndex(tabs.SelectedIndex) is { } header)
        {
            header.Focus(NavigationMethod.Directional);
        }

        return true;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
