using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Composition;
using LayerPeek.App.Notifications;
using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// The guards of the macOS notification center (T-020) before anything native: only the expected bundle opens it, so the test
/// host (no bundle) never calls <c>currentNotificationCenter</c>. The real framework is only used by the on-demand E2E test.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class MacUserNotificationCenterTests
{
    [Theory]
    [InlineData(null, "io.github.pfoltynek.LayerPeek", "shows no notifications")]
    [InlineData("io.github.pfoltynek.LayerPeek", null, "does not run from an app bundle")]
    [InlineData("io.github.pfoltynek.LayerPeek.E2E", "io.github.pfoltynek.LayerPeek", "not 'io.github.pfoltynek.LayerPeek.E2E'")]
    [InlineData("io.github.pfoltynek.LayerPeek", "io.github.pfoltynek.LayerPeek.E2E", "not 'io.github.pfoltynek.LayerPeek'")]
    [InlineData("io.github.pfoltynek.LayerPeek", "IO.GITHUB.PFOLTYNEK.LAYERPEEK", "not 'io.github.pfoltynek.LayerPeek'")]
    public void AnotherIdentityOpensNothing(string? expected, string? actual, string reason)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The notification center of macOS.");

        Assert.Null(MacUserNotificationCenter.TryOpen(expected, () => actual, NullLogger.Instance, out string? why));
        Assert.Contains(reason, why, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTestHostHasNoBundleIdentifier()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The bundle identifier of macOS.");

        // dotnet test runs outside any .app: the app would open no notification center here.
        Assert.Null(MacMainBundle.Identifier());
        Assert.Null(MacUserNotificationCenter.TryOpen(AppEnvironment.ProductionBundleIdentifier, MacMainBundle.Identifier, NullLogger.Instance, out _));
        Assert.Null(MacUserNotificationCenter.Shared);
    }

    [Theory]
    [InlineData(0, (int)NotificationPermission.NotDetermined)]
    [InlineData(1, (int)NotificationPermission.Denied)]
    [InlineData(2, (int)NotificationPermission.Allowed)]
    [InlineData(3, (int)NotificationPermission.Allowed)]
    [InlineData(4, (int)NotificationPermission.Allowed)]
    [InlineData(-1, (int)NotificationPermission.Unknown)]
    [InlineData(5, (int)NotificationPermission.Unknown)]
    public void TheAuthorizationStatusIsMapped(long status, int permission) =>
        Assert.Equal((NotificationPermission)permission, MacUserNotificationCenter.PermissionOf(status));
}
