using System.Diagnostics;

using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// T-099: the CFRunLoop of a thread with the wake-up source of the startup loop. Runs on a thread of its own (not the main
/// thread of the test host, which belongs to the runner); Core Foundation only, nothing of the user is touched.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class MacStartupRunLoopTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task AWakeFromAnotherThreadEndsTheRunEarly()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Core Foundation exists only on macOS.");

        TimeSpan elapsed = await OnOwnThreadAsync(runLoop =>
        {
            using Timer wake = new(_ => runLoop.Wake(), null, TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
            Stopwatch watch = Stopwatch.StartNew();
            runLoop.RunFor(Long);
            return watch.Elapsed;
        });

        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"Woken after {elapsed}.");
    }

    [Fact]
    public async Task AWakeBeforeTheRunIsNotLost()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Core Foundation exists only on macOS.");

        TimeSpan elapsed = await OnOwnThreadAsync(runLoop =>
        {
            runLoop.Wake();
            Stopwatch watch = Stopwatch.StartNew();
            runLoop.RunFor(Long);
            return watch.Elapsed;
        });

        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"Returned after {elapsed}.");
    }

    [Fact]
    public async Task WithoutAWakeTheRunLastsItsTimeoutAndADisposedLoopDoesNothing()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Core Foundation exists only on macOS.");

        TimeSpan elapsed = await OnOwnThreadAsync(runLoop =>
        {
            Stopwatch watch = Stopwatch.StartNew();
            runLoop.RunFor(TimeSpan.FromMilliseconds(200));
            TimeSpan ran = watch.Elapsed;
            runLoop.Dispose();
            // Ignored after dispose: no released source is touched.
            runLoop.Wake();
            runLoop.RunFor(Long);
            return ran;
        });

        Assert.True(elapsed >= TimeSpan.FromMilliseconds(150), $"Returned after {elapsed}.");
    }

    private static Task<TimeSpan> OnOwnThreadAsync(Func<MacStartupRunLoop, TimeSpan> body)
    {
        TaskCompletionSource<TimeSpan> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                MacStartupRunLoop? runLoop = MacStartupRunLoop.TryCreateForCurrentThread();
                Assert.NotNull(runLoop);
                using (runLoop)
                {
                    result.SetResult(body(runLoop));
                }
            }
            catch (Exception ex)
            {
                result.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "MacStartupRunLoopTests",
        };
        thread.Start();
        return result.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
    }
}
