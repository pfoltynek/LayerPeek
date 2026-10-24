namespace LayerPeek.Core.Printers;

/// <summary>
/// Printer model, as far as the app tells models apart (each known model gets its own illustration). The mapping from
/// the firmware code is <see cref="PrinterModels.FromCode(PrinterModelCode)"/>; see docs/api/prusalink.md.
/// </summary>
/// <remarks>
/// Values are never persisted as numbers (settings use <see cref="PrinterModels.Format"/>), so new models can be added
/// anywhere. The detected firmware code is stored as such, so a later version can map codes this one does not know.
/// </remarks>
public enum PrinterModel
{
    /// <summary>Not detected, not reported (firmware before 6.5, Prusa Connect) or a code the app does not know.</summary>
    Unknown,

    /// <summary>MINI and MINI+ (the firmware reports both as 2.1.0).</summary>
    Mini,

    Mk35,

    Mk35S,

    Mk39,

    Mk39S,

    Mk4,

    Mk4S,

    /// <summary>XL with any number of tools (the code does not tell them apart).</summary>
    Xl,

    /// <summary>XL+ ("XLS" in the firmware source).</summary>
    XlPlus,

    CoreOne,

    CoreOneL,
}
