using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Layout;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Composition;
using LayerPeek.App.Localization;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Cameras.Layout;
using LayerPeek.App.Tests.Cameras.Overlay;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;
using static LayerPeek.Core.Cameras.Overlay.OverlayWidgetIds;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The layout mode in the real camera window (T-091, design T-076 5): entry by the window button and the context menu (only in
/// the normal window, disabled when it is too small), zones, mouse drags into every anchor and within one with the target
/// highlighted, the popover, full keyboard control, automation ids, the live region, the longest languages, the snapshots,
/// and the entry from outside (the settings window, T-092) with the temporary enlargement of a small window.
/// </summary>
public sealed class CameraWindowLayoutModeUiTests(HeadlessSession session)
{
    private static readonly string[] SizeIds = ["CameraLayoutSizeS", "CameraLayoutSizeM", "CameraLayoutSizeL"];

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static CameraLayoutModeViewModel Mode(CameraWindow window) => ViewModel(window).LayoutMode;

    private static Panel Zone(CameraWindow window, OverlayAnchor anchor) => window.Find<Panel>(CameraLayoutZoneViewModel.AutomationIdPrefix + anchor);

    private static CameraLayoutCard Card(CameraWindow window, string id) => window.Find<CameraLayoutCard>(CameraLayoutCardViewModel.AutomationIdPrefix + id);

    private static string[] ZoneCards(CameraWindow window, OverlayAnchor anchor) =>
        [.. Zone(window, anchor).GetVisualDescendants().OfType<CameraLayoutCard>()
            .OrderBy(card => card.TranslatePoint(default, window)!.Value.Y)
            .Select(static card => card.Card!.WidgetId)];

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root) ?? throw new InvalidOperationException("Not in the window."), control.Bounds.Size);

    /// <summary>The five widgets of v1 drawn by the test widget; Cool-down has no data (a placeholder card in the mode).</summary>
    private static OverlayWidgetRegistry Widgets() => new(
    [
        LayoutTestWidgets.Definition(Status),
        LayoutTestWidgets.Definition(Times),
        LayoutTestWidgets.Definition(Temperatures),
        LayoutTestWidgets.Definition(PrintName),
        new OverlayWidgetDefinition(OverlayWidgetIds.Cooldown, "CameraOverlayWidgetCooldown", 0, TestOverlayWidgets.Template, static _ => new TestOverlayWidget(static _ => default), new OverlayWidgetMinWidths(120, 180, 240)),
    ]);

    private static CameraHarness Harness(CameraWindowDefaults? window = null, IScreenCatalog? screens = null, ICameraWindowStateStore? states = null) =>
        new(states: states, screens: screens ?? FakeScreenCatalog.ThreeMonitors(), window: window, widgets: Widgets());

    /// <summary>Opens the normal window of printer "a" in <paramref name="width"/> × <paramref name="height"/>.</summary>
    private static CameraWindow OpenNormal(CameraHarness harness, double width = 960, double height = 540)
    {
        CameraWindow window = harness.Open();
        window.Width = width;
        window.Height = height;
        window.Settle();
        Assert.True(ViewModel(window).IsNormal);
        return window;
    }

    /// <summary>Enters the layout mode with the window button (mouse).</summary>
    private static void EnterByButton(CameraWindow window)
    {
        window.Click(window.Find<Button>("CameraLayoutButton"));
        Dispatcher.UIThread.RunJobs();
        window.Settle();
        Assert.True(Mode(window).IsActive);
    }

    private static void Press(CameraWindow window, Key key, PhysicalKey physical, string? symbol = null, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, symbol);
        window.KeyRelease(key, modifiers, physical, symbol);
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static object? Focused(CameraWindow window) => window.FocusManager?.GetFocusedElement();

    private static string? FocusedId(CameraWindow window) =>
        Focused(window) is Control control ? AutomationProperties.GetAutomationId(control) ?? control.GetType().Name : null;

    private static object? FocusedAnywhere(Control control) => TopLevel.GetTopLevel(control)?.FocusManager?.GetFocusedElement();

    /// <summary>
    /// Drags <paramref name="card"/> with the left button to <paramref name="target"/> in steps (as a user would), checks the
    /// state just before the release with <paramref name="beforeRelease"/>, then releases.
    /// </summary>
    private static void Drag(CameraWindow window, Control card, Point target, Action? beforeRelease = null)
    {
        Point start = Ui.CentreOf(card, window);
        window.MouseMove(start);
        window.MouseDown(start, MouseButton.Left);
        window.Settle();
        for (int step = 1; step <= 6; step++)
        {
            window.MouseMove(start + ((target - start) * (step / 6.0)), RawInputModifiers.LeftMouseButton);
            window.Settle();
        }

        beforeRelease?.Invoke();
        window.MouseUp(target, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    [Fact]
    public Task ButtonOnlyInTheNormalWindowAndDisabledWhenTooSmall() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            Button button = window.Find<Button>("CameraLayoutButton");

            Assert.True(button.IsVisible);
            Assert.True(button.IsEffectivelyEnabled);
            Assert.Equal("Edit layout", ToolTip.GetTip(button));
            Assert.Equal("Edit layout", AutomationProperties.GetName(button));
            // T-076 3.2: [layout][full screen][mini preview], 3 × 30 + 2 × 4 = 98 wide.
            Assert.Equal(98, window.Find<StackPanel>("CameraWindowButtons").Bounds.Width, 0.5);

            // Below 480 × 300: disabled with the tip, still shown.
            window.Width = 479;
            window.Settle();
            Assert.False(button.IsEffectivelyEnabled);
            Assert.True(button.IsVisible);
            Assert.True(ToolTip.GetShowOnDisabled(button));
            Assert.Equal("Enlarge the window to edit the layout.", ToolTip.GetTip(button));
            window.Width = 480;
            window.Height = 299;
            window.Settle();
            Assert.False(button.IsEffectivelyEnabled);
            window.Height = 300;
            window.Settle();
            Assert.True(button.IsEffectivelyEnabled);

            ViewModel(window).ShowFullScreenCommand.Execute(null);
            window.Settle();
            Assert.False(button.IsVisible);
            ViewModel(window).ShowMiniCommand.Execute(null);
            window.Settle();
            Assert.False(button.IsVisible);
        });

    [Fact]
    public Task ContextMenuOpensOnlyInTheNormalWindowAndEntersTheMode() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            ContextMenu menu = window.ContextMenu!;
            Assert.Equal("CameraContextMenu", AutomationProperties.GetAutomationId(menu));
            MenuItem item = menu.Items.OfType<MenuItem>().Single();
            Assert.Equal("CameraLayoutMenuItem", AutomationProperties.GetAutomationId(item));
            Assert.Equal("Edit layout", item.Header);

            foreach (Action switchMode in new Action[] { () => ViewModel(window).ShowFullScreenCommand.Execute(null), () => ViewModel(window).ShowMiniCommand.Execute(null) })
            {
                switchMode();
                window.Settle();
                RightClick(window, new Point(100, 100));
                Assert.False(menu.IsOpen);
            }

            ViewModel(window).ShowNormalCommand.Execute(null);
            window.Width = 400;
            window.Height = 250;
            window.Settle();
            RightClick(window, new Point(100, 100));
            Assert.True(menu.IsOpen);
            Assert.False(item.IsEffectivelyEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(item));
            Assert.Equal("Enlarge the window to edit the layout.", ToolTip.GetTip(item));
            menu.Close();

            window.Width = 960;
            window.Height = 540;
            window.Settle();
            RightClick(window, new Point(100, 100));
            Assert.True(menu.IsOpen);
            Assert.True(item.IsEffectivelyEnabled);
            item.Command!.Execute(null);
            menu.Close();
            window.Settle();
            Assert.True(Mode(window).IsActive);

            // No menu in the layout mode.
            RightClick(window, new Point(480, 270));
            Assert.False(menu.IsOpen);
        });

    private static void RightClick(CameraWindow window, Point point)
    {
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    [Fact]
    public Task ModeShowsDimZonesCardsAndToolbarWithAllAutomationIds() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            Assert.True(window.IsShown<Panel>("CameraOverlay"));

            EnterByButton(window);

            Assert.True(window.IsShown<Panel>("CameraLayoutOverlay"));
            Assert.True(window.IsShown<Border>("CameraLayoutDim"));
            Assert.False(window.Find<StackPanel>("CameraWindowButtons").IsVisible);
            // The overlay cards are hidden; the mode draws the same widgets in its zones.
            Assert.False(window.IsShown<Panel>("CameraOverlay"));
            foreach (OverlayAnchor anchor in OverlayAnchors.All)
            {
                Assert.True(Zone(window, anchor).IsEffectivelyVisible, anchor.ToString());
            }

            foreach (string id in Known)
            {
                Assert.True(Card(window, id).IsEffectivelyVisible, id);
            }

            Assert.True(window.IsShown<Border>("CameraLayoutToolbar"));
            Assert.True(window.IsShown<TextBlock>("CameraLayoutHint"));
            Assert.Equal("drag a widget to an anchor", window.Find<TextBlock>("CameraLayoutHint").Text);
            Assert.False(window.IsShown<TextBlock>("CameraLayoutSaveError"));
            string[] buttons = [.. window.LayoutModeView.Root.GetVisualDescendants().OfType<Button>().Select(static button => AutomationProperties.GetAutomationId(button) ?? "")];
            // Windows order (T-076 5.2): [Restore defaults] … [Done] [Cancel].
            Assert.Equal(["CameraLayoutReset", "CameraLayoutDone", "CameraLayoutCancel"], buttons);
            Assert.Equal(["Restore defaults", "Done", "Cancel"], window.LayoutModeView.Root.GetVisualDescendants().OfType<Button>().Select(static b => b.Content));
            Assert.Contains("accent", window.Find<Button>("CameraLayoutDone").Classes);
            Assert.False(window.Find<Button>("CameraLayoutDone").IsDefault);

            // Empty zones show their name; the cards their name, zone and position for screen readers.
            Assert.True(Ui.ShowsText(Zone(window, OverlayAnchor.TopRight), "Top right"));
            Assert.True(Ui.ShowsText(Zone(window, OverlayAnchor.BottomCenter), "Bottom center"));
            Assert.Equal("Status: Top left, position 1 of 1", AutomationProperties.GetName(Card(window, Status)));
            Assert.Equal(
                "Space picks a widget up, arrow keys move it, Enter opens its options, Esc discards the changes.",
                AutomationProperties.GetHelpText(Card(window, Status)));
            Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.Button, ControlAutomationPeerFor(Card(window, Status)).GetAutomationControlType());

            // Zones T-076 5.2: 260 × 150 at 960 × 540, 12 from the edges; the top right one also from 12 (buttons hidden).
            Rect topLeft = BoundsIn(Zone(window, OverlayAnchor.TopLeft), window);
            Assert.Equal(new Point(12, 12), topLeft.TopLeft);
            Assert.Equal(260, topLeft.Width, 0.5);
            Assert.True(topLeft.Height >= 150);
            Rect topRight = BoundsIn(Zone(window, OverlayAnchor.TopRight), window);
            Assert.Equal(960 - 12, topRight.Right, 0.5);
            Assert.Equal(12, topRight.Top, 0.5);
            Rect bottomRight = BoundsIn(Zone(window, OverlayAnchor.BottomRight), window);
            Assert.Equal(540 - 12, bottomRight.Bottom, 0.5);

            // A widget without data is a placeholder card: its name and "no data right now".
            Assert.True(Ui.ShowsText(Card(window, OverlayWidgetIds.Cooldown), "Cool-down"));
            Assert.True(Ui.ShowsText(Card(window, OverlayWidgetIds.Cooldown), "no data right now"));

            // The toolbar is centred and inside the window.
            Rect toolbar = BoundsIn(window.Find<Border>("CameraLayoutToolbar"), window);
            Assert.InRange(toolbar.Center.X, 479, 481);
            Assert.InRange(toolbar.Center.Y, 269, 271);
            Assert.True(toolbar.Width <= 720);
            Assert.False(window.LayoutModeView.ToolbarContent.IsWrapped);
        });

    private static Avalonia.Automation.Peers.AutomationPeer ControlAutomationPeerFor(Control control) =>
        Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(control);

    public static TheoryData<OverlayAnchor> Anchors() => [.. OverlayAnchors.All];

    [Theory]
    [MemberData(nameof(Anchors))]
    public Task MouseDragMovesACardIntoEveryAnchorWithTheTargetHighlighted(OverlayAnchor anchor) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            EnterByButton(window);
            CameraLayoutCard status = Card(window, Status);
            Rect zone = BoundsIn(Zone(window, anchor), window);
            Point target = new(zone.Center.X, OverlayAnchors.IsTop(anchor) ? zone.Bottom - 10 : zone.Top + 10);
            List<string> expected = [.. ZoneCards(window, anchor).Where(static id => id != Status)];
            // Below all cards of a top zone, above all cards of a bottom zone.
            expected.Insert(OverlayAnchors.IsTop(anchor) ? expected.Count : 0, Status);

            Drag(window, status, target, beforeRelease: () =>
            {
                Assert.True(Mode(window).Card(Status)!.IsDragged);
                Assert.Contains("target", Zone(window, anchor).Classes);
                Assert.Single(OverlayAnchors.All, a => Zone(window, a).Classes.Contains("target"));
                Assert.True(Ui.ShowsText(Zone(window, anchor), "Drop here"));
                // An empty target zone says "Drop here" instead of its name; only one of the texts at a time.
                Assert.False(Ui.ShowsText(Zone(window, anchor), CameraLayoutModeViewModel.AnchorName(anchor)));
                Assert.Single(Ui.VisibleTexts(Zone(window, anchor)), static text => text == "Drop here");
                Assert.True(window.IsShown<Border>("CameraLayoutInsertIndicator"));
                Rect indicator = BoundsIn(window.Find<Border>("CameraLayoutInsertIndicator"), window);
                Assert.True(zone.Contains(indicator.Center), "The indicator is in the target zone.");
                Assert.Equal(zone.Width - 16, indicator.Width, 1);
                // The card follows the pointer, an outline stays at its place.
                Assert.True(Ui.CentreOf(status, window).X - target.X is > -2 and < 2 || status.RenderTransform is not null);
                Assert.True(status.GetVisualDescendants().OfType<Rectangle>().Single(static r => r.Classes.Contains("placeholderOutline")).IsVisible);
            });

            Assert.Equal(expected, ZoneCards(window, anchor));
            Assert.Equal(anchor, Mode(window).Card(Status)!.Anchor);
            Assert.DoesNotContain(OverlayAnchors.All, a => Zone(window, a).Classes.Contains("target"));
            Assert.False(window.IsShown<Border>("CameraLayoutInsertIndicator"));
            Assert.Null(Card(window, Status).RenderTransform);
            Assert.Equal($"Status moved: {CameraLayoutModeViewModel.AnchorName(anchor)}, position {expected.IndexOf(Status) + 1}", Mode(window).Announcement.TrimEnd());
        });

    [Fact]
    public Task MouseDragReordersWithinAnAnchorAndDropOnTheToolbarReturnsTheCard() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            EnterByButton(window);
            Assert.Equal([OverlayWidgetIds.Cooldown, Times], ZoneCards(window, OverlayAnchor.BottomLeft));

            // Times (at the bottom) above Cool-down.
            Rect cooldown = BoundsIn(Card(window, OverlayWidgetIds.Cooldown), window);
            Drag(window, Card(window, Times), new Point(cooldown.Center.X, cooldown.Top + 2), beforeRelease: () =>
            {
                Assert.Contains("target", Zone(window, OverlayAnchor.BottomLeft).Classes);
                Assert.Equal(0, Mode(window).Zone(OverlayAnchor.BottomLeft).InsertIndex);
            });
            Assert.Equal([Times, OverlayWidgetIds.Cooldown], ZoneCards(window, OverlayAnchor.BottomLeft));

            // Over the toolbar there is no target: the card goes back.
            CameraOverlayLayout draft = Mode(window).Draft!;
            Drag(window, Card(window, Status), BoundsIn(window.Find<Border>("CameraLayoutToolbar"), window).Center, beforeRelease: () =>
                Assert.DoesNotContain(OverlayAnchors.All, a => Zone(window, a).Classes.Contains("target")));
            Assert.Same(draft, Mode(window).Draft);
            Assert.Equal([Status], ZoneCards(window, OverlayAnchor.TopLeft));
        });

    [Fact]
    public Task ScreenReadersOpenAndCloseThePopoverThroughExpandCollapse() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            EnterByButton(window);
            CameraLayoutCard card = Card(window, Times);
            var provider = (Avalonia.Automation.Provider.IExpandCollapseProvider)ControlAutomationPeerFor(card);
            Assert.Equal(ExpandCollapseState.Collapsed, provider.ExpandCollapseState);

            provider.Expand();
            window.Settle();
            Assert.True(window.LayoutModeView.PopoverFlyout.IsOpen);
            Assert.Equal(ExpandCollapseState.Expanded, provider.ExpandCollapseState);

            provider.Collapse();
            window.Settle();
            Assert.False(window.LayoutModeView.PopoverFlyout.IsOpen);
            Assert.Equal(ExpandCollapseState.Collapsed, provider.ExpandCollapseState);
        });

    [Fact]
    public Task EscDuringAMouseDragCancelsOnlyTheDrag() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            EnterByButton(window);
            Rect target = BoundsIn(Zone(window, OverlayAnchor.BottomCenter), window);

            Drag(window, Card(window, Status), target.Center, beforeRelease: () =>
            {
                Press(window, Key.Escape, PhysicalKey.Escape);
                Assert.Null(Mode(window).Dragged);
                Assert.Null(Card(window, Status).RenderTransform);
                window.MouseMove(target.Center + new Vector(20, 0), RawInputModifiers.LeftMouseButton);
                window.Settle();
                Assert.Null(Mode(window).Dragged);
            });

            Assert.True(Mode(window).IsActive);
            Assert.Same(CameraOverlayLayout.Default, Mode(window).Draft);
            Assert.Equal([Status], ZoneCards(window, OverlayAnchor.TopLeft));
        });

    [Fact]
    public Task ClickOpensThePopoverWithHighlightAndSizes() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            EnterByButton(window);
            CameraLayoutCard times = Card(window, Times);
            double width = times.Bounds.Width;

            window.Click(times);
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.True(window.LayoutModeView.PopoverFlyout.IsOpen);
            Assert.Contains("selected", times.Classes);
            Assert.True(times.IsPopoverOpen);
            CameraLayoutPopover popover = window.LayoutModeView.Popover;
            Assert.Equal("CameraLayoutPopover", AutomationProperties.GetAutomationId(popover));
            Assert.Equal("Times", popover.Find<TextBlock>("CameraLayoutPopoverTitle").Text);
            ToggleSwitch emphasize = popover.Find<ToggleSwitch>("CameraLayoutEmphasize");
            Assert.Equal("Highlight", AutomationProperties.GetName(emphasize));
            Assert.Equal("Shows the key value in orange.", AutomationProperties.GetHelpText(emphasize));
            RadioButton large = popover.Find<RadioButton>("CameraLayoutSizeL");
            Assert.Equal("Large", AutomationProperties.GetName(large));
            Assert.Equal(["S", "M", "L"], SizeIds.Select(id => popover.Find<RadioButton>(id).Content));
            Assert.True(popover.Find<RadioButton>("CameraLayoutSizeM").IsChecked);
            Assert.Equal(new Size(40, 32), large.Bounds.Size);

            TopLevel popup = TopLevel.GetTopLevel(popover)!;
            popup.MouseDown(Ui.CentreOf(emphasize, popup), MouseButton.Left);
            popup.MouseUp(Ui.CentreOf(emphasize, popup), MouseButton.Left);
            popup.MouseDown(Ui.CentreOf(large, popup), MouseButton.Left);
            popup.MouseUp(Ui.CentreOf(large, popup), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.True(Mode(window).Draft!.Find(Times)!.Emphasized);
            Assert.Equal(OverlayWidgetSize.Large, Mode(window).Draft!.Find(Times)!.Size);
            // The card shows it right away: larger, the key value in the accent colour.
            Assert.True(times.Bounds.Width > width);
            Assert.Contains(times.GetVisualDescendants().OfType<Border>(), static b => b.Classes.Contains("overlayCard") && b.Classes.Contains("large"));
            Assert.Contains(times.GetVisualDescendants().OfType<TextBlock>(), static t => t.Name == "Value" && t.Classes.Contains("emphasized"));

            // Esc closes only the popover; the selection and the mode stay.
            popup.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            Assert.False(window.LayoutModeView.PopoverFlyout.IsOpen);
            Assert.True(Mode(window).IsActive);
            Assert.Contains("selected", times.Classes);
        });

    [Fact]
    public Task UnsupportedSizeIsDisabledWithATip() =>
        session.RunAsync(async scope =>
        {
            Dictionary<string, OverlayWidgetSize[]> sizes = new() { [Status] = [OverlayWidgetSize.Medium, OverlayWidgetSize.Large] };
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), widgets: LayoutTestWidgets.Registry(sizes));
            CameraWindow window = OpenNormal(harness);
            EnterByButton(window);

            window.Click(Card(window, Status));
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            RadioButton small = window.LayoutModeView.Popover.Find<RadioButton>("CameraLayoutSizeS");
            Assert.False(small.IsEffectivelyEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(small));
            Assert.Equal("This widget has no such size.", ToolTip.GetTip(small));
            Assert.True(window.LayoutModeView.Popover.Find<RadioButton>("CameraLayoutSizeL").IsEffectivelyEnabled);
        });

    [Fact]
    public Task KeyboardAloneMovesThroughEveryAnchorChangesSizeAndHighlightAndSaves() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            CameraLayoutModeViewModel mode = Mode(window);

            // Tab to "Edit layout", Enter: the focus is on the first card, with a visible ring.
            Button button = window.Find<Button>("CameraLayoutButton");
            Assert.True(button.Focus(NavigationMethod.Tab));
            Press(window, Key.Enter, PhysicalKey.Enter, "\r");
            Assert.True(mode.IsActive);
            Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + Status, FocusedId(window));
            Assert.Contains(":focus-visible", Card(window, Status).Classes);
            Assert.True(Card(window, Status).GetVisualDescendants().OfType<Border>().Single(static b => b.Classes.Contains("focusOuter")).IsEffectivelyVisible);

            // Tab follows the reading order: TL, TC, BL (top to bottom), BR, then the toolbar.
            Press(window, Key.Tab, PhysicalKey.Tab, "\t");
            Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + PrintName, FocusedId(window));
            Press(window, Key.Tab, PhysicalKey.Tab, "\t");
            Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + OverlayWidgetIds.Cooldown, FocusedId(window));
            Press(window, Key.Tab, PhysicalKey.Tab, "\t");
            Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + Times, FocusedId(window));
            Press(window, Key.Tab, PhysicalKey.Tab, "\t");
            Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + Temperatures, FocusedId(window));
            Press(window, Key.Tab, PhysicalKey.Tab, "\t");
            Assert.Same(window.Find<Button>("CameraLayoutReset"), Focused(window));

            // Arrow keys on a card that is not picked up move the focus.
            Card(window, Status).Focus(NavigationMethod.Tab);
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + PrintName, FocusedId(window));
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + Status, FocusedId(window));
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + OverlayWidgetIds.Cooldown, FocusedId(window));
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + Times, FocusedId(window));
            Card(window, Status).Focus(NavigationMethod.Tab);

            // Space picks Status up; it goes around all six anchors and back; the focus stays on it.
            Press(window, Key.Space, PhysicalKey.Space, " ");
            Assert.Contains("raised", Card(window, Status).Classes);
            Assert.Contains("target", Zone(window, OverlayAnchor.TopLeft).Classes);
            (Key Key, PhysicalKey Physical, OverlayAnchor Anchor)[] route =
            [
                (Key.Right, PhysicalKey.ArrowRight, OverlayAnchor.TopCenter),
                (Key.Right, PhysicalKey.ArrowRight, OverlayAnchor.TopRight),
                (Key.Down, PhysicalKey.ArrowDown, OverlayAnchor.BottomRight),
                (Key.Left, PhysicalKey.ArrowLeft, OverlayAnchor.BottomCenter),
                (Key.Left, PhysicalKey.ArrowLeft, OverlayAnchor.BottomLeft),
            ];
            foreach ((Key key, PhysicalKey physical, OverlayAnchor anchor) in route)
            {
                Press(window, key, physical);
                Assert.Contains(Status, ZoneCards(window, anchor));
                Assert.Contains("target", Zone(window, anchor).Classes);
                Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + Status, FocusedId(window));
                Assert.StartsWith("Status: " + CameraLayoutModeViewModel.AnchorName(anchor), window.Find<TextBlock>("CameraLayoutAnnouncement").Text);
            }

            // Order in the anchor: Home to the top, Up / Down by one.
            Assert.Equal([OverlayWidgetIds.Cooldown, Times, Status], ZoneCards(window, OverlayAnchor.BottomLeft));
            Press(window, Key.Home, PhysicalKey.Home);
            Assert.Equal([Status, OverlayWidgetIds.Cooldown, Times], ZoneCards(window, OverlayAnchor.BottomLeft));
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.Equal([OverlayWidgetIds.Cooldown, Status, Times], ZoneCards(window, OverlayAnchor.BottomLeft));
            Press(window, Key.Space, PhysicalKey.Space, " ");
            Assert.DoesNotContain("raised", Card(window, Status).Classes);
            Assert.Equal("Status moved: Bottom left, position 2", window.Find<TextBlock>("CameraLayoutAnnouncement").Text!.TrimEnd());

            // F2 opens the popover with the focus on "Highlight"; Space toggles it, Tab and the arrow keys choose the size.
            Press(window, Key.F2, PhysicalKey.F2);
            CameraLayoutPopover popover = window.LayoutModeView.Popover;
            Assert.True(window.LayoutModeView.PopoverFlyout.IsOpen);
            ToggleSwitch emphasize = popover.Find<ToggleSwitch>("CameraLayoutEmphasize");
            Assert.Same(emphasize, FocusedAnywhere(emphasize));
            TopLevel popup = TopLevel.GetTopLevel(popover)!;
            PressIn(popup, Key.Space, PhysicalKey.Space, " ");
            Assert.False(mode.Draft!.Find(Status)!.Emphasized);
            PressIn(popup, Key.Tab, PhysicalKey.Tab, "\t");
            RadioButton focusedSize = Assert.IsType<RadioButton>(FocusedAnywhere(emphasize));
            Assert.Equal("CameraLayoutSizeM", AutomationProperties.GetAutomationId(focusedSize));
            PressIn(popup, Key.Right, PhysicalKey.ArrowRight);
            Assert.Equal(OverlayWidgetSize.Large, mode.Draft!.Find(Status)!.Size);
            PressIn(popup, Key.Escape, PhysicalKey.Escape);
            window.Settle();
            Assert.False(window.LayoutModeView.PopoverFlyout.IsOpen);
            Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + Status, FocusedId(window));
            Assert.True(mode.IsActive);

            // Tab to Done, Enter: saved through the writer once.
            window.Find<Button>("CameraLayoutDone").Focus(NavigationMethod.Tab);
            Press(window, Key.Enter, PhysicalKey.Enter, "\r");
            await Ui.UntilAsync(() => !mode.IsActive, "the layout saved");
            window.Settle();
            CameraOverlayLayout saved = Assert.Single(harness.LayoutWriter.Saves).Layout;
            Assert.Equal(OverlayAnchor.BottomLeft, saved.Find(Status)!.Anchor);
            Assert.False(saved.Find(Status)!.Emphasized);
            Assert.Equal(OverlayWidgetSize.Large, saved.Find(Status)!.Size);
            Assert.Equal([Times, Status, OverlayWidgetIds.Cooldown], saved.InAnchor(OverlayAnchor.BottomLeft).Select(static p => p.WidgetId));
            Assert.Equal("Layout saved", window.Find<TextBlock>("CameraLayoutAnnouncement").Text);
            // The window shows the saved layout at once, and its buttons again.
            Assert.True(window.IsShown<Panel>("CameraOverlay"));
            Assert.Contains(Status, ViewModel(window).Overlay.BottomLeft.Select(static w => w.Id));
            Assert.True(window.Find<StackPanel>("CameraWindowButtons").IsVisible);
        });

    private static void PressIn(TopLevel topLevel, Key key, PhysicalKey physical, string? symbol = null)
    {
        topLevel.KeyPress(key, RawInputModifiers.None, physical, symbol);
        topLevel.KeyRelease(key, RawInputModifiers.None, physical, symbol);
        Dispatcher.UIThread.RunJobs();
        topLevel.Settle();
    }

    [Fact]
    public Task EscOnAPickedUpCardPutsItBackAndEscAgainDiscardsTheEdit() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            EnterByButton(window);
            CameraLayoutModeViewModel mode = Mode(window);
            Drag(window, Card(window, Temperatures), BoundsIn(Zone(window, OverlayAnchor.TopRight), window).Center);
            CameraOverlayLayout moved = mode.Draft!;

            Card(window, Status).Focus(NavigationMethod.Tab);
            Press(window, Key.Space, PhysicalKey.Space, " ");
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Press(window, Key.Escape, PhysicalKey.Escape);

            Assert.True(mode.IsActive);
            Assert.Equal(moved, mode.Draft);
            Assert.Equal("Move cancelled", window.Find<TextBlock>("CameraLayoutAnnouncement").Text);
            Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + Status, FocusedId(window));

            Press(window, Key.Escape, PhysicalKey.Escape);

            Assert.False(mode.IsActive);
            Assert.Empty(harness.LayoutWriter.Saves);
            Assert.Equal("Layout changes discarded", window.Find<TextBlock>("CameraLayoutAnnouncement").Text);
            Assert.Same(CameraOverlayLayout.Default, ViewModel(window).Overlay.Layout);
            Assert.True(ViewModel(window).IsNormal);
        });

    [Fact]
    public Task EscWithTheFocusOutsideTheModeDiscardsAndCancelButtonToo() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            EnterByButton(window);
            window.FocusManager?.Focus(null);

            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(Mode(window).IsActive);
            Assert.True(ViewModel(window).IsNormal);

            EnterByButton(window);
            window.Click(window.Find<Button>("CameraLayoutCancel"));
            Assert.False(Mode(window).IsActive);
            Assert.Empty(harness.LayoutWriter.Saves);
        });

    [Fact]
    public Task ResetRestoresTheDefaultsAndASaveErrorKeepsTheMode() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            EnterByButton(window);
            Drag(window, Card(window, Status), BoundsIn(Zone(window, OverlayAnchor.BottomCenter), window).Center);

            window.Click(window.Find<Button>("CameraLayoutReset"));
            Assert.Equal(CameraOverlayLayout.Default, Mode(window).Draft);
            Assert.Equal([Status], ZoneCards(window, OverlayAnchor.TopLeft));

            harness.LayoutWriter.Failure = new IOException("Locked.");
            Drag(window, Card(window, Status), BoundsIn(Zone(window, OverlayAnchor.BottomCenter), window).Center);
            window.Click(window.Find<Button>("CameraLayoutDone"));
            await Ui.UntilAsync(() => Mode(window).HasSaveError, "the save error");
            window.Settle();

            Assert.True(Mode(window).IsActive);
            Assert.True(window.IsShown<TextBlock>("CameraLayoutSaveError"));
            Assert.Equal("The layout could not be saved. Try again.", window.Find<TextBlock>("CameraLayoutSaveError").Text);
            Assert.Equal([Status], ZoneCards(window, OverlayAnchor.BottomCenter));

            harness.LayoutWriter.Failure = null;
            window.Click(window.Find<Button>("CameraLayoutDone"));
            await Ui.UntilAsync(() => !Mode(window).IsActive, "the layout saved");
            Assert.Equal(2, harness.LayoutWriter.Saves.Count);
        });

    [Fact]
    public Task AnnouncementsGoToAPoliteLiveRegion() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            TextBlock live = window.Find<TextBlock>("CameraLayoutAnnouncement");

            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(live));
            EnterByButton(window);

            Assert.Equal("Layout mode. Space picks a widget up, arrow keys move it, Enter opens its options, Esc discards the changes.", live.Text);
            Assert.Equal(live.Text, AutomationProperties.GetName(live));
            Card(window, Times).Focus(NavigationMethod.Tab);
            Press(window, Key.Space, PhysicalKey.Space, " ");
            Assert.Equal("Times picked up. Use the arrow keys to choose a place, Space to drop, Esc to cancel.", live.Text);
            Press(window, Key.Up, PhysicalKey.ArrowUp);
            Assert.Equal("Times: Bottom left, position 1 of 2", live.Text);
            Assert.Equal("Times: Bottom left, position 1 of 2", AutomationProperties.GetName(Card(window, Times)));
            Press(window, Key.Enter, PhysicalKey.Enter, "\r");
            Assert.Equal("Times moved: Bottom left, position 1", live.Text);
            window.Click(window.Find<Button>("CameraLayoutReset"));
            Assert.Equal("Default layout restored", live.Text);
            // Still there after the mode ended.
            window.Click(window.Find<Button>("CameraLayoutCancel"));
            Assert.Equal("Layout changes discarded", live.Text);
            Assert.True(live.IsEffectivelyVisible);
        });

    [Fact]
    public Task SwitchingToFullScreenOrTheMiniPreviewDiscardsTheEdit() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            EnterByButton(window);
            Drag(window, Card(window, Status), BoundsIn(Zone(window, OverlayAnchor.BottomCenter), window).Center);

            Press(window, Key.F11, PhysicalKey.F11);
            Assert.True(ViewModel(window).IsFullScreen);
            Assert.False(Mode(window).IsActive);
            Assert.Same(CameraOverlayLayout.Default, ViewModel(window).Overlay.Layout);
            Assert.False(window.IsShown<Panel>("CameraLayoutOverlay"));

            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.True(ViewModel(window).IsNormal);
            EnterByButton(window);
            ViewModel(window).ShowMiniCommand.Execute(null);
            window.Settle();
            Assert.False(Mode(window).IsActive);
            Assert.Empty(harness.LayoutWriter.Saves);
        });

    [Fact]
    public Task SavedLayoutIsUsedInFullScreenAndWhenTheWindowOpensAgain() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            EnterByButton(window);
            Drag(window, Card(window, Temperatures), BoundsIn(Zone(window, OverlayAnchor.TopRight), window).Center);
            window.Click(window.Find<Button>("CameraLayoutDone"));
            await Ui.UntilAsync(() => !Mode(window).IsActive, "the layout saved");
            CameraOverlayLayout saved = harness.LayoutWriter.Saves.Single().Layout;
            window.Settle();
            Assert.Contains(window.Find<ItemsControl>("CameraOverlayAnchorTopRight").Items.OfType<OverlayWidgetViewModel>(), static w => w.Id == Temperatures);

            ViewModel(window).ShowFullScreenCommand.Execute(null);
            window.Settle();
            Assert.Contains(window.Find<ItemsControl>("CameraOverlayAnchorTopRight").Items.OfType<OverlayWidgetViewModel>(), static w => w.Id == Temperatures);

            // The monitoring session takes the saved layout (T-091, MonitoringSessionLayoutTests); a window opened again uses it.
            ((LayerPeek.App.Tray.IMonitoringObserver)harness.Printers).UpdateSettings(new AppSettings
            {
                Printers = [CameraHarness.CameraPrinter("a", "Alpha", overlay: saved), CameraHarness.CameraPrinter("b", "Beta")],
            });
            harness.Windows.Toggle("a");
            Dispatcher.UIThread.RunJobs();
            CameraWindow again = harness.Open();
            again.Settle();
            Assert.Equal(saved, ViewModel(again).Overlay.Layout);
            Assert.Contains(again.Find<ItemsControl>("CameraOverlayAnchorTopRight").Items.OfType<OverlayWidgetViewModel>(), static w => w.Id == Temperatures);
        });

    [Fact]
    public Task LauncherOpensAClosedWindowInTheLayoutMode() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraOverlayLayout starting = CameraOverlayLayout.Default.WithVisible(PrintName, false);

            Assert.True(harness.Windows.Start("a", starting));
            CameraWindow window = harness.Windows.OpenWindows["a"];
            window.Settle();

            Assert.True(window.IsVisible);
            Assert.True(ViewModel(window).IsNormal);
            Assert.True(Mode(window).IsActive);
            Assert.Same(starting, Mode(window).Draft);
            Assert.False(window.IsShown<CameraLayoutCard>(CameraLayoutCardViewModel.AutomationIdPrefix + PrintName));
            Assert.Equal(CameraLayoutCardViewModel.AutomationIdPrefix + Status, FocusedId(window));

            // Esc changes nothing: no write, the window shows the saved layout.
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Empty(harness.LayoutWriter.Saves);
            Assert.Same(CameraOverlayLayout.Default, ViewModel(window).Overlay.Layout);
            Assert.False(harness.Windows.Start("c", null));
        });

    [Theory]
    [InlineData(CameraWindowStartMode.Mini)]
    [InlineData(CameraWindowStartMode.FullScreen)]
    public Task LauncherSwitchesAnOpenWindowToTheNormalWindowAndItStaysThere(CameraWindowStartMode start) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(window: CameraWindowDefaults.Default with { StartMode = start });
            CameraWindow window = harness.Open();
            window.Settle();
            Assert.False(ViewModel(window).IsNormal);

            Assert.True(harness.Windows.Start("a", null));
            window.Settle();

            Assert.True(ViewModel(window).IsNormal);
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
            Assert.True(Mode(window).IsActive);
            Assert.Same(CameraOverlayLayout.Default, Mode(window).Draft);

            window.Click(window.Find<Button>("CameraLayoutDone"));
            await Ui.UntilAsync(() => !Mode(window).IsActive, "the layout saved");
            Assert.True(ViewModel(window).IsNormal);
        });

    [Fact]
    public Task LauncherWhileEditingKeepsTheEdit() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness);
            EnterByButton(window);
            Drag(window, Card(window, Status), BoundsIn(Zone(window, OverlayAnchor.BottomCenter), window).Center);
            CameraOverlayLayout draft = Mode(window).Draft!;

            Assert.True(harness.Windows.Start("a", CameraOverlayLayout.Default));
            window.Settle();

            Assert.Same(draft, Mode(window).Draft);
            Assert.True(Mode(window).IsActive);
        });

    public static TheoryData<string> Endings => ["done", "esc", "fullscreen", "mini"];

    [Theory]
    [MemberData(nameof(Endings))]
    public Task LauncherEnlargesASmallWindowAndRestoresIt(string ending) =>
        session.RunAsync(async scope =>
        {
            MemoryCameraWindowStateStore states = new();
            await using CameraHarness harness = Harness(states: states);
            CameraWindow window = OpenNormal(harness, 400, 225);
            window.Position = new PixelPoint(300, 200);
            window.Settle();
            Size original = window.ClientSize;
            PixelPoint position = window.Position;
            Assert.False(ViewModel(window).CanEditLayout);

            Assert.True(harness.Windows.Start("a", null));
            window.Settle();

            Assert.Equal(new Size(640, 360), window.ClientSize);
            Assert.Equal(position, window.Position);
            Assert.True(window.HasTemporaryLayoutSpace);
            Assert.True(Mode(window).IsActive);

            switch (ending)
            {
                case "done":
                    window.Click(window.Find<Button>("CameraLayoutDone"));
                    await Ui.UntilAsync(() => !Mode(window).IsActive, "the layout saved");
                    break;
                case "esc":
                    Press(window, Key.Escape, PhysicalKey.Escape);
                    break;
                case "fullscreen":
                    Press(window, Key.F11, PhysicalKey.F11);
                    Press(window, Key.Escape, PhysicalKey.Escape);
                    break;
                default:
                    ViewModel(window).ShowMiniCommand.Execute(null);
                    window.Settle();
                    ViewModel(window).ShowNormalCommand.Execute(null);
                    break;
            }

            window.Settle();
            Assert.False(Mode(window).IsActive);
            Assert.True(ViewModel(window).IsNormal);
            Assert.Equal(original, window.ClientSize);
            Assert.Equal(position, window.Position);
            Assert.False(window.HasTemporaryLayoutSpace);

            // The temporary size is never remembered (camera-windows.json).
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new CameraWindowPlacement(position.X, position.Y, original.Width, original.Height), states.Get("a").Normal);
        });

    [Fact]
    public Task ClosingTheEnlargedWindowRemembersItsOwnSize() =>
        session.RunAsync(async scope =>
        {
            MemoryCameraWindowStateStore states = new();
            await using CameraHarness harness = Harness(states: states);
            CameraWindow window = OpenNormal(harness, 400, 225);
            window.Position = new PixelPoint(300, 200);
            window.Settle();
            harness.Windows.Start("a", null);
            window.Settle();
            Assert.Equal(new Size(640, 360), window.ClientSize);

            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new CameraWindowPlacement(300, 200, 400, 225), states.Get("a").Normal);
            Assert.Empty(harness.LayoutWriter.Saves);
        });

    [Fact]
    public Task LauncherMovesAnEnlargedWindowIntoTheWorkArea() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = OpenNormal(harness, 320, 180);
            // Bottom right of the primary monitor (2560 × 1440, task bar 48).
            window.Position = new PixelPoint(2560 - 340, 1440 - 48 - 230);
            window.Settle();
            PixelPoint position = window.Position;

            harness.Windows.Start("a", null);
            window.Settle();

            Assert.Equal(new Size(640, 360), window.ClientSize);
            Assert.True(window.Position.X < position.X && window.Position.Y < position.Y);

            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Equal(position, window.Position);
            Assert.Equal(new Size(320, 180), window.ClientSize);
        });

    public static TheoryData<string> Languages => ["en", "cs", "de", "es", "fr", "it", "pl", "sk"];

    [Theory]
    [MemberData(nameof(Languages))]
    public Task EveryLanguageFitsTheSmallestWindow(string language) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            new UiLanguage().Apply(language);
            CameraWindow window = OpenNormal(harness, 480, 300);
            EnterByButton(window);
            window.Click(Card(window, Status));
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Rect client = new(window.ClientSize);
            Rect toolbar = BoundsIn(window.Find<Border>("CameraLayoutToolbar"), window);
            Assert.True(client.Contains(toolbar), $"{language}: the toolbar {toolbar} fits {client}.");
            foreach (Button button in window.LayoutModeView.Root.GetVisualDescendants().OfType<Button>())
            {
                Rect bounds = BoundsIn(button, window);
                Assert.True(toolbar.Contains(bounds), $"{language}: {button.Content} inside the toolbar.");
                Assert.True(button.DesiredSize.Width - button.Margin.Left - button.Margin.Right <= button.Bounds.Width + 0.5, $"{language}: {button.Content} not cut.");
            }

            foreach (TextBlock text in window.Find<Border>("CameraLayoutToolbar").GetVisualDescendants().OfType<TextBlock>().Where(static t => t.IsEffectivelyVisible))
            {
                Assert.True(toolbar.Contains(BoundsIn(text, window)), $"{language}: '{text.Text}' inside the toolbar.");
            }

            foreach (OverlayAnchor anchor in OverlayAnchors.All)
            {
                Assert.True(client.Contains(BoundsIn(Zone(window, anchor), window)), $"{language}: zone {anchor} inside the window.");
            }

            // The popover's texts are not cut either.
            foreach (TextBlock text in window.LayoutModeView.Popover.GetVisualDescendants().OfType<TextBlock>().Where(static t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
            {
                Assert.False(text.TextLayout.TextLines.Any(static line => line.HasCollapsed), $"{language}: '{text.Text}' is cut in the popover.");
            }

            Assert.Equal(LocalizedStrings.Instance["CameraLayoutTitle"], AutomationProperties.GetName(window.Find<Border>("CameraLayoutToolbar")));
        });

    [Fact]
    public Task SnapshotOfTheLayoutModeWithAPickedUpCard() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            (CameraWindow window, _) = await harness.OpenStreamingAsync(960, 540, 0xFF506070);
            harness.Monitor.Raise(Snapshot(PrinterState.Printing, Job(42.7, TimeSpan.FromMinutes(95)), printerId: "a"));
            await Ui.UntilAsync(() => ViewModel(window).Overlay.Widgets.All(static w => w.Id == OverlayWidgetIds.Cooldown || w.IsShown), "the cards shown");
            window.Width = 960;
            window.Height = 540;
            window.Settle();
            EnterByButton(window);
            window.Find<Border>("CameraLayoutDim").Transitions = null;
            Card(window, Times).Focus(NavigationMethod.Tab);
            Press(window, Key.Space, PhysicalKey.Space, " ");
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Mode(window).Select(Mode(window).Card(Status));
            window.Settle();
            // The selection outline (2 outside the card) is not cut by the card.
            Border selection = Card(window, Status).GetVisualDescendants().OfType<Border>().Single(static b => b.Classes.Contains("selection"));
            Assert.DoesNotContain(selection.GetVisualAncestors().OfType<Visual>().TakeWhile(static v => v is not CameraLayoutOverlay), static v => v.ClipToBounds);

            SnapshotAssert.Matches(window, "camera-layout-mode");
        });

    [Fact]
    public Task SnapshotOfTheLayoutModeInTheSmallestWindowInGerman() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            new UiLanguage().Apply("de");
            (CameraWindow window, _) = await harness.OpenStreamingAsync(960, 540, 0xFF506070);
            window.Width = 480;
            window.Height = 300;
            window.Settle();
            EnterByButton(window);
            window.Find<Border>("CameraLayoutDim").Transitions = null;
            Assert.True(window.LayoutModeView.ToolbarContent.IsWrapped);

            SnapshotAssert.Matches(window, "camera-layout-mode-de-480");
        });

    [Fact]
    public Task ProductionWidgetsTemperaturesAndPrintNameAreCardsThatMoveAndSave() =>
        session.RunAsync(async scope =>
        {
            // The real registration (T-082..T-085): Status, Times, Temperatures and Print name; Cool-down is not registered yet.
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), widgets: OverlayWidgets.CreateRegistry());
            CameraWindow window = OpenNormal(harness);
            CameraLayoutModeViewModel mode = Mode(window);
            EnterByButton(window);

            // Reading order: top left, top centre, bottom left, bottom right; no card for the unregistered Cool-down.
            Assert.Equal([Status, PrintName, Times, Temperatures], mode.Cards.Select(static card => card.WidgetId));
            Assert.Null(mode.Card(OverlayWidgetIds.Cooldown));
            Assert.True(Card(window, Temperatures).IsEffectivelyVisible);
            Assert.True(Card(window, PrintName).IsEffectivelyVisible);
            Assert.Equal("Temperatures: Bottom right, position 1 of 1", AutomationProperties.GetName(Card(window, Temperatures)));

            // Keyboard: Temperatures from the bottom right up into the top right anchor.
            Card(window, Temperatures).Focus(NavigationMethod.Tab);
            Press(window, Key.Space, PhysicalKey.Space, " ");
            Press(window, Key.Up, PhysicalKey.ArrowUp);
            Press(window, Key.Space, PhysicalKey.Space, " ");
            Assert.Equal([Temperatures], ZoneCards(window, OverlayAnchor.TopRight));

            // Mouse: Print name from the top centre into the bottom centre anchor.
            Drag(window, Card(window, PrintName), BoundsIn(Zone(window, OverlayAnchor.BottomCenter), window).Center);
            Assert.Equal([PrintName], ZoneCards(window, OverlayAnchor.BottomCenter));

            window.Click(window.Find<Button>("CameraLayoutDone"));
            await Ui.UntilAsync(() => !mode.IsActive, "the layout saved");
            window.Settle();
            CameraOverlayLayout saved = Assert.Single(harness.LayoutWriter.Saves).Layout;
            Assert.Equal(OverlayAnchor.TopRight, saved.Find(Temperatures)!.Anchor);
            Assert.Equal(OverlayAnchor.BottomCenter, saved.Find(PrintName)!.Anchor);
            // The window's overlay draws the real widgets where they were put.
            Assert.Contains(Temperatures, ViewModel(window).Overlay.TopRight.Select(static w => w.Id));
            Assert.Contains(PrintName, ViewModel(window).Overlay.BottomCenter.Select(static w => w.Id));
            Assert.True(window.IsShown<Panel>("CameraOverlay"));
        });
}
