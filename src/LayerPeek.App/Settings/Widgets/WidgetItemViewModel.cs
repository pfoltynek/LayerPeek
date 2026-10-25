using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Localization;
using LayerPeek.App.Resources;

namespace LayerPeek.App.Settings.Widgets;

/// <summary>
/// One row of the widget list on the Widgets tab (T-092, design 3.2 and 5.2): a registered widget with its icon, name,
/// visibility check box and the "!" of an error in its settings panel, for the printer selected in the window. The visibility
/// is that of the printer's edited layout (<see cref="PrinterEditorViewModel.OverlayLayout"/>), shared by the normal window
/// and full screen. UI thread only.
/// </summary>
public sealed class WidgetItemViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;

    internal WidgetItemViewModel(SettingsViewModel owner, IOverlayWidgetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(definition);
        _owner = owner;
        Definition = definition;
    }

    internal IOverlayWidgetDefinition Definition { get; }

    /// <summary>The widget id (automation ids <c>WidgetsItem-{id}</c>, <c>WidgetsVisible-{id}</c>, <c>WidgetsItemMarker-{id}</c>).</summary>
    public string Id => Definition.Id;

    /// <summary>The widget name in the current language.</summary>
    public string Name => LocalizedStrings.Instance[Definition.NameKey];

    /// <summary>The one-sentence description of the panel (design 4.1); empty when the widget declares none.</summary>
    public string Description => Definition.DescriptionKey is { } key ? LocalizedStrings.Instance[key] : string.Empty;

    public bool HasDescription => Definition.DescriptionKey is not null;

    /// <summary>The resource key of the icon (<see cref="IOverlayWidgetDefinition.IconKey"/>); <see langword="null"/> = none.</summary>
    public string? IconKey => Definition.IconKey;

    /// <summary>The widget has a settings panel of its own; otherwise its panel says it has no other settings.</summary>
    public bool HasSettings => Definition.Settings is not null;

    /// <summary>
    /// Shown in the camera window (the visibility of the selected printer's layout). Changing it selects the row too, as a
    /// click on the check box does (design 5.2). Nothing changes without a selected printer or while the camera is off (the
    /// check box is disabled then; the value is kept).
    /// </summary>
    public bool IsShown
    {
        get => _owner.SelectedPrinter?.IsWidgetShown(Id) ?? false;
        set => _owner.SetWidgetShown(this, value);
    }

    /// <summary>The check box can be changed: a printer is selected, its camera is on and the window is editable.</summary>
    public bool CanChangeVisibility => _owner.SelectedPrinter is { CameraEnabled: true } && _owner.IsEditable;

    /// <summary>The "!" of the row: the widget's settings of the selected printer have a shown error.</summary>
    public bool ShowsErrorMarker => _owner.SelectedPrinter is { } printer && Definition.Settings?.HasError(printer) == true;

    /// <summary>Accessible name of the row: "{name}, shown" / "{name}, hidden" (design 5.2).</summary>
    public string AutomationName => Format(IsShown ? Strings.SettingsWidgetsItemShown : Strings.SettingsWidgetsItemHidden, Name);

    /// <summary>Accessible name of the check box: "Show the {name} widget".</summary>
    public string ShowName => Format(Strings.SettingsWidgetsShowName, Name);

    /// <summary>The selected printer, its layout, camera, errors or the language changed: everything is read again.</summary>
    internal void Refresh() => OnPropertyChanged(string.Empty);

    private static string Format(string format, string name) => string.Format(CultureInfo.CurrentCulture, format, name);
}
