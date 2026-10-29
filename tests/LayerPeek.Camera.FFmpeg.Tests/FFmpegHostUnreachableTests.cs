namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// "No route to host" of FFmpeg (T-097): AVERROR(EHOSTUNREACH) with the errno of the running OS marks the camera failure,
/// so that macOS can explain a denied Local Network permission. No network: the results are given as numbers.
/// </summary>
public sealed class FFmpegHostUnreachableTests
{
    [Fact]
    public void TheErrnoOfTheRunningSystemIsHostUnreachable()
    {
        int errno = OperatingSystem.IsMacOS() ? 65 : OperatingSystem.IsLinux() ? 113 : 10065;

        Assert.True(FFmpegDecoder.IsHostUnreachable(-errno));
    }

    [Fact]
    public void TheWindowsBuildAlsoReportsTheCRuntimeNumber()
    {
        Assert.Equal(OperatingSystem.IsWindows(), FFmpegDecoder.IsHostUnreachable(-110));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-61)]   // ECONNREFUSED (macOS)
    [InlineData(-64)]   // EHOSTDOWN (macOS)
    [InlineData(-60)]   // ETIMEDOUT (macOS)
    [InlineData(-111)]  // ECONNREFUSED (Linux)
    [InlineData(-10061)] // WSAECONNREFUSED
    [InlineData(-1094995529)] // AVERROR_INVALIDDATA
    public void OtherResultsAreNot(int result)
    {
        Assert.False(FFmpegDecoder.IsHostUnreachable(result));
    }

    [Fact]
    public void OtherSystemsNumbersAreNotTakenForTheRunningOne()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.False(FFmpegDecoder.IsHostUnreachable(-65));
        }

        if (!OperatingSystem.IsLinux())
        {
            Assert.False(FFmpegDecoder.IsHostUnreachable(-113));
        }

        if (!OperatingSystem.IsWindows())
        {
            Assert.False(FFmpegDecoder.IsHostUnreachable(-10065));
        }
    }
}
