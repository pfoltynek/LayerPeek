using System.Text;

using FlaUI.UIA3;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Tests.Shared;

[assembly: AssemblyFixture(typeof(E2EFixture))]

// One real app at a time: windows, focus and the screen are shared.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// The on-demand end-to-end run (T-044) of the real <c>PrusaTrayDiagnostic.exe</c> (Debug build) for the whole test
/// assembly. Without <c>PTD_E2E=1</c> (a normal <c>dotnet test</c>) or outside Windows nothing is touched and every test
/// is skipped.
/// <para>
/// Isolation: every app started by these tests runs in the Debug-only test mode with its data folder under
/// <see cref="Root"/> (reserved per worktree, T-032) and its own instance name suffix, so the user's data, Credential
/// Manager, Start menu shortcut and running instance are never touched. Every started app carries the marker argument
/// <c>--ptd-e2e-run=&lt;folder&gt;</c>; before a reserved folder is deleted (start sweep after a crash, end of the run),
/// the app processes marked with it are killed. The user's own instance never carries the marker.
/// </para>
/// <para>
/// System changes (T-065): before anything starts, the journal of <see cref="SystemChanges"/> is restored (changes of a
/// crashed run) and the touched system state is recorded; after the last test the run-wide tray change and anything a
/// test left in the journal are restored, and the state must equal the recorded one, or the run fails.
/// </para>
/// </summary>
public sealed class E2EFixture : IAsyncLifetime
{
    /// <summary>The switch of the on-demand run (set by <c>tools/run-e2e.ps1</c>).</summary>
    public const string RunVariable = "PTD_E2E";

    /// <summary>Optional: where the screenshots go (the script passes its results folder).</summary>
    public const string ResultsVariable = "PTD_E2E_RESULTS";

    /// <summary>Optional: the app exe to test; default the Debug build of this worktree.</summary>
    public const string AppVariable = "PTD_E2E_APP";

    private static readonly ReservedTempDirectory Reserved = ReservedTempDirectory.ForCurrentWorktree(
        "PrusaTrayDiagnostic.App.E2E.Tests",
        beforeDelete: static directory => AppProcesses.KillMarkedAsync(directory));

    /// <summary>
    /// Machine-wide (all worktrees): the tests drive the one shared desktop – the real mouse, the foreground window, the
    /// notification area. A run of another worktree at the same time activates its windows, which closes this run's tray
    /// menu (an Avalonia popup closes when deactivated) in the middle of reading it (T-062).
    /// </summary>
    private const string DesktopLockName = "PrusaTrayDiagnostic.App.E2E.Desktop";

    /// <summary>Long enough for a full run of another worktree to finish.</summary>
    private static readonly TimeSpan DesktopLockTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// <c>1</c> in a child test process the self-tests of T-065 start (one test method, e.g. a change that gets killed):
    /// the parent holds the locks and owns the folders, so the child's fixture does nothing.
    /// </summary>
    public const string ChildVariable = "PTD_E2E_CHILD";

    /// <summary>How long the start cleanup waits for a test process that still owns journal entries to end.</summary>
    private static readonly TimeSpan JournalOwnerWait = TimeSpan.FromSeconds(30);

    private TestRunLock? _desktopLock;
    private TestRunLock? _runLock;
    private UIA3Automation? _automation;
    private SystemStateSnapshot? _startState;

    /// <summary>Why the tests do not run, or <see langword="null"/>.</summary>
    public static string? SkipReason { get; } = ComputeSkipReason();

    /// <summary>
    /// <c>1</c> in the restore process of <c>tools/run-e2e.ps1 -RestoreOnly</c> (also the watchdog's), which runs only
    /// <see cref="SystemChangeRestore.RestorePendingSystemChanges"/>; it may run while a test run holds every lock.
    /// </summary>
    public const string RestoreVariable = "PTD_E2E_RESTORE";

    /// <summary>Whether this process is a child test process of a self-test (see <see cref="ChildVariable"/>).</summary>
    public static bool IsChildProcess { get; } = Environment.GetEnvironmentVariable(ChildVariable) == "1";

    /// <summary>Whether this process is the restore process (see <see cref="RestoreVariable"/>).</summary>
    public static bool IsRestoreProcess { get; } = Environment.GetEnvironmentVariable(RestoreVariable) == "1";

    /// <summary>A child or restore process: no locks, no folders, no unit tests.</summary>
    public static bool IsAuxiliaryProcess => IsChildProcess || IsRestoreProcess;

    /// <summary>
    /// The run-wide tray icon change of a run with confirmed system changes (begun before any app starts, restored after
    /// the last test, T-065): tests promote the test icon with <see cref="TrayIconPromotionExtensions.PromoteAsync"/>;
    /// at its end every entry of the tested exe gets its original <c>IsPromoted</c> back, and entries Explorer created
    /// during the run are kept without <c>IsPromoted</c> (T-065 Q3). <see langword="null"/> unless the warning listed
    /// <see cref="SystemChangeTraits.TrayPromotion"/> (T-066).
    /// </summary>
    internal SystemChange<TrayIconPromotionState>? TrayIconPromotion { get; private set; }

    /// <summary>Reserved for this run: data folders of the started apps.</summary>
    public static string Root => Reserved.Path;

    /// <summary>Screenshots of this run (replaced by the next run).</summary>
    public static string ResultsDirectory { get; } = Environment.GetEnvironmentVariable(ResultsVariable) is { Length: > 0 } results
        ? Path.GetFullPath(results)
        : Path.Combine(Path.GetTempPath(), $"PrusaTrayDiagnostic.App.E2E.Results.{TestWorktree.Id}");

    /// <summary>The exe under test.</summary>
    public static string AppPath { get; } = Environment.GetEnvironmentVariable(AppVariable) is { Length: > 0 } app
        ? Path.GetFullPath(app)
        : Path.Combine(TestWorktree.Root, "src", "PrusaTrayDiagnostic.App", "bin", "Debug", "net11.0", "PrusaTrayDiagnostic.exe");

    internal UIA3Automation Automation => _automation ?? throw new InvalidOperationException(SkipReason ?? "Not initialized.");

    /// <summary>Skips the calling test unless the on-demand run is on.</summary>
    public static void Require() => Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);

    public async ValueTask InitializeAsync()
    {
        if (SkipReason is not null)
        {
            return;
        }

        // Window rectangles of the app in physical pixels at any scaling.
        Native.SetProcessDpiAwarenessContext(Native.PerMonitorAwareV2);
        if (IsAuxiliaryProcess)
        {
            // The parent run holds the locks and owns the folders.
            return;
        }

        try
        {
            _desktopLock = await TestRunLock.AcquireAsync(DesktopLockName, DesktopLockTimeout);
            _runLock = await Reserved.AcquireLockAsync(TimeSpan.FromMinutes(2));
            CheckIsDebugTestBuild(AppPath);

            // System changes of a crashed run go back before anything starts (T-065, layer 2), under the desktop lock.
            SystemChangeRestoreReport restored = await SystemChanges.Journal.RestorePendingAsync(includeOwn: false, JournalOwnerWait, CancellationToken.None);
            await Reserved.SweepAsync();
            await Reserved.CreateAsync();
            PrepareResultsDirectory();
            if (!restored.IsEmpty)
            {
                Screenshots.SaveText(restored.ToString(), "system-changes-restored-at-start");
            }

            if (!restored.Success)
            {
                throw new InvalidOperationException($"System changes of an earlier run could not be restored; nothing runs.{Environment.NewLine}{restored}");
            }

            _automation = new UIA3Automation();
            // Reading Do Not Disturb opens the notification center: only in a run whose warning listed a change of it.
            _startState = await SystemStateSnapshot.CaptureAsync(
                includeDoNotDisturb: SystemChanges.IsConfirmedFor(SystemChangeTraits.DoNotDisturb) || SystemChanges.IsConfirmedFor(SystemChangeTraits.Toasts));
            Screenshots.SaveText(_startState.ToString(), "system-state-start");
            if (SystemChanges.IsConfirmedFor(SystemChangeTraits.TrayPromotion))
            {
                TrayIconPromotion = await SystemChanges.TrayIconPromotionAsync(AppPath);
            }
        }
        catch (Exception startFailure)
        {
            try
            {
                await RestoreAfterFailedStartAsync();
            }
            catch (Exception restoreFailure)
            {
                // Both matter: why the start failed, and that the tray change is still in the journal (its watchdog or the
                // next start restores it).
                throw new AggregateException("The end-to-end run could not start, and its tray icon change was not restored.", startFailure, restoreFailure);
            }
            finally
            {
                ReleaseLocks();
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_runLock is null)
        {
            return;
        }

        List<string> problems = [];
        try
        {
            _automation?.Dispose();
            _automation = null;
            List<string> leaked = await AppProcesses.KillMarkedAsync(Root);
            if (leaked.Count != 0)
            {
                problems.Add($"App processes outlived their tests and were killed: {string.Join(", ", leaked)}.");
            }

            try
            {
                // After the apps are gone (their tray icons too), before the reserved folder goes.
                await CheckSystemStateAsync(problems);
            }
            finally
            {
                await Reserved.DeleteAsync();
            }

            if (problems.Count != 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
            }
        }
        finally
        {
            ReleaseLocks();
        }
    }

    /// <summary>
    /// End of the run (T-065): restores the run-wide tray change and whatever a test left in the journal (a failure of its
    /// own restore), then compares the system with the state at the start.
    /// </summary>
    private async Task CheckSystemStateAsync(List<string> problems)
    {
        if (TrayIconPromotion is { } tray)
        {
            TrayIconPromotion = null;
            try
            {
                await tray.DisposeAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Any failure (also a TimeoutException of the journal lock): the entry stays, the restore below tries again.
                problems.Add($"The tray icon entries were not restored: {ex.Message}");
            }
        }

        SystemChangeRestoreReport left = await SystemChanges.Journal.RestorePendingAsync(includeOwn: true, JournalOwnerWait, CancellationToken.None);
        if (!left.IsEmpty)
        {
            Screenshots.SaveText(left.ToString(), "system-changes-left-by-tests");
            problems.Add($"Tests left system changes in the journal (their own restore failed):{Environment.NewLine}{left}");
        }

        if (_startState is null)
        {
            return;
        }

        SystemStateSnapshot end = await SystemStateSnapshot.CaptureAsync(includeDoNotDisturb: _startState.DoNotDisturb is not null);
        if (_startState.DoNotDisturb is not null && end.DoNotDisturb is null)
        {
            problems.Add($"Do Not Disturb could not be read at the end of the run: {end.DoNotDisturbError}");
        }

        List<string> notes = [];
        List<string> differences = _startState.Compare(end, AppPath, notes);
        Screenshots.SaveText(
            $"{end}{Environment.NewLine}Differences from the start:{Environment.NewLine}{(differences.Count == 0 ? "  none" : string.Join(Environment.NewLine, differences.Select(static d => "  " + d)))}"
            + $"{Environment.NewLine}Notes:{Environment.NewLine}{(notes.Count == 0 ? "  none" : string.Join(Environment.NewLine, notes.Select(static n => "  " + n)))}",
            "system-state-end");
        if (differences.Count != 0)
        {
            problems.Add($"The system is not as before the run (see docs/troubleshooting.md, tools/run-e2e.ps1 -RestoreOnly):{Environment.NewLine}{string.Join(Environment.NewLine, differences)}");
        }
    }

    /// <summary>A start that failed after the run-wide tray change began restores it at once.</summary>
    private async Task RestoreAfterFailedStartAsync()
    {
        if (TrayIconPromotion is { } tray)
        {
            TrayIconPromotion = null;
            await tray.DisposeAsync();
        }

        _automation?.Dispose();
        _automation = null;
    }

    private void ReleaseLocks()
    {
        _runLock?.Dispose();
        _runLock = null;
        _desktopLock?.Dispose();
        _desktopLock = null;
    }

    /// <summary>
    /// Empties the results folder of the previous run: only a folder with the marker of these tests is deleted, so a
    /// mistyped <c>PTD_E2E_RESULTS</c> never deletes someone else's files.
    /// </summary>
    private static void PrepareResultsDirectory()
    {
        string marker = Path.Combine(ResultsDirectory, ResultsMarkerFileName);
        if (Directory.Exists(ResultsDirectory))
        {
            if (File.Exists(marker))
            {
                Directory.Delete(ResultsDirectory, recursive: true);
            }
            else if (Directory.EnumerateFileSystemEntries(ResultsDirectory).Any())
            {
                throw new InvalidOperationException($"'{ResultsDirectory}' is not empty and not a results folder of these tests.");
            }
        }

        Directory.CreateDirectory(ResultsDirectory);
        File.WriteAllText(marker, TestWorktree.Root);
    }

    private const string ResultsMarkerFileName = ".ptd-e2e-results";

    private static string? ComputeSkipReason()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "The end-to-end tests of the real app run on Windows only.";
        }

        if (Environment.GetEnvironmentVariable(RunVariable) != "1")
        {
            return $"On-demand end-to-end tests: run tools/run-e2e.ps1 (or set {RunVariable}=1 after building the Debug app).";
        }

        return null;
    }

    /// <summary>
    /// The exe under test is a Native AOT publish (T-064: <c>tools/run-e2e.ps1 -PublishAot</c> also runs the log tests against
    /// the Debug AOT exe): one native file, no managed assembly next to it.
    /// </summary>
    public static bool IsNativeAot => !File.Exists(Path.ChangeExtension(AppPath, ".dll"));

    /// <summary>
    /// The exe must be a Debug build that knows the test mode (the managed assembly next to it, or the Native AOT exe
    /// itself); anything else would run against the user's data.
    /// </summary>
    private static void CheckIsDebugTestBuild(string exe)
    {
        if (!File.Exists(exe))
        {
            throw new InvalidOperationException($"The Debug app is not built: '{exe}'. Run tools/run-e2e.ps1 (it builds it).");
        }

        string code = IsNativeAot ? exe : Path.ChangeExtension(exe, ".dll");
        if (!BinaryContains(code, AppSwitches.DataDirectoryVariable))
        {
            throw new InvalidOperationException($"'{code}' has no test mode (not a Debug build); refusing to start it.");
        }
    }

    /// <summary>Whether the file contains <paramref name="text"/> as UTF-16 (string literals of .NET assemblies) or UTF-8.</summary>
    internal static bool BinaryContains(string file, string text)
    {
        byte[] content = File.ReadAllBytes(file);
        return content.AsSpan().IndexOf(Encoding.Unicode.GetBytes(text)) >= 0 || content.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0;
    }
}
