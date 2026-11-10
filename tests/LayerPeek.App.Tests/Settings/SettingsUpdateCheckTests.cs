using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Updates;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Updates;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The update check on the Startup and updates tab (T-058): "Check for updates automatically" (Q2 a, on by default, saved
/// with the settings), "Check now" with its result next to it (Q4 a) and the macOS hint to move the app first (Q8 a). T-181:
/// the error of a refused update feed while the refusal is active (decisions 5 and 7), followed on the UI thread.
/// </summary>
public sealed class SettingsUpdateCheckTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private readonly FakeFeedRejections _rejections = new();
    private readonly ManualDispatcher _dispatcher = new();
    private SettingsViewModel? _viewModel;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheOptionShowsTheSavedSetting(bool check)
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { CheckForUpdates = check });

        Assert.Equal(check, vm.CheckForUpdates);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task TurningItOffIsSaved()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings());

        vm.CheckForUpdates = false;
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(Assert.Single(_store.Saves).Updated.CheckForUpdates);
    }

    [Fact]
    public async Task WithoutTheOfferThereIsNoButton()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings());

        Assert.False(vm.ShowsCheckForUpdatesNow);
        Assert.False(vm.CheckForUpdatesNowCommand.CanExecute(null));
        Assert.False(vm.ShowsUpdateCheckResult);
    }

    [Theory]
    [InlineData(UpdateCheckOutcome.UpToDate, "LayerPeek 0.3.1 is up to date.", false)]
    [InlineData(UpdateCheckOutcome.UpdateAvailable, "Version 0.3.2 is available; the tray menu offers it.", false)]
    [InlineData(UpdateCheckOutcome.Failed, "The check failed. Try again later.", true)]
    public async Task CheckNowShowsItsResult(UpdateCheckOutcome outcome, string text, bool isError)
    {
        TaskCompletionSource<UpdateCheckResult> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SettingsViewModel vm = await LoadAsync(new AppSettings(), _ => answer.Task);
        Assert.True(vm.ShowsCheckForUpdatesNow);

        Task click = vm.CheckForUpdatesNowCommand.ExecuteAsync(null);
        Assert.True(vm.IsCheckingForUpdates);
        Assert.Equal("Checking…", vm.UpdateCheckResultText);
        Assert.False(vm.CheckForUpdatesNowCommand.CanExecute(null));

        UpdateRelease? release = outcome == UpdateCheckOutcome.UpdateAvailable
            ? new UpdateRelease(ReleaseVersion.Parse("0.3.2"), UpdateRelease.PageFor(ReleaseVersion.Parse("0.3.2")), IsPrerelease: false)
            : null;
        answer.SetResult(new UpdateCheckResult(outcome, release));
        await click;

        Assert.False(vm.IsCheckingForUpdates);
        Assert.True(vm.ShowsUpdateCheckResult);
        Assert.Equal(text, vm.UpdateCheckResultText);
        Assert.Equal(isError, vm.UpdateCheckResultIsError);
        Assert.True(vm.CheckForUpdatesNowCommand.CanExecute(null));
        // A check is no edit.
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task TheResultFollowsTheLanguage()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings(), _ => Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.UpToDate, null)));
        await vm.CheckForUpdatesNowCommand.ExecuteAsync(null);

        using (new CultureScope("de-DE"))
        {
            vm.RefreshTexts();
            Assert.Equal("LayerPeek 0.3.1 ist aktuell.", vm.UpdateCheckResultText);
        }
    }

    [Fact]
    public async Task ClosingTheWindowCancelsTheWait()
    {
        TaskCompletionSource<UpdateCheckResult> never = new();
        CancellationToken seen = default;
        SettingsViewModel vm = await LoadAsync(new AppSettings(), token =>
        {
            seen = token;
            return never.Task.WaitAsync(token);
        });

        Task click = vm.CheckForUpdatesNowCommand.ExecuteAsync(null);
        vm.Dispose();
        await click;

        Assert.True(seen.IsCancellationRequested);
    }

    [Fact]
    public async Task TheMoveHintIsShownWhenTheCompositionRootSaysSo()
    {
        _store.Data = new SettingsEditorData(new AppSettings(), SettingsLoadStatus.Loaded, [], Ids());
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, productVersion: "0.3.1") { ShowsMoveToApplications = true };
        await _viewModel.LoadAsync();

        Assert.True(_viewModel.ShowsMoveToApplications);
    }

    [Fact]
    public async Task ARefusalActiveWhenTheWindowOpensIsShown()
    {
        _rejections.Current = Refused("0.5.0");
        SettingsViewModel vm = await LoadAsync(new AppSettings());

        Assert.True(vm.ShowsUpdateRejected);
        Assert.True(vm.ShowsUpdateRejectedVersion);
        Assert.Equal("Refused version: 0.5.0", vm.UpdateRejectedVersionText);
        // The tab shows it; no check of this window found it.
        Assert.False(vm.ShowsUpdateCheckResult);
    }

    [Fact]
    public async Task ARefusalFollowsTheServiceOnTheUiThread()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings());
        Assert.False(vm.ShowsUpdateRejected);

        // Raised on the thread of the check: nothing changes until the UI thread runs.
        await Task.Run(() => _rejections.Set(Refused(null)), TestContext.Current.CancellationToken);
        Assert.False(vm.ShowsUpdateRejected);
        _dispatcher.RunAll();
        Assert.True(vm.ShowsUpdateRejected);
        Assert.False(vm.ShowsUpdateRejectedVersion);
        Assert.Null(vm.UpdateRejectedVersionText);

        // A later good check clears it.
        _rejections.Set(null);
        _dispatcher.RunAll();
        Assert.False(vm.ShowsUpdateRejected);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task CheckNowShowsARefusalAsAnError()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings(), _ =>
        {
            // The service reports the refusal before the check returns, like UpdateCheckService.
            _rejections.Set(Refused("0.5.0"));
            return Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.FeedRejected, null, Refused("0.5.0")));
        });

        await vm.CheckForUpdatesNowCommand.ExecuteAsync(null);
        _dispatcher.RunAll();

        Assert.Equal("The update was refused.", vm.UpdateCheckResultText);
        Assert.True(vm.UpdateCheckResultIsError);
        Assert.True(vm.ShowsUpdateRejected);
    }

    [Fact]
    public async Task AResultThatTheRefusalContradictsIsCleared()
    {
        UpdateCheckOutcome outcome = UpdateCheckOutcome.UpToDate;
        SettingsViewModel vm = await LoadAsync(new AppSettings(), _ => Task.FromResult(new UpdateCheckResult(outcome, null)));
        await vm.CheckForUpdatesNowCommand.ExecuteAsync(null);
        Assert.Equal("LayerPeek 0.3.1 is up to date.", vm.UpdateCheckResultText);

        // An automatic check refuses the feed afterwards: "up to date" next to the error would contradict it.
        _rejections.Set(Refused("0.5.0"));
        _dispatcher.RunAll();
        Assert.False(vm.ShowsUpdateCheckResult);
        Assert.True(vm.ShowsUpdateRejected);

        // "Refused" next to no error any more neither; a plain failure says nothing about the feed and stays.
        outcome = UpdateCheckOutcome.FeedRejected;
        await vm.CheckForUpdatesNowCommand.ExecuteAsync(null);
        _rejections.Set(null);
        _dispatcher.RunAll();
        Assert.False(vm.ShowsUpdateCheckResult);
        outcome = UpdateCheckOutcome.Failed;
        await vm.CheckForUpdatesNowCommand.ExecuteAsync(null);
        _rejections.Set(Refused(null));
        _dispatcher.RunAll();
        Assert.Equal("The check failed. Try again later.", vm.UpdateCheckResultText);
    }

    [Fact]
    public async Task TheRefusedVersionFollowsTheLanguage()
    {
        _rejections.Current = Refused("0.5.0");
        SettingsViewModel vm = await LoadAsync(new AppSettings());

        using (new CultureScope("cs-CZ"))
        {
            vm.RefreshTexts();
            Assert.Equal("Odmítnutá verze: 0.5.0", vm.UpdateRejectedVersionText);
        }
    }

    [Fact]
    public async Task AClosedWindowNoLongerFollowsTheService()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings());
        Assert.Equal(1, _rejections.Subscribers);

        vm.Dispose();
        _rejections.Set(Refused("0.5.0"));
        _dispatcher.RunAll();

        Assert.Equal(0, _rejections.Subscribers);
        Assert.False(vm.ShowsUpdateRejected);
    }

    [Fact]
    public async Task TheNotificationOfARefusalOpensTheStartupAndUpdatesTab()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings());

        vm.ShowPage(SettingsPage.StartupUpdates);

        Assert.Equal(SettingsViewModel.StartupUpdatesTab, vm.SelectedTabIndex);
    }

    private static RejectedUpdateFeed Refused(string? version) => FakeFeedRejections.Refused(version);

    private async Task<SettingsViewModel> LoadAsync(AppSettings settings, Func<CancellationToken, Task<UpdateCheckResult>>? check = null)
    {
        _store.Data = new SettingsEditorData(settings, SettingsLoadStatus.Loaded, [], Ids());
        _viewModel?.Dispose();
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, productVersion: "0.3.1", uiDispatcher: _dispatcher)
        {
            CheckForUpdatesNow = check,
            FeedRejections = _rejections,
        };
        await _viewModel.LoadAsync();
        return _viewModel;
    }
}
