using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Cameras.Layout;

/// <summary>How the layout mode ended (<see cref="CameraLayoutModeViewModel.Ended"/>).</summary>
public enum CameraLayoutEndReason
{
    /// <summary>"Done": the draft was saved.</summary>
    Saved,

    /// <summary>Cancel, Esc, a mode switch or the window closing: the draft was discarded.</summary>
    Discarded,
}

/// <summary>The layout mode ended (<see cref="CameraLayoutModeViewModel.Ended"/>).</summary>
public sealed class CameraLayoutEndedEventArgs(CameraLayoutEndReason reason) : EventArgs
{
    public CameraLayoutEndReason Reason { get; } = reason;
}

/// <summary>
/// The layout mode of one camera window (T-091, variant C of the T-076 design): a draft of the printer's overlay layout
/// edited in the normal window — cards moved between the six anchors and within one, their size and emphasis changed,
/// the defaults restored — and saved only by "Done" (through <see cref="ICameraOverlayLayoutWriter"/>, which announces the
/// saved layout to the settings window), discarded by Cancel and Esc. The widgets of the overlay show the draft meanwhile
/// (<see cref="CameraOverlayViewModel.BeginPreview"/>).
/// </summary>
/// <remarks>
/// <para>
/// No Avalonia here: the view passes gestures (clicks, keys, the target of a drag measured on the screen) and shows the
/// state. Positions in a zone are counted as seen on the screen, from the top (the cards of a bottom anchor stack upward
/// from the window edge); only the cards drawn in the window count, while the model positions also count hidden and
/// unregistered widgets of the anchor, which keep their place relative to the drawn ones.
/// </para>
/// <para>
/// Keyboard (T-076 5.5): Space picks the focused card up; the arrow keys, Home and End move it in the draft right away
/// (Left / Right to the next zone of the row at the same position, Up / Down one position, continuing into the other zone
/// of the column), Space or Enter drop it and Esc puts it back where it was picked up. Every step is announced to screen
/// readers (<see cref="Announcement"/>). A mouse drag changes the draft only when the card is dropped.
/// </para>
/// <para>UI thread only.</para>
/// </remarks>
public sealed partial class CameraLayoutModeViewModel : ObservableObject, IDisposable
{
    private readonly CameraOverlayViewModel _overlay;
    private readonly ICameraOverlayLayoutWriter? _writer;
    private readonly ILogger _logger;
    private readonly CameraLayoutZoneViewModel[] _zones;
    private readonly Dictionary<string, CameraLayoutCardViewModel> _cardsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly RelayCommand _resetCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly AsyncRelayCommand _doneCommand;

    private CameraOverlayLayout? _draft;
    private CameraLayoutCardViewModel[] _cards = [];
    private CameraLayoutCardViewModel? _selected;
    private CameraLayoutCardViewModel? _popoverCard;
    private CameraLayoutCardViewModel? _lifted;
    private CameraOverlayLayout? _liftOrigin;
    private CameraLayoutCardViewModel? _dragged;
    private OverlayAnchor? _dropAnchor;
    private int _dropIndex = -1;
    private bool _isActive;
    private bool _isSaving;
    private bool _hasSaveError;
    private bool _disposed;
    private string _announcement = string.Empty;

    /// <param name="printerId">The printer whose layout is edited.</param>
    /// <param name="overlay">The overlay of the window: its widgets are the cards, it previews the draft.</param>
    /// <param name="writer">Saves the layout ("Done"); <see langword="null"/>: saving always fails (shown as an error).</param>
    /// <param name="logger">Receives failed saves.</param>
    internal CameraLayoutModeViewModel(string printerId, CameraOverlayViewModel overlay, ICameraOverlayLayoutWriter? writer, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(logger);
        PrinterId = printerId;
        _overlay = overlay;
        _writer = writer;
        _logger = logger;
        _zones = [.. OverlayAnchors.All.Select(static anchor => new CameraLayoutZoneViewModel(anchor))];
        _resetCommand = new RelayCommand(Reset, () => _isActive && !_isSaving);
        _cancelCommand = new RelayCommand(Cancel, () => _isActive && !_isSaving);
        _doneCommand = new AsyncRelayCommand(SaveAsync, () => _isActive && !_isSaving);
        _overlay.SavedLayoutChanged += OnSavedLayoutChanged;
    }

    /// <summary>The layout mode ended (saved or discarded); raised after <see cref="IsActive"/> turned off.</summary>
    public event EventHandler<CameraLayoutEndedEventArgs>? Ended;

    public string PrinterId { get; }

    /// <summary>The mode is on: the window shows the zones, the cards and the toolbar.</summary>
    public bool IsActive => _isActive;

    /// <summary>"Done" is writing the layout; the mode ignores input meanwhile.</summary>
    public bool IsSaving => _isSaving;

    /// <summary>The last save failed: the toolbar says so, the mode and the draft stay (T-076 5.2).</summary>
    public bool HasSaveError => _hasSaveError;

    /// <summary>The layout being edited; <see langword="null"/> while the mode is off.</summary>
    public CameraOverlayLayout? Draft => _draft;

    /// <summary>The six zones in <see cref="OverlayAnchor"/> order (also the reading and Tab order, T-076 5.5).</summary>
    public IReadOnlyList<CameraLayoutZoneViewModel> Zones => _zones;

    /// <summary>Every card in reading order: zones in <see cref="OverlayAnchor"/> order, each top to bottom on the screen.</summary>
    public IReadOnlyList<CameraLayoutCardViewModel> Cards => _cards;

    /// <summary>The selected card (accent outline, the popover's card).</summary>
    public CameraLayoutCardViewModel? Selected => _selected;

    /// <summary>The card picked up with the keyboard.</summary>
    public CameraLayoutCardViewModel? Lifted => _lifted;

    /// <summary>The card dragged with the mouse.</summary>
    public CameraLayoutCardViewModel? Dragged => _dragged;

    /// <summary>The card whose popover is open.</summary>
    public CameraLayoutCardViewModel? PopoverCard => _popoverCard;

    public bool IsPopoverOpen => _popoverCard is not null;

    /// <summary>The text screen readers announce (a polite live region): entering, each keyboard step, drops, results.</summary>
    public string Announcement
    {
        get => _announcement;
        private set => SetProperty(ref _announcement, value);
    }

    /// <summary>"Restore defaults": the default layout in the draft, without asking (Cancel still undoes it).</summary>
    public IRelayCommand ResetCommand => _resetCommand;

    /// <summary>"Cancel" (and Esc): the draft is discarded.</summary>
    public IRelayCommand CancelCommand => _cancelCommand;

    /// <summary>"Done": the draft is saved.</summary>
    public IAsyncRelayCommand DoneCommand => _doneCommand;

    /// <summary>The popover's title: the name of its widget.</summary>
    public string PopoverTitle => _popoverCard?.Name ?? string.Empty;

    /// <summary>The popover's "Highlight" switch.</summary>
    public bool PopoverEmphasized
    {
        get => _popoverCard?.IsEmphasized ?? false;
        set
        {
            if (_popoverCard is { } card)
            {
                SetEmphasized(card, value);
            }
        }
    }

    public bool IsPopoverSmall
    {
        get => _popoverCard?.Size == OverlayWidgetSize.Small;
        set => SetPopoverSize(OverlayWidgetSize.Small, value);
    }

    public bool IsPopoverMedium
    {
        get => _popoverCard?.Size == OverlayWidgetSize.Medium;
        set => SetPopoverSize(OverlayWidgetSize.Medium, value);
    }

    public bool IsPopoverLarge
    {
        get => _popoverCard?.Size == OverlayWidgetSize.Large;
        set => SetPopoverSize(OverlayWidgetSize.Large, value);
    }

    /// <summary>The popover's widget has the small size (an unsupported size is disabled, T-076 5.4).</summary>
    public bool CanPopoverSmall => _popoverCard?.Supports(OverlayWidgetSize.Small) ?? false;

    public bool CanPopoverMedium => _popoverCard?.Supports(OverlayWidgetSize.Medium) ?? false;

    public bool CanPopoverLarge => _popoverCard?.Supports(OverlayWidgetSize.Large) ?? false;

    /// <summary>The zone of <paramref name="anchor"/>.</summary>
    public CameraLayoutZoneViewModel Zone(OverlayAnchor anchor) =>
        OverlayAnchors.IsDefined(anchor) ? _zones[(int)anchor] : throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unknown overlay anchor.");

    /// <summary>The card of <paramref name="widgetId"/>; <see langword="null"/> when the window draws no such widget.</summary>
    public CameraLayoutCardViewModel? Card(string widgetId) => _cardsById.GetValueOrDefault(widgetId);

    /// <summary>The anchor's name in the current language: the corners as in the camera window settings, the centres own texts.</summary>
    public static string AnchorName(OverlayAnchor anchor) => anchor switch
    {
        OverlayAnchor.TopLeft => Strings.SettingsCameraWindowCornerTopLeft,
        OverlayAnchor.TopCenter => Strings.CameraLayoutAnchorTopCenter,
        OverlayAnchor.TopRight => Strings.SettingsCameraWindowCornerTopRight,
        OverlayAnchor.BottomLeft => Strings.SettingsCameraWindowCornerBottomLeft,
        OverlayAnchor.BottomCenter => Strings.CameraLayoutAnchorBottomCenter,
        OverlayAnchor.BottomRight => Strings.SettingsCameraWindowCornerBottomRight,
        _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unknown overlay anchor."),
    };

    /// <summary>
    /// Starts the mode with <paramref name="startingLayout"/> as the draft (a draft of the settings window, T-091 Q4), or the
    /// saved layout when <see langword="null"/>. While the mode is on already, the edit goes on unchanged (Q3).
    /// </summary>
    /// <returns>Whether the mode is on.</returns>
    public bool Start(CameraOverlayLayout? startingLayout = null)
    {
        if (_disposed)
        {
            return false;
        }

        if (_isActive)
        {
            return true;
        }

        _draft = startingLayout ?? _overlay.SavedLayout;
        _hasSaveError = false;
        _overlay.BeginPreview(_draft);
        RebuildCards();
        SetActive(true);
        OnPropertyChanged(nameof(Draft));
        OnPropertyChanged(nameof(HasSaveError));
        Announce(CameraLayoutTexts.Format(Strings.CameraLayoutAnnounceEnter, Strings.CameraLayoutKeyboardHint));
        return true;
    }

    /// <summary>Cancel, Esc, a mode switch, the window closing: the draft is discarded (nothing is written). Ignored while saving.</summary>
    public void Cancel()
    {
        if (!_isActive || _isSaving)
        {
            return;
        }

        End(saved: null);
    }

    /// <summary>
    /// The window leaves the normal window (mini preview, full screen): the mode ends at once, also during a save. A save
    /// already on its way still writes; the monitored settings then bring that layout to the window.
    /// </summary>
    internal void Abandon()
    {
        if (_isActive && !_disposed)
        {
            End(saved: null);
        }
    }

    /// <summary>"Restore defaults" (T-077 <see cref="CameraOverlayLayout.ResetToDefault"/>) in the draft.</summary>
    public void Reset()
    {
        if (!CanEdit)
        {
            return;
        }

        CancelLiftAndDrag();
        SetDraft(_draft!.ResetToDefault());
        Announce(Strings.CameraLayoutAnnounceReset);
    }

    /// <summary>
    /// "Done": writes the draft (once, even when pressed again meanwhile) and, when written, ends the mode with the draft as
    /// the window's layout. A failed write keeps the mode and the draft and shows the error (T-076 5.2).
    /// </summary>
    /// <returns>Whether the layout was saved.</returns>
    public async Task<bool> SaveAsync()
    {
        if (!_isActive || _isSaving)
        {
            return false;
        }

        // A card picked up with the keyboard stays where it is now.
        DropLiftedSilently();
        CancelDrag();
        CameraOverlayLayout draft = _draft!;
        SetSaving(true);
        SetSaveError(false);
        bool saved = false;
        try
        {
            saved = _writer is not null && await _writer.SaveAsync(PrinterId, draft, CancellationToken.None).ConfigureAwait(true);
            if (!saved)
            {
                LogNotSaved();
            }
        }
        catch (Exception ex)
        {
            // Any failure (the file, no data folder, a bug) is shown in the toolbar; it must not end the app from the command.
            LogSaveFailed(ex);
        }
        finally
        {
            SetSaving(false);
        }

        if (!_isActive || _disposed)
        {
            // The window closed meanwhile; what was written stays written.
            return saved;
        }

        if (!saved)
        {
            SetSaveError(true);
            Announce(Strings.CameraLayoutSaveError);
            return false;
        }

        End(saved: draft);
        return true;
    }

    /// <summary>A click on a card (or Enter / F2): the card is selected.</summary>
    public void Select(CameraLayoutCardViewModel? card)
    {
        if (card is not null && !IsCard(card))
        {
            return;
        }

        if (ReferenceEquals(_selected, card))
        {
            return;
        }

        if (_selected is { } previous)
        {
            previous.IsSelected = false;
        }

        _selected = card;
        if (card is not null)
        {
            card.IsSelected = true;
        }

        OnPropertyChanged(nameof(Selected));
    }

    /// <summary>Selects <paramref name="card"/> and opens its popover (highlight, size); not while a card is moved.</summary>
    /// <returns>Whether the popover is open for the card.</returns>
    public bool OpenPopover(CameraLayoutCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!CanEdit || !IsCard(card) || _lifted is not null || _dragged is not null)
        {
            return false;
        }

        Select(card);
        if (ReferenceEquals(_popoverCard, card))
        {
            return true;
        }

        if (_popoverCard is { } previous)
        {
            previous.IsPopoverOpen = false;
        }

        _popoverCard = card;
        card.IsPopoverOpen = true;
        OnPopoverChanged();
        return true;
    }

    /// <summary>The popover closed (Esc, a click outside); the selection stays.</summary>
    public void ClosePopover()
    {
        if (_popoverCard is not { } card)
        {
            return;
        }

        card.IsPopoverOpen = false;
        _popoverCard = null;
        OnPopoverChanged();
    }

    /// <summary>The emphasis of <paramref name="card"/> in the draft.</summary>
    public void SetEmphasized(CameraLayoutCardViewModel card, bool emphasized)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (CanEdit && IsCard(card))
        {
            SetDraft(_draft!.WithEmphasized(card.WidgetId, emphasized));
        }
    }

    /// <summary>The size of <paramref name="card"/> in the draft; a size the widget does not have is ignored.</summary>
    public void SetSize(CameraLayoutCardViewModel card, OverlayWidgetSize size)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (CanEdit && IsCard(card) && card.Supports(size))
        {
            SetDraft(_draft!.WithSize(card.WidgetId, size));
        }
    }

    /// <summary>Space on a card: picks it up; the arrow keys move it from now on.</summary>
    /// <returns>Whether the card is picked up.</returns>
    public bool Lift(CameraLayoutCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!CanEdit || !IsCard(card) || _dragged is not null)
        {
            return false;
        }

        if (_lifted is not null)
        {
            return ReferenceEquals(_lifted, card);
        }

        ClosePopover();
        Select(card);
        _lifted = card;
        _liftOrigin = _draft;
        card.IsLifted = true;
        MarkTarget(card.Anchor, insertIndex: -1);
        OnPropertyChanged(nameof(Lifted));
        Announce(CameraLayoutTexts.Format(Strings.CameraLayoutAnnounceLifted, card.Name));
        return true;
    }

    /// <summary>
    /// An arrow key on the picked-up card: Left / Right to the next zone of the row at the same position from the window
    /// edge (or the last one), Up / Down one position on the screen, past the end of a zone into the other zone of the column.
    /// </summary>
    /// <returns>Whether the card moved.</returns>
    public bool MoveLifted(CameraLayoutDirection direction)
    {
        if (_lifted is not { } card || !CanEdit)
        {
            return false;
        }

        OverlayAnchor anchor = card.Anchor;
        int others = card.Count - 1;
        int index = card.ScreenIndex;
        (OverlayAnchor Anchor, int ScreenIndex)? target = direction switch
        {
            CameraLayoutDirection.Left or CameraLayoutDirection.Right => Beside(anchor, direction) is { } beside
                ? (beside, ScreenFromEdge(beside, Math.Min(EdgeFromScreen(anchor, index, others), OtherCards(beside, card)), OtherCards(beside, card)))
                : null,
            CameraLayoutDirection.Up when index > 0 => (anchor, index - 1),
            CameraLayoutDirection.Up when !OverlayAnchors.IsTop(anchor) => (Above(anchor), OtherCards(Above(anchor), card)),
            CameraLayoutDirection.Down when index < others => (anchor, index + 1),
            CameraLayoutDirection.Down when OverlayAnchors.IsTop(anchor) => (Below(anchor), 0),
            _ => null,
        };

        return target is { } place && MoveLiftedTo(card, place.Anchor, place.ScreenIndex);
    }

    /// <summary>Home / End on the picked-up card: the top or the bottom of its zone on the screen.</summary>
    /// <returns>Whether the card moved.</returns>
    public bool MoveLiftedToEnd(bool top)
    {
        if (_lifted is not { } card || !CanEdit)
        {
            return false;
        }

        return MoveLiftedTo(card, card.Anchor, top ? 0 : card.Count - 1);
    }

    /// <summary>Space or Enter on the picked-up card: it stays where it is (announced).</summary>
    /// <returns>Whether a card was dropped.</returns>
    public bool DropLifted()
    {
        if (_lifted is not { } card)
        {
            return false;
        }

        DropLiftedSilently();
        Announce(CameraLayoutTexts.Format(Strings.CameraLayoutAnnounceDropped, card.Name, AnchorName(card.Anchor), card.ScreenIndex + 1));
        return true;
    }

    /// <summary>Esc on the picked-up card: it goes back to where it was picked up; the mode stays.</summary>
    /// <returns>Whether a card was put back.</returns>
    public bool CancelLift()
    {
        if (_lifted is not { } card)
        {
            return false;
        }

        CameraOverlayLayout origin = _liftOrigin!;
        _lifted = null;
        _liftOrigin = null;
        card.IsLifted = false;
        ClearTarget();
        if (_draft != origin && !_isSaving)
        {
            SetDraft(origin);
        }

        OnPropertyChanged(nameof(Lifted));
        Announce(Strings.CameraLayoutAnnounceCancelled);
        return true;
    }

    /// <summary>A mouse drag of <paramref name="card"/> started (pressed and moved more than 4 DIP).</summary>
    /// <returns>Whether the card is dragged.</returns>
    public bool BeginDrag(CameraLayoutCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!CanEdit || !IsCard(card) || _lifted is not null)
        {
            return false;
        }

        if (_dragged is not null)
        {
            return ReferenceEquals(_dragged, card);
        }

        ClosePopover();
        Select(card);
        _dragged = card;
        card.IsDragged = true;
        OnPropertyChanged(nameof(Dragged));
        return true;
    }

    /// <summary>
    /// The pointer of the drag is over <paramref name="anchor"/> (<see langword="null"/>: no zone, e.g. over the toolbar)
    /// at <paramref name="insertIndex"/> among the other cards of the zone, from the top of the screen.
    /// </summary>
    public void DragOver(OverlayAnchor? anchor, int insertIndex)
    {
        if (_dragged is not { } card)
        {
            return;
        }

        if (anchor is not { } target)
        {
            ClearTarget();
            return;
        }

        int others = OtherCards(target, card);
        MarkTarget(target, Math.Clamp(insertIndex, 0, others));
    }

    /// <summary>The mouse button was released: the dragged card goes to the target, or back without one.</summary>
    /// <returns>Whether the draft changed.</returns>
    public bool Drop()
    {
        if (_dragged is not { } card)
        {
            return false;
        }

        OverlayAnchor? anchor = _dropAnchor;
        int index = _dropIndex;
        EndDrag(card);
        if (anchor is not { } target || !CanEdit)
        {
            return false;
        }

        CameraOverlayLayout before = _draft!;
        SetDraft(Moved(before, card, target, index));
        Announce(CameraLayoutTexts.Format(Strings.CameraLayoutAnnounceDropped, card.Name, AnchorName(card.Anchor), card.ScreenIndex + 1));
        return _draft != before;
    }

    /// <summary>Esc during a mouse drag (or the drag lost its pointer): the card goes back; the mode stays.</summary>
    public void CancelDrag()
    {
        if (_dragged is { } card)
        {
            EndDrag(card);
        }
    }

    /// <summary>The language changed: names and the texts of the cards and zones.</summary>
    public void RefreshTexts()
    {
        foreach (CameraLayoutZoneViewModel zone in _zones)
        {
            zone.RefreshTexts();
        }

        foreach (CameraLayoutCardViewModel card in _cardsById.Values)
        {
            card.RefreshTexts();
        }

        OnPropertyChanged(nameof(PopoverTitle));
    }

    /// <summary>The window closes: a draft is discarded (an ongoing save completes, its result is ignored).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _overlay.SavedLayoutChanged -= OnSavedLayoutChanged;
        if (_isActive)
        {
            End(saved: null, announce: false);
        }

        _disposed = true;
    }

    private bool CanEdit => _isActive && !_isSaving && !_disposed;

    private bool IsCard(CameraLayoutCardViewModel card) => _cardsById.TryGetValue(card.WidgetId, out CameraLayoutCardViewModel? known) && ReferenceEquals(known, card);

    private void End(CameraOverlayLayout? saved, bool announce = true)
    {
        CancelLiftAndDrag();
        ClosePopover();
        Select(null);
        _overlay.EndPreview(saved);
        _draft = null;
        _hasSaveError = false;
        foreach (CameraLayoutZoneViewModel zone in _zones)
        {
            zone.SetCards([]);
            zone.SetTarget(false, -1);
        }

        _cards = [];
        _cardsById.Clear();
        SetActive(false);
        OnPropertyChanged(nameof(Draft));
        OnPropertyChanged(nameof(Cards));
        OnPropertyChanged(nameof(HasSaveError));
        if (announce)
        {
            Announce(saved is null ? Strings.CameraLayoutAnnounceDiscarded : Strings.CameraLayoutAnnounceSaved);
        }

        Ended?.Invoke(this, new CameraLayoutEndedEventArgs(saved is null ? CameraLayoutEndReason.Discarded : CameraLayoutEndReason.Saved));
    }

    private void SetActive(bool active)
    {
        if (_isActive == active)
        {
            return;
        }

        _isActive = active;
        OnPropertyChanged(nameof(IsActive));
        NotifyCommands();
    }

    private void SetSaving(bool saving)
    {
        if (_isSaving == saving)
        {
            return;
        }

        _isSaving = saving;
        OnPropertyChanged(nameof(IsSaving));
        NotifyCommands();
    }

    private void SetSaveError(bool error)
    {
        if (_hasSaveError != error)
        {
            _hasSaveError = error;
            OnPropertyChanged(nameof(HasSaveError));
        }
    }

    private void NotifyCommands()
    {
        _resetCommand.NotifyCanExecuteChanged();
        _cancelCommand.NotifyCanExecuteChanged();
        _doneCommand.NotifyCanExecuteChanged();
    }

    /// <summary>A new draft: the overlay widgets follow it, the cards are placed again.</summary>
    private void SetDraft(CameraOverlayLayout draft)
    {
        if (draft == _draft)
        {
            return;
        }

        _draft = draft;
        _overlay.Preview(draft);
        RebuildCards();
        OnPropertyChanged(nameof(Draft));
        OnPopoverChanged();
    }

    /// <summary>
    /// The cards of the draft: one per widget the overlay draws (visible in the draft and registered), in their zones as
    /// seen on the screen. Cards keep their instance; cards whose widget left the draft drop their state.
    /// </summary>
    private void RebuildCards()
    {
        CameraOverlayLayout draft = _draft!;
        List<CameraLayoutCardViewModel> all = new(draft.Widgets.Count);
        HashSet<string> present = new(StringComparer.OrdinalIgnoreCase);
        foreach (CameraLayoutZoneViewModel zone in _zones)
        {
            List<(CameraLayoutCardViewModel Card, OverlayWidgetPlacement Placement)> fromEdge = [];
            foreach (OverlayWidgetPlacement placement in draft.InAnchor(zone.Anchor))
            {
                if (CardFor(placement.WidgetId) is { } card)
                {
                    fromEdge.Add((card, placement));
                }
            }

            int count = fromEdge.Count;
            CameraLayoutCardViewModel[] onScreen = new CameraLayoutCardViewModel[count];
            for (int edge = 0; edge < count; edge++)
            {
                (CameraLayoutCardViewModel card, OverlayWidgetPlacement placement) = fromEdge[edge];
                int screen = ScreenFromEdge(zone.Anchor, edge, count - 1);
                onScreen[screen] = card;
                card.Update(zone.Anchor, screen, count, placement.Size, placement.Emphasized);
                present.Add(card.WidgetId);
            }

            zone.SetCards(onScreen);
            all.AddRange(onScreen);
        }

        foreach (string gone in _cardsById.Keys.Where(id => !present.Contains(id)).ToList())
        {
            CameraLayoutCardViewModel card = _cardsById[gone];
            _cardsById.Remove(gone);
            if (ReferenceEquals(_lifted, card))
            {
                _lifted = null;
                _liftOrigin = null;
                card.IsLifted = false;
                ClearTarget();
                OnPropertyChanged(nameof(Lifted));
            }

            if (ReferenceEquals(_dragged, card))
            {
                EndDrag(card);
            }

            if (ReferenceEquals(_popoverCard, card))
            {
                ClosePopover();
            }

            if (ReferenceEquals(_selected, card))
            {
                Select(null);
            }
        }

        if (!_cards.AsSpan().SequenceEqual(all.ToArray()))
        {
            _cards = [.. all];
            OnPropertyChanged(nameof(Cards));
        }
    }

    private CameraLayoutCardViewModel? CardFor(string widgetId)
    {
        if (_overlay.FindWidget(widgetId) is not { } widget || _overlay.FindDefinition(widgetId) is not { } definition)
        {
            return null;
        }

        if (_cardsById.TryGetValue(widgetId, out CameraLayoutCardViewModel? card) && ReferenceEquals(card.Widget, widget))
        {
            return card;
        }

        card = new CameraLayoutCardViewModel(widget, definition);
        _cardsById[widgetId] = card;
        return card;
    }

    private bool MoveLiftedTo(CameraLayoutCardViewModel card, OverlayAnchor anchor, int screenIndex)
    {
        CameraOverlayLayout before = _draft!;
        SetDraft(Moved(before, card, anchor, screenIndex));
        if (_draft == before)
        {
            return false;
        }

        MarkTarget(card.Anchor, insertIndex: -1);
        Announce(CameraLayoutTexts.Format(Strings.CameraLayoutAnnouncePosition, card.Name, AnchorName(card.Anchor), card.ScreenIndex + 1, card.Count));
        return true;
    }

    /// <summary>
    /// <paramref name="layout"/> with <paramref name="card"/> in <paramref name="anchor"/> at <paramref name="screenIndex"/>
    /// among the other cards of that zone (from the top of the screen). The model position counts every widget of the
    /// anchor: the card goes right before the drawn card it lands in front of (from the window edge), or right after the last
    /// drawn one.
    /// </summary>
    private CameraOverlayLayout Moved(CameraOverlayLayout layout, CameraLayoutCardViewModel card, OverlayAnchor anchor, int screenIndex)
    {
        List<OverlayWidgetPlacement> others = [.. layout.InAnchor(anchor).Where(p => !string.Equals(p.WidgetId, card.WidgetId, StringComparison.OrdinalIgnoreCase))];
        int drawn = others.Count(p => _cardsById.ContainsKey(p.WidgetId));
        int edge = EdgeFromScreen(anchor, Math.Clamp(screenIndex, 0, drawn), drawn);
        int seen = 0;
        int position = -1;
        int lastDrawn = -1;
        for (int i = 0; i < others.Count; i++)
        {
            if (!_cardsById.ContainsKey(others[i].WidgetId))
            {
                continue;
            }

            if (seen++ == edge)
            {
                position = i;
                break;
            }

            lastDrawn = i;
        }

        if (position < 0)
        {
            position = lastDrawn < 0 ? others.Count : lastDrawn + 1;
        }

        return layout.MoveTo(card.WidgetId, anchor, position);
    }

    /// <summary>Cards in <paramref name="anchor"/> besides <paramref name="card"/>.</summary>
    private int OtherCards(OverlayAnchor anchor, CameraLayoutCardViewModel card)
    {
        IReadOnlyList<CameraLayoutCardViewModel> cards = _zones[(int)anchor].Cards;
        return cards.Count - (cards.Contains(card) ? 1 : 0);
    }

    /// <summary>The position from the window edge of screen position <paramref name="screen"/> among <paramref name="others"/> + 1 slots.</summary>
    private static int EdgeFromScreen(OverlayAnchor anchor, int screen, int others) => OverlayAnchors.IsTop(anchor) ? screen : others - screen;

    private static int ScreenFromEdge(OverlayAnchor anchor, int edge, int others) => OverlayAnchors.IsTop(anchor) ? edge : others - edge;

    private static OverlayAnchor? Beside(OverlayAnchor anchor, CameraLayoutDirection direction)
    {
        int column = (int)anchor % 3;
        int next = column + (direction == CameraLayoutDirection.Left ? -1 : 1);
        return next is < 0 or > 2 ? null : (OverlayAnchor)(((int)anchor - column) + next);
    }

    private static OverlayAnchor Above(OverlayAnchor anchor) => (OverlayAnchor)((int)anchor - 3);

    private static OverlayAnchor Below(OverlayAnchor anchor) => (OverlayAnchor)((int)anchor + 3);

    private void DropLiftedSilently()
    {
        if (_lifted is not { } card)
        {
            return;
        }

        _lifted = null;
        _liftOrigin = null;
        card.IsLifted = false;
        ClearTarget();
        OnPropertyChanged(nameof(Lifted));
    }

    private void EndDrag(CameraLayoutCardViewModel card)
    {
        _dragged = null;
        card.IsDragged = false;
        ClearTarget();
        OnPropertyChanged(nameof(Dragged));
    }

    private void CancelLiftAndDrag()
    {
        if (_lifted is not null)
        {
            // Back to where it was picked up, like Esc (the caller replaces or discards the draft anyway).
            CameraLayoutCardViewModel card = _lifted;
            CameraOverlayLayout origin = _liftOrigin!;
            _lifted = null;
            _liftOrigin = null;
            card.IsLifted = false;
            ClearTarget();
            if (_draft is not null && _draft != origin && _isActive)
            {
                SetDraft(origin);
            }

            OnPropertyChanged(nameof(Lifted));
        }

        CancelDrag();
    }

    private void MarkTarget(OverlayAnchor anchor, int insertIndex)
    {
        _dropAnchor = anchor;
        _dropIndex = insertIndex;
        foreach (CameraLayoutZoneViewModel zone in _zones)
        {
            bool target = zone.Anchor == anchor;
            zone.SetTarget(target, target ? insertIndex : -1);
        }
    }

    private void ClearTarget()
    {
        _dropAnchor = null;
        _dropIndex = -1;
        foreach (CameraLayoutZoneViewModel zone in _zones)
        {
            zone.SetTarget(false, -1);
        }
    }

    private void SetPopoverSize(OverlayWidgetSize size, bool selected)
    {
        if (selected && _popoverCard is { } card)
        {
            SetSize(card, size);
        }
    }

    private void OnPopoverChanged()
    {
        OnPropertyChanged(nameof(PopoverCard));
        OnPropertyChanged(nameof(IsPopoverOpen));
        OnPropertyChanged(nameof(PopoverTitle));
        OnPropertyChanged(nameof(PopoverEmphasized));
        OnPropertyChanged(nameof(IsPopoverSmall));
        OnPropertyChanged(nameof(IsPopoverMedium));
        OnPropertyChanged(nameof(IsPopoverLarge));
        OnPropertyChanged(nameof(CanPopoverSmall));
        OnPropertyChanged(nameof(CanPopoverMedium));
        OnPropertyChanged(nameof(CanPopoverLarge));
    }

    /// <summary>
    /// The settings were saved elsewhere during the edit (T-091, robust): the draft keeps its own changes, but takes the
    /// visibility that the other save changed (shown or hidden widgets), so "Done" does not undo it.
    /// </summary>
    private void OnSavedLayoutChanged(object? sender, OverlaySavedLayoutChangedEventArgs e)
    {
        if (!_isActive || _draft is not { } draft)
        {
            return;
        }

        CameraOverlayLayout merged = WithSavedVisibility(draft, e.Previous, e.Current);
        if (_liftOrigin is { } origin)
        {
            // The place a picked-up card returns to follows the same visibility, so that Esc does not bring an old one back.
            _liftOrigin = WithSavedVisibility(origin, e.Previous, e.Current);
        }

        SetDraft(merged);
    }

    /// <summary>
    /// <paramref name="draft"/> with the visibility that changed from <paramref name="previous"/> to <paramref name="current"/>
    /// (the rest of the draft stays); a widget new in <paramref name="current"/> is added as it is there.
    /// </summary>
    internal static CameraOverlayLayout WithSavedVisibility(CameraOverlayLayout draft, CameraOverlayLayout previous, CameraOverlayLayout current)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        CameraOverlayLayout merged = draft;
        foreach (OverlayWidgetPlacement saved in current.Widgets)
        {
            if (merged.Find(saved.WidgetId) is null)
            {
                if (merged.Widgets.Count >= CameraOverlayLayout.MaxWidgets)
                {
                    continue;
                }

                merged = new CameraOverlayLayout([.. merged.Widgets, saved]);
            }
            else if (previous.Find(saved.WidgetId) is { } before && before.Visible != saved.Visible)
            {
                merged = merged.WithVisible(saved.WidgetId, saved.Visible);
            }
        }

        return merged;
    }

    /// <summary>
    /// Sets the polite live region; the same text twice in a row still changes it (a trailing space), so that screen
    /// readers announce a repeated step.
    /// </summary>
    private void Announce(string text) =>
        Announcement = string.Equals(_announcement, text, StringComparison.Ordinal) ? text + " " : text;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The camera overlay layout was not saved (the printer or the settings file was not found, or the file is from a newer version).")]
    private partial void LogNotSaved();

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the camera overlay layout failed.")]
    private partial void LogSaveFailed(Exception exception);
}
