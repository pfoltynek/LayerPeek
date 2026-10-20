using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Cameras;

/// <summary>Remembered state versus the window defaults when a camera window opens and closes (T-035).</summary>
public sealed class CameraViewModelWindowTests : IAsyncDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly CameraWindowState Remembered = new(
        CameraWindowMode.Mini,
        new CameraWindowPlacement(100, 80, 700, 400),
        new CameraWindowPlacement(300, 200, 400, 225));

    private readonly FakeCameraFeedFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakePrinterMonitor _monitor = new();
    private readonly MonitoredPrinters _printers = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly MemoryCameraWindowStateStore _states = new();
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

    private CameraViewModel Create(CameraWindowDefaults defaults, string id = "mk4")
    {
        ReconnectingCameraFeed feed = new(_factory, _time, NullLogger<ReconnectingCameraFeed>.Instance, CameraFeedOptions.Default);
        CameraTarget target = new(id, "MK4", StreamUri) { Window = defaults };
        CameraViewModel viewModel = new(target, feed, _monitor, _printers, _dispatcher, _states, _time, CameraFeedOptions.Default, NullLogger<CameraViewModel>.Instance);
        _created.Add(viewModel);
        return viewModel;
    }

    private static PrinterDefinition Printer(CameraWindowDefaults defaults, string id = "mk4") =>
        Definition(id, "MK4") with { Camera = new CameraSettings(enabled: true, StreamUri) { Window = defaults } };

    [Fact]
    public void TargetCarriesTheWindowDefaultsOfThePrinter()
    {
        CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };

        Assert.Equal(defaults, CameraTarget.From(Printer(defaults))!.Window);
        Assert.Same(CameraWindowDefaults.Default, new CameraTarget("a", "A", StreamUri).Window);
    }

    [Fact]
    public void RememberOnAndNothingRememberedOpensWithTheDefaults()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);

        Assert.Equal(CameraWindowMode.Normal, viewModel.Mode);
        Assert.Null(viewModel.NormalPlacement);
        Assert.Null(viewModel.MiniPlacement);
        Assert.Same(CameraWindowDefaults.Default, viewModel.Defaults);
    }

    [Fact]
    public void RememberOnUsesTheRememberedModeAndPlacements()
    {
        _states.Set("mk4", Remembered);

        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);

        Assert.Equal(CameraWindowMode.Mini, viewModel.Mode);
        Assert.Equal(Remembered.Normal, viewModel.NormalPlacement);
        Assert.Equal(Remembered.Mini, viewModel.MiniPlacement);
    }

    [Fact]
    public void RememberOffIgnoresThePlacementsButSameAsLastTimeKeepsTheMode()
    {
        // T-035 Q2: the remembered mode, at the default position and size.
        _states.Set("mk4", Remembered);

        CameraViewModel viewModel = Create(CameraWindowDefaults.Default with { RememberLastPlacement = false });

        Assert.Equal(CameraWindowMode.Mini, viewModel.Mode);
        Assert.Null(viewModel.NormalPlacement);
        Assert.Null(viewModel.MiniPlacement);
    }

    [Theory]
    [InlineData(CameraWindowStartMode.Last, CameraWindowMode.Mini)]
    [InlineData(CameraWindowStartMode.Normal, CameraWindowMode.Normal)]
    [InlineData(CameraWindowStartMode.Mini, CameraWindowMode.Mini)]
    [InlineData(CameraWindowStartMode.FullScreen, CameraWindowMode.FullScreen)]
    public void StartModeChoosesTheMode(CameraWindowStartMode startMode, CameraWindowMode expected)
    {
        _states.Set("mk4", Remembered);

        Assert.Equal(expected, Create(CameraWindowDefaults.Default with { StartMode = startMode }).Mode);
    }

    [Fact]
    public void StateIsWrittenBackWhenTheWindowCloses()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        viewModel.RememberPlacement(CameraWindowMode.Normal, new CameraWindowPlacement(5, 6, 700, 400));

        viewModel.Dispose();

        Assert.Equal(new CameraWindowState(CameraWindowMode.Normal, new CameraWindowPlacement(5, 6, 700, 400), null), _states.Get("mk4"));
    }

    [Fact]
    public void RememberOffKeepsTheStoredPlacementsAndRemembersOnlyTheMode()
    {
        // User decision 2026-10-19: with "remember" off, closing must not overwrite the placements remembered earlier.
        _states.Set("mk4", Remembered);
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default with { RememberLastPlacement = false });
        Assert.True(viewModel.IsMini);

        // The user moved the window and switched to the normal window (without showing the mini preview again).
        viewModel.RememberPlacement(CameraWindowMode.Mini, new CameraWindowPlacement(5, 6, 330, 190));
        viewModel.ShowNormalCommand.Execute(null);
        viewModel.RememberPlacement(CameraWindowMode.Normal, new CameraWindowPlacement(7, 8, 800, 450));
        viewModel.Dispose();

        Assert.Equal(Remembered with { Mode = CameraWindowMode.Normal }, _states.Get("mk4"));
    }

    [Fact]
    public void RememberOffWithNothingStoredRemembersOnlyTheMode()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default with { RememberLastPlacement = false, StartMode = CameraWindowStartMode.Mini });
        viewModel.RememberPlacement(CameraWindowMode.Mini, new CameraWindowPlacement(5, 6, 330, 190));

        viewModel.Dispose();

        Assert.Equal(new CameraWindowState(CameraWindowMode.Mini, null, null), _states.Get("mk4"));
    }

    [Fact]
    public void StateRemovedWhileTheWindowIsOpenIsNotWrittenBack()
    {
        // The settings changed the defaults: they must apply the next time the window opens (T-035).
        _states.Set("mk4", Remembered);
        _states.Set("xl", Remembered);
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        CameraViewModel other = Create(CameraWindowDefaults.Default, id: "xl");
        viewModel.RememberPlacement(CameraWindowMode.Normal, new CameraWindowPlacement(5, 6, 700, 400));

        _states.Remove("mk4");
        viewModel.Dispose();
        other.Dispose();

        Assert.Same(CameraWindowState.Default, _states.Get("mk4"));
        Assert.Equal(Remembered, _states.Get("xl"));
    }

    [Fact]
    public void DefaultsFollowSavedSettingsWithoutChangingTheOpenWindow()
    {
        _states.Set("mk4", Remembered);
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        CameraWindowDefaults changed = CameraWindowDefaults.Default with { Mini = new CameraWindowLayout(new CameraWindowSize(400, 300), CameraWindowCorner.TopLeft) };

        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(changed)] });
        _dispatcher.RunAll();

        Assert.Equal(changed, viewModel.Defaults);
        Assert.Equal(CameraWindowMode.Mini, viewModel.Mode);
        Assert.Equal(Remembered.Mini, viewModel.MiniPlacement);
    }

    public static TheoryData<bool, bool> TopmostOptions => new() { { false, false }, { false, true }, { true, false }, { true, true } };

    [Theory]
    [MemberData(nameof(TopmostOptions))]
    public void TopmostFollowsTheModeAndTheOptions(bool normal, bool mini)
    {
        // T-037: normal window and mini preview by their options, full screen never.
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default with { TopmostNormal = normal, TopmostMini = mini });
        Assert.True(viewModel.IsNormal);
        Assert.Equal(normal, viewModel.IsTopmost);

        viewModel.ShowMiniCommand.Execute(null);
        Assert.Equal(mini, viewModel.IsTopmost);

        viewModel.ToggleFullScreenCommand.Execute(null);
        Assert.True(viewModel.IsFullScreen);
        Assert.False(viewModel.IsTopmost);

        viewModel.ShowNormalCommand.Execute(null);
        Assert.Equal(normal, viewModel.IsTopmost);

        viewModel.ShowMiniCommand.Execute(null);
        Assert.Equal(mini, viewModel.IsTopmost);
    }

    [Theory]
    [InlineData(CameraWindowStartMode.Normal, false)]
    [InlineData(CameraWindowStartMode.Mini, true)]
    [InlineData(CameraWindowStartMode.FullScreen, false)]
    public void DefaultTopmostIsTodaysBehaviour(CameraWindowStartMode startMode, bool expected) =>
        Assert.Equal(expected, Create(CameraWindowDefaults.Default with { StartMode = startMode }).IsTopmost);

    [Fact]
    public void FullScreenIsNeverTopmostEvenWithBothOptions() =>
        Assert.False(Create(CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.FullScreen, TopmostNormal = true, TopmostMini = true }).IsTopmost);

    [Fact]
    public void ModeChangeRaisesTopmostChanged()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.ShowMiniCommand.Execute(null);

        Assert.Contains(nameof(CameraViewModel.IsTopmost), changed);
        Assert.True(changed.IndexOf(nameof(CameraViewModel.Mode)) < changed.IndexOf(nameof(CameraViewModel.IsTopmost)));
    }

    [Fact]
    public void SavedTopmostOptionAppliesToTheOpenWindowRightAway()
    {
        // T-037: unlike the placement defaults, "always on top" changes the open window after saving.
        _states.Set("mk4", Remembered);
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        Assert.True(viewModel.IsMini);
        Assert.True(viewModel.IsTopmost);
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(CameraWindowDefaults.Default with { TopmostMini = false })] });
        _dispatcher.RunAll();

        Assert.False(viewModel.IsTopmost);
        Assert.Equal([nameof(CameraViewModel.IsTopmost), nameof(CameraViewModel.MiniToolTip)], changed);
        Assert.Equal(CameraWindowMode.Mini, viewModel.Mode);
        Assert.Equal(Remembered.Mini, viewModel.MiniPlacement);
        Assert.Equal(Remembered.Normal, viewModel.NormalPlacement);
    }

    [Fact]
    public void SavedOptionOfAnotherModeDoesNotRaiseTopmostChanged()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        Assert.True(viewModel.IsNormal);
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(CameraWindowDefaults.Default with { TopmostMini = false })] });
        _dispatcher.RunAll();

        Assert.False(viewModel.IsTopmost);
        Assert.DoesNotContain(nameof(CameraViewModel.IsTopmost), changed);

        viewModel.ShowMiniCommand.Execute(null);
        Assert.False(viewModel.IsTopmost);
    }

    [Theory]
    [InlineData(true, "Mini preview, always on top")]
    [InlineData(false, "Mini preview")]
    public void MiniToolTipFollowsTheMiniOption(bool topmostMini, string expected)
    {
        // User decision 2026-10-20 (T-037 Q1): "always on top" only while the mini preview really is.
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default with { TopmostMini = topmostMini, TopmostNormal = !topmostMini });

        Assert.Equal(expected, viewModel.MiniToolTip);
    }

    [Fact]
    public void SavedMiniOptionChangesTheToolTipOfTheOpenWindow()
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        Assert.True(viewModel.IsNormal);
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(CameraWindowDefaults.Default with { TopmostMini = false })] });
        _dispatcher.RunAll();

        Assert.Equal("Mini preview", viewModel.MiniToolTip);
        Assert.Equal([nameof(CameraViewModel.MiniToolTip)], changed);

        // Only the normal window option changed: the tooltip stays and is not announced.
        changed.Clear();
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(CameraWindowDefaults.Default with { TopmostMini = false, TopmostNormal = true })] });
        _dispatcher.RunAll();

        Assert.Equal("Mini preview", viewModel.MiniToolTip);
        Assert.DoesNotContain(nameof(CameraViewModel.MiniToolTip), changed);

        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(CameraWindowDefaults.Default)] });
        _dispatcher.RunAll();

        Assert.Equal("Mini preview, always on top", viewModel.MiniToolTip);
        Assert.Contains(nameof(CameraViewModel.MiniToolTip), changed);
    }

    [Theory]
    [InlineData(true, "Malý náhled vždy navrchu")]
    [InlineData(false, "Malý náhled")]
    public void MiniToolTipFollowsALanguageSwitch(bool topmostMini, string expected)
    {
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default with { TopmostMini = topmostMini });
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        using (new CultureScope("cs-CZ"))
        {
            viewModel.RefreshTexts();

            Assert.Equal(expected, viewModel.MiniToolTip);
        }

        Assert.Contains(nameof(CameraViewModel.MiniToolTip), changed);
    }

    [Fact]
    public void TopmostChangeKeepsTheRememberedState()
    {
        // T-035 Q3: the open window still writes its state back when only "always on top" changed.
        CameraViewModel viewModel = Create(CameraWindowDefaults.Default);
        viewModel.RememberPlacement(CameraWindowMode.Normal, new CameraWindowPlacement(5, 6, 700, 400));
        ((IMonitoringObserver)_printers).ApplySettings(new AppSettings { Printers = [Printer(CameraWindowDefaults.Default with { TopmostNormal = true })] });
        _dispatcher.RunAll();
        Assert.True(viewModel.IsTopmost);

        viewModel.Dispose();

        Assert.Equal(new CameraWindowState(CameraWindowMode.Normal, new CameraWindowPlacement(5, 6, 700, 400), null), _states.Get("mk4"));
    }
}
