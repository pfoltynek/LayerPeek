using LayerPeek.App.Platform;

namespace LayerPeek.App.Tests.Platform;

/// <summary>T-187: a window counts as occluded only after the grace time, and as visible again at once.</summary>
public sealed class OcclusionGraceTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    private readonly List<bool> _reported = [];
    private readonly List<Scheduled> _scheduled = [];

    private OcclusionGrace Create() => new(_reported.Add, Schedule, Grace);

    private Scheduled Schedule(Action action, TimeSpan delay)
    {
        Scheduled scheduled = new(action, delay);
        _scheduled.Add(scheduled);
        return scheduled;
    }

    [Fact]
    public void StartsVisibleAtOnce()
    {
        using OcclusionGrace grace = Create();

        Assert.Equal([false], _reported);
        Assert.Empty(_scheduled);
    }

    [Fact]
    public void OcclusionIsReportedOnlyAfterTheGraceTime()
    {
        using OcclusionGrace grace = Create();

        grace.Update(occluded: true);
        Assert.Equal([false], _reported);
        Scheduled pending = Assert.Single(_scheduled);
        Assert.Equal(Grace, pending.Delay);

        pending.Run();
        Assert.Equal([false, true], _reported);
    }

    [Fact]
    public void ShortOcclusionIsNeverReported()
    {
        // A Space switch or full screen animation: occluded for a moment.
        using OcclusionGrace grace = Create();

        grace.Update(occluded: true);
        grace.Update(occluded: false);

        Scheduled pending = Assert.Single(_scheduled);
        Assert.True(pending.Cancelled);
        pending.Run();
        Assert.Equal([false], _reported);
    }

    [Fact]
    public void VisibleAgainIsReportedAtOnce()
    {
        using OcclusionGrace grace = Create();
        grace.Update(occluded: true);
        _scheduled[0].Run();

        grace.Update(occluded: false);

        Assert.Equal([false, true, false], _reported);
    }

    [Fact]
    public void RepeatedNotificationsKeepOneTimerAndReportOnce()
    {
        using OcclusionGrace grace = Create();

        grace.Update(occluded: true);
        grace.Update(occluded: true);
        _scheduled[0].Run();
        grace.Update(occluded: true);
        grace.Update(occluded: false);
        grace.Update(occluded: false);

        Assert.Single(_scheduled);
        Assert.Equal([false, true, false], _reported);
    }

    [Fact]
    public void DisposeDropsAPendingOcclusion()
    {
        OcclusionGrace grace = Create();
        grace.Update(occluded: true);

        grace.Dispose();
        _scheduled[0].Run();
        grace.Update(occluded: true);

        Assert.True(_scheduled[0].Cancelled);
        Assert.Single(_scheduled);
        Assert.Equal([false], _reported);
    }

    private sealed class Scheduled(Action action, TimeSpan delay) : IDisposable
    {
        public TimeSpan Delay { get; } = delay;

        public bool Cancelled { get; private set; }

        /// <summary>The timer fires (a cancelled one does nothing, like <c>DispatcherTimer.RunOnce</c>).</summary>
        public void Run()
        {
            if (!Cancelled)
            {
                Cancelled = true;
                action();
            }
        }

        public void Dispose() => Cancelled = true;
    }
}
