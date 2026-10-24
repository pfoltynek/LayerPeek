using System.Globalization;
using System.Text;

using LayerPeek.App.E2E.Tests.Infrastructure;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// Self-tests of the display changes of T-066 in the end-to-end run, only with the confirmed warning: the scale of the
/// primary and of the second monitor (100 → 125 → 150 % → original, <c>GetDpiForMonitor</c> after every step,
/// <c>PerMonitorSettings</c> exactly as before), a switched-off second monitor (topology and the windows of other apps
/// back), and both changes of a killed child process restored by the start cleanup and by the watchdog. Every report
/// (results folder) has the Windows build, the system DPI and the DPI of every monitor before and after.
/// </summary>
public sealed class DisplayChangeTests
{
    private static readonly int[] Percents = [100, 125, 150];

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public Task DisplayScaleOfThePrimaryMonitorIsChangedAndRestored() => ScaleIsChangedAndRestoredAsync(primary: true);

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public Task DisplayScaleOfTheSecondMonitorIsChangedAndRestored() => ScaleIsChangedAndRestoredAsync(primary: false);

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.MonitorOff)]
    public async Task SecondMonitorIsSwitchedOffAndBackWithTheWindowsOfOtherApps()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        StringBuilder report = new();
        report.AppendLine("Before:").Append(DisplayConfig.Report());
        SystemChange<DisplayTopologyState> topology = await SystemChanges.MonitorDisconnectAsync(cancellationToken);
        try
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"Original topology: {topology.Original}");
            MonitorOff off = await topology.SwitchOffAsync(cancellationToken: cancellationToken);
            report.Append(MonitorDisconnectChanges.Describe(off));
            report.AppendLine("Monitor off:").Append(DisplayConfig.Report());

            List<string> notBack = await topology.RestoreAndCheckWindowsAsync(off, cancellationToken);
            DisplayTopologyState restored = await topology.ReadAsync(cancellationToken);
            report.AppendLine(CultureInfo.InvariantCulture, $"Restored topology: {restored}");
            report.AppendLine("After:").Append(DisplayConfig.Report());

            Assert.True(SystemChanges.MonitorDisconnect.Matches(topology.Original, restored), string.Join(Environment.NewLine, DisplayTopology.Differences(topology.Original.Monitors, restored.Monitors)));
            report.AppendLine(CultureInfo.InvariantCulture, $"Windows of other apps not back on their monitor: {(notBack.Count == 0 ? "none" : string.Join(Environment.NewLine + "  ", notBack.Prepend(string.Empty)))}");
            Assert.True(notBack.Count == 0, $"Windows did not move these windows of other apps back (the test never moves them; is 'Remember window locations based on monitor connection' on?):{Environment.NewLine}{string.Join(Environment.NewLine, notBack)}");
        }
        finally
        {
            await topology.DisposeAsync();
            Screenshots.SaveText(report.ToString(), "monitor-off-report");
        }
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public async Task DisplayScaleChangeOfAKilledProcessIsRestoredByTheStartCleanup()
    {
        SystemChanges.Require();
        await KilledScaleChildIsRestoredAsync(watchdog: false);
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public async Task DisplayScaleChangeOfAKilledProcessIsRestoredByTheWatchdog()
    {
        SystemChanges.Require();
        await KilledScaleChildIsRestoredAsync(watchdog: true);
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.MonitorOff)]
    public async Task SwitchedOffMonitorOfAKilledProcessIsRestoredByTheStartCleanup()
    {
        SystemChanges.Require();
        await KilledMonitorOffChildIsRestoredAsync(watchdog: false);
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.MonitorOff)]
    public async Task SwitchedOffMonitorOfAKilledProcessIsRestoredByTheWatchdog()
    {
        SystemChanges.Require();
        await KilledMonitorOffChildIsRestoredAsync(watchdog: true);
    }

    private static async Task ScaleIsChangedAndRestoredAsync(bool primary)
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        StringBuilder report = new();
        report.AppendLine("Before:").Append(DisplayConfig.Report());
        string label = primary ? "scale-primary" : "scale-second";
        SystemChange<DisplayScaleState> scale = await SystemChanges.DisplayScaleAsync(cancellationToken);
        try
        {
            MonitorScaleState? monitor = scale.Original.Monitors.FirstOrDefault(m => m.Primary == primary);
            Assert.SkipWhen(monitor is null, "Needs a second (not primary) monitor.");
            report.AppendLine(CultureInfo.InvariantCulture, $"Monitor: {monitor}");
            report.AppendLine(PerMonitorSettingsRegistry.Describe(scale.Original.PerMonitorSettings));
            uint systemDpi = DisplayConfig.SystemDpi();

            foreach (int percent in Percents)
            {
                uint dpi = await scale.SetPercentAsync(monitor!.DevicePath, percent, cancellationToken);
                report.AppendLine(CultureInfo.InvariantCulture, $"{percent} %: monitorDpi={dpi}, systemDpi={DisplayConfig.SystemDpi()}");
                Assert.Equal(DisplayScaleSteps.ExpectedDpi(percent), dpi);
                // The system DPI stays the one of the sign-in (T-065, Remains manual 1).
                Assert.Equal(systemDpi, DisplayConfig.SystemDpi());
            }

            await scale.DisposeAsync();
            DisplayScaleState restored = await SystemChanges.DisplayScale.ReadStateAsync(null, cancellationToken);
            report.AppendLine("After:").Append(DisplayConfig.Report());
            report.AppendLine(PerMonitorSettingsRegistry.Describe(restored.PerMonitorSettings));
            Assert.Equal(monitor!.Step, restored.Monitor(monitor.DevicePath).Step);
            Assert.Equal(PerMonitorSettingsRegistry.Export(scale.Original.PerMonitorSettings), PerMonitorSettingsRegistry.Export(restored.PerMonitorSettings));
            uint? effective = null;
            await Wait.UntilAsync(
                () => (effective = DisplayConfig.EffectiveDpi(restored.Monitor(monitor.DevicePath).GdiName)) == DisplayScaleSteps.ExpectedDpi(monitor.Percent),
                DisplayScaleKind.Timeout,
                $"the original DPI of {monitor.Name}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Restored: monitorDpi={effective}");
        }
        finally
        {
            await scale.DisposeAsync();
            Screenshots.SaveText(report.ToString(), $"{label}-report");
        }
    }

    private static async Task KilledScaleChildIsRestoredAsync(bool watchdog)
    {
        DisplayScaleState original = await DisplayScaleChanges.OrSkipWhenUnavailableAsync(
            () => SystemChanges.DisplayScale.ReadStateAsync(null, TestContext.Current.CancellationToken));
        MonitorScaleState primary = original.Monitors.First(static m => m.Primary);
        Assert.SkipWhen(primary.MinimumStep == primary.MaximumStep, $"{primary.Name} offers only one scale.");
        string before = DisplayConfig.Report();

        await SystemChangeTests.KilledChildIsRestoredAsync(SystemChanges.DisplayScale, watchdog);

        await Wait.UntilAsync(
            () => DisplayConfig.EffectiveDpi(primary.GdiName) == DisplayScaleSteps.ExpectedDpi(primary.Percent),
            DisplayScaleKind.Timeout,
            $"the original DPI of {primary.Name}");
        Screenshots.SaveText($"Before:{Environment.NewLine}{before}After:{Environment.NewLine}{DisplayConfig.Report()}", $"killed-scale-{(watchdog ? "watchdog" : "start-cleanup")}-report");
    }

    private static async Task KilledMonitorOffChildIsRestoredAsync(bool watchdog)
    {
        DisplayTopologyState original = await SystemChanges.MonitorDisconnect.ReadStateAsync(null, TestContext.Current.CancellationToken);
        Assert.SkipWhen(!original.Monitors.Any(static m => !m.Primary), "Needs a second (not primary) monitor.");
        string before = DisplayConfig.Report();

        await SystemChangeTests.KilledChildIsRestoredAsync(SystemChanges.MonitorDisconnect, watchdog);

        Assert.Equal(original.Monitors.Count, DisplayConfig.DisplayMonitors().Count);
        Screenshots.SaveText($"Before:{Environment.NewLine}{before}After:{Environment.NewLine}{DisplayConfig.Report()}", $"killed-monitor-off-{(watchdog ? "watchdog" : "start-cleanup")}-report");
    }
}
