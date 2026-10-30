using LayerPeek.Tests.Shared;

namespace LayerPeek.Scripts.Tests;

/// <summary>
/// Runs the Pester tests of the scripts as part of dotnet test: the release scripts
/// (scripts/ReleaseTools/ReleaseTools.Tests.ps1 and ReleaseOrchestrator.Tests.ps1 of scripts/release.ps1, T-055) and the
/// one-time migration from the former name (tools/migrate-from-prusatraydiagnostic.Tests.ps1, T-074) and the before/after
/// check of the user profile (tools/profile-snapshot.Tests.ps1, T-095) and the maintainer tool for a new FFmpeg version on
/// macOS and Windows (build/update-ffmpeg.Tests.ps1, T-026) and the engine of the user-run verification scripts
/// (tools/VerifyTools.Tests.ps1, T-105). Skipped when pwsh or Pester 5+ is not installed. The
/// Pester run uses the reserved <see cref="TestDirectories.Root"/> as its temp root (also TEMP / TMP / TMPDIR of the
/// child pwsh), so nothing is left behind even when pwsh is killed.
/// </summary>
public sealed class PesterScriptTests
{
    private const string Command =
        "$ErrorActionPreference = 'Stop'; " +
        "Import-Module Pester -MinimumVersion 5.0.0; " +
        "$c = New-PesterConfiguration; " +
        "$c.Run.Path = $env:LAYERPEEK_PESTER_TESTS_PATH; " +
        "$c.Run.PassThru = $true; " +
        "$c.Output.Verbosity = 'Detailed'; " +
        "if ($c.Output.PSObject.Properties['RenderMode']) { $c.Output.RenderMode = 'Plaintext' }; " +
        "$r = Invoke-Pester -Configuration $c; " +
        "Write-Output \"PESTER RESULT: $($r.Result) passed=$($r.PassedCount) failed=$($r.FailedCount) skipped=$($r.SkippedCount)\"; " +
        "if ($r.Result -ne 'Passed' -or $r.FailedCount -gt 0 -or $r.PassedCount -eq 0) { exit 1 }; exit 0";

    // Time limit per test file in minutes: the orchestrator tests run release.ps1 end to end many times (about 1.5-2.5 min).
    [Theory]
    [InlineData("scripts/ReleaseTools/ReleaseTools.Tests.ps1", 5)]
    [InlineData("scripts/ReleaseTools/ReleaseOrchestrator.Tests.ps1", 15)]
    [InlineData("tools/migrate-from-prusatraydiagnostic.Tests.ps1", 5)]
    [InlineData("tools/profile-snapshot.Tests.ps1", 5)]
    [InlineData("build/update-ffmpeg.Tests.ps1", 5)]
    [InlineData("tools/VerifyTools.Tests.ps1", 5)]
    public async Task ScriptPesterTestsPass(string relativePath, int timeoutMinutes)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string? skipReason = await PowerShell.PesterSkipReasonAsync(cancellationToken);
        if (skipReason is not null)
        {
            Assert.Skip(skipReason);
        }

        string tests = Path.Combine(TestWorktree.Root, relativePath);
        Assert.True(File.Exists(tests), $"Pester tests not found: {tests}");
        // Everything the child pwsh writes goes under the reserved folder: the Pester temp root and, through TEMP / TMP /
        // TMPDIR, the system temp folder of the scripts (smoke logs, tar file lists). A killed run leaves nothing in the
        // user's %TEMP%; the reserved folder is swept by the next run.
        // Short names: the git repositories of the Pester tests live deep below this folder (Windows path length limit).
        string root = Path.Combine(TestDirectories.Root, Guid.NewGuid().ToString("N")[..8]);
        string temp = Path.Combine(root, "p");
        string processTemp = Path.Combine(root, "t");
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(processTemp);
        try
        {
            PowerShell.Result result = await PowerShell.RunAsync(
                Command,
                new Dictionary<string, string>
                {
                    ["LAYERPEEK_PESTER_TESTS_PATH"] = tests,
                    ["LAYERPEEK_PESTER_TESTS_TEMP"] = temp,
                    ["TEMP"] = processTemp,
                    ["TMP"] = processTemp,
                    ["TMPDIR"] = processTemp,
                },
                TimeSpan.FromMinutes(timeoutMinutes),
                cancellationToken);

            Assert.True(result.ExitCode == 0, $"Pester failed (exit code {result.ExitCode}):{Environment.NewLine}{result.Output}{Environment.NewLine}{result.Error}");
            Assert.Contains("PESTER RESULT: Passed", result.Output, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
            // pwsh itself may keep files of its own in TEMP; the scripts' temporary files must all be gone.
            Assert.Empty(Directory.EnumerateFileSystemEntries(processTemp, "layerpeek-*"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
