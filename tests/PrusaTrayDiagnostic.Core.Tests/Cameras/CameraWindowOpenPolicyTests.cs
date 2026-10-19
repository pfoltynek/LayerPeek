using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Core.Tests.Cameras;

public class CameraWindowOpenPolicyTests
{
    [Fact]
    public void RememberOnWithoutRememberedStateUsesDefaults()
    {
        CameraWindowOpenPlan plan = CameraWindowOpenPolicy.Decide(CameraWindowDefaults.Default, rememberedMode: null, hasRememberedNormal: false, hasRememberedMini: false);

        Assert.Equal(new CameraWindowOpenPlan(CameraWindowMode.Normal, UseRememberedNormal: false, UseRememberedMini: false), plan);
    }

    [Fact]
    public void RememberOnWithRememberedStateUsesIt()
    {
        CameraWindowOpenPlan plan = CameraWindowOpenPolicy.Decide(CameraWindowDefaults.Default, CameraWindowMode.Mini, hasRememberedNormal: true, hasRememberedMini: true);

        Assert.Equal(new CameraWindowOpenPlan(CameraWindowMode.Mini, UseRememberedNormal: true, UseRememberedMini: true), plan);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void EachRememberedPlacementIsUsedOnItsOwn(bool normal, bool mini)
    {
        CameraWindowOpenPlan plan = CameraWindowOpenPolicy.Decide(CameraWindowDefaults.Default, CameraWindowMode.Normal, normal, mini);

        Assert.Equal(normal, plan.UseRememberedNormal);
        Assert.Equal(mini, plan.UseRememberedMini);
    }

    [Fact]
    public void RememberOffAlwaysUsesDefaultPlacement()
    {
        CameraWindowDefaults defaults = new() { RememberLastPlacement = false };

        CameraWindowOpenPlan plan = CameraWindowOpenPolicy.Decide(defaults, CameraWindowMode.Normal, hasRememberedNormal: true, hasRememberedMini: true);

        Assert.False(plan.UseRememberedNormal);
        Assert.False(plan.UseRememberedMini);
    }

    [Theory]
    [InlineData(CameraWindowMode.Normal)]
    [InlineData(CameraWindowMode.Mini)]
    [InlineData(CameraWindowMode.FullScreen)]
    public void AsLastTimeUsesRememberedMode(CameraWindowMode remembered)
    {
        Assert.Equal(remembered, CameraWindowOpenPolicy.Decide(CameraWindowDefaults.Default, remembered, false, false).Mode);
    }

    [Fact]
    public void AsLastTimeWithoutRememberedModeOpensNormal()
    {
        Assert.Equal(CameraWindowMode.Normal, CameraWindowOpenPolicy.Decide(CameraWindowDefaults.Default, null, false, false).Mode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AsLastTimeUsesRememberedModeRegardlessOfRememberToggle(bool remember)
    {
        CameraWindowDefaults defaults = new() { RememberLastPlacement = remember };

        Assert.Equal(CameraWindowMode.Mini, CameraWindowOpenPolicy.Decide(defaults, CameraWindowMode.Mini, true, true).Mode);
    }

    [Theory]
    [InlineData(CameraWindowStartMode.Normal, CameraWindowMode.Normal)]
    [InlineData(CameraWindowStartMode.Mini, CameraWindowMode.Mini)]
    [InlineData(CameraWindowStartMode.FullScreen, CameraWindowMode.FullScreen)]
    public void FixedStartModeIgnoresRememberedMode(CameraWindowStartMode start, CameraWindowMode expected)
    {
        CameraWindowDefaults defaults = new() { StartMode = start };

        Assert.Equal(expected, CameraWindowOpenPolicy.Decide(defaults, CameraWindowMode.FullScreen == expected ? CameraWindowMode.Mini : CameraWindowMode.FullScreen, true, true).Mode);
        Assert.Equal(expected, CameraWindowOpenPolicy.Decide(defaults, null, false, false).Mode);
    }

    [Fact]
    public void UnknownRememberedModeIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraWindowOpenPolicy.Decide(CameraWindowDefaults.Default, (CameraWindowMode)42, false, false));

    [Fact]
    public void NullDefaultsAreRejected() =>
        Assert.Throws<ArgumentNullException>(() => CameraWindowOpenPolicy.Decide(null!, null, false, false));
}
