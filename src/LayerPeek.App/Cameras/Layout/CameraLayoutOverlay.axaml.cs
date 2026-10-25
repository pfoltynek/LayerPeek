using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Cameras.Layout;

/// <summary>
/// The view of the layout mode (T-091): passes the gestures to <see cref="CameraLayoutModeViewModel"/> and shows its state.
/// </summary>
/// <remarks>
/// <para>
/// Mouse (T-076 5.3): a press on a card that moves more than 4 DIP drags it — the card follows the pointer (kept grip, raised,
/// above everything) and an outline stays at its place; the target is the zone under the pointer or the nearest one (none
/// over the toolbar) and the position follows the pointer's height among the other cards. Releasing drops it there; a press
/// without moving is a click that selects the card and opens its popover.
/// </para>
/// <para>
/// Keyboard (T-076 5.5): Space picks the focused card up, the arrow keys / Home / End move it, Space or Enter drop it, Esc puts
/// it back; on a card that is not picked up the arrow keys move the focus to the nearest card that way and Enter / F2 open the
/// popover with the focus on "Highlight". Esc closes the popover (the focus returns to the card), cancels a drag, and otherwise
/// discards the edit. Tab goes through the cards in reading order and then the toolbar, cycling inside the mode.
/// </para>
/// </remarks>
public sealed partial class CameraLayoutOverlay : UserControl
{
    /// <summary>Pointer travel (DIP) that turns a press on a card into a drag.</summary>
    internal const double DragThreshold = 4;

    private readonly Flyout _flyout;
    private readonly CameraLayoutPopover _popover;
    private CameraLayoutModeViewModel? _viewModel;

    // The press on a card that becomes a drag once it moves DragThreshold; the grip offset inside the card.
    private (CameraLayoutCard Card, Point Origin)? _press;
    private CameraLayoutCard? _draggedCard;
    private Control? _draggedZone;
    private bool _popoverFromKeyboard;

    public CameraLayoutOverlay()
    {
        InitializeComponent();
        _popover = new CameraLayoutPopover();
        _flyout = new Flyout { Content = _popover, Placement = PlacementMode.RightEdgeAlignedTop };
        _flyout.Opened += OnFlyoutOpened;
        _flyout.Closed += OnFlyoutClosed;
        _popover.AddHandler(KeyDownEvent, OnPopoverKeyDown, RoutingStrategies.Bubble);
        if (UsesMacButtonOrder)
        {
            // macOS (HIG): the confirming button on the right, [Cancel] [Done].
            ToolbarButtons.Children.Move(ToolbarButtons.Children.IndexOf(CancelButton), ToolbarButtons.Children.IndexOf(DoneButton));
        }

        LayoutRoot.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble);
        LayoutRoot.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble);
        LayoutRoot.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble);
        LayoutRoot.AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        SizeChanged += (_, e) => Toolbar.MaxWidth = Math.Max(0, Math.Min(720, e.NewSize.Width - 24));
    }

    /// <summary>The toolbar puts the confirming button last, as on macOS (T-076 5.2); Windows: [Done] [Cancel].</summary>
    internal static bool UsesMacButtonOrder => OperatingSystem.IsMacOS();

    /// <summary>The popover (a flyout in the app theme) of the selected card.</summary>
    internal Flyout PopoverFlyout => _flyout;

    /// <summary>The content of the popover.</summary>
    internal CameraLayoutPopover Popover => _popover;

    /// <summary>The root of the mode (shown while it is on).</summary>
    internal Panel Root => LayoutRoot;

    /// <summary>The toolbar's content panel (one or two rows).</summary>
    internal CameraLayoutToolbarPanel ToolbarContent => ToolbarPanel;

    /// <summary>The card control of <paramref name="card"/>; <see langword="null"/> when it is not shown.</summary>
    internal CameraLayoutCard? CardControl(CameraLayoutCardViewModel card) =>
        LayoutRoot.GetVisualDescendants().OfType<CameraLayoutCard>().FirstOrDefault(control => ReferenceEquals(control.DataContext, card));

    /// <summary>
    /// The mode started: the focus goes to the first card in reading order (the toolbar when there is none), with a visible
    /// focus ring only when the mode was entered from the keyboard (T-076 5.5).
    /// </summary>
    internal void FocusFirst(bool fromKeyboard)
    {
        if (_viewModel is not { IsActive: true } viewModel)
        {
            return;
        }

        NavigationMethod method = fromKeyboard ? NavigationMethod.Tab : NavigationMethod.Unspecified;
        if (viewModel.Cards.Count > 0)
        {
            FocusCard(viewModel.Cards[0], method);
        }
        else
        {
            DoneButton.Focus(method);
        }
    }

    /// <summary>
    /// Esc with the focus outside the mode (e.g. nothing focused): like Esc on a card. Returns whether it was handled.
    /// </summary>
    internal bool HandleEscape()
    {
        if (_viewModel is not { IsActive: true } viewModel)
        {
            return false;
        }

        if (_flyout.IsOpen)
        {
            _flyout.Hide();
        }
        else if (viewModel.Dragged is not null)
        {
            // Only the drag ends (the card goes back); the press is over too, so moving on does not start a new drag.
            _press = null;
            EndDragVisuals();
            viewModel.CancelDrag();
        }
        else if (viewModel.Lifted is { } lifted)
        {
            viewModel.CancelLift();
            FocusCard(lifted, NavigationMethod.Tab);
        }
        else
        {
            viewModel.Cancel();
        }

        return true;
    }

    /// <summary>A screen reader expanded a card (UI Automation Expand): its popover opens.</summary>
    internal void Expand(CameraLayoutCard card)
    {
        if (_viewModel is { } viewModel && card.Card is { } cardViewModel)
        {
            _popoverFromKeyboard = true;
            viewModel.OpenPopover(cardViewModel);
        }
    }

    /// <summary>A screen reader collapsed a card: its popover closes.</summary>
    internal void Collapse(CameraLayoutCard card)
    {
        if (_viewModel?.PopoverCard is { } open && ReferenceEquals(open, card.Card))
        {
            _flyout.Hide();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as CameraLayoutModeViewModel;
        _popover.DataContext = _viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        base.OnDataContextChanged(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(CameraLayoutModeViewModel.IsActive) when !viewModel.IsActive:
                _press = null;
                EndDragVisuals();
                if (_flyout.IsOpen)
                {
                    _flyout.Hide();
                }

                break;
            case nameof(CameraLayoutModeViewModel.PopoverCard):
                SyncPopover(viewModel);
                break;
        }
    }

    /// <summary>The flyout follows the popover card of the view model.</summary>
    private void SyncPopover(CameraLayoutModeViewModel viewModel)
    {
        if (viewModel.PopoverCard is not { } card)
        {
            if (_flyout.IsOpen)
            {
                _flyout.Hide();
            }

            return;
        }

        if (_flyout.IsOpen)
        {
            return;
        }

        UpdateLayout();
        if (CardControl(card) is not { } control)
        {
            viewModel.ClosePopover();
            return;
        }

        // Towards the centre of the window (T-076 5.4); Avalonia flips it when it does not fit.
        _flyout.Placement = card.Anchor switch
        {
            OverlayAnchor.TopLeft or OverlayAnchor.BottomLeft => PlacementMode.RightEdgeAlignedTop,
            OverlayAnchor.TopRight or OverlayAnchor.BottomRight => PlacementMode.LeftEdgeAlignedTop,
            OverlayAnchor.TopCenter => PlacementMode.Bottom,
            _ => PlacementMode.Top,
        };
        _flyout.ShowAt(control);
    }

    private void OnFlyoutOpened(object? sender, EventArgs e)
    {
        if (_popoverFromKeyboard)
        {
            _popover.EmphasizeSwitch.Focus(NavigationMethod.Tab);
        }
    }

    private void OnFlyoutClosed(object? sender, EventArgs e)
    {
        bool fromKeyboard = _popoverFromKeyboard;
        _popoverFromKeyboard = false;
        if (_viewModel is not { } viewModel || viewModel.PopoverCard is not { } card)
        {
            return;
        }

        viewModel.ClosePopover();
        if (viewModel.IsActive)
        {
            // The focus returns to the card (T-076 5.5).
            FocusCard(card, fromKeyboard ? NavigationMethod.Tab : NavigationMethod.Pointer);
        }
    }

    private void OnPopoverKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _popoverFromKeyboard = true;
            _flyout.Hide();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { IsActive: true } viewModel || e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = HandleEscape();
            return;
        }

        if ((e.Source as Visual)?.FindAncestorOfType<CameraLayoutCard>(includeSelf: true) is not { Card: { } card } control)
        {
            return;
        }

        if (ReferenceEquals(viewModel.Lifted, card))
        {
            e.Handled = HandleLiftedKey(viewModel, card, e.Key);
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                e.Handled = true;
                if (viewModel.Lift(card))
                {
                    FocusCard(card, NavigationMethod.Tab);
                }

                break;
            case Key.Enter or Key.F2:
                e.Handled = true;
                _popoverFromKeyboard = true;
                viewModel.OpenPopover(card);
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                e.Handled = true;
                FocusNearest(control, Direction(e.Key));
                break;
        }
    }

    private bool HandleLiftedKey(CameraLayoutModeViewModel viewModel, CameraLayoutCardViewModel card, Key key)
    {
        switch (key)
        {
            case Key.Left or Key.Right or Key.Up or Key.Down:
                viewModel.MoveLifted(Direction(key));
                break;
            case Key.Home or Key.End:
                viewModel.MoveLiftedToEnd(top: key == Key.Home);
                break;
            case Key.Space or Key.Enter:
                viewModel.DropLifted();
                break;
            case Key.Tab:
                // Leaving the card puts it down where it is.
                viewModel.DropLifted();
                return false;
            default:
                return false;
        }

        FocusCard(card, NavigationMethod.Tab);
        return true;
    }

    private static CameraLayoutDirection Direction(Key key) => key switch
    {
        Key.Left => CameraLayoutDirection.Left,
        Key.Right => CameraLayoutDirection.Right,
        Key.Up => CameraLayoutDirection.Up,
        _ => CameraLayoutDirection.Down,
    };

    private void FocusNearest(CameraLayoutCard from, CameraLayoutDirection direction)
    {
        List<CameraLayoutCard> cards = [.. LayoutRoot.GetVisualDescendants().OfType<CameraLayoutCard>().Where(card => !ReferenceEquals(card, from) && card.IsEffectivelyVisible)];
        if (BoundsIn(from) is not { } origin)
        {
            return;
        }

        Rect[] bounds = [.. cards.Select(card => BoundsIn(card) ?? default)];
        if (CameraLayoutGeometry.Nearest(origin, bounds, direction) is { } index)
        {
            cards[index].Focus(NavigationMethod.Directional);
        }
    }

    /// <summary>Focuses the control of <paramref name="card"/> (laid out first: a moved card may have a new control).</summary>
    private void FocusCard(CameraLayoutCardViewModel card, NavigationMethod method)
    {
        UpdateLayout();
        CardControl(card)?.Focus(method);
    }

    private Rect? BoundsIn(Control control) =>
        control.TranslatePoint(default, LayoutRoot) is { } topLeft ? new Rect(topLeft, control.Bounds.Size) : null;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is not { IsActive: true, IsSaving: false } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Every press inside the mode stays here (the image below must not switch modes or move the window).
        if ((e.Source as Visual)?.FindAncestorOfType<CameraLayoutCard>(includeSelf: true) is not { Card: not null } card)
        {
            if (e.Source == LayoutRoot || e.Source is Border { Classes: var classes } && classes.Contains("dim"))
            {
                e.Handled = true;
            }

            return;
        }

        e.Handled = true;
        card.Focus(NavigationMethod.Pointer);
        _press = (card, e.GetPosition(card));
        e.Pointer.Capture(card);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_press is not { } press || _viewModel is not { } viewModel)
        {
            return;
        }

        Point inCard = e.GetPosition(press.Card);
        if (_draggedCard is null)
        {
            Vector moved = inCard - press.Origin;
            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
            {
                return;
            }

            if (press.Card.Card is not { } card || !viewModel.BeginDrag(card))
            {
                _press = null;
                return;
            }

            _draggedCard = press.Card;
            // The zone of the dragged card (its container in the zones panel) above the other zones and the toolbar.
            _draggedZone = press.Card.GetVisualAncestors().OfType<Control>().FirstOrDefault(control => control.GetVisualParent() is CameraLayoutZonesPanel);
            _draggedZone?.SetValue(ZIndexProperty, 10);
            Zones.SetValue(ZIndexProperty, 10);
        }

        e.Handled = true;
        // The card follows the pointer with the grip kept; transforms do not change the layout, so nothing else moves.
        Vector offset = inCard - press.Origin;
        Vector current = press.Card.RenderTransform is TranslateTransform translate ? new Vector(translate.X, translate.Y) : default;
        press.Card.RenderTransform = new TranslateTransform(current.X + offset.X, current.Y + offset.Y);
        UpdateDropTarget(viewModel, e.GetPosition(LayoutRoot));
    }

    private void UpdateDropTarget(CameraLayoutModeViewModel viewModel, Point pointer)
    {
        List<CameraLayoutZoneBounds> zones = [];
        Dictionary<OverlayAnchor, Control> zoneControls = [];
        if (Zones.ItemsPanelRoot is { } panel)
        {
            foreach (Control zone in panel.Children)
            {
                if (zone.DataContext is CameraLayoutZoneViewModel { Anchor: var anchor } && BoundsIn(zone) is { } bounds)
                {
                    zones.Add(new CameraLayoutZoneBounds(anchor, bounds));
                    zoneControls[anchor] = zone;
                }
            }
        }

        if (CameraLayoutGeometry.TargetZone(pointer, zones, BoundsIn(Toolbar)) is not { } target)
        {
            viewModel.DragOver(null, 0);
            return;
        }

        // The other cards of the zone, top to bottom, where they are laid out (not where a dragged one is drawn).
        List<Rect> others = [];
        foreach (CameraLayoutCard card in zoneControls[target].GetVisualDescendants().OfType<CameraLayoutCard>())
        {
            if (!ReferenceEquals(card, _draggedCard) && card.GetVisualParent() is Control container && BoundsIn(container) is { } bounds)
            {
                others.Add(bounds);
            }
        }

        others.Sort(static (a, b) => a.Y.CompareTo(b.Y));
        viewModel.DragOver(target, CameraLayoutGeometry.InsertIndex(pointer.Y, others));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_press is not { } press || _viewModel is not { } viewModel)
        {
            return;
        }

        e.Handled = true;
        _press = null;
        CameraLayoutCardViewModel? card = press.Card.Card;
        if (_draggedCard is not null)
        {
            EndDragVisuals();
            viewModel.Drop();
            if (card is not null)
            {
                FocusCard(card, NavigationMethod.Pointer);
            }
        }
        else if (card is not null)
        {
            // A click: selected, its popover opens.
            _popoverFromKeyboard = false;
            viewModel.OpenPopover(card);
        }

        e.Pointer.Capture(null);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_draggedCard is null)
        {
            _press = null;
            return;
        }

        if (_press is not null)
        {
            // Lost without a release (another window took the pointer): the card goes back.
            _press = null;
            EndDragVisuals();
            _viewModel?.CancelDrag();
        }
    }

    private void EndDragVisuals()
    {
        if (_draggedCard is { } card)
        {
            card.RenderTransform = null;
        }

        _draggedZone?.ClearValue(ZIndexProperty);
        Zones.ClearValue(ZIndexProperty);
        _draggedCard = null;
        _draggedZone = null;
    }
}
