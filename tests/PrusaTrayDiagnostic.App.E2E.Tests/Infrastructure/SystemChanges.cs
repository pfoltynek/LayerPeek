using System.Runtime.Versioning;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// The only way to change settings of Windows in the end-to-end tests (T-065). <c>tools/run-e2e.ps1</c> shows a warning
/// with what may change and a 5 s countdown, and only then sets <see cref="Variable"/>; without it (e.g. a run from the
/// IDE with just <c>PTD_E2E=1</c>) every test with a change is skipped.
/// <para>
/// Rules for such a test: mark it <c>[Trait(SystemChanges.Trait, SystemChanges.TraitValue)]</c> (the script finds the
/// tests that need the warning by it), call <see cref="Require"/> first, and make every change through a method of this
/// class inside <c>await using</c>. The changes record the original state in the journal before they change anything
/// and restore it when disposed; crashes are restored at the next start, by the watchdog process or by
/// <c>tools/run-e2e.ps1 -RestoreOnly</c>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SystemChanges
{
    /// <summary>Set to 1 by <c>tools/run-e2e.ps1</c> after the warning and the countdown.</summary>
    public const string Variable = "PTD_E2E_SYSTEM_CHANGES";

    /// <summary>Trait of every test that changes the system.</summary>
    public const string Trait = "SystemChanges";

    public const string TraitValue = "true";

    /// <summary>The journal folder, shared by all worktrees (the system state is one).</summary>
    public static string JournalDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "PrusaTrayDiagnostic.E2E.Restore");

    public static ThemeKind Theme { get; } = new();

    public static TrayIconPromotionKind TrayIconPromotion { get; } = new();

    public static DoNotDisturbKind DoNotDisturb { get; } = new();

    /// <summary>The notifications switch of Windows (T-068).</summary>
    public static ToastsEnabledKind ToastsEnabled { get; } = new();

    /// <summary>Windows' notification settings of the test app (T-068).</summary>
    public static ToastAppSettingsKind ToastAppSettings { get; } = new();

    /// <summary>Toast history and Start menu shortcut of the test app (T-068).</summary>
    public static TestToastsKind TestToasts { get; } = new();

    /// <summary>Self-test probe of the journal machinery (a registry value of the tests' own, no warning needed).</summary>
    public static ProbeKind Probe { get; } = new("Probe", changesSystem: false);

    /// <summary>Self-test probe of the guard: handled like a system change, but touches only a value of the tests' own.</summary>
    public static ProbeKind GuardProbe { get; } = new("GuardProbe", changesSystem: true);

    /// <summary>Every kind a journal entry may name (a crash of any of them can be restored by any process).</summary>
    public static IReadOnlyDictionary<string, ISystemChangeKind> Kinds { get; } =
        new ISystemChangeKind[] { Theme, TrayIconPromotion, DoNotDisturb, ToastsEnabled, ToastAppSettings, TestToasts, Probe, GuardProbe }.ToDictionary(static kind => kind.Name, StringComparer.Ordinal);

    /// <summary>The journal of this process; the first change starts the watchdog (unless switched off).</summary>
    public static SystemChangeJournal Journal { get; } = new(
        JournalDirectory,
        Kinds,
        ProcessOwner.Current,
        static owner => owner.IsAlive(),
        SystemChangeWatchdog.IsEnabled ? static () => SystemChangeWatchdog.StartForCurrentProcess() : null);

    /// <summary>Whether the warning of <c>tools/run-e2e.ps1</c> was shown and its countdown ran out.</summary>
    public static bool IsConfirmed => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable(Variable) == "1";

    /// <summary>Why a test with a system change does not run, or <see langword="null"/>.</summary>
    public static string? SkipReason =>
        E2EFixture.SkipReason
        ?? (IsConfirmed ? null : $"Changes Windows settings: run tools/run-e2e.ps1, which shows the warning first (it sets {Variable}=1 after its countdown).");

    /// <summary>The first call of every test with a system change: skips it without the confirmed warning.</summary>
    public static void Require() => Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);

    /// <summary>The guard inside the changes themselves: no system change without the confirmed warning, whoever asks.</summary>
    internal static void EnsureConfirmed()
    {
        if (!IsConfirmed)
        {
            throw new InvalidOperationException($"Refusing to change Windows settings without the warning of tools/run-e2e.ps1 ({Variable} is not 1).");
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
    public static Task<SystemChange<RegistryKeyState>> ToastAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        Require();
        return SystemChange<RegistryKeyState>.BeginAsync(Journal, ToastAppSettings, key: null, cancellationToken);
    }

    /// <summary>
    /// Toasts and Start menu shortcut of a test app with toasts started from <paramref name="exe"/> (T-067, T-068); cleared
    /// by <c>--ptd-e2e-clear-toasts</c> of that exe when disposed. Begin it before the app starts.
    /// </summary>
    public static Task<SystemChange<TestToastsState>> TestToastsAsync(string exe, CancellationToken cancellationToken = default)
    {
        Require();
        return SystemChange<TestToastsState>.BeginAsync(Journal, TestToasts, Path.GetFullPath(exe), cancellationToken);
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
