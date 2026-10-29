namespace LayerPeek.App.Diagnostics.Chart;

/// <summary>
/// Maps temperatures <see cref="Low"/> – <see cref="High"/> to the vertical pixel range that ends at
/// <see cref="Bottom"/> and is <see cref="Height"/> high (higher values further up). The full chart has one band from
/// 0 °C over the whole plot; the compact chart has one band per series (<see cref="CompactBands"/>).
/// </summary>
/// <param name="Bottom">Y of <see cref="Low"/>.</param>
/// <param name="Height">Pixels between <see cref="Low"/> and <see cref="High"/> (positive).</param>
/// <param name="Low">Value at the bottom.</param>
/// <param name="High">Value at the top (greater than <see cref="Low"/>).</param>
public readonly record struct ValueBand(double Bottom, double Height, double Low, double High)
{
    /// <summary>Y of <see cref="High"/>.</summary>
    public double Top => Bottom - Height;

    /// <summary>Y of <paramref name="value"/>; values outside the range land above or below the band.</summary>
    public double ToY(double value) => Bottom - ((value - Low) / (High - Low) * Height);
}

/// <summary>
/// Layout of the compact chart (T-106): every series with values in the shown range gets its own horizontal band
/// (nozzle on top, bed below), scaled to that series' own minimum and maximum, so that a fluctuation of 1–2 °C is
/// visible on a chart only ~60 px high. The full chart in the diagnostics window keeps its axis from 0 °C.
/// </summary>
public static class CompactBands
{
    /// <summary>
    /// The smallest value range of a band (°C), centred on the data when the data spans less. Prusa printers hold the
    /// nozzle and the bed within about ±1 °C of the target and report tenths of a degree: over 6 °C a band of the
    /// smallest compact chart (52 px for two bands, ~24 px each) shows 1 °C as ~4 px, while 0.1 °C sensor noise stays
    /// below half a pixel and does not turn into waves. A wider range (heating, cooling) just scales the band.
    /// </summary>
    public const double MinimumSpan = 6;

    /// <summary>Empty pixels between two bands, so that the lines of neighbouring bands never touch.</summary>
    public const double Gap = 4;

    /// <summary>
    /// The value range of a band for data from <paramref name="min"/> to <paramref name="max"/>: the data itself when it
    /// spans at least <see cref="MinimumSpan"/>, otherwise <see cref="MinimumSpan"/> centred on the data (a single value
    /// lies in the middle). <see langword="false"/> when the series has no finite value (no band).
    /// </summary>
    public static bool TryGetRange(float min, float max, out double low, out double high)
    {
        if (!float.IsFinite(min) || !float.IsFinite(max) || min > max)
        {
            low = high = double.NaN;
            return false;
        }

        low = min;
        high = max;
        if (high - low < MinimumSpan)
        {
            double centre = (low + high) / 2;
            low = centre - (MinimumSpan / 2);
            high = centre + (MinimumSpan / 2);
        }

        return true;
    }

    /// <summary>
    /// Pixel extent of band <paramref name="index"/> (0 = top) of <paramref name="count"/> equal bands stacked within
    /// <paramref name="top"/> – <paramref name="top"/> + <paramref name="height"/>, <see cref="Gap"/> apart. A band is at
    /// least 1 px high, even when the chart is too small for the gaps.
    /// </summary>
    public static (double Top, double Height) Slot(int index, int count, double top, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
        double slot = Math.Max(1, (height - (Gap * (count - 1))) / count);
        return (top + (index * (slot + Gap)), slot);
    }

    /// <summary>
    /// The band of a series in slot <paramref name="slotTop"/> / <paramref name="slotHeight"/>: its value range
    /// (<see cref="TryGetRange"/>) mapped to the slot shrunk by <paramref name="inset"/> at the top and the bottom, so
    /// that a line of that half thickness at the extremes is not cut off. Returns <see langword="false"/> without data.
    /// </summary>
    public static bool TryCreate(float min, float max, double slotTop, double slotHeight, double inset, out ValueBand band)
    {
        if (!TryGetRange(min, max, out double low, out double high))
        {
            band = default;
            return false;
        }

        double margin = Math.Clamp(inset, 0, Math.Max(0, (slotHeight - 1) / 2));
        band = new ValueBand(slotTop + slotHeight - margin, slotHeight - (2 * margin), low, high);
        return true;
    }
}
