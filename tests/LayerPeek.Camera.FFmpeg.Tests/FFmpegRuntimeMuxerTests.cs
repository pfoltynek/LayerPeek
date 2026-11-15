namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// The muxer camera recording needs (T-144: fragmented MP4) is in the loaded libraries on both platforms: the minimal
/// builds add it with --enable-muxer=mp4 (macOS since T-147, Windows since T-202).
/// </summary>
public sealed class FFmpegRuntimeMuxerTests
{
    [FFmpegFact]
    public void TheMp4MuxerIsAvailable()
    {
        Assert.True(FFmpegRuntime.HasMuxer("mp4"));
    }

    [FFmpegFact]
    public void AMuxerThatIsNotBuiltInIsReportedAsMissing()
    {
        Assert.False(FFmpegRuntime.HasMuxer("layerpeek-no-such-muxer"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AMissingShortNameIsRejected(string? shortName)
    {
        Assert.ThrowsAny<ArgumentException>(() => FFmpegRuntime.HasMuxer(shortName!));
    }
}
