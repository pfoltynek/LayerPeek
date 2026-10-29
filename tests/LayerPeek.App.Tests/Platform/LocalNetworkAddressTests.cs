using System.Net;

using LayerPeek.App.Platform;

namespace LayerPeek.App.Tests.Platform;

/// <summary>Which hosts are in the local network by their address alone (T-097, Apple TN3179).</summary>
public sealed class LocalNetworkAddressTests
{
    [Theory]
    [InlineData("192.168.1.20")]
    [InlineData("10.0.0.6")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("169.254.12.3")]
    [InlineData("224.0.0.251")]
    [InlineData("255.255.255.255")]
    [InlineData("[fe80::1]")]
    [InlineData("fe80::1%en0")]
    [InlineData("[fd12:3456::7]")]
    [InlineData("ff02::fb")]
    [InlineData("[::ffff:192.168.0.227]")]
    [InlineData("prusa-mk4")]
    [InlineData("prusa-mk4.local")]
    [InlineData("Prusa-MK4.LOCAL.")]
    [InlineData(" 192.168.1.20 ")]
    public void LocalAddressesAndNames(string host)
    {
        Assert.True(LocalNetworkAddress.IsLocal(host));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("[]")]
    [InlineData("127.0.0.1")]
    [InlineData("127.12.0.1")]
    [InlineData("[::1]")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("printer.localhost")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("100.64.0.1")]
    [InlineData("2001:db8::1")]
    [InlineData("printer.example.com")]
    [InlineData("connect.prusa3d.com")]
    public void LoopbackRoutedAndOtherHosts(string? host)
    {
        Assert.False(LocalNetworkAddress.IsLocal(host));
    }

    [Fact]
    public void TheHostOfAUriIsUnderstood()
    {
        Assert.True(LocalNetworkAddress.IsLocal(new Uri("rtsp://admin:secret@[fe80::2]:554/live").Host));
        Assert.True(LocalNetworkAddress.IsLocal(new Uri("http://192.168.1.20/").Host));
        Assert.False(LocalNetworkAddress.IsLocal(new Uri("rtsp://127.0.0.1:8554/live").Host));
        Assert.False(LocalNetworkAddress.IsLocal(IPAddress.IPv6Loopback));
    }
}
