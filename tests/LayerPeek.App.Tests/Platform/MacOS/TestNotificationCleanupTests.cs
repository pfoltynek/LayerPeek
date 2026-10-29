#if DEBUG
using LayerPeek.App.Composition;
using LayerPeek.App.Notifications;
using LayerPeek.App.Platform.MacOS;
using LayerPeek.App.Tests.Notifications;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>The cleanup of the test bundle's notifications (T-020) on a fake center: never the production bundle.</summary>
public sealed class TestNotificationCleanupTests
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(5);

    private readonly ListLogger<TestNotificationCleanup> _logger = new();

    [Theory]
    [InlineData(AppEnvironment.ProductionBundleIdentifier)]
    [InlineData("io.github.pfoltynek.LayerPeek.E2E.Other")]
    [InlineData("io.github.pfoltynek.layerpeek.e2e")]
    public void OnlyTheTestBundleIsCleaned(string bundleIdentifier)
    {
        FakeUserNotificationCenter center = new(bundleIdentifier);

        Assert.Throws<ArgumentException>(() => new TestNotificationCleanup(center, _logger));
        Assert.Equal(0, center.Removals);
    }

    [Fact]
    public void TheTestBundleIsTheProductionBundleWithASuffix()
    {
        Assert.Equal(AppEnvironment.ProductionBundleIdentifier + ".E2E", AppEnvironment.TestBundleIdentifier);
        Assert.NotEqual(AppEnvironment.ProductionBundleIdentifier, AppEnvironment.TestBundleIdentifier);
    }

    [Fact]
    public async Task DeliveredNotificationsAreRemovedAndCheckedGone()
    {
        FakeUserNotificationCenter center = new(AppEnvironment.TestBundleIdentifier);
        center.Post(new UserNotificationRequest("1", "MK4", "Print paused", PlaySound: false, LaunchArgument: null));

        IReadOnlyList<string> failures = await new TestNotificationCleanup(center, _logger).RunAsync(Short);

        Assert.Empty(failures);
        Assert.Equal(0, center.Delivered);
        Assert.Contains(_logger.Entries, static entry => entry.Message.Contains("were removed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANotificationThatArrivesLateIsRemovedByARepetition()
    {
        FakeUserNotificationCenter center = new(AppEnvironment.TestBundleIdentifier) { StubbornDelivered = 2 };
        center.Post(new UserNotificationRequest("1", "MK4", "Print paused", PlaySound: false, LaunchArgument: null));

        Assert.Empty(await new TestNotificationCleanup(center, _logger).RunAsync(Short));
        Assert.Equal(3, center.Removals);
        Assert.Equal(0, center.Delivered);
    }

    [Fact]
    public async Task WhatCannotBeRemovedInTimeIsReported()
    {
        FakeUserNotificationCenter center = new(AppEnvironment.TestBundleIdentifier) { StubbornDelivered = int.MaxValue };
        center.Post(new UserNotificationRequest("1", "MK4", "Print paused", PlaySound: false, LaunchArgument: null));

        IReadOnlyList<string> failures = await new TestNotificationCleanup(center, _logger).RunAsync(TimeSpan.FromMilliseconds(300));

        Assert.Contains("still in the notification center", Assert.Single(failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailingCenterIsReportedNotThrown()
    {
        FakeUserNotificationCenter center = new(AppEnvironment.TestBundleIdentifier) { CountFailure = new TimeoutException("no reply") };

        IReadOnlyList<string> failures = await new TestNotificationCleanup(center, _logger).RunAsync(TimeSpan.FromMilliseconds(300));

        Assert.Contains("TimeoutException", Assert.Single(failures), StringComparison.Ordinal);
    }
}
#endif
