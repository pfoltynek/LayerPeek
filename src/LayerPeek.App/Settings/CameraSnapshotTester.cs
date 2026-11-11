using Microsoft.Extensions.Logging;

using LayerPeek.App.Platform;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Settings;

/// <summary>
/// Tests a camera with <see cref="CameraSnapshot.CaptureAsync"/>: success once the first image arrived (the result carries
/// that image for the preview, T-034; a failure or timeout carries none). The capture runs
/// on the thread pool (a decoder may block while connecting) and is abandoned at the timeout or cancellation even if the
/// decoder does not react to its token in time. The address is logged redacted only.
/// <para>
/// T-143 (user decision 2026-11-03, 3): the saved, enabled camera of the edited printer (the address the hub was configured
/// with) is tested through the <see cref="CameraStreamHub"/>, sharing an open camera window's connection; its first image
/// after subscribing is the result and the first error status a failure. An unsaved address always gets a connection of its own.
/// </para>
/// <para>
/// T-194 (user decision 2026-11-11): like opening the camera window, the test of a saved camera ends a reconnect wait of
/// the shared connection (<see cref="CameraFrameSubscriptionOptions.ShortensReconnectWait"/>): the attempt starts at once
/// with the backoff reset, so the test sees a fresh attempt instead of the waiting connection's old failure.
/// </para>
/// </summary>
internal sealed partial class CameraSnapshotTester : ICameraTester
{
    /// <summary>User decision (T-014).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly ICameraFeedFactory _factory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CameraSnapshotTester> _logger;
    private readonly ILocalNetworkPrivacy _localNetwork;
    private readonly CameraStreamHub? _hub;

    /// <param name="hub">The shared connections for saved cameras (T-143); <see langword="null"/>: every test connects by itself.</param>
    public CameraSnapshotTester(
        ICameraFeedFactory factory,
        TimeProvider timeProvider,
        ILogger<CameraSnapshotTester> logger,
        TimeSpan? timeout = null,
        ILocalNetworkPrivacy? localNetwork = null,
        CameraStreamHub? hub = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _factory = factory;
        _timeProvider = timeProvider;
        _logger = logger;
        _localNetwork = localNetwork ?? NoLocalNetworkPrivacy.Instance;
        _hub = hub;
        Timeout = timeout ?? DefaultTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Timeout, TimeSpan.Zero, nameof(timeout));
    }

    public TimeSpan Timeout { get; }

    /// <summary>The subject of the local network log line (T-097): the address is not logged there.</summary>
    internal const string CameraSubject = "the tested camera";

    public Task<CameraTestResult> TestAsync(Uri streamUri, CancellationToken cancellationToken) =>
        TestAsync(streamUri, printerId: null, cancellationToken);

    public async Task<CameraTestResult> TestAsync(Uri streamUri, string? printerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(streamUri);
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource timeout = new(Timeout, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        CancellationToken token = linked.Token;
        string address = UriRedaction.Redact(streamUri);
        try
        {
            Task<CameraImage> capture = printerId is not null && _hub is { } hub && hub.IsConfigured(printerId, streamUri)
                ? CaptureSharedAsync(hub, printerId, streamUri, token)
                : Task.Run(() => CameraSnapshot.CaptureAsync(_factory, streamUri, token), CancellationToken.None);
            // A capture abandoned at the timeout may still fail later; its exception must not go unobserved.
            _ = capture.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            CameraImage image = await capture.WaitAsync(token).ConfigureAwait(false);
            return new CameraTestResult(CameraTestOutcome.Success, image.Width, image.Height, Image: image);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            LogTimedOut(address, Timeout.TotalSeconds);
            return new CameraTestResult(CameraTestOutcome.Timeout);
        }
        catch (CameraFeedException ex)
        {
            LogFailed(ex, address);
            // The message is meant for the user and never contains the address (ICameraFeed contract).
            // T-097: "no route to host" to a camera in the local network may be the denied Local Network permission (macOS 15+).
            CameraTestOutcome outcome = ex.IsHostUnreachable && _localNetwork.MayBeDenied(streamUri.Host, CameraSubject)
                ? CameraTestOutcome.LocalNetworkAccessMayBeOff
                : CameraTestOutcome.Failed;
            return new CameraTestResult(outcome, Reason: ex.Message);
        }
        catch (Exception ex)
        {
            // Other messages may contain the address or credentials: the result gets only "failed", the log the details.
            LogFailed(ex, address);
            return new CameraTestResult(CameraTestOutcome.Failed);
        }
    }

    /// <summary>
    /// The first image of the saved camera's shared connection after subscribing; the first error status fails like a
    /// failed connection of its own. Taken during a reconnect wait, the lease starts the next attempt at once (T-194). The
    /// lease is released at the end (the hub keeps the connection for its linger time).
    /// </summary>
    internal static async Task<CameraImage> CaptureSharedAsync(CameraStreamHub hub, string printerId, Uri streamUri, CancellationToken cancellationToken)
    {
        using SemaphoreSlim frameReady = new(0);
        // Completed with the failure (never faulted, so nothing is left unobserved when the test ends otherwise).
        TaskCompletionSource<CameraFeedException> failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CameraFrameSubscription lease = hub.SubscribeFrames(printerId, streamUri, new CameraFrameSubscriptionOptions
        {
            FrameAvailable = () => Signal(frameReady),
            Status = new FailureObserver(failed),
            Ended = _ => failed.TrySetResult(new CameraFeedException("The camera stream ended before sending an image.")),
            ShortensReconnectWait = true,
        });
        using (lease)
        {
            if (lease.IsEnded)
            {
                // The camera is no longer configured, or the app is quitting.
                throw new CameraFeedException("The camera stream is not available.");
            }

            while (true)
            {
                Task wait = frameReady.WaitAsync(cancellationToken);
                if (await Task.WhenAny(wait, failed.Task).ConfigureAwait(false) == failed.Task)
                {
                    throw await failed.Task.ConfigureAwait(false);
                }

                await wait.ConfigureAwait(false);
                if (lease.TakeLatest() is { } frame)
                {
                    return frame.ToImage();
                }
            }
        }

        static void Signal(SemaphoreSlim frameReady)
        {
            try
            {
                frameReady.Release();
            }
            catch (ObjectDisposedException)
            {
                // A frame that arrived while the test ended.
            }
        }
    }

    /// <summary>Fails the shared capture on the first error status (with the feed's reason, never the address).</summary>
    private sealed class FailureObserver(TaskCompletionSource<CameraFeedException> failed) : IProgress<CameraStatus>
    {
        public void Report(CameraStatus value)
        {
            if (value.State == CameraConnectionState.Error)
            {
                failed.TrySetResult(new CameraFeedException(value.Reason ?? "The camera stream failed.") { IsHostUnreachable = value.IsHostUnreachable });
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera test of '{Address}' got no image within {TimeoutSeconds} s.")]
    private partial void LogTimedOut(string address, double timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera test of '{Address}' failed.")]
    private partial void LogFailed(Exception exception, string address);
}
