using System.Diagnostics;
using System.Runtime.InteropServices;

using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Camera.FFmpeg.Tests;

/// <summary>Decoding the recorded Buddy3D fixture (local file, no network).</summary>
public sealed class FFmpegCameraFeedFileTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [FFmpegFact]
    public async Task DecodesEveryFrameOfTheFixture()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);

        int count = await TestFixtures.CountFramesAsync(feed, Ct);

        Assert.Equal(TestFixtures.Buddy3DFrameCount, count);
    }

    [FFmpegFact]
    public async Task FramesAreOpaqueBgraWithPlausibleContent()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        int checkedFrames = 0;

        await foreach (CameraFrame frame in feed.ReadFramesAsync(Ct))
        {
            Assert.True(frame.Stride >= frame.Width * CameraFrame.BytesPerPixel);
            Assert.Equal(0, frame.Stride % CameraFrame.BytesPerPixel);
            ReadOnlySpan<byte> pixels = frame.Pixels.Span;
            Assert.True(pixels.Length >= (frame.Stride * (frame.Height - 1)) + (frame.Width * CameraFrame.BytesPerPixel));

            long brightness = 0;
            int samples = 0;
            bool opaque = true;
            for (int y = 0; y < frame.Height; y += 16)
            {
                ReadOnlySpan<byte> row = pixels.Slice(y * frame.Stride, frame.Width * CameraFrame.BytesPerPixel);
                for (int x = 0; x < frame.Width; x += 16)
                {
                    int p = x * CameraFrame.BytesPerPixel;
                    brightness += row[p] + row[p + 1] + row[p + 2];
                    opaque &= row[p + 3] == 255;
                    samples++;
                }
            }

            double mean = brightness / (3.0 * samples);
            Assert.True(opaque, "Every pixel must be opaque.");
            // A real camera picture: neither black (nothing decoded) nor white (wrong conversion).
            Assert.InRange(mean, 10, 245);
            checkedFrames++;
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount, checkedFrames);
    }

    [FFmpegFact]
    public async Task FramesReuseOneBuffer()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        byte[]? first = null;
        int frames = 0;

        await foreach (CameraFrame frame in feed.ReadFramesAsync(Ct))
        {
            Assert.True(MemoryMarshal.TryGetArray(frame.Pixels, out ArraySegment<byte> segment));
            first ??= segment.Array;
            Assert.Same(first, segment.Array);
            if (++frames == 10)
            {
                break;
            }
        }

        Assert.Equal(10, frames);
    }

    [FFmpegFact]
    public async Task EndOfFileCompletesTheEnumeration()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        IAsyncEnumerator<CameraFrame> frames = feed.ReadFramesAsync(Ct).GetAsyncEnumerator(Ct);
        try
        {
            while (await frames.MoveNextAsync())
            {
            }

            // Native resources are released as soon as the stream ends, not only on dispose.
            Assert.False(feed.HoldsNativeResources);
            Assert.False(await frames.MoveNextAsync());
        }
        finally
        {
            await frames.DisposeAsync();
        }
    }

    [FFmpegFact]
    public async Task CancellationMidStreamThrowsAndReleasesResources()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        int frames = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (CameraFrame frame in feed.ReadFramesAsync(cancellation.Token))
            {
                if (++frames == 5)
                {
                    Assert.True(feed.HoldsNativeResources);
                    await cancellation.CancelAsync();
                }
            }
        });

        Assert.Equal(5, frames);
        Assert.False(feed.HoldsNativeResources);
    }

    [FFmpegFact]
    public async Task EnumeratorTokenIsHonouredToo()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        IAsyncEnumerator<CameraFrame> frames = feed.ReadFramesAsync(Ct).GetAsyncEnumerator(cancellation.Token);
        try
        {
            Assert.True(await frames.MoveNextAsync());
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await frames.MoveNextAsync());
        }
        finally
        {
            await frames.DisposeAsync();
        }

        Assert.False(feed.HoldsNativeResources);
    }

    [FFmpegFact]
    public async Task AlreadyCancelledTokenThrowsWithoutOpening()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await TestFixtures.CountFramesAsync(feed, cancellation.Token));

        Assert.False(feed.HoldsNativeResources);
    }

    [FFmpegFact]
    public async Task DisposingTheFeedDuringEnumerationEndsIt()
    {
        FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        IAsyncEnumerator<CameraFrame> frames = feed.ReadFramesAsync(Ct).GetAsyncEnumerator(Ct);
        try
        {
            Assert.True(await frames.MoveNextAsync());
            Assert.True(await frames.MoveNextAsync());

            await feed.DisposeAsync();

            Assert.False(feed.HoldsNativeResources);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await frames.MoveNextAsync());
        }
        finally
        {
            await frames.DisposeAsync();
            await feed.DisposeAsync();
        }
    }

    [FFmpegFact]
    public async Task DisposingTheFeedWhileAFrameIsPendingEndsTheRead()
    {
        FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        IAsyncEnumerator<CameraFrame> frames = feed.ReadFramesAsync(Ct).GetAsyncEnumerator(Ct);
        try
        {
            Assert.True(await frames.MoveNextAsync());
            // Races with the decoder on purpose: either the frame arrives first or the read is aborted by the dispose.
            ValueTask<bool> pending = frames.MoveNextAsync();
            await feed.DisposeAsync();

            try
            {
                await pending;
            }
            catch (ObjectDisposedException)
            {
            }

            Assert.False(feed.HoldsNativeResources);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await frames.MoveNextAsync());
        }
        finally
        {
            await frames.DisposeAsync();
        }
    }

    [FFmpegFact]
    public async Task DisposeIsIdempotent()
    {
        FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        IAsyncEnumerator<CameraFrame> frames = feed.ReadFramesAsync(Ct).GetAsyncEnumerator(Ct);
        Assert.True(await frames.MoveNextAsync());

        await frames.DisposeAsync();
        await frames.DisposeAsync();
        await feed.DisposeAsync();
        await feed.DisposeAsync();

        Assert.False(feed.HoldsNativeResources);
    }

    [FFmpegFact]
    public async Task DisposingWithoutEnumeratingDoesNothing()
    {
        FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);

        await feed.DisposeAsync();

        Assert.False(feed.HoldsNativeResources);
        Assert.Throws<ObjectDisposedException>(() => feed.ReadFramesAsync(Ct));
    }

    [FFmpegFact]
    public async Task CanBeEnumeratedOnlyOnce()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        IAsyncEnumerable<CameraFrame> frames = feed.ReadFramesAsync(Ct);
        await using IAsyncEnumerator<CameraFrame> first = frames.GetAsyncEnumerator(Ct);

        Assert.Throws<InvalidOperationException>(() => feed.ReadFramesAsync(Ct));
        Assert.Throws<InvalidOperationException>(() => frames.GetAsyncEnumerator(Ct));
    }

    [FFmpegFact]
    public async Task MissingFileThrowsCameraFeedExceptionWithoutThePath()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.MissingFile);

        CameraFeedException error = await Assert.ThrowsAsync<CameraFeedException>(() => TestFixtures.CountFramesAsync(feed, Ct));

        Assert.DoesNotContain("does-not-exist", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(feed.HoldsNativeResources);
    }

    [FFmpegFact]
    public async Task FileThatIsNotVideoThrowsCameraFeedException()
    {
        // The test assembly's dependency manifest: an existing file that is certainly no video.
        Uri notVideo = new(Path.Combine(AppContext.BaseDirectory, "PrusaTrayDiagnostic.Camera.FFmpeg.Tests.deps.json"));
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(notVideo);

        await Assert.ThrowsAsync<CameraFeedException>(() => TestFixtures.CountFramesAsync(feed, Ct));

        Assert.False(feed.HoldsNativeResources);
    }

    [FFmpegFact]
    public async Task FileInAnotherContainerIsNotOpened()
    {
        // A valid WAV file: without the format whitelist FFmpeg would open it and report that it has no video.
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.SilenceWav);

        CameraFeedException error = await Assert.ThrowsAsync<CameraFeedException>(() => TestFixtures.CountFramesAsync(feed, Ct));

        Assert.StartsWith("Opening the video failed", error.Message, StringComparison.Ordinal);
        Assert.False(feed.HoldsNativeResources);
    }

    [FFmpegFact]
    public async Task EnumerationDoesNotBlockTheCallersThread()
    {
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D);
        await using IAsyncEnumerator<CameraFrame> frames = feed.ReadFramesAsync(Ct).GetAsyncEnumerator(Ct);

        Stopwatch watch = Stopwatch.StartNew();
        ValueTask<bool> first = frames.MoveNextAsync();
        TimeSpan callTook = watch.Elapsed;

        // Opening and probing happen on the decoder thread; the call itself only hands the work over.
        Assert.True(callTook < TimeSpan.FromMilliseconds(500), $"MoveNextAsync blocked for {callTook}.");
        Assert.True(await first);
    }
}
