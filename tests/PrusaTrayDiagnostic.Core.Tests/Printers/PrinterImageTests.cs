using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.Core.Tests.Printers;

/// <summary><see cref="PrinterImageChoice"/>, <see cref="PrinterImage.Resolve"/> and <see cref="AppSettings.WithDetectedModel"/>.</summary>
public class PrinterImageTests
{
    private static readonly PrinterModelCode Mk4SCode = new(1, 4, 1);

    [Fact]
    public void NewPrinterHasAutomaticImageAndNoDetectedModel()
    {
        PrinterDefinition printer = Printer();

        Assert.Same(PrinterImageChoice.Automatic, printer.Image);
        Assert.Null(printer.DetectedModel);
    }

    [Fact]
    public void ImageChoiceCannotBeNull()
    {
        Assert.Throws<ArgumentNullException>(() => Printer() with { Image = null! });
    }

    [Theory]
    [InlineData(PrinterModel.Unknown)]
    [InlineData((PrinterModel)999)]
    public void ModelChoiceNeedsAKnownModel(PrinterModel model)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PrinterImageChoice.ForModel(model));
    }

    [Fact]
    public void ChoicesHaveValueEquality()
    {
        Assert.Equal(PrinterImageChoice.ForModel(PrinterModel.Xl), PrinterImageChoice.ForModel(PrinterModel.Xl));
        Assert.NotEqual(PrinterImageChoice.ForModel(PrinterModel.Xl), PrinterImageChoice.ForModel(PrinterModel.XlPlus));
        Assert.Equal(PrinterImageMode.Model, PrinterImageChoice.ForModel(PrinterModel.Xl).Mode);
        Assert.Equal(PrinterModel.Unknown, PrinterImageChoice.Custom.Model);
    }

    [Fact]
    public void AutomaticShowsDetectedModel()
    {
        PrinterImage image = PrinterImage.Resolve(Printer() with { DetectedModel = Mk4SCode }, customImageExists: false);

        Assert.Equal(new PrinterImage(PrinterImageKind.Model, PrinterModel.Mk4S), image);
    }

    [Fact]
    public void AutomaticWithoutDetectionIsGeneric()
    {
        Assert.Equal(PrinterImage.Generic, PrinterImage.Resolve(Printer(), customImageExists: false));
    }

    [Fact]
    public void AutomaticWithUnknownCodeIsGeneric()
    {
        PrinterImage image = PrinterImage.Resolve(Printer() with { DetectedModel = new PrinterModelCode(4, 1, 0) }, customImageExists: false);

        Assert.Equal(PrinterImage.Generic, image);
    }

    [Fact]
    public void PrusaConnectHasNoAutomaticDetection()
    {
        PrinterDefinition printer = Printer() with { ConnectionType = ConnectionType.PrusaConnect, DetectedModel = Mk4SCode };

        Assert.Equal(PrinterImage.Generic, PrinterImage.Resolve(printer, customImageExists: false));
    }

    [Fact]
    public void ManualModelWinsOverDetection()
    {
        PrinterDefinition printer = Printer() with { DetectedModel = Mk4SCode, Image = PrinterImageChoice.ForModel(PrinterModel.CoreOne) };

        Assert.Equal(new PrinterImage(PrinterImageKind.Model, PrinterModel.CoreOne), PrinterImage.Resolve(printer, customImageExists: false));
    }

    [Fact]
    public void ManualModelWorksForPrusaConnect()
    {
        PrinterDefinition printer = Printer() with { ConnectionType = ConnectionType.PrusaConnect, Image = PrinterImageChoice.ForModel(PrinterModel.Mini) };

        Assert.Equal(new PrinterImage(PrinterImageKind.Model, PrinterModel.Mini), PrinterImage.Resolve(printer, customImageExists: false));
    }

    [Fact]
    public void CustomImageWins()
    {
        PrinterDefinition printer = Printer() with { DetectedModel = Mk4SCode, Image = PrinterImageChoice.Custom };

        Assert.Equal(new PrinterImage(PrinterImageKind.Custom, PrinterModel.Unknown), PrinterImage.Resolve(printer, customImageExists: true));
    }

    [Fact]
    public void MissingCustomFileFallsBackToAutomatic()
    {
        PrinterDefinition printer = Printer() with { DetectedModel = Mk4SCode, Image = PrinterImageChoice.Custom };

        Assert.Equal(new PrinterImage(PrinterImageKind.Model, PrinterModel.Mk4S), PrinterImage.Resolve(printer, customImageExists: false));
    }

    [Fact]
    public void CustomFileIsIgnoredWhenNotChosen()
    {
        Assert.Equal(PrinterImage.Generic, PrinterImage.Resolve(Printer(), customImageExists: true));
    }

    [Fact]
    public void DetectedModelIsPartOfEquality()
    {
        Assert.NotEqual(Printer(), Printer() with { DetectedModel = Mk4SCode });
        Assert.NotEqual(Printer(), Printer() with { Image = PrinterImageChoice.Custom });
        Assert.Equal(Printer() with { DetectedModel = Mk4SCode }, Printer() with { DetectedModel = new PrinterModelCode(1, 4, 1) });
    }

    [Fact]
    public void WithDetectedModelUpdatesOnlyThatPrinter()
    {
        AppSettings settings = new() { Printers = [Printer("a"), Printer("b") with { Image = PrinterImageChoice.Custom }] };

        AppSettings updated = settings.WithDetectedModel("B", Mk4SCode);

        Assert.Null(updated.Printers[0].DetectedModel);
        Assert.Equal(Mk4SCode, updated.Printers[1].DetectedModel);
        Assert.Same(PrinterImageChoice.Custom, updated.Printers[1].Image);
        Assert.Equal(settings.Printers[1] with { DetectedModel = Mk4SCode }, updated.Printers[1]);
        Assert.Null(settings.Printers[1].DetectedModel);
    }

    [Fact]
    public void WithSameDetectedModelReturnsSameInstance()
    {
        AppSettings settings = new() { Printers = [Printer("a") with { DetectedModel = Mk4SCode }] };

        Assert.Same(settings, settings.WithDetectedModel("a", new PrinterModelCode(1, 4, 1)));
    }

    [Fact]
    public void WithDetectedModelOfUnknownPrinterReturnsSameInstance()
    {
        AppSettings settings = new() { Printers = [Printer("a")] };

        Assert.Same(settings, settings.WithDetectedModel("removed", Mk4SCode));
    }

    [Fact]
    public void ChangedDetectedModelReplacesTheOldOne()
    {
        AppSettings settings = new() { Printers = [Printer("a") with { DetectedModel = new PrinterModelCode(1, 4, 0) }] };

        AppSettings updated = settings.WithDetectedModel("a", Mk4SCode);

        Assert.NotEqual(settings, updated);
        Assert.Equal(Mk4SCode, updated.Printers[0].DetectedModel);
    }

    private static PrinterDefinition Printer(string id = "mk4") =>
        new() { Id = id, Name = id, ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" };
}
