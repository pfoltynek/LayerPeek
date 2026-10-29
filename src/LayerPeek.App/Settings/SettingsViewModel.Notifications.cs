using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Platform;
using LayerPeek.App.Resources;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Settings;

/// <summary>
/// The OS side of the notifications on the General tab: the warning that the OS turned them off with the button that opens
/// its settings (T-020 macOS, T-101 Windows), and the test notification (T-101). One slot in the heading row of the section
/// holds either the test button or (turned off) the button to the OS settings; the text next to it is either the warning or
/// the result of the last test (the warning wins: when it appears, the result goes).
/// </summary>
public sealed partial class SettingsViewModel
{
    private SystemNotificationState _notificationState = SystemNotificationState.Unknown;
    private TestNotificationResultKind _testNotificationResult;
    private bool _isSendingTestNotification;

    // Every read of the state and every state the test learnt counts up; a read overtaken meanwhile is discarded.
    private int _notificationStateVersion;

    private enum TestNotificationResultKind
    {
        None,

        /// <summary>macOS shows its question whether LayerPeek may send notifications.</summary>
        Asking,

        Sent,

        Failed,
    }

    /// <summary>
    /// The OS turned LayerPeek's notifications off (macOS: System Settings, T-020; Windows: Settings, T-101, user decision
    /// 2026-10-29): the warning and the button that opens the OS settings (<see cref="OpenNotificationSettingsCommand"/>)
    /// take the slot of the test button. Read when the window loads and whenever it becomes active again
    /// (<see cref="RefreshNotificationPermissionAsync"/>), and by the test.
    /// </summary>
    public bool ShowsNotificationsTurnedOff => _notificationState == SystemNotificationState.TurnedOff;

    /// <summary>Windows (T-101): a policy of the organization turned the notifications off; a warning without a button.</summary>
    public bool ShowsNotificationsBlockedByPolicy => _notificationState == SystemNotificationState.BlockedByPolicy;

    /// <summary>The warning next to the heading of the section (<see cref="NotificationWarningText"/>) is shown.</summary>
    public bool ShowsNotificationWarning => ShowsNotificationsTurnedOff || ShowsNotificationsBlockedByPolicy;

    /// <summary>The warning: turned off in macOS / Windows, or by a policy; <see langword="null"/> without one.</summary>
    public string? NotificationWarningText => _notificationState switch
    {
        SystemNotificationState.TurnedOff => UsesMacTerms ? Strings.SettingsNotificationsTurnedOff : Strings.SettingsNotificationsTurnedOffWindows,
        SystemNotificationState.BlockedByPolicy => Strings.SettingsTestNotificationBlockedByPolicy,
        _ => null,
    };

    /// <summary>The button to the OS settings: System Settings &gt; Notifications (macOS) or Settings &gt; System &gt; Notifications.</summary>
    public string OpenNotificationSettingsText => UsesMacTerms ? Strings.SettingsOpenNotificationSettings : Strings.SettingsOpenNotificationSettingsWindows;

    /// <summary>Tooltip and screen reader help of the button to the OS settings.</summary>
    public string OpenNotificationSettingsHint => UsesMacTerms ? Strings.SettingsOpenNotificationSettingsHint : Strings.SettingsOpenNotificationSettingsHintWindows;

    /// <summary>Opens the OS settings of the app's notifications (<see cref="ISystemNotificationSettings.Open"/>).</summary>
    public IRelayCommand OpenNotificationSettingsCommand { get; }

    /// <summary>
    /// The test button is in the slot (T-101): the OS has notifications for this run and has not turned them off. Also while
    /// "Show notifications" is off (user decision 2026-10-29: the test checks the OS, not the choice of events).
    /// </summary>
    public bool ShowsTestNotification => _notificationSettings is not null && !ShowsNotificationsTurnedOff;

    /// <summary>
    /// Sends the test notification (T-101): macOS asks for permission first when it has not asked yet; with the sound when
    /// the Sound option shown in the window (saved or not, user decision 2026-10-29) is not Off. Disabled while it runs.
    /// </summary>
    public IAsyncRelayCommand SendTestNotificationCommand { get; }

    /// <summary>The test is running (waiting for the OS or for the user's answer to macOS).</summary>
    public bool IsSendingTestNotification
    {
        get => _isSendingTestNotification;
        private set
        {
            if (SetProperty(ref _isSendingTestNotification, value))
            {
                SendTestNotificationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// The result of the last test: waiting for the answer to macOS, sent (with the hint about Focus / Do not disturb) or
    /// failed; <see langword="null"/> before the first test, while a warning is shown, and right after a click.
    /// Formatted in the current language (kept as its kind, so a language switch translates it).
    /// </summary>
    public string? TestNotificationResult => _testNotificationResult switch
    {
        TestNotificationResultKind.Asking => Strings.SettingsTestNotificationAsking,
        TestNotificationResultKind.Sent => UsesMacTerms ? Strings.SettingsTestNotificationSentMac : Strings.SettingsTestNotificationSentWindows,
        TestNotificationResultKind.Failed => Strings.SettingsTestNotificationFailed,
        _ => null,
    };

    /// <summary>The result is shown (never together with the warning).</summary>
    public bool ShowsTestNotificationResult => _testNotificationResult != TestNotificationResultKind.None && !ShowsNotificationWarning;

    /// <summary>The result is a failure (red); waiting and sent are neutral (delivery cannot be confirmed).</summary>
    public bool TestNotificationResultIsError => _testNotificationResult == TestNotificationResultKind.Failed;

    private bool CanSendTestNotification => _notificationSettings is not null && !_isSendingTestNotification && !_disposed && !ShowsNotificationsTurnedOff;

    /// <summary>
    /// Reads again what the OS does with the app's notifications (T-020, T-101); UI thread. Never throws; nothing without
    /// <see cref="ISystemNotificationSettings"/> or after the window closed. A state the OS did not say leaves no warning.
    /// </summary>
    public async Task RefreshNotificationPermissionAsync()
    {
        if (_notificationSettings is not { } settings || _disposed)
        {
            return;
        }

        int version = ++_notificationStateVersion;
        SystemNotificationState state = await settings.ReadStateAsync(LifetimeToken).ConfigureAwait(true);
        if (!_disposed && version == _notificationStateVersion)
        {
            SetNotificationState(state);
        }
    }

    /// <summary>The texts of the warning, the button and the result in the new language.</summary>
    private void RefreshNotificationTexts()
    {
        OnPropertyChanged(nameof(NotificationWarningText));
        OnPropertyChanged(nameof(OpenNotificationSettingsText));
        OnPropertyChanged(nameof(OpenNotificationSettingsHint));
        OnPropertyChanged(nameof(TestNotificationResult));
    }

    /// <summary>
    /// The OS settings of the notifications. The warning stays until the window reads the state again (it becomes active
    /// again after the user comes back).
    /// </summary>
    private void OpenNotificationSettings()
    {
        if (ShowsNotificationsTurnedOff)
        {
            _notificationSettings?.Open();
        }
    }

    private async Task SendTestNotificationAsync()
    {
        if (_notificationSettings is not { } settings || !CanSendTestNotification)
        {
            return;
        }

        CancellationToken lifetime = LifetimeToken;
        // What the window shows now, saved or not.
        bool playSound = _selectedSound.Value != NotificationSound.Off;
        IsSendingTestNotification = true;
        SetTestNotificationResult(TestNotificationResultKind.None);
        try
        {
            SystemNotificationState state = await settings.ReadStateAsync(lifetime).ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            if (state == SystemNotificationState.NotAsked)
            {
                // macOS asks the user now (only this once); the window waits without blocking.
                SetTestNotificationResult(TestNotificationResultKind.Asking);
                bool granted = await settings.RequestPermissionAsync(lifetime).ConfigureAwait(true);
                if (_disposed)
                {
                    return;
                }

                if (!granted)
                {
                    // Denied: the warning with the button to System Settings is the result (no further text).
                    state = await settings.ReadStateAsync(lifetime).ConfigureAwait(true);
                    if (_disposed)
                    {
                        return;
                    }

                    LearnNotificationState(state);
                    SetTestNotificationResult(state == SystemNotificationState.TurnedOff ? TestNotificationResultKind.None : TestNotificationResultKind.Failed);
                    return;
                }

                state = SystemNotificationState.Allowed;
            }

            if (state is SystemNotificationState.TurnedOff or SystemNotificationState.BlockedByPolicy)
            {
                // Sending would show nothing: the warning says why.
                LearnNotificationState(state);
                SetTestNotificationResult(TestNotificationResultKind.None);
                return;
            }

            if (state == SystemNotificationState.Allowed)
            {
                LearnNotificationState(state);
            }

            // Unknown: the OS did not say; the platform decides (it checks again before sending).
            TestNotificationOutcome outcome = await settings.SendTestAsync(playSound, lifetime).ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            switch (outcome)
            {
                case TestNotificationOutcome.Sent:
                    SetTestNotificationResult(TestNotificationResultKind.Sent);
                    break;
                case TestNotificationOutcome.TurnedOff:
                    LearnNotificationState(SystemNotificationState.TurnedOff);
                    SetTestNotificationResult(TestNotificationResultKind.None);
                    break;
                case TestNotificationOutcome.BlockedByPolicy:
                    LearnNotificationState(SystemNotificationState.BlockedByPolicy);
                    SetTestNotificationResult(TestNotificationResultKind.None);
                    break;
                default:
                    SetTestNotificationResult(TestNotificationResultKind.Failed);
                    break;
            }
        }
        finally
        {
            if (!_disposed)
            {
                IsSendingTestNotification = false;
            }
        }
    }

    /// <summary>A state the test learnt: newer than any read still running.</summary>
    private void LearnNotificationState(SystemNotificationState state)
    {
        _notificationStateVersion++;
        SetNotificationState(state);
    }

    private void SetNotificationState(SystemNotificationState state)
    {
        if (state == _notificationState)
        {
            return;
        }

        bool turnedOff = ShowsNotificationsTurnedOff;
        _notificationState = state;
        if (ShowsNotificationWarning)
        {
            // The warning wins: the result of an earlier test would contradict it.
            SetTestNotificationResult(TestNotificationResultKind.None);
        }

        if (turnedOff != ShowsNotificationsTurnedOff)
        {
            OnPropertyChanged(nameof(ShowsNotificationsTurnedOff));
            OpenNotificationSettingsCommand.NotifyCanExecuteChanged();
            SendTestNotificationCommand.NotifyCanExecuteChanged();
        }

        OnPropertyChanged(nameof(ShowsNotificationsBlockedByPolicy));
        OnPropertyChanged(nameof(ShowsNotificationWarning));
        OnPropertyChanged(nameof(NotificationWarningText));
        OnPropertyChanged(nameof(ShowsTestNotification));
        OnPropertyChanged(nameof(ShowsTestNotificationResult));
    }

    private void SetTestNotificationResult(TestNotificationResultKind result)
    {
        if (result == _testNotificationResult)
        {
            return;
        }

        _testNotificationResult = result;
        OnPropertyChanged(nameof(TestNotificationResult));
        OnPropertyChanged(nameof(ShowsTestNotificationResult));
        OnPropertyChanged(nameof(TestNotificationResultIsError));
    }
}
