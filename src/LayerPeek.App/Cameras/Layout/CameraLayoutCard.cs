using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Cameras.Layout;

/// <summary>
/// One widget card in the layout mode (T-091): focusable (Tab, arrow keys), a pointer target for clicks and drags, and for
/// screen readers a button named "Status: Top left, position 1 of 2" whose popover is its expand / collapse state
/// (T-076 5.5). The look is in <c>CameraLayoutOverlay.axaml</c>; the gestures are handled by the overlay.
/// </summary>
public sealed class CameraLayoutCard : ContentControl
{
    public static readonly StyledProperty<bool> IsPopoverOpenProperty =
        AvaloniaProperty.Register<CameraLayoutCard, bool>(nameof(IsPopoverOpen));

    public static readonly StyledProperty<Geometry?> PlaceholderIconProperty =
        AvaloniaProperty.Register<CameraLayoutCard, Geometry?>(nameof(PlaceholderIcon));

    public static readonly StyledProperty<double> PlaceholderIconCanvasProperty =
        AvaloniaProperty.Register<CameraLayoutCard, double>(nameof(PlaceholderIconCanvas), 16);

    static CameraLayoutCard()
    {
        FocusableProperty.OverrideDefaultValue<CameraLayoutCard>(true);
    }

    /// <summary>Its popover is open (screen readers: expanded).</summary>
    public bool IsPopoverOpen
    {
        get => GetValue(IsPopoverOpenProperty);
        set => SetValue(IsPopoverOpenProperty, value);
    }

    /// <summary>The widget's icon on the placeholder card of a widget without data (T-076 5.2).</summary>
    public Geometry? PlaceholderIcon
    {
        get => GetValue(PlaceholderIconProperty);
        set => SetValue(PlaceholderIconProperty, value);
    }

    /// <summary>The canvas size (16 / 20) of <see cref="PlaceholderIcon"/>.</summary>
    public double PlaceholderIconCanvas
    {
        get => GetValue(PlaceholderIconCanvasProperty);
        set => SetValue(PlaceholderIconCanvasProperty, value);
    }

    /// <summary>The card's view model.</summary>
    public CameraLayoutCardViewModel? Card => DataContext as CameraLayoutCardViewModel;

    /// <summary>The icon resource of a widget's placeholder card (Fluent UI System Icons, T-076 6) and its canvas size.</summary>
    internal static (string Key, double Canvas) PlaceholderIconOf(string widgetId) => widgetId switch
    {
        OverlayWidgetIds.Status => ("IconStatus16Regular", 16),
        OverlayWidgetIds.Times => ("IconTimer16Regular", 16),
        OverlayWidgetIds.Temperatures => ("IconTemperature16Regular", 16),
        OverlayWidgetIds.PrintName => ("IconCube16Regular", 16),
        OverlayWidgetIds.Cooldown => ("IconWeatherSnowflake20Regular", 20),
        _ => ("IconLayoutCellFour16Regular", 16),
    };

    protected override AutomationPeer OnCreateAutomationPeer() => new CameraLayoutCardAutomationPeer(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UpdatePlaceholderIcon();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdatePlaceholderIcon();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPopoverOpenProperty && ControlAutomationPeer.FromElement(this) is CameraLayoutCardAutomationPeer peer)
        {
            bool open = change.GetNewValue<bool>();
            peer.RaiseExpandCollapseChanged(!open, open);
        }
    }

    internal void RequestExpand() => this.FindAncestorOfType<CameraLayoutOverlay>()?.Expand(this);

    internal void RequestCollapse() => this.FindAncestorOfType<CameraLayoutOverlay>()?.Collapse(this);

    private void UpdatePlaceholderIcon()
    {
        if (Card is not { } card)
        {
            return;
        }

        (string key, double canvas) = PlaceholderIconOf(card.WidgetId);
        PlaceholderIcon = this.TryFindResource(key, out object? icon) ? icon as Geometry : null;
        PlaceholderIconCanvas = canvas;
    }
}

/// <summary>A layout card for UI Automation / the macOS accessibility API: a button with an expand / collapse popover.</summary>
internal sealed class CameraLayoutCardAutomationPeer(CameraLayoutCard owner) : ControlAutomationPeer(owner), IExpandCollapseProvider
{
    public ExpandCollapseState ExpandCollapseState =>
        ((CameraLayoutCard)Owner).IsPopoverOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public bool ShowsMenu => false;

    public void Expand() => ((CameraLayoutCard)Owner).RequestExpand();

    public void Collapse() => ((CameraLayoutCard)Owner).RequestCollapse();

    internal void RaiseExpandCollapseChanged(bool wasOpen, bool isOpen) =>
        RaisePropertyChangedEvent(
            ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
            wasOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
            isOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;
}
