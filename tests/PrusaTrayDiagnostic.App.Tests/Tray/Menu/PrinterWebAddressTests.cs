using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

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
    public void OnlyPlainHttpAddressesAreSafeToLaunch(string uri, bool expected)
    {
        Assert.Equal(expected, PrinterWebAddress.IsSafeToLaunch(new Uri(uri)));
    }

    [Fact]
    public void RelativeAddressIsNotSafeToLaunch()
    {
        Assert.False(PrinterWebAddress.IsSafeToLaunch(new Uri("/relative", UriKind.Relative)));
    }

    [Theory]
    [InlineData("maker", "secret", "http://maker:secret@printer.local/")]
    [InlineData("maker", "p@ss:w/rd#%?", "http://maker:p%40ss%3Aw%2Frd%23%25%3F@printer.local/")]
    [InlineData("a b@c", "two words", "http://a%20b%40c:two%20words@printer.local/")]
    [InlineData("maker", "žluťoučký kůň", "http://maker:%C5%BElu%C5%A5ou%C4%8Dk%C3%BD%20k%C5%AF%C5%88@printer.local/")]
    [InlineData("maker", "x%41y", "http://maker:x%2541y@printer.local/")]
    [InlineData("maker", "+&=;$,!*'()[]~._-", "http://maker:%2B%26%3D%3B%24%2C%21%2A%27%28%29%5B%5D~._-@printer.local/")]
    public void CredentialsArePercentEncodedIntoUserInfo(string userName, string password, string expected)
    {
        string address = PrinterWebAddress.WithCredentials(new Uri("http://printer.local/"), userName, password);

        Assert.Equal(expected, address);
    }

    [Theory]
    [InlineData("maker", "p@ss:w/rd#%?")]
    [InlineData("a b@c/d", "two words?#")]
    [InlineData("maker", "žluťoučký kůň 🙂")]
    [InlineData("maker", "%2F%40")]
    [InlineData("maker", ":::@@@")]
    public void EncodedCredentialsRoundTripAndKeepTheHost(string userName, string password)
    {
        string address = PrinterWebAddress.WithCredentials(new Uri("https://printer.example:8443/prusa/"), userName, password);

        Uri parsed = new(address);
        Assert.Equal("printer.example", parsed.Host);
        Assert.Equal(8443, parsed.Port);
        Assert.Equal("/prusa/", parsed.AbsolutePath);
        Assert.Empty(parsed.Query);
        Assert.Empty(parsed.Fragment);
        string[] parts = parsed.UserInfo.Split(':');
        Assert.Equal(2, parts.Length);
        Assert.Equal(userName, Uri.UnescapeDataString(parts[0]));
        Assert.Equal(password, Uri.UnescapeDataString(parts[1]));
    }

    [Theory]
    [InlineData("http://[fe80::1]/", "http://maker:pw@[fe80::1]/")]
    [InlineData("http://printer:8080/", "http://maker:pw@printer:8080/")]
    [InlineData("HTTPS://PRINTER/", "https://maker:pw@printer/")]
    public void CredentialsGoRightAfterTheScheme(string baseUri, string expected)
    {
        Assert.Equal(expected, PrinterWebAddress.WithCredentials(new Uri(baseUri), "maker", "pw"));
    }

    [Theory]
    [InlineData("http://user:other@printer/")]
    [InlineData("http://user@printer/")]
    [InlineData("http://@printer/")]
    [InlineData("ftp://printer/")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    public void CredentialsAreNeverAddedToAnUnsafeAddress(string baseUri)
    {
        Assert.Throws<ArgumentException>(() => PrinterWebAddress.WithCredentials(new Uri(baseUri), "maker", "pw"));
    }

    [Theory]
    [InlineData("", "pw")]
    [InlineData("maker", "")]
    public void EmptyCredentialsAreRejected(string userName, string password)
    {
        Assert.Throws<ArgumentException>(() => PrinterWebAddress.WithCredentials(new Uri("http://printer/"), userName, password));
    }

    private static PrinterDefinition PrusaLink(string address) =>
        new() { Id = "p", Name = "P", ConnectionType = ConnectionType.PrusaLink, Address = address };
}
