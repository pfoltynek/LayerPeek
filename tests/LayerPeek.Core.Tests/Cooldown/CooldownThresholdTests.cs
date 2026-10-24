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
        CooldownThresholdCelsius = 70,
    };

    [Fact]
    public void RangeAndDefault()
    {
        Assert.Equal(30, CooldownThreshold.MinCelsius);
        Assert.Equal(100, CooldownThreshold.MaxCelsius);
        Assert.Equal(50, CooldownThreshold.DefaultCelsius);
        Assert.Equal(50, Alpha.CooldownThresholdCelsius);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    [InlineData(-50, false)]
    [InlineData(int.MaxValue, false)]
    public void PrinterDefinitionAcceptsOnlyTheRange(int celsius, bool valid)
    {
        Assert.Equal(valid, CooldownThreshold.IsValid(celsius));
        if (valid)
        {
            Assert.Equal(celsius, (Alpha with { CooldownThresholdCelsius = celsius }).CooldownThresholdCelsius);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Alpha with { CooldownThresholdCelsius = celsius });
        }
    }

    [Fact]
    public void ThresholdTakesPartInEquality()
    {
        Assert.NotEqual(Alpha, Alpha with { CooldownThresholdCelsius = 60 });
        Assert.Equal(Alpha with { CooldownThresholdCelsius = 60 }, Alpha with { CooldownThresholdCelsius = 60 });
    }

    [Fact]
    public void ApplyToAllChangesOnlyTheThreshold()
    {
        IReadOnlyList<PrinterDefinition> printers = CooldownThreshold.ApplyToAll([Alpha, Beta], 42);

        Assert.Equal([Alpha with { CooldownThresholdCelsius = 42 }, Beta with { CooldownThresholdCelsius = 42 }], printers);
    }

    [Fact]
    public void ApplyToAllReturnsTheSameListWhenNothingChanges()
    {
        PrinterDefinition[] printers = [Alpha with { CooldownThresholdCelsius = 70 }, Beta];

        Assert.Same(printers, CooldownThreshold.ApplyToAll(printers, 70));
        Assert.Empty(CooldownThreshold.ApplyToAll([], 70));
    }

    [Fact]
    public void ApplyToAllKeepsUnchangedPrintersAsTheyAre()
    {
        IReadOnlyList<PrinterDefinition> printers = CooldownThreshold.ApplyToAll([Alpha, Beta], 70);

        Assert.Same(Beta, printers[1]);
        Assert.Equal(70, printers[0].CooldownThresholdCelsius);
    }

    [Fact]
    public void ApplyToAllRejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => CooldownThreshold.ApplyToAll(null!, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => CooldownThreshold.ApplyToAll([Alpha], 29));
        Assert.Throws<ArgumentOutOfRangeException>(() => CooldownThreshold.ApplyToAll([Alpha], 101));
        Assert.Throws<ArgumentException>(() => CooldownThreshold.ApplyToAll([Alpha, null!], 50));
    }

    [Fact]
    public void AppSettingsApplyToAllPrinters()
    {
        AppSettings settings = new() { Printers = [Alpha, Beta], DebugLogging = true };

        AppSettings updated = settings.WithCooldownThresholdForAllPrinters(35);

        Assert.Equal(settings with { Printers = [Alpha with { CooldownThresholdCelsius = 35 }, Beta with { CooldownThresholdCelsius = 35 }] }, updated);
        Assert.Same(updated, updated.WithCooldownThresholdForAllPrinters(35));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.WithCooldownThresholdForAllPrinters(200));
    }
}
