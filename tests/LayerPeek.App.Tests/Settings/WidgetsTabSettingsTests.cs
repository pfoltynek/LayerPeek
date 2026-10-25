using System.ComponentModel;
using System.Globalization;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Composition;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Settings.Widgets;
using LayerPeek.App.Tests.Cameras.Overlay;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Events;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The Widgets tab of the settings window (T-092): the widget list and its visibility, the panels of the widgets (the
/// contract extension of T-081, the Cool-down panel with the threshold of T-080), "Restore defaults", "Use layout for all
/// printers", the card background opacity, "Edit layout in camera window…" and the layout saved by a camera window.
/// </summary>
public sealed class WidgetsTabSettingsTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private readonly FakeLayoutModeLauncher _launcher = new();
    private readonly FakeLayoutAnnouncer _announcer = new();
    private readonly ManualDispatcher _dispatcher = new();
    private SettingsViewModel? _viewModel;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
    }

    private static PrinterDefinition Printer(string id, string name, bool camera = true, CameraOverlayLayout? layout = null, int threshold = 50) =>
        LinkPrinter(id, name, $"192.168.1.{20 + id.Length}") with
        {
            Camera = new CameraSettings(camera, StreamUri) { Overlay = layout ?? CameraOverlayLayout.Default },
            CooldownThresholdCelsius = threshold,
        };

    private async Task<SettingsViewModel> LoadAsync(OverlayWidgetRegistry? widgets, params PrinterDefinition[] printers)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = printers }, SettingsLoadStatus.Loaded, [], Ids());
        Queue<string> ids = new(["n1", "n2"]);
        SettingsViewModel vm = new(
            _store,
            new FakeConnectionTester(),
            Supported,
            ids.Dequeue,
            cameraTester: new FakeCameraTester(),
            widgets: widgets ?? OverlayWidgets.CreateRegistry(),
            layoutModeLauncher: _launcher,
            layoutWriter: _announcer,
            uiDispatcher: _dispatcher);
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }

    private Task<SettingsViewModel> LoadAsync(params PrinterDefinition[] printers) => LoadAsync(null, printers);

    private async Task<AppSettings> SaveAsync(SettingsViewModel vm)
    {
        await vm.SaveCommand.ExecuteAsync(null);
        return Assert.Single(_store.Saves).Updated;
    }

    private static WidgetItemViewModel Widget(SettingsViewModel vm, string id) => vm.Widgets.Single(widget => widget.Id == id);

    // ---- Contract (T-081 extended by T-092, design 6) ----

    [Fact]
    public void DefinitionWithoutTheNewMembersBuildsAndBehavesAsBefore()
    {
        OverlayWidgetDefinition plain = TestOverlayWidgets.Definition("test-a", priority: 7);
        IOverlayWidgetDefinition asInterface = plain;
        IOverlayWidgetDefinition legacy = new LegacyDefinition();

        Assert.Null(asInterface.DescriptionKey);
        Assert.Null(asInterface.IconKey);
        Assert.Null(asInterface.Settings);
        Assert.Null(legacy.DescriptionKey);
        Assert.Null(legacy.IconKey);
        Assert.Null(legacy.Settings);
        Assert.Equal(7, plain.Priority);
        OverlayWidgetRegistry registry = new([plain, legacy]);
        Assert.Equal(["test-a", "legacy"], registry.Definitions.Select(static definition => definition.Id));
    }

    [Fact]
    public void RegisteredWidgetsHaveADescriptionAndAnIconInEveryLanguage()
    {
        OverlayWidgetRegistry registry = OverlayWidgets.CreateRegistry();

        Assert.Equal(
            [OverlayWidgetIds.Status, OverlayWidgetIds.Times, OverlayWidgetIds.Temperatures, OverlayWidgetIds.PrintName, OverlayWidgetIds.Cooldown],
            registry.Definitions.Select(static definition => definition.Id));
        foreach (IOverlayWidgetDefinition definition in registry.Definitions)
        {
            string description = Assert.IsType<string>(definition.DescriptionKey);
            Assert.StartsWith("Icon", Assert.IsType<string>(definition.IconKey), StringComparison.Ordinal);
            foreach (string language in AppLanguages.Supported)
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(language);
                string? text = Strings.ResourceManager.GetString(description, culture);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{description} ({language})");
                // Design 4.1 / 9: one sentence, at most 110 characters (2 lines in 508 DIP).
                Assert.True(text!.Length <= 110, $"{description} ({language}) has {text.Length} characters.");
                Assert.False(string.IsNullOrWhiteSpace(Strings.ResourceManager.GetString(definition.NameKey, culture)), $"{definition.NameKey} ({language})");
            }
        }
    }

    [Fact]
    public void OnlyTheCooldownWidgetHasASettingsPanel()
    {
        // T-092 builds the Cool-down panel; T-088 registers it with the Cool-down widget.
        foreach (IOverlayWidgetDefinition definition in OverlayWidgets.CreateRegistry().Definitions)
        {
            if (definition.Id == OverlayWidgetIds.Cooldown)
            {
                Assert.Same(CooldownWidgetSettings.Instance, definition.Settings);
            }
            else
            {
                Assert.Null(definition.Settings);
            }
        }
    }

    [Theory]
    [InlineData("IconStatus16Regular", 16)]
    [InlineData("IconWeatherSnowflake20Regular", 20)]
    [InlineData("IconLineHorizontal1_16Filled", 16)]
    [InlineData("IconWeatherSnowflake24Regular", 24)]
    [InlineData("IconWithoutSize", 16)]
    public void IconCanvasSizeComesFromTheKey(string key, double size) => Assert.Equal(size, WidgetIcon.CanvasSizeOf(key));

    // ---- The list ----

    [Fact]
    public async Task ListShowsTheRegisteredWidgetsInRegistrationOrderAndSelectsTheFirst()
    {
        CameraOverlayLayout layout = new([.. CameraOverlayLayout.Default.Widgets, new OverlayWidgetPlacement("future-widget", OverlayAnchor.TopRight)]);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", layout: layout.WithVisible(OverlayWidgetIds.Times, visible: false)));

        Assert.Equal(["status", "times", "temperatures", "print-name", "cooldown"], vm.Widgets.Select(static widget => widget.Id));
        Assert.Equal(["Status", "Times", "Temperatures", "Print name", "Cool-down"], vm.Widgets.Select(static widget => widget.Name));
        Assert.Equal([true, false, true, true, true], vm.Widgets.Select(static widget => widget.IsShown));
        Assert.Same(vm.Widgets[0], vm.SelectedWidget);
        Assert.Equal("Times, hidden", Widget(vm, "times").AutomationName);
        Assert.Equal("Status, shown", Widget(vm, "status").AutomationName);
        Assert.Equal("Show the Times widget", Widget(vm, "times").ShowName);
        Assert.Equal("Printer name, state and print progress in percent.", vm.Widgets[0].Description);
        Assert.False(vm.Widgets[0].HasSettings);
        Assert.True(Widget(vm, "cooldown").HasSettings);
        Assert.True(vm.ShowsWidgets);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task VisibilityIsSavedIntoThePrinterKeepingUnknownWidgets()
    {
        OverlayWidgetPlacement future = new("future-widget", OverlayAnchor.TopRight, emphasized: true);
        CameraOverlayLayout layout = new([.. CameraOverlayLayout.Default.Widgets, future]);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", layout: layout), Printer("b", "Beta"));
        WidgetItemViewModel temperatures = Widget(vm, "temperatures");
        List<string?> changed = [];
        temperatures.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        temperatures.IsShown = false;

        Assert.False(temperatures.IsShown);
        Assert.Contains(string.Empty, changed);
        Assert.Same(temperatures, vm.SelectedWidget);
        Assert.True(vm.IsDirty);
        AppSettings saved = await SaveAsync(vm);
        CameraOverlayLayout expected = layout.WithVisible(OverlayWidgetIds.Temperatures, visible: false);
        Assert.Equal(expected, saved.Printers[0].Camera.Overlay);
        Assert.Equal(future, saved.Printers[0].Camera.Overlay.Find("future-widget"));
        // The other printer and the other values of this one did not change.
        Assert.Equal(_store.Saves[0].Original.Printers[1], saved.Printers[1]);
        Assert.Equal(_store.Saves[0].Original.Printers[0] with { Camera = _store.Saves[0].Original.Printers[0].Camera with { Overlay = expected } }, saved.Printers[0]);
    }

    [Fact]
    public async Task UndoneVisibilityIsNoChange()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));

        Widget(vm, "times").IsShown = false;
        Widget(vm, "times").IsShown = true;

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task RegisteredWidgetMissingFromTheLayoutIsHiddenAndAddedAtItsDefaultPlacementWhenShown()
    {
        CameraOverlayLayout withoutCooldown = new(CameraOverlayLayout.Default.Widgets.Where(static widget => widget.WidgetId != OverlayWidgetIds.Cooldown));
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", layout: withoutCooldown));
        WidgetItemViewModel cooldown = Widget(vm, "cooldown");
        Assert.False(cooldown.IsShown);

        cooldown.IsShown = false;
        Assert.False(vm.IsDirty);
        cooldown.IsShown = true;

        Assert.Equal(CameraOverlayLayout.DefaultPlacement(OverlayWidgetIds.Cooldown), vm.Printers[0].OverlayLayout.Find(OverlayWidgetIds.Cooldown));
        Assert.True(cooldown.IsShown);
    }

    [Fact]
    public async Task VisibilityCannotChangeWhileTheCameraIsOffButTheValuesStay()
    {
        CameraOverlayLayout layout = CameraOverlayLayout.Default.WithVisible(OverlayWidgetIds.Status, visible: false);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", camera: false, layout: layout));
        WidgetItemViewModel status = Widget(vm, "status");

        Assert.True(vm.ShowsWidgetsCameraOff);
        Assert.False(status.CanChangeVisibility);
        status.IsShown = true;
        Assert.False(vm.ToggleSelectedWidget());

        Assert.False(status.IsShown);
        Assert.False(vm.IsDirty);
        // Selecting a widget, its panel and the opacity stay usable.
        vm.SelectedWidget = Widget(vm, "cooldown");
        Assert.IsType<CooldownWidgetSettingsViewModel>(vm.SelectedWidgetPanel);

        vm.Printers[0].CameraEnabled = true;

        Assert.False(vm.ShowsWidgetsCameraOff);
        Assert.True(status.CanChangeVisibility);
        Assert.False(status.IsShown);
    }

    [Fact]
    public async Task SpaceSwitchesTheSelectedWidget()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        vm.SelectedWidget = Widget(vm, "print-name");

        Assert.True(vm.ToggleSelectedWidget());
        Assert.False(Widget(vm, "print-name").IsShown);
        Assert.True(vm.ToggleSelectedWidget());
        Assert.True(Widget(vm, "print-name").IsShown);
    }

    [Fact]
    public async Task SelectionStaysForAnotherPrinterAndListShowsItsLayout()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"), Printer("b", "Beta", layout: CameraOverlayLayout.Default.WithVisible(OverlayWidgetIds.Times, visible: false)));
        vm.SelectedWidget = Widget(vm, "times");
        Assert.True(Widget(vm, "times").IsShown);

        vm.SelectedPrinter = vm.Printers[1];

        Assert.Same(Widget(vm, "times"), vm.SelectedWidget);
        Assert.False(Widget(vm, "times").IsShown);
        vm.SelectedWidget = null;
        Assert.Same(Widget(vm, "times"), vm.SelectedWidget);
    }

    [Fact]
    public async Task NoPrinterHidesTheListAndDisablesTheLayoutActions()
    {
        SettingsViewModel vm = await LoadAsync();

        Assert.False(vm.ShowsWidgets);
        Assert.True(vm.HasNoPrinters);
        Assert.False(vm.ShowsNoWidgetSettings);
        Assert.Null(vm.SelectedWidgetPanel);
        Assert.False(vm.EditLayoutCommand.CanExecute(null));
        Assert.False(vm.ResetLayoutCommand.CanExecute(null));
        Assert.False(vm.ApplyLayoutToAllCommand.CanExecute(null));
        Assert.False(Widget(vm, "status").IsShown);
        Assert.False(Widget(vm, "status").CanChangeVisibility);
        Assert.False(vm.ToggleSelectedWidget());
        // The opacity is global.
        vm.OverlayOpacityPercent = 50;
        Assert.Equal(50, (await SaveAsync(vm)).CameraOverlayOpacityPercent);
    }

    // ---- Panels ----

    [Fact]
    public async Task PanelIsCreatedOnlyForAWidgetThatDeclaresOneAndDisposedWhenTheSelectionChanges()
    {
        CountingFactory factory = new();
        OverlayWidgetRegistry registry = new([
            TestOverlayWidgets.Definition("test-a"),
            new OverlayWidgetDefinition("test-b", "CameraOverlayWidgetTimes", 0, TestOverlayWidgets.Template, static _ => new TestOverlayWidget()) { Settings = factory },
        ]);
        SettingsViewModel vm = await LoadAsync(registry, Printer("a", "Alpha"), Printer("b", "Beta"));
        Assert.Null(vm.SelectedWidgetPanel);
        Assert.True(vm.ShowsNoWidgetSettings);
        Assert.Empty(factory.Created);

        vm.SelectedWidget = vm.Widgets[1];
        CountingPanel first = Assert.Single(factory.Created);
        Assert.Same(first, vm.SelectedWidgetPanel);
        Assert.Same(vm.Printers[0], first.Printer);
        Assert.False(vm.ShowsNoWidgetSettings);

        vm.SelectedPrinter = vm.Printers[1];
        Assert.True(first.IsDisposed);
        CountingPanel second = factory.Created[1];
        Assert.Same(vm.Printers[1], second.Printer);
        Assert.Same(second, vm.SelectedWidgetPanel);

        vm.SelectedWidget = vm.Widgets[0];
        Assert.True(second.IsDisposed);
        Assert.Null(vm.SelectedWidgetPanel);
        Assert.Equal(2, factory.Created.Count);

        vm.SelectedWidget = vm.Widgets[1];
        vm.Dispose();
        Assert.True(factory.Created[2].IsDisposed);
    }

    [Fact]
    public async Task CooldownPanelShowsTheNotificationStateAsEdited()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        vm.SelectedWidget = Widget(vm, "cooldown");
        CooldownWidgetSettingsViewModel panel = Assert.IsType<CooldownWidgetSettingsViewModel>(vm.SelectedWidgetPanel);
        List<string?> changed = [];
        panel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.True(vm.NotificationsEnabled);
        Assert.Equal("“Printer cooled down” notification: off", panel.NotifyStateText);
        vm.NotifyCooledDown = true;
        Assert.Equal("“Printer cooled down” notification: on", panel.NotifyStateText);
        vm.NotificationsEnabled = false;
        Assert.Equal("Notifications are turned off.", panel.NotifyStateText);
        Assert.Equal(2, changed.Count(static name => name == nameof(CooldownWidgetSettingsViewModel.NotifyStateText)));
        Assert.Same(vm.Printers[0], panel.Printer);

        vm.SelectedWidget = Widget(vm, "status");
        Assert.True(panel.IsDisposed);
        changed.Clear();
        vm.NotificationsEnabled = true;
        Assert.Empty(changed);
    }

    [Fact]
    public async Task LinkOfTheCooldownPanelOpensTheGeneralTab()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        vm.SelectedTabIndex = SettingsViewModel.WidgetsTab;
        vm.SelectedWidget = Widget(vm, "cooldown");
        List<PrintEventKind> requested = [];
        vm.NotificationSettingRequested += (_, kind) => requested.Add(kind);

        ((CooldownWidgetSettingsViewModel)vm.SelectedWidgetPanel!).ShowNotificationSettingCommand.Execute(null);

        Assert.Equal(SettingsViewModel.GeneralTab, vm.SelectedTabIndex);
        Assert.Equal([PrintEventKind.CooledDown], requested);
    }

    [Fact]
    public async Task CooldownPanelTextsFollowALanguageSwitch()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        vm.SelectedWidget = Widget(vm, "cooldown");
        CooldownWidgetSettingsViewModel panel = (CooldownWidgetSettingsViewModel)vm.SelectedWidgetPanel!;

        using (new UiCultureScope("cs"))
        {
            vm.RefreshTexts();

            Assert.Equal("Upozornění „Tiskárna vychladla“: vypnuté", panel.NotifyStateText);
            Assert.Equal("Vychladnutí", Widget(vm, "cooldown").Name);
            Assert.Equal("Vychladnutí, zobrazený", Widget(vm, "cooldown").AutomationName);
            Assert.Equal("Widgety", vm.WidgetsTabName);
            Assert.Equal("Po skončení tisku ukáže chladnutí trysky a kdy je tiskárnu bezpečné vypnout.", Widget(vm, "cooldown").Description);
        }
    }

    // ---- Threshold in the Cool-down panel (taken over from T-080) ----

    [Theory]
    [InlineData(30)]
    [InlineData(100)]
    public async Task ThresholdAtTheBoundsIsSaved(int celsius)
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        vm.SelectedWidget = Widget(vm, "cooldown");
        Assert.Equal(50m, vm.SelectedWidgetPanel!.Printer.CooldownThresholdCelsius);

        vm.SelectedWidgetPanel.Printer.CooldownThresholdCelsius = celsius;

        Assert.False(Widget(vm, "cooldown").ShowsErrorMarker);
        Assert.Equal(celsius, (await SaveAsync(vm)).Printers[0].CooldownThresholdCelsius);
    }

    [Theory]
    [InlineData("29")]
    [InlineData("101")]
    [InlineData("64.5")]
    public async Task InvalidThresholdBlocksSavingAndSelectsTheWidgetsTabPrinterAndWidget(string value)
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"), Printer("b", "Beta"), Printer("c", "Gamma"));
        PrinterEditorViewModel beta = vm.Printers[1];
        beta.CooldownThresholdCelsius = decimal.Parse(value, CultureInfo.InvariantCulture);
        vm.SelectedPrinter = vm.Printers[2];
        vm.SelectedWidget = Widget(vm, "status");
        vm.SelectedTabIndex = SettingsViewModel.GeneralTab;
        Assert.True(vm.ShowsWidgetsErrorMarker);
        Assert.False(Widget(vm, "cooldown").ShowsErrorMarker);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.True(vm.StatusIsError);
        Assert.Equal(SettingsViewModel.WidgetsTab, vm.SelectedTabIndex);
        Assert.Same(beta, vm.SelectedPrinter);
        Assert.Same(Widget(vm, "cooldown"), vm.SelectedWidget);
        Assert.True(Widget(vm, "cooldown").ShowsErrorMarker);
        Assert.False(Widget(vm, "status").ShowsErrorMarker);
        Assert.True(beta.ShowsErrorMarker);
        Assert.Equal("Enter a whole number from 30 to 100.", ((CooldownWidgetSettingsViewModel)vm.SelectedWidgetPanel!).Printer.CooldownThresholdError);

        beta.CooldownThresholdCelsius = 60;

        Assert.False(Widget(vm, "cooldown").ShowsErrorMarker);
        Assert.False(vm.ShowsWidgetsErrorMarker);
        Assert.False(beta.ShowsErrorMarker);
    }

    [Fact]
    public async Task ThresholdErrorWithoutARegisteredPanelStillOpensTheWidgetsTab()
    {
        // Without the Cool-down widget (e.g. a host that does not register it) nothing in the window makes the threshold
        // invalid; if it were, the save would still stop on the Widgets tab.
        OverlayWidgetRegistry withoutCooldown = new([.. OverlayWidgets.CreateRegistry().Definitions.Where(static definition => definition.Id != OverlayWidgetIds.Cooldown)]);
        SettingsViewModel vm = await LoadAsync(withoutCooldown, Printer("a", "Alpha"));
        vm.Printers[0].CooldownThresholdCelsius = 5;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(SettingsViewModel.WidgetsTab, vm.SelectedTabIndex);
        Assert.Same(vm.Widgets[0], vm.SelectedWidget);
    }

    [Fact]
    public async Task ThresholdOfThePanelIsCopiedToAllPrinters()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"), Printer("b", "Beta", threshold: 80), Printer("c", "Gamma", threshold: 90));
        vm.SelectedWidget = Widget(vm, "cooldown");
        PrinterEditorViewModel alpha = vm.SelectedWidgetPanel!.Printer;
        alpha.CooldownThresholdCelsius = 44;

        alpha.ApplyCooldownThresholdToAllCommand.Execute(null);

        Assert.Equal([44m, 44m, 44m], vm.Printers.Select(static printer => printer.CooldownThresholdCelsius));
        Assert.Equal(Strings.SettingsCooldownAppliedAll, vm.StatusText);
        AppSettings saved = await SaveAsync(vm);
        Assert.All(saved.Printers, static printer => Assert.Equal(44, printer.CooldownThresholdCelsius));
        Assert.All(saved.Printers, static printer => Assert.Same(CameraOverlayLayout.Default, printer.Camera.Overlay));
    }

    // ---- Restore defaults, Use layout for all printers ----

    [Fact]
    public async Task RestoreDefaultsResetsOnlyTheLayoutOfTheSelectedPrinter()
    {
        CameraOverlayLayout custom = CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopRight)
            .WithVisible(OverlayWidgetIds.Times, visible: false)
            .WithSize(OverlayWidgetIds.Status, OverlayWidgetSize.Large);
        CameraOverlayLayout withFuture = new([.. custom.Widgets, new OverlayWidgetPlacement("future-widget", OverlayAnchor.TopCenter)]);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", layout: withFuture, threshold: 70), Printer("b", "Beta", layout: custom));
        vm.OverlayOpacityPercent = 40;

        vm.ResetLayoutCommand.Execute(null);

        Assert.Equal(withFuture.ResetToDefault(), vm.Printers[0].OverlayLayout);
        Assert.NotNull(vm.Printers[0].OverlayLayout.Find("future-widget"));
        Assert.Equal(custom, vm.Printers[1].OverlayLayout);
        Assert.Equal(70m, vm.Printers[0].CooldownThresholdCelsius);
        Assert.Equal(40, vm.OverlayOpacityPercent);
        Assert.Equal("The default layout was restored. It applies after saving.", vm.StatusText);
        Assert.False(vm.StatusIsError);
        Assert.Empty(_store.Saves);
        AppSettings saved = await SaveAsync(vm);
        Assert.Equal(withFuture.ResetToDefault(), saved.Printers[0].Camera.Overlay);
        Assert.Equal(custom, saved.Printers[1].Camera.Overlay);
    }

    [Fact]
    public async Task UseLayoutForAllPrintersCopiesTheWholeLayoutAndNothingElse()
    {
        CameraOverlayLayout custom = CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopRight)
            .WithVisible(OverlayWidgetIds.Times, visible: false)
            .WithEmphasized(OverlayWidgetIds.Temperatures, emphasized: true);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", layout: custom), Printer("b", "Beta", camera: false, threshold: 80), Printer("c", "Gamma"));
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel added = vm.SelectedPrinter!;
        vm.SelectedPrinter = vm.Printers[0];
        Assert.True(vm.ApplyLayoutToAllCommand.CanExecute(null));
        Assert.Null(vm.ApplyLayoutToAllUnavailableReason);

        vm.ApplyLayoutToAllCommand.Execute(null);

        Assert.All(vm.Printers, printer => Assert.Equal(custom, printer.OverlayLayout));
        Assert.Same(custom, added.OverlayLayout);
        Assert.Equal("The layout was copied to the other printers. It applies after saving.", vm.StatusText);
        // The new printer (without name and address) would block saving; remove it.
        vm.SelectedPrinter = added;
        vm.RemovePrinterCommand.Execute(null);
        AppSettings saved = await SaveAsync(vm);
        AppSettings original = _store.Saves[0].Original;
        Assert.Equal(original.WithCameraOverlayLayoutForAllPrinters(custom).Printers, saved.Printers);
        Assert.Equal(80, saved.Printers[1].CooldownThresholdCelsius);
        Assert.False(saved.Printers[1].Camera.Enabled);
    }

    [Fact]
    public async Task UseLayoutForAllPrintersIsDisabledWithASinglePrinterAndSaysWhy()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));

        Assert.False(vm.ApplyLayoutToAllCommand.CanExecute(null));
        Assert.Equal("Only one printer is set up.", vm.ApplyLayoutToAllUnavailableReason);
        Assert.True(vm.ResetLayoutCommand.CanExecute(null));

        vm.AddPrinterCommand.Execute(null);

        Assert.True(vm.ApplyLayoutToAllCommand.CanExecute(null));
        Assert.Null(vm.ApplyLayoutToAllUnavailableReason);
    }

    [Fact]
    public async Task LayoutActionsStayEnabledWhileTheCameraIsOff()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", camera: false), Printer("b", "Beta"));

        Assert.True(vm.ResetLayoutCommand.CanExecute(null));
        Assert.True(vm.ApplyLayoutToAllCommand.CanExecute(null));
        Assert.False(vm.EditLayoutCommand.CanExecute(null));
    }

    // ---- Opacity ----

    [Fact]
    public async Task OpacityIsGlobalWithinItsBoundsAndSaved()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        Assert.Equal(72, vm.OverlayOpacityPercent);
        Assert.Equal("72%", vm.OverlayOpacityText);
        Assert.Equal(30, SettingsViewModel.MinOverlayOpacityPercent);
        Assert.Equal(95, SettingsViewModel.MaxOverlayOpacityPercent);

        vm.OverlayOpacityPercent = 10;
        Assert.Equal(30, vm.OverlayOpacityPercent);
        vm.OverlayOpacityPercent = 120;
        Assert.Equal(95, vm.OverlayOpacityPercent);
        vm.OverlayOpacityPercent = double.NaN;
        Assert.Equal(95, vm.OverlayOpacityPercent);
        vm.OverlayOpacityPercent = 64.6;
        Assert.Equal(65, vm.OverlayOpacityPercent);
        Assert.True(vm.IsDirty);

        Assert.Equal(65, (await SaveAsync(vm)).CameraOverlayOpacityPercent);
    }

    [Fact]
    public async Task SavedOpacityIsLoadedAndFormattedForTheLanguage()
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = [Printer("a", "Alpha")], CameraOverlayOpacityPercent = 30 }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(_store, new FakeConnectionTester(), Supported, cameraTester: new FakeCameraTester());
        _viewModel = vm;
        await vm.LoadAsync();

        Assert.Equal(30, vm.OverlayOpacityPercent);
        Assert.False(vm.IsDirty);
        using (new CultureScope("cs"))
        {
            vm.RefreshTexts();
            // The percent format of the culture: a (narrow) non-breaking space in Czech.
            Assert.Matches(@"^30\s%$", vm.OverlayOpacityText);
        }

        vm.OverlayOpacityPercent = 95;
        Assert.Equal(95, (await SaveAsync(vm)).CameraOverlayOpacityPercent);
    }

    // ---- Edit layout in camera window… ----

    [Fact]
    public async Task EditLayoutStartsTheLayoutModeWithTheLayoutAsEdited()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"), Printer("b", "Beta"));
        vm.SelectedPrinter = vm.Printers[1];
        Widget(vm, "print-name").IsShown = false;
        Assert.True(vm.EditLayoutCommand.CanExecute(null));
        Assert.Null(vm.EditLayoutUnavailableReason);
        Assert.Equal(Strings.SettingsWidgetsEditLayoutHelp, vm.EditLayoutToolTip);

        vm.EditLayoutCommand.Execute(null);

        (string id, CameraOverlayLayout? layout) = Assert.Single(_launcher.Starts);
        Assert.Equal("b", id);
        Assert.Equal(CameraOverlayLayout.Default.WithVisible(OverlayWidgetIds.PrintName, visible: false), layout);
        // The window stays as it is: nothing saved, the edit still unsaved.
        Assert.Empty(_store.Saves);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task EditLayoutNeedsASavedPrinterWithTheCameraOnInTheSavedAndEditedSettings()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", camera: false));
        PrinterEditorViewModel alpha = vm.Printers[0];

        Assert.False(vm.EditLayoutCommand.CanExecute(null));
        Assert.Equal("Turn on this printer's camera and save the settings.", vm.EditLayoutUnavailableReason);
        Assert.Equal(vm.EditLayoutUnavailableReason, vm.EditLayoutToolTip);

        alpha.CameraEnabled = true;
        Assert.False(vm.EditLayoutCommand.CanExecute(null));
        Assert.Equal("Save the settings first.", vm.EditLayoutUnavailableReason);

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.EditLayoutCommand.CanExecute(null));
        Assert.Null(vm.EditLayoutUnavailableReason);

        alpha.CameraEnabled = false;
        Assert.False(vm.EditLayoutCommand.CanExecute(null));
        vm.EditLayoutCommand.Execute(null);
        Assert.Empty(_launcher.Starts);
    }

    [Fact]
    public async Task EditLayoutIsDisabledForANewPrinterAndWithoutPrintersOrLauncher()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        vm.AddPrinterCommand.Execute(null);
        vm.SelectedPrinter!.CameraEnabled = true;

        Assert.False(vm.EditLayoutCommand.CanExecute(null));
        Assert.Equal("Save the settings first.", vm.EditLayoutUnavailableReason);

        vm.RemovePrinterCommand.Execute(null);
        vm.RemovePrinterCommand.Execute(null);
        Assert.Null(vm.SelectedPrinter);
        Assert.False(vm.EditLayoutCommand.CanExecute(null));
        Assert.Null(vm.EditLayoutUnavailableReason);

        _store.Data = new SettingsEditorData(new AppSettings { Printers = [Printer("a", "Alpha")] }, SettingsLoadStatus.Loaded, [], Ids());
        using SettingsViewModel withoutLauncher = new(_store, new FakeConnectionTester(), Supported, cameraTester: new FakeCameraTester(), widgets: OverlayWidgets.CreateRegistry());
        await withoutLauncher.LoadAsync();
        Assert.False(withoutLauncher.EditLayoutCommand.CanExecute(null));
    }

    // ---- Layout saved by the layout mode of a camera window (T-077 Q3) ----

    [Fact]
    public async Task SavedLayoutReplacesOnlyTheLayoutOnTheUiThreadAndKeepsOtherUnsavedChanges()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"), Printer("b", "Beta"));
        PrinterEditorViewModel alpha = vm.Printers[0];
        alpha.Name = "Alpha 2";
        alpha.CooldownThresholdCelsius = 41;
        Widget(vm, "status").IsShown = false;
        vm.OverlayOpacityPercent = 50;
        Assert.Equal(1, _announcer.Subscribers);
        CameraOverlayLayout fromCamera = CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.BottomRight);

        // The writer's thread: nothing changes until the UI thread runs the posted work.
        await Task.Run(() => _announcer.Announce("A", fromCamera), TestContext.Current.CancellationToken);
        Assert.Equal(1, _dispatcher.Pending);
        Assert.False(Widget(vm, "status").IsShown);
        _dispatcher.RunAll();

        Assert.Equal(fromCamera, alpha.OverlayLayout);
        Assert.True(Widget(vm, "status").IsShown);
        Assert.Equal("Alpha 2", alpha.Name);
        Assert.Equal(41m, alpha.CooldownThresholdCelsius);
        Assert.Equal(50, vm.OverlayOpacityPercent);
        Assert.True(vm.IsDirty);

        AppSettings saved = await SaveAsync(vm);
        Assert.Equal(fromCamera, saved.Printers[0].Camera.Overlay);
        Assert.Equal("Alpha 2", saved.Printers[0].Name);
        Assert.Equal(41, saved.Printers[0].CooldownThresholdCelsius);
        // The saved state knew the new layout already: it is no change of this window.
        Assert.Equal(fromCamera, _store.Saves[0].Original.Printers[0].Camera.Overlay);
        Assert.Same(CameraOverlayLayout.Default, saved.Printers[1].Camera.Overlay);
    }

    [Fact]
    public async Task SavedLayoutAloneIsNoUnsavedChange()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        Widget(vm, "times").IsShown = false;
        Assert.True(vm.IsDirty);

        _announcer.Announce("a", CameraOverlayLayout.Default.WithVisible(OverlayWidgetIds.Times, visible: false));
        _dispatcher.RunAll();

        Assert.False(vm.IsDirty);
        Assert.False(Widget(vm, "times").IsShown);
    }

    [Fact]
    public async Task NewRemovedAndUnknownPrintersIgnoreASavedLayout()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"), Printer("b", "Beta"));
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel added = vm.SelectedPrinter!;
        vm.SelectedPrinter = vm.Printers[1];
        vm.RemovePrinterCommand.Execute(null);
        CameraOverlayLayout fromCamera = CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopLeft);

        _announcer.Announce(added.Id, fromCamera);
        _announcer.Announce("b", fromCamera);
        _announcer.Announce("unknown", fromCamera);
        _dispatcher.RunAll();

        Assert.All(vm.Printers, static printer => Assert.Same(CameraOverlayLayout.Default, printer.OverlayLayout));
        vm.SelectedPrinter = added;
        vm.RemovePrinterCommand.Execute(null);
        AppSettings saved = await SaveAsync(vm);
        Assert.DoesNotContain(saved.Printers, static printer => printer.Id == "b");
        Assert.All(saved.Printers, static printer => Assert.Same(CameraOverlayLayout.Default, printer.Camera.Overlay));
    }

    [Fact]
    public async Task ClosedWindowStopsListeningToSavedLayouts()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        Assert.Equal(1, _announcer.ConfirmSubscribers);
        _announcer.Announce("a", CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopLeft));
        _announcer.Confirm("a", CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.BottomLeft));

        vm.Dispose();
        _dispatcher.RunAll();

        Assert.Equal(0, _announcer.Subscribers);
        Assert.Equal(0, _announcer.ConfirmSubscribers);
        Assert.Same(CameraOverlayLayout.Default, vm.Printers[0].OverlayLayout);
    }

    // ---- Layout confirmed by the layout mode without a write (T-092 follow-up, user decision 2026-10-25) ----

    [Fact]
    public async Task ConfirmedLayoutDropsOnlyTheUnsavedLayoutOfItsPrinterOnTheUiThread()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"), Printer("b", "Beta"));
        PrinterEditorViewModel alpha = vm.Printers[0];
        PrinterEditorViewModel beta = vm.Printers[1];
        vm.SelectedPrinter = alpha;
        Widget(vm, "status").IsShown = false;
        alpha.Name = "Alpha 2";
        vm.SelectedPrinter = beta;
        Widget(vm, "times").IsShown = false;
        CameraOverlayLayout betaUnsaved = beta.OverlayLayout;
        vm.SelectedPrinter = alpha;

        // "Done" in Alpha's camera window with the saved layout restored: nothing written, the layout confirmed.
        await Task.Run(() => _announcer.Confirm("A", CameraOverlayLayout.Default), TestContext.Current.CancellationToken);
        Assert.Equal(1, _dispatcher.Pending);
        Assert.False(Widget(vm, "status").IsShown);
        _dispatcher.RunAll();

        Assert.Equal(CameraOverlayLayout.Default, alpha.OverlayLayout);
        Assert.True(Widget(vm, "status").IsShown);
        Assert.Equal("Alpha 2", alpha.Name);
        Assert.Equal(betaUnsaved, beta.OverlayLayout);
        Assert.True(vm.IsDirty);

        // Only Beta's layout and Alpha's name are still unsaved.
        alpha.Name = "Alpha";
        vm.SelectedPrinter = beta;
        Widget(vm, "times").IsShown = true;
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task ConfirmedLayoutAloneLeavesNoUnsavedChangeAndALaterSaveKeepsIt()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        Widget(vm, "times").IsShown = false;
        Assert.True(vm.IsDirty);

        _announcer.Confirm("a", CameraOverlayLayout.Default);
        _dispatcher.RunAll();

        Assert.False(vm.IsDirty);
        Assert.True(Widget(vm, "times").IsShown);
        vm.Printers[0].Name = "Alpha 2";
        AppSettings saved = await SaveAsync(vm);
        Assert.Equal(CameraOverlayLayout.Default, saved.Printers[0].Camera.Overlay);
        Assert.Equal("Alpha 2", saved.Printers[0].Name);
    }

    [Fact]
    public async Task NewRemovedAndUnknownPrintersIgnoreAConfirmedLayout()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"), Printer("b", "Beta"));
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel added = vm.SelectedPrinter!;
        vm.SelectedPrinter = vm.Printers[1];
        vm.RemovePrinterCommand.Execute(null);
        CameraOverlayLayout confirmed = CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopLeft);

        _announcer.Confirm(added.Id, confirmed);
        _announcer.Confirm("b", confirmed);
        _announcer.Confirm("unknown", confirmed);
        _dispatcher.RunAll();

        Assert.All(vm.Printers, static printer => Assert.Same(CameraOverlayLayout.Default, printer.OverlayLayout));
    }

    // ---- Tab ----

    [Fact]
    public async Task WidgetsTabComesRightAfterThePrintersTab()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));

        Assert.Equal([0, 1, 2, 3], new[] { SettingsViewModel.PrintersTab, SettingsViewModel.WidgetsTab, SettingsViewModel.GeneralTab, SettingsViewModel.StartupUpdatesTab });
        Assert.True(vm.SelectAdjacentTab(forward: true));
        Assert.Equal(SettingsViewModel.WidgetsTab, vm.SelectedTabIndex);
        Assert.False(vm.SelectAdjacentPrinterTab(forward: true));
        Assert.Equal(3, SettingsViewModel.PrinterTabCount);
        Assert.Equal("Widgets", vm.WidgetsTabName);
    }

    private sealed class LegacyDefinition : IOverlayWidgetDefinition
    {
        public string Id => "legacy";

        public string NameKey => "CameraOverlayWidgetStatus";

        public IReadOnlyList<OverlayWidgetSize> SupportedSizes => [OverlayWidgetSize.Medium];

        public int Priority => 0;

        public Avalonia.Controls.Templates.IDataTemplate CardTemplate => TestOverlayWidgets.Template;

        public double MinWidth(OverlayWidgetSize size) => 0;

        public OverlayWidgetViewModel Create(OverlayWidgetContext context) => new TestOverlayWidget();
    }

    private sealed class CountingFactory : IOverlayWidgetSettingsFactory
    {
        public List<CountingPanel> Created { get; } = [];

        public OverlayWidgetSettingsViewModel Create(IOverlayWidgetSettingsHost host)
        {
            CountingPanel panel = new(host);
            Created.Add(panel);
            return panel;
        }

        public bool HasError(PrinterEditorViewModel printer) => false;
    }

    private sealed class CountingPanel(IOverlayWidgetSettingsHost host) : OverlayWidgetSettingsViewModel(host)
    {
        public int Hosted => Host.PrinterCount;
    }
}
