using LayerPeek.App.Diagnostics.Chart;

namespace LayerPeek.App.Tests.Diagnostics;

/// <summary>Band scaling of the compact chart (T-106).</summary>
public sealed class CompactBandsTests
{
    [Fact]
    public void SmallFluctuationGetsTheMinimumSpanCentredOnTheData()
    {
        Assert.True(CompactBands.TryGetRange(214f, 216f, out double low, out double high));

        Assert.Equal(CompactBands.MinimumSpan, high - low, 6);
        Assert.Equal(212, low, 6);
        Assert.Equal(218, high, 6);
    }

    [Fact]
    public void AsymmetricDataStaysCentred()
    {
        Assert.True(CompactBands.TryGetRange(59.5f, 60.5f, out double low, out double high));

        Assert.Equal(57, low, 6);
        Assert.Equal(63, high, 6);
    }

    [Fact]
    public void SingleValueLiesInTheMiddle()
    {
        Assert.True(CompactBands.TryGetRange(215.5f, 215.5f, out double low, out double high));
        Assert.True(CompactBands.TryCreate(215.5f, 215.5f, slotTop: 10, slotHeight: 30, inset: 0, out ValueBand band));

        Assert.Equal(CompactBands.MinimumSpan, high - low, 6);
        Assert.Equal(25, band.ToY(215.5), 6);
    }

    [Fact]
    public void HeatingRampJustScalesTheBand()
    {
        Assert.True(CompactBands.TryGetRange(25f, 215f, out double low, out double high));

        Assert.Equal(25, low);
        Assert.Equal(215, high);
    }

    [Fact]
    public void ExactlyTheMinimumSpanIsKept()
    {
        Assert.True(CompactBands.TryGetRange(212f, 218f, out double low, out double high));

        Assert.Equal(212, low);
        Assert.Equal(218, high);
    }

    [Theory]
    [InlineData(float.NaN, float.NaN)]
    [InlineData(float.NaN, 215f)]
    [InlineData(215f, float.NaN)]
    [InlineData(float.PositiveInfinity, float.PositiveInfinity)]
    [InlineData(216f, 214f)]
    public void NoFiniteDataHasNoBand(float min, float max)
    {
        Assert.False(CompactBands.TryGetRange(min, max, out double low, out double high));
        Assert.True(double.IsNaN(low) && double.IsNaN(high));
        Assert.False(CompactBands.TryCreate(min, max, 0, 30, 1, out _));
    }

    [Fact]
    public void OneDegreeIsSeveralPixelsInTheSmallestBand()
    {
        // The mini strip's chart row (52 px) with two bands; the line is 1.5 thick.
        (double top, double height) = CompactBands.Slot(0, 2, 0, 52);
        Assert.True(CompactBands.TryCreate(214f, 216f, top, height, inset: 0.75, out ValueBand band));

        Assert.Equal(24, height, 6);
        Assert.True(band.ToY(215) - band.ToY(216) >= 3.5, "1 °C must be clearly visible.");
        // 0.1 °C sensor noise stays below half a pixel.
        Assert.True(band.ToY(215) - band.ToY(215.1) < 0.5, "0.1 °C must not become a wave.");
    }

    [Fact]
    public void BandMapsItsRangeToTheSlotShrunkByTheInset()
    {
        Assert.True(CompactBands.TryCreate(25f, 215f, slotTop: 28, slotHeight: 24, inset: 1, out ValueBand band));

        Assert.Equal(51, band.Bottom);
        Assert.Equal(22, band.Height);
        Assert.Equal(29, band.Top);
        Assert.Equal(51, band.ToY(25), 6);
        Assert.Equal(29, band.ToY(215), 6);
        Assert.Equal(40, band.ToY(120), 6);
    }

    [Fact]
    public void InsetNeverLeavesLessThanOnePixel()
    {
        Assert.True(CompactBands.TryCreate(20f, 30f, slotTop: 0, slotHeight: 3, inset: 5, out ValueBand band));

        Assert.Equal(1, band.Height, 6);
        Assert.Equal(2, band.Bottom, 6);
    }

    [Fact]
    public void SlotsStackTopDownWithGaps()
    {
        Assert.Equal((0d, 52d), CompactBands.Slot(0, 1, 0, 52));
        Assert.Equal((0d, 24d), CompactBands.Slot(0, 2, 0, 52));
        Assert.Equal((24 + CompactBands.Gap, 24d), CompactBands.Slot(1, 2, 0, 52));
        Assert.Equal((10d, 30d), CompactBands.Slot(0, 2, 10, 64));
    }

    [Fact]
    public void TinyChartStillGetsOnePixelBands()
    {
        (double top, double height) = CompactBands.Slot(1, 2, 0, 3);

        Assert.Equal(1, height);
        Assert.Equal(1 + CompactBands.Gap, top);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(2, 2)]
    [InlineData(0, 0)]
    public void SlotRejectsInvalidIndices(int index, int count) =>
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => CompactBands.Slot(index, count, 0, 52));

    [Fact]
    public void FullAxisFromZeroIsTheDiagnosticsMapping()
    {
        // The diagnostics chart: 0 °C at the bottom, the axis top at the top (unchanged by T-106).
        ValueBand axis = new(Bottom: 200, Height: 180, Low: 0, High: 250);

        Assert.Equal(200, axis.ToY(0));
        Assert.Equal(20, axis.ToY(250));
        Assert.Equal(200 - (215 / 250d * 180), axis.ToY(215));
    }
}
