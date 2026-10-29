using LayerPeek.App.Platform;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Installer;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// "Start after logon" and "Offer pre-release versions" of the settings window (T-017, user decisions 2026-10-24).
/// Autostart is the state of the OS (the app's shortcut in the Startup folder), off by default, changed only by Save. The
/// pre-release choice is saved only once the user changed it; until then it follows the running version (0.x on).
/// </summary>
public sealed class SettingsStartupAndUpdatesTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly FakeEditorStore _store = new();
    private readonly FakeAutostart _autostart = new();
    private SettingsViewModel? _viewModel;

    public void Dispose() => _viewModel?.Dispose();

    [Fact]
    public async Task WithoutAutostartTheOptionIsHidden()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart: null);

        Assert.False(vm.IsAutostartAvailable);
        Assert.False(vm.StartWithWindows);
    }

    /// <summary>User decision 2026-10-24: a <c>.zip</c> or a development build shows the option disabled with the reason.</summary>
    [Fact]
    public async Task WithoutAnInstallationTheOptionIsDisabledWithTheReason()
    {
        _store.Data = new SettingsEditorData(new AppSettings(), SettingsLoadStatus.Loaded, [], Ids());
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, static () => Guid.NewGuid().ToString("N"), autostart: null, autostartNeedsInstallation: true, productVersion: "0.1.0");
        await _viewModel.LoadAsync();

        Assert.True(_viewModel.IsAutostartShown);
        Assert.False(_viewModel.IsAutostartAvailable);
        Assert.True(_viewModel.ShowsAutostartNeedsInstallation);
        _viewModel.StartWithWindows = true;
        Assert.False(_viewModel.StartWithWindows);
        Assert.False(_viewModel.IsDirty);

        // User decision 2026-10-24: "Offer pre-release versions" is disabled too, with the same reason.
        Assert.False(_viewModel.IsPrereleaseOptionAvailable);
        _viewModel.OfferPrereleaseUpdates = false;
        Assert.True(_viewModel.OfferPrereleaseUpdates);
        Assert.False(_viewModel.IsDirty);

        // Screen readers: a disabled check box cannot be focused, so the reason is its help text.
        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsNeedsInstallation, _viewModel.AutostartHelpText);
        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsNeedsInstallation, _viewModel.PrereleaseHelpText);
    }

    /// <summary>
    /// T-019 (user decision 2026-10-29): on macOS the option is "Open at Login", disabled outside LayerPeek.app with its own
    /// reason, and the tab has no pre-release option until macOS has updates.
    /// </summary>
    [Fact]
    public async Task OnMacOSTheOptionUsesTheTermsOfMacOS()
    {
        _store.Data = new SettingsEditorData(new AppSettings(), SettingsLoadStatus.Loaded, [], Ids());
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, static () => Guid.NewGuid().ToString("N"), autostart: null, autostartNeedsInstallation: true, productVersion: "0.1.0")
        {
            UsesMacTerms = true,
            ShowsPrereleaseOption = false,
        };
        await _viewModel.LoadAsync();

        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsOpenAtLogin, _viewModel.AutostartLabel);
        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsNeedsAppBundle, _viewModel.AutostartNeedsInstallationText);
        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsNeedsAppBundle, _viewModel.AutostartHelpText);
        Assert.True(_viewModel.IsAutostartShown);
        Assert.False(_viewModel.ShowsPrereleaseOption);
    }

    [Fact]
    public async Task OnWindowsTheOptionKeepsItsTexts()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart: _autostart);

        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsStartWithWindows, vm.AutostartLabel);
        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsNeedsInstallation, vm.AutostartNeedsInstallationText);
        Assert.True(vm.ShowsPrereleaseOption);
    }

    /// <summary>The composition root chooses the terms by OS.</summary>
    [Fact]
    public async Task TheCompositionRootChoosesTheTermsByOS()
    {
        await using Microsoft.Extensions.DependencyInjection.ServiceProvider services = LayerPeek.App.Composition.AppServices.Create();
        using SettingsViewModel vm = LayerPeek.App.Composition.AppServices.CreateSettingsViewModel(services);

        Assert.Equal(OperatingSystem.IsMacOS(), vm.UsesMacTerms);
        Assert.Equal(!OperatingSystem.IsMacOS(), vm.ShowsPrereleaseOption);
    }

    [Fact]
    public async Task InTheInstalledAppTheHelpTextsAreTheHints()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings());

        Assert.True(vm.IsPrereleaseOptionAvailable);
        Assert.Null(vm.AutostartHelpText);
        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsOfferPrereleaseUpdatesHint, vm.PrereleaseHelpText);
    }

    [Fact]
    public async Task TheHelpTextsFollowTheLanguage()
    {
        _store.Data = new SettingsEditorData(new AppSettings(), SettingsLoadStatus.Loaded, [], Ids());
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, static () => Guid.NewGuid().ToString("N"), autostart: null, autostartNeedsInstallation: true, productVersion: "0.1.0");
        await _viewModel.LoadAsync();
        List<string?> changes = [];
        _viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _viewModel.RefreshTexts();

        Assert.Contains(nameof(SettingsViewModel.AutostartHelpText), changes);
        Assert.Contains(nameof(SettingsViewModel.PrereleaseHelpText), changes);
    }

    [Fact]
    public async Task AnAvailableAutostartNeedsNoReason()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings());

        Assert.True(vm.IsAutostartShown);
        Assert.False(vm.ShowsAutostartNeedsInstallation);
    }

    [Fact]
    public async Task WithoutAutostartAndWithoutTheReasonTheOptionIsHidden()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart: null);

        Assert.False(vm.IsAutostartShown);
        Assert.False(vm.ShowsAutostartNeedsInstallation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadShowsWhetherTheAppStartsAfterLogon(bool enabled)
    {
        _autostart.Enabled = enabled;

        SettingsViewModel vm = await LoadAsync(new AppSettings());

        Assert.True(vm.IsAutostartAvailable);
        Assert.Equal(enabled, vm.StartWithWindows);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task AnUnreadableAutostartShowsOff()
    {
        _autostart.ReadFailure = new IOException("damaged");

        SettingsViewModel vm = await LoadAsync(new AppSettings());

        Assert.False(vm.StartWithWindows);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task SwitchingAutostartOnIsAppliedBySaveOnly()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings());

        vm.StartWithWindows = true;

        Assert.True(vm.IsDirty);
        Assert.Empty(_autostart.Changes);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal([true], _autostart.Changes);
        // Nothing else changed: the settings file is not written.
        Assert.Empty(_store.Saves);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task SwitchingAutostartOffIsApplied()
    {
        _autostart.Enabled = true;
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });

        vm.StartWithWindows = false;
        vm.DebugLogging = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal([false], _autostart.Changes);
        Assert.True(Assert.Single(_store.Saves).Updated.DebugLogging);
    }

    [Fact]
    public async Task AnUnchangedAutostartIsNotWritten()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings());

        vm.StartWithWindows = true;
        vm.StartWithWindows = false;
        vm.DebugLogging = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_autostart.Changes);
    }

    [Fact]
    public async Task CancelLeavesAutostartAlone()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings());

        vm.StartWithWindows = true;
        vm.CancelCommand.Execute(null);

        Assert.Empty(_autostart.Changes);
    }

    [Fact]
    public async Task AFailedAutostartKeepsTheWindowOpenWithAWarning()
    {
        _autostart.Failure = new UnauthorizedAccessException("denied");
        SettingsViewModel vm = await LoadAsync(new AppSettings());
        bool closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.StartWithWindows = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.True(vm.HasStatus);
        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsSavedAutostartFailed, vm.StatusText);
        // Still to do: the next Save tries again.
        Assert.True(vm.IsDirty);
    }

    /// <summary>
    /// macOS (T-019, user decisions 2026-10-29): the user switched LayerPeek off in System Settings &gt; General &gt; Login
    /// Items (the Login Item needs approval). Opening the window says so on the Startup and updates tab and offers to open
    /// System Settings; the offer opens them (a fake: tests never open the real ones).
    /// </summary>
    [Fact]
    public async Task AnAutostartTheUserHasToAllowShowsTheHintWhenLoading()
    {
        ApprovalAutostart autostart = new() { RequiresApproval = true };
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart);

        Assert.True(vm.ShowsAutostartApproval);
        Assert.True(vm.OpenAutostartApprovalSettingsCommand.CanExecute(null));
        Assert.False(vm.StartWithWindows);
        Assert.False(vm.HasStatus);
        Assert.False(vm.IsDirty);

        vm.OpenAutostartApprovalSettingsCommand.Execute(null);
        vm.OpenAutostartApprovalSettingsCommand.Execute(null);

        Assert.Equal(2, autostart.Opened);
        // The hint follows the state of the OS, not the language.
        vm.RefreshTexts();
        Assert.True(vm.ShowsAutostartApproval);
    }

    [Fact]
    public async Task AnAllowedAutostartShowsNoHint()
    {
        ApprovalAutostart autostart = new() { Enabled = true };
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart);

        Assert.False(vm.ShowsAutostartApproval);
        Assert.False(vm.OpenAutostartApprovalSettingsCommand.CanExecute(null));
        vm.OpenAutostartApprovalSettingsCommand.Execute(null);
        Assert.Equal(0, autostart.Opened);
        Assert.Equal(1, autostart.ApprovalReads);
    }

    /// <summary>
    /// Save could not switch it on because only the user can allow it (user decision 2026-10-29): a macOS status of its own
    /// instead of the shared "could not be changed", and the hint on the tab.
    /// </summary>
    [Fact]
    public async Task ASwitchTheUserHasToAllowSaysSoAndShowsTheHint()
    {
        ApprovalAutostart autostart = new() { Failure = new AutostartNeedsApprovalException("requires approval") };
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart);
        Assert.False(vm.ShowsAutostartApproval);

        vm.StartWithWindows = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsSavedOpenAtLoginNeedsApproval, vm.StatusText);
        Assert.False(vm.StatusIsError);
        Assert.True(vm.ShowsAutostartApproval);
        Assert.True(vm.OpenAutostartApprovalSettingsCommand.CanExecute(null));
        Assert.True(vm.IsDirty);
        vm.RefreshTexts();
        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsSavedOpenAtLoginNeedsApproval, vm.StatusText);
    }

    /// <summary>
    /// The user allowed LayerPeek in System Settings and came back (the window becomes active): the hint goes and the option
    /// the user has not changed shows the state of the OS, without a change to save.
    /// </summary>
    [Fact]
    public async Task ComingBackAfterAllowingHidesTheHintAndShowsTheOption()
    {
        ApprovalAutostart autostart = new() { RequiresApproval = true };
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart);
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        autostart.RequiresApproval = false;
        autostart.Enabled = true;
        await vm.RefreshAutostartApprovalAsync();

        Assert.False(vm.ShowsAutostartApproval);
        Assert.False(vm.OpenAutostartApprovalSettingsCommand.CanExecute(null));
        Assert.True(vm.StartWithWindows);
        Assert.Contains(nameof(SettingsViewModel.StartWithWindows), changed);
        Assert.False(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(autostart.Changes);
    }

    /// <summary>Switched off there again (needs approval): the hint comes back on the next activation.</summary>
    [Fact]
    public async Task ComingBackAfterSwitchingOffShowsTheHint()
    {
        ApprovalAutostart autostart = new() { Enabled = true };
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart);
        Assert.True(vm.StartWithWindows);

        autostart.Enabled = false;
        autostart.RequiresApproval = true;
        await vm.RefreshAutostartApprovalAsync();

        Assert.True(vm.ShowsAutostartApproval);
        Assert.False(vm.StartWithWindows);
        Assert.False(vm.IsDirty);
    }

    /// <summary>An option the user changed in the window stays as they set it; Save switches it.</summary>
    [Fact]
    public async Task AnEditedOptionIsLeftToSave()
    {
        ApprovalAutostart autostart = new() { RequiresApproval = true };
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart);
        vm.StartWithWindows = true;

        autostart.RequiresApproval = false;
        autostart.Enabled = true;
        await vm.RefreshAutostartApprovalAsync();

        Assert.False(vm.ShowsAutostartApproval);
        Assert.True(vm.StartWithWindows);
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal([true], autostart.Changes);
    }

    /// <summary>A state that cannot be read changes nothing of the option; the hint shows what macOS reported.</summary>
    [Fact]
    public async Task AnUnreadableStateLeavesTheOption()
    {
        ApprovalAutostart autostart = new() { Enabled = true };
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart);

        autostart.ReadFailure = new InvalidOperationException("XPC failed");
        autostart.RequiresApproval = true;
        await vm.RefreshAutostartApprovalAsync();

        Assert.True(vm.ShowsAutostartApproval);
        Assert.True(vm.StartWithWindows);
        Assert.False(vm.IsDirty);
    }

    /// <summary>Allowed in System Settings meanwhile: the next Save switches it on, the hint goes.</summary>
    [Fact]
    public async Task ASwitchAfterAllowingHidesTheHint()
    {
        ApprovalAutostart autostart = new() { Failure = new AutostartNeedsApprovalException("requires approval") };
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart);
        bool closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        vm.StartWithWindows = true;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.ShowsAutostartApproval);

        autostart.Failure = null;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.False(vm.HasStatus);
        Assert.False(vm.ShowsAutostartApproval);
        Assert.False(vm.OpenAutostartApprovalSettingsCommand.CanExecute(null));
        vm.OpenAutostartApprovalSettingsCommand.Execute(null);
        Assert.Equal(0, autostart.Opened);
        Assert.Equal([true], autostart.Changes);
    }

    /// <summary>Switching off an item that needs approval removes it: nothing left to allow.</summary>
    [Fact]
    public async Task SwitchingOffAfterTheHintHidesIt()
    {
        ApprovalAutostart autostart = new() { Enabled = true };
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart);
        autostart.RequiresApproval = true;
        await vm.RefreshAutostartApprovalAsync();
        Assert.True(vm.ShowsAutostartApproval);
        // The OS still reports it as on for the option (a fake); the user switches it off.
        vm.StartWithWindows = false;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.ShowsAutostartApproval);
        Assert.Equal([false], autostart.Changes);
    }

    [Fact]
    public async Task AnotherFailureKeepsTheSharedStatus()
    {
        ApprovalAutostart autostart = new() { Failure = new UnauthorizedAccessException("denied") };
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart);

        vm.StartWithWindows = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsSavedAutostartFailed, vm.StatusText);
        Assert.False(vm.ShowsAutostartApproval);
    }

    /// <summary>Windows: an autostart without settings to open keeps the shared status and never shows the hint.</summary>
    [Fact]
    public async Task AnAutostartWithoutApprovalSettingsOffersNone()
    {
        _autostart.Failure = new AutostartNeedsApprovalException("requires approval");
        SettingsViewModel vm = await LoadAsync(new AppSettings());

        vm.StartWithWindows = true;
        await vm.SaveCommand.ExecuteAsync(null);
        await vm.RefreshAutostartApprovalAsync();

        Assert.Equal(LayerPeek.App.Resources.Strings.SettingsSavedAutostartFailed, vm.StatusText);
        Assert.False(vm.ShowsAutostartApproval);
    }

    /// <summary>The hint belongs to the state of the OS: another status (here a validation error) keeps it.</summary>
    [Fact]
    public async Task ANewStatusKeepsTheHint()
    {
        ApprovalAutostart autostart = new() { Failure = new AutostartNeedsApprovalException("requires approval") };
        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart);
        vm.StartWithWindows = true;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.ShowsAutostartApproval);

        vm.RemainingTimeMinutes = -1;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.StatusIsError);
        Assert.True(vm.ShowsAutostartApproval);
    }

    /// <summary>Nothing is read before loading, while Save switches the option, or after the window closed.</summary>
    [Fact]
    public async Task TheStateIsReadOnlyWhileTheWindowCanShowIt()
    {
        ApprovalAutostart autostart = new() { RequiresApproval = true };
        _store.Data = new SettingsEditorData(new AppSettings(), SettingsLoadStatus.Loaded, [], Ids());
        using SettingsViewModel notLoaded = new(_store, new FakeConnectionTester(), Supported, autostart: autostart, productVersion: "0.1.0");
        await notLoaded.RefreshAutostartApprovalAsync();
        Assert.Equal(0, autostart.ApprovalReads);
        Assert.False(notLoaded.ShowsAutostartApproval);

        SettingsViewModel vm = await LoadAsync(new AppSettings(), autostart);
        Assert.Equal(1, autostart.ApprovalReads);
        using ManualResetEventSlim release = new();
        using ManualResetEventSlim switching = new();
        autostart.WhileSwitching = () =>
        {
            switching.Set();
            release.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        };
        vm.StartWithWindows = true;
        Task save = vm.SaveCommand.ExecuteAsync(null);
        Assert.True(switching.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        await vm.RefreshAutostartApprovalAsync();
        Assert.Equal(1, autostart.ApprovalReads);
        release.Set();
        await save;

        vm.Dispose();
        await vm.RefreshAutostartApprovalAsync();
        Assert.Equal(1, autostart.ApprovalReads);
    }

    [Fact]
    public async Task AFailedSettingsSaveDoesNotChangeAutostart()
    {
        _store.SaveResult = new SettingsSaveResult(SettingsSaveStatus.SettingsNotSaved);
        SettingsViewModel vm = await LoadAsync(new AppSettings());

        vm.StartWithWindows = true;
        vm.DebugLogging = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_autostart.Changes);
        Assert.True(vm.StatusIsError);
    }

    [Theory]
    [InlineData("0.1.0", true)]
    [InlineData("0.9.3", true)]
    [InlineData("1.0.0", false)]
    [InlineData("2.4.0", false)]
    public async Task WithoutAChoicePrereleasesFollowTheRunningVersion(string version, bool expected)
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings(), version: version);

        Assert.Equal(expected, vm.OfferPrereleaseUpdates);
        Assert.False(vm.IsDirty);
    }

    [Theory]
    [InlineData("0.1.0", true)]
    [InlineData("0.1.0", false)]
    [InlineData("1.0.0", true)]
    [InlineData("1.0.0", false)]
    public async Task ASavedChoiceAlwaysWins(string version, bool choice)
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { OfferPrereleaseUpdates = choice }, version: version);

        Assert.Equal(choice, vm.OfferPrereleaseUpdates);
    }

    [Fact]
    public async Task ChangingThePrereleaseOptionSavesTheChoice()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings(), version: "0.1.0");

        vm.OfferPrereleaseUpdates = false;
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        SettingsChange change = Assert.Single(_store.Saves);
        Assert.Null(change.Original.OfferPrereleaseUpdates);
        Assert.False(change.Updated.OfferPrereleaseUpdates);
    }

    /// <summary>Switching it back before saving is no choice: the default keeps following the version (1.0 then switches it off).</summary>
    [Fact]
    public async Task SwitchingThePrereleaseOptionBackKeepsItUnset()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] }, version: "0.1.0");

        vm.OfferPrereleaseUpdates = false;
        vm.OfferPrereleaseUpdates = true;
        Assert.False(vm.IsDirty);
        vm.DebugLogging = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(_store.Saves).Updated.OfferPrereleaseUpdates);
    }

    /// <summary>A saved choice stays a choice, also when it equals the default of the version.</summary>
    [Fact]
    public async Task ASavedChoiceStaysExplicit()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { OfferPrereleaseUpdates = true, Printers = [LinkPrinter("a", "Alpha")] }, version: "0.1.0");

        vm.DebugLogging = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(_store.Saves).Updated.OfferPrereleaseUpdates);
    }

    private async Task<SettingsViewModel> LoadAsync(AppSettings settings, string version = "0.1.0", bool withAutostart = true) =>
        await LoadAsync(settings, withAutostart ? _autostart : null, version);

    private async Task<SettingsViewModel> LoadAsync(AppSettings settings, IAutostart? autostart, string version = "0.1.0")
    {
        _store.Data = new SettingsEditorData(settings, SettingsLoadStatus.Loaded, [], Ids([.. settings.Printers.Select(static p => p.Id)]));
        _viewModel?.Dispose();
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, static () => Guid.NewGuid().ToString("N"), autostart: autostart, productVersion: version);
        await _viewModel.LoadAsync();
        return _viewModel;
    }
}

/// <summary>
/// An autostart the user allows in the settings of the OS (macOS, T-019), in memory: whether it needs approval is set by the
/// test, opening the settings is only counted.
/// </summary>
internal sealed class ApprovalAutostart : IAutostart, IAutostartApproval
{
    public bool Enabled { get; set; }

    public bool RequiresApproval { get; set; }

    public Exception? Failure { get; set; }

    public Exception? ReadFailure { get; set; }

    /// <summary>Runs inside <see cref="SetEnabled"/> (a switch still running).</summary>
    public Action? WhileSwitching { get; set; }

    public List<bool> Changes { get; } = [];

    public int Opened { get; private set; }

    public int ApprovalReads { get; private set; }

    public bool IsEnabled() => ReadFailure is not null ? throw ReadFailure : Enabled;

    public void SetEnabled(bool enabled)
    {
        WhileSwitching?.Invoke();
        if (Failure is not null)
        {
            throw Failure;
        }

        Changes.Add(enabled);
        Enabled = enabled;
        RequiresApproval = false;
    }

    public bool NeedsApproval()
    {
        ApprovalReads++;
        return RequiresApproval;
    }

    public void OpenApprovalSettings() => Opened++;
}
