using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Core.Tests.Cameras;

public class UriRedactionTests
{
    [Theory]
    [InlineData("rtsp://192.168.0.227/live", "rtsp://192.168.0.227/live")]
    [InlineData("rtsp://192.168.0.227:8554/live/track1", "rtsp://192.168.0.227:8554/live/track1")]
    [InlineData("http://printer.local/", "http://printer.local/")]
    public void AddressWithoutUserInfoIsUnchanged(string url, string expected)
    {
        Assert.Equal(expected, UriRedaction.Redact(new Uri(url)));
    }

    [Theory]
    [InlineData("rtsp://admin:secret@192.168.0.227:554/live", "rtsp://***@192.168.0.227:554/live")]
    [InlineData("rtsp://admin@192.168.0.227/live", "rtsp://***@192.168.0.227/live")]
    [InlineData("rtsp://admin:secret%40x@camera.local/live?a=1", "rtsp://***@camera.local/live?***")]
    // Uri sees host "admin", port 1234 and no user info; the text up to the last "@" is masked anyway.
    [InlineData("rtsp://admin:1234/secret@192.168.0.227/live", "rtsp://***@192.168.0.227/live")]
    [InlineData("rtsp://admin:secret@192.168.0.227/live?x=admin@b", "rtsp://***@192.168.0.227/live?***")]
    public void UserInfoIsMasked(string url, string expected)
    {
        string redacted = UriRedaction.Redact(new Uri(url));

        Assert.Equal(expected, redacted);
        Assert.DoesNotContain("admin", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rtsp://camera.local/live?channel=1", "rtsp://camera.local/live?***")]
    [InlineData("rtsp://camera.local/live?token=Tk9secret#part", "rtsp://camera.local/live?***#***")]
    [InlineData("http://printer.local/page#Tk9secret", "http://printer.local/page#***")]
    public void QueryAndFragmentAreMasked(string url, string expected)
    {
        // T-048: a token in the query of a camera address must not reach the log.
        Assert.Equal(expected, UriRedaction.Redact(new Uri(url)));
    }

    [Fact]
    public void RelativeUriIsRejected()
    {
        Assert.Throws<ArgumentException>(() => UriRedaction.Redact(new Uri("live", UriKind.Relative)));
    }
}
