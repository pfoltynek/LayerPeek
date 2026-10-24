using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Printers;

public class PrinterUserNameTests
{
    [Fact]
    public void DefaultIsTheFirmwareAccount()
    {
        Assert.Equal("maker", PrinterUserName.Default);
        Assert.True(PrinterUserName.IsValid(PrinterUserName.Default));
    }

    [Theory]
    [InlineData("maker")]
    [InlineData(" workshop ")]
    [InlineData("john.doe-2_x")]
    [InlineData("two words")]
    [InlineData("a@b+c!#$%&'()*,/;<=>?[]^`{|}~")]
    public void ValidNamesAreAccepted(string value)
    {
        Assert.Equal(PrinterUserNameError.None, PrinterUserName.Validate(value));
        Assert.True(PrinterUserName.IsValid(value));
    }

    [Fact]
    public void MaxLengthIsCountedAfterTrimming()
    {
        string longest = new('a', PrinterUserName.MaxLength);

        Assert.Equal(64, PrinterUserName.MaxLength);
        Assert.Equal(PrinterUserNameError.None, PrinterUserName.Validate($"  {longest}  "));
        Assert.Equal(PrinterUserNameError.TooLong, PrinterUserName.Validate(longest + "a"));
    }

    [Theory]
    [InlineData(null, PrinterUserNameError.Empty)]
    [InlineData("", PrinterUserNameError.Empty)]
    [InlineData("  \t ", PrinterUserNameError.Empty)]
    [InlineData("user:name", PrinterUserNameError.ContainsColon)]
    [InlineData(":", PrinterUserNameError.ContainsColon)]
    [InlineData("user\tname", PrinterUserNameError.ContainsControlCharacter)]
    [InlineData("user\nname", PrinterUserNameError.ContainsControlCharacter)]
    [InlineData("user\0", PrinterUserNameError.ContainsControlCharacter)]
    [InlineData("user\u007f", PrinterUserNameError.ContainsControlCharacter)]
    [InlineData("user\u0085name", PrinterUserNameError.ContainsControlCharacter)]
    [InlineData("us:er\r\n", PrinterUserNameError.ContainsColon)]
    [InlineData("us:er\rx", PrinterUserNameError.ContainsControlCharacter)]
    [InlineData("user\"name", PrinterUserNameError.ContainsUnsupportedCharacter)]
    [InlineData("user\\name", PrinterUserNameError.ContainsUnsupportedCharacter)]
    [InlineData("uživatel", PrinterUserNameError.ContainsUnsupportedCharacter)]
    [InlineData("user\u00a0name", PrinterUserNameError.ContainsUnsupportedCharacter)]
    public void InvalidNamesAreRejected(string? value, PrinterUserNameError expected)
    {
        Assert.Equal(expected, PrinterUserName.Validate(value));
        Assert.False(PrinterUserName.IsValid(value));
    }
}
