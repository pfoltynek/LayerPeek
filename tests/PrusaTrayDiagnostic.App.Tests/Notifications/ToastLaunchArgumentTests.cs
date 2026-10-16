using PrusaTrayDiagnostic.App.Notifications;

namespace PrusaTrayDiagnostic.App.Tests.Notifications;

public sealed class ToastLaunchArgumentTests
{
    [Theory]
    [InlineData("mk4")]
    [InlineData("MK4 in the garage")]
    [InlineData("a&b<c>\"'=;%20/?#")]
    [InlineData("příliš žluťoučký 🐎")]
    public void PrinterIdRoundTrips(string printerId)
    {
        string argument = ToastLaunchArgument.ForPrinter(printerId);

        Assert.True(ToastLaunchArgument.TryGetPrinterId(argument, out string? parsed));
        Assert.Equal(printerId, parsed);
    }

    [Fact]
    public void ArgumentContainsNoCharactersThatNeedXmlEscaping()
    {
        string argument = ToastLaunchArgument.ForPrinter("a&b<c>\"'");

        Assert.DoesNotContain('&', argument);
        Assert.DoesNotContain('<', argument);
        Assert.DoesNotContain('"', argument);
        Assert.DoesNotContain('\'', argument);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("printer=")]
    [InlineData("printer=%20%20")]
    [InlineData("job=mk4")]
    [InlineData("PRINTER=mk4")]
    public void MalformedArgumentIsRejected(string? argument)
    {
        Assert.False(ToastLaunchArgument.TryGetPrinterId(argument, out string? printerId));
        Assert.Null(printerId);
    }

    [Fact]
    public void LongestEncodableIdStillFits()
    {
        string id = new('a', ToastLaunchArgument.MaxLength - "printer=".Length);

        Assert.True(ToastLaunchArgument.TryForPrinter(id, out string? argument));
        Assert.Equal(ToastLaunchArgument.MaxLength, argument.Length);
    }

    [Theory]
    [InlineData(600, 'a')]
    // Short as text, but every 'ř' takes six characters when percent-encoded.
    [InlineData(100, 'ř')]
    public void TooLongIdCannotBeEncoded(int length, char character)
    {
        string id = new(character, length);

        Assert.False(ToastLaunchArgument.TryForPrinter(id, out string? argument));
        Assert.Null(argument);
        Assert.Throws<ArgumentException>(() => ToastLaunchArgument.ForPrinter(id));
    }

    [Fact]
    public void EmptyPrinterIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => ToastLaunchArgument.ForPrinter(" "));
        Assert.Throws<ArgumentNullException>(() => ToastLaunchArgument.ForPrinter(null!));
    }
}
