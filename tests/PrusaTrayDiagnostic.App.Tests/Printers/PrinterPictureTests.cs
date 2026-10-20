using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.Core.Printers;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.Tests.Printers;

/// <summary>What a printer shows (T-039): custom picture first, a chosen model, the detected model, else generic.</summary>
public sealed class PrinterPictureTests
{
    private static readonly PrinterModelCode Mk4SCode = new(1, 4, 1);

    [Fact]
    public void DetectedModelShowsItsIllustration()
    {
        PrinterPicture picture = PrinterPicture.For(LinkPrinter("a", "A") with { DetectedModel = Mk4SCode }, customPng: null);

        Assert.Equal("mk4", picture.IllustrationId);
        Assert.False(picture.IsCustom);
        Assert.Equal(new PrinterImage(PrinterImageKind.Model, PrinterModel.Mk4S), picture.Image);
    }

    [Fact]
    public void UnknownOrMissingModelIsGeneric()
    {
        Assert.Same(PrinterPicture.Generic, PrinterPicture.For(LinkPrinter("a", "A"), customPng: null));
        Assert.Same(PrinterPicture.Generic, PrinterPicture.For(LinkPrinter("a", "A") with { DetectedModel = new PrinterModelCode(4, 1, 0) }, customPng: null));
    }

    [Fact]
    public void ChosenModelWinsOverTheDetectedOne()
    {
        PrinterDefinition printer = LinkPrinter("a", "A") with { DetectedModel = Mk4SCode, Image = PrinterImageChoice.ForModel(PrinterModel.Mini) };

        Assert.Equal("mini", PrinterPicture.For(printer, customPng: null).IllustrationId);
    }

    [Fact]
    public void CustomImageWinsWhenItExists()
    {
        byte[] png = TestImages.Png();
        PrinterDefinition printer = LinkPrinter("a", "A") with { DetectedModel = Mk4SCode, Image = PrinterImageChoice.Custom };

        PrinterPicture picture = PrinterPicture.For(printer, png);

        Assert.True(picture.IsCustom);
        Assert.Same(png, picture.CustomPng);
        Assert.Equal(PrinterImageKind.Custom, picture.Image.Kind);
    }

    [Fact]
    public void MissingCustomFileFallsBackToTheAutomaticImage()
    {
        PrinterDefinition printer = LinkPrinter("a", "A") with { DetectedModel = Mk4SCode, Image = PrinterImageChoice.Custom };

        PrinterPicture picture = PrinterPicture.For(printer, customPng: null);

        Assert.False(picture.IsCustom);
        Assert.Equal("mk4", picture.IllustrationId);
    }

    [Fact]
    public void CustomBytesAreIgnoredWhenTheCustomImageIsNotChosen()
    {
        PrinterPicture picture = PrinterPicture.For(LinkPrinter("a", "A") with { DetectedModel = Mk4SCode }, TestImages.Png());

        Assert.False(picture.IsCustom);
        Assert.Null(picture.CustomPng);
    }

    [Fact]
    public void PrusaConnectHasNoAutomaticDetection()
    {
        // Even a stored code does not count for Prusa Connect (T-039 decision): generic unless chosen manually.
        PrinterDefinition cloud = CloudPrinter("c", "Cloud") with { DetectedModel = Mk4SCode };

        Assert.Same(PrinterPicture.Generic, PrinterPicture.For(cloud, customPng: null));
        Assert.Equal("xl", PrinterPicture.For(cloud with { Image = PrinterImageChoice.ForModel(PrinterModel.Xl) }, customPng: null).IllustrationId);
    }

    [Fact]
    public void EqualityComparesCustomBytesByReference()
    {
        PrinterDefinition printer = LinkPrinter("a", "A") with { Image = PrinterImageChoice.Custom };
        byte[] png = TestImages.Png();

        Assert.Equal(PrinterPicture.For(printer, png), PrinterPicture.For(printer, png));
        Assert.NotEqual(PrinterPicture.For(printer, png), PrinterPicture.For(printer, TestImages.Png()));
        Assert.Equal(PrinterPicture.For(LinkPrinter("a", "A") with { DetectedModel = Mk4SCode }, null), PrinterPicture.For(LinkPrinter("b", "B") with { DetectedModel = new PrinterModelCode(1, 4, 1) }, null));
    }
}
