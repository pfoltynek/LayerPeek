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

    private TestRunLock? _desktopLock;
    private TestRunLock? _runLock;
    private UIA3Automation? _automation;

    /// <summary>Why the tests do not run, or <see langword="null"/>.</summary>
    public static string? SkipReason { get; } = ComputeSkipReason();

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
        try
        {
            _desktopLock = await TestRunLock.AcquireAsync(DesktopLockName, DesktopLockTimeout);
            _runLock = await Reserved.AcquireLockAsync(TimeSpan.FromMinutes(2));
            CheckIsDebugTestBuild(AppPath);
            await Reserved.SweepAsync();
            await Reserved.CreateAsync();
            PrepareResultsDirectory();
            _automation = new UIA3Automation();
        }
        catch
        {
            ReleaseLocks();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_runLock is null)
        {
            return;
        }

        try
        {
            _automation?.Dispose();
            _automation = null;
            List<string> leaked = await AppProcesses.KillMarkedAsync(Root);
            await Reserved.DeleteAsync();
            if (leaked.Count != 0)
            {
                throw new InvalidOperationException($"App processes outlived their tests and were killed: {string.Join(", ", leaked)}.");
            }
        }
        finally
        {
            ReleaseLocks();
        }
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

    /// <summary>The exe must be a Debug build that knows the test mode; anything else would run against the user's data.</summary>
    private static void CheckIsDebugTestBuild(string exe)
    {
        string dll = Path.ChangeExtension(exe, ".dll");
        if (!File.Exists(exe) || !File.Exists(dll))
        {
            throw new InvalidOperationException($"The Debug app is not built: '{exe}'. Run tools/run-e2e.ps1 (it builds it).");
        }

        if (!BinaryContains(dll, AppSwitches.DataDirectoryVariable))
        {
            throw new InvalidOperationException($"'{dll}' has no test mode (not a Debug build); refusing to start it.");
        }
    }

    /// <summary>Whether the file contains <paramref name="text"/> as UTF-16 (string literals of .NET assemblies) or UTF-8.</summary>
    internal static bool BinaryContains(string file, string text)
    {
        byte[] content = File.ReadAllBytes(file);
        return content.AsSpan().IndexOf(Encoding.Unicode.GetBytes(text)) >= 0 || content.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0;
    }
}
