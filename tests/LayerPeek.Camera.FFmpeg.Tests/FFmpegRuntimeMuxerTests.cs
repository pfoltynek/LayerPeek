namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// The muxer camera recording needs (T-144: fragmented MP4) is in the loaded libraries on both platforms: the BtbN
/// build on Windows has all muxers, the minimal macOS build adds it with --enable-muxer=mp4 (T-147).
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
