namespace LayerPeek.Core.Printers;

/// <summary>
/// Firmware code to <see cref="PrinterModel"/> mapping and the stable names used in the settings file. Source of the
/// codes: Prusa-Firmware-Buddy <c>include/common/printer_model_data.hpp</c> (see docs/api/prusalink.md).
/// </summary>
public static class PrinterModels
{
    /// <summary>Models a user can choose manually (all except <see cref="PrinterModel.Unknown"/>), in display order.</summary>
    public static IReadOnlyList<PrinterModel> Selectable { get; } =
    [
        PrinterModel.CoreOne,
        PrinterModel.CoreOneL,
        PrinterModel.Mk4S,
        PrinterModel.Mk4,
        PrinterModel.Mk39S,
        PrinterModel.Mk39,
        PrinterModel.Mk35S,
        PrinterModel.Mk35,
        PrinterModel.XlPlus,
        PrinterModel.Xl,
        PrinterModel.Mini,
    ];

    /// <summary>
    /// The model of a firmware code; <see cref="PrinterModel.Unknown"/> for codes without an illustration (iX, Core One
    /// INDX, unreleased models) and for codes the app does not know.
    /// </summary>
    public static PrinterModel FromCode(PrinterModelCode code) => (code.Type, code.Version, code.Subversion) switch
    {
        (1, 3, 5) => PrinterModel.Mk35,
        (1, 3, 6) => PrinterModel.Mk35S,
        (1, 3, 9) => PrinterModel.Mk39,
        (1, 3, 10) => PrinterModel.Mk39S,
        (1, 4, 0) => PrinterModel.Mk4,
        (1, 4, 1) => PrinterModel.Mk4S,
        (2, 1, 0) => PrinterModel.Mini,
        (3, 1, 0) => PrinterModel.Xl,
        (3, 1, 1) => PrinterModel.XlPlus,
        // XL development kit: "works like standard XL" in the firmware source (same id string "XL").
        (5, 1, 0) => PrinterModel.Xl,
        (7, 1, 0) => PrinterModel.CoreOne,
        (8, 1, 0) => PrinterModel.CoreOneL,
        _ => PrinterModel.Unknown,
    };

    /// <summary>The model of a code as PrusaLink reports it; <see cref="PrinterModel.Unknown"/> when missing or malformed.</summary>
    public static PrinterModel FromCode(string? code) =>
        PrinterModelCode.TryParse(code, out PrinterModelCode parsed) ? FromCode(parsed) : PrinterModel.Unknown;

    /// <summary>Stable name for the settings file: the identifier (e.g. <c>Mk4S</c>, <c>CoreOneL</c>); never localized.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="model"/> is not a defined value.</exception>
    public static string Format(PrinterModel model) => model switch
    {
        // Explicit names instead of ToString/Enum.Parse: numbers must not be accepted and the format must stay stable.
        PrinterModel.Unknown => nameof(PrinterModel.Unknown),
        PrinterModel.Mini => nameof(PrinterModel.Mini),
        PrinterModel.Mk35 => nameof(PrinterModel.Mk35),
        PrinterModel.Mk35S => nameof(PrinterModel.Mk35S),
        PrinterModel.Mk39 => nameof(PrinterModel.Mk39),
        PrinterModel.Mk39S => nameof(PrinterModel.Mk39S),
        PrinterModel.Mk4 => nameof(PrinterModel.Mk4),
        PrinterModel.Mk4S => nameof(PrinterModel.Mk4S),
        PrinterModel.Xl => nameof(PrinterModel.Xl),
        PrinterModel.XlPlus => nameof(PrinterModel.XlPlus),
        PrinterModel.CoreOne => nameof(PrinterModel.CoreOne),
        PrinterModel.CoreOneL => nameof(PrinterModel.CoreOneL),
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown printer model."),
    };

    /// <summary>
    /// Product name as Prusa writes it (e.g. "MK3.9S", "Core One L", "XL+", "MINI"); a proper name, the same in every
    /// language. MINI covers MINI+ too. <see cref="PrinterModel.Unknown"/> has none (the UI shows its own text).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="model"/> is <see cref="PrinterModel.Unknown"/> or not defined.</exception>
    public static string GetProductName(PrinterModel model) => model switch
    {
        PrinterModel.Mini => "MINI",
        PrinterModel.Mk35 => "MK3.5",
        PrinterModel.Mk35S => "MK3.5S",
        PrinterModel.Mk39 => "MK3.9",
        PrinterModel.Mk39S => "MK3.9S",
        PrinterModel.Mk4 => "MK4",
        PrinterModel.Mk4S => "MK4S",
        PrinterModel.Xl => "XL",
        PrinterModel.XlPlus => "XL+",
        PrinterModel.CoreOne => "Core One",
        PrinterModel.CoreOneL => "Core One L",
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "The model has no product name."),
    };

    /// <summary>Parses a name written by <see cref="Format"/> (case-insensitive, surrounding spaces ignored); numbers are rejected.</summary>
    public static bool TryParse(string? name, out PrinterModel model)
    {
        ReadOnlySpan<char> trimmed = name.AsSpan().Trim();
        if (trimmed.Equals(Format(PrinterModel.Unknown), StringComparison.OrdinalIgnoreCase))
        {
            model = PrinterModel.Unknown;
            return true;
        }

        foreach (PrinterModel candidate in Selectable)
        {
            if (trimmed.Equals(Format(candidate), StringComparison.OrdinalIgnoreCase))
            {
                model = candidate;
                return true;
            }
        }

        model = PrinterModel.Unknown;
        return false;
    }
}
