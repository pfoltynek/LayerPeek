using System.Globalization;

using LayerPeek.App.Localization;
using LayerPeek.App.Platform;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Localization;
using LayerPeek.App.Tests.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The warning "notifications are turned off in macOS / Windows" of the General tab with its button to the OS settings
/// (T-020, T-101 user decisions 2026-10-29) and the test notification (T-101), on a stand-in of the OS: no notification is
/// shown, no permission asked and no settings opened in a test. Some tests switch the process-wide language, so the class
/// runs alone (<see cref="GlobalLanguageGroup"/>) and restores the system language.
/// </summary>
[Collection(nameof(GlobalLanguageGroup))]
public sealed class SettingsNotificationPermissionTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly FakeEditorStore _store = new();
    private readonly FakeSystemNotificationSettings _system = new();
    private readonly UiLanguage _language = new();
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private SettingsViewModel? _viewModel;

    public SettingsNotificationPermissionTests() => _language.Apply("en");

    public void Dispose()
    {
        _viewModel?.Dispose();
        // Never leave another language behind for the tests that run next.
        Strings.Culture = null;
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    [Fact]
    public async Task TurnedOffNotificationsShowTheWarningAndTheButtonOpensTheSystemSettings()
    {
        _system.TurnedOff = true;
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true);

        Assert.True(vm.ShowsNotificationsTurnedOff);
        Assert.True(vm.ShowsNotificationWarning);
        Assert.Equal(Strings.SettingsNotificationsTurnedOff, vm.NotificationWarningText);
        Assert.Equal(Strings.SettingsOpenNotificationSettings, vm.OpenNotificationSettingsText);
        // One slot: the button to the OS settings, not the test.
        Assert.False(vm.ShowsTestNotification);
        Assert.False(vm.SendTestNotificationCommand.CanExecute(null));
        Assert.True(vm.OpenNotificationSettingsCommand.CanExecute(null));
        vm.OpenNotificationSettingsCommand.Execute(null);
        Assert.Equal(1, _system.Opened);
        // Saving is not needed and nothing of the settings changed.
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task OnWindowsTheWarningAndTheButtonUseTheTermsOfWindows()
    {
        _system.TurnedOff = true;
        SettingsViewModel vm = await LoadAsync(_system, macTerms: false);

        Assert.Equal(Strings.SettingsNotificationsTurnedOffWindows, vm.NotificationWarningText);
        Assert.Equal(Strings.SettingsOpenNotificationSettingsWindows, vm.OpenNotificationSettingsText);
        Assert.Equal(Strings.SettingsOpenNotificationSettingsHintWindows, vm.OpenNotificationSettingsHint);
    }

    [Fact]
    public async Task APolicyShowsItsWarningWithoutAButtonAndKeepsTheTest()
    {
        _system.State = SystemNotificationState.BlockedByPolicy;
        SettingsViewModel vm = await LoadAsync(_system, macTerms: false);

        Assert.True(vm.ShowsNotificationWarning);
        Assert.True(vm.ShowsNotificationsBlockedByPolicy);
        Assert.Equal(Strings.SettingsTestNotificationBlockedByPolicy, vm.NotificationWarningText);
        // The user cannot change a policy: no button to Settings.
        Assert.False(vm.ShowsNotificationsTurnedOff);
        Assert.False(vm.OpenNotificationSettingsCommand.CanExecute(null));
        Assert.True(vm.ShowsTestNotification);

        await vm.SendTestNotificationCommand.ExecuteAsync(null);
        Assert.False(vm.ShowsTestNotificationResult);
        Assert.Empty(_system.Sent);
    }

    [Fact]
    public async Task AllowedNotificationsShowOnlyTheTestButton()
    {
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true);

        Assert.False(vm.ShowsNotificationsTurnedOff);
        Assert.False(vm.ShowsNotificationWarning);
        Assert.Null(vm.NotificationWarningText);
        Assert.True(vm.ShowsTestNotification);
        Assert.True(vm.SendTestNotificationCommand.CanExecute(null));
        Assert.False(vm.ShowsTestNotificationResult);
        Assert.False(vm.OpenNotificationSettingsCommand.CanExecute(null));
        vm.OpenNotificationSettingsCommand.Execute(null);
        Assert.Equal(0, _system.Opened);
    }

    [Fact]
    public async Task AnUnknownStateShowsNoWarning()
    {
        _system.State = SystemNotificationState.Unknown;
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true);

        Assert.False(vm.ShowsNotificationWarning);
        Assert.True(vm.ShowsTestNotification);
    }

    [Fact]
    public async Task WithoutOsSettingsThereIsNoWarningAndNoTest()
    {
        SettingsViewModel vm = await LoadAsync(null, macTerms: true);

        Assert.False(vm.ShowsNotificationsTurnedOff);
        Assert.False(vm.ShowsTestNotification);
        Assert.False(vm.SendTestNotificationCommand.CanExecute(null));
        await vm.RefreshNotificationPermissionAsync();
        await vm.SendTestNotificationCommand.ExecuteAsync(null);
        Assert.False(vm.ShowsNotificationsTurnedOff);
        Assert.False(vm.ShowsTestNotificationResult);
    }

    [Fact]
    public async Task ComingBackAfterAllowingThemHidesTheWarning()
    {
        _system.TurnedOff = true;
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true);
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _system.TurnedOff = false;
        await vm.RefreshNotificationPermissionAsync();

        Assert.False(vm.ShowsNotificationsTurnedOff);
        Assert.False(vm.OpenNotificationSettingsCommand.CanExecute(null));
        Assert.True(vm.ShowsTestNotification);
        Assert.Contains(nameof(SettingsViewModel.ShowsNotificationsTurnedOff), changed);
        Assert.Contains(nameof(SettingsViewModel.ShowsTestNotification), changed);

        _system.TurnedOff = true;
        await vm.RefreshNotificationPermissionAsync();
        Assert.True(vm.ShowsNotificationsTurnedOff);
        Assert.False(vm.ShowsTestNotification);
    }

    [Fact]
    public async Task AClosedWindowReadsNothingMore()
    {
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true);
        int reads = _system.Reads;
        vm.Dispose();
        _system.TurnedOff = true;

        await vm.RefreshNotificationPermissionAsync();
        await vm.SendTestNotificationCommand.ExecuteAsync(null);

        Assert.Equal(reads, _system.Reads);
        Assert.False(vm.ShowsNotificationsTurnedOff);
        Assert.Empty(_system.Sent);
    }

    [Theory]
    [InlineData(true, "en", "Test notification sent. If you don't see it, check Focus in macOS.")]
    [InlineData(false, "en", "Test notification sent. If you don't see it, check Do not disturb in Windows.")]
    [InlineData(true, "cs", "Zkušební upozornění odesláno. Pokud ho nevidíte, zkontrolujte v macOS režim Soustředění.")]
    [InlineData(false, "de", "Testbenachrichtigung gesendet. Falls sie nicht erscheint, prüfen Sie „Nicht stören“ in Windows.")]
    public async Task AnAllowedTestIsSentAndSaysWhereToLookIfItIsNotSeen(bool macTerms, string language, string expected)
    {
        _language.Apply(language);
        SettingsViewModel vm = await LoadAsync(_system, macTerms);

        await vm.SendTestNotificationCommand.ExecuteAsync(null);

        Assert.Single(_system.Sent);
        Assert.Equal(0, _system.Requests);
        Assert.True(vm.ShowsTestNotificationResult);
        Assert.False(vm.TestNotificationResultIsError);
        Assert.Equal(expected, vm.TestNotificationResult);
        Assert.True(vm.SendTestNotificationCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(NotificationSound.Off, false)]
    [InlineData(NotificationSound.ImportantOnly, true)]
    [InlineData(NotificationSound.All, true)]
    public async Task TheTestPlaysTheSoundUnlessTheSoundShownInTheWindowIsOff(NotificationSound saved, bool sound)
    {
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true, AppSettings.Default with { Notifications = AppSettings.Default.Notifications with { Sound = saved } });

        await vm.SendTestNotificationCommand.ExecuteAsync(null);
        Assert.Equal([sound], _system.Sent);

        // The choice shown in the window counts, saved or not (user decision 2026-10-29).
        vm.SelectedSound = vm.SoundOptions.Single(option => option.Value == (saved == NotificationSound.Off ? NotificationSound.All : NotificationSound.Off));
        await vm.SendTestNotificationCommand.ExecuteAsync(null);
        Assert.Equal([sound, !sound], _system.Sent);
    }

    [Fact]
    public async Task TheTestStaysAvailableWhenNotificationsAreSwitchedOffInTheWindow()
    {
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true);
        vm.NotificationsEnabled = false;

        Assert.True(vm.ShowsTestNotification);
        Assert.True(vm.SendTestNotificationCommand.CanExecute(null));
        await vm.SendTestNotificationCommand.ExecuteAsync(null);
        Assert.Single(_system.Sent);
    }

    [Fact]
    public async Task NotAskedYetMacOSAsksWhileTheWindowWaitsThenTheTestIsSent()
    {
        _system.State = SystemNotificationState.NotAsked;
        _system.AnswerGate = new TaskCompletionSource();
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true);

        Task running = vm.SendTestNotificationCommand.ExecuteAsync(null);
        await Waiting.ForAsync(() => _system.Requests == 1);

        // The window is not blocked: it says what it waits for, and the button is disabled meanwhile.
        Assert.True(vm.IsSendingTestNotification);
        Assert.False(vm.SendTestNotificationCommand.CanExecute(null));
        Assert.Equal(Strings.SettingsTestNotificationAsking, vm.TestNotificationResult);
        Assert.False(vm.TestNotificationResultIsError);

        _system.AnswerGate.SetResult();
        await running.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Single(_system.Sent);
        Assert.Equal(Strings.SettingsTestNotificationSentMac, vm.TestNotificationResult);
        Assert.False(vm.IsSendingTestNotification);
        Assert.False(vm.ShowsNotificationWarning);
    }

    [Fact]
    public async Task DeniedAtTheQuestionTheWarningWithTheButtonIsTheResult()
    {
        _system.State = SystemNotificationState.NotAsked;
        _system.Grants = false;
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true);

        await vm.SendTestNotificationCommand.ExecuteAsync(null);

        Assert.Equal(1, _system.Requests);
        Assert.Empty(_system.Sent);
        Assert.True(vm.ShowsNotificationsTurnedOff);
        Assert.True(vm.ShowsNotificationWarning);
        Assert.True(vm.OpenNotificationSettingsCommand.CanExecute(null));
        // No further text: the warning is the result, and the test button left the slot.
        Assert.False(vm.ShowsTestNotificationResult);
        Assert.Null(vm.TestNotificationResult);
        Assert.False(vm.ShowsTestNotification);
    }

    [Fact]
    public async Task AQuestionThatFailedWithoutADenialIsAFailure()
    {
        _system.State = SystemNotificationState.NotAsked;
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true);
        // E.g. the user did not answer in time: macOS still has not decided.
        _system.AnswerGate = new TaskCompletionSource();
        _system.AnswerGate.SetCanceled(TestContext.Current.CancellationToken);

        await vm.SendTestNotificationCommand.ExecuteAsync(null);

        Assert.Empty(_system.Sent);
        Assert.Equal(Strings.SettingsTestNotificationFailed, vm.TestNotificationResult);
        Assert.True(vm.TestNotificationResultIsError);
        Assert.False(vm.ShowsNotificationWarning);
    }

    [Theory]
    [InlineData((int)TestNotificationOutcome.TurnedOff)]
    [InlineData((int)TestNotificationOutcome.BlockedByPolicy)]
    public async Task ATestTheOSRefusesShowsTheWarningInsteadOfAResult(int refused)
    {
        TestNotificationOutcome outcome = (TestNotificationOutcome)refused;
        SettingsViewModel vm = await LoadAsync(_system, macTerms: false);
        _system.Outcome = outcome;

        await vm.SendTestNotificationCommand.ExecuteAsync(null);

        Assert.True(vm.ShowsNotificationWarning);
        Assert.Equal(outcome == TestNotificationOutcome.TurnedOff, vm.ShowsNotificationsTurnedOff);
        Assert.False(vm.ShowsTestNotificationResult);
    }

    [Fact]
    public async Task AFailedTestIsShownInRedUntilTheNextTest()
    {
        SettingsViewModel vm = await LoadAsync(_system, macTerms: false);
        _system.Outcome = TestNotificationOutcome.Failed;

        await vm.SendTestNotificationCommand.ExecuteAsync(null);
        Assert.Equal(Strings.SettingsTestNotificationFailed, vm.TestNotificationResult);
        Assert.True(vm.TestNotificationResultIsError);

        // The next click clears it right away, then shows its own result.
        _system.Outcome = TestNotificationOutcome.Sent;
        _system.SendGate = new TaskCompletionSource();
        Task running = vm.SendTestNotificationCommand.ExecuteAsync(null);
        Assert.False(vm.ShowsTestNotificationResult);
        Assert.Null(vm.TestNotificationResult);
        _system.SendGate.SetResult();
        await running.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        Assert.Equal(Strings.SettingsTestNotificationSentWindows, vm.TestNotificationResult);
        Assert.False(vm.TestNotificationResultIsError);
    }

    [Fact]
    public async Task AWarningThatAppearsLaterClearsTheResult()
    {
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true);
        await vm.SendTestNotificationCommand.ExecuteAsync(null);
        Assert.True(vm.ShowsTestNotificationResult);

        _system.TurnedOff = true;
        await vm.RefreshNotificationPermissionAsync();
        Assert.False(vm.ShowsTestNotificationResult);

        // Allowed again: the old result does not come back.
        _system.TurnedOff = false;
        await vm.RefreshNotificationPermissionAsync();
        Assert.False(vm.ShowsTestNotificationResult);
    }

    [Fact]
    public async Task TheResultAndTheWarningAreTranslatedWhenTheLanguageChanges()
    {
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true);
        await vm.SendTestNotificationCommand.ExecuteAsync(null);
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _language.Apply("fr");
        vm.RefreshTexts();

        Assert.Equal("Notification de test envoyée. Si elle n'apparaît pas, vérifiez « Concentration » dans macOS.", vm.TestNotificationResult);
        Assert.Contains(nameof(SettingsViewModel.TestNotificationResult), changed);

        _system.TurnedOff = true;
        await vm.RefreshNotificationPermissionAsync();
        _language.Apply("pl");
        vm.RefreshTexts();
        Assert.Equal("Powiadomienia są wyłączone w macOS.", vm.NotificationWarningText);
        Assert.Contains(nameof(SettingsViewModel.NotificationWarningText), changed);
        Assert.Contains(nameof(SettingsViewModel.OpenNotificationSettingsText), changed);
    }

    [Fact]
    public async Task ClosingTheWindowWhileMacOSAsksEndsTheWaitQuietly()
    {
        _system.State = SystemNotificationState.NotAsked;
        _system.AnswerGate = new TaskCompletionSource();
        SettingsViewModel vm = await LoadAsync(_system, macTerms: true);
        Task running = vm.SendTestNotificationCommand.ExecuteAsync(null);
        await Waiting.ForAsync(() => _system.Requests == 1);

        vm.Dispose();
        await running.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Empty(_system.Sent);
        Assert.Equal(0, _system.Opened);
    }

    [Fact]
    public async Task AReadOvertakenByTheTestIsDiscarded()
    {
        DelayedReads delayed = new(_system);
        SettingsViewModel vm = await LoadAsync(delayed, macTerms: true);

        // A read started on activation, still waiting for the OS, while the test learns that macOS turned them off.
        delayed.Hold = true;
        Task refresh = vm.RefreshNotificationPermissionAsync();
        delayed.Hold = false;
        _system.Outcome = TestNotificationOutcome.TurnedOff;
        await vm.SendTestNotificationCommand.ExecuteAsync(null);
        Assert.True(vm.ShowsNotificationsTurnedOff);

        delayed.Release(SystemNotificationState.Allowed);
        await refresh.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        Assert.True(vm.ShowsNotificationsTurnedOff);
    }

    private async Task<SettingsViewModel> LoadAsync(ISystemNotificationSettings? system, bool macTerms, AppSettings? settings = null)
    {
        _store.Data = new SettingsEditorData(settings ?? new AppSettings(), SettingsLoadStatus.Loaded, [], Ids());
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, static () => Guid.NewGuid().ToString("N"), productVersion: "0.1.0", notificationSettings: system)
        {
            UsesMacTerms = macTerms,
        };
        await _viewModel.LoadAsync();
        return _viewModel;
    }

    /// <summary>The fake, with one read held until the test releases it.</summary>
    private sealed class DelayedReads(FakeSystemNotificationSettings inner) : ISystemNotificationSettings
    {
        private TaskCompletionSource<SystemNotificationState>? _held;

        public bool Hold { get; set; }

        public void Release(SystemNotificationState state) => _held?.SetResult(state);

        public Task<SystemNotificationState> ReadStateAsync(CancellationToken cancellationToken)
        {
            if (!Hold)
            {
                return inner.ReadStateAsync(cancellationToken);
            }

            _held = new TaskCompletionSource<SystemNotificationState>();
            return _held.Task;
        }

        public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken) => inner.RequestPermissionAsync(cancellationToken);

        public Task<TestNotificationOutcome> SendTestAsync(bool playSound, CancellationToken cancellationToken) => inner.SendTestAsync(playSound, cancellationToken);

        public void Open() => inner.Open();
    }
}
