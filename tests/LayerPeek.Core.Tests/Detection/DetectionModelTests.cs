using LayerPeek.Core.Detection;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Detection;

/// <summary>T-153: the bed region, thresholds of the sensitivity, detector ids and the sample.</summary>
public sealed class DetectionModelTests
{
    private static PrinterDefinition Printer(PrinterModelCode? detected = null, PrinterImageChoice? image = null) => new()
    {
        Id = "p",
        Name = "P",
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.10",
        DetectedModel = detected,
        Image = image ?? PrinterImageChoice.Automatic,
    };

    [Theory]
    [InlineData(-1, 2, 0.5, 0.5, 0, 0.99, 0.5, 0.01)]
    [InlineData(0.2, 0.3, 5, double.NaN, 0.2, 0.3, 0.8, 0.7)]
    [InlineData(0.5, 0.5, 0, 0, 0.5, 0.5, 0.01, 0.01)]
    [InlineData(double.NaN, double.NaN, double.NaN, double.NaN, 0, 0, 1, 1)]
    public void TheRegionIsClippedIntoTheFrame(double x, double y, double width, double height, double ex, double ey, double ew, double eh)
    {
        DetectionRegion region = new(x, y, width, height);

        Assert.Equal(ex, region.X, precision: 9);
        Assert.Equal(ey, region.Y, precision: 9);
        Assert.Equal(ew, region.Width, precision: 9);
        Assert.Equal(eh, region.Height, precision: 9);
    }

    [Fact]
    public void TheRegionInPixelsIsNeverEmptyNorOutside()
    {
        Assert.Equal(new DetectionPixelRegion(0, 0, 320, 180), DetectionRegion.Full.ToPixels(320, 180));
        Assert.Equal(new DetectionPixelRegion(0, 0, 320, 180), default(DetectionRegion).ToPixels(320, 180));
        Assert.Equal(new DetectionPixelRegion(48, 99, 224, 81), DetectionRegion.CoreOneBed.ToPixels(320, 180));
        Assert.Equal(new DetectionPixelRegion(317, 178, 3, 2), new DetectionRegion(0.999, 0.999, 0.01, 0.01).ToPixels(320, 180));
        Assert.Equal(new DetectionPixelRegion(0, 0, 1, 1), new DetectionRegion(0, 0, 0, 0).ToPixels(16, 16));
        Assert.True(DetectionRegion.Full.IsFull);
        Assert.False(DetectionRegion.CoreOneBed.IsFull);
    }

    [Fact]
    public void ACoreOneStartsWithTheCoreOneBedAndOtherPrintersWithTheWholeFrame()
    {
        Assert.Equal(DetectionRegion.CoreOneBed, DetectionRegion.DefaultFor(Printer(new PrinterModelCode(7, 1, 0))));
        Assert.Equal(DetectionRegion.CoreOneBed, DetectionRegion.DefaultFor(Printer(image: PrinterImageChoice.ForModel(PrinterModel.CoreOne))));
        // The model chosen for the image wins over the detected one.
        Assert.Equal(DetectionRegion.Full, DetectionRegion.DefaultFor(Printer(new PrinterModelCode(7, 1, 0), PrinterImageChoice.ForModel(PrinterModel.Mk4S))));
        Assert.Equal(DetectionRegion.Full, DetectionRegion.DefaultFor(Printer(new PrinterModelCode(8, 1, 0))));
        Assert.Equal(DetectionRegion.Full, DetectionRegion.DefaultFor(Printer()));

        PrinterDefinition coreOne = Printer(new PrinterModelCode(7, 1, 0));
        Assert.Equal(DetectionRegion.CoreOneBed, coreOne.Detection.RegionFor(coreOne));
        PrinterDetectionSettings own = new() { Region = DetectionRegion.Full };
        Assert.Equal(DetectionRegion.Full, own.RegionFor(coreOne));
    }

    [Theory]
    [InlineData(0, 0.06f)]
    [InlineData(50, 0.04f)]
    [InlineData(100, 0.02f)]
    [InlineData(-5, 0.06f)]
    [InlineData(500, 0.02f)]
    public void TheSensitivityInterpolatesTheThreshold(int sensitivity, float expected)
    {
        Assert.Equal(expected, new DetectionThresholds(0.06f, 0.02f).ThresholdFor(sensitivity), precision: 6);
    }

    [Fact]
    public void ThresholdsAndIdsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetectionThresholds(1.5f, 0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetectionThresholds(0.1f, 0.2f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetectionThresholds(float.NaN, 0f));
        Assert.True(DetectorSettings.IsValidId("bed-change"));
        Assert.False(DetectorSettings.IsValidId("Bed-Change"));
        Assert.False(DetectorSettings.IsValidId("bed change"));
        Assert.False(DetectorSettings.IsValidId(""));
        Assert.False(DetectorSettings.IsValidId(new string('a', DetectorSettings.MaxIdLength + 1)));
        Assert.Equal(100, new DetectorSettings { Sensitivity = 300 }.Sensitivity);
        Assert.Throws<ArgumentException>(() => new PrinterDetectionSettings { Detectors = new Dictionary<string, DetectorSettings> { ["Nope"] = DetectorSettings.Default } });
    }

    [Fact]
    public void SettingsWithTheSameDetectorsAreEqual()
    {
        PrinterDetectionSettings a = new() { Enabled = true, Detectors = new Dictionary<string, DetectorSettings> { ["bed-change"] = new() { Sensitivity = 10 } } };
        PrinterDetectionSettings b = new() { Enabled = true, Detectors = new Dictionary<string, DetectorSettings> { ["bed-change"] = new() { Sensitivity = 10 } } };
        PrinterDetectionSettings c = b with { Detectors = new Dictionary<string, DetectorSettings> { ["bed-change"] = new() { Sensitivity = 11 } } };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ASampleMustHoldItsRegion()
    {
        byte[] pixels = new byte[320 * 180];
        PrintFailureContext context = new(1, 0.2, 5, TimeSpan.Zero, DetectionRegion.Full);

        Assert.Throws<ArgumentOutOfRangeException>(() => new DetectionSample(pixels, 320, 180, 320, TimeSpan.Zero, new DetectionPixelRegion(300, 0, 40, 10), context));
        Assert.Throws<ArgumentException>(() => new DetectionSample(pixels.AsSpan(0, 100), 320, 180, 320, TimeSpan.Zero, new DetectionPixelRegion(0, 0, 1, 1), context));
        DetectionSample sample = new(pixels, 320, 180, 320, TimeSpan.FromSeconds(2), new DetectionPixelRegion(0, 0, 320, 180), context);
        Assert.Equal(320, sample.Row(179).Length);
    }

    [Fact]
    public void ARegistrationIsOnByDefaultAndOfferedToEveryModel()
    {
        PrintFailureDetectorRegistration registration = new("bed-change", DetectionFrameSpec.Default, new DetectionThresholds(0.5f, 0.1f), static () => null!);

        Assert.True(registration.EnabledByDefault);
        Assert.False(registration.IsExperimental);
        Assert.Null(registration.Models);
        Assert.Equal(DetectorSettings.Default, registration.DefaultSettings);
        Assert.True(registration.IsOfferedFor(PrinterModel.Unknown));
        Assert.True(registration.IsOfferedFor(PrinterModel.Mini));
    }

    [Fact]
    public void ADetectorOffByDefaultAndForSomeModelsOnly()
    {
        PrintFailureDetectorRegistration registration = new("air-printing", DetectionFrameSpec.Default, new DetectionThresholds(0.9f, 0.5f), static () => null!)
        {
            EnabledByDefault = false,
            IsExperimental = true,
            Models = [PrinterModel.CoreOne, PrinterModel.Xl],
        };
        PrinterDefinition coreOne = new() { Id = "a", Name = "a", ConnectionType = ConnectionType.PrusaLink, Address = "a", DetectedModel = new PrinterModelCode(7, 1, 0) };
        PrinterDefinition chosenMk4 = coreOne with { Image = PrinterImageChoice.ForModel(PrinterModel.Mk4) };
        PrinterDefinition unknown = coreOne with { DetectedModel = null };

        Assert.Equal(DetectorSettings.Disabled, registration.DefaultSettings);
        Assert.False(DetectorSettings.Disabled.Enabled);
        Assert.Equal(DetectorSettings.DefaultSensitivity, DetectorSettings.Disabled.Sensitivity);
        Assert.True(registration.IsOfferedFor(coreOne));
        // The model chosen for the image wins over the detected one, as for the default region.
        Assert.False(registration.IsOfferedFor(chosenMk4));
        Assert.False(registration.IsOfferedFor(unknown));
        Assert.True(registration.IsOfferedFor(PrinterModel.Xl));
        Assert.Equal(PrinterModel.CoreOne, DetectionRegion.ModelOf(coreOne));
        Assert.Equal(PrinterModel.Mk4, DetectionRegion.ModelOf(chosenMk4));
        Assert.Equal(PrinterModel.Unknown, DetectionRegion.ModelOf(unknown));
    }

    [Fact]
    public void ThePrintersSettingsOfADetectorFallBackToItsOwnDefault()
    {
        PrintFailureDetectorRegistration offByDefault = new("air-printing", DetectionFrameSpec.Default, new DetectionThresholds(0.9f, 0.5f), static () => null!) { EnabledByDefault = false };
        PrintFailureDetectorRegistration onByDefault = new("bed-change", DetectionFrameSpec.Default, new DetectionThresholds(0.5f, 0.1f), static () => null!);
        PrinterDetectionSettings none = PrinterDetectionSettings.Default;
        PrinterDetectionSettings chosen = new() { Detectors = new Dictionary<string, DetectorSettings> { ["air-printing"] = DetectorSettings.Default } };

        Assert.False(none.For(offByDefault).Enabled);
        Assert.True(none.For(onByDefault).Enabled);
        Assert.True(chosen.For(offByDefault).Enabled);
    }
}
