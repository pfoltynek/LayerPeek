using Avalonia.Automation;

using CommunityToolkit.Mvvm.ComponentModel;

using LayerPeek.App.Resources;

namespace LayerPeek.App.Settings.Widgets;

/// <summary>
/// One row of the list on the Widgets tab (T-092, T-110): a widget (<see cref="WidgetItemViewModel"/>), the separator below
/// the last widget (<see cref="WidgetsSeparatorEntry"/>) or the "Mini preview" item (<see cref="MiniStripListEntry"/>).
/// The detail panel next to the list shows the title, icon and description of the selected entry. UI thread only.
/// </summary>
public abstract class WidgetsListEntry : ObservableObject
{
    private protected WidgetsListEntry()
    {
    }

    /// <summary>The automation id of the row (<c>WidgetsItem-{id}</c>).</summary>
    public abstract string AutomationId { get; }

    /// <summary>The name in the current language (the title of the panel).</summary>
    public abstract string Name { get; }

    /// <summary>Accessible name of the row.</summary>
    public abstract string AutomationName { get; }

    /// <summary>The resource key of the icon; <see langword="null"/> = none.</summary>
    public abstract string? IconKey { get; }

    /// <summary>The one-sentence description of the panel; empty when there is none.</summary>
    public abstract string Description { get; }

    public bool HasDescription => Description.Length > 0;

    /// <summary>The row can be selected (and is a keyboard stop); the separator cannot.</summary>
    public virtual bool IsSelectable => true;

    /// <summary>Height of the row (DIP): 32, the separator 9 (T-104 design 2).</summary>
    public virtual double RowHeight => 32;

    /// <summary>Where the row is in the automation tree: the separator is not an element (raw view).</summary>
    public virtual AccessibilityView AccessibilityView => AccessibilityView.Default;

    /// <summary>The language or the selected printer changed: everything is read again.</summary>
    internal void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>The line between the widgets and the "Mini preview" item (T-104 design 2): 9 DIP, not selectable, skipped by the keyboard.</summary>
public sealed class WidgetsSeparatorEntry : WidgetsListEntry
{
    public override string AutomationId => "WidgetsSeparator";

    public override string Name => string.Empty;

    public override string AutomationName => string.Empty;

    public override string? IconKey => null;

    public override string Description => string.Empty;

    public override bool IsSelectable => false;

    public override double RowHeight => 9;

    public override AccessibilityView AccessibilityView => AccessibilityView.Raw;
}

/// <summary>
/// The "Mini preview" item at the end of the list (T-110, design T-104 2): what the info strip of the mini preview shows,
/// for the printer selected above the list. Not a widget of the overlay: it has no visibility check box (the strip cannot be
/// hidden), and Space on it does nothing.
/// </summary>
public sealed class MiniStripListEntry : WidgetsListEntry
{
    /// <summary>The id of the item (automation id <c>WidgetsItem-miniStrip</c>).</summary>
    public const string Id = "miniStrip";

    /// <summary>Fluent PictureInPicture 16 (<c>OverlayIcons.axaml</c>).</summary>
    public const string Icon = "IconPictureInPicture16Regular";

    public override string AutomationId => "WidgetsItem-" + Id;

    public override string Name => Strings.SettingsCameraWindowMini;

    /// <summary>Only the name (T-104 design 2: no "shown / hidden", the strip has no visibility).</summary>
    public override string AutomationName => Name;

    public override string? IconKey => Icon;

    public override string Description => Strings.SettingsMiniStripDescription;
}
