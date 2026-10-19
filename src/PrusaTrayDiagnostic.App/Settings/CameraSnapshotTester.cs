using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Settings;

/// <summary>
/// Tests a camera with <see cref="CameraSnapshot.CaptureAsync"/>: success once the first image arrived (the result carries
/// that image for the preview, T-034; a failure or timeout carries none). The capture runs
/// on the thread pool (a decoder may block while connecting) and is abandoned at the timeout or cancellation even if the
/// decoder does not react to its token in time. The address is logged redacted only.
/// </summary>
internal sealed partial class CameraSnapshotTester : ICameraTester
{
    /// <summary>User decision (T-014).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly ICameraFeedFactory _factory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CameraSnapshotTester> _logger;

    public CameraSnapshotTester(ICameraFeedFactory factory, TimeProvider timeProvider, ILogger<CameraSnapshotTester> logger, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _factory = factory;
        _timeProvider = timeProvider;
        _logger = logger;
        Timeout = timeout ?? DefaultTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Timeout, TimeSpan.Zero, nameof(timeout));
    }

    public TimeSpan Timeout { get; }

    public async Task<CameraTestResult> TestAsync(Uri streamUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(streamUri);
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource timeout = new(Timeout, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        CancellationToken token = linked.Token;
        string address = UriRedaction.Redact(streamUri);
        try
        {
            Task<CameraImage> capture = Task.Run(() => CameraSnapshot.CaptureAsync(_factory, streamUri, token), CancellationToken.None);
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
            return new CameraTestResult(CameraTestOutcome.Failed, Reason: ex.Message);
        }
        catch (Exception ex)
        {
            // Other messages may contain the address or credentials: the result gets only "failed", the log the details.
            LogFailed(ex, address);
            return new CameraTestResult(CameraTestOutcome.Failed);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera test of '{Address}' got no image within {TimeoutSeconds} s.")]
    private partial void LogTimedOut(string address, double timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera test of '{Address}' failed.")]
    private partial void LogFailed(Exception exception, string address);
}
