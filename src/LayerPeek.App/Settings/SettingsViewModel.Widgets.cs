using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Cameras.Layout;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings.Widgets;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Events;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Settings;

/// <summary>
/// The Widgets tab (T-092, approved design "Návrh UI"): the printer selection shared with the Printers tab
/// (<see cref="SelectedPrinter"/>), the list of the registered widgets with their visibility, the panel of the selected
/// widget (its description and, when it declares one, its settings panel), and the "All widgets" section: "Edit layout in
/// camera window…", "Restore defaults", "Use layout for all printers" and the global card background opacity. A layout
/// saved by the layout mode of a camera window replaces only the layout of its printer here (T-077 Q3).
/// </summary>
public sealed partial class SettingsViewModel
{
    private WidgetItemViewModel[] _widgets = [];
    private WidgetItemViewModel? _selectedWidget;
    private OverlayWidgetSettingsViewModel? _widgetPanel;
    private ICameraLayoutModeLauncher? _layoutModeLauncher;
    private ICameraOverlayLayoutWriter? _layoutWriter;
    private IUiDispatcher _uiDispatcher = null!;
    private int _overlayOpacityPercent = CameraOverlayOpacity.DefaultPercent;

    /// <summary>"Change on the General tab" of a widget panel: the window moves the focus to the switch of the notification.</summary>
    public event EventHandler<PrintEventKind>? NotificationSettingRequested;

    /// <summary>The registered widgets in registration order (design 3.2); widgets of the layout that are not registered are not listed.</summary>
    public IReadOnlyList<WidgetItemViewModel> Widgets => _widgets;

    /// <summary>
    /// The widget whose panel is shown; the first one by default. It stays when another printer is selected (the same
    /// widget of the other printer). A list control writing <see langword="null"/> while it is rebuilt is ignored.
    /// </summary>
    public WidgetItemViewModel? SelectedWidget
    {
        get => _selectedWidget;
        set
        {
            if (value is null || Array.IndexOf(_widgets, value) < 0)
            {
                return;
            }

            if (SetProperty(ref _selectedWidget, value))
            {
                UpdateWidgetPanel();
            }
        }
    }

    /// <summary>
    /// The settings panel of the selected widget for the selected printer (<see cref="IOverlayWidgetSettingsFactory"/>);
    /// <see langword="null"/> when the widget has none or no printer is selected. Replaced (the old one disposed) when the
    /// widget or the printer changes.
    /// </summary>
    public OverlayWidgetSettingsViewModel? SelectedWidgetPanel => _widgetPanel;

    /// <summary>The selected widget has no settings of its own: the panel says so (design 4.2).</summary>
    public bool ShowsNoWidgetSettings => _selectedWidget is { HasSettings: false } && _selectedPrinter is not null;

    /// <summary>The list and the panel are shown: a printer is selected (design 3.6: hidden without printers).</summary>
    public bool ShowsWidgets => _selectedPrinter is not null;

    /// <summary>The selected printer's camera is off: the hint next to the printer selection (design 3.5).</summary>
    public bool ShowsWidgetsCameraOff => _selectedPrinter is { CameraEnabled: false };

    /// <summary>The "!" on the Widgets tab header: a printer has a shown error in a widget's settings (design 4.3).</summary>
    public bool ShowsWidgetsErrorMarker
    {
        get
        {
            foreach (PrinterEditorViewModel printer in Printers)
            {
                if (printer.ShowsWidgetErrorMarker)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Accessible name of the Widgets tab header: says when it contains errors (the "!" alone is not read out).</summary>
    public string WidgetsTabName => ShowsWidgetsErrorMarker ? Format(Strings.SettingsTabContainsErrors, Strings.SettingsTabWidgets) : Strings.SettingsTabWidgets;

    /// <summary>
    /// "Edit layout in camera window…" (design 5.4, Q4): starts the layout mode of the selected printer's camera window
    /// with the layout as edited here. Only for a saved printer whose camera is on in the saved and the edited settings.
    /// </summary>
    public IRelayCommand EditLayoutCommand { get; private set; } = null!;

    /// <summary>Why "Edit layout in camera window…" is disabled (its tooltip); <see langword="null"/> while it is enabled or without a printer.</summary>
    public string? EditLayoutUnavailableReason =>
        _selectedPrinter is not { } printer ? null
        : !printer.CameraEnabled ? Strings.SettingsWidgetsEditLayoutCameraOff
        : printer.IsNew || !printer.IsCameraEnabledWhenSaved ? Strings.SettingsWidgetsEditLayoutUnsaved
        : null;

    /// <summary>
    /// The tooltip and help text of "Edit layout in camera window…": the reason while it is disabled, otherwise what it does.
    /// </summary>
    public string EditLayoutToolTip => EditLayoutUnavailableReason ?? Strings.SettingsWidgetsEditLayoutHelp;

    /// <summary>"Restore defaults" (design 5.5): the whole layout of the selected printer back to the default, nothing else.</summary>
    public IRelayCommand ResetLayoutCommand { get; private set; } = null!;

    /// <summary>"Use layout for all printers": the whole layout of the selected printer to every other one (T-076 Q1, T-077 Q1).</summary>
    public IRelayCommand ApplyLayoutToAllCommand { get; private set; } = null!;

    /// <summary>Tooltip of the disabled "Use layout for all printers": only one printer; <see langword="null"/> otherwise.</summary>
    public string? ApplyLayoutToAllUnavailableReason => Printers.Count == 1 ? Strings.SettingsOverlayApplyAllSingle : null;

    public static double MinOverlayOpacityPercent => CameraOverlayOpacity.MinPercent;

    public static double MaxOverlayOpacityPercent => CameraOverlayOpacity.MaxPercent;

    /// <summary>
    /// "Card background opacity" of every camera window (T-076 Q2), whole percent within 30–95 (a slider value is rounded
    /// and clamped); applied to the open camera windows after saving, like the other settings.
    /// </summary>
    public double OverlayOpacityPercent
    {
        get => _overlayOpacityPercent;
        set
        {
            int percent = double.IsNaN(value)
                ? _overlayOpacityPercent
                : (int)Math.Round(Math.Clamp(value, CameraOverlayOpacity.MinPercent, CameraOverlayOpacity.MaxPercent), MidpointRounding.AwayFromZero);
            if (percent != _overlayOpacityPercent)
            {
                _overlayOpacityPercent = percent;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OverlayOpacityText));
                UpdateDirty();
            }
        }
    }

    /// <summary>The opacity as a percentage of the current culture ("72 %" in Czech, "72%" in English).</summary>
    public string OverlayOpacityText => (_overlayOpacityPercent / 100.0).ToString("P0", CultureInfo.CurrentCulture);

    /// <summary>
    /// A failed save because of a widget's settings selects the first widget with the error (design 4.3: the printer, the
    /// widget and the tab show the "!"); without a registered widget showing it, the selection stays.
    /// </summary>
    private void SelectFirstWidgetWithError(PrinterEditorViewModel printer)
    {
        foreach (WidgetItemViewModel widget in _widgets)
        {
            if (widget.Definition.Settings?.HasError(printer) == true)
            {
                SelectedWidget = widget;
                return;
            }
        }
    }

    private void InitializeWidgets(OverlayWidgetRegistry registry, ICameraLayoutModeLauncher? launcher, ICameraOverlayLayoutWriter? writer, IUiDispatcher? dispatcher)
    {
        _widgets = [.. registry.Definitions.Select(definition => new WidgetItemViewModel(this, definition))];
        _selectedWidget = _widgets.Length > 0 ? _widgets[0] : null;
        _layoutModeLauncher = launcher;
        _uiDispatcher = dispatcher ?? new AvaloniaUiDispatcher();
        EditLayoutCommand = new RelayCommand(EditLayout, CanEditLayout);
        ResetLayoutCommand = new RelayCommand(ResetLayout, () => IsEditable && _selectedPrinter is not null);
        ApplyLayoutToAllCommand = new RelayCommand(ApplyLayoutToAll, () => IsEditable && _selectedPrinter is not null && Printers.Count > 1);
        Printers.CollectionChanged += OnWidgetPrintersChanged;
        if (writer is not null)
        {
            _layoutWriter = writer;
            writer.LayoutSaved += OnLayoutSaved;
        }
    }

    private void DisposeWidgets()
    {
        if (_layoutWriter is { } writer)
        {
            writer.LayoutSaved -= OnLayoutSaved;
            _layoutWriter = null;
        }

        Printers.CollectionChanged -= OnWidgetPrintersChanged;
        foreach (PrinterEditorViewModel printer in Printers)
        {
            printer.PropertyChanged -= OnWidgetPrinterChanged;
        }

        ReplaceWidgetPanel(null);
    }

    /// <summary>Visibility of <paramref name="widget"/> for the selected printer (its check box or Space in the list).</summary>
    internal void SetWidgetShown(WidgetItemViewModel widget, bool shown)
    {
        if (_selectedPrinter is not { CameraEnabled: true } printer || !IsEditable)
        {
            // A disabled check box: the value is kept; the binding reads it back.
            widget.Refresh();
            return;
        }

        SelectedWidget = widget;
        printer.SetWidgetShown(widget.Id, shown);
    }

    /// <summary>Space on the widget list (design 5.2): switches the visibility of the selected widget.</summary>
    public bool ToggleSelectedWidget()
    {
        if (_selectedWidget is not { CanChangeVisibility: true } widget)
        {
            return false;
        }

        widget.IsShown = !widget.IsShown;
        return true;
    }

    /// <summary>Another printer was selected (here or on the Printers tab): its panel and rows; the widget stays selected.</summary>
    private void OnSelectedPrinterChanged()
    {
        UpdateWidgetPanel();
        OnWidgetsStateChanged();
    }

    /// <summary>Something the Widgets tab shows changed (selection, printers, editability, errors, camera).</summary>
    private void OnWidgetsStateChanged()
    {
        foreach (WidgetItemViewModel widget in _widgets)
        {
            widget.Refresh();
        }

        OnPropertyChanged(nameof(ShowsWidgets));
        OnPropertyChanged(nameof(ShowsNoWidgetSettings));
        OnPropertyChanged(nameof(ShowsWidgetsCameraOff));
        OnPropertyChanged(nameof(ShowsWidgetsErrorMarker));
        OnPropertyChanged(nameof(WidgetsTabName));
        OnPropertyChanged(nameof(EditLayoutUnavailableReason));
        OnPropertyChanged(nameof(EditLayoutToolTip));
        OnPropertyChanged(nameof(ApplyLayoutToAllUnavailableReason));
        EditLayoutCommand.NotifyCanExecuteChanged();
        ResetLayoutCommand.NotifyCanExecuteChanged();
        ApplyLayoutToAllCommand.NotifyCanExecuteChanged();
    }

    private void OnWidgetPrintersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (PrinterEditorViewModel printer in e.OldItems?.OfType<PrinterEditorViewModel>() ?? [])
        {
            printer.PropertyChanged -= OnWidgetPrinterChanged;
        }

        foreach (PrinterEditorViewModel printer in e.NewItems?.OfType<PrinterEditorViewModel>() ?? [])
        {
            printer.PropertyChanged += OnWidgetPrinterChanged;
        }
    }

    private void OnWidgetPrinterChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PrinterEditorViewModel.ShowsWidgetErrorMarker):
            case nameof(PrinterEditorViewModel.CooldownThresholdError):
                OnWidgetsStateChanged();
                break;
            case nameof(PrinterEditorViewModel.OverlayLayout):
            case nameof(PrinterEditorViewModel.CameraEnabled):
            case nameof(PrinterEditorViewModel.IsNew):
            case nameof(PrinterEditorViewModel.IsCameraEnabledWhenSaved):
                if (ReferenceEquals(sender, _selectedPrinter))
                {
                    OnWidgetsStateChanged();
                }

                break;
        }
    }

    /// <summary>The panel of the selected widget for the selected printer; the previous one is disposed.</summary>
    private void UpdateWidgetPanel()
    {
        OverlayWidgetSettingsViewModel? panel = !_disposed && _selectedPrinter is { } printer && _selectedWidget?.Definition.Settings is { } factory
            ? factory.Create(new WidgetHost(this, printer))
            : null;
        ReplaceWidgetPanel(panel);
        OnPropertyChanged(nameof(ShowsNoWidgetSettings));
    }

    private void ReplaceWidgetPanel(OverlayWidgetSettingsViewModel? panel)
    {
        OverlayWidgetSettingsViewModel? old = _widgetPanel;
        if (ReferenceEquals(old, panel))
        {
            return;
        }

        _widgetPanel = panel;
        OnPropertyChanged(nameof(SelectedWidgetPanel));
        old?.Dispose();
    }

    private void RefreshWidgetTexts()
    {
        OnWidgetsStateChanged();
        OnPropertyChanged(nameof(OverlayOpacityText));
        _widgetPanel?.RefreshTexts();
    }

    private bool CanEditLayout() =>
        IsEditable && _layoutModeLauncher is not null && _selectedPrinter is { } printer && EditLayoutUnavailableReason is null && Printers.Contains(printer);

    private void EditLayout()
    {
        if (!CanEditLayout() || _selectedPrinter is not { } printer)
        {
            return;
        }

        // Q4: from the layout as edited here; "Done" saves it (and announces it, OnLayoutSaved), Esc changes nothing.
        _layoutModeLauncher!.Start(printer.Id, printer.OverlayLayout);
    }

    private void ResetLayout()
    {
        if (!IsEditable || _selectedPrinter is not { } printer)
        {
            return;
        }

        printer.SetOverlayLayout(printer.OverlayLayout.ResetToDefault());
        ShowStatus(static () => Strings.SettingsWidgetsResetDone, isError: false);
    }

    private void ApplyLayoutToAll()
    {
        if (!IsEditable || _selectedPrinter is not { } source || Printers.Count < 2)
        {
            return;
        }

        CameraOverlayLayout layout = source.OverlayLayout;
        ApplyToOtherPrinters(source, target => target.SetOverlayLayout(layout), static () => Strings.SettingsOverlayAppliedAll);
    }

    /// <summary>"… for all printers": <paramref name="copy"/> for every printer except <paramref name="source"/>, then the status.</summary>
    private void ApplyToOtherPrinters(PrinterEditorViewModel source, Action<PrinterEditorViewModel> copy, Func<string> done)
    {
        if (!IsEditable || Printers.Count < 2 || !Printers.Contains(source))
        {
            return;
        }

        foreach (PrinterEditorViewModel printer in Printers.ToArray())
        {
            if (!ReferenceEquals(printer, source))
            {
                copy(printer);
            }
        }

        ShowStatus(done, isError: false);
    }

    /// <summary>
    /// The writer's thread: the layout mode of a camera window saved a layout (T-077 Q3, T-091). On the UI thread the
    /// printer takes only that layout as its saved and edited layout; its other unsaved changes and those of the window stay.
    /// A printer removed in the window or not saved yet ignores it.
    /// </summary>
    private void OnLayoutSaved(object? sender, CameraOverlayLayoutSavedEventArgs saved)
    {
        if (saved is null)
        {
            return;
        }

        string printerId = saved.PrinterId;
        CameraOverlayLayout layout = saved.Layout;
        _uiDispatcher.Post(() => TakeSavedLayout(printerId, layout));
    }

    /// <summary>UI thread: see <see cref="OnLayoutSaved"/>.</summary>
    internal void TakeSavedLayout(string printerId, CameraOverlayLayout layout)
    {
        if (_disposed || !_loaded)
        {
            return;
        }

        PrinterEditorViewModel? printer = Printers.FirstOrDefault(candidate => !candidate.IsNew && string.Equals(candidate.Id, printerId, StringComparison.OrdinalIgnoreCase));
        if (printer is null)
        {
            return;
        }

        _baseline = _baseline.WithCameraOverlayLayout(printer.Id, layout);
        printer.TakeSavedOverlayLayout(layout);
        UpdateDirty();
    }

    /// <summary>The settings window as <see cref="IOverlayWidgetSettingsHost"/> of one widget panel, for its printer.</summary>
    private sealed class WidgetHost(SettingsViewModel owner, PrinterEditorViewModel printer) : IOverlayWidgetSettingsHost
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => owner.PropertyChanged += value;
            remove => owner.PropertyChanged -= value;
        }

        public PrinterEditorViewModel Printer => printer;

        public int PrinterCount => owner.Printers.Count;

        public bool IsCameraEnabled => printer.CameraEnabled;

        public bool NotificationsEnabled => owner.NotificationsEnabled;

        public bool IsNotificationOn(PrintEventKind kind) => kind switch
        {
            PrintEventKind.PrintStarted => owner.NotifyPrintStarted,
            PrintEventKind.PrintFinished => owner.NotifyPrintFinished,
            PrintEventKind.PrintPaused => owner.NotifyPrintPaused,
            PrintEventKind.PrintResumed => owner.NotifyPrintResumed,
            PrintEventKind.PrintStopped => owner.NotifyPrintStopped,
            PrintEventKind.Error => owner.NotifyError,
            PrintEventKind.AttentionRequired => owner.NotifyAttention,
            PrintEventKind.PrinterOffline or PrintEventKind.PrinterOnline => owner.NotifyOffline,
            PrintEventKind.RemainingTime => owner.RemainingTimeMinutes > 0,
            PrintEventKind.CooledDown => owner.NotifyCooledDown,
            _ => false,
        };

        public void ApplyToOtherPrinters(Action<PrinterEditorViewModel> copy, Func<string> done)
        {
            ArgumentNullException.ThrowIfNull(copy);
            ArgumentNullException.ThrowIfNull(done);
            owner.ApplyToOtherPrinters(printer, copy, done);
        }

        public void ShowStatus(Func<string> text)
        {
            ArgumentNullException.ThrowIfNull(text);
            owner.ShowStatus(text, isError: false);
        }

        public void ShowNotificationSetting(PrintEventKind kind) => owner.ShowNotificationSetting(kind);
    }

    /// <summary>Switches to the General tab; the window moves the focus to the switch of <paramref name="kind"/>.</summary>
    private void ShowNotificationSetting(PrintEventKind kind)
    {
        if (_disposed)
        {
            return;
        }

        SelectedTabIndex = GeneralTab;
        NotificationSettingRequested?.Invoke(this, kind);
    }
}
