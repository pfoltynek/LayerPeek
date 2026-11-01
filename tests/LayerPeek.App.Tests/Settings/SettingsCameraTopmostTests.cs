using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras;
using LayerPeek.App.Composition;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// "Always on top" saved by the pin of a camera window while the settings window is open (T-117), like a layout saved by the
/// layout mode (<see cref="SettingsOverlayLayoutRaceTests"/>): the window shows the new option of that printer and mode and
/// keeps its other unsaved changes; a save of the window keeps the pin's option unless the window changed it itself, in both
/// orders. Everything in memory.
/// </summary>
public sealed class SettingsCameraTopmostTests : IAsyncDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

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

    private static PrinterDefinition Printer(string id, string name) =>
        LinkPrinter(id, name, $"192.168.1.{20 + id.Length}") with { Camera = new CameraSettings(enabled: true, StreamUri) };

    private static AppSettings Settings(params PrinterDefinition[] printers) => new() { Printers = printers };

    private static CameraWindowDefaults WindowOf(AppSettings settings, string id) => settings.Printers.Single(printer => printer.Id == id).Camera.Window;

    private SettingsEditorStore Editor(TopmostStore store) =>
        new(() => store, new FakeSecretStore(), Supported, new RecordingApplier(store), _gateway, NullLogger<SettingsEditorStore>.Instance, writeGate: _gate);

    private DeferredCameraWindowTopmostWriter Writer(TopmostStore store) => new(() => store, NullLogger<CameraWindowTopmostWriter>.Instance, _gate);

    private async Task<SettingsViewModel> LoadAsync(TopmostStore store, ICameraWindowTopmostWriter writer)
    {
        SettingsViewModel vm = new(
            Editor(store),
            new FakeConnectionTester(),
            Supported,
            cameraTester: new FakeCameraTester(),
            widgets: OverlayWidgets.CreateRegistry(),
            uiDispatcher: _dispatcher,
            topmostWriter: writer);
        _viewModel = vm;
        await vm.LoadAsync();
        return vm;
    }

    private static SettingsChange Change(AppSettings original, AppSettings updated, params CameraTopmostChoice[] latest) =>
        new(original, updated, new Dictionary<string, string>(), LatestCameraTopmost: new Latest(latest));

    // ---- Store ----

    [Fact]
    public async Task UnchangedOptionIsSavedAsThePinSavedIt()
    {
        TopmostStore store = new(Settings(Printer("a", "Alpha"), Printer("b", "Beta")));
        AppSettings original = store.Current;
        AppSettings updated = original with { Printers = [original.Printers[0] with { Name = "Alpha 2" }, original.Printers[1]] };

        SettingsSaveResult result = await Editor(store).SaveAsync(Change(original, updated, new CameraTopmostChoice("A", CameraWindowMode.Mini, false)));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(CameraWindowDefaults.Default with { TopmostMini = false }, WindowOf(store.Current, "a"));
        Assert.Equal("Alpha 2", store.Current.Printers[0].Name);
        Assert.Same(CameraWindowDefaults.Default, WindowOf(store.Current, "b"));
        Assert.Equal([new CameraTopmostChoice("a", CameraWindowMode.Mini, false)], result.KeptCameraTopmost);
    }

    [Fact]
    public async Task OptionChangedInTheWindowWinsAndNewPrintersKeepTheirs()
    {
        TopmostStore store = new(Settings(Printer("a", "Alpha")));
        AppSettings original = store.Current;
        AppSettings updated = original.WithCameraWindowTopmost("a", CameraWindowMode.Normal, true) with
        {
            Printers = [original.WithCameraWindowTopmost("a", CameraWindowMode.Normal, true).Printers[0], Printer("n", "New")],
        };

        SettingsSaveResult result = await Editor(store).SaveAsync(Change(
            original,
            updated,
            new CameraTopmostChoice("a", CameraWindowMode.Normal, false),
            new CameraTopmostChoice("n", CameraWindowMode.Normal, true)));

        Assert.True(WindowOf(store.Current, "a").TopmostNormal);
        Assert.False(WindowOf(store.Current, "n").TopmostNormal);
        Assert.Null(result.KeptCameraTopmost);
    }

    // ---- Settings window ----

    [Fact]
    public async Task PinIsTakenOverWithoutLosingUnsavedChangesOrMakingTheWindowDirty()
    {
        TopmostStore store = new(Settings(Printer("a", "Alpha"), Printer("b", "Beta")));
        DeferredCameraWindowTopmostWriter writer = Writer(store);
        SettingsViewModel vm = await LoadAsync(store, writer);
        PrinterEditorViewModel alpha = vm.Printers[0];
        alpha.Name = "Alpha 2";
        alpha.TopmostMini = false;
        Assert.True(vm.IsDirty);

        Assert.True(await writer.SaveAsync("a", CameraWindowMode.Normal, true, Ct));
        Assert.False(alpha.TopmostNormal);
        _dispatcher.RunAll();

        // Only that option of that printer; the unsaved name and mini option stay.
        Assert.True(alpha.TopmostNormal);
        Assert.False(alpha.TopmostMini);
        Assert.Equal("Alpha 2", alpha.Name);
        Assert.False(vm.Printers[1].TopmostNormal);
        Assert.True(vm.IsDirty);

        // Undoing the own changes leaves nothing to save: the pin's option is the saved one now.
        alpha.Name = "Alpha";
        alpha.TopmostMini = true;
        Assert.False(vm.IsDirty);

        alpha.Name = "Alpha 3";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(CameraWindowDefaults.Default with { TopmostNormal = true }, WindowOf(store.Current, "a"));
        Assert.Equal("Alpha 3", store.Current.Printers[0].Name);
    }

    [Fact]
    public async Task PinSavedJustBeforeApplyIsSavedAndTakenOverWithoutMakingTheWindowDirty()
    {
        TopmostStore store = new(Settings(Printer("a", "Alpha")));
        DeferredCameraWindowTopmostWriter writer = Writer(store);
        SettingsViewModel vm = await LoadAsync(store, writer);
        PrinterEditorViewModel alpha = vm.Printers[0];
        alpha.Name = "Alpha 2";

        // Written and announced; the UI thread has not taken it over yet.
        Assert.True(await writer.SaveAsync("a", CameraWindowMode.Mini, false, Ct));
        Assert.Equal(1, _dispatcher.Pending);
        await vm.ApplyAsync(alpha);

        Assert.False(WindowOf(store.Current, "a").TopmostMini);
        Assert.Equal("Alpha 2", store.Current.Printers[0].Name);
        Assert.False(alpha.TopmostMini);
        Assert.False(vm.IsDirty);
        _dispatcher.RunAll();
        Assert.False(alpha.TopmostMini);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task PinSavedWhileApplyingIsWrittenAfterTheSettingsAndTakenOver()
    {
        TopmostStore store = new(Settings(Printer("a", "Alpha"))) { SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        DeferredCameraWindowTopmostWriter writer = Writer(store);
        SettingsViewModel vm = await LoadAsync(store, writer);
        PrinterEditorViewModel alpha = vm.Printers[0];
        alpha.Name = "Alpha 2";

        Task applying = vm.ApplyAsync(alpha);
        await store.SaveStarted.Task.WaitAsync(TestTimeouts.Wait, Ct);
        Task<bool> pinSaving = writer.SaveAsync("a", CameraWindowMode.Normal, true, Ct);
        Assert.False(pinSaving.IsCompleted);
        store.SaveGate.SetResult();
        await applying.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.True(await pinSaving.WaitAsync(TestTimeouts.Wait, Ct));
        _dispatcher.RunAll();

        Assert.Equal(["save settings", "apply", "set topmost a Normal"], store.Log);
        Assert.True(WindowOf(store.Current, "a").TopmostNormal);
        Assert.Equal("Alpha 2", store.Current.Printers[0].Name);
        Assert.True(alpha.TopmostNormal);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task PinOfARemovedPrinterIsIgnored()
    {
        TopmostStore store = new(Settings(Printer("a", "Alpha"), Printer("b", "Beta")));
        DeferredCameraWindowTopmostWriter writer = Writer(store);
        SettingsViewModel vm = await LoadAsync(store, writer);
        vm.Printers.RemoveAt(1);

        Assert.True(await writer.SaveAsync("b", CameraWindowMode.Normal, true, Ct));
        _dispatcher.RunAll();

        Assert.Single(vm.Printers);
        Assert.False(vm.Printers[0].TopmostNormal);
    }

    private sealed class Latest(IReadOnlyList<CameraTopmostChoice> choices) : ILatestCameraTopmost
    {
        public IReadOnlyList<CameraTopmostChoice> Read() => choices;
    }

    /// <summary>
    /// The settings file in memory, written by the settings window (whole file) and the pin (one option, like the file
    /// store's read-modify-write); records the order of the writes. Thread-safe.
    /// </summary>
    private sealed class TopmostStore(AppSettings settings) : ISettingsStore
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

            lock (_lock)
            {
                _current = settings;
            }
        }

        /// <summary>Like the file store: written only when the stored option differs; a printer it does not have is left alone.</summary>
        public Task<CameraWindowTopmostStoreResult> SetCameraWindowTopmostAsync(string printerId, CameraWindowMode mode, bool topmost, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                AppSettings updated = _current.WithCameraWindowTopmost(printerId, mode, topmost);
                if (ReferenceEquals(updated, _current))
                {
                    return Task.FromResult(_current.Printers.Any(p => string.Equals(p.Id, printerId, StringComparison.OrdinalIgnoreCase)) ? CameraWindowTopmostStoreResult.AlreadyStored : CameraWindowTopmostStoreResult.LeftAlone);
                }

                _log.Add($"set topmost {printerId} {mode}");
                _current = updated;
            }

            return Task.FromResult(CameraWindowTopmostStoreResult.Written);
        }
    }

    /// <summary>Monitoring: records the applied settings into the store's log.</summary>
    private sealed class RecordingApplier(TopmostStore store) : ISettingsApplier
    {
        public Task ApplyAsync(AppSettings settings, bool credentialsChanged)
        {
            store.Record("apply");
            return Task.CompletedTask;
        }
    }
}
