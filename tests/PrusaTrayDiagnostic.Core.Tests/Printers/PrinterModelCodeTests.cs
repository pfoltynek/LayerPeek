using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Tests.Printers;

public class PrinterModelCodeTests
{
    [Theory]
    [InlineData("1.4.1", 1, 4, 1)]
    [InlineData("1.3.10", 1, 3, 10)]
    [InlineData("0.0.0", 0, 0, 0)]
    [InlineData("255.255.255", 255, 255, 255)]
    [InlineData("007.01.000", 7, 1, 0)]
    public void ValidCodesAreParsed(string text, int type, int version, int subversion)
    {
        Assert.True(PrinterModelCode.TryParse(text, out PrinterModelCode code));
        Assert.Equal(new PrinterModelCode(type, version, subversion), code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.4")]
    [InlineData("1.4.")]
    [InlineData(".4.1")]
    [InlineData("1..1")]
    [InlineData("1.4.1.0")]
    [InlineData("1.4.1 ")]
    [InlineData(" 1.4.1")]
    [InlineData("+1.4.1")]
    [InlineData("-1.4.1")]
    [InlineData("1.4.256")]
    [InlineData("1.4.1000")]
    [InlineData("1.4.0001")]
    [InlineData("1,4,1")]
    [InlineData("MK4")]
    [InlineData("١.٤.١")]
    public void MalformedCodesAreRejected(string? text)
    {
        Assert.False(PrinterModelCode.TryParse(text, out PrinterModelCode code));
        Assert.Equal(default, code);
    }

    [Fact]
    public void ToStringWritesTheFirmwareFormat()
    {
        Assert.Equal("1.3.10", new PrinterModelCode(1, 3, 10).ToString());
        Assert.Equal("7.1.0", new PrinterModelCode(7, 1, 0).ToString());
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 256, 0)]
    [InlineData(0, 0, int.MaxValue)]
    public void PartsOutsideAByteAreRejected(int type, int version, int subversion)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrinterModelCode(type, version, subversion));
    }

    [Fact]
    public void CodesHaveValueEquality()
    {
        Assert.Equal(new PrinterModelCode(1, 4, 0), new PrinterModelCode(1, 4, 0));
        Assert.NotEqual(new PrinterModelCode(1, 4, 0), new PrinterModelCode(1, 4, 1));
    }
}
