using LayerPeek.App.Platform;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The OS side of the notifications in memory (T-020, T-101): the state, the user's answer to the question of macOS and the
/// outcome of the test notification are set by the test; nothing reaches macOS or Windows, opening the OS settings is only
/// counted. Single-threaded use (the tests call from one thread); the gates hold a call until the test releases it.
/// </summary>
internal sealed class FakeSystemNotificationSettings : ISystemNotificationSettings
{
    /// <summary>What <see cref="ReadStateAsync"/> reports.</summary>
    public SystemNotificationState State { get; set; } = SystemNotificationState.Allowed;

    /// <summary>
    /// Shortcut of the T-020 tests: <see cref="SystemNotificationState.TurnedOff"/>, otherwise
    /// <see cref="SystemNotificationState.Allowed"/>.
    /// </summary>
    public bool TurnedOff
    {
        get => State == SystemNotificationState.TurnedOff;
        set => State = value ? SystemNotificationState.TurnedOff : SystemNotificationState.Allowed;
    }

    /// <summary>The user's answer to the question of macOS; the state follows it (allowed / turned off).</summary>
    public bool Grants { get; set; } = true;

    /// <summary>What <see cref="SendTestAsync"/> reports.</summary>
    public TestNotificationOutcome Outcome { get; set; } = TestNotificationOutcome.Sent;

    /// <summary>When set, <see cref="RequestPermissionAsync"/> waits for it (the user has not answered yet).</summary>
    public TaskCompletionSource? AnswerGate { get; set; }

    /// <summary>When set, <see cref="SendTestAsync"/> waits for it.</summary>
    public TaskCompletionSource? SendGate { get; set; }

    public int Reads { get; private set; }

    public int Requests { get; private set; }

    public int Opened { get; private set; }

    /// <summary>The sound of every test notification sent so far.</summary>
    public List<bool> Sent { get; } = [];

    public Task<SystemNotificationState> ReadStateAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(cancellationToken.IsCancellationRequested ? SystemNotificationState.Unknown : State);
    }

    public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken)
    {
        Requests++;
        if (AnswerGate is { } gate)
        {
            try
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        if (State == SystemNotificationState.NotAsked)
        {
            State = Grants ? SystemNotificationState.Allowed : SystemNotificationState.TurnedOff;
        }

        return State == SystemNotificationState.Allowed;
    }

    public async Task<TestNotificationOutcome> SendTestAsync(bool playSound, CancellationToken cancellationToken)
    {
        if (SendGate is { } gate)
        {
            try
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return TestNotificationOutcome.Failed;
            }
        }

        if (Outcome == TestNotificationOutcome.Sent)
        {
            Sent.Add(playSound);
        }

        return Outcome;
    }

    public void Open() => Opened++;
}
