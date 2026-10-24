using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Settings;

/// <summary>Outcome of <see cref="ICameraTester.TestAsync"/>. Public only because xUnit theory data needs it.</summary>
public enum CameraTestOutcome
{
    /// <summary>The stream opened and an image arrived.</summary>
    Success,

    /// <summary>No image arrived within the test timeout.</summary>
    Timeout,

    /// <summary>Opening or reading the stream failed.</summary>
    Failed,
}

/// <param name="Width">Image width in pixels (success only).</param>
/// <param name="Height">Image height in pixels (success only).</param>
/// <param name="Reason">
/// Short technical English reason of a failure (from the decoder, never containing the address); <see langword="null"/>
/// when unknown.
/// </param>
/// <param name="Image">The first image (success only, T-034): the snapshot shown on the Camera sub-tab.</param>
internal sealed record CameraTestResult(CameraTestOutcome Outcome, int Width = 0, int Height = 0, string? Reason = null, CameraImage? Image = null);

/// <summary>"Test" button of the camera section in the settings window: opens the stream once and waits for an image.</summary>
internal interface ICameraTester
{
    /// <summary>How long a test waits for the first image (user decision, T-014: 15 s).</summary>
    TimeSpan Timeout { get; }

    /// <summary>Never blocks the caller (all stream work runs on the thread pool) and never throws for stream failures.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<CameraTestResult> TestAsync(Uri streamUri, CancellationToken cancellationToken);
}
