using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// The pin of the camera window (T-117): switching "always on top" of the current mode right away, saving only that option
/// of the printer, switching back to the stored option when the save fails, and keeping the switch while settings are
/// applied before the save finished.
/// </summary>
public sealed class CameraViewModelTopmostTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly FakeCameraFeedFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakePrinterMonitor _monitor = new();
    private readonly MonitoredPrinters _printers = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly MemoryCameraWindowStateStore _states = new();
    private readonly FakeTopmostWriter _writer = new();
    private readonly ListLogger<CameraViewModel> _logger = new();
    private readonly List<CameraViewModel> _created = [];
    private readonly CultureScope _culture = new("en-US");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        _culture.Dispose();
        foreach (CameraViewModel viewModel in _created)
        {
            viewModel.Dispose();
            await viewModel.StreamCompletion.WaitAsync(TestTimeouts.Wait, Ct);
        }
    }

    private static PrinterDefinition Printer(CameraWindowDefaults defaults, string id = "mk4") =>
        Definition(id, "MK4") with { Camera = new CameraSettings(enabled: true, StreamUri) { Window = defaults } };

    /// <summary>The printers monitoring runs with (what the window switches back to).</summary>
    private void Apply(CameraWindowDefaults defaults)
    {
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(defaults), Printer(CameraWindowDefaults.Default, "xl")] });
        _dispatcher.RunAll();
    }

    private CameraViewModel Create(CameraWindowStartMode startMode = CameraWindowStartMode.Normal, CameraWindowDefaults? defaults = null, bool withWriter = true, ICameraWindowTopmostWriter? writer = null)
    {
        CameraWindowDefaults window = (defaults ?? CameraWindowDefaults.Default) with { StartMode = startMode };
        Apply(window);
        ReconnectingCameraFeed feed = new(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraViewModel viewModel = new(
            CameraTarget.From(Printer(window))!, feed, _monitor, _printers, _dispatcher, _states, _time, CameraFeedOptions.Default, _logger,
            topmostWriter: withWriter ? writer ?? _writer : null);
        _created.Add(viewModel);
        return viewModel;
    }

    private static List<string?> Changes(CameraViewModel viewModel)
    {
        List<string?> changes = [];
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        return changes;
    }

    [Fact]
    public async Task PinInTheNormalWindowSwitchesAtOnceAndSavesOnlyItsOption()
    {
        CameraViewModel viewModel = Create();
        List<string?> changes = Changes(viewModel);
        Assert.False(viewModel.IsTopmost);
        Assert.Equal("Always on top: off", viewModel.TopmostToolTip);

        viewModel.ToggleTopmostCommand.Execute(null);

        Assert.True(viewModel.IsTopmost);
        Assert.True(viewModel.Defaults.TopmostNormal);
        Assert.True(viewModel.Defaults.TopmostMini);
        Assert.Equal("Always on top: on", viewModel.TopmostToolTip);
        Assert.Contains(nameof(CameraViewModel.IsTopmost), changes);
        Assert.Contains(nameof(CameraViewModel.TopmostToolTip), changes);
        await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.Equal([("mk4", CameraWindowMode.Normal, true)], _writer.Saves);
        Assert.True(viewModel.IsTopmost);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task PinInTheMiniPreviewSavesTheMiniOptionAndTheMiniTipFollows()
    {
        CameraViewModel viewModel = Create(CameraWindowStartMode.Mini);
        List<string?> changes = Changes(viewModel);
        Assert.True(viewModel.IsTopmost);
        Assert.Equal("Mini preview, always on top", viewModel.MiniToolTip);

        viewModel.ToggleTopmostCommand.Execute(null);

        Assert.False(viewModel.IsTopmost);
        Assert.False(viewModel.Defaults.TopmostMini);
        Assert.False(viewModel.Defaults.TopmostNormal);
        Assert.Equal("Preview", viewModel.MiniToolTip);
        Assert.Contains(nameof(CameraViewModel.MiniToolTip), changes);
        await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.Equal([("mk4", CameraWindowMode.Mini, false)], _writer.Saves);

        // Each mode keeps its own option.
        viewModel.ShowNormalCommand.Execute(null);
        Assert.False(viewModel.IsTopmost);
        Assert.Equal("Always on top: off", viewModel.TopmostToolTip);
    }

    [Fact]
    public void FullScreenHasNoPin()
    {
        CameraViewModel viewModel = Create(CameraWindowStartMode.FullScreen, CameraWindowDefaults.Default with { TopmostNormal = true });
        int canExecuteChanges = 0;
        viewModel.ToggleTopmostCommand.CanExecuteChanged += (_, _) => canExecuteChanges++;

        Assert.False(viewModel.ToggleTopmostCommand.CanExecute(null));
        viewModel.ToggleTopmostCommand.Execute(null);
        Assert.False(viewModel.IsTopmost);
        Assert.Empty(_writer.Saves);

        viewModel.ToggleFullScreenCommand.Execute(null);
        Assert.True(viewModel.IsNormal);
        Assert.True(viewModel.ToggleTopmostCommand.CanExecute(null));
        Assert.Equal(1, canExecuteChanges);
        Assert.Equal("Always on top: on", viewModel.TopmostToolTip);
    }

    public static TheoryData<string> Failures => ["exception", "not written", "no writer"];

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task FailedSaveSwitchesBackToTheStoredOptionAndLogsWithoutSecrets(string failure)
    {
        CameraViewModel viewModel = Create(withWriter: failure != "no writer");
        _writer.Hold();

        viewModel.ToggleTopmostCommand.Execute(null);
        // Without a writer nothing is pending: the window switches back at once.
        Assert.Equal(failure != "no writer", viewModel.IsTopmost);
        if (failure == "exception")
        {
            _writer.Failure = new IOException("The settings file is locked.");
        }

        _writer.Release(failure == "exception");
        await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.False(viewModel.IsTopmost);
        Assert.False(viewModel.Defaults.TopmostNormal);
        Assert.Equal("Always on top: off", viewModel.TopmostToolTip);
        (LogLevel level, string message, Exception? exception) = Assert.Single(_logger.Entries);
        Assert.Equal(failure == "exception" ? LogLevel.Error : LogLevel.Warning, level);
        Assert.Equal(failure == "exception", exception is IOException);
        Assert.Contains("'mk4'", message, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.0.227", message, StringComparison.Ordinal);

        // The pin works again afterwards.
        _writer.Failure = null;
        _writer.Result = true;
        viewModel.ToggleTopmostCommand.Execute(null);
        await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.Equal(failure != "no writer", viewModel.IsTopmost);
    }

    [Fact]
    public async Task FailedSaveSwitchesBackToWhatMonitoringRunsWith()
    {
        CameraViewModel viewModel = Create();
        _writer.Hold();
        viewModel.ToggleTopmostCommand.Execute(null);

        // Settings saved meanwhile turned the option on: switching back means staying on.
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(CameraWindowDefaults.Default with { TopmostNormal = true })] });
        _dispatcher.RunAll();
        _writer.Failure = new IOException("locked");
        _writer.Release(true);
        await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.True(viewModel.IsTopmost);
    }

    [Fact]
    public async Task SettingsAppliedWhileTheSaveIsPendingKeepTheSwitchedOption()
    {
        CameraViewModel viewModel = Create();
        _writer.Hold();

        viewModel.ToggleTopmostCommand.Execute(null);
        // Another change of the settings (e.g. a layout saved by another window) arrives with the old option.
        Apply(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal });

        Assert.True(viewModel.IsTopmost);
        _writer.Release(true);
        await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.True(viewModel.IsTopmost);

        // Once saved, the settings decide again.
        Apply(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal, TopmostNormal = true });
        Assert.True(viewModel.IsTopmost);
        Apply(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal });
        Assert.False(viewModel.IsTopmost);
    }

    [Fact]
    public async Task RepeatedClicksSaveInOrderAndTheLastOneDecides()
    {
        CameraViewModel viewModel = Create();
        _writer.Hold();

        viewModel.ToggleTopmostCommand.Execute(null);
        viewModel.ToggleTopmostCommand.Execute(null);
        viewModel.ToggleTopmostCommand.Execute(null);
        Assert.True(viewModel.IsTopmost);

        _writer.Release(true);
        await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal([("mk4", CameraWindowMode.Normal, true), ("mk4", CameraWindowMode.Normal, false), ("mk4", CameraWindowMode.Normal, true)], _writer.Saves);
        Assert.True(viewModel.IsTopmost);
    }

    [Fact]
    public async Task ClosedWindowIgnoresTheResultOfItsSave()
    {
        CameraViewModel viewModel = Create();
        _writer.Hold();
        viewModel.ToggleTopmostCommand.Execute(null);

        viewModel.Dispose();
        _writer.Failure = new IOException("locked");
        _writer.Release(false);
        await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.False(viewModel.ToggleTopmostCommand.CanExecute(null));
        Assert.Single(_writer.Saves);
    }

    [Fact]
    public async Task OptionTheFileHoldsAlreadyStaysSwitchedWithoutAWarning()
    {
        // Review of T-117: a save of the settings window wrote "on" for the normal window, but monitoring still runs with
        // "off" (its old run is ending). The click asks for "on": the file holds it, nothing is written, and that is no
        // failure; the announcement lets the running settings catch up.
        CameraWindowTopmostWriter writer = new(new AlreadyStoredStore());
        CameraViewModel viewModel = Create(writer: writer);
        List<CameraWindowTopmostSavedEventArgs> announced = [];
        writer.TopmostSaved += (_, e) => announced.Add(e);

        viewModel.ToggleTopmostCommand.Execute(null);
        await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.True(viewModel.IsTopmost);
        Assert.Empty(_logger.Entries);
        Assert.Single(announced);
    }

    [Fact]
    public async Task ApplyInTheSettingsWhileTheSaveIsPendingKeepsTheSwitchedOption()
    {
        CameraViewModel viewModel = Create();
        _writer.Hold();
        viewModel.ToggleTopmostCommand.Execute(null);
        List<string?> changes = Changes(viewModel);

        // "Apply" (T-040) with the old option: no flicker back while the pin's save is pending.
        viewModel.ApplyWindowSettings(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal });

        Assert.True(viewModel.IsTopmost);
        Assert.DoesNotContain(nameof(CameraViewModel.IsTopmost), changes);
        _writer.Release(true);
        await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.True(viewModel.IsTopmost);
    }

    [Fact]
    public async Task BothModesSwitchedWhileTheFirstSaveIsPendingEndUpCorrect()
    {
        CameraViewModel viewModel = Create();
        _writer.Hold();

        viewModel.ToggleTopmostCommand.Execute(null);
        viewModel.ShowMiniCommand.Execute(null);
        Assert.True(viewModel.IsTopmost);
        viewModel.ToggleTopmostCommand.Execute(null);
        Assert.False(viewModel.IsTopmost);
        Assert.True(viewModel.Defaults.TopmostNormal);

        // Settings applied meanwhile keep both pending options.
        Apply(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal });
        Assert.Equal(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal, TopmostNormal = true, TopmostMini = false }, viewModel.Defaults);

        _writer.Release(true);
        await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal([("mk4", CameraWindowMode.Normal, true), ("mk4", CameraWindowMode.Mini, false)], _writer.Saves);
        Assert.True(viewModel.Defaults.TopmostNormal);
        Assert.False(viewModel.Defaults.TopmostMini);
        Assert.False(viewModel.IsTopmost);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task FailedFirstModeSwitchesOnlyThatModeBack()
    {
        CameraViewModel viewModel = Create();
        _writer.Hold();

        viewModel.ToggleTopmostCommand.Execute(null);
        viewModel.ShowMiniCommand.Execute(null);
        viewModel.ToggleTopmostCommand.Execute(null);
        _writer.Release(false);
        await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait, Ct);

        // The normal window's save was left alone and switches back; the mini preview's (a separate save) succeeded.
        Assert.False(viewModel.Defaults.TopmostNormal);
        Assert.False(viewModel.Defaults.TopmostMini);
        Assert.Single(_logger.Entries);
    }

    [Fact]
    public void TipFollowsTheLanguage()
    {
        CameraViewModel viewModel = Create(defaults: CameraWindowDefaults.Default with { TopmostNormal = true });
        List<string?> changes = Changes(viewModel);
        using CultureScope czech = new("cs");

        viewModel.RefreshTexts();

        Assert.Contains(nameof(CameraViewModel.TopmostToolTip), changes);
        Assert.Equal("Vždy navrchu: zapnuto", viewModel.TopmostToolTip);
    }

    [Fact]
    public void ToolTipChangesWithTheMode()
    {
        CameraViewModel viewModel = Create(defaults: CameraWindowDefaults.Default with { TopmostNormal = false, TopmostMini = true });
        List<string?> changes = Changes(viewModel);

        viewModel.ShowMiniCommand.Execute(null);

        Assert.Equal("Always on top: on", viewModel.TopmostToolTip);
        Assert.Contains(nameof(CameraViewModel.TopmostToolTip), changes);
    }

    /// <summary>A settings file that holds every option already: nothing is written, the writer still announces it.</summary>
    private sealed class AlreadyStoredStore : ISettingsStore
    {
        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsLoadResult { Settings = AppSettings.Default, Status = SettingsLoadStatus.Loaded });

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CameraWindowTopmostStoreResult> SetCameraWindowTopmostAsync(string printerId, CameraWindowMode mode, bool topmost, CancellationToken cancellationToken) =>
            Task.FromResult(CameraWindowTopmostStoreResult.AlreadyStored);
    }
}
