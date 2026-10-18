using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Core.Tests.Cameras;

public class UriRedactionTests
{
    [Theory]
    [InlineData("rtsp://192.168.0.227/live", "rtsp://192.168.0.227/live")]
    [InlineData("rtsp://192.168.0.227:8554/live/track1", "rtsp://192.168.0.227:8554/live/track1")]
    [InlineData("rtsp://camera.local/live?channel=1", "rtsp://camera.local/live?channel=1")]
    [InlineData("http://printer.local/", "http://printer.local/")]
    public void AddressWithoutUserInfoIsUnchanged(string url, string expected)
    {
        Assert.Equal(expected, UriRedaction.Redact(new Uri(url)));
    }

    [Theory]
    [InlineData("rtsp://admin:secret@192.168.0.227:554/live", "rtsp://***@192.168.0.227:554/live")]
    [InlineData("rtsp://admin@192.168.0.227/live", "rtsp://***@192.168.0.227/live")]
    [InlineData("rtsp://admin:secret%40x@camera.local/live?a=1", "rtsp://***@camera.local/live?a=1")]
    public void UserInfoIsMasked(string url, string expected)
    {
        string redacted = UriRedaction.Redact(new Uri(url));

        Assert.Equal(expected, redacted);
        Assert.DoesNotContain("admin", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RelativeUriIsRejected()
    {
        Assert.Throws<ArgumentException>(() => UriRedaction.Redact(new Uri("live", UriKind.Relative)));
    }
}
