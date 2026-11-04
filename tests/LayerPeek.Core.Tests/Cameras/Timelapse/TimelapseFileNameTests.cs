using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>T-160: timelapse file names (user decision 2026-11-04) are recording names that are told apart.</summary>
public sealed class TimelapseFileNameTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 14, 5, 9, TimeSpan.FromHours(1));

    [Fact]
    public void ATimelapseIsNamedLikeARecordingWithTheMarker()
    {
        Assert.Equal("2026-11-04_14-05-09_Benchy_timelapse.mp4", CameraRecordingFolders.TimelapseFileName(Start, "benchy_0.4n_0.2mm_PLA_MK4S_1h2m.bgcode"));
        Assert.Equal("2026-11-04_14-05-09_timelapse.mp4", CameraRecordingFolders.TimelapseFileName(Start, null));
        Assert.True(CameraRecordingFolders.IsRecordingFileName(CameraRecordingFolders.TimelapseFileName(Start, "a:b.gcode")));
    }

    [Theory]
    [InlineData("2026-11-04_14-05-09_timelapse.mp4", true)]
    [InlineData("2026-11-04_14-05-09_Benchy_timelapse.mp4", true)]
    [InlineData("2026-11-04_14-05-09_Benchy_TIMELAPSE.MP4", true)]
    [InlineData("2026-11-04_14-05-09_Benchy_timelapse-2.mp4", true)]
    [InlineData("2026-11-04_14-05-09_Benchy_timelapse-12.mp4", true)]
    [InlineData("2026-11-04_14-05-09_Benchy.mp4", false)]
    [InlineData("2026-11-04_14-05-09.mp4", false)]
    [InlineData("2026-11-04_14-05-09-2.mp4", false)]
    [InlineData("2026-11-04_14-05-09_Benchy_dashcam-error.mp4", false)]
    [InlineData("2026-11-04_14-05-09_timelapse_Benchy.mp4", false)]
    [InlineData("2026-11-04_14-05-09_Benchy_timelapse-x.mp4", false)]
    [InlineData("2026-11-04_14-05-09_Benchy_timelapse.jpg", false)]
    [InlineData("holiday_timelapse.mp4", false)]
    [InlineData(null, false)]
    public void TimelapsesAreRecognized(string? fileName, bool expected) =>
        Assert.Equal(expected, CameraRecordingFolders.IsTimelapseFileName(fileName));
}
