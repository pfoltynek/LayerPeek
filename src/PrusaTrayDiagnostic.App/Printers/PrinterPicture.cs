using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Printers;

/// <summary>
/// What a printer image shows (T-039), free of Avalonia so that view models stay testable: a bundled illustration or the
/// user's own picture (PNG bytes, never changed after creation). Value equality compares the custom bytes by reference: a
/// new import or a reload is a new array.
/// </summary>
/// <param name="Image">The resolved image (<see cref="PrinterImage.Resolve"/>).</param>
/// <param name="CustomPng">The custom picture for <see cref="PrinterImageKind.Custom"/>; otherwise <see langword="null"/>.</param>
public sealed record PrinterPicture(PrinterImage Image, byte[]? CustomPng)
{
    /// <summary>The generic illustration (no printer, unknown model).</summary>
    public static PrinterPicture Generic { get; } = new(PrinterImage.Generic, null);

    /// <summary>The bundled illustration to show: that of the model, or <see cref="PrinterIllustrations.Generic"/>.</summary>
    public string IllustrationId => PrinterIllustrations.IdFor(Image);

    public bool IsCustom => CustomPng is not null;

    /// <summary>
    /// The picture of <paramref name="printer"/>: its custom picture when it uses one and <paramref name="customPng"/> is
    /// there, otherwise its illustration (a missing custom file falls back to the automatic one).
    /// </summary>
    public static PrinterPicture For(PrinterDefinition printer, byte[]? customPng)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return For(printer.Image, printer.ConnectionType, printer.DetectedModel, customPng);
    }

    /// <inheritdoc cref="For(PrinterDefinition, byte[])"/>
    public static PrinterPicture For(PrinterImageChoice choice, ConnectionType connectionType, PrinterModelCode? detectedModel, byte[]? customPng)
    {
        PrinterImage image = PrinterImage.Resolve(choice, connectionType, detectedModel, customImageExists: customPng is not null);
        return image.Kind == PrinterImageKind.Custom ? new PrinterPicture(image, customPng) : FromImage(image);
    }

    /// <summary>An illustration (shared instances, so that equal pictures need no allocation).</summary>
    public static PrinterPicture FromImage(PrinterImage image) => image.Kind switch
    {
        PrinterImageKind.Model => new PrinterPicture(image, null),
        _ => Generic,
    };
}
