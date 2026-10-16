namespace PrusaTrayDiagnostic.PrusaLink.Tests;

public class PrusaLinkAddressTests
{
    [Theory]
    [InlineData("192.168.1.20", "http://192.168.1.20/")]
    [InlineData(" 192.168.1.20 ", "http://192.168.1.20/")]
    [InlineData("prusa-core-one.local", "http://prusa-core-one.local/")]
    [InlineData("http://10.0.0.5:8080", "http://10.0.0.5:8080/")]
    [InlineData("HTTP://Printer", "http://printer/")]
    [InlineData("https://printer.example/", "https://printer.example/")]
    [InlineData("http://proxy.example/printers/mk4", "http://proxy.example/printers/mk4/")]
    [InlineData("[fe80::1]", "http://[fe80::1]/")]
    public void ValidAddressesAreNormalizedToBaseUri(string address, string expected)
    {
        Assert.True(PrusaLinkAddress.TryCreateBaseUri(address, out Uri? baseUri));
        Assert.Equal(expected, baseUri.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://")]
    [InlineData("ftp://printer")]
    [InlineData("file:///c:/printer")]
    [InlineData("host with spaces")]
    [InlineData("http://maker:secret@printer")]
    [InlineData("http://printer/?debug=1")]
    [InlineData("http://printer/#fragment")]
    public void InvalidAddressesAreRejected(string? address)
    {
        Assert.False(PrusaLinkAddress.TryCreateBaseUri(address, out Uri? baseUri));
        Assert.Null(baseUri);
    }
}
