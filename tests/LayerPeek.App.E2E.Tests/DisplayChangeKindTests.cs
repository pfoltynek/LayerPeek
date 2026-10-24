using System.Text.Json;

using Microsoft.Win32;

using LayerPeek.App.E2E.Tests.Infrastructure;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// Unit tests of the pure parts of the display changes of T-066 (scale steps, the <c>PerMonitorSettings</c> restore plan,
/// the topology without a monitor, the window check, the interop layouts): they run in every <c>dotnet test</c> and
/// read or change nothing of the system.
/// </summary>
public sealed class DisplayChangeKindTests
{
    private const string First = @"\\?\DISPLAY#DELA0E7#5&1&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string Second = @"\\?\DISPLAY#DELA0E7#5&2&UID4354#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    [Fact]
    public void ScaleStepsAreRelativeToTheRecommendedScale()
    {
        // Recommended 150 %: the minimum step -2 is 100 %, the maximum step 2 is 200 %.
        DisplayConfig.DpiScaleSteps steps = new(-2, 0, 2);

        Assert.Equal(150, DisplayScaleSteps.RecommendedPercent(steps));
        Assert.Equal(150, DisplayScaleSteps.CurrentPercent(steps));
        Assert.Equal(200, DisplayScaleSteps.MaximumPercent(steps));
        Assert.Equal(-2, DisplayScaleSteps.StepFor(steps, 100));
        Assert.Equal(-1, DisplayScaleSteps.StepFor(steps, 125));
        Assert.Equal(2, DisplayScaleSteps.StepFor(steps, 200));
    }

    [Theory]
    [InlineData(225)]
    [InlineData(110)]
    [InlineData(50)]
    public void ScaleTheMonitorDoesNotOfferHasNoStep(int percent) =>
        Assert.Null(DisplayScaleSteps.StepFor(new DisplayConfig.DpiScaleSteps(-2, 1, 2), percent));

    [Fact]
    public void RecommendedHundredPercentStartsAtStepZero()
    {
        DisplayConfig.DpiScaleSteps steps = new(0, 2, 3);

        Assert.Equal(100, DisplayScaleSteps.RecommendedPercent(steps));
        Assert.Equal(150, DisplayScaleSteps.CurrentPercent(steps));
        Assert.Equal(0, DisplayScaleSteps.StepFor(steps, 100));
        Assert.Equal(1, DisplayScaleSteps.StepFor(steps, 125));
        Assert.Equal(3, DisplayScaleSteps.StepFor(steps, 175));
        Assert.Null(DisplayScaleSteps.StepFor(steps, 200));
    }

    [Theory]
    [InlineData(1, 0, 2)]
    [InlineData(-2, 0, 20)]
    [InlineData(0, 0, -1)]
    public void StepsTheTableCannotExpressAreRefused(int minimum, int current, int maximum) =>
        Assert.Throws<InvalidOperationException>(() => DisplayScaleSteps.CurrentPercent(new DisplayConfig.DpiScaleSteps(minimum, current, maximum)));

    [Theory]
    [InlineData(100, 96u)]
    [InlineData(125, 120u)]
    [InlineData(150, 144u)]
    [InlineData(175, 168u)]
    [InlineData(200, 192u)]
    public void ExpectedDpiOfAScale(int percent, uint dpi) => Assert.Equal(dpi, DisplayScaleSteps.ExpectedDpi(percent));

    [Fact]
    public void RegistryRestoreDeletesAKeyThatDidNotExist()
    {
        RegistryKeyState current = Key("PerMonitorSettings", [], [Key("DELA0E7", [Value("DpiValue", 1)], [])]);

        Assert.Equal([new RegistryOperation(RegistryAction.DeleteKeyTree, string.Empty)], PerMonitorSettingsRegistry.PlanRestore(null, current));
        Assert.Empty(PerMonitorSettingsRegistry.PlanRestore(null, null));
    }

    [Fact]
    public void RegistryRestoreWritesBackChangedValuesAndDeletesWhatWindowsCreated()
    {
        RegistryKeyState original = Key("PerMonitorSettings", [], [Key("A", [Value("DpiValue", 0), Value("Other", 7)], [])]);
        RegistryKeyState current = Key("PerMonitorSettings", [], [
            Key("A", [Value("DpiValue", 2), Value("Extra", 1)], []),
            Key("B", [Value("DpiValue", 1)], []),
        ]);

        List<RegistryOperation> plan = PerMonitorSettingsRegistry.PlanRestore(original, current);

        Assert.Equal(
            [
                new RegistryOperation(RegistryAction.DeleteKeyTree, "B"),
                new RegistryOperation(RegistryAction.SetValue, "A", "DpiValue", "DWord", "0"),
                new RegistryOperation(RegistryAction.SetValue, "A", "Other", "DWord", "7"),
                new RegistryOperation(RegistryAction.DeleteValue, "A", "Extra"),
            ],
            plan);
    }

    [Fact]
    public void RegistryRestoreRecreatesAMissingTree()
    {
        RegistryKeyState original = Key("PerMonitorSettings", [], [Key("A", [Value("DpiValue", -1)], [])]);

        Assert.Equal(
            [
                new RegistryOperation(RegistryAction.CreateKey, string.Empty),
                new RegistryOperation(RegistryAction.CreateKey, "A"),
                new RegistryOperation(RegistryAction.SetValue, "A", "DpiValue", "DWord", "-1"),
            ],
            PerMonitorSettingsRegistry.PlanRestore(original, null));
    }

    [Fact]
    public void RegistryRestoreOfAnEqualTreeDoesNothingAndIgnoresCase()
    {
        RegistryKeyState original = Key("PerMonitorSettings", [], [Key("Abc", [Value("DpiValue", 1)], [])]);
        RegistryKeyState current = Key("PerMonitorSettings", [], [Key("abc", [Value("dpivalue", 1)], [])]);

        Assert.Empty(PerMonitorSettingsRegistry.PlanRestore(original, current));
    }

    [Fact]
    public void RegistryValueOfAnotherKindIsWrittenBack()
    {
        RegistryKeyState original = Key("PerMonitorSettings", [], [Key("A", [new RegistryValueState("DpiValue", "DWord", "1")], [])]);
        RegistryKeyState current = Key("PerMonitorSettings", [], [Key("A", [new RegistryValueState("DpiValue", "String", "1")], [])]);

        Assert.Equal([new RegistryOperation(RegistryAction.SetValue, "A", "DpiValue", "DWord", "1")], PerMonitorSettingsRegistry.PlanRestore(original, current));
        Assert.NotEqual(PerMonitorSettingsRegistry.Export(original), PerMonitorSettingsRegistry.Export(current));
    }

    [Fact]
    public void RegistryExportIsSortedSoOrderDoesNotMatter()
    {
        RegistryKeyState one = Key("PerMonitorSettings", [], [Key("B", [Value("DpiValue", 1)], []), Key("A", [Value("DpiValue", 0)], [])]);
        RegistryKeyState two = Key("PerMonitorSettings", [], [Key("A", [Value("DpiValue", 0)], []), Key("B", [Value("DpiValue", 1)], [])]);

        Assert.Equal(PerMonitorSettingsRegistry.Export(one), PerMonitorSettingsRegistry.Export(two));
        Assert.Equal(["(key missing)"], PerMonitorSettingsRegistry.Export(null));
        Assert.NotEqual(PerMonitorSettingsRegistry.Export(Key("PerMonitorSettings", [], [])), PerMonitorSettingsRegistry.Export(null));
    }

    [Theory]
    [InlineData(RegistryValueKind.DWord)]
    [InlineData(RegistryValueKind.QWord)]
    [InlineData(RegistryValueKind.String)]
    [InlineData(RegistryValueKind.ExpandString)]
    [InlineData(RegistryValueKind.MultiString)]
    [InlineData(RegistryValueKind.Binary)]
    public void RegistryValuesSurviveTheTextForm(RegistryValueKind kind)
    {
        object value = kind switch
        {
            RegistryValueKind.DWord => -2,
            RegistryValueKind.QWord => 1L << 40,
            RegistryValueKind.MultiString => new[] { "a", "b c" },
            RegistryValueKind.Binary => new byte[] { 0, 1, 255 },
            _ => "%TEMP%\\x",
        };

        (object data, RegistryValueKind back) = PerMonitorSettingsRegistry.Parse(kind.ToString(), PerMonitorSettingsRegistry.Format(kind, value));

        Assert.Equal(kind, back);
        Assert.Equal(value, data);
    }

    [Fact]
    public void RegistryValueThatCannotBeWrittenBackExactlyIsRefused() =>
        Assert.Throws<InvalidOperationException>(() => PerMonitorSettingsRegistry.Format(RegistryValueKind.None, new byte[] { 1 }));

    [Fact]
    public void ScaleStatesMatchByStepsAndRegistry()
    {
        DisplayScaleState original = Scale([Monitor(First, primary: true, step: 0), Monitor(Second, primary: false, step: -1)], Key("PerMonitorSettings", [], []));
        DisplayScaleKind kind = SystemChanges.DisplayScale;

        Assert.True(kind.Matches(original, original with { Monitors = [.. original.Monitors.Reverse()] }));
        Assert.False(kind.Matches(original, original.WithStep(Second, 1)));
        Assert.False(kind.Matches(original, original with { PerMonitorSettings = null }));
        Assert.False(kind.Matches(original, original with { Monitors = [original.Monitors[0]] }));
        // A monitor that was off at the start does not count.
        Assert.True(kind.Matches(original with { Monitors = [original.Monitors[0]] }, original));
    }

    [Fact]
    public void WithStepChangesOnlyThatMonitor()
    {
        DisplayScaleState state = Scale([Monitor(First, primary: true, step: 0), Monitor(Second, primary: false, step: 0)], null);

        DisplayScaleState changed = state.WithStep(Second.ToUpperInvariant(), 2);

        Assert.Equal(0, changed.Monitor(First).Step);
        Assert.Equal(2, changed.Monitor(Second).Step);
        Assert.Throws<InvalidOperationException>(() => changed.Monitor("missing"));
    }

    [Fact]
    public void ScaleStateSurvivesTheJournalJson()
    {
        DisplayScaleState state = Scale(
            [Monitor(First, primary: true, step: 1)],
            Key("PerMonitorSettings", [], [Key("A", [Value("DpiValue", 1), new RegistryValueState("Name", "String", "x")], [Key("Nested", [], [])])]));

        JsonElement json = SystemChanges.DisplayScale.Serialize(state);
        DisplayScaleState back = SystemChanges.DisplayScale.Deserialize(json);

        Assert.True(SystemChanges.DisplayScale.Matches(state, back));
        Assert.Equal(PerMonitorSettingsRegistry.Export(state.PerMonitorSettings), PerMonitorSettingsRegistry.Export(back.PerMonitorSettings));
        Assert.Contains("perMonitorSettings", json.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void InteropLayoutsMatchTheWindowsStructures()
    {
        Assert.Equal(72, System.Runtime.CompilerServices.Unsafe.SizeOf<DisplayConfig.PathInfo>());
        Assert.Equal(64, System.Runtime.CompilerServices.Unsafe.SizeOf<DisplayConfig.ModeInfo>());
        Assert.Equal(20, System.Runtime.CompilerServices.Unsafe.SizeOf<DisplayConfig.DeviceInfoHeader>());
        Assert.Equal(84, System.Runtime.CompilerServices.Unsafe.SizeOf<DisplayConfig.SourceDeviceName>());
        Assert.Equal(420, System.Runtime.CompilerServices.Unsafe.SizeOf<DisplayConfig.TargetDeviceName>());
        Assert.Equal(32, System.Runtime.CompilerServices.Unsafe.SizeOf<DisplayConfig.DpiScaleGet>());
        Assert.Equal(24, System.Runtime.CompilerServices.Unsafe.SizeOf<DisplayConfig.DpiScaleSet>());
        Assert.Equal(104, System.Runtime.CompilerServices.Unsafe.SizeOf<DisplayConfig.MonitorInfoEx>());
    }

    [Fact]
    public void TopologyWithoutAPathKeepsOnlyTheModesOfTheOtherPaths()
    {
        DisplayConfig.ModeInfo[] modes = [Mode(1, 10), Mode(2, 11), Mode(1, 20), Mode(2, 21)];
        DisplayConfig.PathInfo[] paths = [Path(source: 0, target: 1, id: 1), Path(source: 2, target: 3, id: 2)];

        (DisplayConfig.PathInfo[] keptPaths, DisplayConfig.ModeInfo[] keptModes) = DisplayTopology.WithoutPath(paths, modes, 0);

        DisplayConfig.PathInfo kept = Assert.Single(keptPaths);
        Assert.Equal(2u, kept.TargetInfo.Id);
        Assert.Equal(0u, kept.SourceInfo.ModeInfoIdx);
        Assert.Equal(1u, kept.TargetInfo.ModeInfoIdx);
        Assert.Equal([20u, 21u], keptModes.Select(static m => m.Id));
        // The input is not changed.
        Assert.Equal(2u, paths[1].SourceInfo.ModeInfoIdx);
    }

    [Fact]
    public void TopologyWithoutAPathKeepsASharedSourceModeAndInvalidIndices()
    {
        DisplayConfig.ModeInfo[] modes = [Mode(1, 10), Mode(2, 11), Mode(2, 12)];
        DisplayConfig.PathInfo[] paths =
        [
            Path(source: 0, target: 1, id: 1),
            Path(source: 0, target: 2, id: 2),
            Path(source: DisplayConfig.ModeIndexInvalid, target: DisplayConfig.ModeIndexInvalid, id: 3),
        ];

        (DisplayConfig.PathInfo[] keptPaths, DisplayConfig.ModeInfo[] keptModes) = DisplayTopology.WithoutPath(paths, modes, 0);

        Assert.Equal([2u, 3u], keptPaths.Select(static p => p.TargetInfo.Id));
        Assert.Equal([10u, 12u], keptModes.Select(static m => m.Id));
        Assert.Equal((0u, 1u), (keptPaths[0].SourceInfo.ModeInfoIdx, keptPaths[0].TargetInfo.ModeInfoIdx));
        Assert.Equal(DisplayConfig.ModeIndexInvalid, keptPaths[1].SourceInfo.ModeInfoIdx);
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayTopology.WithoutPath(paths, modes, 3));
    }

    [Fact]
    public void StructuresSurviveBase64()
    {
        DisplayConfig.PathInfo[] paths = [Path(source: 0, target: 1, id: 7)];
        DisplayConfig.ModeInfo[] modes = [Mode(1, 10) with { SourceWidth = 2560, SourceHeight = 1440, SourceX = -1440, SourceY = 5 }];

        Assert.Equal(paths, DisplayTopology.Decode<DisplayConfig.PathInfo>(DisplayTopology.Encode(paths)));
        Assert.Equal(modes, DisplayTopology.Decode<DisplayConfig.ModeInfo>(DisplayTopology.Encode(modes)));
        Assert.Throws<InvalidOperationException>(() => DisplayTopology.Decode<DisplayConfig.ModeInfo>(Convert.ToBase64String(new byte[65])));
    }

    [Fact]
    public void TopologyDifferencesCoverMissingMovedRotatedAndExtraMonitors()
    {
        DisplayMonitorState primary = new(First, "DELL U2719D", @"\\.\DISPLAY1", 0, 0, 2560, 1440, 1);
        DisplayMonitorState rotated = new(Second, "DELL U2719D", @"\\.\DISPLAY2", -1440, -500, 1440, 2560, 2);

        Assert.Empty(DisplayTopology.Differences([primary, rotated], [rotated with { GdiName = @"\\.\DISPLAY3" }, primary]));
        Assert.Single(DisplayTopology.Differences([primary, rotated], [primary]));
        Assert.Single(DisplayTopology.Differences([primary, rotated], [primary, rotated with { Left = -1400 }]));
        Assert.Single(DisplayTopology.Differences([primary, rotated], [primary, rotated with { Rotation = 1 }]));
        Assert.Single(DisplayTopology.Differences([primary], [primary, rotated]));
        Assert.True(primary.Primary);
        Assert.False(rotated.Primary);
    }

    [Fact]
    public void WithoutMonitorRemovesItsPathAndRefusesThePrimary()
    {
        DisplayConfig.ModeInfo[] modes = [Mode(1, 10), Mode(2, 11), Mode(1, 20), Mode(2, 21)];
        DisplayConfig.PathInfo[] paths = [Path(source: 0, target: 1, id: 1), Path(source: 2, target: 3, id: 2)];
        DisplayTopologyState state = new(
            DisplayTopology.Encode(paths),
            DisplayTopology.Encode(modes),
            [new(First, "A", @"\\.\DISPLAY1", 0, 0, 2560, 1440, 1), new(Second, "B", @"\\.\DISPLAY2", 2560, 0, 2560, 1440, 1)]);

        DisplayTopologyState off = MonitorDisconnectKind.WithoutMonitor(state, Second);

        Assert.Equal(First, Assert.Single(off.Monitors).DevicePath);
        Assert.Equal(1u, Assert.Single(DisplayTopology.Decode<DisplayConfig.PathInfo>(off.Paths)).TargetInfo.Id);
        Assert.Equal(2, DisplayTopology.Decode<DisplayConfig.ModeInfo>(off.Modes).Length);
        Assert.Throws<InvalidOperationException>(() => MonitorDisconnectKind.WithoutMonitor(state, First));
        Assert.Throws<InvalidOperationException>(() => MonitorDisconnectKind.WithoutMonitor(state, "missing"));
        Assert.False(SystemChanges.MonitorDisconnect.Matches(state, off));
    }

    [Fact]
    public void WindowsOfOtherAppsCountAsBackWithinTheToleranceOrWhenClosed()
    {
        OtherAppWindow editor = new(1, "notepad", 3000, 100, 3800, 700);
        OtherAppWindow browser = new(2, "msedge", 2600, 0, 5100, 1400);
        OtherAppWindow closed = new(3, "calc", 2700, 10, 2900, 300);
        Dictionary<long, Native.Rect?> now = new()
        {
            [1] = new Native.Rect { Left = 3002, Top = 99, Right = 3801, Bottom = 704 },
            [2] = new Native.Rect { Left = 40, Top = 0, Right = 2540, Bottom = 1400 },
            [3] = null,
        };

        List<string> notBack = OtherAppWindows.NotReturned([editor, browser, closed], handle => now[handle]);

        // Process name and rectangles only: never a window title of another app (user decision 2026-10-22).
        Assert.Equal("msedge (2600,0)-(5100,1400) is now at (40,0)-(2540,1400) 2500x1400", Assert.Single(notBack));
    }

    [Fact]
    public async Task UnreadableScaleSkipsTheTestInsteadOfFailingIt()
    {
        List<string> skips = [];
        Action<string> skip = reason =>
        {
            skips.Add(reason);
            throw new OperationCanceledException("skipped");
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => DisplayScaleChanges.OrSkipWhenUnavailableAsync<int>(() => throw new DisplayScaleUnavailableException("packet -3 failed"), skip));
        InvalidOperationException other = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DisplayScaleChanges.OrSkipWhenUnavailableAsync<int>(() => throw new InvalidOperationException("journal"), skip));
        int read = await DisplayScaleChanges.OrSkipWhenUnavailableAsync(() => Task.FromResult(7), skip);

        string reason = Assert.Single(skips);
        Assert.Contains("packet -3 failed", reason, StringComparison.Ordinal);
        Assert.Contains("not available on this Windows version", reason, StringComparison.Ordinal);
        // Any other failure (e.g. a crashed run's change not restored) still fails the test.
        Assert.Equal("journal", other.Message);
        Assert.Equal(7, read);
    }

    [Fact]
    public async Task UnreadableDisplayStateIsOnlyNotedInTheSnapshot()
    {
        List<string> errors = [];

        string? scale = await SystemStateSnapshot.ReadDisplayAsync<string>(() => throw new DisplayScaleUnavailableException("packet -3 failed"), "Display scale", errors);
        string? topology = await SystemStateSnapshot.ReadDisplayAsync(() => Task.FromResult("topology"), "Display topology", errors);

        Assert.Null(scale);
        Assert.Equal("topology", topology);
        Assert.Equal(["Display scale: packet -3 failed"], errors);
    }

    [Fact]
    public void OnlyTheDisplayKindsCallTheSettersOfTheDisplayApi()
    {
        // The raw setters skip the guard of SystemChanges; only the kinds (which check the warning and journal first) use them.
        string project = System.IO.Path.Combine(LayerPeek.Tests.Shared.TestWorktree.Root, "tests", "LayerPeek.App.E2E.Tests");
        System.Text.RegularExpressions.Regex setter = new(@"DisplayConfig\.(Set|SetTopology|SetDpiScale)\(");
        string[] callers = [.. Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !file.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => setter.IsMatch(File.ReadAllText(file)))
            .Select(System.IO.Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)];

        Assert.Equal(["DisplayScaleChange.cs", "MonitorDisconnectChange.cs"], callers);
    }

    [Fact]
    public void TopologyIsNeverSavedToTheDatabase()
    {
        const uint SaveToDatabase = 0x200;

        Assert.Equal(0u, MonitorDisconnectKind.ApplySupplied & SaveToDatabase);
        Assert.All(MonitorDisconnectKind.RestoreFallbacks, static fallback => Assert.Equal(0u, fallback.Flags & SaveToDatabase));
        Assert.Equal(
            ["SDC_TOPOLOGY_EXTEND", "SDC_USE_DATABASE_CURRENT"],
            MonitorDisconnectKind.RestoreFallbacks.Select(static fallback => fallback.Name));
        Assert.Equal(0x80u | 0x20u | 0x400u, MonitorDisconnectKind.ApplySupplied);
    }

    [Fact]
    public void SwitchedOffMonitorIsRestoredBeforeTheScale()
    {
        Assert.True(SystemChanges.MonitorDisconnect.RestoreOrder < SystemChanges.DisplayScale.RestoreOrder);
        Assert.Equal(SystemChangeTraits.DisplayScale, SystemChanges.DisplayScale.Trait);
        Assert.Equal(SystemChangeTraits.MonitorOff, SystemChanges.MonitorDisconnect.Trait);
    }

    private static RegistryKeyState Key(string name, IReadOnlyList<RegistryValueState> values, IReadOnlyList<RegistryKeyState> subKeys) => new(name, values, subKeys);

    private static RegistryValueState Value(string name, int value) => new(name, "DWord", value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static MonitorScaleState Monitor(string devicePath, bool primary, int step) =>
        new(devicePath, "DELL U2719D", primary ? @"\\.\DISPLAY1" : @"\\.\DISPLAY2", primary, step, 0, 3, 100, 100);

    private static DisplayScaleState Scale(IReadOnlyList<MonitorScaleState> monitors, RegistryKeyState? registry) => new(monitors, registry);

    private static DisplayConfig.ModeInfo Mode(int type, uint id) => new() { InfoType = type, Id = id };

    private static DisplayConfig.PathInfo Path(uint source, uint target, uint id) =>
        new() { SourceInfo = new() { ModeInfoIdx = source, Id = id }, TargetInfo = new() { ModeInfoIdx = target, Id = id } };
}
