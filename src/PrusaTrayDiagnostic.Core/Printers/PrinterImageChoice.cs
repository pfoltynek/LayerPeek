namespace PrusaTrayDiagnostic.Core.Printers;

/// <summary>Where the image of a printer comes from (T-039).</summary>
public enum PrinterImageMode
{
    /// <summary>Illustration of <see cref="PrinterDefinition.DetectedModel"/>; the generic one when it is unknown.</summary>
    Automatic,

    /// <summary>Illustration of a model chosen by the user (<see cref="PrinterImageChoice.Model"/>).</summary>
    Model,

    /// <summary>
    /// The user's own picture, stored by <see cref="Images.IPrinterImageStore"/> under the printer id (the settings hold no
    /// path). When the file is missing, the image is shown as for <see cref="Automatic"/>.
    /// </summary>
    Custom,
}

/// <summary>The user's choice of the printer image; <see cref="Automatic"/> by default.</summary>
public sealed record PrinterImageChoice
{
    private PrinterImageChoice(PrinterImageMode mode, PrinterModel model)
    {
        Mode = mode;
        Model = model;
    }

    public static PrinterImageChoice Automatic { get; } = new(PrinterImageMode.Automatic, PrinterModel.Unknown);

    public static PrinterImageChoice Custom { get; } = new(PrinterImageMode.Custom, PrinterModel.Unknown);

    public PrinterImageMode Mode { get; }

    /// <summary>The chosen model for <see cref="PrinterImageMode.Model"/>; <see cref="PrinterModel.Unknown"/> otherwise.</summary>
    public PrinterModel Model { get; }

    /// <summary>The illustration of <paramref name="model"/>, chosen manually.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="model"/> is <see cref="PrinterModel.Unknown"/> or not defined.</exception>
    public static PrinterImageChoice ForModel(PrinterModel model)
    {
        if (model == PrinterModel.Unknown || !Enum.IsDefined(model))
        {
            throw new ArgumentOutOfRangeException(nameof(model), model, "A known model is required.");
        }

        return new PrinterImageChoice(PrinterImageMode.Model, model);
    }
}

/// <summary>What to show as the image of a printer.</summary>
public enum PrinterImageKind
{
    /// <summary>The generic printer illustration.</summary>
    Generic,

    /// <summary>The illustration of <see cref="PrinterImage.Model"/>.</summary>
    Model,

    /// <summary>The custom picture from <see cref="Images.IPrinterImageStore"/>.</summary>
    Custom,
}

/// <summary>Resolved image of a printer (<see cref="PrinterImage.Resolve"/>).</summary>
/// <param name="Model">The model whose illustration to show for <see cref="PrinterImageKind.Model"/>; otherwise <see cref="PrinterModel.Unknown"/>.</param>
public readonly record struct PrinterImage(PrinterImageKind Kind, PrinterModel Model)
{
    public static PrinterImage Generic => new(PrinterImageKind.Generic, PrinterModel.Unknown);

    /// <summary>
    /// The image of <paramref name="printer"/>: the custom picture when chosen and <paramref name="customImageExists"/>;
    /// a manually chosen model; otherwise automatic, i.e. the detected model of a PrusaLink printer. Unknown model,
    /// firmware before 6.5 and Prusa Connect (no automatic detection, T-039 decision) give <see cref="Generic"/>.
    /// A missing custom file falls back to automatic.
    /// </summary>
    public static PrinterImage Resolve(PrinterDefinition printer, bool customImageExists)
    {
        ArgumentNullException.ThrowIfNull(printer);
        switch (printer.Image.Mode)
        {
            case PrinterImageMode.Custom when customImageExists:
                return new PrinterImage(PrinterImageKind.Custom, PrinterModel.Unknown);
            case PrinterImageMode.Model:
                return new PrinterImage(PrinterImageKind.Model, printer.Image.Model);
        }

        PrinterModel detected = printer.ConnectionType == ConnectionType.PrusaLink && printer.DetectedModel is { } code
            ? PrinterModels.FromCode(code)
            : PrinterModel.Unknown;
        return detected == PrinterModel.Unknown ? Generic : new PrinterImage(PrinterImageKind.Model, detected);
    }
}
