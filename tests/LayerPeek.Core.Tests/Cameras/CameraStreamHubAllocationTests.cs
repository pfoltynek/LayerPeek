using System.Threading.Tasks.Sources;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Tests.Allocation;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>
/// T-143: the hub's fan-out allocates nothing per frame or packet; isolated, see <see cref="AllocationSensitive"/>. The fake
/// camera produces the next item only when the consumers took the previous one (like a live camera that is slower than its
/// consumers), each completed asynchronously on its own thread like a real decoder.
/// </summary>
[Collection(nameof(AllocationSensitive))]
public sealed class CameraStreamHubAllocationTests
{
    private const int WarmUp = 32;
    private const int Measured = 256;

    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    [Fact]
    public async Task FramesToSeveralSubscribersDoNotAllocate()
    {
        long allocated = await Task.Run(MeasureFramesAsync, TestContext.Current.CancellationToken);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task PacketsToAReaderDoNotAllocate()
    {
        long allocated = await Task.Run(MeasurePacketsAsync, TestContext.Current.CancellationToken);

        Assert.Equal(0, allocated);
    }

    private static async Task<long> MeasureFramesAsync()
    {
        LockstepFeed feed = new(packets: false);
        // The fake is stopped before the hub waits for its connection to close (its pending read then ends).
        await using CameraStreamHub hub = new(new ReconnectingCameraFeed(new SingleFeedFactory(feed), TimeProvider.System, NullLogger<ReconnectingCameraFeed>.Instance), TimeProvider.System, NullLogger<CameraStreamHub>.Instance, TimeSpan.Zero);
        int notified = 0;
        using Stopper stopper = new(feed);
        using CameraFrameSubscription window = hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions { FrameAvailable = () => Interlocked.Increment(ref notified) });
        using CameraFrameSubscription detection = hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions { MinFrameInterval = TimeSpan.FromHours(1) });
        await feed.Started.WaitAsync(TestContext.Current.CancellationToken);

        int taken = 0;
        long Step()
        {
            feed.Produce();
            while (window.TakeLatest() is null)
            {
                Thread.Yield();
            }

            return ++taken;
        }

        for (int i = 0; i < WarmUp; i++)
        {
            Step();
        }

        long minimum = long.MaxValue;
        for (int pass = 0; pass < 4 && minimum > 0; pass++)
        {
            long before = GC.GetTotalAllocatedBytes(precise: true);
            for (int i = 0; i < Measured; i++)
            {
                Step();
            }

            minimum = Math.Min(minimum, GC.GetTotalAllocatedBytes(precise: true) - before);
        }

        Assert.True(notified > 0);
        return minimum;
    }

    [Fact]
    public async Task GrayscaleKeyFrameSamplesDoNotAllocate()
    {
        long allocated = await Task.Run(() => MeasureSamplesAsync(fromFeed: false), TestContext.Current.CancellationToken);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task GrayscaleKeyFrameSamplesOfTheFeedDoNotAllocate()
    {
        // T-189: the feed converts the samples itself; the hub only copies them.
        long allocated = await Task.Run(() => MeasureSamplesAsync(fromFeed: true), TestContext.Current.CancellationToken);

        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// T-153: the sample lease of print failure detection (key frames only, downscaled to grayscale while copying, or
    /// <paramref name="fromFeed"/> as grayscale frames of the feed, T-189) and the key frame scheduling after every sample
    /// allocate nothing.
    /// </summary>
    private static async Task<long> MeasureSamplesAsync(bool fromFeed)
    {
        LockstepFeed feed = new(packets: false, grayscale: fromFeed ? new CameraGrayscaleSize(320, 180) : null);
        await using CameraStreamHub hub = new(new ReconnectingCameraFeed(new SingleFeedFactory(feed), TimeProvider.System, NullLogger<ReconnectingCameraFeed>.Instance), TimeProvider.System, NullLogger<CameraStreamHub>.Instance, TimeSpan.Zero);
        using Stopper stopper = new(feed);
        int notified = 0;
        using CameraFrameSubscription samples = hub.SubscribeFrames("mk4", StreamUri, new CameraFrameSubscriptionOptions
        {
            KeyFramesOnly = true,
            Grayscale = new CameraGrayscaleSize(320, 180),
            FrameAvailable = () => Interlocked.Increment(ref notified),
        });
        await feed.Started.WaitAsync(TestContext.Current.CancellationToken);

        int sourceWidth = 0;
        void Step()
        {
            feed.Produce();
            CameraFrameBuffer? sample;
            while ((sample = samples.TakeLatest()) is null)
            {
                Thread.Yield();
            }

            sourceWidth = sample.SourceWidth;
        }

        for (int i = 0; i < WarmUp; i++)
        {
            Step();
        }

        long minimum = long.MaxValue;
        for (int pass = 0; pass < 4 && minimum > 0; pass++)
        {
            long before = GC.GetTotalAllocatedBytes(precise: true);
            for (int i = 0; i < Measured; i++)
            {
                Step();
            }

            minimum = Math.Min(minimum, GC.GetTotalAllocatedBytes(precise: true) - before);
        }

        Assert.True(notified > 0);
        // The feed's grayscale frames (their source is the camera's 1280 × 720), or the fake's 4 × 2 BGRA frames.
        Assert.Equal(fromFeed ? 1280 : 4, sourceWidth);
        return minimum;
    }

    private static async Task<long> MeasurePacketsAsync()
    {
        LockstepFeed feed = new(packets: true);
        // The fake is stopped before the hub waits for its connection to close (its pending read then ends).
        await using CameraStreamHub hub = new(new ReconnectingCameraFeed(new SingleFeedFactory(feed), TimeProvider.System, NullLogger<ReconnectingCameraFeed>.Instance), TimeProvider.System, NullLogger<CameraStreamHub>.Instance, TimeSpan.Zero);
        using Stopper stopper = new(feed);
        using CameraPacketSubscription recording = hub.SubscribePackets("mk4", StreamUri);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        // A bug fails the test instead of hanging the run (set once: nothing of it runs in the measured loop).
        stop.CancelAfter(TimeSpan.FromSeconds(60));
        IAsyncEnumerator<CameraStreamItem> items = recording.ReadAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        try
        {
            // The description.
            Assert.True(await items.MoveNextAsync());
            for (int i = 0; i < WarmUp; i++)
            {
                feed.Produce();
                Assert.True(await items.MoveNextAsync());
            }

            long minimum = long.MaxValue;
            for (int pass = 0; pass < 4 && minimum > 0; pass++)
            {
                long before = GC.GetTotalAllocatedBytes(precise: true);
                for (int i = 0; i < Measured; i++)
                {
                    feed.Produce();
                    Assert.True(await items.MoveNextAsync());
                }

                minimum = Math.Min(minimum, GC.GetTotalAllocatedBytes(precise: true) - before);
            }

            return minimum;
        }
        finally
        {
            await stop.CancelAsync();
            await items.DisposeAsync();
        }
    }

    internal sealed class Stopper(LockstepFeed feed) : IDisposable
    {
        public void Dispose() => feed.Dispose();
    }

    internal sealed class SingleFeedFactory(ICameraFeed feed) : ICameraFeedFactory
    {
        public ICameraFeed Create(Uri streamUri) => feed;
    }

    /// <summary>
    /// Yields the description, then one item per <see cref="Produce"/>, each completed by its own producer thread through a
    /// reusable value task source (the fake itself allocates nothing per item).
    /// </summary>
    internal sealed class LockstepFeed : ICameraFeed, IAsyncEnumerable<CameraStreamItem>, IAsyncEnumerator<CameraStreamItem>, IValueTaskSource<bool>, IDisposable
    {
        private static readonly CameraStreamInfo Info = new(CameraVideoCodec.H264, 4, 2, CameraTimeBase.Rtp90kHz, default);

        private readonly bool _packets;
        private readonly CameraGrayscaleSize? _grayscale;
        private readonly byte[] _pixels = new byte[4 * 2 * CameraFrame.BytesPerPixel];
        private readonly byte[] _luma;
        private readonly byte[] _data = new byte[6000];
        private readonly SemaphoreSlim _credits = new(0);
        private readonly SemaphoreSlim _requests = new(0);
        private readonly Thread _producer;
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ManualResetValueTaskSourceCore<bool> _source;
        private bool _infoDelivered;
        private long _pts;
        private volatile bool _stopped;

        private CameraStreamDemand? _demand;

        /// <param name="grayscale">Yields grayscale frames of this size while the demand wants them (T-189), as the decoder does.</param>
        public LockstepFeed(bool packets, CameraGrayscaleSize? grayscale = null)
        {
            _packets = packets;
            _grayscale = grayscale;
            _luma = new byte[grayscale?.Length ?? 0];
            _producer = new Thread(Run) { IsBackground = true };
            _producer.Start();
        }

        public Task Started => _started.Task;

        public CameraStreamItem Current { get; private set; }

        public void Produce() => _credits.Release();

        public IAsyncEnumerable<CameraFrame> ReadFramesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<CameraStreamItem> ReadAsync(CameraStreamDemand demand, CancellationToken cancellationToken)
        {
            _demand = demand;
            return this;
        }

        public IAsyncEnumerator<CameraStreamItem> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync()
        {
            if (!_infoDelivered)
            {
                _infoDelivered = true;
                Current = CameraStreamItem.Started(Info);
                _started.TrySetResult();
                return new ValueTask<bool>(true);
            }

            _source.Reset();
            _requests.Release();
            return new ValueTask<bool>(this, _source.Version);
        }

        bool IValueTaskSource<bool>.GetResult(short token) => _source.GetResult(token);

        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _source.GetStatus(token);

        void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _source.OnCompleted(continuation, state, token, flags);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
            _stopped = true;
            _requests.Release();
            _credits.Release();
        }

        private void Run()
        {
            while (true)
            {
                _requests.Wait();
                if (_stopped)
                {
                    return;
                }

                _credits.Wait();
                if (_stopped)
                {
                    // Stopping: the hub's pending read ends (the hub cancels it as well).
                    _source.SetResult(false);
                    return;
                }

                _pts += 4500;
                Current = _packets
                    ? CameraStreamItem.FromPacket(new CameraPacket(_data, _pts, _pts, 4500, IsKeyFrame: false, CameraTimeBase.Rtp90kHz))
                    : _grayscale is { } size && _demand is { GrayscaleDue: true }
                        ? CameraStreamItem.FromFrames(default, default, new CameraGrayscaleFrame(size.Width, size.Height, size.Width, _luma, 1280, 720))
                        : CameraStreamItem.FromFrame(new CameraFrame(4, 2, 4 * CameraFrame.BytesPerPixel, _pixels));
                _source.SetResult(true);
            }
        }
    }
}
