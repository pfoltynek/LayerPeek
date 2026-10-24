using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Printers;

/// <summary>
/// Codes from Prusa-Firmware-Buddy <c>include/common/printer_model_data.hpp</c> (tags v6.5.1–v6.10.3 and master
/// 1ce23f33, 2026-08-27); see docs/api/prusalink.md.
/// </summary>
public class PrinterModelsTests
{
    [Theory]
    [InlineData("1.3.5", PrinterModel.Mk35)]
    [InlineData("1.3.6", PrinterModel.Mk35S)]
    [InlineData("1.3.9", PrinterModel.Mk39)]
    [InlineData("1.3.10", PrinterModel.Mk39S)]
    [InlineData("1.4.0", PrinterModel.Mk4)]
    [InlineData("1.4.1", PrinterModel.Mk4S)]
    [InlineData("2.1.0", PrinterModel.Mini)]
    [InlineData("3.1.0", PrinterModel.Xl)]
    [InlineData("3.1.1", PrinterModel.XlPlus)]
    [InlineData("5.1.0", PrinterModel.Xl)]
    [InlineData("7.1.0", PrinterModel.CoreOne)]
    [InlineData("8.1.0", PrinterModel.CoreOneL)]
    public void FirmwareCodesMapToModels(string code, PrinterModel expected)
    {
        Assert.Equal(expected, PrinterModels.FromCode(code));
    }

    [Theory]
    // MK3 / MK3S (8-bit firmware without PrusaLink 6.x), iX, Core One INDX, Core One L INDX, Core One "Oak" (unreleased).
    [InlineData("1.3.0")]
    [InlineData("1.3.1")]
    [InlineData("4.1.0")]
    [InlineData("7.10.0")]
    [InlineData("8.10.0")]
    [InlineData("7.2.0")]
    // Not in the firmware table at all (e.g. the invented value of older test fixtures).
    [InlineData("7.0.0")]
    [InlineData("0.0.0")]
    [InlineData("255.255.255")]
    public void CodesWithoutIllustrationAreUnknown(string code)
    {
        Assert.Equal(PrinterModel.Unknown, PrinterModels.FromCode(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MK4")]
    [InlineData("1.4")]
    public void MissingOrMalformedCodeIsUnknown(string? code)
    {
        Assert.Equal(PrinterModel.Unknown, PrinterModels.FromCode(code));
    }

    [Fact]
    public void EveryKnownModelIsSelectableExactlyOnce()
    {
        PrinterModel[] known = [.. Enum.GetValues<PrinterModel>().Where(static m => m != PrinterModel.Unknown)];

        Assert.Equal(known.Order(), PrinterModels.Selectable.Order());
        Assert.Equal(PrinterModels.Selectable.Count, PrinterModels.Selectable.Distinct().Count());
    }

    [Fact]
    public void EveryModelNameRoundTrips()
    {
        foreach (PrinterModel model in Enum.GetValues<PrinterModel>())
        {
            string name = PrinterModels.Format(model);

            Assert.True(PrinterModels.TryParse(name, out PrinterModel parsed));
            Assert.Equal(model, parsed);
            Assert.True(PrinterModels.TryParse($"  {name.ToUpperInvariant()} ", out parsed));
            Assert.Equal(model, parsed);
        }
    }

    [Theory]
    [InlineData(PrinterModel.Mk35, "Mk35")]
    [InlineData(PrinterModel.XlPlus, "XlPlus")]
    [InlineData(PrinterModel.CoreOneL, "CoreOneL")]
    public void StoredNamesAreStable(PrinterModel model, string name)
    {
        Assert.Equal(name, PrinterModels.Format(model));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("6")]
    [InlineData("MK3.5")]
    [InlineData("Mk4, Mk4S")]
    public void InvalidNamesAreRejected(string? name)
    {
        Assert.False(PrinterModels.TryParse(name, out PrinterModel model));
        Assert.Equal(PrinterModel.Unknown, model);
    }

    [Theory]
    [InlineData(PrinterModel.Mini, "MINI")]
    [InlineData(PrinterModel.Mk39S, "MK3.9S")]
    [InlineData(PrinterModel.XlPlus, "XL+")]
    [InlineData(PrinterModel.CoreOneL, "Core One L")]
    public void ProductNamesFollowPrusa(PrinterModel model, string expected)
    {
        Assert.Equal(expected, PrinterModels.GetProductName(model));
    }

    [Fact]
    public void EveryKnownModelHasAProductName()
    {
        Assert.All(PrinterModels.Selectable, static model => Assert.False(string.IsNullOrWhiteSpace(PrinterModels.GetProductName(model))));
        Assert.Throws<ArgumentOutOfRangeException>(static () => PrinterModels.GetProductName(PrinterModel.Unknown));
    }

    [Fact]
    public void UndefinedValueCannotBeFormatted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(static () => PrinterModels.Format((PrinterModel)999));
    }
}
