using LayerPeek.Core.Detection;

namespace LayerPeek.Detection;

/// <summary>
/// The detectors the app offers, an explicit list for the composition root (no scanning, Native AOT). Empty until the
/// first detector, "bed-change" (T-155), is added here; detection then has nothing to run and holds no camera lease.
/// </summary>
public static class PrintFailureDetectors
{
    /// <summary>Every detector, in the order the settings list them (T-154).</summary>
    public static IReadOnlyList<PrintFailureDetectorRegistration> All { get; } = [];
}
