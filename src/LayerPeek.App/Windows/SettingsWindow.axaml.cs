using System.ComponentModel;

using Avalonia;
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
/// widget of the list. macOS (T-100) adds Apple's shortcuts: ⌘⇧] / ⌘⇧[ for the main tabs, ⌘⌥→ / ⌘⌥← for the sub-tabs,
/// ⌘W and ⌘. for Cancel. T-112: a failed Save and a refused test focus the first invalid field (<see cref="OnFieldFocusRequested"/>).
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly WorkAreaFitter _fitter;
    private SettingsViewModel? _viewModel;

    // The element that got the keyboard focus last (T-101: the slot of the notification heading moves it along).
    private IInputElement? _lastFocused;

    public SettingsWindow()
    {
        InitializeComponent();
        _fitter = new WorkAreaFitter(this);
        // The views of the widget settings panels, chosen by the type of the panel's view model (T-092, design 6).
        DataTemplates.AddRange(WidgetSettingsTemplates.All());
        // DataContext (SettingsViewModel) is set by the creator.
        // Tunnelling: before a text box or the focus navigation (Tab) sees the keys.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        // ⌘W and ⌘. (macOS, T-100) are Cancel, also for closing: the edits are discarded as with Cancel or Esc. ⌘. closes an
        // open drop-down first, like Esc (user decision 2026-10-30).
        MacShortcuts.HandleCloseKeys(this, () => UsesMacShortcuts, CancelFromKeyboard, CancelFromCommandPeriod);
        WidgetsList.AddHandler(KeyDownEvent, OnWidgetsListKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        Activated += OnWindowActivated;
    }

    /// <summary>The fitting and remembering of the window size (T-094).</summary>
    internal WorkAreaFitter Sizing => _fitter;

    /// <summary>
    /// The macOS shortcuts (T-100): ⌘⇧] / ⌘⇧[, ⌘⌥→ / ⌘⌥←, ⌘W and ⌘. (next to Ctrl+Tab and Ctrl+PageDown, which stay).
    /// Default: on macOS only; tests set it.
    /// </summary>
    internal bool UsesMacShortcuts { get; set; } = MacShortcuts.IsPlatformDefault;

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
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.FieldFocusRequested -= OnFieldFocusRequested;
        }

        _viewModel = DataContext as SettingsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested += OnCloseRequested;
            _viewModel.NotificationSettingRequested += OnNotificationSettingRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.FieldFocusRequested += OnFieldFocusRequested;
        }

        base.OnDataContextChanged(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _fitter.CorrectAfterOpening();
    }

    private void OnAnyGotFocus(object? sender, FocusChangedEventArgs e) => _lastFocused = e.Source as IInputElement;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.ShowsNotificationsTurnedOff))
        {
            // After the bindings hid one button of the slot and showed the other.
            Dispatcher.UIThread.Post(MoveFocusInNotificationSlot);
        }
    }

    /// <summary>
    /// T-101: the slot of the notification heading switched between the test button and the button to the OS settings
    /// (e.g. macOS denied the permission the test asked for). The focus that was on the hidden button goes to the shown one,
    /// not to the start of the window.
    /// </summary>
    private void MoveFocusInNotificationSlot()
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        (Button hidden, Button shown) = viewModel.ShowsNotificationsTurnedOff
            ? (TestNotificationButton, OpenNotificationSettingsButton)
            : (OpenNotificationSettingsButton, TestNotificationButton);
        if (!ReferenceEquals(_lastFocused, hidden) || !shown.IsEffectivelyVisible)
        {
            return;
        }

        IInputElement? focused = FocusManager?.GetFocusedElement();
        if (focused is null || ReferenceEquals(focused, hidden) || ReferenceEquals(focused, this) || focused is Visual { IsEffectivelyVisible: false })
        {
            shown.UpdateLayout();
            shown.Focus(NavigationMethod.Tab);
        }
    }

    /// <summary>
    /// T-020 (macOS), T-101 (Windows): coming back to the window (e.g. from System Settings &gt; Notifications or Settings &gt;
    /// System &gt; Notifications) reads the notification state again, so the warning goes as soon as the user turned the
    /// notifications on. T-019 (macOS): the same for "Open at Login"
    /// the user has to allow in System Settings &gt; General &gt; Login Items.
    /// </summary>
    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_viewModel is { } viewModel)
        {
            _ = viewModel.RefreshNotificationPermissionAsync();
            _ = viewModel.RefreshAutostartApprovalAsync();
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
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.FieldFocusRequested -= OnFieldFocusRequested;
            _viewModel = null;
        }

        base.OnClosed(e);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        if (UsesMacShortcuts && MacTabStep(viewModel, e) is { } handled)
        {
            e.Handled = handled;
            return;
        }

        if ((e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.Control)
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
    /// macOS (T-100): ⌘⇧] / ⌘⇧[ switch the main tabs, ⌘⌥→ / ⌘⌥← the sub-tabs of the printer form (Mac keyboards have no
    /// PageUp and PageDown keys). <see langword="null"/> when the key is none of them.
    /// </summary>
    private bool? MacTabStep(SettingsViewModel viewModel, KeyEventArgs e)
    {
        if (MacShortcuts.MainTabStep(e) is var main and not 0)
        {
            return viewModel.SelectAdjacentTab(forward: main > 0) && FocusSelectedTab(MainTabs);
        }

        if (MacShortcuts.SubTabStep(e) is var sub and not 0)
        {
            return viewModel.SelectAdjacentPrinterTab(forward: sub > 0) && FocusSelectedTab(PrinterTabs);
        }

        return null;
    }

    /// <summary>
    /// ⌘. (macOS, T-100 question 3, user decision 2026-10-30): what Esc does. An open drop-down (<see cref="ComboBox"/>) is
    /// closed first and the window stays open; otherwise Cancel (<see cref="CancelFromKeyboard"/>). ⌘W always cancels.
    /// </summary>
    private void CancelFromCommandPeriod()
    {
        if (!MacShortcuts.TryCloseOpenDropDown(this))
        {
            CancelFromKeyboard();
        }
    }

    /// <summary>⌘W / ⌘. (macOS, T-100): what the Cancel button does; nothing while saving (Cancel is disabled then).</summary>
    private void CancelFromKeyboard()
    {
        if (_viewModel is { } viewModel && viewModel.CancelCommand.CanExecute(null))
        {
            viewModel.CancelCommand.Execute(null);
        }
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
    /// T-112 (design 4): the first invalid field of the view the view model just selected gets the keyboard focus, once the
    /// (sub-)tab and its content are shown. As if reached with Tab (<see cref="NavigationMethod.Tab"/>: <c>:focus-visible</c>,
    /// the text selected), so a screen reader reads its name, value and error (its help text). A NumericUpDown and an editable
    /// ComboBox are focused in the TextBox of their template, which holds the keyboard focus. Nothing happens when the field
    /// is not shown or disabled then (the status line still says what is wrong).
    /// </summary>
    private void OnFieldFocusRequested(object? sender, string automationId) =>
        Dispatcher.UIThread.Post(() => FocusField(automationId), DispatcherPriority.Loaded);

    private void FocusField(string automationId)
    {
        if (_viewModel is null)
        {
            return;
        }

        UpdateLayout();
        Control? field = this.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(control => Avalonia.Automation.AutomationProperties.GetAutomationId(control) == automationId);
        if (field is not { IsEffectivelyVisible: true, IsEffectivelyEnabled: true })
        {
            return;
        }

        string? part = field switch
        {
            NumericUpDown => "PART_TextBox",
            ComboBox { IsEditable: true } => "PART_EditableTextBox",
            _ => null,
        };
        InputElement? target = part is null ? field
            : field.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.Name == part);
        if (target is not null)
        {
            field.BringIntoView();
            target.Focus(NavigationMethod.Tab);
        }
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
