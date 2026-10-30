using System.ComponentModel;

using LayerPeek.App.Cameras;
using LayerPeek.App.Composition;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Settings.Widgets;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The "Mini preview" item of the Widgets tab (T-110, design T-104 2): the list entry, the panel of the selected printer,
/// the edited copy saved by Save and dropped by Cancel, "Restore defaults" and "Use layout for all printers" (Q10 A), the
/// camera off, and the remembered window state, which never forgets these settings (Q9 A). All in memory.
/// </summary>
public sealed class MiniStripSettingsTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");
    private static readonly CameraMiniStripSettings NoRemaining = new() { Remaining = false };

    private static readonly CameraMiniStripSettings AllOff = new()
    {
        ProgressBar = false,
        Percent = false,
        Remaining = false,
        Finish = false,
        Cooldown = false,
        NameRow = false,
        ChartRow = false,
    };

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private readonly MemoryCameraWindowStateStore _states = new();
    private SettingsViewModel? _viewModel;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
    }

    private static PrinterDefinition Printer(string id, string name, bool camera = true, CameraMiniStripSettings? strip = null, CameraOverlayLayout? layout = null) =>
        LinkPrinter(id, name, $"192.168.1.{20 + id.Length}") with
        {
            Camera = new CameraSettings(camera, StreamUri)
            {
                MiniStrip = strip ?? CameraMiniStripSettings.Default,
                Overlay = layout ?? CameraOverlayLayout.Default,
            },
        };

    private async Task<SettingsViewModel> LoadAsync(params PrinterDefinition[] printers)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = printers }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(
            _store,
            new FakeConnectionTester(),
            Supported,
            cameraTester: new FakeCameraTester(),
            widgets: OverlayWidgets.CreateRegistry(),
            cameraWindowStates: _states);
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }

    private async Task<AppSettings> SaveAsync(SettingsViewModel vm)
    {
        await vm.SaveCommand.ExecuteAsync(null);
        return Assert.Single(_store.Saves).Updated;
    }

    private static MiniStripSettingsViewModel Select(SettingsViewModel vm)
    {
        vm.SelectedWidgetsListEntry = vm.MiniStripEntry;
        return Assert.IsType<MiniStripSettingsViewModel>(vm.MiniStripPanel);
    }

    // ---- The list entry ----

    [Fact]
    public async Task ListEndsWithASeparatorAndTheMiniPreviewItem()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));

        Assert.Equal(vm.Widgets.Count + 2, vm.WidgetsListEntries.Count);
        Assert.Equal(vm.Widgets, vm.WidgetsListEntries.Take(vm.Widgets.Count));
        WidgetsListEntry separator = vm.WidgetsListEntries[^2];
        Assert.IsType<WidgetsSeparatorEntry>(separator);
        Assert.False(separator.IsSelectable);
        Assert.Equal(9, separator.RowHeight);
        Assert.Equal(Avalonia.Automation.AccessibilityView.Raw, separator.AccessibilityView);
        MiniStripListEntry entry = Assert.IsType<MiniStripListEntry>(vm.WidgetsListEntries[^1]);
        Assert.Same(vm.MiniStripEntry, entry);
        Assert.True(entry.IsSelectable);
        Assert.Equal(32, entry.RowHeight);
        Assert.Equal("WidgetsItem-miniStrip", entry.AutomationId);
        Assert.Equal("Mini preview", entry.Name);
        Assert.Equal("Mini preview", entry.AutomationName);
        Assert.Equal("IconPictureInPicture16Regular", entry.IconKey);
        Assert.Equal(Strings.SettingsMiniStripDescription, entry.Description);
        Assert.True(entry.HasDescription);
        // The first widget stays the default selection.
        Assert.Same(vm.Widgets[0], vm.SelectedWidgetsListEntry);
        Assert.Null(vm.MiniStripPanel);
    }

    [Fact]
    public async Task SelectingTheItemShowsItsPanelInsteadOfTheWidgetsAndBack()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        WidgetItemViewModel cooldown = vm.Widgets.Single(static widget => widget.Id == OverlayWidgetIds.Cooldown);
        vm.SelectedWidget = cooldown;
        Assert.NotNull(vm.SelectedWidgetPanel);
        List<string?> changes = [];
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        MiniStripSettingsViewModel panel = Select(vm);

        Assert.True(vm.IsMiniStripSelected);
        Assert.Same(vm.MiniStripEntry, vm.SelectedWidgetsListEntry);
        Assert.Null(vm.SelectedWidget);
        Assert.Null(vm.SelectedWidgetPanel);
        Assert.False(vm.ShowsNoWidgetSettings);
        Assert.Same(vm.Printers[0], panel.Printer);
        Assert.True(panel.CanEdit);
        Assert.Contains(nameof(SettingsViewModel.MiniStripPanel), changes);
        Assert.Contains(nameof(SettingsViewModel.SelectedWidgetsListEntry), changes);

        // The separator and null (a list being rebuilt) change nothing.
        vm.SelectedWidgetsListEntry = vm.WidgetsListEntries[^2];
        vm.SelectedWidgetsListEntry = null;
        Assert.Same(vm.MiniStripEntry, vm.SelectedWidgetsListEntry);

        vm.SelectedWidgetsListEntry = cooldown;
        Assert.False(vm.IsMiniStripSelected);
        Assert.Same(cooldown, vm.SelectedWidget);
        Assert.NotNull(vm.SelectedWidgetPanel);
        Assert.Null(vm.MiniStripPanel);
    }

    [Fact]
    public async Task SpaceOnTheItemDoesNothing()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        Select(vm);

        Assert.False(vm.ToggleSelectedWidget());

        Assert.Equal(CameraOverlayLayout.Default, vm.Printers[0].OverlayLayout);
        Assert.Same(CameraMiniStripSettings.Default, vm.Printers[0].MiniStrip);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task NoPrinterHidesTheItemAndHasNoPanel()
    {
        SettingsViewModel vm = await LoadAsync();
        vm.SelectedWidgetsListEntry = vm.MiniStripEntry;

        Assert.False(vm.ShowsWidgets);
        Assert.True(vm.IsMiniStripSelected);
        Assert.Null(vm.MiniStripPanel);

        vm.AddPrinterCommand.Execute(null);

        Assert.True(vm.ShowsWidgets);
        Assert.Same(vm.SelectedPrinter, vm.MiniStripPanel?.Printer);
    }

    // ---- The edited copy ----

    [Fact]
    public async Task CheckBoxesEditTheCopyAndSaveWritesOnlyThatPrinter()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"), Printer("b", "Beta", strip: NoRemaining));
        MiniStripSettingsViewModel panel = Select(vm);
        List<string?> changes = [];
        panel.Printer.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        panel.Printer.MiniStripPercent = false;
        panel.Printer.MiniStripChartRow = false;

        Assert.Equal(new CameraMiniStripSettings { Percent = false, ChartRow = false }, vm.Printers[0].MiniStrip);
        Assert.Contains(nameof(PrinterEditorViewModel.MiniStripPercent), changes);
        Assert.Contains(nameof(PrinterEditorViewModel.MiniStrip), changes);
        Assert.True(vm.IsDirty);
        Assert.Empty(_store.Saves);

        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(new CameraMiniStripSettings { Percent = false, ChartRow = false }, saved.Printers[0].Camera.MiniStrip);
        Assert.Equal(NoRemaining, saved.Printers[1].Camera.MiniStrip);
    }

    [Fact]
    public async Task EveryCheckBoxSwitchesItsOwnItem()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        PrinterEditorViewModel printer = Select(vm).Printer;

        (Action<bool> set, Func<CameraMiniStripSettings, bool> get)[] items =
        [
            (v => printer.MiniStripProgressBar = v, static s => s.ProgressBar),
            (v => printer.MiniStripPercent = v, static s => s.Percent),
            (v => printer.MiniStripRemaining = v, static s => s.Remaining),
            (v => printer.MiniStripFinish = v, static s => s.Finish),
            (v => printer.MiniStripCooldown = v, static s => s.Cooldown),
            (v => printer.MiniStripNameRow = v, static s => s.NameRow),
            (v => printer.MiniStripChartRow = v, static s => s.ChartRow),
        ];
        foreach ((Action<bool> set, Func<CameraMiniStripSettings, bool> get) in items)
        {
            set(false);
            Assert.False(get(printer.MiniStrip));
            Assert.Equal(6, CountOn(printer.MiniStrip));
            set(true);
            Assert.Equal(CameraMiniStripSettings.Default, printer.MiniStrip);
        }

        // Undone changes are no change.
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task CancelDropsTheEditedCopy()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        bool closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        Select(vm).Printer.MiniStripFinish = false;

        vm.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.Empty(_store.Saves);
        vm.Dispose();
        SettingsViewModel again = await LoadAsync(Printer("a", "Alpha"));
        Assert.Same(CameraMiniStripSettings.Default, again.Printers[0].MiniStrip);
    }

    [Fact]
    public async Task PanelFollowsThePrinterSelectedAboveTheListAndKeepsTheOtherEdits()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", strip: NoRemaining), Printer("b", "Beta"));
        MiniStripSettingsViewModel first = Select(vm);
        first.Printer.MiniStripCooldown = false;

        vm.SelectedPrinter = vm.Printers[1];

        MiniStripSettingsViewModel second = Assert.IsType<MiniStripSettingsViewModel>(vm.MiniStripPanel);
        Assert.NotSame(first, second);
        Assert.Same(vm.Printers[1], second.Printer);
        Assert.True(second.Printer.MiniStripRemaining);
        Assert.True(second.Printer.MiniStripCooldown);
        Assert.True(vm.IsMiniStripSelected);

        vm.SelectedPrinter = vm.Printers[0];

        Assert.Equal(NoRemaining with { Cooldown = false }, vm.MiniStripPanel!.Printer.MiniStrip);
    }

    [Fact]
    public async Task CameraOffDisablesTheCheckBoxesButKeepsAndSavesTheValues()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", camera: false, strip: AllOff), Printer("b", "Beta"));
        MiniStripSettingsViewModel panel = Select(vm);

        Assert.False(panel.CanEdit);
        Assert.Equal(AllOff, panel.Printer.MiniStrip);

        List<string?> changes = [];
        panel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        panel.Printer.CameraEnabled = true;
        Assert.True(panel.CanEdit);
        Assert.Contains(string.Empty, changes);
        panel.Printer.CameraEnabled = false;
        vm.Printers[1].MiniStripPercent = false;

        AppSettings saved = await SaveAsync(vm);
        Assert.False(saved.Printers[0].Camera.Enabled);
        Assert.Equal(AllOff, saved.Printers[0].Camera.MiniStrip);
    }

    // ---- Restore defaults, Use layout for all printers (Q10 A) ----

    [Fact]
    public async Task RestoreDefaultsAlsoResetsTheStripOfTheSelectedPrinterOnly()
    {
        CameraOverlayLayout custom = CameraOverlayLayout.Default.WithVisible(OverlayWidgetIds.Times, visible: false);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", strip: AllOff, layout: custom), Printer("b", "Beta", strip: NoRemaining));

        vm.ResetLayoutCommand.Execute(null);

        Assert.Equal(CameraMiniStripSettings.Default, vm.Printers[0].MiniStrip);
        Assert.Equal(NoRemaining, vm.Printers[1].MiniStrip);
        AppSettings saved = await SaveAsync(vm);
        Assert.Equal(CameraMiniStripSettings.Default, saved.Printers[0].Camera.MiniStrip);
        Assert.Equal(CameraOverlayLayout.Default, saved.Printers[0].Camera.Overlay);
        Assert.Equal(NoRemaining, saved.Printers[1].Camera.MiniStrip);
    }

    [Fact]
    public async Task UseLayoutForAllPrintersAlsoCopiesTheStrip()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", strip: NoRemaining), Printer("b", "Beta", camera: false), Printer("c", "Gamma", strip: AllOff));
        vm.SelectedPrinter = vm.Printers[0];

        vm.ApplyLayoutToAllCommand.Execute(null);

        Assert.All(vm.Printers, static printer => Assert.Equal(NoRemaining, printer.MiniStrip));
        AppSettings saved = await SaveAsync(vm);
        Assert.All(saved.Printers, static printer => Assert.Equal(NoRemaining, printer.Camera.MiniStrip));
        Assert.False(saved.Printers[1].Camera.Enabled);
    }

    [Fact]
    public void ResetHelpMentionsTheMiniPreviewInEveryLanguage()
    {
        // Q2 (T-110): the tooltip of "Restore defaults" names the mini preview; the user-approved Czech wording.
        using (new CultureScope("cs-CZ"))
        {
            Assert.Equal("Vrátí viditelnost, polohu, pořadí, velikost a zvýraznění widgetů a obsah malého náhledu této tiskárny na výchozí.", Strings.SettingsWidgetsResetHelp);
            Assert.Equal("Řádek rozbalíte v malém náhledu tlačítkem „Název tisku v pásu“.", Strings.SettingsMiniStripNameRowHelp);
            Assert.Equal("Řádek rozbalíte v malém náhledu tlačítkem „Graf teplot v pásu“.", Strings.SettingsMiniStripChartRowHelp);
        }

        // The name of the item, declined where the language needs it.
        Dictionary<string, string> declined = new(StringComparer.Ordinal) { ["cs"] = "malého náhledu", ["sk"] = "malého náhľadu", ["pl"] = "małego podglądu" };
        foreach (string language in AppLanguages.Supported)
        {
            using CultureScope culture = new(language);
            string name = declined.GetValueOrDefault(language) ?? Strings.SettingsCameraWindowMini;
            Assert.Contains(name, Strings.SettingsWidgetsResetHelp, StringComparison.OrdinalIgnoreCase);
            // Q-D1 A (ui-designer review 2026-10-30): the help names the toggle of the strip by its own name, no symbol.
            Assert.Contains(Strings.CameraMiniToggleName, Strings.SettingsMiniStripNameRowHelp, StringComparison.Ordinal);
            Assert.Contains(Strings.CameraMiniToggleChart, Strings.SettingsMiniStripChartRowHelp, StringComparison.Ordinal);
            Assert.DoesNotContain("⬡", Strings.SettingsMiniStripNameRowHelp, StringComparison.Ordinal);
            Assert.DoesNotContain("📈", Strings.SettingsMiniStripChartRowHelp, StringComparison.Ordinal);
            Assert.InRange(Strings.SettingsMiniStripHint.Length, 1, 60);
        }
    }

    // ---- The remembered camera window state (Q9 A) ----

    [Fact]
    public async Task ForgettingTheRememberedWindowKeepsTheStrip()
    {
        _states.Set("a", new CameraWindowState(CameraWindowMode.Mini, null, new CameraWindowPlacement(3, 4, 400, 225), new CameraMiniRows(Name: true, Chart: true)));
        PrinterDefinition printer = Printer("a", "Alpha", strip: NoRemaining with { ChartRow = false });
        SettingsViewModel vm = await LoadAsync(printer with { Camera = printer.Camera with { Window = new CameraWindowDefaults { TopmostMini = false } } });

        vm.Printers[0].ResetCameraWindowCommand.Execute(null);
        AppSettings saved = await SaveAsync(vm);

        Assert.Equal(1, _states.GetRemoveCount("a"));
        Assert.Equal(NoRemaining with { ChartRow = false }, saved.Printers[0].Camera.MiniStrip);
    }

    [Fact]
    public async Task SwitchingARowOffInTheSettingsKeepsItsRememberedExpansion()
    {
        CameraWindowState remembered = new(CameraWindowMode.Mini, null, null, new CameraMiniRows(Name: true, Chart: true));
        _states.Set("a", remembered);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));

        Select(vm).Printer.MiniStripChartRow = false;
        await SaveAsync(vm);

        Assert.Equal(0, _states.GetRemoveCount("a"));
        Assert.Equal(remembered, _states.Get("a"));
    }

    private static int CountOn(CameraMiniStripSettings strip) =>
        new[] { strip.ProgressBar, strip.Percent, strip.Remaining, strip.Finish, strip.Cooldown, strip.NameRow, strip.ChartRow }.Count(static on => on);
}
