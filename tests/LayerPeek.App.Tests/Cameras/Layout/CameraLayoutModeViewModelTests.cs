using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Layout;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;
using static LayerPeek.Core.Cameras.Overlay.OverlayWidgetIds;

namespace LayerPeek.App.Tests.Cameras.Layout;

/// <summary>
/// The layout mode of the camera window (T-091): the draft of the printer's layout, moves into every anchor and position
/// (mouse and keyboard), size, emphasis, defaults, Cancel / Esc, Done through the layout writer, save errors, the mode
/// switch and the window closing, settings saved elsewhere meanwhile, and the screen reader announcements.
/// </summary>
public sealed class CameraLayoutModeViewModelTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly FakeCameraFeedFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakePrinterMonitor _monitor = new();
    private readonly MonitoredPrinters _printers = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly MemoryCameraWindowStateStore _states = new();
    private readonly FakeLayoutWriter _writer = new();
    private readonly List<CameraViewModel> _created = [];
    private readonly CultureScope _culture = new("en-US");

    public async ValueTask DisposeAsync()
    {
        _culture.Dispose();
        foreach (CameraViewModel viewModel in _created)
        {
            viewModel.Dispose();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Every anchor with every screen position "status" can take there in the default layout (others in the zone + 1).</summary>
    public static TheoryData<OverlayAnchor, int> EveryAnchorAndPosition()
    {
        TheoryData<OverlayAnchor, int> data = [];
        foreach (OverlayAnchor anchor in OverlayAnchors.All)
        {
            int others = CameraOverlayLayout.Default.InAnchor(anchor).Count(p => p.WidgetId != Status);
            for (int position = 0; position <= others; position++)
            {
                data.Add(anchor, position);
            }
        }

        return data;
    }

    private static PrinterDefinition Printer(CameraOverlayLayout layout, CameraWindowDefaults? window = null) =>
        Definition("mk4", "MK4") with { Camera = new CameraSettings(enabled: true, StreamUri) { Overlay = layout, Window = window ?? CameraWindowDefaults.Default } };

    private CameraViewModel Create(CameraOverlayLayout? layout = null, OverlayWidgetRegistry? widgets = null, CameraWindowDefaults? window = null, bool withWriter = true)
    {
        ReconnectingCameraFeed feed = new(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraViewModel viewModel = new(
            CameraTarget.From(Printer(layout ?? CameraOverlayLayout.Default, window))!,
            feed,
            _monitor,
            _printers,
            _dispatcher,
            _states,
            _time,
            CameraFeedOptions.Default,
            NullLogger<CameraViewModel>.Instance,
            widgets: widgets ?? LayoutTestWidgets.Registry(),
            layoutWriter: withWriter ? _writer : null);
        _created.Add(viewModel);
        viewModel.OnClientSizeChanged(960, 540);
        return viewModel;
    }

    private void SaveSettings(CameraOverlayLayout layout)
    {
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(layout)] });
        _dispatcher.RunAll();
    }

    private static string[] Screen(CameraLayoutModeViewModel mode, OverlayAnchor anchor) => [.. mode.Zone(anchor).Cards.Select(static card => card.WidgetId)];

    private static string[] Edge(CameraOverlayLayout layout, OverlayAnchor anchor) => [.. layout.InAnchor(anchor).Select(static p => p.WidgetId)];

    private static CameraLayoutCardViewModel Card(CameraLayoutModeViewModel mode, string id) =>
        mode.Card(id) ?? throw new InvalidOperationException($"No card {id}.");

    [Fact]
    public void StartUsesTheSavedLayoutAndHidesTheOverlayCards()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.True(viewModel.EditLayoutCommand.CanExecute(null));
        viewModel.EditLayoutCommand.Execute(null);

        Assert.True(mode.IsActive);
        Assert.True(viewModel.IsLayoutModeActive);
        Assert.False(viewModel.CanEditLayout);
        Assert.Contains(nameof(CameraViewModel.IsLayoutModeActive), changed);
        Assert.Same(CameraOverlayLayout.Default, mode.Draft);
        Assert.False(viewModel.Overlay.AreCardsVisible);
        Assert.True(viewModel.Overlay.IsPreviewing);
        // The cards as seen on the screen: bottom anchors stack upward from the window edge.
        Assert.Equal([Status], Screen(mode, OverlayAnchor.TopLeft));
        Assert.Equal([PrintName], Screen(mode, OverlayAnchor.TopCenter));
        Assert.Equal([OverlayWidgetIds.Cooldown, Times], Screen(mode, OverlayAnchor.BottomLeft));
        Assert.Equal([Temperatures], Screen(mode, OverlayAnchor.BottomRight));
        Assert.True(mode.Zone(OverlayAnchor.TopRight).IsEmpty);
        // Reading order: TL, TC, TR, BL, BC, BR, each top to bottom.
        Assert.Equal([Status, PrintName, OverlayWidgetIds.Cooldown, Times, Temperatures], mode.Cards.Select(static card => card.WidgetId));
        Assert.Equal("Status: Top left, position 1 of 1", Card(mode, Status).AccessibleName);
        Assert.Equal("Times: Bottom left, position 2 of 2", Card(mode, Times).AccessibleName);
        Assert.Equal("Layout mode. Space picks a widget up, arrow keys move it, Enter opens its options, Esc discards the changes.", mode.Announcement);
        Assert.Equal(["CameraLayoutAnchorTopLeft", "CameraLayoutAnchorTopCenter", "CameraLayoutAnchorTopRight", "CameraLayoutAnchorBottomLeft", "CameraLayoutAnchorBottomCenter", "CameraLayoutAnchorBottomRight"], mode.Zones.Select(static zone => zone.AutomationId));
        Assert.Equal("CameraLayoutWidget-print-name", Card(mode, PrintName).AutomationId);
        Assert.Equal(["Top left", "Top center", "Top right", "Bottom left", "Bottom center", "Bottom right"], mode.Zones.Select(static zone => zone.Name));
    }

    [Fact]
    public void HiddenAndUnregisteredWidgetsHaveNoCard()
    {
        CameraOverlayLayout layout = CameraOverlayLayout.Default.WithVisible(PrintName, false);
        CameraViewModel viewModel = Create(layout, LayoutTestWidgets.Registry(null, Status, Times, PrintName, Temperatures));

        viewModel.LayoutMode.Start();

        Assert.Equal([Status, Times, Temperatures], viewModel.LayoutMode.Cards.Select(static card => card.WidgetId));
        Assert.Null(viewModel.LayoutMode.Card(OverlayWidgetIds.Cooldown));
        Assert.Equal("Times: Bottom left, position 1 of 1", Card(viewModel.LayoutMode, Times).AccessibleName);
    }

    [Theory]
    [MemberData(nameof(EveryAnchorAndPosition))]
    public void DragMovesACardIntoEveryAnchorAndPosition(OverlayAnchor anchor, int position)
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();
        CameraLayoutCardViewModel status = Card(mode, Status);
        List<string> expected = [.. Screen(mode, anchor).Where(static id => id != Status)];
        expected.Insert(position, Status);

        Assert.True(mode.BeginDrag(status));
        Assert.True(status.IsDragged);
        mode.DragOver(anchor, position);
        Assert.True(mode.Zone(anchor).IsTarget);
        Assert.Equal(position, mode.Zone(anchor).InsertIndex);
        Assert.Single(mode.Zones, static zone => zone.IsTarget);
        Assert.Contains(CameraLayoutInsertMarker.Instance, mode.Zone(anchor).Items);
        mode.Drop();

        Assert.False(status.IsDragged);
        Assert.Null(mode.Dragged);
        Assert.DoesNotContain(mode.Zones, static zone => zone.IsTarget || zone.InsertIndex >= 0);
        Assert.Equal(expected, Screen(mode, anchor));
        string[] edge = Edge(mode.Draft!, anchor);
        Assert.Equal(OverlayAnchors.IsTop(anchor) ? expected : [.. Enumerable.Reverse(expected)], edge);
        Assert.Equal(anchor, status.Anchor);
        Assert.Equal(position, status.ScreenIndex);
        // The overlay widgets follow the draft (in place, not drawn as overlay cards meanwhile).
        Assert.Same(mode.Draft, viewModel.Overlay.Layout);
        Assert.Same(CameraOverlayLayout.Default, viewModel.Overlay.SavedLayout);
        Assert.Empty(_writer.Saves);
    }

    [Fact]
    public void DragReordersWithinAnAnchor()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();

        mode.BeginDrag(Card(mode, Times));
        mode.DragOver(OverlayAnchor.BottomLeft, 0);
        mode.Drop();

        Assert.Equal([Times, OverlayWidgetIds.Cooldown], Screen(mode, OverlayAnchor.BottomLeft));
        Assert.Equal([OverlayWidgetIds.Cooldown, Times], Edge(mode.Draft!, OverlayAnchor.BottomLeft));
        Assert.Equal("Times moved: Bottom left, position 1", mode.Announcement);
    }

    [Fact]
    public void DropOverNoZoneOrCancelledDragLeavesTheDraft()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();

        mode.BeginDrag(Card(mode, Status));
        mode.DragOver(OverlayAnchor.BottomRight, 0);
        mode.DragOver(null, 0);
        Assert.False(mode.Drop());

        mode.BeginDrag(Card(mode, Status));
        mode.DragOver(OverlayAnchor.BottomRight, 0);
        mode.CancelDrag();

        Assert.Same(CameraOverlayLayout.Default, mode.Draft);
        Assert.Null(mode.Dragged);
        Assert.True(mode.IsActive);
    }

    [Fact]
    public void HiddenWidgetsKeepTheirPlaceNextToTheCards()
    {
        // Cool-down is not registered: it has no card and stays in the model; a card dropped above Times goes right after it.
        CameraViewModel viewModel = Create(widgets: LayoutTestWidgets.Registry(null, Status, Times, PrintName, Temperatures));
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();

        mode.BeginDrag(Card(mode, Status));
        mode.DragOver(OverlayAnchor.BottomLeft, 0);
        mode.Drop();
        Assert.Equal([Times, Status, OverlayWidgetIds.Cooldown], Edge(mode.Draft!, OverlayAnchor.BottomLeft));
        Assert.Equal([Status, Times], Screen(mode, OverlayAnchor.BottomLeft));

        mode.BeginDrag(Card(mode, Status));
        mode.DragOver(OverlayAnchor.BottomLeft, 1);
        mode.Drop();
        Assert.Equal([Status, Times, OverlayWidgetIds.Cooldown], Edge(mode.Draft!, OverlayAnchor.BottomLeft));
        Assert.Equal([Times, Status], Screen(mode, OverlayAnchor.BottomLeft));
    }

    [Fact]
    public void KeyboardMovesAPickedUpCardThroughEveryAnchor()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();
        CameraLayoutCardViewModel status = Card(mode, Status);

        Assert.True(mode.Lift(status));
        Assert.True(status.IsLifted && status.IsRaised && status.IsSelected);
        Assert.True(mode.Zone(OverlayAnchor.TopLeft).IsTarget);
        Assert.Equal("Status picked up. Use the arrow keys to choose a place, Space to drop, Esc to cancel.", mode.Announcement);

        Step(CameraLayoutDirection.Right, OverlayAnchor.TopCenter, [Status, PrintName], "Status: Top center, position 1 of 2");
        Step(CameraLayoutDirection.Right, OverlayAnchor.TopRight, [Status], "Status: Top right, position 1 of 1");
        // Right of the top right anchor there is nothing: the card stays.
        Assert.False(mode.MoveLifted(CameraLayoutDirection.Right));
        Assert.False(mode.MoveLifted(CameraLayoutDirection.Up));
        // Past the end of a top anchor Down continues at the top of the bottom anchor of the column.
        Step(CameraLayoutDirection.Down, OverlayAnchor.BottomRight, [Status, Temperatures], "Status: Bottom right, position 1 of 2");
        Step(CameraLayoutDirection.Down, OverlayAnchor.BottomRight, [Temperatures, Status], "Status: Bottom right, position 2 of 2");
        Assert.False(mode.MoveLifted(CameraLayoutDirection.Down));
        // Same position from the window edge in the next zone of the row (or its end).
        Step(CameraLayoutDirection.Left, OverlayAnchor.BottomCenter, [Status], "Status: Bottom center, position 1 of 1");
        Step(CameraLayoutDirection.Left, OverlayAnchor.BottomLeft, [OverlayWidgetIds.Cooldown, Times, Status], "Status: Bottom left, position 3 of 3");
        Assert.False(mode.MoveLifted(CameraLayoutDirection.Left));
        Step(CameraLayoutDirection.Up, OverlayAnchor.BottomLeft, [OverlayWidgetIds.Cooldown, Status, Times], "Status: Bottom left, position 2 of 3");
        Step(CameraLayoutDirection.Up, OverlayAnchor.BottomLeft, [Status, OverlayWidgetIds.Cooldown, Times], "Status: Bottom left, position 1 of 3");
        // Past the top of a bottom anchor Up continues at the bottom of the top anchor of the column.
        Step(CameraLayoutDirection.Up, OverlayAnchor.TopLeft, [Status], "Status: Top left, position 1 of 1");

        Assert.True(mode.DropLifted());
        Assert.False(status.IsLifted);
        Assert.Null(mode.Lifted);
        Assert.DoesNotContain(mode.Zones, static zone => zone.IsTarget);
        Assert.Equal("Status moved: Top left, position 1", mode.Announcement);
        Assert.Equal(CameraOverlayLayout.Default, mode.Draft);

        void Step(CameraLayoutDirection direction, OverlayAnchor anchor, string[] screen, string announcement)
        {
            Assert.True(mode.MoveLifted(direction), $"{direction} to {anchor}");
            Assert.Equal(screen, Screen(mode, anchor));
            Assert.Equal(anchor, status.Anchor);
            Assert.True(mode.Zone(anchor).IsTarget);
            Assert.Equal(announcement, mode.Announcement.TrimEnd());
            Assert.Same(mode.Draft, viewModel.Overlay.Layout);
        }
    }

    [Fact]
    public void HomeEndAndEscOfAPickedUpCard()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();
        mode.BeginDrag(Card(mode, Status));
        mode.DragOver(OverlayAnchor.BottomLeft, 1);
        mode.Drop();
        CameraOverlayLayout before = mode.Draft!;
        Assert.Equal([OverlayWidgetIds.Cooldown, Status, Times], Screen(mode, OverlayAnchor.BottomLeft));

        mode.Lift(Card(mode, Status));
        Assert.True(mode.MoveLiftedToEnd(top: true));
        Assert.Equal([Status, OverlayWidgetIds.Cooldown, Times], Screen(mode, OverlayAnchor.BottomLeft));
        Assert.True(mode.MoveLiftedToEnd(top: false));
        Assert.Equal([OverlayWidgetIds.Cooldown, Times, Status], Screen(mode, OverlayAnchor.BottomLeft));
        Assert.True(mode.MoveLifted(CameraLayoutDirection.Right));
        Assert.True(mode.CancelLift());

        Assert.Equal(before, mode.Draft);
        Assert.Equal([OverlayWidgetIds.Cooldown, Status, Times], Screen(mode, OverlayAnchor.BottomLeft));
        Assert.Null(mode.Lifted);
        Assert.Equal("Move cancelled", mode.Announcement);
        Assert.True(mode.IsActive);
    }

    [Fact]
    public void PopoverChangesSizeAndEmphasisInTheDraft()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();
        CameraLayoutCardViewModel times = Card(mode, Times);

        Assert.True(mode.OpenPopover(times));
        Assert.True(times.IsSelected && times.IsPopoverOpen && mode.IsPopoverOpen);
        Assert.Equal("Times", mode.PopoverTitle);
        Assert.True(mode.IsPopoverMedium);
        Assert.False(mode.PopoverEmphasized);
        Assert.True(mode.CanPopoverSmall && mode.CanPopoverMedium && mode.CanPopoverLarge);

        mode.IsPopoverLarge = true;
        mode.PopoverEmphasized = true;

        Assert.Equal(OverlayWidgetSize.Large, mode.Draft!.Find(Times)!.Size);
        Assert.True(mode.Draft.Find(Times)!.Emphasized);
        Assert.True(mode.IsPopoverLarge && !mode.IsPopoverMedium);
        Assert.Equal(OverlayWidgetSize.Large, times.Size);
        Assert.True(times.IsEmphasized);
        // The widget of the overlay draws the draft right away.
        OverlayWidgetViewModel widget = viewModel.Overlay.FindWidget(Times)!;
        Assert.Equal(OverlayWidgetSize.Large, widget.Size);
        Assert.Equal(OverlayWidgetSize.Large, widget.RenderedSize);
        Assert.True(widget.IsEmphasized);

        mode.ClosePopover();
        Assert.False(mode.IsPopoverOpen);
        Assert.False(times.IsPopoverOpen);
        Assert.True(times.IsSelected);
        Assert.Same(times, mode.Selected);
    }

    [Fact]
    public void OnlySupportedSizesCanBeChosen()
    {
        Dictionary<string, OverlayWidgetSize[]> sizes = new() { [Status] = [OverlayWidgetSize.Medium, OverlayWidgetSize.Large] };
        CameraViewModel viewModel = Create(widgets: LayoutTestWidgets.Registry(sizes));
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();
        CameraLayoutCardViewModel status = Card(mode, Status);
        mode.OpenPopover(status);

        Assert.False(mode.CanPopoverSmall);
        Assert.True(mode.CanPopoverMedium && mode.CanPopoverLarge);
        mode.IsPopoverSmall = true;
        mode.SetSize(status, OverlayWidgetSize.Small);

        Assert.Same(CameraOverlayLayout.Default, mode.Draft);
        Assert.True(mode.IsPopoverMedium);
    }

    [Fact]
    public void ResetRestoresTheDefaultLayoutInTheDraft()
    {
        CameraOverlayLayout custom = CameraOverlayLayout.Default.MoveTo(Status, OverlayAnchor.BottomCenter, 0).WithSize(Times, OverlayWidgetSize.Small).WithEmphasized(Status, false);
        CameraViewModel viewModel = Create(custom);
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();

        mode.ResetCommand.Execute(null);

        Assert.Equal(CameraOverlayLayout.Default, mode.Draft);
        Assert.Equal([Status], Screen(mode, OverlayAnchor.TopLeft));
        Assert.Equal("Default layout restored", mode.Announcement);
        Assert.Empty(_writer.Saves);
        Assert.Same(custom, viewModel.Overlay.SavedLayout);
    }

    [Fact]
    public void CancelDiscardsTheDraftAndTheOverlayShowsTheSavedLayout()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        List<CameraLayoutEndReason> ended = [];
        mode.Ended += (_, e) => ended.Add(e.Reason);
        mode.Start();
        mode.BeginDrag(Card(mode, Status));
        mode.DragOver(OverlayAnchor.BottomRight, 0);
        mode.Drop();
        mode.OpenPopover(Card(mode, Times));
        mode.IsPopoverSmall = true;

        mode.CancelCommand.Execute(null);

        Assert.False(mode.IsActive);
        Assert.False(viewModel.IsLayoutModeActive);
        Assert.True(viewModel.CanEditLayout);
        Assert.Null(mode.Draft);
        Assert.Empty(mode.Cards);
        Assert.False(mode.IsPopoverOpen);
        Assert.Same(CameraOverlayLayout.Default, viewModel.Overlay.Layout);
        Assert.True(viewModel.Overlay.AreCardsVisible);
        Assert.False(viewModel.Overlay.IsPreviewing);
        Assert.Equal([OverlayWidgetSize.Medium], viewModel.Overlay.Widgets.Where(static w => w.Id == Times).Select(static w => w.Size));
        Assert.Empty(_writer.Saves);
        Assert.Equal([CameraLayoutEndReason.Discarded], ended);
        Assert.Equal("Layout changes discarded", mode.Announcement);
    }

    [Fact]
    public async Task DoneSavesTheDraftOnceAndAppliesItRightAway()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        List<CameraLayoutEndReason> ended = [];
        mode.Ended += (_, e) => ended.Add(e.Reason);
        List<CameraOverlayLayoutSavedEventArgs> announced = [];
        _writer.LayoutSaved += (_, e) => announced.Add(e);
        mode.Start();
        mode.BeginDrag(Card(mode, Temperatures));
        mode.DragOver(OverlayAnchor.TopRight, 0);
        mode.Drop();
        CameraOverlayLayout draft = mode.Draft!;
        _writer.Hold();

        Task<bool> first = mode.SaveAsync();
        Assert.True(mode.IsSaving);
        Assert.False(mode.DoneCommand.CanExecute(null));
        Assert.False(mode.CancelCommand.CanExecute(null));
        // Pressed again and Esc while saving: ignored.
        Assert.False(await mode.SaveAsync());
        mode.Cancel();
        Assert.True(mode.IsActive);
        _writer.Release(result: true);

        Assert.True(await first.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
        Assert.Equal([("mk4", draft)], _writer.Saves);
        Assert.Single(announced);
        Assert.Same(draft, announced[0].Layout);
        Assert.False(mode.IsActive);
        Assert.False(mode.IsSaving);
        Assert.Equal([CameraLayoutEndReason.Saved], ended);
        Assert.Equal("Layout saved", mode.Announcement);
        // The open window uses it at once (also as its saved layout); the monitored settings deliver the same layout later.
        Assert.Same(draft, viewModel.Overlay.Layout);
        Assert.Same(draft, viewModel.Overlay.SavedLayout);
        Assert.Equal(["temperatures"], viewModel.Overlay.TopRight.Select(static w => w.Id));
        SaveSettings(draft);
        Assert.Equal(["temperatures"], viewModel.Overlay.TopRight.Select(static w => w.Id));
    }

    [Fact]
    public async Task SaveFailureIsShownAndNothingIsLost()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();
        mode.BeginDrag(Card(mode, Status));
        mode.DragOver(OverlayAnchor.BottomCenter, 0);
        mode.Drop();
        CameraOverlayLayout draft = mode.Draft!;

        _writer.Failure = new IOException("The settings file is locked.");
        Assert.False(await mode.SaveAsync());
        Assert.True(mode.IsActive);
        Assert.True(mode.HasSaveError);
        Assert.Same(draft, mode.Draft);
        Assert.Equal("The layout could not be saved. Try again.", mode.Announcement);

        _writer.Failure = null;
        _writer.Result = false;
        Assert.False(await mode.SaveAsync());
        Assert.True(mode.HasSaveError);
        Assert.Same(draft, mode.Draft);

        _writer.Result = true;
        Assert.True(await mode.SaveAsync());
        Assert.False(mode.HasSaveError);
        Assert.Equal(3, _writer.Saves.Count);
        Assert.All(_writer.Saves, save => Assert.Same(draft, save.Layout));
        Assert.Same(draft, viewModel.Overlay.SavedLayout);
    }

    /// <summary>
    /// T-093 (found by the E2E test on the real app): "Done" with the saved layout as the draft (nothing changed, or moved and
    /// restored to the saved defaults) has nothing to write; the settings store reports such a write as "not written", which
    /// must not show the save error and keep the mode.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DoneWithTheSavedLayoutEndsTheModeWithoutWriting(bool movedAndRestored)
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        List<CameraLayoutEndReason> ended = [];
        mode.Ended += (_, e) => ended.Add(e.Reason);
        mode.Start();
        if (movedAndRestored)
        {
            mode.BeginDrag(Card(mode, Status));
            mode.DragOver(OverlayAnchor.BottomCenter, 0);
            mode.Drop();
            mode.Reset();
        }

        // What the settings store answers for a layout equal to the stored one.
        _writer.Result = false;

        Assert.True(await mode.SaveAsync());

        Assert.Empty(_writer.Saves);
        // T-092 follow-up: the layout is confirmed instead (an open settings window drops its unsaved layout of the printer).
        Assert.Equal([("mk4", CameraOverlayLayout.Default)], _writer.Confirms);
        Assert.Equal(1, _writer.Confirmed);
        Assert.False(mode.IsActive);
        Assert.False(mode.HasSaveError);
        Assert.Equal([CameraLayoutEndReason.Saved], ended);
        Assert.Equal(CameraOverlayLayout.Default, viewModel.Overlay.SavedLayout);
        Assert.Equal(["status"], viewModel.Overlay.TopLeft.Select(static w => w.Id));
    }

    /// <summary>
    /// T-092 follow-up: started from the settings window with its unsaved layout, the saved layout restored and "Done"
    /// confirms the saved layout; a changed draft is saved and never confirmed.
    /// </summary>
    [Fact]
    public async Task DoneWithTheSavedLayoutRestoredFromAStartingLayoutConfirmsIt()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        CameraOverlayLayout fromSettings = CameraOverlayLayout.Default.WithVisible(Times, visible: false);
        mode.Start(fromSettings);
        mode.Reset();

        Assert.True(await mode.SaveAsync());

        Assert.Empty(_writer.Saves);
        Assert.Equal([("mk4", CameraOverlayLayout.Default)], _writer.Confirms);
        Assert.False(mode.IsActive);

        mode.Start(fromSettings);
        Assert.True(await mode.SaveAsync());
        Assert.Equal([("mk4", fromSettings)], _writer.Saves);
        Assert.Single(_writer.Confirms);
    }

    [Fact]
    public async Task ConfirmationThatWritesEndsTheModeWithTheLayout()
    {
        // A settings save changed the file meanwhile: the writer writes the confirmed layout (announced as a save).
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        _writer.ConfirmWrites = true;
        mode.Start();

        Assert.True(await mode.SaveAsync());

        Assert.False(mode.IsActive);
        Assert.False(mode.HasSaveError);
        Assert.Equal(1, _writer.Announced);
        Assert.Equal(0, _writer.Confirmed);
    }

    [Fact]
    public async Task FailedConfirmationKeepsTheModeAndShowsTheError()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        List<CameraLayoutEndReason> ended = [];
        mode.Ended += (_, e) => ended.Add(e.Reason);
        mode.Start();
        _writer.Failure = new IOException("The settings file is locked.");

        Assert.False(await mode.SaveAsync());

        Assert.True(mode.IsActive);
        Assert.True(mode.HasSaveError);
        Assert.Empty(ended);
        Assert.Equal("The layout could not be saved. Try again.", mode.Announcement);

        _writer.Failure = null;
        Assert.True(await mode.SaveAsync());
        Assert.False(mode.IsActive);
        Assert.Equal(2, _writer.Confirms.Count);
    }

    [Fact]
    public async Task HeldConfirmationBlocksInputUntilItCompletes()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();
        _writer.Hold();

        Task<bool> saving = mode.SaveAsync();

        Assert.True(mode.IsSaving);
        Assert.False(mode.DoneCommand.CanExecute(null));
        Assert.False(await mode.SaveAsync());
        _writer.Release(result: false);
        Assert.True(await saving.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
        Assert.False(mode.IsSaving);
        Assert.False(mode.IsActive);
        Assert.Single(_writer.Confirms);
    }

    [Fact]
    public async Task WithoutAWriterDoneFailsVisibly()
    {
        CameraViewModel viewModel = Create(withWriter: false);
        viewModel.LayoutMode.Start();

        Assert.False(await viewModel.LayoutMode.SaveAsync());

        Assert.True(viewModel.LayoutMode.IsActive);
        Assert.True(viewModel.LayoutMode.HasSaveError);
    }

    [Fact]
    public async Task DonePutsAPickedUpCardWhereItIs()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();
        mode.Lift(Card(mode, Status));
        mode.MoveLifted(CameraLayoutDirection.Right);

        Assert.True(await mode.SaveAsync());

        Assert.Equal([Status, PrintName], Edge(_writer.Saves.Single().Layout, OverlayAnchor.TopCenter));
    }

    [Theory]
    [InlineData(CameraWindowMode.FullScreen)]
    [InlineData(CameraWindowMode.Mini)]
    public void SwitchingTheWindowModeDiscardsTheEdit(CameraWindowMode mode)
    {
        CameraViewModel viewModel = Create();
        viewModel.LayoutMode.Start();
        viewModel.LayoutMode.BeginDrag(Card(viewModel.LayoutMode, Status));
        viewModel.LayoutMode.DragOver(OverlayAnchor.BottomRight, 0);
        viewModel.LayoutMode.Drop();
        List<string?> order = [];
        viewModel.PropertyChanged += (_, e) => order.Add(e.PropertyName);

        (mode == CameraWindowMode.Mini ? viewModel.ShowMiniCommand : viewModel.ShowFullScreenCommand).Execute(null);

        Assert.Equal(mode, viewModel.Mode);
        Assert.False(viewModel.LayoutMode.IsActive);
        Assert.Empty(_writer.Saves);
        Assert.Same(CameraOverlayLayout.Default, viewModel.Overlay.Layout);
        // The edit ends before the window switches (the window gets its size back first).
        Assert.True(order.IndexOf(nameof(CameraViewModel.IsLayoutModeActive)) < order.IndexOf(nameof(CameraViewModel.Mode)));
        Assert.False(viewModel.CanEditLayout);
        Assert.False(viewModel.EditLayoutCommand.CanExecute(null));
    }

    [Fact]
    public async Task SwitchingTheWindowModeWhileSavingEndsTheModeAndTheWriteStillHappens()
    {
        CameraViewModel viewModel = Create();
        viewModel.LayoutMode.Start();
        viewModel.LayoutMode.BeginDrag(Card(viewModel.LayoutMode, Status));
        viewModel.LayoutMode.DragOver(OverlayAnchor.BottomRight, 0);
        viewModel.LayoutMode.Drop();
        _writer.Hold();
        Task<bool> saving = viewModel.LayoutMode.SaveAsync();

        viewModel.ShowFullScreenCommand.Execute(null);

        Assert.True(viewModel.IsFullScreen);
        Assert.False(viewModel.LayoutMode.IsActive);
        Assert.False(viewModel.Overlay.IsPreviewing);
        _writer.Release(result: true);
        Assert.True(await saving.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
        Assert.Single(_writer.Saves);
        Assert.False(viewModel.LayoutMode.IsActive);
        Assert.False(viewModel.LayoutMode.HasSaveError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASaveFinishingAfterTheModeWasLeftAndStartedAgainDoesNotEndTheNewEdit(bool written)
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        List<CameraLayoutEndReason> ended = [];
        mode.Ended += (_, e) => ended.Add(e.Reason);
        mode.Start();
        mode.BeginDrag(Card(mode, Status));
        mode.DragOver(OverlayAnchor.BottomRight, 0);
        mode.Drop();
        _writer.Hold();
        Task<bool> saving = mode.SaveAsync();

        // Full screen ends the edit while the write is on its way; back in the normal window the user edits again.
        viewModel.ShowFullScreenCommand.Execute(null);
        viewModel.ShowNormalCommand.Execute(null);
        Assert.True(mode.Start());
        CameraOverlayLayout newDraft = mode.Draft!;
        _writer.Release(written);

        Assert.Equal(written, await saving.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
        // The new edit goes on with its own draft and without the old save's result.
        Assert.True(mode.IsActive);
        Assert.False(mode.IsSaving);
        Assert.False(mode.HasSaveError);
        Assert.Same(newDraft, mode.Draft);
        Assert.True(viewModel.Overlay.IsPreviewing);
        Assert.Equal([CameraLayoutEndReason.Discarded], ended);
        Assert.True(mode.DoneCommand.CanExecute(null));
    }

    [Fact]
    public void LayoutModeIsOnlyForTheNormalWindowAndNeedsRoom()
    {
        CameraViewModel viewModel = Create();
        Assert.True(viewModel.HasLayoutRoom);
        Assert.Equal("Edit layout", viewModel.LayoutToolTip);

        viewModel.OnClientSizeChanged(479, 540);
        Assert.False(viewModel.CanEditLayout);
        Assert.False(viewModel.EditLayoutCommand.CanExecute(null));
        Assert.Equal("Enlarge the window to edit the layout.", viewModel.LayoutToolTip);
        viewModel.EditLayoutCommand.Execute(null);
        Assert.False(viewModel.LayoutMode.IsActive);

        viewModel.OnClientSizeChanged(480, 299);
        Assert.False(viewModel.CanEditLayout);
        viewModel.OnClientSizeChanged(480, 300);
        Assert.True(viewModel.CanEditLayout);
        Assert.True(viewModel.EditLayoutCommand.CanExecute(null));

        viewModel.ShowFullScreenCommand.Execute(null);
        Assert.False(viewModel.CanEditLayout);
        viewModel.ShowNormalCommand.Execute(null);
        Assert.True(viewModel.CanEditLayout);
    }

    [Fact]
    public void DoubleClickDoesNotSwitchModesInTheLayoutMode()
    {
        CameraViewModel viewModel = Create();
        viewModel.LayoutMode.Start();

        viewModel.OnImageDoubleClicked();

        Assert.True(viewModel.IsNormal);
        Assert.True(viewModel.LayoutMode.IsActive);
    }

    [Fact]
    public void SettingsSavedElsewhereKeepTheDraftAndTakeTheChangedVisibility()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();
        mode.BeginDrag(Card(mode, Status));
        mode.DragOver(OverlayAnchor.BottomRight, 0);
        mode.Drop();

        // The settings window hides Times and moves Temperatures; only the visibility comes into the draft.
        CameraOverlayLayout saved = CameraOverlayLayout.Default.WithVisible(Times, false).MoveTo(Temperatures, OverlayAnchor.TopRight, 0);
        SaveSettings(saved);

        Assert.True(mode.IsActive);
        Assert.Same(saved, viewModel.Overlay.SavedLayout);
        Assert.Same(mode.Draft, viewModel.Overlay.Layout);
        Assert.False(mode.Draft!.Find(Times)!.Visible);
        Assert.Null(mode.Card(Times));
        Assert.Equal(OverlayAnchor.BottomRight, mode.Draft.Find(Temperatures)!.Anchor);
        Assert.Equal([Status, Temperatures], Screen(mode, OverlayAnchor.BottomRight));
        Assert.False(viewModel.Overlay.AreCardsVisible);

        // Esc now returns to the newly saved layout.
        mode.Cancel();
        Assert.Same(saved, viewModel.Overlay.Layout);
    }

    [Fact]
    public void ASelectedOrPickedUpCardWhoseWidgetIsHiddenElsewhereIsDropped()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();
        mode.Lift(Card(mode, Times));
        mode.MoveLifted(CameraLayoutDirection.Right);

        SaveSettings(CameraOverlayLayout.Default.WithVisible(Times, false));

        Assert.Null(mode.Lifted);
        Assert.Null(mode.Selected);
        Assert.DoesNotContain(mode.Zones, static zone => zone.IsTarget);
        Assert.True(mode.IsActive);
    }

    [Fact]
    public void ClosingTheWindowDiscardsTheEdit()
    {
        CameraViewModel viewModel = Create();
        viewModel.LayoutMode.Start();
        viewModel.LayoutMode.BeginDrag(Card(viewModel.LayoutMode, Status));
        viewModel.LayoutMode.DragOver(OverlayAnchor.BottomRight, 0);
        viewModel.LayoutMode.Drop();

        viewModel.Dispose();

        Assert.False(viewModel.LayoutMode.IsActive);
        Assert.Empty(_writer.Saves);
        Assert.False(viewModel.LayoutMode.Start());
    }

    [Fact]
    public async Task ClosingTheWindowWhileSavingKeepsTheWrite()
    {
        CameraViewModel viewModel = Create();
        viewModel.LayoutMode.Start();
        // A change to write (the saved layout itself has nothing to write, T-093).
        viewModel.LayoutMode.BeginDrag(Card(viewModel.LayoutMode, Status));
        viewModel.LayoutMode.DragOver(OverlayAnchor.BottomCenter, 0);
        viewModel.LayoutMode.Drop();
        _writer.Hold();
        Task<bool> saving = viewModel.LayoutMode.SaveAsync();

        viewModel.Dispose();
        _writer.Release(result: true);

        Assert.True(await saving.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken));
        Assert.Single(_writer.Saves);
        Assert.False(viewModel.LayoutMode.IsActive);
    }

    [Fact]
    public void StartFromOutsideUsesTheStartingLayoutAndEscChangesNothing()
    {
        CameraViewModel viewModel = Create();
        CameraOverlayLayout starting = CameraOverlayLayout.Default.WithVisible(PrintName, false).MoveTo(Status, OverlayAnchor.TopRight, 0);

        Assert.True(viewModel.StartLayoutModeFromOutside(starting));

        Assert.Same(starting, viewModel.LayoutMode.Draft);
        Assert.Null(viewModel.LayoutMode.Card(PrintName));
        Assert.Equal([Status], Screen(viewModel.LayoutMode, OverlayAnchor.TopRight));
        Assert.True(viewModel.IsLayoutSpaceRequested);
        viewModel.OnLayoutSpaceProvided();
        Assert.False(viewModel.IsLayoutSpaceRequested);

        viewModel.LayoutMode.Cancel();

        Assert.Same(CameraOverlayLayout.Default, viewModel.Overlay.Layout);
        Assert.Empty(_writer.Saves);
    }

    [Fact]
    public async Task StartFromOutsideSavesTheStartingLayoutWithTheChanges()
    {
        CameraViewModel viewModel = Create();
        CameraOverlayLayout starting = CameraOverlayLayout.Default.WithVisible(PrintName, false);
        viewModel.StartLayoutModeFromOutside(starting);
        viewModel.LayoutMode.BeginDrag(Card(viewModel.LayoutMode, Temperatures));
        viewModel.LayoutMode.DragOver(OverlayAnchor.TopRight, 0);
        viewModel.LayoutMode.Drop();

        Assert.True(await viewModel.LayoutMode.SaveAsync());

        CameraOverlayLayout saved = _writer.Saves.Single().Layout;
        Assert.Equal(starting.MoveTo(Temperatures, OverlayAnchor.TopRight, 0), saved);
        Assert.False(saved.Find(PrintName)!.Visible);
        Assert.Equal(1, _writer.Announced);
        Assert.False(viewModel.IsLayoutSpaceRequested);
    }

    [Theory]
    [InlineData(CameraWindowStartMode.Mini)]
    [InlineData(CameraWindowStartMode.FullScreen)]
    public void StartFromOutsideSwitchesToTheNormalWindowAndStaysThere(CameraWindowStartMode start)
    {
        CameraViewModel viewModel = Create(window: CameraWindowDefaults.Default with { StartMode = start });
        Assert.False(viewModel.IsNormal);

        Assert.True(viewModel.StartLayoutModeFromOutside(null));
        Assert.True(viewModel.IsNormal);
        Assert.Same(CameraOverlayLayout.Default, viewModel.LayoutMode.Draft);

        viewModel.LayoutMode.Cancel();
        Assert.True(viewModel.IsNormal);
    }

    [Fact]
    public void StartFromOutsideWhileEditingKeepsTheEdit()
    {
        CameraViewModel viewModel = Create();
        viewModel.LayoutMode.Start();
        viewModel.LayoutMode.BeginDrag(Card(viewModel.LayoutMode, Status));
        viewModel.LayoutMode.DragOver(OverlayAnchor.BottomRight, 0);
        viewModel.LayoutMode.Drop();
        CameraOverlayLayout draft = viewModel.LayoutMode.Draft!;

        Assert.True(viewModel.StartLayoutModeFromOutside(CameraOverlayLayout.Default.WithVisible(Status, false)));

        Assert.Same(draft, viewModel.LayoutMode.Draft);
        Assert.False(viewModel.IsLayoutSpaceRequested);
    }

    [Fact]
    public void StartingAgainKeepsTheEditAndTextsFollowTheLanguage()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();
        mode.OpenPopover(Card(mode, Status));

        Assert.True(mode.Start(CameraOverlayLayout.Default.WithVisible(Status, false)));
        Assert.Same(CameraOverlayLayout.Default, mode.Draft);

        using UiCultureScope czech = new("cs");
        viewModel.RefreshTexts();
        Assert.Equal("Stav: Vlevo nahoře, pozice 1 z 1", Card(mode, Status).AccessibleName);
        Assert.Equal("Dole uprostřed", mode.Zone(OverlayAnchor.BottomCenter).Name);
        Assert.Equal("Stav", mode.PopoverTitle);
        Assert.Equal("Upravit rozložení", viewModel.LayoutToolTip);
    }

    [Fact]
    public void RepeatedAnnouncementStillChangesTheLiveRegion()
    {
        CameraViewModel viewModel = Create();
        CameraLayoutModeViewModel mode = viewModel.LayoutMode;
        mode.Start();
        List<string> announced = [];
        mode.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CameraLayoutModeViewModel.Announcement))
            {
                announced.Add(mode.Announcement);
            }
        };

        mode.Reset();
        mode.Reset();
        mode.Reset();

        Assert.Equal(3, announced.Count);
        Assert.All(announced, text => Assert.Equal("Default layout restored", text.TrimEnd()));
        Assert.NotEqual(announced[0], announced[1]);
    }

    [Fact]
    public void WithSavedVisibilityTakesOnlyChangedVisibility()
    {
        CameraOverlayLayout draft = CameraOverlayLayout.Default.MoveTo(Status, OverlayAnchor.BottomCenter, 0).WithVisible(OverlayWidgetIds.Cooldown, false);
        CameraOverlayLayout previous = CameraOverlayLayout.Default;
        CameraOverlayLayout current = CameraOverlayLayout.Default.WithVisible(Times, false).WithSize(Status, OverlayWidgetSize.Large);

        CameraOverlayLayout merged = CameraLayoutModeViewModel.WithSavedVisibility(draft, previous, current);

        Assert.Equal(draft.WithVisible(Times, false), merged);
        Assert.Same(draft, CameraLayoutModeViewModel.WithSavedVisibility(draft, previous, previous));
    }
}
