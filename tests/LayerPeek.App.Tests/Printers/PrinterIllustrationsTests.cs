using LayerPeek.App.Printers;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Tests.Printers;

/// <summary>Which illustration a model shows and which pre-rendered size is drawn (T-039, user decisions 2026-10-20).</summary>
public sealed class PrinterIllustrationsTests
{
    public static TheoryData<PrinterModel, string> Models => new()
    {
        { PrinterModel.Mk4, "mk4" },
        { PrinterModel.Mk4S, "mk4" },
        { PrinterModel.Mk39, "mk4" },
        { PrinterModel.Mk39S, "mk4" },
        { PrinterModel.Mk35, "mk4" },
        { PrinterModel.Mk35S, "mk4" },
        { PrinterModel.CoreOne, "core-one" },
        { PrinterModel.CoreOneL, "core-one" },
        { PrinterModel.Xl, "xl" },
        { PrinterModel.XlPlus, "xl" },
        { PrinterModel.Mini, "mini" },
        { PrinterModel.Unknown, "generic" },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void ModelsShareTheDecidedIllustrations(PrinterModel model, string id)
    {
        Assert.Equal(id, PrinterIllustrations.IdFor(model));
    }

    [Fact]
    public void EverySelectableModelHasAnIllustrationThatExists()
    {
        foreach (PrinterModel model in PrinterModels.Selectable)
        {
            Assert.Contains(PrinterIllustrations.IdFor(model), PrinterIllustrations.Ids);
        }

        Assert.Equal(["mk4", "core-one", "xl", "mini", "generic"], PrinterIllustrations.Ids);
    }

    [Theory]
    [InlineData("7.10.0")] // Core One INDX
    [InlineData("8.10.0")] // Core One L INDX
    [InlineData("4.1.0")] // iX
    [InlineData("9.9.9")] // unknown
    public void ModelsWithoutAnIllustrationAreGeneric(string code)
    {
        Assert.Equal(PrinterIllustrations.Generic, PrinterIllustrations.IdFor(PrinterModels.FromCode(code)));
    }

    [Fact]
    public void CustomAndGenericImagesHaveTheGenericIllustration()
    {
        Assert.Equal("generic", PrinterIllustrations.IdFor(PrinterImage.Generic));
        Assert.Equal("generic", PrinterIllustrations.IdFor(new PrinterImage(PrinterImageKind.Custom, PrinterModel.Unknown)));
        Assert.Equal("xl", PrinterIllustrations.IdFor(new PrinterImage(PrinterImageKind.Model, PrinterModel.XlPlus)));
    }

    [Theory]
    // Tray menu, 16 DIP: 16 / 20 / 24 / 32 px at 100 / 125 / 150 / 200 %.
    [InlineData(16, 1.0, 16)]
    [InlineData(16, 1.25, 20)]
    [InlineData(16, 1.5, 24)]
    [InlineData(16, 1.75, 32)]
    [InlineData(16, 2.0, 32)]
    // Printer list, 24 DIP.
    [InlineData(24, 1.0, 24)]
    [InlineData(24, 1.25, 32)]
    [InlineData(24, 1.5, 48)]
    [InlineData(24, 2.0, 48)]
    // Diagnostics, 48 DIP.
    [InlineData(48, 1.0, 48)]
    [InlineData(48, 1.25, 64)]
    [InlineData(48, 1.5, 96)]
    [InlineData(48, 2.0, 96)]
    // Printer sub-tab, 56 DIP (the image inside the button is 54).
    [InlineData(56, 1.0, 56)]
    [InlineData(54, 1.0, 56)]
    [InlineData(56, 1.5, 96)]
    [InlineData(56, 2.0, 112)]
    [InlineData(56, 2.25, 128)]
    // Beyond the largest size, and nonsense scaling.
    [InlineData(200, 2.0, 256)]
    [InlineData(24, 0, 24)]
    [InlineData(24, double.NaN, 24)]
    [InlineData(0.1, 1.0, 16)]
    public void SizeIsTheSmallestWithEnoughPhysicalPixels(double dip, double scaling, int expected)
    {
        Assert.Equal(expected, PrinterIllustrations.SelectPixelSize(dip, scaling));
    }

    [Fact]
    public void RoundingNoiseDoesNotPickTheNextSize()
    {
        Assert.Equal(32, PrinterIllustrations.SelectPixelSize(24, 1.25 + 1e-12));
    }

    [Fact]
    public void AssetUriNamesTheBundledFile()
    {
        Assert.Equal("avares://LayerPeek/Assets/Printers/core-one-112.png", PrinterIllustrations.AssetUri("core-one", 112).OriginalString);
    }
}
