using System.Runtime.CompilerServices;

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
