using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cooldown;

/// <summary>
/// The temperatures at or below which a printer counts as cooled down (T-079; nozzle and bed since T-109, user decision
/// 2026-10-29): set per printer (<see cref="PrinterDefinition.CooldownThresholds"/>), whole degrees Celsius within
/// <see cref="MinCelsius"/>–<see cref="MaxCelsius"/> each, <see cref="DefaultNozzleCelsius"/> /
/// <see cref="DefaultBedCelsius"/> by default.
/// </summary>
public static class CooldownThreshold
{
    public const int MinCelsius = 30;

    public const int MaxCelsius = 100;

    /// <summary>Default nozzle threshold (T-109; 50 °C for the single nozzle threshold of 0.3.0 and older).</summary>
    public const int DefaultNozzleCelsius = 40;

    /// <summary>Default bed threshold (T-109).</summary>
    public const int DefaultBedCelsius = 40;

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

    /// <exception cref="ArgumentOutOfRangeException">One of the thresholds is not <see cref="IsValid">valid</see> (also <c>default</c>).</exception>
    public static void Validate(CooldownThresholds thresholds, string? paramName = null)
    {
        Validate(thresholds.NozzleCelsius, paramName ?? nameof(thresholds));
        Validate(thresholds.BedCelsius, paramName ?? nameof(thresholds));
    }

    /// <summary>
    /// "Use these thresholds for all printers": <paramref name="printers"/> with <paramref name="thresholds"/> as the
    /// thresholds of each; nothing else changes and printers that already have them are kept as they are. Returns
    /// <paramref name="printers"/> itself when no printer changes.
    /// </summary>
    /// <exception cref="ArgumentException">The list contains <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="thresholds"/> are not valid.</exception>
    public static IReadOnlyList<PrinterDefinition> ApplyToAll(IReadOnlyList<PrinterDefinition> printers, CooldownThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(printers);
        Validate(thresholds, nameof(thresholds));
        PrinterDefinition[]? updated = null;
        for (int i = 0; i < printers.Count; i++)
        {
            PrinterDefinition? printer = printers[i] ?? throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            if (printer.CooldownThresholds != thresholds)
            {
                updated ??= [.. printers];
                updated[i] = printer with { CooldownThresholds = thresholds };
            }
        }

        return updated ?? printers;
    }
}
