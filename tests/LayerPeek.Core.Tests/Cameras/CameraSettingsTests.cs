using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Tests.Cameras;

public class CameraSettingsTests
{
    [Fact]
    public void DisabledHasNoStreamUri()
    {
        Assert.False(CameraSettings.Disabled.Enabled);
        Assert.Null(CameraSettings.Disabled.StreamUri);
    }

    [Theory]
    [InlineData("rtsp://192.168.0.227/live")]
    [InlineData("rtsp://192.168.0.227:8554/live")]
    [InlineData("RTSP://camera.local/live/track1")]
    [InlineData("rtsp://[fe80::1]/live")]
    [InlineData("rtsp://camera.local")]
    public void EnabledCameraAcceptsRtspUri(string url)
    {
        CameraSettings camera = new(enabled: true, new Uri(url));

        Assert.True(camera.Enabled);
        Assert.Equal(url, camera.StreamUri!.OriginalString);
    }

    [Fact]
    public void EnabledCameraRequiresStreamUri()
    {
        Assert.Throws<ArgumentException>(() => new CameraSettings(enabled: true, streamUri: null));
    }

    [Fact]
    public void DisabledCameraKeepsStreamUri()
    {
        CameraSettings camera = new(enabled: false, new Uri("rtsp://192.168.0.227/live"));

        Assert.False(camera.Enabled);
        Assert.Equal("rtsp://192.168.0.227/live", camera.StreamUri!.OriginalString);
    }

    [Theory]
    [InlineData("http://192.168.0.227/live", CameraUrlProblem.UnsupportedScheme)]
    [InlineData("rtsps://192.168.0.227/live", CameraUrlProblem.UnsupportedScheme)]
    [InlineData("rtsp:live", CameraUrlProblem.MissingHost)]
    [InlineData("rtsp:///live", CameraUrlProblem.MissingHost)]
    [InlineData("rtsp://admin:secret@192.168.0.227/live", CameraUrlProblem.ContainsCredentials)]
    [InlineData("rtsp://admin@192.168.0.227/live", CameraUrlProblem.ContainsCredentials)]
    // No user info for Uri (host "admin", port 1234), but the text hides credentials: any "@" is rejected.
    [InlineData("rtsp://admin:1234/x@192.168.0.227/live", CameraUrlProblem.ContainsCredentials)]
    [InlineData("rtsp://192.168.0.227/live?user=a@b", CameraUrlProblem.ContainsCredentials)]
    public void InvalidStreamUriIsRejectedEvenWhenDisabled(string url, CameraUrlProblem expected)
    {
        Uri uri = new(url);

        Assert.Equal(expected, CameraSettings.ValidateStreamUri(uri));
        ArgumentException error = Assert.Throws<ArgumentException>(() => new CameraSettings(enabled: false, uri));
        // The message may reach logs or the UI; it must never echo the address (it can contain credentials).
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.0.227", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RelativeStreamUriIsRejected()
    {
        Uri uri = new("live", UriKind.Relative);

        Assert.Equal(CameraUrlProblem.NotAbsolute, CameraSettings.ValidateStreamUri(uri));
        Assert.Throws<ArgumentException>(() => new CameraSettings(enabled: true, uri));
    }

    [Theory]
    [InlineData("rtsp://192.168.0.227/live", "rtsp://192.168.0.227/live")]
    [InlineData("  rtsp://192.168.0.227/live \t", "rtsp://192.168.0.227/live")]
    public void TryParseStreamUriAcceptsAndTrims(string value, string expected)
    {
        Assert.True(CameraSettings.TryParseStreamUri(value, out Uri? uri, out CameraUrlProblem problem));
        Assert.Equal(CameraUrlProblem.None, problem);
        Assert.Equal(expected, uri.OriginalString);
    }

    [Theory]
    [InlineData(null, CameraUrlProblem.Missing)]
    [InlineData("", CameraUrlProblem.Missing)]
    [InlineData("   ", CameraUrlProblem.Missing)]
    [InlineData("192.168.0.227/live", CameraUrlProblem.NotAbsolute)]
    [InlineData("live", CameraUrlProblem.NotAbsolute)]
    [InlineData("rtsp://", CameraUrlProblem.MissingHost)]
    [InlineData("https://camera.local/live", CameraUrlProblem.UnsupportedScheme)]
    [InlineData("rtsp:live", CameraUrlProblem.MissingHost)]
    [InlineData("rtsp://user:pw@camera.local/live", CameraUrlProblem.ContainsCredentials)]
    [InlineData("rtsp://admin:1234/x@camera.local/live", CameraUrlProblem.ContainsCredentials)]
    [InlineData("rtsp://camera.local/a@b", CameraUrlProblem.ContainsCredentials)]
    public void TryParseStreamUriReportsProblem(string? value, CameraUrlProblem expected)
    {
        Assert.False(CameraSettings.TryParseStreamUri(value, out Uri? uri, out CameraUrlProblem problem));
        Assert.Null(uri);
        Assert.Equal(expected, problem);
    }

    [Theory]
    [InlineData("http://192.168.0.227/live", CameraUrlProblem.UnsupportedScheme)]
    [InlineData(" not an address ", CameraUrlProblem.NotAbsolute)]
    [InlineData("rtsp:live", CameraUrlProblem.MissingHost)]
    public void InvalidStreamUrlIsKeptOnDisabledCamera(string text, CameraUrlProblem expected)
    {
        CameraSettings camera = CameraSettings.FromInvalidStreamUrl(text);

        Assert.False(camera.Enabled);
        Assert.Null(camera.StreamUri);
        // Kept exactly as stored, so that the user can correct it.
        Assert.Equal(text, camera.InvalidStreamUrl);
        Assert.Equal(expected, camera.StreamUrlProblem);
    }

    [Fact]
    public void ValidCameraHasNoUrlProblem()
    {
        Assert.Null(CameraSettings.Disabled.InvalidStreamUrl);
        Assert.Equal(CameraUrlProblem.None, CameraSettings.Disabled.StreamUrlProblem);
        CameraSettings camera = new(enabled: true, new Uri("rtsp://192.168.0.227/live"));
        Assert.Null(camera.InvalidStreamUrl);
        Assert.Equal(CameraUrlProblem.None, camera.StreamUrlProblem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("rtsp://192.168.0.227/live")]
    [InlineData(" rtsp://192.168.0.227/live ")]
    public void FromInvalidStreamUrlRejectsMissingOrValidText(string? text)
    {
        // A missing address is no address, a valid one belongs into StreamUri.
        Assert.ThrowsAny<ArgumentException>(() => CameraSettings.FromInvalidStreamUrl(text!));
    }

    [Fact]
    public void EqualityIncludesInvalidStreamUrl()
    {
        Assert.Equal(CameraSettings.FromInvalidStreamUrl("http://a/live"), CameraSettings.FromInvalidStreamUrl("http://a/live"));
        Assert.Equal(CameraSettings.FromInvalidStreamUrl("http://a/live").GetHashCode(), CameraSettings.FromInvalidStreamUrl("http://a/live").GetHashCode());
        Assert.NotEqual(CameraSettings.FromInvalidStreamUrl("http://a/live"), CameraSettings.FromInvalidStreamUrl("http://b/live"));
        Assert.NotEqual(CameraSettings.Disabled, CameraSettings.FromInvalidStreamUrl("http://a/live"));
    }

    [Fact]
    public void ToStringNeverRevealsAnInvalidStreamUrl()
    {
        // Records print all properties by default (also via PrinterDefinition); the raw text stays out of it.
        CameraSettings camera = CameraSettings.FromInvalidStreamUrl("http://192.168.0.227/live");

        Assert.DoesNotContain("192.168.0.227", camera.ToString(), StringComparison.Ordinal);
        Assert.Contains(nameof(CameraUrlProblem.UnsupportedScheme), camera.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rtsp://admin:secret@192.168.0.227/live")]
    [InlineData("http://admin@192.168.0.227/live")]
    [InlineData("rtsp://admin:se cret@[broken/live")]
    [InlineData("admin:secret@192.168.0.227/live")]
    [InlineData("http://192.168.0.227/a@b")]
    [InlineData("rtsp://admin:secret/x@192.168.0.227/live")]
    public void FromInvalidStreamUrlRejectsTextWithCredentials(string text)
    {
        ArgumentException error = Assert.ThrowsAny<ArgumentException>(() => CameraSettings.FromInvalidStreamUrl(text));

        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    // Valid once the credentials are gone: kept as a normal address, but disabled until the user reviews it.
    [InlineData("rtsp://admin:secret@192.168.0.227/live", "rtsp://192.168.0.227/live", null, CameraUrlProblem.None)]
    [InlineData("rtsp://admin@192.168.0.227:554/live", "rtsp://192.168.0.227:554/live", null, CameraUrlProblem.None)]
    [InlineData("rtsp://admin:p@ss@192.168.0.227/live", "rtsp://192.168.0.227/live", null, CameraUrlProblem.None)]
    [InlineData(" rtsp://admin:secret@192.168.0.227/live ", "rtsp://192.168.0.227/live", null, CameraUrlProblem.None)]
    // Still invalid for another reason: kept as text without the credentials.
    [InlineData("http://admin:secret@192.168.0.227/live", null, "http://192.168.0.227/live", CameraUrlProblem.UnsupportedScheme)]
    [InlineData("rtsp://admin:se cret@[broken/live", null, "rtsp://[broken/live", CameraUrlProblem.NotAbsolute)]
    [InlineData("admin:secret@192.168.0.227/live", null, "192.168.0.227/live", CameraUrlProblem.NotAbsolute)]
    // Nothing (useful) left.
    [InlineData("rtsp://admin:secret@", null, "rtsp://", CameraUrlProblem.MissingHost)]
    [InlineData("admin:secret@", null, null, CameraUrlProblem.None)]
    // "@" outside the authority as Uri sees it: everything up to the last "@" goes.
    [InlineData("rtsp://admin:secret/x@192.168.0.227/live", "rtsp://192.168.0.227/live", null, CameraUrlProblem.None)]
    [InlineData("rtsp://admin:se/c?r#et@192.168.0.227/live", "rtsp://192.168.0.227/live", null, CameraUrlProblem.None)]
    [InlineData("http://192.168.0.227/a@secret", null, "http://secret", CameraUrlProblem.UnsupportedScheme)]
    public void StoredStreamUrlLosesItsCredentials(string text, string? expectedUri, string? expectedInvalid, CameraUrlProblem expectedProblem)
    {
        CameraSettings camera = CameraSettings.FromStoredStreamUrl(enabled: true, text);

        Assert.True(camera.CredentialsRemoved);
        Assert.False(camera.Enabled);
        Assert.Equal(expectedUri, camera.StreamUri?.OriginalString);
        Assert.Equal(expectedInvalid, camera.InvalidStreamUrl);
        Assert.Equal(expectedProblem, camera.StreamUrlProblem);
        Assert.DoesNotContain("secret", camera.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "rtsp://192.168.0.227/live", "rtsp://192.168.0.227/live", null)]
    [InlineData(false, " rtsp://192.168.0.227/live ", "rtsp://192.168.0.227/live", null)]
    [InlineData(true, "not an address", null, "not an address")]
    public void StoredStreamUrlWithoutCredentialsIsKept(bool enabled, string text, string? expectedUri, string? expectedInvalid)
    {
        CameraSettings camera = CameraSettings.FromStoredStreamUrl(enabled, text);

        Assert.False(camera.CredentialsRemoved);
        Assert.Equal(enabled && expectedUri is not null, camera.Enabled);
        Assert.Equal(expectedUri, camera.StreamUri?.OriginalString);
        Assert.Equal(expectedInvalid, camera.InvalidStreamUrl);
    }

    [Fact]
    public void CredentialsRemovedIsNotPartOfEquality()
    {
        // The flag only informs the UI after loading; the persisted value is the same.
        CameraSettings stripped = CameraSettings.FromStoredStreamUrl(enabled: false, "rtsp://admin:secret@192.168.0.227/live");

        Assert.Equal(new CameraSettings(enabled: false, new Uri("rtsp://192.168.0.227/live")), stripped);
    }

    [Fact]
    public void EqualityComparesEnabledAndAddressText()
    {
        CameraSettings a = new(enabled: true, new Uri("rtsp://192.168.0.227/live"));
        CameraSettings b = new(enabled: true, new Uri("rtsp://192.168.0.227/live"));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new CameraSettings(enabled: false, new Uri("rtsp://192.168.0.227/live")));
        Assert.NotEqual(a, new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live2")));
        Assert.NotEqual(CameraSettings.Disabled, new CameraSettings(enabled: false, new Uri("rtsp://192.168.0.227/live")));
        Assert.Equal(CameraSettings.Disabled, new CameraSettings(enabled: false, streamUri: null));
    }
}
