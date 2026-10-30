using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Tests.Cooldown;

public sealed class CooldownThresholdTests
{
    private static readonly PrinterDefinition Alpha = new() { Id = "a", Name = "Alpha", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" };

    private static readonly PrinterDefinition Beta = new()
    {
        Id = "b",
        Name = "Beta",
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.1.21",
        UserName = "maker",
        Camera = new CameraSettings(enabled: false, new Uri("rtsp://192.168.1.30/live")),
        DetectedModel = new PrinterModelCode(1, 4, 0),
        Image = PrinterImageChoice.Custom,
        CooldownThresholds = new CooldownThresholds(70, 55),
    };

    [Fact]
    public void RangeAndDefaults()
    {
        Assert.Equal(30, CooldownThreshold.MinCelsius);
        Assert.Equal(100, CooldownThreshold.MaxCelsius);
        // T-109 (user decision 2026-10-29): nozzle 40 °C and bed 40 °C.
        Assert.Equal(40, CooldownThreshold.DefaultNozzleCelsius);
        Assert.Equal(40, CooldownThreshold.DefaultBedCelsius);
        Assert.Equal(new CooldownThresholds(40, 40), CooldownThresholds.Default);
        Assert.Equal(CooldownThresholds.Default, Alpha.CooldownThresholds);
        Assert.True(CooldownThresholds.Default.IsValid);
        Assert.False(default(CooldownThresholds).IsValid);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    [InlineData(-50, false)]
    [InlineData(int.MaxValue, false)]
    public void PrinterDefinitionAcceptsOnlyTheRangeForBoth(int celsius, bool valid)
    {
        Assert.Equal(valid, CooldownThreshold.IsValid(celsius));
        CooldownThresholds nozzle = new(celsius, 40);
        CooldownThresholds bed = new(40, celsius);
        Assert.Equal(valid, nozzle.IsValid);
        Assert.Equal(valid, bed.IsValid);
        if (valid)
        {
            Assert.Equal(nozzle, (Alpha with { CooldownThresholds = nozzle }).CooldownThresholds);
            Assert.Equal(bed, (Alpha with { CooldownThresholds = bed }).CooldownThresholds);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Alpha with { CooldownThresholds = nozzle });
            Assert.Throws<ArgumentOutOfRangeException>(() => Alpha with { CooldownThresholds = bed });
        }
    }

    [Fact]
    public void DefaultStructIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Alpha with { CooldownThresholds = default });

    [Fact]
    public void ThresholdsTakePartInEquality()
    {
        Assert.NotEqual(Alpha, Alpha with { CooldownThresholds = new(60, 40) });
        Assert.NotEqual(Alpha, Alpha with { CooldownThresholds = new(40, 60) });
        Assert.Equal(Alpha with { CooldownThresholds = new(60, 45) }, Alpha with { CooldownThresholds = new(60, 45) });
    }

    [Fact]
    public void ApplyToAllChangesOnlyTheThresholds()
    {
        CooldownThresholds thresholds = new(42, 35);

        IReadOnlyList<PrinterDefinition> printers = CooldownThreshold.ApplyToAll([Alpha, Beta], thresholds);

        Assert.Equal([Alpha with { CooldownThresholds = thresholds }, Beta with { CooldownThresholds = thresholds }], printers);
    }

    [Fact]
    public void ApplyToAllCopiesBothThresholdsTogether()
    {
        // A printer with the same nozzle threshold but another bed threshold changes too.
        PrinterDefinition sameNozzle = Alpha with { CooldownThresholds = new(70, 40) };

        IReadOnlyList<PrinterDefinition> printers = CooldownThreshold.ApplyToAll([sameNozzle, Beta], Beta.CooldownThresholds);

        Assert.Equal(new CooldownThresholds(70, 55), printers[0].CooldownThresholds);
        Assert.Same(Beta, printers[1]);
    }

    [Fact]
    public void ApplyToAllReturnsTheSameListWhenNothingChanges()
    {
        PrinterDefinition[] printers = [Alpha with { CooldownThresholds = new(70, 55) }, Beta];

        Assert.Same(printers, CooldownThreshold.ApplyToAll(printers, new CooldownThresholds(70, 55)));
        Assert.Empty(CooldownThreshold.ApplyToAll([], new CooldownThresholds(70, 55)));
    }

    [Fact]
    public void ApplyToAllRejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => CooldownThreshold.ApplyToAll(null!, CooldownThresholds.Default));
        Assert.Throws<ArgumentOutOfRangeException>(() => CooldownThreshold.ApplyToAll([Alpha], new CooldownThresholds(29, 40)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CooldownThreshold.ApplyToAll([Alpha], new CooldownThresholds(40, 101)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CooldownThreshold.ApplyToAll([Alpha], default));
        Assert.Throws<ArgumentException>(() => CooldownThreshold.ApplyToAll([Alpha, null!], CooldownThresholds.Default));
    }

    [Fact]
    public void AppSettingsApplyToAllPrinters()
    {
        AppSettings settings = new() { Printers = [Alpha, Beta], DebugLogging = true };
        CooldownThresholds thresholds = new(35, 45);

        AppSettings updated = settings.WithCooldownThresholdsForAllPrinters(thresholds);

        Assert.Equal(settings with { Printers = [Alpha with { CooldownThresholds = thresholds }, Beta with { CooldownThresholds = thresholds }] }, updated);
        Assert.Same(updated, updated.WithCooldownThresholdsForAllPrinters(thresholds));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.WithCooldownThresholdsForAllPrinters(new CooldownThresholds(40, 200)));
    }
}
