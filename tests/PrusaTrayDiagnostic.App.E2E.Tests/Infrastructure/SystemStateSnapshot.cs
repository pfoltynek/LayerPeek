using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// The state the system changes of T-065 and T-066 touch, taken at the start of an end-to-end run and compared at its end,
/// after a successful as well as a failed run: theme (and <c>ColorPrevalence</c>), Do Not Disturb (read only when the
/// warning listed Do Not Disturb or toasts: it opens the notification center), Explorer's notification area entries, the
/// display topology and scale (with <c>PerMonitorSettings</c>; the text also has the Windows build, system DPI and the
/// DPI of every monitor), what the test toasts touch (T-068) and the journal folder.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed record SystemStateSnapshot(
    ThemeState Theme,
    int? ColorPrevalence,
    DoNotDisturbState? DoNotDisturb,
    IReadOnlyList<(string Key, string? ExecutablePath, int? IsPromoted)> TrayIcons,
    IReadOnlyList<string> JournalEntries)
{
    /// <summary>Why Do Not Disturb could not be read, or <see langword="null"/> (the Do Not Disturb self-test reports it).</summary>
    public string? DoNotDisturbError { get; init; }

    /// <summary>What the toasts of the test app touch (T-068).</summary>
    public ToastSystemState? Toasts { get; init; }

    /// <summary>Scale of every active monitor and <c>PerMonitorSettings</c> (T-066), or <see langword="null"/> when unreadable.</summary>
    public DisplayScaleState? DisplayScale { get; init; }

    /// <summary>The active display topology (T-066), or <see langword="null"/> when unreadable.</summary>
    public DisplayTopologyState? DisplayTopology { get; init; }

    /// <summary>Windows build, system DPI and the DPI of every monitor, for the results (T-066).</summary>
    public string? DisplayReport { get; init; }

    /// <summary>Why the display state could not be read, or <see langword="null"/>.</summary>
    public string? DisplayError { get; init; }

    public static async Task<SystemStateSnapshot> CaptureAsync(bool includeDoNotDisturb, CancellationToken cancellationToken = default)
    {
        DoNotDisturbState? doNotDisturb = null;
        string? error = null;
        if (includeDoNotDisturb)
        {
            try
            {
                doNotDisturb = await SystemChanges.DoNotDisturb.ReadStateAsync(null, cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Runtime.InteropServices.COMException or FlaUI.Core.Exceptions.FlaUIException)
            {
                // Not comparable then; the other states still are.
                error = ex.Message;
            }
        }

        // Read apart: an unknown scale (the undocumented packet −3 does not work here) still leaves the topology
        // comparable. Neither ever fails the run; an unreadable state is only noted and not compared.
        List<string> displayErrors = [];
        DisplayScaleState? scale = await ReadDisplayAsync(
            () => SystemChanges.DisplayScale.ReadStateAsync(null, cancellationToken), "Display scale", displayErrors);
        DisplayTopologyState? topology = await ReadDisplayAsync(
            () => SystemChanges.MonitorDisconnect.ReadStateAsync(null, cancellationToken), "Display topology", displayErrors);
        string? displayError = displayErrors.Count == 0 ? null : string.Join(" ", displayErrors);

        return new(
            await SystemChanges.Theme.ReadStateAsync(null, cancellationToken),
            ThemeKind.ColorPrevalence(),
            doNotDisturb,
            TrayIconPromotionKind.ReadAll(),
            [.. SystemChanges.Journal.PendingFiles().Select(Path.GetFileName).OfType<string>()])
        {
            DoNotDisturbError = error,
            DisplayScale = scale,
            DisplayTopology = topology,
            DisplayReport = DisplayConfig.Report(),
            DisplayError = displayError,
            Toasts = ToastSystemState.Capture(),
        };
    }

    /// <summary>A read of the display state that never fails the snapshot: an error is added to <paramref name="errors"/>.</summary>
    internal static async Task<T?> ReadDisplayAsync<T>(Func<Task<T>> read, string what, List<string> errors)
        where T : class
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add($"{what}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Differences of <paramref name="end"/> from this start state. Every tray entry that existed must keep its
    /// <c>IsPromoted</c>. A new entry of <paramref name="testedExe"/> is a difference when it is promoted (the restore of
    /// the run-wide tray change keeps new entries but removes their <c>IsPromoted</c>, T-065 Q3; 0 written by Explorer is
    /// fine); otherwise, like new entries of other programs (Explorer creates one for any program that shows an icon), it
    /// is listed in <paramref name="notes"/>.
    /// </summary>
    public List<string> Compare(SystemStateSnapshot end, string testedExe, List<string> notes)
    {
        List<string> differences = [];
        if (Theme != end.Theme)
        {
            differences.Add($"Theme: {Theme} at the start, {end.Theme} at the end.");
        }

        if (ColorPrevalence != end.ColorPrevalence)
        {
            differences.Add($"ColorPrevalence: {Show(ColorPrevalence)} at the start, {Show(end.ColorPrevalence)} at the end.");
        }

        if (DoNotDisturb is not null && end.DoNotDisturb is not null && DoNotDisturb != end.DoNotDisturb)
        {
            differences.Add($"Do Not Disturb: {DoNotDisturb.Enabled} at the start, {end.DoNotDisturb.Enabled} at the end.");
        }

        foreach ((string key, string? path, int? promoted) in TrayIcons)
        {
            (string Key, string? ExecutablePath, int? IsPromoted)? now = end.TrayIcons.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
            if (now is null)
            {
                notes.Add($"Tray icon entry {key} ({path}) is gone (Explorer removed it).");
            }
            else if (now.Value.IsPromoted != promoted)
            {
                differences.Add($"Tray icon entry {key} ({path}): IsPromoted {Show(promoted)} at the start, {Show(now.Value.IsPromoted)} at the end.");
            }
        }

        foreach ((string key, string? path, int? promoted) in end.TrayIcons)
        {
            if (TrayIcons.Any(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            bool ofTestedExe = path is not null && string.Equals(path, testedExe, StringComparison.OrdinalIgnoreCase);
            if (ofTestedExe && !TrayIconPromotionKind.IsNotPromoted(promoted))
            {
                differences.Add($"Tray icon entry {key} ({path}, IsPromoted {Show(promoted)}) appeared during the run.");
            }
            else
            {
                notes.Add($"Tray icon entry {key} ({path}) appeared during the run (created by Explorer when the icon was first shown).");
            }
        }

        if (Toasts is not null && end.Toasts is not null)
        {
            Toasts.Compare(end.Toasts, differences, notes);
        }

        if (DisplayTopology is not null && end.DisplayTopology is not null && !SystemChanges.MonitorDisconnect.Matches(DisplayTopology, end.DisplayTopology))
        {
            differences.Add($"Display topology: {DisplayTopology} at the start, {end.DisplayTopology} at the end.");
        }

        if (DisplayScale is not null && end.DisplayScale is not null && !SystemChanges.DisplayScale.Matches(DisplayScale, end.DisplayScale))
        {
            differences.Add(
                $"Display scale: {DisplayScale} at the start, {end.DisplayScale} at the end; PerMonitorSettings at the start:{Environment.NewLine}"
                + $"  {PerMonitorSettingsRegistry.Describe(DisplayScale.PerMonitorSettings)}{Environment.NewLine}at the end:{Environment.NewLine}"
                + $"  {PerMonitorSettingsRegistry.Describe(end.DisplayScale.PerMonitorSettings)}");
        }

        if (end.JournalEntries.Count != 0)
        {
            differences.Add($"The journal {SystemChanges.JournalDirectory} still holds: {string.Join(", ", end.JournalEntries)}.");
        }

        return differences;
    }

    public override string ToString()
    {
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"Theme: {Theme}, ColorPrevalence={Show(ColorPrevalence)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Do Not Disturb: {(DoNotDisturb is null ? $"(not read{(DoNotDisturbError is null ? string.Empty : ": " + DoNotDisturbError)})" : DoNotDisturb.Enabled ? "on" : "off")}; SHQueryUserNotificationState: {DoNotDisturbKind.QueryUserNotificationState()}");
        text.Append(DisplayReport ?? string.Empty);
        if (DisplayError is not null)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Display state not readable: {DisplayError}");
        }

        if (DisplayScale is not null)
        {
            text.AppendLine(PerMonitorSettingsRegistry.Describe(DisplayScale.PerMonitorSettings));
        }
        text.AppendLine(CultureInfo.InvariantCulture, $"Journal: {(JournalEntries.Count == 0 ? "(empty)" : string.Join(", ", JournalEntries))}");
        text.AppendLine("Tray icon entries (HKCU\\Control Panel\\NotifyIconSettings):");
        foreach ((string key, string? path, int? promoted) in TrayIcons)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {key}: IsPromoted={Show(promoted)} {path}");
        }

        if (Toasts is not null)
        {
            text.Append(Toasts);
        }

        return text.ToString();
    }

    private static string Show(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "(missing)";
}
