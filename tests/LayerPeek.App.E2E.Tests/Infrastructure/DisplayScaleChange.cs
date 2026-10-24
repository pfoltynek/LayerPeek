using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

using Microsoft.Win32;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The scale of one monitor: the step relative to the recommended scale (what the undocumented API reads and sets) and,
/// for the reader, the percentages. Identified by <see cref="DevicePath"/> (stable); <see cref="GdiName"/> may change
/// with the topology.
/// </summary>
internal sealed record MonitorScaleState(
    string DevicePath,
    string Name,
    string GdiName,
    bool Primary,
    int Step,
    int MinimumStep,
    int MaximumStep,
    int Percent,
    int RecommendedPercent)
{
    public DisplayConfig.DpiScaleSteps Steps => new(MinimumStep, Step, MaximumStep);

    public override string ToString() =>
        $"{(Name.Length == 0 ? "(no name)" : Name)} {GdiName}{(Primary ? " primary" : string.Empty)}: {Percent} % (step {Step}, recommended {RecommendedPercent} %, "
        + $"up to {DisplayScaleSteps.MaximumPercent(Steps)} %)";
}

/// <summary>A registry value as exported: kind (<see cref="RegistryValueKind"/> name) and data as text.</summary>
internal sealed record RegistryValueState(string Name, string Kind, string Data);

/// <summary>A registry key with its values and subkeys (names sorted).</summary>
internal sealed record RegistryKeyState(string Name, IReadOnlyList<RegistryValueState> Values, IReadOnlyList<RegistryKeyState> SubKeys);

/// <summary>
/// The display scale of every active monitor and <c>HKCU\Control Panel\Desktop\PerMonitorSettings</c>, where Windows keeps
/// the scale of each monitor (<see langword="null"/> = the key does not exist).
/// </summary>
internal sealed record DisplayScaleState(IReadOnlyList<MonitorScaleState> Monitors, RegistryKeyState? PerMonitorSettings)
{
    /// <summary>This state with the step of the monitor <paramref name="devicePath"/> replaced.</summary>
    public DisplayScaleState WithStep(string devicePath, int step) =>
        this with
        {
            Monitors = [.. Monitors.Select(monitor => string.Equals(monitor.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase) ? monitor with { Step = step } : monitor)],
        };

    public MonitorScaleState Monitor(string devicePath) =>
        Monitors.FirstOrDefault(monitor => string.Equals(monitor.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No active monitor '{devicePath}'.");

    public override string ToString() => string.Join("; ", Monitors);
}

/// <summary>The scale steps of Windows and their percentages (pure, unit-tested).</summary>
internal static class DisplayScaleSteps
{
    /// <summary>The scales Windows offers, in order; a step moves by one entry from the recommended scale.</summary>
    public static IReadOnlyList<int> Percents { get; } = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    /// <summary>
    /// Index of the recommended scale in <see cref="Percents"/>: the minimum step always means 100 %, so it is minus the
    /// minimum step. Values the table cannot express are refused (the undocumented API changed).
    /// </summary>
    public static int RecommendedIndex(DisplayConfig.DpiScaleSteps steps)
    {
        int recommended = -steps.MinimumStep;
        if (recommended < 0 || recommended + steps.MaximumStep >= Percents.Count || steps.MaximumStep < 0)
        {
            throw new InvalidOperationException(
                $"Unexpected DPI scale steps (minimum {steps.MinimumStep}, current {steps.CurrentStep}, maximum {steps.MaximumStep}): "
                + "the undocumented display scale API changed.");
        }

        return recommended;
    }

    public static int PercentOf(DisplayConfig.DpiScaleSteps steps, int step) =>
        Percents[RecommendedIndex(steps) + Math.Clamp(step, steps.MinimumStep, steps.MaximumStep)];

    public static int CurrentPercent(DisplayConfig.DpiScaleSteps steps) => PercentOf(steps, steps.CurrentStep);

    public static int RecommendedPercent(DisplayConfig.DpiScaleSteps steps) => Percents[RecommendedIndex(steps)];

    public static int MaximumPercent(DisplayConfig.DpiScaleSteps steps) => PercentOf(steps, steps.MaximumStep);

    /// <summary>The step for <paramref name="percent"/>, or <see langword="null"/> when the monitor does not offer it.</summary>
    public static int? StepFor(DisplayConfig.DpiScaleSteps steps, int percent)
    {
        int index = Percents.ToList().IndexOf(percent);
        if (index < 0)
        {
            return null;
        }

        int step = index - RecommendedIndex(steps);
        return step >= steps.MinimumStep && step <= steps.MaximumStep ? step : null;
    }

    /// <summary>The effective DPI of a monitor at <paramref name="percent"/> (96 = 100 %).</summary>
    public static uint ExpectedDpi(int percent) => (uint)(percent * 96 / 100);

    /// <summary>For reports: the scale, or why it cannot be expressed.</summary>
    public static string TryDescribe(DisplayConfig.DpiScaleSteps steps)
    {
        try
        {
            return $"scale {CurrentPercent(steps)} % (step {steps.CurrentStep}, recommended {RecommendedPercent(steps)} %, 100–{MaximumPercent(steps)} %)";
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }
}

/// <summary>Operations that bring a registry tree back to a recorded state.</summary>
internal enum RegistryAction
{
    CreateKey,
    SetValue,
    DeleteValue,
    DeleteKeyTree,
}

/// <summary>One operation; <see cref="Path"/> is relative to the root key ("" = the root itself).</summary>
internal sealed record RegistryOperation(RegistryAction Action, string Path, string? Name = null, string? Kind = null, string? Data = null);

/// <summary>
/// <c>HKCU\Control Panel\Desktop\PerMonitorSettings</c>: Windows writes <c>DpiValue</c> of a monitor there when its scale
/// changes. Read, exported as text (compared) and restored exactly: values and keys that did not exist are deleted. Never
/// touches anything outside this key.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PerMonitorSettingsRegistry
{
    public const string SubKey = @"Control Panel\Desktop\PerMonitorSettings";

    public static RegistryKeyState? Read()
    {
        using RegistryKey? root = Registry.CurrentUser.OpenSubKey(SubKey);
        return root is null ? null : ReadKey(root, "PerMonitorSettings");
    }

    /// <summary>One line per key and value, sorted: equal exports = equal trees.</summary>
    public static List<string> Export(RegistryKeyState? root)
    {
        List<string> lines = [];
        if (root is null)
        {
            lines.Add("(key missing)");
            return lines;
        }

        Add(root, string.Empty);
        lines.Sort(StringComparer.OrdinalIgnoreCase);
        return lines;

        void Add(RegistryKeyState key, string path)
        {
            lines.Add($"[{path}]");
            foreach (RegistryValueState value in key.Values)
            {
                lines.Add($"[{path}] {value.Name} = {value.Kind}:{value.Data}");
            }

            foreach (RegistryKeyState sub in key.SubKeys)
            {
                Add(sub, path.Length == 0 ? sub.Name : $@"{path}\{sub.Name}");
            }
        }
    }

    /// <summary>What makes <paramref name="current"/> equal to <paramref name="original"/> (empty when they are equal).</summary>
    public static List<RegistryOperation> PlanRestore(RegistryKeyState? original, RegistryKeyState? current)
    {
        List<RegistryOperation> plan = [];
        if (original is null)
        {
            if (current is not null)
            {
                plan.Add(new(RegistryAction.DeleteKeyTree, string.Empty));
            }

            return plan;
        }

        PlanKey(original, current, string.Empty);
        return plan;

        void PlanKey(RegistryKeyState wanted, RegistryKeyState? now, string path)
        {
            if (now is null)
            {
                plan.Add(new(RegistryAction.CreateKey, path));
            }

            foreach (RegistryValueState value in wanted.Values)
            {
                RegistryValueState? existing = now?.Values.FirstOrDefault(v => string.Equals(v.Name, value.Name, StringComparison.OrdinalIgnoreCase));
                if (existing is null || existing.Kind != value.Kind || existing.Data != value.Data)
                {
                    plan.Add(new(RegistryAction.SetValue, path, value.Name, value.Kind, value.Data));
                }
            }

            foreach (RegistryValueState extra in now?.Values.Where(v => !wanted.Values.Any(w => string.Equals(w.Name, v.Name, StringComparison.OrdinalIgnoreCase))) ?? [])
            {
                plan.Add(new(RegistryAction.DeleteValue, path, extra.Name));
            }

            foreach (RegistryKeyState extra in now?.SubKeys.Where(s => !wanted.SubKeys.Any(w => string.Equals(w.Name, s.Name, StringComparison.OrdinalIgnoreCase))) ?? [])
            {
                plan.Add(new(RegistryAction.DeleteKeyTree, Join(path, extra.Name)));
            }

            foreach (RegistryKeyState sub in wanted.SubKeys)
            {
                PlanKey(sub, now?.SubKeys.FirstOrDefault(s => string.Equals(s.Name, sub.Name, StringComparison.OrdinalIgnoreCase)), Join(path, sub.Name));
            }
        }
    }

    public static void Execute(IEnumerable<RegistryOperation> plan)
    {
        foreach (RegistryOperation operation in plan)
        {
            string full = operation.Path.Length == 0 ? SubKey : $@"{SubKey}\{operation.Path}";
            switch (operation.Action)
            {
                case RegistryAction.CreateKey:
                    Registry.CurrentUser.CreateSubKey(full, writable: true).Dispose();
                    break;
                case RegistryAction.DeleteKeyTree:
                    Registry.CurrentUser.DeleteSubKeyTree(full, throwOnMissingSubKey: false);
                    break;
                case RegistryAction.SetValue:
                {
                    using RegistryKey key = Registry.CurrentUser.CreateSubKey(full, writable: true);
                    (object data, RegistryValueKind kind) = Parse(operation.Kind!, operation.Data!);
                    key.SetValue(operation.Name!, data, kind);
                    break;
                }

                case RegistryAction.DeleteValue:
                {
                    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(full, writable: true);
                    key?.DeleteValue(operation.Name!, throwOnMissingValue: false);
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unknown registry action {operation.Action}.");
            }
        }
    }

    public static string Describe(RegistryKeyState? root) =>
        $@"HKCU\{SubKey}:{Environment.NewLine}    " + string.Join(Environment.NewLine + "    ", Export(root));

    /// <summary>The data of a value as text; a kind that cannot be written back exactly is refused.</summary>
    internal static string Format(RegistryValueKind kind, object value) => kind switch
    {
        RegistryValueKind.DWord => ((int)value).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => ((long)value).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.String or RegistryValueKind.ExpandString => (string)value,
        RegistryValueKind.MultiString => string.Join('\0', (string[])value),
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
        _ => throw new InvalidOperationException($"A registry value of the kind {kind} cannot be restored exactly; the display scale is not changed."),
    };

    internal static (object Data, RegistryValueKind Kind) Parse(string kind, string data)
    {
        RegistryValueKind parsed = Enum.Parse<RegistryValueKind>(kind);
        return parsed switch
        {
            RegistryValueKind.DWord => (int.Parse(data, CultureInfo.InvariantCulture), parsed),
            RegistryValueKind.QWord => (long.Parse(data, CultureInfo.InvariantCulture), parsed),
            RegistryValueKind.String or RegistryValueKind.ExpandString => (data, parsed),
            RegistryValueKind.MultiString => (data.Length == 0 ? [] : data.Split('\0'), parsed),
            RegistryValueKind.Binary => (Convert.FromBase64String(data), parsed),
            _ => throw new InvalidOperationException($"Registry value kind {kind} is not supported."),
        };
    }

    private static RegistryKeyState ReadKey(RegistryKey key, string name)
    {
        List<RegistryValueState> values = [];
        foreach (string valueName in key.GetValueNames().Order(StringComparer.OrdinalIgnoreCase))
        {
            RegistryValueKind kind = key.GetValueKind(valueName);
            object value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                ?? throw new InvalidOperationException($"Registry value '{valueName}' vanished while reading.");
            values.Add(new(valueName, kind.ToString(), Format(kind, value)));
        }

        List<RegistryKeyState> subKeys = [];
        foreach (string subName in key.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase))
        {
            using RegistryKey? sub = key.OpenSubKey(subName);
            if (sub is not null)
            {
                subKeys.Add(ReadKey(sub, subName));
            }
        }

        return new(name, values, subKeys);
    }

    private static string Join(string path, string name) => path.Length == 0 ? name : $@"{path}\{name}";
}

/// <summary>The display scale did not change although the API was called (the undocumented API does not work here).</summary>
internal sealed class DisplayScaleNotAppliedException(string message) : InvalidOperationException(message);

/// <summary>
/// The undocumented read packet −3 failed or reported steps the table cannot express: the scale is unknown on this
/// Windows. Nothing was changed; a test skips (decision Q2 of T-066), the snapshot of a run only notes it.
/// </summary>
internal sealed class DisplayScaleUnavailableException(string message) : InvalidOperationException(message);

/// <summary>
/// The display scale of the active monitors (T-066) through the undocumented DPI packets of
/// <see cref="DisplayConfig"/>, without signing out. The restore sets the original steps, then makes
/// <c>PerMonitorSettings</c> exactly as before (values and keys Windows created are deleted). The system DPI
/// (<c>GetDpiForSystem</c>) never changes until sign-out.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DisplayScaleKind() : SystemChangeKind<DisplayScaleState>("DisplayScale", SystemChangeTraits.DisplayScale, SystemChangeJson.Default.DisplayScaleState)
{
    /// <summary>How long the scale may take to show in the API (and the registry to settle).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>How long <c>PerMonitorSettings</c> must stay unchanged to count as settled after a scale change.</summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1);

    public override Task<DisplayScaleState> ReadStateAsync(string? key, CancellationToken cancellationToken)
    {
        List<MonitorScaleState> monitors = [];
        foreach (DisplayConfig.Monitor monitor in DisplayConfig.Monitors(DisplayConfig.Query()))
        {
            try
            {
                DisplayConfig.DpiScaleSteps steps = DisplayConfig.GetDpiScale(monitor.SourceAdapter, monitor.SourceId);
                monitors.Add(new(
                    monitor.DevicePath,
                    monitor.Name,
                    monitor.GdiName,
                    monitor.Primary,
                    steps.CurrentStep,
                    steps.MinimumStep,
                    steps.MaximumStep,
                    DisplayScaleSteps.CurrentPercent(steps),
                    DisplayScaleSteps.RecommendedPercent(steps)));
            }
            catch (InvalidOperationException ex)
            {
                throw new DisplayScaleUnavailableException(
                    $"The display scale of {DisplayConfig.Show(monitor)} cannot be read on Windows {DisplayConfig.WindowsBuild()}: {ex.Message}");
            }
        }

        return Task.FromResult(new DisplayScaleState(monitors, PerMonitorSettingsRegistry.Read()));
    }

    /// <summary>The steps of every monitor of <paramref name="original"/> and the registry export are as recorded.</summary>
    public override bool Matches(DisplayScaleState original, DisplayScaleState current) =>
        original.Monitors.All(monitor => current.Monitors.Any(now =>
            string.Equals(now.DevicePath, monitor.DevicePath, StringComparison.OrdinalIgnoreCase) && now.Step == monitor.Step))
        && PerMonitorSettingsRegistry.Export(original.PerMonitorSettings).SequenceEqual(PerMonitorSettingsRegistry.Export(current.PerMonitorSettings), StringComparer.Ordinal);

    public override string Describe(string? key, DisplayScaleState original)
    {
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"Display scale: {original}.");
        text.AppendLine("By hand: Settings > System > Display > select the monitor > Scale (the values above); then make the registry exactly");
        text.AppendLine("as below (delete subkeys and values that are not listed; '(key missing)' = delete the whole key):");
        text.Append("    ").Append(PerMonitorSettingsRegistry.Describe(original.PerMonitorSettings));
        return text.ToString();
    }

    /// <summary>Sets the steps of <paramref name="state"/> and waits until the API reports them; the registry is left to Windows.</summary>
    protected override async Task ApplyCoreAsync(string? key, DisplayScaleState state, CancellationToken cancellationToken)
    {
        await SetStepsAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The original steps, then <c>PerMonitorSettings</c> exactly as recorded (after Windows has written it).</summary>
    protected override async Task RestoreCoreAsync(string? key, DisplayScaleState original, CancellationToken cancellationToken)
    {
        await SetStepsAsync(original, cancellationToken).ConfigureAwait(false);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            // Windows writes DpiValue after the change; restore only once it settled, or it overwrites the restore. An
            // empty plan counts only when the key also stayed unchanged for a while (a late write would undo it).
            RegistryKeyState? settled = await SettledPerMonitorSettingsAsync(cancellationToken).ConfigureAwait(false);
            List<RegistryOperation> plan = PerMonitorSettingsRegistry.PlanRestore(original.PerMonitorSettings, settled);
            if (plan.Count == 0)
            {
                return;
            }

            PerMonitorSettingsRegistry.Execute(plan);
        }

        // Still different: the journal's check after the restore reports it and keeps the entry.
    }

    /// <summary>
    /// <c>PerMonitorSettings</c> once two reads <see cref="SettleTime"/> apart are equal (at most <see cref="Timeout"/>).
    /// </summary>
    private static async Task<RegistryKeyState?> SettledPerMonitorSettingsAsync(CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        RegistryKeyState? previous = PerMonitorSettingsRegistry.Read();
        while (true)
        {
            await Task.Delay(SettleTime, cancellationToken).ConfigureAwait(false);
            RegistryKeyState? current = PerMonitorSettingsRegistry.Read();
            if (PerMonitorSettingsRegistry.Export(previous).SequenceEqual(PerMonitorSettingsRegistry.Export(current), StringComparer.Ordinal) || DateTime.UtcNow >= deadline)
            {
                return current;
            }

            previous = current;
        }
    }

    private static async Task SetStepsAsync(DisplayScaleState state, CancellationToken cancellationToken)
    {
        List<DisplayConfig.Monitor> active = DisplayConfig.Monitors(DisplayConfig.Query());
        List<(DisplayConfig.Monitor Monitor, int Step)> pending = [];
        foreach (MonitorScaleState wanted in state.Monitors)
        {
            DisplayConfig.Monitor monitor = active.FirstOrDefault(m => string.Equals(m.DevicePath, wanted.DevicePath, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"The monitor {wanted.Name} ({wanted.DevicePath}) is not active; its scale cannot be set.");
            DisplayConfig.DpiScaleSteps now = DisplayConfig.GetDpiScale(monitor.SourceAdapter, monitor.SourceId);
            if (now.CurrentStep == wanted.Step)
            {
                continue;
            }

            if (wanted.Step < now.MinimumStep || wanted.Step > now.MaximumStep)
            {
                throw new InvalidOperationException($"Step {wanted.Step} is outside {now.MinimumStep}..{now.MaximumStep} of {wanted.Name}.");
            }

            int result = DisplayConfig.SetDpiScale(monitor.SourceAdapter, monitor.SourceId, wanted.Step);
            if (result != 0)
            {
                throw new DisplayScaleNotAppliedException($"DisplayConfigSetDeviceInfo (DPI scale) of {wanted.Name} failed with Win32 error {result}.");
            }

            pending.Add((monitor, wanted.Step));
        }

        foreach ((DisplayConfig.Monitor monitor, int step) in pending)
        {
            try
            {
                await Wait.UntilAsync(
                    () => DisplayConfig.GetDpiScale(monitor.SourceAdapter, monitor.SourceId).CurrentStep == step,
                    Timeout,
                    $"the scale step {step} of {monitor.Name}").ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new DisplayScaleNotAppliedException($"The scale of {monitor.Name} did not change to step {step}: {ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}

/// <summary>Setting one monitor to an absolute scale with the checks of T-066.</summary>
[SupportedOSPlatform("windows")]
internal static class DisplayScaleChanges
{
    /// <summary>
    /// Runs <paramref name="read"/> (a read of the scale, or the start of a change, which reads it before anything
    /// changes) and skips the test when the undocumented read packet does not work on this Windows (decision Q2).
    /// </summary>
    public static Task<T> OrSkipWhenUnavailableAsync<T>(Func<Task<T>> read) => OrSkipWhenUnavailableAsync(read, static reason => Assert.Skip(reason));

    /// <summary><see cref="OrSkipWhenUnavailableAsync{T}(Func{Task{T}})"/> with the skip made by <paramref name="skip"/> (unit tests).</summary>
    internal static async Task<T> OrSkipWhenUnavailableAsync<T>(Func<Task<T>> read, Action<string> skip)
    {
        try
        {
            return await read();
        }
        catch (DisplayScaleUnavailableException ex)
        {
            skip($"Changing the display scale at run time is not available on this Windows version: {ex.Message}");
            throw new InvalidOperationException("Unreachable: the skip did not end the test.", ex);
        }
    }

    /// <summary>
    /// Sets the monitor <paramref name="devicePath"/> to <paramref name="percent"/> and checks it with
    /// <c>GetDpiForMonitor</c> within 10 s; returns the effective DPI. A scale the monitor does not offer skips the test;
    /// a change that does not take effect (the undocumented API does not work on this Windows) restores the original and
    /// skips the test with the Windows build.
    /// </summary>
    public static async Task<uint> SetPercentAsync(this SystemChange<DisplayScaleState> change, string devicePath, int percent, CancellationToken cancellationToken = default)
    {
        DisplayScaleState current = await change.ReadAsync(cancellationToken);
        MonitorScaleState monitor = current.Monitor(devicePath);
        if (DisplayScaleSteps.StepFor(monitor.Steps, percent) is not { } step)
        {
            Assert.Skip($"{percent} % is not available on {monitor.Name} {monitor.GdiName} (100–{DisplayScaleSteps.MaximumPercent(monitor.Steps)} %).");
            return 0;
        }

        string? failure = null;
        uint expected = DisplayScaleSteps.ExpectedDpi(percent);
        uint? dpi = null;
        try
        {
            await change.ApplyAsync(current.WithStep(devicePath, step), cancellationToken);
            string gdiName = DisplayConfig.Monitors(DisplayConfig.Query()).First(m => string.Equals(m.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase)).GdiName;
            await Wait.UntilAsync(() => (dpi = DisplayConfig.EffectiveDpi(gdiName)) == expected, DisplayScaleKind.Timeout, $"GetDpiForMonitor of {monitor.Name} to report {expected}");
        }
        catch (DisplayScaleNotAppliedException ex)
        {
            failure = ex.Message;
        }
        catch (TimeoutException ex)
        {
            failure = $"{ex.Message} It reports {dpi?.ToString(CultureInfo.InvariantCulture) ?? "nothing"}.";
        }

        if (failure is not null)
        {
            await change.RestoreAsync(cancellationToken);
            Assert.Skip($"Changing the display scale at run time is not available on this Windows version (Windows {DisplayConfig.WindowsBuild()}): {failure}");
        }

        return dpi ?? 0;
    }
}
