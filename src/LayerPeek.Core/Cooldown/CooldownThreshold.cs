using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cooldown;

/// <summary>
/// The nozzle temperature at or below which a printer counts as cooled down (T-079, user decision 2026-10-24): set per
/// printer (<see cref="PrinterDefinition.CooldownThresholdCelsius"/>), whole degrees Celsius within
/// <see cref="MinCelsius"/>–<see cref="MaxCelsius"/>, <see cref="DefaultCelsius"/> by default.
/// </summary>
public static class CooldownThreshold
{
    public const int MinCelsius = 30;

    public const int MaxCelsius = 100;

    public const int DefaultCelsius = 50;

    public static bool IsValid(int celsius) => celsius is >= MinCelsius and <= MaxCelsius;

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="celsius"/> is not <see cref="IsValid">valid</see>.</exception>
    public static void Validate(int celsius, string? paramName = null)
    {
        if (!IsValid(celsius))
        {
            // A programming error (the settings window validates the field with its own translated text), never shown
            // in the UI, so a normal space before "°C".
            throw new ArgumentOutOfRangeException(paramName ?? nameof(celsius), celsius, $"The cool-down threshold must be within {MinCelsius}–{MaxCelsius} °C.");
        }
    }

    /// <summary>
    /// "Use this threshold for all printers": <paramref name="printers"/> with <paramref name="thresholdCelsius"/> as the
    /// threshold of each; nothing else changes and printers that already have it are kept as they are. Returns
    /// <paramref name="printers"/> itself when no printer changes.
    /// </summary>
    /// <exception cref="ArgumentException">The list contains <see langword="null"/>.</exception>
    public static IReadOnlyList<PrinterDefinition> ApplyToAll(IReadOnlyList<PrinterDefinition> printers, int thresholdCelsius)
    {
        ArgumentNullException.ThrowIfNull(printers);
        Validate(thresholdCelsius, nameof(thresholdCelsius));
        PrinterDefinition[]? updated = null;
        for (int i = 0; i < printers.Count; i++)
        {
            PrinterDefinition? printer = printers[i] ?? throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            if (printer.CooldownThresholdCelsius != thresholdCelsius)
            {
                updated ??= [.. printers];
                updated[i] = printer with { CooldownThresholdCelsius = thresholdCelsius };
            }
        }

        return updated ?? printers;
    }
}
