using LayerPeek.Core.Detection;

namespace LayerPeek.Detection;

/// <summary>
/// The detectors the app offers, an explicit list for the composition root (no scanning, Native AOT): "Change on the bed"
/// (<see cref="BedChangeDetector"/>, T-155) and "Printing into the air" (<see cref="AirPrintingDetector"/>, T-167).
/// </summary>
public static class PrintFailureDetectors
{
    /// <summary>Every detector, in the order the settings list them (T-154).</summary>
    public static IReadOnlyList<PrintFailureDetectorRegistration> All { get; } = [BedChangeDetector.Registration, AirPrintingDetector.Registration];
}
