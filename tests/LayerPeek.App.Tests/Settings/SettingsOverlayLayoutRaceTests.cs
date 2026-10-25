using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras.Layout;
using LayerPeek.App.Composition;
using LayerPeek.App.Settings;
using LayerPeek.App.Settings.Widgets;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// T-092 follow-up (lost update): "Done" of the layout mode of a camera window and "Save" / "Apply" of the settings window
/// at almost the same moment. A layout the settings window did not change is saved as the camera window saved it, in both
/// orders and while the window loads; a layout changed in the window still wins; the window is not dirty afterwards.
/// Everything in memory.
/// </summary>
public sealed class SettingsOverlayLayoutRaceTests : IAsyncDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");
    private static readonly CameraOverlayLayout Moved = CameraOverlayLayout.Default.MoveTo(OverlayWidgetIds.Status, OverlayAnchor.BottomCenter, 0);

    private readonly FakeWebGateway _gateway = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly SettingsWriteGate _gate = new();
    private SettingsViewModel? _viewModel;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        _viewModel?.Dispose();
        _gate.Dispose();
        await _gateway.DisposeAsync();
    }

    private static PrinterDefinition Printer(string id, string name, CameraOverlayLayout? layout = null) =>
        LinkPrinter(id, name, $"192.168.1.{20 + id.Length}") with
        {
            Camera = new CameraSettings(enabled: true, StreamUri) { Overlay = layout ?? CameraOverlayLayout.Default },
        };

    private static AppSettings Settings(params PrinterDefinition[] printers) => new() { Printers = printers };

    private static CameraOverlayLayout OverlayOf(AppSettings settings, string id) => settings.Printers.Single(printer => printer.Id == id).Camera.Overlay;

    private SettingsEditorStore Editor(LayoutRaceStore store, ISettingsApplier? applier = null) =>
        new(() => store, new FakeSecretStore(), Supported, applier ?? new LoggingApplier(store), _gateway, NullLogger<SettingsEditorStore>.Instance, writeGate: _gate);

    private DeferredCameraOverlayLayoutWriter Writer(LayoutRaceStore store) => new(() => store, NullLogger<CameraOverlayLayoutWriter>.Instance, _gate);

    private SettingsViewModel ViewModel(ISettingsEditorStore store, ICameraOverlayLayoutWriter writer)
    {
        SettingsViewModel vm = new(
            store,
            new FakeConnectionTester(),
            Supported,
            cameraTester: new FakeCameraTester(),
            widgets: OverlayWidgets.CreateRegistry(),
            layoutWriter: writer,
            uiDispatcher: _dispatcher);
        _viewModel = vm;
        return vm;
    }

    private async Task<SettingsViewModel> LoadAsync(LayoutRaceStore store, ICameraOverlayLayoutWriter writer)
    {
        SettingsViewModel vm = ViewModel(Editor(store), writer);
        await vm.LoadAsync();
        return vm;
    }

    private static WidgetItemViewModel Widget(SettingsViewModel vm, string id) => vm.Widgets.Single(widget => widget.Id == id);

    private static SettingsChange Change(AppSettings original, AppSettings updated, ILatestOverlayLayouts? latest) =>
        new(original, updated, new Dictionary<string, string>(), LatestOverlayLayouts: latest);

    // ---- Store ----

    [Fact]
    public async Task UnchangedLayoutIsSavedAsTheCameraWindowSavedIt()
    {
        LayoutRaceStore store = new(Settings(Printer("a", "Alpha"), Printer("b", "Beta")));
        LoggingApplier applier = new(store);
        AppSettings original = store.Current;
        AppSettings updated = original with { Printers = [original.Printers[0] with { Name = "Alpha 2" }, original.Printers[1]] };

        SettingsSaveResult result = await Editor(store, applier).SaveAsync(Change(original, updated, new Latest { ["A"] = Moved }));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(Moved, OverlayOf(store.Current, "a"));
        Assert.Equal("Alpha 2", store.Current.Printers[0].Name);
        Assert.Same(CameraOverlayLayout.Default, OverlayOf(store.Current, "b"));
        Assert.Equal(Moved, OverlayOf(Assert.Single(applier.Applied), "a"));
        Assert.NotNull(result.KeptOverlayLayouts);
        Assert.Single(result.KeptOverlayLayouts);
        Assert.Equal(Moved, result.KeptOverlayLayouts["a"]);
    }

    [Fact]
    public async Task LayoutChangedInTheWindowWinsOverTheCameraWindow()
    {
        LayoutRaceStore store = new(Settings(Printer("a", "Alpha")));
        AppSettings original = store.Current;
        CameraOverlayLayout hidden = CameraOverlayLayout.Default.WithVisible(OverlayWidgetIds.Times, visible: false);
        AppSettings updated = original.WithCameraOverlayLayout("a", hidden);

        SettingsSaveResult result = await Editor(store).SaveAsync(Change(original, updated, new Latest { ["a"] = Moved }));

        Assert.Equal(hidden, OverlayOf(store.Current, "a"));
        Assert.Null(result.KeptOverlayLayouts);
    }

    [Fact]
    public async Task NewPrintersAndPrintersWithoutALaterLayoutKeepTheWindowsLayout()
    {
        LayoutRaceStore store = new(Settings(Printer("a", "Alpha")));
        AppSettings original = store.Current;
        AppSettings updated = original with { Printers = [.. original.Printers, Printer("n", "New")] };

        SettingsSaveResult result = await Editor(store).SaveAsync(Change(original, updated, new Latest { ["n"] = Moved }));

        Assert.All(store.Current.Printers, static printer => Assert.Same(CameraOverlayLayout.Default, printer.Camera.Overlay));
        Assert.Null(result.KeptOverlayLayouts);

        // Without a source (tests, older callers) nothing changes either.
        await Editor(store).SaveAsync(Change(original, original.WithCameraOverlayLayout("a", Moved), latest: null));
        Assert.Equal(Moved, OverlayOf(store.Current, "a"));
    }

    [Fact]
    public async Task LatestLayoutsAreReadOnlyOnceNoCameraWindowWritesAnyMore()
    {
        LayoutRaceStore store = new(Settings(Printer("a", "Alpha")));
        AppSettings original = store.Current;
        Latest latest = [];
        await _gate.EnterAsync(Ct);

        // A camera window is saving its layout: the settings window's save waits before it reads anything.
        Task<SettingsSaveResult> saving = Editor(store).SaveAsync(Change(original, original with { PollingInterval = TimeSpan.FromSeconds(9) }, latest));
        Assert.False(saving.IsCompleted);
        latest["a"] = Moved;
        _gate.Exit();
        SettingsSaveResult result = await saving.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(Moved, OverlayOf(store.Current, "a"));
        Assert.Equal(TimeSpan.FromSeconds(9), store.Current.PollingInterval);
    }

    [Fact]
    public async Task CameraWindowWritesItsLayoutOnlyAfterMonitoringHasTheSettings()
    {
        LayoutRaceStore store = new(Settings(Printer("a", "Alpha"))) { SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        DeferredCameraOverlayLayoutWriter writer = Writer(store);
        writer.LayoutSaved += (_, e) => store.Record($"announced {e.PrinterId}");
        AppSettings original = store.Current;

        Task<SettingsSaveResult> saving = Editor(store).SaveAsync(Change(original, original with { PollingInterval = TimeSpan.FromSeconds(9) }, new Latest()));
        await store.SaveStarted.Task.WaitAsync(TestTimeouts.Wait, Ct);
        Task<bool> layoutSaving = writer.SaveAsync("a", Moved, Ct);
        Assert.False(layoutSaving.IsCompleted);
        store.SaveGate.SetResult();

        Assert.Equal(SettingsSaveStatus.Saved, (await saving.WaitAsync(TestTimeouts.Wait, Ct)).Status);
        Assert.True(await layoutSaving.WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Equal(["save settings", "apply", "set layout a", "announced a"], store.Log);
        Assert.Equal(Moved, OverlayOf(store.Current, "a"));
        Assert.Equal(TimeSpan.FromSeconds(9), store.Current.PollingInterval);
    }

    [Fact]
    public async Task FailedSettingsSaveLetsCameraWindowsSaveAgain()
    {
        LayoutRaceStore store = new(Settings(Printer("a", "Alpha"))) { SaveFailure = new IOException("disk full") };
        AppSettings original = store.Current;

        SettingsSaveResult result = await Editor(store).SaveAsync(Change(original, original with { PollingInterval = TimeSpan.FromSeconds(9) }, new Latest { ["a"] = Moved }));

        Assert.Equal(SettingsSaveStatus.SettingsNotSaved, result.Status);
        Assert.True(await Writer(store).SaveAsync("a", Moved, Ct).WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Equal(Moved, OverlayOf(store.Current, "a"));
    }

    [Fact]
    public async Task FailingApplyLetsCameraWindowsSaveAgain()
    {
        LayoutRaceStore store = new(Settings(Printer("a", "Alpha")));
        AppSettings original = store.Current;

        SettingsSaveResult result = await Editor(store, new ThrowingApplier()).SaveAsync(Change(original, original with { PollingInterval = TimeSpan.FromSeconds(9) }, new Latest()));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.True(await Writer(store).SaveAsync("a", Moved, Ct).WaitAsync(TestTimeouts.Wait, Ct));
        Assert.Equal(Moved, OverlayOf(store.Current, "a"));
        Assert.Equal(TimeSpan.FromSeconds(9), store.Current.PollingInterval);
    }

    // ---- Settings window ----

    [Fact]
    public async Task LayoutSavedJustBeforeApplyIsSavedAndTakenOverWithoutMakingTheWindowDirty()
    {
        LayoutRaceStore store = new(Settings(Printer("a", "Alpha"), Printer("b", "Beta")));
        DeferredCameraOverlayLayoutWriter writer = Writer(store);
        SettingsViewModel vm = await LoadAsync(store, writer);
        PrinterEditorViewModel alpha = vm.Printers[0];
        alpha.Name = "Alpha 2";

        // "Done" in the camera window: written and announced; the UI thread has not taken it over yet.
        Assert.True(await writer.SaveAsync("a", Moved, Ct));
        Assert.Equal(1, _dispatcher.Pending);
        Assert.Same(CameraOverlayLayout.Default, alpha.OverlayLayout);
        await vm.ApplyAsync(alpha);

        Assert.Equal(Moved, OverlayOf(store.Current, "a"));
        Assert.Equal("Alpha 2", store.Current.Printers[0].Name);
        Assert.Equal(Moved, alpha.OverlayLayout);
        Assert.False(vm.IsDirty);
        _dispatcher.RunAll();
        Assert.Equal(Moved, alpha.OverlayLayout);
        Assert.False(vm.IsDirty);
        Assert.Same(CameraOverlayLayout.Default, vm.Printers[1].OverlayLayout);
    }

    [Fact]
    public async Task LayoutSavedJustBeforeSaveIsSaved()
    {
        LayoutRaceStore store = new(Settings(Printer("a", "Alpha")));
        DeferredCameraOverlayLayoutWriter writer = Writer(store);
        SettingsViewModel vm = await LoadAsync(store, writer);
        vm.Printers[0].Name = "Alpha 2";

        Assert.True(await writer.SaveAsync("a", Moved, Ct));
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Moved, OverlayOf(store.Current, "a"));
        Assert.Equal("Alpha 2", store.Current.Printers[0].Name);
    }

    [Fact]
    public async Task LayoutSavedWhileApplyingIsWrittenAfterTheSettingsAndTakenOver()
    {
        LayoutRaceStore store = new(Settings(Printer("a", "Alpha"))) { SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        DeferredCameraOverlayLayoutWriter writer = Writer(store);
        SettingsViewModel vm = await LoadAsync(store, writer);
        PrinterEditorViewModel alpha = vm.Printers[0];
        alpha.Name = "Alpha 2";

        Task applying = vm.ApplyAsync(alpha);
        await store.SaveStarted.Task.WaitAsync(TestTimeouts.Wait, Ct);
        Task<bool> layoutSaving = writer.SaveAsync("a", Moved, Ct);
        store.SaveGate.SetResult();
        await applying.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.True(await layoutSaving.WaitAsync(TestTimeouts.Wait, Ct));
        _dispatcher.RunAll();

        Assert.Equal(["save settings", "apply", "set layout a"], store.Log);
        Assert.Equal(Moved, OverlayOf(store.Current, "a"));
        Assert.Equal("Alpha 2", store.Current.Printers[0].Name);
        Assert.Equal(Moved, alpha.OverlayLayout);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task LayoutAnnouncedWhileSavingWaitsAndIsTakenOverAfterTheSave()
    {
        FakeEditorStore store = new() { SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        store.Data = new SettingsEditorData(Settings(Printer("a", "Alpha")), SettingsLoadStatus.Loaded, [], Ids());
        FakeLayoutAnnouncer announcer = new();
        SettingsViewModel vm = ViewModel(store, announcer);
        await vm.LoadAsync();
        PrinterEditorViewModel alpha = vm.Printers[0];
        alpha.Name = "Alpha 2";

        Task applying = vm.ApplyAsync(alpha);
        Assert.True(vm.IsSaving);
        announcer.Announce("a", Moved);
        _dispatcher.RunAll();
        Assert.Same(CameraOverlayLayout.Default, alpha.OverlayLayout);
        store.SaveGate.SetResult();
        await applying.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(Moved, alpha.OverlayLayout);
        Assert.False(vm.IsDirty);
        Assert.Equal("Alpha 2", alpha.Name);
    }

    [Fact]
    public async Task LayoutAnnouncedWhileASaveFailsIsTakenOverAfterwards()
    {
        FakeEditorStore store = new()
        {
            SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously),
            SaveResult = new SettingsSaveResult(SettingsSaveStatus.SettingsNotSaved),
        };
        store.Data = new SettingsEditorData(Settings(Printer("a", "Alpha")), SettingsLoadStatus.Loaded, [], Ids());
        FakeLayoutAnnouncer announcer = new();
        SettingsViewModel vm = ViewModel(store, announcer);
        await vm.LoadAsync();
        PrinterEditorViewModel alpha = vm.Printers[0];
        alpha.Name = "Alpha 2";

        Task applying = vm.ApplyAsync(alpha);
        announcer.Announce("a", Moved);
        _dispatcher.RunAll();
        store.SaveGate.SetResult();
        await applying.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(Moved, alpha.OverlayLayout);
        // Only the name is still unsaved.
        Assert.True(vm.IsDirty);
        alpha.Name = "Alpha";
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task LayoutAnnouncedWhileLoadingIsTakenOverWhenLoaded()
    {
        FakeEditorStore store = new() { LoadGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        store.Data = new SettingsEditorData(Settings(Printer("a", "Alpha"), Printer("b", "Beta")), SettingsLoadStatus.Loaded, [], Ids());
        FakeLayoutAnnouncer announcer = new();
        SettingsViewModel vm = ViewModel(store, announcer);

        Task loading = vm.LoadAsync();
        announcer.Announce("A", Moved);
        _dispatcher.RunAll();
        store.LoadGate.SetResult();
        await loading.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(Moved, vm.Printers[0].OverlayLayout);
        Assert.Same(CameraOverlayLayout.Default, vm.Printers[1].OverlayLayout);
        Assert.False(vm.IsDirty);
        _dispatcher.RunAll();
        Assert.Equal(Moved, vm.Printers[0].OverlayLayout);

        vm.Printers[0].Name = "Alpha 2";
        await vm.SaveCommand.ExecuteAsync(null);
        SettingsChange saved = Assert.Single(store.Saves);
        Assert.Equal(Moved, OverlayOf(saved.Original, "a"));
        Assert.Equal(Moved, OverlayOf(saved.Updated, "a"));
    }

    [Fact]
    public async Task UseLayoutForAllPrintersWinsOverALayoutSavedMeanwhile()
    {
        LayoutRaceStore store = new(Settings(Printer("a", "Alpha"), Printer("b", "Beta"), Printer("c", "Gamma")));
        DeferredCameraOverlayLayoutWriter writer = Writer(store);
        SettingsViewModel vm = await LoadAsync(store, writer);
        PrinterEditorViewModel alpha = vm.Printers[0];
        vm.SelectedPrinter = alpha;
        Widget(vm, OverlayWidgetIds.Times).IsShown = false;
        CameraOverlayLayout chosen = alpha.OverlayLayout;
        vm.ApplyLayoutToAllCommand.Execute(null);

        // The camera window of Beta saves its layout before the UI thread could take it over.
        Assert.True(await writer.SaveAsync("b", Moved, Ct));
        await vm.ApplyAsync(alpha);
        _dispatcher.RunAll();

        Assert.All(store.Current.Printers, printer => Assert.Equal(chosen, printer.Camera.Overlay));
        Assert.All(vm.Printers, printer => Assert.Equal(chosen, printer.OverlayLayout));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task LayoutChangedAndAppliedInTheWindowStaysAfterAnotherSave()
    {
        LayoutRaceStore store = new(Settings(Printer("a", "Alpha")));
        DeferredCameraOverlayLayoutWriter writer = Writer(store);
        SettingsViewModel vm = await LoadAsync(store, writer);
        PrinterEditorViewModel alpha = vm.Printers[0];
        Assert.True(await writer.SaveAsync("a", Moved, Ct));
        Widget(vm, OverlayWidgetIds.Times).IsShown = false;
        CameraOverlayLayout chosen = alpha.OverlayLayout;

        await vm.ApplyAsync(alpha);
        _dispatcher.RunAll();
        Assert.Equal(chosen, OverlayOf(store.Current, "a"));
        Assert.Equal(chosen, alpha.OverlayLayout);
        Assert.False(vm.IsDirty);

        // The camera window's older layout was replaced by the window's change: a later save does not bring it back.
        alpha.Name = "Alpha 2";
        await vm.ApplyAsync(alpha);
        Assert.Equal(chosen, OverlayOf(store.Current, "a"));
        Assert.Equal("Alpha 2", store.Current.Printers[0].Name);
        Assert.False(vm.IsDirty);
    }

    /// <summary>Latest layouts as a test sets them.</summary>
    private sealed class Latest : Dictionary<string, CameraOverlayLayout>, ILatestOverlayLayouts
    {
        public Latest()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public IReadOnlyDictionary<string, CameraOverlayLayout> Read() => new Dictionary<string, CameraOverlayLayout>(this, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The settings file in memory, written by the settings window (whole file) and the layout writer (one layout, like
    /// the file store's read-modify-write); records the order of the writes. Thread-safe.
    /// </summary>
    private sealed class LayoutRaceStore(AppSettings settings) : ISettingsStore
    {
        private readonly Lock _lock = new();
        private readonly List<string> _log = [];
        private AppSettings _current = settings;

        public AppSettings Current
        {
            get
            {
                lock (_lock)
                {
                    return _current;
                }
            }
        }

        public IReadOnlyList<string> Log
        {
            get
            {
                lock (_lock)
                {
                    return [.. _log];
                }
            }
        }

        /// <summary>When set, a save of the whole file waits for it (after <see cref="SaveStarted"/>).</summary>
        public TaskCompletionSource? SaveGate { get; init; }

        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? SaveFailure { get; init; }

        public void Record(string entry)
        {
            lock (_lock)
            {
                _log.Add(entry);
            }
        }

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsLoadResult { Settings = Current, Status = SettingsLoadStatus.Loaded });

        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Record("save settings");
            SaveStarted.TrySetResult();
            if (SaveGate is { } gate)
            {
                await gate.Task.ConfigureAwait(false);
            }

            if (SaveFailure is not null)
            {
                throw SaveFailure;
            }

            lock (_lock)
            {
                _current = settings;
            }
        }

        public Task<bool> SetCameraOverlayLayoutAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _log.Add($"set layout {printerId}");
                _current = _current.WithCameraOverlayLayout(printerId, layout);
            }

            return Task.FromResult(true);
        }
    }

    /// <summary>Monitoring that breaks the interface contract by throwing (a bug); the save must still release the write gate.</summary>
    private sealed class ThrowingApplier : ISettingsApplier
    {
        public Task ApplyAsync(AppSettings settings, bool credentialsChanged) => throw new InvalidOperationException("apply failed");
    }

    /// <summary>Monitoring: records the applied settings into the store's log.</summary>
    private sealed class LoggingApplier(LayoutRaceStore store) : ISettingsApplier
    {
        private readonly Lock _lock = new();
        private readonly List<AppSettings> _applied = [];

        public IReadOnlyList<AppSettings> Applied
        {
            get
            {
                lock (_lock)
                {
                    return [.. _applied];
                }
            }
        }

        public Task ApplyAsync(AppSettings settings, bool credentialsChanged)
        {
            store.Record("apply");
            lock (_lock)
            {
                _applied.Add(settings);
            }

            return Task.CompletedTask;
        }
    }
}
