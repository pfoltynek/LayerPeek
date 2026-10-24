using LayerPeek.PrusaLink.WebProxy;

namespace LayerPeek.PrusaLink.Tests.WebProxy;

public class ProxyLocationTests
{
    private const string Prefix = "/tok/";

    [Theory]
    [InlineData("/api/v1/files/usb/a.gcode", "/tok/api/v1/files/usb/a.gcode")]
    [InlineData("/", "/tok/")]
    [InlineData("http://192.168.1.20/index.html?x=1", "/tok/index.html?x=1")]
    [InlineData("http://192.168.1.20:80/a#frag", "/tok/a#frag")]
    [InlineData("HTTP://192.168.1.20/a", "/tok/a")]
    [InlineData("https://192.168.1.20/a", "https://192.168.1.20/a")]
    [InlineData("http://192.168.1.20:8080/a", "http://192.168.1.20:8080/a")]
    [InlineData("https://www.prusa3d.com/", "https://www.prusa3d.com/")]
    [InlineData("//evil.example/a", "//evil.example/a")]
    [InlineData("relative/path", "relative/path")]
    [InlineData("../up", "../up")]
    public void LocationOnThePrinterMapsToTheProxy(string location, string expected) =>
        Assert.Equal(expected, new ProxyLocation(new Uri("http://192.168.1.20/"), Prefix).Rewrite(location));

    [Theory]
    [InlineData("/printers/mk4/api/x", "/tok/api/x")]
    [InlineData("http://proxy.lan:8080/printers/mk4/", "/tok/")]
    [InlineData("/printers/other/", "/printers/other/")]
    [InlineData("/", "/")]
    [InlineData("http://proxy.lan:8080/printers/other/", "http://proxy.lan:8080/printers/other/")]
    public void PrinterBasePathIsReplacedByTheToken(string location, string expected) =>
        Assert.Equal(expected, new ProxyLocation(new Uri("http://proxy.lan:8080/printers/mk4/"), Prefix).Rewrite(location));
}
