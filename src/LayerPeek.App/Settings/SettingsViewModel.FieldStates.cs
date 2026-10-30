using LayerPeek.App.Resources;
using LayerPeek.App.Settings.Widgets;

namespace LayerPeek.App.Settings;

/// <summary>
/// T-112: the invalid-field states of the General tab and the focus on the first invalid field. A failed Save (also Apply
/// of the Camera window sub-tab) selects the tab and sub-tab with the error as before and then asks the window to focus the
/// first invalid field there in reading order (design 4, Q3 A); a refused Test connection / Test camera does the same
/// within its sub-tab (Q5 A). The window moves the focus once the tab is shown and only to a visible, enabled field.
/// No live announcement (Q6 A): the screen reader reads the error from the help text of the focused field.
/// </summary>
public sealed partial class SettingsViewModel
{
    /// <summary>
    /// The automation id (<see cref="SettingsFieldIds"/>) of the field that should get the keyboard focus because it is the
    /// first invalid one of the view just selected.
    /// </summary>
    public event EventHandler<string>? FieldFocusRequested;

    /// <summary>The polling interval shows its error (its field is marked, T-112).</summary>
    public bool HasPollingIntervalError => _pollingIntervalError is not null;

    /// <summary>The remaining-time threshold shows its error.</summary>
    public bool HasRemainingTimeError => _remainingTimeError is not null;

    /// <summary>For screen readers: the error of the polling interval, else its hint.</summary>
    public string? PollingIntervalHelpText => _pollingIntervalError ?? Strings.SettingsPollingIntervalHint;

    /// <summary>For screen readers: the error of the remaining-time threshold (it has no hint).</summary>
    public string? RemainingTimeHelpText => _remainingTimeError;

    /// <summary>
    /// A refused test of <paramref name="printer"/> (Q5): the first invalid field of <paramref name="printerTab"/>, when that
    /// sub-tab of that printer is the one shown.
    /// </summary>
    internal void RequestFieldFocus(PrinterEditorViewModel printer, int printerTab)
    {
        if (ReferenceEquals(_selectedPrinter, printer) && _selectedTabIndex == PrintersTab && _selectedPrinterTabIndex == printerTab)
        {
            RequestFieldFocus(printer.FirstInvalidField(printerTab));
        }
    }

    private void RequestFieldFocus(string? field)
    {
        if (field is not null && !_disposed)
        {
            FieldFocusRequested?.Invoke(this, field);
        }
    }

    /// <summary>The first invalid field of the selected widget panel (today only the cool-down thresholds have fields).</summary>
    private string? FirstInvalidWidgetField(PrinterEditorViewModel printer) =>
        _widgetPanel is CooldownWidgetSettingsViewModel ? printer.FirstInvalidCooldownField() : null;

    private string? FirstInvalidGeneralField() =>
        HasPollingIntervalError ? SettingsFieldIds.SettingsPollingInterval
        : HasRemainingTimeError ? SettingsFieldIds.SettingsRemainingTime
        : null;
}
