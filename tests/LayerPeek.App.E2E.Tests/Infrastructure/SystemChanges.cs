using System.Runtime.Versioning;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// Values of the trait <see cref="SystemChanges.Trait"/>: one per kind of change a test makes (T-066). A test carries one
/// <c>[Trait(SystemChanges.Trait, SystemChangeTraits.X)]</c> per kind it changes; <c>tools/run-e2e.ps1</c> lists the
/// values of the selected tests in its warning (text per value in <c>tools/RunE2E.SystemChanges.psm1</c>) and confirms
/// only those (<see cref="SystemChanges.KindsVariable"/>); a change of another kind is refused.
/// </summary>
internal static class SystemChangeTraits
{
    /// <summary>Light/dark mode of the taskbar and apps (T-065).</summary>
    public const string Theme = "Theme";

    /// <summary>Do Not Disturb (T-065).</summary>
    public const string DoNotDisturb = "DoNotDisturb";

    /// <summary>The test tray icon promoted onto the taskbar (T-065).</summary>
    public const string TrayPromotion = "TrayPromotion";

    /// <summary>Display scale of a monitor (T-066).</summary>
    public const string DisplayScale = "DisplayScale";

    /// <summary>A non-primary monitor switched off for a moment (T-066).</summary>
    public const string MonitorOff = "MonitorOff";

    /// <summary>Test toasts and the temporary Start menu shortcut (T-068).</summary>
    public const string Toasts = "Toasts";

    /// <summary>Temporary entries in Credential Manager (T-073).</summary>
    public const string Credentials = "Credentials";

    /// <summary>A real silent install and uninstall of the Velopack installer (T-017).</summary>
    public const string Installer = "Installer";

    /// <summary>The guard self-test's value under <c>HKCU\Software\LayerPeek.E2E.Tests</c> (no Windows setting).</summary>
    public const string TestProbe = "TestProbe";

    /// <summary>Every value, in the order of the warning.</summary>
    public static IReadOnlyList<string> All { get; } = [Theme, DoNotDisturb, TrayPromotion, DisplayScale, MonitorOff, Toasts, Credentials, Installer, TestProbe];
}

/// <summary>
/// The only way to change settings of Windows in the end-to-end tests (T-065). <c>tools/run-e2e.ps1</c> shows a warning
/// with what the selected tests change and a 5 s countdown, and only then sets <see cref="Variable"/> and
/// <see cref="KindsVariable"/>; without them (e.g. a run from the IDE with just <c>LAYERPEEK_E2E=1</c>) every test with a
/// change is skipped.
/// <para>
/// Rules for such a test: mark it <c>[Trait(SystemChanges.Trait, SystemChangeTraits.X)]</c> for every kind it changes
/// (the script finds the tests and what they change by it, T-066), call <see cref="Require"/> first, and make every
/// change through a method of this class inside <c>await using</c>. The changes record the original state in the
/// journal before they change anything and restore it when disposed; crashes are restored at the next start, by the
/// watchdog process or by <c>tools/run-e2e.ps1 -RestoreOnly</c>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SystemChanges
{
    /// <summary>Set to 1 by <c>tools/run-e2e.ps1</c> after the warning and the countdown.</summary>
    public const string Variable = "LAYERPEEK_E2E_SYSTEM_CHANGES";

    /// <summary>
    /// The trait values (<see cref="SystemChangeTraits"/>) the warning listed, comma-separated; set by
    /// <c>tools/run-e2e.ps1</c> together with <see cref="Variable"/>. Only these kinds may change.
    /// </summary>
    public const string KindsVariable = "LAYERPEEK_E2E_SYSTEM_CHANGE_KINDS";

    /// <summary>Trait of every test that changes the system; its values are <see cref="SystemChangeTraits"/>.</summary>
    public const string Trait = "SystemChanges";

    /// <summary>The journal folder, shared by all worktrees (the system state is one).</summary>
    public static string JournalDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "LayerPeek.E2E.Restore");

    public static ThemeKind Theme { get; } = new();

    public static TrayIconPromotionKind TrayIconPromotion { get; } = new();

    public static DoNotDisturbKind DoNotDisturb { get; } = new();

    public static DisplayScaleKind DisplayScale { get; } = new();

    public static MonitorDisconnectKind MonitorDisconnect { get; } = new();

    /// <summary>The notifications switch of Windows (T-068).</summary>
    public static ToastsEnabledKind ToastsEnabled { get; } = new();

    /// <summary>Windows' notification settings of the test app (T-068).</summary>
    public static ToastAppSettingsKind ToastAppSettings { get; } = new();

    /// <summary>Toast history and Start menu shortcut of the test app (T-068).</summary>
    public static TestToastsKind TestToasts { get; } = new();

    /// <summary>Session-only test entries in Credential Manager (T-073).</summary>
    public static CredentialsKind Credentials { get; } = new();

    /// <summary>A real install and uninstall of the Velopack installer (T-017).</summary>
    public static InstallerKind Installer { get; } = new();

    /// <summary>Self-test probe of the journal machinery (a registry value of the tests' own, no warning needed).</summary>
    public static ProbeKind Probe { get; } = new("Probe", trait: null);

    /// <summary>Self-test probe of the guard: handled like a system change, but touches only a value of the tests' own.</summary>
    public static ProbeKind GuardProbe { get; } = new("GuardProbe", SystemChangeTraits.TestProbe);

    /// <summary>Every kind a journal entry may name (a crash of any of them can be restored by any process).</summary>
    public static IReadOnlyDictionary<string, ISystemChangeKind> Kinds { get; } =
        new ISystemChangeKind[] { Theme, TrayIconPromotion, DoNotDisturb, DisplayScale, MonitorDisconnect, ToastsEnabled, ToastAppSettings, TestToasts, Credentials, Installer, Probe, GuardProbe }
            .ToDictionary(static kind => kind.Name, StringComparer.Ordinal);

    /// <summary>The journal of this process; the first change starts the watchdog (unless switched off).</summary>
    public static SystemChangeJournal Journal { get; } = new(
        JournalDirectory,
        Kinds,
        ProcessOwner.Current,
        static owner => owner.IsAlive(),
        SystemChangeWatchdog.IsEnabled ? static () => SystemChangeWatchdog.StartForCurrentProcess() : null);

    /// <summary>Whether the warning of <c>tools/run-e2e.ps1</c> was shown and its countdown ran out.</summary>
    public static bool IsConfirmed => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable(Variable) == "1";

    /// <summary>Whether the confirmed warning listed changes of the trait value <paramref name="trait"/>.</summary>
    public static bool IsConfirmedFor(string trait) =>
        OperatingSystem.IsWindows() && IsConfirmedFor(trait, Environment.GetEnvironmentVariable(Variable), Environment.GetEnvironmentVariable(KindsVariable));

    /// <summary>
    /// Whether <paramref name="trait"/> is confirmed by the values of <see cref="Variable"/> (<paramref name="confirmed"/>)
    /// and <see cref="KindsVariable"/> (<paramref name="kinds"/>, comma-separated, exact case).
    /// </summary>
    internal static bool IsConfirmedFor(string trait, string? confirmed, string? kinds) =>
        confirmed == "1"
        && kinds is not null
        && kinds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(trait, StringComparer.Ordinal);

    /// <summary>Why a test with a system change does not run, or <see langword="null"/>.</summary>
    public static string? SkipReason =>
        E2EFixture.SkipReason
        ?? (IsConfirmed ? null : $"Changes Windows settings: run tools/run-e2e.ps1, which shows the warning first (it sets {Variable}=1 after its countdown).");

    /// <summary>The first call of every test with a system change: skips it without the confirmed warning.</summary>
    public static void Require() => Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);

    /// <summary>
    /// The guard inside the changes themselves: no system change without the confirmed warning that listed its kind,
    /// whoever asks (a test without the matching trait value fails here instead of changing what the warning did not say).
    /// </summary>
    internal static void EnsureConfirmed(string trait)
    {
        if (!IsConfirmed)
        {
            throw new InvalidOperationException($"Refusing to change Windows settings without the warning of tools/run-e2e.ps1 ({Variable} is not 1).");
        }

        if (!IsConfirmedFor(trait))
        {
            throw new InvalidOperationException(
                $"Refusing a change of the kind '{trait}': the warning of tools/run-e2e.ps1 did not list it ({KindsVariable}="
                + $"'{Environment.GetEnvironmentVariable(KindsVariable)}'). Give the test [Trait(SystemChanges.Trait, SystemChangeTraits.{trait})].");
        }
    }

    /// <summary>Light/dark theme; restored when disposed.</summary>
    public static Task<SystemChange<ThemeState>> ThemeAsync(CancellationToken cancellationToken = default)
    {
        Require();
        return SystemChange<ThemeState>.BeginAsync(Journal, Theme, key: null, cancellationToken);
    }

    /// <summary>Do Not Disturb; restored when disposed.</summary>
    public static Task<SystemChange<DoNotDisturbState>> DoNotDisturbAsync(CancellationToken cancellationToken = default)
    {
        Require();
        return SystemChange<DoNotDisturbState>.BeginAsync(Journal, DoNotDisturb, key: null, cancellationToken);
    }

    /// <summary>
    /// Display scale of the active monitors (one change for all of them, so that the registry Windows writes is restored
    /// as one); set a monitor with <see cref="DisplayScaleChanges.SetPercentAsync"/>. Restored when disposed. Skips the
    /// test, with nothing changed, when the scale cannot be read on this Windows (T-066 Q2).
    /// </summary>
    public static Task<SystemChange<DisplayScaleState>> DisplayScaleAsync(CancellationToken cancellationToken = default)
    {
        Require();
        return DisplayScaleChanges.OrSkipWhenUnavailableAsync(
            () => SystemChange<DisplayScaleState>.BeginAsync(Journal, DisplayScale, key: null, cancellationToken));
    }

    /// <summary>
    /// The display topology, for switching a non-primary monitor off (<see cref="MonitorDisconnectChanges.SwitchOffAsync"/>).
    /// Restored when disposed.
    /// </summary>
    public static Task<SystemChange<DisplayTopologyState>> MonitorDisconnectAsync(CancellationToken cancellationToken = default)
    {
        Require();
        return SystemChange<DisplayTopologyState>.BeginAsync(Journal, MonitorDisconnect, key: null, cancellationToken);
    }

    /// <summary>
    /// Tray icon entries of <paramref name="exe"/>. Begin it before the exe shows its icon, so that entries Explorer creates
    /// afterwards lose their <c>IsPromoted</c> in the restore (they are kept, T-065 Q3); the run-wide change of
    /// <see cref="E2EFixture.TrayIconPromotion"/> does.
    /// </summary>
    public static Task<SystemChange<TrayIconPromotionState>> TrayIconPromotionAsync(string exe, CancellationToken cancellationToken = default)
    {
        Require();
        return SystemChange<TrayIconPromotionState>.BeginAsync(Journal, TrayIconPromotion, Path.GetFullPath(exe), cancellationToken);
    }

    /// <summary>The notifications switch of Windows (T-068); restored when disposed.</summary>
    public static Task<SystemChange<ToastsEnabledState>> ToastsEnabledAsync(CancellationToken cancellationToken = default)
    {
        Require();
        return SystemChange<ToastsEnabledState>.BeginAsync(Journal, ToastsEnabled, key: null, cancellationToken);
    }

    /// <summary>Windows' notification settings key of the test app (T-068); restored (usually deleted) when disposed.</summary>
    public static Task<SystemChange<RegistryKeySnapshot>> ToastAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        Require();
        return SystemChange<RegistryKeySnapshot>.BeginAsync(Journal, ToastAppSettings, key: null, cancellationToken);
    }

    /// <summary>
    /// Toasts and Start menu shortcut of a test app with toasts started from <paramref name="exe"/> (T-067, T-068); cleared
    /// by <c>--layerpeek-e2e-clear-toasts</c> of that exe when disposed. Begin it before the app starts.
    /// </summary>
    public static Task<SystemChange<TestToastsState>> TestToastsAsync(string exe, CancellationToken cancellationToken = default)
    {
        Require();
        return SystemChange<TestToastsState>.BeginAsync(Journal, TestToasts, Path.GetFullPath(exe), cancellationToken);
    }

    /// <summary>
    /// Session-only entries a test app writes to Credential Manager under <paramref name="prefix"/> (T-073, from
    /// <see cref="TestCredentials.NewPrefix"/>); every entry under it is deleted when disposed. Begin it before the app
    /// starts.
    /// </summary>
    public static Task<SystemChange<CredentialsState>> CredentialsAsync(string prefix, CancellationToken cancellationToken = default)
    {
        Require();
        return SystemChange<CredentialsState>.BeginAsync(Journal, Credentials, prefix, cancellationToken);
    }

    /// <summary>
    /// What a real install and uninstall of the Velopack installer touch (T-017); restored (a leftover installation
    /// uninstalled) when disposed. Begin it before the installer runs.
    /// </summary>
    /// <param name="version">The version of the test installer: the restore uninstalls only an installation of it (never a real one).</param>
    public static Task<SystemChange<InstallerState>> InstallerAsync(string version, CancellationToken cancellationToken = default)
    {
        Require();
        return SystemChange<InstallerState>.BeginAsync(Journal, Installer, version, cancellationToken);
    }

    /// <summary>The probe of the journal self-tests (end-to-end run only, no warning needed).</summary>
    public static Task<SystemChange<ProbeState>> ProbeAsync(CancellationToken cancellationToken = default)
    {
        E2EFixture.Require();
        return SystemChange<ProbeState>.BeginAsync(Journal, Probe, key: null, cancellationToken);
    }

    /// <summary>The probe of the guard self-test: needs the warning like a real change.</summary>
    public static Task<SystemChange<ProbeState>> GuardProbeAsync(CancellationToken cancellationToken = default)
    {
        Require();
        return SystemChange<ProbeState>.BeginAsync(Journal, GuardProbe, key: null, cancellationToken);
    }
}
