using System.Diagnostics;

using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Platform;

namespace LayerPeek.App.Tests.Platform;

/// <summary>
/// T-099: while waiting for a display the main thread runs the posted UI-thread work and its run loop instead of sleeping;
/// a quit or a finished task ends the loop early. Fakes only: runs on every OS.
/// </summary>
public sealed class StartupLoopTests
{
    [Fact]
    public void APauseRunsPostedWorkAndTheRunLoopUntilTheIntervalPassed()
    {
        FakeTimeProvider clock = new();
        FakeRunLoop runLoop = new(clock);
        using StartupLoop loop = new(runLoop, clock);
        int ran = 0;
        // Posted from "another thread" while the loop runs.
        runLoop.OnRun = () => loop.Dispatcher.Post(() => ran++);

        Assert.False(loop.Pause(TimeSpan.FromSeconds(1), CancellationToken.None));

        Assert.True(ran >= 1, "Posted work ran during the pause.");
        Assert.Equal(TimeSpan.FromSeconds(1), runLoop.Slices.Aggregate(TimeSpan.Zero, static (sum, slice) => sum + slice));
        Assert.True(runLoop.Wakes >= 1, "Posting woke the run loop.");
    }

    [Fact]
    public void ACancelledPauseReturnsTrueAndRunsWhatWasPostedFirst()
    {
        FakeTimeProvider clock = new();
        FakeRunLoop runLoop = new(clock);
        using StartupLoop loop = new(runLoop, clock);
        using CancellationTokenSource quit = new();
        bool ran = false;
        loop.Dispatcher.Post(() => ran = true);
        runLoop.OnRun = quit.Cancel;

        Assert.True(loop.Pause(TimeSpan.FromHours(1), quit.Token));

        Assert.True(ran);
        Assert.Single(runLoop.Slices);
    }

    [Fact]
    public void RunUntilEndsWhenTheTaskCompletesOrTheLimitPassed()
    {
        FakeTimeProvider clock = new();
        FakeRunLoop runLoop = new(clock);
        using StartupLoop loop = new(runLoop, clock);
        TaskCompletionSource done = new();
        runLoop.OnRun = () => loop.Dispatcher.Post(done.SetResult);

        Assert.True(loop.RunUntil(done.Task, TimeSpan.FromSeconds(5)));

        runLoop.OnRun = null;
        Assert.False(loop.RunUntil(new TaskCompletionSource().Task, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TheManagedRunLoopKeepsAWakeAndReturnsAtOnce()
    {
        using ManagedStartupRunLoop runLoop = new();
        runLoop.Wake();
        runLoop.Wake();

        Stopwatch elapsed = Stopwatch.StartNew();
        runLoop.RunFor(TimeSpan.FromSeconds(30));

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), "The wake before the run was kept.");
    }

    [Fact]
    public async Task TheManagedRunLoopWakesFromAnotherThreadAndIgnoresWakesAfterDispose()
    {
        ManagedStartupRunLoop runLoop = new();
        Task wake = Task.Run(async () =>
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            runLoop.Wake();
        }, TestContext.Current.CancellationToken);

        Stopwatch elapsed = Stopwatch.StartNew();
        runLoop.RunFor(TimeSpan.FromSeconds(30));
        await wake;

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
        runLoop.Dispose();
        runLoop.Wake();
        runLoop.RunFor(TimeSpan.FromSeconds(30));
    }

    /// <summary>A run loop on fake time: each run moves the clock by its whole slice unless something woke it.</summary>
    private sealed class FakeRunLoop(FakeTimeProvider clock) : IStartupRunLoop
    {
        private int _signalled;

        public List<TimeSpan> Slices { get; } = [];

        public int Wakes { get; private set; }

        public Action? OnRun { get; set; }

        public void RunFor(TimeSpan timeout)
        {
            Slices.Add(timeout);
            OnRun?.Invoke();
            if (Interlocked.Exchange(ref _signalled, 0) == 0)
            {
                clock.Advance(timeout);
            }
            else
            {
                // Woken: some time passed, not the whole slice.
                TimeSpan step = timeout < TimeSpan.FromMilliseconds(100) ? timeout : TimeSpan.FromMilliseconds(100);
                clock.Advance(step);
                Slices[^1] = step;
            }
        }

        public void Wake()
        {
            Wakes++;
            Volatile.Write(ref _signalled, 1);
        }

        public void Dispose()
        {
        }
    }
}
