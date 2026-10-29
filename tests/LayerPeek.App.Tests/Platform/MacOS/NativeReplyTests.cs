using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>The turns of native calls whose completion handler is a global block without state (T-020).</summary>
public sealed class NativeReplyTests : IDisposable
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    private readonly NativeReply<int> _reply = new();

    public void Dispose() => _reply.Dispose();

    [Fact]
    public async Task TheReplyAnswersTheWaitingCall()
    {
        Task<int> asked = _reply.AskAsync(() => _reply.Complete(42), Long, TestContext.Current.CancellationToken);

        Assert.Equal(42, await asked);
    }

    [Fact]
    public async Task ALaterReplyAnswersTooAndAReplyWithoutACallIsDropped()
    {
        _reply.Complete(1);
        Task<int> asked = _reply.AskAsync(static () => { }, Long, TestContext.Current.CancellationToken);
        Assert.False(asked.IsCompleted);

        _reply.Complete(2);
        Assert.Equal(2, await asked);
    }

    [Fact]
    public async Task CallsTakeTurns()
    {
        int sent = 0;
        Task<int> first = _reply.AskAsync(() => Interlocked.Increment(ref sent), Long, TestContext.Current.CancellationToken);
        Task<int> second = _reply.AskAsync(() => Interlocked.Increment(ref sent), Long, TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref sent));

        _reply.Complete(10);
        Assert.Equal(10, await first);
        await LayerPeek.App.Tests.Notifications.Waiting.ForAsync(() => Volatile.Read(ref sent) == 2);
        _reply.Complete(20);
        Assert.Equal(20, await second);
    }

    [Fact]
    public async Task NoReplyTimesOutAndFreesTheTurn()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => _reply.AskAsync(static () => { }, TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken));
        // The late reply finds no call.
        _reply.Complete(7);

        Task<int> next = _reply.AskAsync(() => _reply.Complete(8), Long, TestContext.Current.CancellationToken);
        Assert.Equal(8, await next);
    }

    [Fact]
    public async Task AFailingSendAndACancelFreeTheTurn()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _reply.AskAsync(static () => throw new InvalidOperationException("refused"), Long, TestContext.Current.CancellationToken));
        using CancellationTokenSource cancel = new();
        Task<int> cancelled = _reply.AskAsync(static () => { }, Long, cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        Assert.Equal(3, await _reply.AskAsync(() => _reply.Complete(3), Long, TestContext.Current.CancellationToken));
    }
}
