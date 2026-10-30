using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Updates;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The update check on the Startup and updates tab (T-058): "Check for updates automatically" (Q2 a, on by default, saved
/// with the settings), "Check now" with its result next to it (Q4 a) and the macOS hint to move the app first (Q8 a).
/// </summary>
public sealed class SettingsUpdateCheckTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
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

    private async Task<SettingsViewModel> LoadAsync(AppSettings settings, Func<CancellationToken, Task<UpdateCheckResult>>? check = null)
    {
        _store.Data = new SettingsEditorData(settings, SettingsLoadStatus.Loaded, [], Ids());
        _viewModel?.Dispose();
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, productVersion: "0.3.1") { CheckForUpdatesNow = check };
        await _viewModel.LoadAsync();
        return _viewModel;
    }
}
