using PrusaTrayDiagnostic.Core.Web;

namespace PrusaTrayDiagnostic.Core.Tests.Web;

public class PrinterWebAccessTests
{
    [Fact]
    public void ProxiedAccessHasNoFallback()
    {
        Uri address = new("http://127.0.0.1:50123/token/");

        PrinterWebAccess access = PrinterWebAccess.Proxied(address);

        Assert.True(access.IsProxied);
        Assert.Equal(PrinterWebFallback.None, access.Fallback);
        Assert.Same(address, access.Address);
    }

    [Fact]
    public void ProxiedRejectsRelativeAddress() =>
        Assert.Throws<ArgumentException>(() => PrinterWebAccess.Proxied(new Uri("/token/", UriKind.Relative)));

    [Fact]
    public void PlainAccessKeepsAddressAndReason()
    {
        Uri address = new("http://192.168.1.20/");

        PrinterWebAccess access = PrinterWebAccess.Plain(address, PrinterWebFallback.PasswordRejected);

        Assert.False(access.IsProxied);
        Assert.Equal(PrinterWebFallback.PasswordRejected, access.Fallback);
        Assert.Same(address, access.Address);
    }

    [Fact]
    public void PlainAccessMayHaveNoAddress() =>
        Assert.Null(PrinterWebAccess.Plain(null, PrinterWebFallback.NotSupported).Address);

    [Theory]
    [InlineData(PrinterWebFallback.None)]
    [InlineData((PrinterWebFallback)999)]
    public void PlainAccessRequiresKnownReason(PrinterWebFallback reason) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PrinterWebAccess.Plain(new Uri("http://printer/"), reason));

    [Fact]
    public void PlainRejectsRelativeAddress() =>
        Assert.Throws<ArgumentException>(() => PrinterWebAccess.Plain(new Uri("printer", UriKind.Relative), PrinterWebFallback.NotReachable));

    [Fact]
    public void TextNeverContainsTheAddress()
    {
        string text = PrinterWebAccess.Proxied(new Uri("http://127.0.0.1:50123/SecretToken123/")).ToString();

        Assert.DoesNotContain("SecretToken123", text, StringComparison.Ordinal);
        Assert.DoesNotContain("50123", text, StringComparison.Ordinal);
        Assert.Contains("IsProxied = True", text, StringComparison.Ordinal);
    }
}
