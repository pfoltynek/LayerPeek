using Avalonia.Media;

using LayerPeek.App.Settings;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Detection;

namespace LayerPeek.App.Tests.Settings;

/// <summary>Detector registrations and last frames for the settings tests (T-154); shared with the UI tests.</summary>
internal static class DetectionFakes
{
    /// <summary>"bed-change" (T-155) as the settings see it: the texts of the real detector, a detector that never runs.</summary>
    public static PrintFailureDetectorRegistration BedChange { get; } = Registration(LayerPeek.App.Detection.DetectorTexts.BedChange);

    /// <summary>A second kind with an id this version has no texts for.</summary>
    public static PrintFailureDetectorRegistration Other { get; } = Registration("other-kind");

    public static PrintFailureDetectorRegistration Registration(string id) =>
        new(id, DetectionFrameSpec.Default, new DetectionThresholds(0.06f, 0.02f), static () => new IdleDetector());

    private sealed class IdleDetector : IPrintFailureDetector
    {
        public void Reset(in PrintFailureContext context)
        {
        }

        public DetectionResult Analyze(in DetectionSample sample) => new(false, 0);
    }
}

/// <summary>An in-memory <see cref="ILastCameraFrames"/>: saved images counted, a frame to load set by the test.</summary>
internal sealed class FakeLastCameraFrames : ILastCameraFrames
{
    private readonly Lock _gate = new();

    /// <summary>What <see cref="LoadAsync"/> returns (per printer); <see langword="null"/>: none.</summary>
    public Func<string, IImage?> Frame { get; set; } = static _ => null;

    /// <summary>Completes loads only when set (a load that has to wait); <see langword="null"/>: at once.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public List<(string PrinterId, int Width, int Height)> Saved { get; } = [];

    public List<string> Loaded { get; } = [];

    public List<string> Deleted { get; } = [];

    public void Save(string printerId, CameraImage image)
    {
        lock (_gate)
        {
            Saved.Add((printerId, image.Width, image.Height));
        }
    }

    public async Task<IImage?> LoadAsync(string printerId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Loaded.Add(printerId);
        }

        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
        }

        return Frame(printerId);
    }

    public void Delete(string printerId)
    {
        lock (_gate)
        {
            Deleted.Add(printerId);
        }
    }
}
