using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Cameras.Layout;

/// <summary>
/// One widget card of the layout mode (T-091): the overlay widget it draws, where it is in the draft (zone and position as
/// seen on the screen) and its state in the mode (selected, picked up, dragged). Created and updated by
/// <see cref="CameraLayoutModeViewModel"/>; UI thread.
/// </summary>
public sealed class CameraLayoutCardViewModel : ObservableObject
{
    /// <summary>Prefix of the <c>AutomationId</c> of the cards (T-076 5.7): <c>CameraLayoutWidget-{id}</c>.</summary>
    public const string AutomationIdPrefix = "CameraLayoutWidget-";

    private OverlayAnchor _anchor;
    private int _screenIndex;
    private int _count = 1;
    private OverlayWidgetSize _size = OverlayWidgetSize.Medium;
    private bool _isEmphasized;
    private bool _isSelected;
    private bool _isLifted;
    private bool _isDragged;
    private bool _isPopoverOpen;
    private string _name;

    internal CameraLayoutCardViewModel(OverlayWidgetViewModel widget, IOverlayWidgetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(definition);
        Widget = widget;
        Definition = definition;
        WidgetId = definition.Id;
        AutomationId = AutomationIdPrefix + definition.Id;
        _name = NameOf(definition);
    }

    /// <summary>The widget's id in the layout.</summary>
    public string WidgetId { get; }

    /// <summary><c>CameraLayoutWidget-{id}</c>.</summary>
    public string AutomationId { get; }

    /// <summary>The overlay widget drawn in the card (its content and size follow the draft).</summary>
    public OverlayWidgetViewModel Widget { get; }

    /// <summary>The widget's name in the current language ("Status").</summary>
    public string Name => _name;

    /// <summary>The zone of the card in the draft.</summary>
    public OverlayAnchor Anchor => _anchor;

    /// <summary>Position in the zone as seen on the screen, from the top (0-based).</summary>
    public int ScreenIndex => _screenIndex;

    /// <summary>Cards in the zone.</summary>
    public int Count => _count;

    /// <summary>The size in the draft.</summary>
    public OverlayWidgetSize Size => _size;

    /// <summary>Emphasized in the draft.</summary>
    public bool IsEmphasized => _isEmphasized;

    /// <summary>The narrowest card of <see cref="Size"/>: the width of the placeholder card of a widget without data.</summary>
    public double MinCardWidth => Definition.MinWidth(_size);

    /// <summary>Selected (accent outline; the popover belongs to it).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Picked up with the keyboard (Space): arrow keys move it.</summary>
    public bool IsLifted
    {
        get => _isLifted;
        internal set
        {
            if (SetProperty(ref _isLifted, value))
            {
                OnPropertyChanged(nameof(IsRaised));
            }
        }
    }

    /// <summary>Dragged with the mouse: it follows the pointer, an outline stays at its place.</summary>
    public bool IsDragged
    {
        get => _isDragged;
        internal set
        {
            if (SetProperty(ref _isDragged, value))
            {
                OnPropertyChanged(nameof(IsRaised));
            }
        }
    }

    /// <summary>Picked up by the keyboard or the mouse: drawn raised (shadow, slightly larger).</summary>
    public bool IsRaised => _isLifted || _isDragged;

    /// <summary>Its popover is open (the expand/collapse state for screen readers).</summary>
    public bool IsPopoverOpen
    {
        get => _isPopoverOpen;
        internal set => SetProperty(ref _isPopoverOpen, value);
    }

    /// <summary>Name, zone and position for screen readers: "Status: Top left, position 1 of 2".</summary>
    public string AccessibleName =>
        CameraLayoutTexts.Format(Strings.CameraLayoutAnnouncePosition, _name, CameraLayoutModeViewModel.AnchorName(_anchor), _screenIndex + 1, _count);

    internal IOverlayWidgetDefinition Definition { get; }

    internal bool Supports(OverlayWidgetSize size) => Definition.SupportedSizes.Contains(size);

    /// <summary>The place and look of the card in the draft; raises only what changed.</summary>
    internal void Update(OverlayAnchor anchor, int screenIndex, int count, OverlayWidgetSize size, bool emphasized)
    {
        bool placeChanged = _anchor != anchor || _screenIndex != screenIndex || _count != count;
        bool sizeChanged = _size != size;
        _anchor = anchor;
        _screenIndex = screenIndex;
        _count = count;
        _size = size;
        if (placeChanged)
        {
            OnPropertyChanged(nameof(Anchor));
            OnPropertyChanged(nameof(ScreenIndex));
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(AccessibleName));
        }

        if (sizeChanged)
        {
            OnPropertyChanged(nameof(Size));
            OnPropertyChanged(nameof(MinCardWidth));
        }

        if (_isEmphasized != emphasized)
        {
            _isEmphasized = emphasized;
            OnPropertyChanged(nameof(IsEmphasized));
        }
    }

    /// <summary>The language changed.</summary>
    internal void RefreshTexts()
    {
        _name = NameOf(Definition);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(AccessibleName));
    }

    private static string NameOf(IOverlayWidgetDefinition definition) =>
        Strings.ResourceManager.GetString(definition.NameKey, Strings.Culture) ?? definition.Id;
}

/// <summary>
/// One of the six zones of the layout mode (T-091): its anchor, name, the cards in it as seen on the screen (top to bottom)
/// and whether it is the target of the card being moved, with the insert indicator among its items while a mouse drag points
/// at it. UI thread.
/// </summary>
public sealed class CameraLayoutZoneViewModel : ObservableObject
{
    /// <summary>Prefix of the <c>AutomationId</c> of the zones (T-076 5.7): <c>CameraLayoutAnchor{Anchor}</c>.</summary>
    public const string AutomationIdPrefix = "CameraLayoutAnchor";

    private static readonly CameraLayoutCardViewModel[] NoCards = [];

    // Changed in place (never replaced), so that the cards that stay keep their controls: the keyboard focus and the pointer
    // capture of a dragged card survive the insert indicator and moves of other cards.
    private readonly ObservableCollection<object> _items = [];
    private CameraLayoutCardViewModel[] _cards = NoCards;
    private bool _isTarget;
    private int _insertIndex = -1;
    private string _name;

    internal CameraLayoutZoneViewModel(OverlayAnchor anchor)
    {
        Anchor = anchor;
        AutomationId = AutomationIdPrefix + anchor;
        _name = CameraLayoutModeViewModel.AnchorName(anchor);
        Items = new ReadOnlyObservableCollection<object>(_items);
    }

    public OverlayAnchor Anchor { get; }

    /// <summary><c>CameraLayoutAnchor{Anchor}</c>.</summary>
    public string AutomationId { get; }

    /// <summary>The anchor's name in the current language ("Top right"), shown in an empty zone.</summary>
    public string Name => _name;

    public bool IsTop => OverlayAnchors.IsTop(Anchor);

    public bool IsBottom => !IsTop;

    public bool IsLeft => OverlayAnchors.IsLeft(Anchor);

    public bool IsRight => OverlayAnchors.IsRight(Anchor);

    public bool IsCenter => OverlayAnchors.IsCenter(Anchor);

    /// <summary>The top right zone: its cards start below the window buttons, like the anchor (T-076 3.2).</summary>
    public bool IsTopRight => Anchor == OverlayAnchor.TopRight;

    /// <summary>The cards of the zone as seen on the screen, top to bottom.</summary>
    public IReadOnlyList<CameraLayoutCardViewModel> Cards => _cards;

    /// <summary>
    /// <see cref="Cards"/> with the <see cref="CameraLayoutInsertMarker"/> where a dragged card would go; changed in place.
    /// </summary>
    public ReadOnlyObservableCollection<object> Items { get; }

    /// <summary>No card in the zone: it shows its name.</summary>
    public bool IsEmpty => _cards.Length == 0;

    /// <summary>The card being moved goes here: highlighted, "Drop here".</summary>
    public bool IsTarget => _isTarget;

    /// <summary>Where the insert indicator is among the cards (screen order, the dragged card left out); −1 = none.</summary>
    public int InsertIndex => _insertIndex;

    internal void SetCards(CameraLayoutCardViewModel[] cards)
    {
        bool wasEmpty = IsEmpty;
        if (_cards.AsSpan().SequenceEqual(cards))
        {
            return;
        }

        _cards = cards.Length == 0 ? NoCards : cards;
        OnPropertyChanged(nameof(Cards));
        if (wasEmpty != IsEmpty)
        {
            OnPropertyChanged(nameof(IsEmpty));
        }

        UpdateItems();
    }

    /// <summary>
    /// "Drop here" (T-076 5.3): the target of a mouse drag. A card picked up with the keyboard is in its target zone already,
    /// so the zone is only highlighted.
    /// </summary>
    public bool ShowsDropHere => _isTarget && _insertIndex >= 0;

    internal void SetTarget(bool isTarget, int insertIndex)
    {
        bool showed = ShowsDropHere;
        if (_isTarget != isTarget)
        {
            _isTarget = isTarget;
            OnPropertyChanged(nameof(IsTarget));
        }

        if (_insertIndex != insertIndex)
        {
            _insertIndex = insertIndex;
            OnPropertyChanged(nameof(InsertIndex));
            UpdateItems();
        }

        if (showed != ShowsDropHere)
        {
            OnPropertyChanged(nameof(ShowsDropHere));
        }
    }

    internal void RefreshTexts()
    {
        _name = CameraLayoutModeViewModel.AnchorName(Anchor);
        OnPropertyChanged(nameof(Name));
    }

    private void UpdateItems()
    {
        List<object> items = new(_cards.Length + 1);
        int shown = 0;
        foreach (CameraLayoutCardViewModel card in _cards)
        {
            if (!card.IsDragged && shown++ == _insertIndex)
            {
                items.Add(CameraLayoutInsertMarker.Instance);
            }

            items.Add(card);
        }

        if (_insertIndex >= 0 && _insertIndex >= shown)
        {
            items.Add(CameraLayoutInsertMarker.Instance);
        }

        Sync(_items, items);
    }

    /// <summary>
    /// Makes <paramref name="target"/> equal to <paramref name="desired"/> with single removals, moves and inserts, so
    /// that items in both keep their place in the collection (and their controls).
    /// </summary>
    internal static void Sync(ObservableCollection<object> target, List<object> desired)
    {
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (int i = 0; i < desired.Count; i++)
        {
            object item = desired[i];
            int current = target.IndexOf(item);
            if (current == i)
            {
                continue;
            }

            if (current < 0)
            {
                target.Insert(i, item);
            }
            else
            {
                target.Move(current, i);
            }
        }
    }
}

/// <summary>Where a dragged card would go among the cards of a zone (the insert indicator, T-076 5.3).</summary>
public sealed class CameraLayoutInsertMarker
{
    private CameraLayoutInsertMarker()
    {
    }

    public static CameraLayoutInsertMarker Instance { get; } = new();
}
