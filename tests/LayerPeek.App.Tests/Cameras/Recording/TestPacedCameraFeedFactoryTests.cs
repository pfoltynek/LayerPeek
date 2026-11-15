#if DEBUG
using System.Diagnostics;

using LayerPeek.App.Cameras.Recording;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Tests.Cameras.Recording;

/// <summary>
/// The slowed-down feeds of the file jobs in the Debug test mode (T-213): every item comes later, in order, cancellation
/// ends the wait at once, the inner feed is created for the same address and disposed with the paced one.
/// </summary>
public sealed class TestPacedCameraFeedFactoryTests
{
    private static readonly Uri File = new("file:///tmp/2026-11-15_10-00-00_Benchy_timelapse.mp4");
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(40);

    [Fact]
    public async Task EveryFrameComesAfterTheDelayInOrder()
    {
        FakeCameraFeedFactory inner = new();
        ICameraFeed feed = new TestPacedCameraFeedFactory(inner, Delay).Create(File);
        FakeCameraFeed source = Assert.Single(inner.Feeds);
        Assert.Equal(File, Assert.Single(inner.Uris));
        source.Send(fill: 1);
        source.Send(fill: 2);
        source.Send(fill: 3);
        source.End();

        Stopwatch clock = Stopwatch.StartNew();
        List<byte> fills = [];
        await foreach (CameraFrame frame in feed.ReadFramesAsync(TestContext.Current.CancellationToken))
        {
            fills.Add(frame.Pixels.Span[0]);
        }

        Assert.Equal([1, 2, 3], fills);
        // Timer granularity: each wait may end up to a millisecond early on some systems.
        Assert.True(clock.Elapsed >= (3 * Delay) - TimeSpan.FromMilliseconds(10), $"{clock.Elapsed.TotalMilliseconds} ms");
        await feed.DisposeAsync();
        Assert.True(source.IsDisposed);
    }

    [Fact]
    public async Task EveryStreamItemComesAfterTheDelay()
    {
        FakeCameraFeedFactory inner = new();
        ICameraFeed feed = new TestPacedCameraFeedFactory(inner, Delay).Create(File);
        FakeCameraFeed source = Assert.Single(inner.Feeds);
        source.Send();
        source.Send();
        source.End();

        Stopwatch clock = Stopwatch.StartNew();
        int items = 0;
        await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: true, packets: false), TestContext.Current.CancellationToken))
        {
            Assert.Equal(CameraStreamItemKind.Frame, item.Kind);
            items++;
        }

        Assert.Equal(2, items);
        Assert.True(clock.Elapsed >= (2 * Delay) - TimeSpan.FromMilliseconds(10), $"{clock.Elapsed.TotalMilliseconds} ms");
        await feed.DisposeAsync();
    }

    [Fact]
    public async Task CancellationEndsTheWaitAtOnce()
    {
        FakeCameraFeedFactory inner = new();
        await using ICameraFeed feed = new TestPacedCameraFeedFactory(inner, TimeSpan.FromSeconds(30)).Create(File);
        Assert.Single(inner.Feeds).Send();
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(50));

        Stopwatch clock = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (CameraFrame frame in feed.ReadFramesAsync(cancel.Token))
            {
                Assert.Fail("No frame may come before the delay.");
            }
        });
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"{clock.Elapsed.TotalMilliseconds} ms");
    }
}
#endif
