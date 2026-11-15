using System.Net;

using LayerPeek.Core.Network;

namespace LayerPeek.Core.Tests.Network;

public class AddressExposureClassifierTests
{
    [Theory]
    [InlineData("http://192.168.1.5")]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://172.31.255.255")]
    [InlineData("http://169.254.1.1")]
    [InlineData("http://[fe80::1]")]
    [InlineData("http://[fe80::1%25eth0]/")]
    [InlineData("http://[fd12::1]")]
    [InlineData("http://[fc00::1]")]
    [InlineData("http://[::ffff:192.168.1.5]")]
    [InlineData("http://127.0.0.1:18080")]
    [InlineData("http://127.200.0.1")]
    [InlineData("http://[::1]:8080")]
    [InlineData("http://localhost")]
    [InlineData("http://LOCALHOST:5000/api")]
    [InlineData("http://prusa-mk4.local")]
    [InlineData("http://Prusa-MK4.LOCAL./")]
    [InlineData("http://prusa-mk4")]
    [InlineData("http://printer.lan")]
    [InlineData("http://printer.home.arpa")]
    [InlineData("http://printer.workshop.home.arpa:8080/printers/mk4")]
    [InlineData("rtsp://192.168.0.227/live")]
    [InlineData("rtsp://buddy3d.local:554/live")]
    public void LocalAddressesAreNotWarnedAbout(string address) =>
        Assert.Equal(AddressExposure.LocalNetwork, AddressExposureClassifier.Classify(new Uri(address)));

    [Theory]
    [InlineData("http://172.32.0.1")]
    [InlineData("http://172.15.255.255")]
    [InlineData("http://8.8.8.8")]
    [InlineData("http://192.169.0.1")]
    [InlineData("http://11.0.0.1")]
    [InlineData("http://[2001:db8::1]")]
    [InlineData("http://[::ffff:8.8.8.8]")]
    [InlineData("http://[fec0::1]")]
    [InlineData("http://printer.example.com")]
    [InlineData("http://printer.localdomain.example")]
    [InlineData("http://local.example.com")]
    [InlineData("rtsp://8.8.8.8/live")]
    [InlineData("rtsp://camera.example.com/live")]
    public void AddressesOutsideTheLocalNetworkAreWarnedAbout(string address) =>
        Assert.Equal(AddressExposure.UntrustedNetwork, AddressExposureClassifier.Classify(new Uri(address)));

    // Q1 (user decision 2026-11-11): by the text only, a name nobody can resolve is classified the same way (no DNS).
    [Theory]
    [InlineData("http://printerlan", AddressExposure.LocalNetwork)]
    [InlineData("http://does-not-exist.invalid", AddressExposure.UntrustedNetwork)]
    [InlineData("http://printer.fritz.box", AddressExposure.UntrustedNetwork)]
    public void NamesAreNotResolved(string address, AddressExposure expected) =>
        Assert.Equal(expected, AddressExposureClassifier.Classify(new Uri(address)));

    // Q4 (user decision 2026-11-11): CGNAT (Tailscale and other VPNs, but also providers) gets the warning.
    [Theory]
    [InlineData("http://100.64.0.1")]
    [InlineData("http://100.100.100.100")]
    [InlineData("http://100.127.255.254")]
    public void CarrierGradeNatGetsTheWarning(string address) =>
        Assert.Equal(AddressExposure.UntrustedNetwork, AddressExposureClassifier.Classify(new Uri(address)));

    [Theory]
    [InlineData("https://8.8.8.8")]
    [InlineData("https://printer.example.com")]
    [InlineData("HTTPS://192.168.1.5")]
    public void HttpsIsEncryptedForAnyHost(string address) =>
        Assert.Equal(AddressExposure.Encrypted, AddressExposureClassifier.Classify(new Uri(address)));

    [Theory]
    [InlineData("printer.lan.example.com", false)]
    [InlineData("example.local", true)]
    [InlineData("printer.", true)]
    [InlineData("", false)]
    [InlineData(".", false)]
    public void NamesAreJudgedByTheirTextOnly(string host, bool local) =>
        Assert.Equal(local, AddressExposureClassifier.IsLocalName(host));

    [Fact]
    public void ARelativeAddressIsRejected() =>
        Assert.Throws<ArgumentException>(() => AddressExposureClassifier.Classify(new Uri("/api", UriKind.Relative)));

    [Fact]
    public void AnyIpAddressAndBroadcastAreNotLocal()
    {
        Assert.False(AddressExposureClassifier.IsLocal(IPAddress.Any));
        Assert.False(AddressExposureClassifier.IsLocal(IPAddress.Broadcast));
        Assert.False(AddressExposureClassifier.IsLocal(IPAddress.Parse("224.0.0.251")));
    }
}
