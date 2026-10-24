using LayerPeek.App.Tray.Menu;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Tests.Tray.Menu;

public sealed class PrinterWebAddressTests
{
    [Theory]
    [InlineData("192.168.1.10", "http://192.168.1.10/")]
    [InlineData("prusa-core-one.local", "http://prusa-core-one.local/")]
    [InlineData("http://printer:8080", "http://printer:8080/")]
    [InlineData("https://proxy.example/prusa/mk4", "https://proxy.example/prusa/mk4/")]
    [InlineData("[fe80::1]", "http://[fe80::1]/")]
    public void PrusaLinkOpensThePrinterItself(string address, string expected)
    {
        Uri? uri = PrinterWebAddress.For(PrusaLink(address));

        Assert.Equal(new Uri(expected), uri);
    }

    [Theory]
    [InlineData("ftp://printer")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://user:secret@printer")]
    [InlineData("maker@printer")]
    [InlineData("http://printer/?q=1")]
    [InlineData("http://")]
    [InlineData("not a host")]
    public void InvalidPrusaLinkAddressHasNoWebAddress(string address)
    {
        Assert.Null(PrinterWebAddress.For(PrusaLink(address)));
    }

    [Theory]
    [InlineData("c0ffee00-1234-5678-9abc-def012345678")]
    [InlineData("../../evil?x=1#y")]
    public void PrusaConnectOpensPrusaConnectHome(string address)
    {
        PrinterDefinition printer = new() { Id = "c", Name = "XL", ConnectionType = ConnectionType.PrusaConnect, Address = address };

        Assert.Equal(new Uri("https://connect.prusa3d.com/"), PrinterWebAddress.For(printer));
    }

    [Theory]
    [InlineData("http://printer/", true)]
    [InlineData("https://printer:8443/x/", true)]
    [InlineData("HTTPS://PRINTER/", true)]
    [InlineData("ftp://printer/", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("mailto:a@b.c", false)]
    [InlineData("http://user:pw@printer/", false)]
    [InlineData("http://maker@printer/", false)]
    [InlineData("http://@printer/", false)]
    [InlineData("http://[fe80::1]:8080/a@b", true)]
    // The local web proxy (T-028): no userinfo, the secret token is the first path segment.
    [InlineData("http://127.0.0.1:50123/Zm9vYmFyU2VjcmV0VG9rZW4xMjM0NTY/", true)]
    [InlineData("http://@127.0.0.1:50123/token/", false)]
    public void OnlyPlainHttpAddressesAreSafeToLaunch(string uri, bool expected)
    {
        Assert.Equal(expected, PrinterWebAddress.IsSafeToLaunch(new Uri(uri)));
    }

    [Fact]
    public void RelativeAddressIsNotSafeToLaunch()
    {
        Assert.False(PrinterWebAddress.IsSafeToLaunch(new Uri("/relative", UriKind.Relative)));
    }

    private static PrinterDefinition PrusaLink(string address) =>
        new() { Id = "p", Name = "P", ConnectionType = ConnectionType.PrusaLink, Address = address };
}
