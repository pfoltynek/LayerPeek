using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.UiTests.Infrastructure;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// The macOS shortcuts of the settings window (T-100, user decision 2026-10-29): ⌘⇧] / ⌘⇧[ switch the main tabs, ⌘⌥→ /
/// ⌘⌥← the sub-tabs of the printer form, ⌘W and ⌘. are Cancel. Ctrl+Tab and Ctrl+PageDown stay. Forced through
/// <see cref="Windows.SettingsWindow.UsesMacShortcuts"/> on every OS; the Windows variant is checked the same way.
/// </summary>
public sealed class SettingsWindowMacShortcutsUiTests(HeadlessSession session)
{
    private const RawInputModifiers Command = RawInputModifiers.Meta;

    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers, string? symbol = null)
    {
        window.KeyPress(key, modifiers, physical, symbol);
        window.KeyRelease(key, modifiers, physical, symbol);
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    /// <summary>A key that closes the window: only pressed (a closed headless window takes no more input).</summary>
    private static void PressClosing(Window window, Key key, PhysicalKey physical, string symbol)
    {
        window.KeyPress(key, Command, physical, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    private static string? FocusedId(Window window) =>
        (window.FocusManager?.GetFocusedElement() as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(AutomationProperties.GetAutomationId)
            .FirstOrDefault(static id => !string.IsNullOrEmpty(id));

    private static async Task<SettingsWindowHarness> OpenAsync(UiTestScope scope, bool mac, LayerPeek.App.Tests.Settings.FakeEditorStore? store = null)
    {
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store ?? SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")), macTerms: mac);
        ui.Window.UsesMacShortcuts = mac;
        return ui;
    }

    [Fact]
    public Task CommandShiftBracketsSwitchTheMainTabsAndCommandOptionArrowsTheSubTabs() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, mac: true);
            Window window = ui.Window;
            Assert.True(ui.NameBox.Focus());

            // In a text box too (tunnelling): ⌘⌥→ / ⌘⌥← have no meaning for text on macOS.
            Press(window, Key.Right, PhysicalKey.ArrowRight, Command | RawInputModifiers.Alt);
            Assert.True(ui.Find<TabItem>("PrinterTabCamera").IsSelected);
            Assert.Equal("PrinterTabCamera", FocusedId(window));
            Press(window, Key.Left, PhysicalKey.ArrowLeft, Command | RawInputModifiers.Alt);
            Press(window, Key.Left, PhysicalKey.ArrowLeft, Command | RawInputModifiers.Alt);
            Assert.True(ui.Find<TabItem>("PrinterTabCameraWindow").IsSelected);

            // ⌘⇧] on a US layout: the logical key is the bracket (⇧ makes it "}").
            Press(window, Key.OemCloseBrackets, PhysicalKey.BracketRight, Command | RawInputModifiers.Shift, "}");
            Assert.True(ui.Find<TabItem>("SettingsTabWidgets").IsSelected);
            Assert.Equal("SettingsTabWidgets", FocusedId(window));
            Press(window, Key.OemOpenBrackets, PhysicalKey.BracketLeft, Command | RawInputModifiers.Shift, "{");
            Assert.True(ui.Find<TabItem>("SettingsTabPrinters").IsSelected);
            Press(window, Key.OemOpenBrackets, PhysicalKey.BracketLeft, Command | RawInputModifiers.Shift, "{");
            Assert.True(ui.Find<TabItem>("SettingsTabStartupUpdates").IsSelected);

            // ⌘⌥→ outside the printer form does nothing.
            Press(window, Key.Right, PhysicalKey.ArrowRight, Command | RawInputModifiers.Alt);
            Assert.True(ui.Find<TabItem>("SettingsTabStartupUpdates").IsSelected);

            // The keys of Windows stay: Ctrl+Tab.
            Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
            Assert.True(ui.Find<TabItem>("SettingsTabPrinters").IsSelected);
        });

    [Fact]
    public Task TheBracketKeysAreFoundByTheirPositionOnANonUsLayout() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, mac: true);
            Window window = ui.Window;

            // Czech layout: the keys right of P type ú and ) — with ⇧ "/" and "(" — so the logical key is no bracket.
            Press(window, Key.OemQuestion, PhysicalKey.BracketLeft, Command | RawInputModifiers.Shift, "/");
            Assert.True(ui.Find<TabItem>("SettingsTabStartupUpdates").IsSelected);
            Press(window, Key.None, PhysicalKey.BracketRight, Command | RawInputModifiers.Shift, "(");
            Assert.True(ui.Find<TabItem>("SettingsTabPrinters").IsSelected);

            // Only with exactly ⌘⇧: ⌘] alone does nothing.
            Press(window, Key.OemCloseBrackets, PhysicalKey.BracketRight, Command, "]");
            Assert.True(ui.Find<TabItem>("SettingsTabPrinters").IsSelected);
        });

    [Theory]
    [InlineData(Key.W, PhysicalKey.W, "w")]
    [InlineData(Key.OemPeriod, PhysicalKey.Period, ".")]
    public Task CommandWAndCommandPeriodAreCancel(Key key, PhysicalKey physical, string symbol) =>
        session.RunAsync(async scope =>
        {
            LayerPeek.App.Tests.Settings.FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha"));
            SettingsWindowHarness ui = await OpenAsync(scope, mac: true, store);
            ui.Window.TypeOver(ui.NameBox, "Renamed");
            Assert.True(ui.ViewModel.IsDirty);

            PressClosing(ui.Window, key, physical, symbol);

            // Like Cancel: nothing saved, the edits are gone with the window.
            Assert.Empty(store.Saves);
            Assert.False(ui.IsOpen);
            Assert.True(ui.ViewModel.IsDisposed);
            Assert.False(ui.Slot.IsOpen);
        });

    [Fact]
    public Task CommandWWhileSavingKeepsTheWindowOpenLikeTheDisabledCancel() =>
        session.RunAsync(static async scope =>
        {
            LayerPeek.App.Tests.Settings.FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha"));
            SettingsWindowHarness ui = await OpenAsync(scope, mac: true, store);
            ui.Window.TypeOver(ui.NameBox, "Renamed");
            store.SaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ui.ViewModel.SaveCommand.Execute(null);
            Assert.True(ui.ViewModel.IsSaving);

            Press(ui.Window, Key.W, PhysicalKey.W, Command, "w");
            Press(ui.Window, Key.OemPeriod, PhysicalKey.Period, Command, ".");
            Assert.True(ui.IsOpen);

            store.SaveGate.SetResult();
            await Ui.UntilAsync(() => !ui.IsOpen, "the window closed after saving");
        });

    [Fact]
    public Task WindowsVariantIgnoresTheMacShortcuts() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, mac: false);
            Window window = ui.Window;
            Assert.True(ui.NameBox.Focus());

            Press(window, Key.OemCloseBrackets, PhysicalKey.BracketRight, Command | RawInputModifiers.Shift, "}");
            Assert.True(ui.Find<TabItem>("SettingsTabPrinters").IsSelected);
            Press(window, Key.Right, PhysicalKey.ArrowRight, Command | RawInputModifiers.Alt);
            Assert.True(ui.Find<TabItem>("PrinterTabConnection").IsSelected);
            Press(window, Key.W, PhysicalKey.W, Command, "w");
            Press(window, Key.OemPeriod, PhysicalKey.Period, Command, ".");
            Assert.True(ui.IsOpen);

            // Ctrl+PageDown and Ctrl+Tab as before.
            Press(window, Key.PageDown, PhysicalKey.PageDown, RawInputModifiers.Control);
            Assert.True(ui.Find<TabItem>("PrinterTabCamera").IsSelected);
            Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
            Assert.True(ui.Find<TabItem>("SettingsTabWidgets").IsSelected);
        });
}
