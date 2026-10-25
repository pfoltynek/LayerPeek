using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Cameras.FakeScreenCatalog;
using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>"Factory settings" and "Apply" of the Camera window sub-tab (T-040).</summary>
public sealed class CameraWindowActionsTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraWindowDefaults Custom = new()
    {
        RememberLastPlacement = false,
        StartMode = CameraWindowStartMode.FullScreen,
        Monitor = new CameraMonitorId("LG HDR 4K", 1920, 1080),
        Normal = new CameraWindowLayout(new CameraWindowSize(1280, 720), CameraWindowCorner.TopRight),
        Mini = new CameraWindowLayout(new CameraWindowSize(400, 225), CameraWindowCorner.TopLeft),
        TopmostNormal = true,
        TopmostMini = false,
    };

    private static readonly CameraOverlayLayout CustomOverlay = CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopRight).WithVisible(OverlayWidgetIds.Times, visible: false);

    private static readonly CameraWindowState Remembered = new(CameraWindowMode.Mini, new CameraWindowPlacement(1, 2, 700, 400), new CameraWindowPlacement(3, 4, 400, 225));

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private readonly MemoryCameraWindowStateStore _states = new();
    private readonly FakeCameraWindowPlacer _placer = new();
    private readonly FakeTimeProvider _time = new();
    private SettingsViewModel? _viewModel;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
    }

    private static PrinterDefinition Printer(string id, string name, bool enabled = true, CameraWindowDefaults? window = null, CameraOverlayLayout? overlay = null) =>
        LinkPrinter(id, name, $"192.168.1.{20 + id.Length}") with
        {
            Camera = new CameraSettings(enabled, StreamUri) { Window = window ?? CameraWindowDefaults.Default, Overlay = overlay ?? CameraOverlayLayout.Default },
        };

    private async Task<SettingsViewModel> LoadAsync(params PrinterDefinition[] printers)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = printers }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(
            _store,
            new FakeConnectionTester(),
            Supported,
            cameraTester: new FakeCameraTester(),
            screens: ThreeMonitors(),
            cameraWindowStates: _states,
            cameraWindowPlacer: _placer,
            timeProvider: _time);
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }

    // ---- Factory settings ----

    [Fact]
    public async Task FactorySettingsRestoreEveryValueOfTheSubTab()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", window: Custom));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.ResetCameraWindowCommand.Execute(null);

        Assert.True(printer.RememberLastPlacement);
        Assert.Equal(CameraWindowStartMode.Last, printer.SelectedStartMode.Value);
        Assert.Same(printer.MonitorOptions[0], printer.SelectedMonitor);
        Assert.Equal(CameraWindowSizeText.Format(new CameraWindowSize(960, 540)), printer.NormalSizeText);
        Assert.Equal(CameraWindowCorner.Center, printer.SelectedNormalCorner.Value);
        Assert.Equal(CameraWindowSizeText.Format(new CameraWindowSize(320, 180)), printer.MiniSizeText);
        Assert.Equal(CameraWindowCorner.BottomRight, printer.SelectedMiniCorner.Value);
        Assert.False(printer.TopmostNormal);
        Assert.True(printer.TopmostMini);
        Assert.Equal(CameraWindowDefaults.Default, printer.CameraWindow);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task FactorySettingsKeepTheOverlayAndTheCamera()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", window: Custom, overlay: CustomOverlay));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.ResetCameraWindowCommand.Execute(null);

        Assert.Equal(CustomOverlay, printer.OverlayLayout);
        Assert.True(printer.CameraEnabled);
        Assert.Equal(StreamUri.OriginalString, printer.CameraUrl);
    }

    [Fact]
    public async Task FactorySettingsClearSizeErrors()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.NormalSizeText = "10 x 10";
        printer.MiniSizeText = "abc";
        Assert.True(printer.ShowsCameraWindowErrorMarker);

        printer.ResetCameraWindowCommand.Execute(null);

        Assert.Null(printer.NormalSizeError);
        Assert.Null(printer.MiniSizeError);
        Assert.False(printer.HasErrors);
        Assert.False(printer.ShowsCameraWindowErrorMarker);
    }

    [Fact]
    public async Task FactorySettingsAloneChangeNothingSavedUntilSaving()
    {
        _states.Set("a", Remembered);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", window: Custom));

        vm.Printers[0].ResetCameraWindowCommand.Execute(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(0, _states.GetRemoveCount("a"));
        Assert.Equal(Remembered, _states.Get("a"));
    }

    [Fact]
    public async Task SaveAfterFactorySettingsSavesTheDefaultsAndForgetsTheRememberedWindow()
    {
        _states.Set("a", Remembered);
        _states.Set("b", Remembered);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", window: Custom), Printer("b", "Beta", window: Custom));

        vm.Printers[0].ResetCameraWindowCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);

        AppSettings saved = Assert.Single(_store.Saves).Updated;
        Assert.Equal(CameraWindowDefaults.Default, saved.Printers[0].Camera.Window);
        Assert.Equal(Custom, saved.Printers[1].Camera.Window);
        Assert.Equal(1, _states.GetRemoveCount("a"));
        Assert.Same(CameraWindowState.Default, _states.Get("a"));
        Assert.Equal(0, _states.GetRemoveCount("b"));
        // Save keeps T-035: no camera window is placed again.
        Assert.Empty(_placer.Calls);
    }

    [Fact]
    public async Task FactorySettingsWithTheSavedValuesStillForgetTheRememberedWindow()
    {
        // The values equal the saved ones (already the defaults): nothing to write, but the remembered window is forgotten.
        _states.Set("a", Remembered);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        int closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        vm.Printers[0].ResetCameraWindowCommand.Execute(null);
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(1, _states.GetRemoveCount("a"));
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task FactorySettingsForgetOnlyOnceAndNotOnTheNextSave()
    {
        _store.SaveResult = new SettingsSaveResult(SettingsSaveStatus.Saved, UndeletedPasswords: 1);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", window: Custom));
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.ResetCameraWindowCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, _states.GetRemoveCount("a"));
        Assert.False(vm.IsDirty);

        // The window stayed open (a warning); another save without "Factory settings" forgets nothing more.
        printer.Name = "Renamed";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(2, _store.Saves.Count);
        Assert.Equal(1, _states.GetRemoveCount("a"));
    }

    [Fact]
    public async Task FailedSaveAfterFactorySettingsForgetsNothingAndTriesAgainNextTime()
    {
        _states.Set("a", Remembered);
        _store.SaveResult = new SettingsSaveResult(SettingsSaveStatus.SettingsNotSaved);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", window: Custom));
        vm.Printers[0].ResetCameraWindowCommand.Execute(null);

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, _states.GetRemoveCount("a"));

        _store.SaveResult = new SettingsSaveResult(SettingsSaveStatus.Saved);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, _states.GetRemoveCount("a"));
    }

    // ---- Apply ----

    [Fact]
    public async Task ApplySavesKeepsTheWindowOpenAndPlacesTheCameraWindowOfThatPrinterOnly()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"), Printer("b", "Beta"));
        int closed = 0;
        vm.CloseRequested += (_, _) => closed++;
        PrinterEditorViewModel beta = vm.Printers[1];
        beta.SelectedStartMode = beta.StartModeOptions.Single(option => option.Value == CameraWindowStartMode.Mini);
        beta.MiniSizeText = "640 x 360";

        await beta.ApplyCameraWindowCommand.ExecuteAsync(null);

        CameraWindowDefaults saved = Assert.Single(_store.Saves).Updated.Printers[1].Camera.Window;
        Assert.Equal(CameraWindowStartMode.Mini, saved.StartMode);
        Assert.Equal(new CameraWindowSize(640, 360), saved.Mini.Size);
        (string id, CameraWindowDefaults placed) = Assert.Single(_placer.Calls);
        Assert.Equal("b", id);
        Assert.Equal(saved, placed);
        Assert.Equal(0, closed);
        Assert.False(vm.IsDirty);
        Assert.Equal("Saved", vm.StatusText);
        Assert.False(vm.StatusIsError);
    }

    [Fact]
    public async Task ApplyHandsTheSavedPrinterToTheCameraWindows()
    {
        // The camera windows may have to open the window (it was closed) before monitoring delivers the saved settings:
        // they get the whole saved printer, with its new name and camera address.
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.Name = "Renamed";
        printer.CameraUrl = "rtsp://192.168.0.228/live";

        await printer.ApplyCameraWindowCommand.ExecuteAsync(null);

        PrinterDefinition saved = Assert.Single(_store.Saves).Updated.Printers[0];
        Assert.Same(saved, Assert.Single(_placer.Printers));
        Assert.Equal("Renamed", saved.Name);
        Assert.Equal("rtsp://192.168.0.228/live", saved.Camera.StreamUri?.OriginalString);
    }

    [Fact]
    public async Task ApplyWithoutChangesPlacesTheCameraWindowWithoutSaving()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", window: Custom));
        int closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        await vm.Printers[0].ApplyCameraWindowCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal([("a", Custom)], _placer.Calls);
        Assert.Equal(0, closed);
    }

    [Fact]
    public async Task ApplyAfterFactorySettingsForgetsTheRememberedWindowBeforePlacing()
    {
        _states.Set("a", Remembered);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", window: Custom));
        int removesWhenPlaced = -1;
        _placer.OnApply = _ => removesWhenPlaced = _states.GetRemoveCount("a");
        vm.Printers[0].ResetCameraWindowCommand.Execute(null);

        await vm.Printers[0].ApplyCameraWindowCommand.ExecuteAsync(null);

        Assert.Equal(CameraWindowDefaults.Default, Assert.Single(_store.Saves).Updated.Printers[0].Camera.Window);
        Assert.Equal(1, _states.GetRemoveCount("a"));
        Assert.Equal(1, removesWhenPlaced);
        Assert.Equal([("a", CameraWindowDefaults.Default)], _placer.Calls);
    }

    [Fact]
    public async Task ApplySavesChangesOfOtherPrintersAndTabsToo()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"), Printer("b", "Beta"));
        vm.Printers[0].Name = "Renamed";
        vm.NotifyError = !vm.NotifyError;

        await vm.Printers[1].ApplyCameraWindowCommand.ExecuteAsync(null);

        AppSettings saved = Assert.Single(_store.Saves).Updated;
        Assert.Equal("Renamed", saved.Printers[0].Name);
        Assert.Equal(vm.NotifyError, saved.Notifications.Error);
        Assert.Equal("b", Assert.Single(_placer.Calls).PrinterId);
    }

    [Fact]
    public async Task ApplyWithErrorsSavesAndPlacesNothing()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.NormalSizeText = "1 x 1";

        await printer.ApplyCameraWindowCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Empty(_placer.Calls);
        Assert.True(vm.StatusIsError);
        Assert.Equal(SettingsViewModel.CameraWindowPrinterTab, vm.SelectedPrinterTabIndex);
    }

    [Fact]
    public async Task FailedApplyPlacesNothing()
    {
        _store.SaveResult = new SettingsSaveResult(SettingsSaveStatus.SettingsNotSaved);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        vm.Printers[0].MiniSizeText = "640 x 360";

        await vm.Printers[0].ApplyCameraWindowCommand.ExecuteAsync(null);

        Assert.Single(_store.Saves);
        Assert.Empty(_placer.Calls);
        Assert.True(vm.StatusIsError);
    }

    [Fact]
    public async Task ApplyWithAWarningKeepsTheWarningAndPlaces()
    {
        _store.SaveResult = new SettingsSaveResult(SettingsSaveStatus.Saved, UndeletedPasswords: 1);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        vm.Printers[0].MiniSizeText = "640 x 360";

        await vm.Printers[0].ApplyCameraWindowCommand.ExecuteAsync(null);

        Assert.True(vm.HasStatus);
        Assert.False(vm.StatusIsError);
        Assert.Single(_placer.Calls);
    }

    [Fact]
    public async Task SaveNeverPlacesACameraWindow()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        vm.Printers[0].MiniSizeText = "640 x 360";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(_store.Saves);
        Assert.Empty(_placer.Calls);
    }

    [Fact]
    public async Task ApplyWorksAgainAfterTheFirstApply()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        await printer.ApplyCameraWindowCommand.ExecuteAsync(null);
        printer.NormalSizeText = "640 x 360";

        await printer.ApplyCameraWindowCommand.ExecuteAsync(null);

        Assert.Single(_store.Saves);
        Assert.Equal(2, _placer.Calls.Count);
        Assert.Equal(new CameraWindowSize(640, 360), _placer.Calls[1].Window.Normal.Size);
    }

    // ---- Camera off ----

    [Fact]
    public async Task ButtonsWorkOnlyWhileTheCameraIsOn()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha", enabled: false, window: Custom));
        PrinterEditorViewModel printer = vm.Printers[0];
        int changes = 0;
        printer.ResetCameraWindowCommand.CanExecuteChanged += (_, _) => changes++;

        Assert.False(printer.ResetCameraWindowCommand.CanExecute(null));
        Assert.False(printer.ApplyCameraWindowCommand.CanExecute(null));
        printer.ResetCameraWindowCommand.Execute(null);
        Assert.Equal(Custom, printer.CameraWindow);

        printer.CameraEnabled = true;

        Assert.True(changes > 0);
        Assert.True(printer.ResetCameraWindowCommand.CanExecute(null));
        Assert.True(printer.ApplyCameraWindowCommand.CanExecute(null));
    }

    // ---- "Saved" after Apply (user decision 2026-10-20) ----

    /// <summary>The confirmation is hidden by a continuation of the fake clock; it may run on another thread.</summary>
    private static async Task UntilNoStatusAsync(SettingsViewModel vm)
    {
        for (int i = 0; i < 200 && vm.HasStatus; i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ApplyShowsSavedForAFewSeconds()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        vm.Printers[0].MiniSizeText = "640 x 360";

        await vm.Printers[0].ApplyCameraWindowCommand.ExecuteAsync(null);
        Assert.Equal("Saved", vm.StatusText);

        _time.Advance(SettingsViewModel.SavedConfirmationDuration - TimeSpan.FromMilliseconds(100));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("Saved", vm.StatusText);

        _time.Advance(TimeSpan.FromMilliseconds(100));
        await UntilNoStatusAsync(vm);
        Assert.False(vm.HasStatus);
    }

    [Fact]
    public async Task SavedIsShownInTheAppLanguageAndAlsoWithoutChanges()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));

        await vm.Printers[0].ApplyCameraWindowCommand.ExecuteAsync(null);
        Assert.Equal("Saved", vm.StatusText);

        using (new CultureScope("cs-CZ"))
        {
            vm.RefreshTexts();
            Assert.Equal("Uloženo", vm.StatusText);
        }
    }

    [Theory]
    [InlineData("printer")]
    [InlineData("general")]
    [InlineData("add")]
    public async Task AnEditHidesSaved(string edit)
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        await vm.Printers[0].ApplyCameraWindowCommand.ExecuteAsync(null);
        Assert.True(vm.HasStatus);

        switch (edit)
        {
            case "printer":
                vm.Printers[0].TopmostNormal = true;
                break;
            case "general":
                vm.NotifyError = !vm.NotifyError;
                break;
            default:
                vm.AddPrinterCommand.Execute(null);
                break;
        }

        Assert.False(vm.HasStatus);
    }

    [Fact]
    public async Task ApplyAgainRestartsTheTime()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        await printer.ApplyCameraWindowCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromSeconds(2));

        printer.MiniSizeText = "640 x 360";
        await printer.ApplyCameraWindowCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // 4 s after the first Apply, 2 s after the second one.
        Assert.Equal("Saved", vm.StatusText);
        _time.Advance(TimeSpan.FromSeconds(1));
        await UntilNoStatusAsync(vm);
        Assert.False(vm.HasStatus);
    }

    [Fact]
    public async Task SaveNeverShowsSaved()
    {
        _store.SaveResult = new SettingsSaveResult(SettingsSaveStatus.Saved, UndeletedPasswords: 1);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        vm.Printers[0].MiniSizeText = "640 x 360";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.HasStatus);
        Assert.NotEqual("Saved", vm.StatusText);
    }

    [Fact]
    public async Task ClosingTheWindowStopsTheTimer()
    {
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        await vm.Printers[0].ApplyCameraWindowCommand.ExecuteAsync(null);

        vm.Dispose();
        _time.Advance(SettingsViewModel.SavedConfirmationDuration);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal("Saved", vm.StatusText);
    }

    // ---- Not while saving (review 2026-10-20) ----

    [Fact]
    public async Task ButtonsAreDisabledWhileSaving()
    {
        _store.SaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SettingsViewModel vm = await LoadAsync(Printer("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        int resetChanges = 0;
        int applyChanges = 0;
        printer.ResetCameraWindowCommand.CanExecuteChanged += (_, _) => resetChanges++;
        printer.ApplyCameraWindowCommand.CanExecuteChanged += (_, _) => applyChanges++;
        printer.MiniSizeText = "640 x 360";

        Task saving = vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.IsSaving);
        Assert.False(printer.ResetCameraWindowCommand.CanExecute(null));
        Assert.False(printer.ApplyCameraWindowCommand.CanExecute(null));
        Assert.True(resetChanges > 0);
        Assert.True(applyChanges > 0);
        printer.ResetCameraWindowCommand.Execute(null);
        Assert.Equal("640 x 360", printer.MiniSizeText);

        _store.SaveGate.SetResult();
        await saving;
        Assert.True(printer.ResetCameraWindowCommand.CanExecute(null));
        Assert.True(printer.ApplyCameraWindowCommand.CanExecute(null));
    }

    [Fact]
    public async Task ButtonsAreDisabledUntilLoaded()
    {
        _store.LoadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.Data = new SettingsEditorData(new AppSettings { Printers = [Printer("a", "Alpha")] }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(_store, new FakeConnectionTester(), Supported, cameraTester: new FakeCameraTester(), cameraWindowPlacer: _placer, timeProvider: _time);
        _viewModel = vm;
        Task loading = vm.LoadAsync();
        Assert.Empty(vm.Printers);

        _store.LoadGate.SetResult();
        await loading;

        Assert.True(vm.Printers[0].ApplyCameraWindowCommand.CanExecute(null));
    }

    /// <summary>Records what "Apply" asked the camera windows to do.</summary>
    private sealed class FakeCameraWindowPlacer : ICameraWindowPlacer
    {
        public List<(string PrinterId, CameraWindowDefaults Window)> Calls { get; } = [];

        public List<PrinterDefinition> Printers { get; } = [];

        public Action<string>? OnApply { get; set; }

        public bool ApplySettings(PrinterDefinition printer)
        {
            OnApply?.Invoke(printer.Id);
            Calls.Add((printer.Id, printer.Camera.Window));
            Printers.Add(printer);
            return true;
        }
    }
}
