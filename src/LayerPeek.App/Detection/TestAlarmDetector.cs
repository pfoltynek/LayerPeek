#if DEBUG
using LayerPeek.Core.Detection;

namespace LayerPeek.App.Detection;

/// <summary>
/// Debug builds only, the test mode only (T-154, <see cref="Composition.AppEnvironment.TestFakeDetectorVariable"/>): a
/// detector that is warmed up at once and scores every sample 1, so that a print of the simulator raises a print failure
/// alarm after the confirmation window (three samples, ~6 s at 2 s), with its snapshot, notification and "False alarm". The
/// darkness check of the orchestration still applies (the simulated camera is lit).
/// </summary>
internal sealed class TestAlarmDetector : IPrintFailureDetector
{
    public const string Id = "e2e-alarm";

    public static PrintFailureDetectorRegistration Registration { get; } =
        new(Id, DetectionFrameSpec.Default, new DetectionThresholds(0.5f, 0.1f), static () => new TestAlarmDetector());

    public void Reset(in PrintFailureContext context)
    {
    }

    public DetectionResult Analyze(in DetectionSample sample) => new(IsWarmedUp: true, Score: 1);
}
#endif
