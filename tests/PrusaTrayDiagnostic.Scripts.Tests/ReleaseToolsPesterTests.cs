using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.Scripts.Tests;

/// <summary>
/// Runs the Pester tests of the release scripts (scripts/ReleaseTools/ReleaseTools.Tests.ps1, T-055) as part of
/// dotnet test. Skipped when pwsh or Pester 5+ is not installed. The Pester run uses the reserved
/// <see cref="TestDirectories.Root"/> as its temp root, so nothing is left behind even when pwsh is killed.
/// </summary>
public sealed class ReleaseToolsPesterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private const string Command =
        "$ErrorActionPreference = 'Stop'; " +
        "Import-Module Pester -MinimumVersion 5.0.0; " +
        "$c = New-PesterConfiguration; " +
        "$c.Run.Path = $env:PTD_RELEASE_TESTS_PATH; " +
        "$c.Run.PassThru = $true; " +
        "$c.Output.Verbosity = 'Detailed'; " +
        "if ($c.Output.PSObject.Properties['RenderMode']) { $c.Output.RenderMode = 'Plaintext' }; " +
        "$r = Invoke-Pester -Configuration $c; " +
        "Write-Output \"PESTER RESULT: $($r.Result) passed=$($r.PassedCount) failed=$($r.FailedCount) skipped=$($r.SkippedCount)\"; " +
        "if ($r.Result -ne 'Passed' -or $r.FailedCount -gt 0 -or $r.PassedCount -eq 0) { exit 1 }; exit 0";

    [Fact]
    public async Task ReleaseScriptPesterTestsPass()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string? skipReason = await PowerShell.PesterSkipReasonAsync(cancellationToken);
        if (skipReason is not null)
        {
            Assert.Skip(skipReason);
        }

        string tests = Path.Combine(TestWorktree.Root, "scripts", "ReleaseTools", "ReleaseTools.Tests.ps1");
        Assert.True(File.Exists(tests), $"Pester tests not found: {tests}");
        string temp = Path.Combine(TestDirectories.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            PowerShell.Result result = await PowerShell.RunAsync(
                Command,
                new Dictionary<string, string> { ["PTD_RELEASE_TESTS_PATH"] = tests, ["PTD_RELEASE_TESTS_TEMP"] = temp },
                Timeout,
                cancellationToken);

            Assert.True(result.ExitCode == 0, $"Pester failed (exit code {result.ExitCode}):{Environment.NewLine}{result.Output}{Environment.NewLine}{result.Error}");
            Assert.Contains("PESTER RESULT: Passed", result.Output, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }
}
