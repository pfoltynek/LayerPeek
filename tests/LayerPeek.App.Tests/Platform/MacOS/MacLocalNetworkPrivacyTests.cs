using Microsoft.Extensions.Logging;

using LayerPeek.App.Platform.MacOS;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// The local network permission of macOS 15+ (T-097): only hosts in the local network, one log line per subject that says
/// where to allow the access, never the address. No native calls, so it runs on every OS.
/// </summary>
public sealed class MacLocalNetworkPrivacyTests
{
    private readonly ListLogger<MacLocalNetworkPrivacy> _logger = new();

    [Fact]
    public void LocalHostsMayBeDeniedAndAreLoggedOncePerSubject()
    {
        MacLocalNetworkPrivacy privacy = new(_logger);

        Assert.True(privacy.MayBeDenied("192.168.1.20", "printer 'mk4'"));
        Assert.True(privacy.MayBeDenied("192.168.1.20", "printer 'MK4'"));
        Assert.True(privacy.MayBeDenied("[fe80::2]", "the camera of printer 'mk4'"));

        Assert.Equal(2, _logger.Entries.Count);
        (LogLevel level, string message, _) = _logger.Entries[0];
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("printer 'mk4'", message, StringComparison.Ordinal);
        Assert.Contains("Local Network access for LayerPeek may be off", message, StringComparison.Ordinal);
        Assert.Contains("System Settings > Privacy & Security > Local Network", message, StringComparison.Ordinal);
        Assert.All(_logger.Entries, static e => Assert.DoesNotContain("192.168.1.20", e.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("printer.example.com")]
    [InlineData("8.8.8.8")]
    public void OtherHostsAreNotAndAreNotLogged(string? host)
    {
        MacLocalNetworkPrivacy privacy = new(_logger);

        Assert.False(privacy.MayBeDenied(host, "printer 'mk4'"));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void ManyThreadsLogOnce()
    {
        MacLocalNetworkPrivacy privacy = new(_logger);

        Parallel.For(0, 64, _ => Assert.True(privacy.MayBeDenied("10.0.0.6", "printer 'xl'")));

        Assert.Single(_logger.Entries);
    }
}
