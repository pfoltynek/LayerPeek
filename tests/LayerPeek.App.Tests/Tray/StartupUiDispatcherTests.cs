using LayerPeek.App.Tests.Notifications;
using LayerPeek.App.Tray;

namespace LayerPeek.App.Tests.Tray;

/// <summary>
/// T-099: the UI thread's dispatcher before Avalonia exists queues work for the main thread, wakes it, and hands over to
/// Avalonia's dispatcher without reordering or losing work.
/// </summary>
public sealed class StartupUiDispatcherTests
{
    [Fact]
    public void WorkWaitsForTheMainThreadAndWakesIt()
    {
        int wakes = 0;
        List<int> ran = [];
        StartupUiDispatcher dispatcher = new(() => Interlocked.Increment(ref wakes));

        dispatcher.Post(() => ran.Add(1));
        dispatcher.Post(() => ran.Add(2));

        Assert.Empty(ran);
        Assert.Equal(2, dispatcher.PendingCount);
        Assert.Equal(2, wakes);
        Assert.Equal(2, dispatcher.RunPending());
        Assert.Equal([1, 2], ran);
        Assert.Equal(0, dispatcher.PendingCount);
    }

    [Fact]
    public void WorkPostedWhileRunningRunsInTheSamePass()
    {
        List<int> ran = [];
        StartupUiDispatcher dispatcher = new();
        dispatcher.Post(() =>
        {
            ran.Add(1);
            dispatcher.Post(() => ran.Add(2));
        });

        Assert.Equal(2, dispatcher.RunPending());
        Assert.Equal([1, 2], ran);
    }

    [Fact]
    public void AttachingRunsWhatWaitedThenForwardsInOrder()
    {
        List<string> ran = [];
        StartupUiDispatcher dispatcher = new();
        ManualDispatcher avalonia = new();
        dispatcher.Post(() => ran.Add("before 1"));
        dispatcher.Post(() =>
        {
            ran.Add("before 2");
            // Posted while the handover runs: queued at the target, after everything that waited.
            dispatcher.Post(() => ran.Add("during"));
        });

        dispatcher.AttachTo(avalonia);
        dispatcher.Post(() => ran.Add("after"));

        Assert.True(dispatcher.IsAttached);
        Assert.Equal(["before 1", "before 2"], ran);
        Assert.Equal(2, avalonia.Pending);
        avalonia.RunAll();
        Assert.Equal(["before 1", "before 2", "during", "after"], ran);
        Assert.Equal(0, dispatcher.PendingCount);
    }

    [Fact]
    public void AttachingTwiceIsRefused()
    {
        StartupUiDispatcher dispatcher = new();
        dispatcher.AttachTo(new ManualDispatcher());

        Assert.Throws<InvalidOperationException>(() => dispatcher.AttachTo(new ManualDispatcher()));
    }

    [Fact]
    public void AnExceptionOfTheWorkEscapesLikeOnTheUiThread()
    {
        StartupUiDispatcher dispatcher = new();
        dispatcher.Post(static () => throw new InvalidOperationException("boom"));

        Assert.Throws<InvalidOperationException>(() => dispatcher.RunPending());
    }

    [Fact]
    public async Task ContinuationsOnTheStartupContextComeBackThroughTheDispatcher()
    {
        StartupUiDispatcher dispatcher = new();
        SynchronizationContext context = new StartupSynchronizationContext(dispatcher);
        TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int continuedOn = 0;
        SynchronizationContext? previous = SynchronizationContext.Current;
        Task awaiting;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            awaiting = ContinueAsync();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        released.SetResult();
        await Waiting.ForAsync(() => dispatcher.PendingCount == 1);
        Assert.False(awaiting.IsCompleted);
        Assert.Equal(1, dispatcher.RunPending());
        Assert.True(awaiting.IsCompleted);
        Assert.Equal(Environment.CurrentManagedThreadId, continuedOn);
        Assert.Throws<NotSupportedException>(() => context.Send(static _ => { }, null));

        async Task ContinueAsync()
        {
            await released.Task.ConfigureAwait(true);
            continuedOn = Environment.CurrentManagedThreadId;
        }
    }
}
