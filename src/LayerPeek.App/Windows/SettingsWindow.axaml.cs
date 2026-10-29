using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Settings;
using LayerPeek.App.Settings.Widgets;
using LayerPeek.Core.Events;

namespace LayerPeek.App.Windows;

/// <summary>
/// Settings window (T-015). Closing it (Save, Cancel or the title bar button) never quits the app
/// (<c>ShutdownMode.OnExplicitShutdown</c>); unsaved edits are discarded. Its view model is disposed by
/// <see cref="AuxiliaryWindowSlot"/> when it closes. Opens at its designed (or remembered, T-094) size, shrunk to the work area of its screen
/// (<see cref="AuxiliaryWindowPlacement"/>, T-033). Ctrl+Tab / Ctrl+Shift+Tab switch the main tabs, Ctrl+PageDown /
/// Ctrl+PageUp the sub-tabs of the printer form. On the Widgets tab (T-092) Space switches the visibility of the selected
/// widget of the list.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly WorkAreaFitter _fitter;
    private SettingsViewModel? _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
        _fitter = new WorkAreaFitter(this);
        // The views of the widget settings panels, chosen by the type of the panel's view model (T-092, design 6).
        DataTemplates.AddRange(WidgetSettingsTemplates.All());
        // DataContext (SettingsViewModel) is set by the creator.
        // Tunnelling: before a text box or the focus navigation (Tab) sees the keys.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        WidgetsList.AddHandler(KeyDownEvent, OnWidgetsListKeyDown, RoutingStrategies.Tunnel);
        Activated += OnWindowActivated;
    }

    /// <summary>The fitting and remembering of the window size (T-094).</summary>
    internal WorkAreaFitter Sizing => _fitter;

    /// <summary>
    /// Before showing (T-094): opens with the size and maximized state remembered in <paramref name="store"/> and
    /// remembers them again when the window closes (also after "Cancel": it is window state, not a setting).
    /// </summary>
    internal void RememberSizeIn(IAuxiliaryWindowStateStore store) => _fitter.Remember(store, AuxiliaryWindowKeys.Settings);

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
            _viewModel.NotificationSettingRequested -= OnNotificationSettingRequested;
        }

        _viewModel = DataContext as SettingsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested += OnCloseRequested;
            _viewModel.NotificationSettingRequested += OnNotificationSettingRequested;
        }

        base.OnDataContextChanged(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _fitter.CorrectAfterOpening();
    }

    /// <summary>
    /// T-020 (macOS): coming back to the window (e.g. from System Settings &gt; Notifications) reads the notification permission
    /// again, so the warning goes as soon as the user turned the notifications on.
    /// </summary>
    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_viewModel is { } viewModel)
        {
            _ = viewModel.RefreshNotificationPermissionAsync();
        }
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
            _viewModel.NotificationSettingRequested -= OnNotificationSettingRequested;
            _viewModel = null;
        }

        base.OnClosed(e);
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
    /// Space on the widget list (design 5.2): the visibility of the selected widget (the check boxes are no Tab stops). The
    /// list handles the arrows, Home and End itself.
    /// </summary>
    private void OnWidgetsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None && _viewModel is { } viewModel)
        {
            e.Handled = viewModel.ToggleSelectedWidget();
        }
    }

    /// <summary>
    /// "Change on the General tab" of a widget panel (design 4.3): the view model selected the General tab; the focus goes to
    /// the switch of the notification once the tab is shown.
    /// </summary>
    private void OnNotificationSettingRequested(object? sender, PrintEventKind kind)
    {
        if (kind != PrintEventKind.CooledDown)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            MainTabs.UpdateLayout();
            if (this.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(static box => Avalonia.Automation.AutomationProperties.GetAutomationId(box) == "SettingsNotifyCooledDown") is { } target)
            {
                target.Focus(NavigationMethod.Tab);
                target.BringIntoView();
            }
        });
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
