using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;

using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Tests.Allocation;

namespace PrusaTrayDiagnostic.Core.Tests.Cameras;

/// <summary>Allocation guarantees of <see cref="ReconnectingCameraFeed"/>; isolated, see <see cref="AllocationSensitive"/>.</summary>
[Collection(nameof(AllocationSensitive))]
public sealed class ReconnectingCameraFeedAllocationTests
{
    [Fact]
    public async Task ForwardingFramesDoesNotAllocate()
    {
        const int PerPass = 256;
        SynchronousCameraFeed inner = new();
        ReconnectingCameraFeed camera = new(new SingleStreamFactory(inner), TimeProvider.System, NullLogger<ReconnectingCameraFeed>.Instance);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        IAsyncEnumerator<CameraFrame> frames = camera.ReadFramesAsync(new Uri("rtsp://192.168.0.227/live"), status: null, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        try
        {
            // The first frame connects (allocates the attempt state).
            Assert.True(await frames.MoveNextAsync());

            bool allSynchronous = true;
            long allocated = AllocationProbe.MinAllocated(() => AllocationProbe.Measure(() =>
            {
                for (int i = 0; i < PerPass; i++)
                {
                    ValueTask<bool> move = frames.MoveNextAsync();
                    allSynchronous &= move.IsCompletedSuccessfully && move.Result;
                }
            }));

            Assert.True(allSynchronous);
            Assert.Equal(0, allocated);
        }
        finally
        {
            await cancellation.CancelAsync();
            await frames.DisposeAsync();
        }
    }

    [Fact]
    public async Task ForwardingAsynchronouslyCompletingFramesDoesNotAllocate()
    {
        // Like a real decoder: every MoveNextAsync completes later, on another thread.
        const int PerPass = 256;
        using AsynchronousCameraFeed inner = new();
        ReconnectingCameraFeed camera = new(new SingleStreamFactory(inner), TimeProvider.System, NullLogger<ReconnectingCameraFeed>.Instance);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // On the thread pool: no synchronization context, continuations stay on the producer thread.
        long allocated = await Task.Run(
            async () =>
            {
                IAsyncEnumerator<CameraFrame> frames = camera.ReadFramesAsync(new Uri("rtsp://192.168.0.227/live"), status: null, cancellation.Token)
                    .GetAsyncEnumerator(cancellation.Token);
                try
                {
                    // Warm-up: connecting allocates the attempt state, the first awaits fill the pools.
                    for (int i = 0; i < 16; i++)
                    {
                        Assert.True(await frames.MoveNextAsync());
                    }

                    long minimum = long.MaxValue;
                    // Process-wide counter: a stray allocation on another thread (e.g. under a full parallel test run)
                    // does not repeat on every pass, a per-frame allocation would.
                    for (int pass = 0; pass < 8 && minimum > 0; pass++)
                    {
                        long before = GC.GetTotalAllocatedBytes(precise: true);
                        for (int i = 0; i < PerPass; i++)
                        {
                            Assert.True(await frames.MoveNextAsync());
                        }

                        minimum = Math.Min(minimum, GC.GetTotalAllocatedBytes(precise: true) - before);
                    }

                    return minimum;
                }
                finally
                {
                    await cancellation.CancelAsync();
                    await frames.DisposeAsync();
                }
            },
            TestContext.Current.CancellationToken);

        Assert.True(inner.CompletedAsynchronously > 0);
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// Endless frames, each completed asynchronously by a producer thread through a reusable value task source, so that
    /// the fake itself allocates nothing per frame.
    /// </summary>
    private sealed class AsynchronousCameraFeed : ICameraFeed, IAsyncEnumerable<CameraFrame>, IAsyncEnumerator<CameraFrame>, IValueTaskSource<bool>, IDisposable
    {
        private readonly byte[] _buffer = new byte[2 * 2 * CameraFrame.BytesPerPixel];
        private readonly SemaphoreSlim _request = new(0);
        private readonly Thread _producer;
        private ManualResetValueTaskSourceCore<bool> _source;
        private volatile bool _stopped;
        private int _completedAsynchronously;

        public AsynchronousCameraFeed()
        {
            _producer = new Thread(Produce) { IsBackground = true };
            _producer.Start();
        }

        public int CompletedAsynchronously => Volatile.Read(ref _completedAsynchronously);

        public CameraFrame Current => new(2, 2, 2 * CameraFrame.BytesPerPixel, _buffer);

        public IAsyncEnumerable<CameraFrame> ReadFramesAsync(CancellationToken cancellationToken) => this;

        public IAsyncEnumerator<CameraFrame> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync()
        {
            _source.Reset();
            _request.Release();
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
            _request.Release();
            // Continuations run inline on the producer thread, so Dispose may be called on it: it then ends by itself.
            if (Thread.CurrentThread != _producer)
            {
                _producer.Join();
                _request.Dispose();
            }
        }

        private void Produce()
        {
            while (true)
            {
                _request.Wait();
                if (_stopped)
                {
                    return;
                }

                Interlocked.Increment(ref _completedAsynchronously);
                _source.SetResult(true);
            }
        }
    }

    /// <summary>Delivers an endless sequence of frames synchronously from one reused buffer.</summary>
    private sealed class SynchronousCameraFeed : ICameraFeed
    {
        private readonly byte[] _buffer = new byte[2 * 2 * CameraFrame.BytesPerPixel];

        public async IAsyncEnumerable<CameraFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            while (!cancellationToken.IsCancellationRequested)
            {
                yield return new CameraFrame(2, 2, 2 * CameraFrame.BytesPerPixel, _buffer);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SingleStreamFactory(ICameraFeed stream) : ICameraFeedFactory
    {
        public ICameraFeed Create(Uri streamUri) => stream;
    }
}
