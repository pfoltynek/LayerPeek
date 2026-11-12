using FFmpeg.AutoGen.Abstractions;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-203: the rule of the OS (<see cref="HardwareDecoderSelection"/>), the Debug test switch, the policy with its generations
/// and the rule against switching back and forth (<see cref="HardwareDecodingHistory"/>). No native code.
/// </summary>
public sealed class HardwareDecodingRuleTests
{
    private const AVHWDeviceType Software = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
    private const AVHWDeviceType VideoToolbox = AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX;

    [Theory]
    [InlineData(CameraHardwareDecoding.Off, true, Software)]
    [InlineData(CameraHardwareDecoding.Off, false, Software)]
    [InlineData(CameraHardwareDecoding.Automatic, true, VideoToolbox)]
    // Windows: software until T-125 measured D3D11VA and the user decided (T-192, question 4).
    [InlineData(CameraHardwareDecoding.Automatic, false, Software)]
    public void TheRuleOfTheOs(CameraHardwareDecoding mode, bool isMacOS, AVHWDeviceType expected) =>
        Assert.Equal(expected, HardwareDecoderSelection.Select(mode, isMacOS));

    [Fact]
    public void TheRuleOfTheRunningOs() =>
        Assert.Equal(OperatingSystem.IsMacOS() ? VideoToolbox : Software, HardwareDecoderSelection.Select(CameraHardwareDecoding.Automatic));

    /// <summary>T-204: the settings window notes "Automatic is still software" exactly where the rule says so.</summary>
    [Fact]
    public void AutomaticUsesHardwareOnlyWhereTheRuleSelectsADevice() =>
        Assert.Equal(OperatingSystem.IsMacOS(), FFmpegCameraFeedFactory.AutomaticUsesHardware);

    [Fact]
    public void AnUnknownModeIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HardwareDecoderSelection.Select((CameraHardwareDecoding)9, isMacOS: true));

    [Theory]
    [InlineData("off", Software)]
    [InlineData(" OFF ", Software)]
    [InlineData("videotoolbox", VideoToolbox)]
    [InlineData("VideoToolbox", VideoToolbox)]
    [InlineData("d3d11va", AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA)]
    public void TheTestSwitchKnowsThreeValues(string value, AVHWDeviceType expected)
    {
        Assert.True(HardwareDecoderSelection.TryParseTestOverride(value, out AVHWDeviceType device));
        Assert.Equal(expected, device);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cuda")]
    [InlineData("qsv")]
    [InlineData("1")]
    [InlineData(null)]
    public void AnyOtherValueOfTheTestSwitchIsSoftware(string? value)
    {
        Assert.False(HardwareDecoderSelection.TryParseTestOverride(value, out AVHWDeviceType device));
        Assert.Equal(Software, device);
    }

    [Fact]
    public void ThePolicyFollowsTheSettingWithANewGenerationPerChange()
    {
        HardwareDecodingPolicy policy = new(CameraHardwareDecoding.Automatic, isMacOS: true);
        HardwareDecodingChoice automatic = policy.Current;
        Assert.Equal(VideoToolbox, automatic.Device);
        Assert.Null(automatic.SoftwareReason);

        policy.Mode = CameraHardwareDecoding.Off;
        HardwareDecodingChoice off = policy.Current;
        Assert.Equal(Software, off.Device);
        Assert.Equal("turned off in the settings", off.SoftwareReason);
        Assert.True(off.Generation > automatic.Generation);

        // The same value again changes nothing.
        policy.Mode = CameraHardwareDecoding.Off;
        Assert.Same(off, policy.Current);
    }

    [Fact]
    public void OnWindowsAutomaticSaysWhyItIsSoftware()
    {
        HardwareDecodingPolicy policy = new(CameraHardwareDecoding.Automatic, isMacOS: false);

        Assert.Equal(Software, policy.Current.Device);
        Assert.Equal("not used on this OS yet", policy.Current.SoftwareReason);
    }

    [Fact]
    public void TheTestOverrideWinsOverTheSettingUntilItIsRemoved()
    {
        HardwareDecodingPolicy policy = new(CameraHardwareDecoding.Off, isMacOS: false);

        policy.SetOverride(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
        Assert.Equal(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, policy.Current.Device);
        policy.SetOverride(Software);
        Assert.Equal("test switch", policy.Current.SoftwareReason);
        policy.SetOverride(null);
        Assert.Equal("turned off in the settings", policy.Current.SoftwareReason);
    }

    [Fact]
    public void TwoFailuresInARowKeepTheCameraInSoftware()
    {
        HardwareDecodingHistory history = new();

        Assert.Equal(1, history.ReportFailure(generation: 0));
        // The next connection tries again.
        Assert.False(history.IsDisabled(generation: 0));
        Assert.Equal(2, history.ReportFailure(generation: 0));
        Assert.True(history.IsDisabled(generation: 0));
        // Nothing can succeed any more in this generation.
        history.ReportSuccess(generation: 0);
        Assert.True(history.IsDisabled(generation: 0));
    }

    [Fact]
    public void ASuccessfulSessionBetweenFailuresStartsTheCountAgain()
    {
        HardwareDecodingHistory history = new();

        history.ReportFailure(generation: 0);
        history.ReportSuccess(generation: 0);
        Assert.Equal(1, history.ReportFailure(generation: 0));
        Assert.False(history.IsDisabled(generation: 0));
    }

    [Fact]
    public void AChangedSettingForgetsTheFailures()
    {
        HardwareDecodingHistory history = new();
        history.ReportFailure(generation: 0);
        history.ReportFailure(generation: 0);

        Assert.False(history.IsDisabled(generation: 1));
        Assert.Equal(1, history.ReportFailure(generation: 1));
        // A decoder that still runs on the old choice counts in the new generation (the stricter one).
        Assert.Equal(2, history.ReportFailure(generation: 0));
        Assert.True(history.IsDisabled(generation: 1));
    }

    [Fact]
    public void EveryCameraHasItsOwnHistory()
    {
        HardwareDecodingPolicy policy = new(CameraHardwareDecoding.Automatic, isMacOS: true);

        Assert.Same(policy.HistoryOf("rtsp://10.0.0.5:554/live"), policy.HistoryOf("rtsp://10.0.0.5:554/live"));
        Assert.NotSame(policy.HistoryOf("rtsp://10.0.0.5:554/live"), policy.HistoryOf("rtsp://10.0.0.6:554/live"));
    }
}
