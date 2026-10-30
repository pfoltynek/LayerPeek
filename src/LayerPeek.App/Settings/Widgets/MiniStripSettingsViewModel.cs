using CommunityToolkit.Mvvm.ComponentModel;

namespace LayerPeek.App.Settings.Widgets;

/// <summary>
/// The panel of the "Mini preview" item on the Widgets tab (T-110, design T-104 2): the seven check boxes of what the info
/// strip of the mini preview shows, bound to the edited values of the printer selected above the list
/// (<see cref="PrinterEditorViewModel.MiniStrip"/>), and the one-line hint. Its view is the <c>DataTemplate</c> in
/// <c>WidgetSettingsTemplates.axaml</c>. Replaced when another printer is selected (the edited values of each printer stay
/// in its editor). UI thread only.
/// </summary>
public sealed class MiniStripSettingsViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;

    internal MiniStripSettingsViewModel(SettingsViewModel owner, PrinterEditorViewModel printer)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(printer);
        _owner = owner;
        Printer = printer;
    }

    /// <summary>The printer whose strip the check boxes edit.</summary>
    public PrinterEditorViewModel Printer { get; }

    /// <summary>The check boxes can be changed: the printer's camera is on and the window is editable (values stay otherwise).</summary>
    public bool CanEdit => Printer.CameraEnabled && _owner.IsEditable;

    /// <summary>The camera, the editability or the language changed.</summary>
    internal void Refresh() => OnPropertyChanged(string.Empty);
}
