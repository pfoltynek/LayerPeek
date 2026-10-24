using System.Diagnostics;
using System.Text.Json;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Tests.Shared;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// Unit tests of the selection of <c>tools/run-e2e.ps1</c> (T-066): the warning lists only the kinds of system change the
/// selected tests make (trait <c>SystemChanges=&lt;kind&gt;</c>). They run the functions of
/// <c>tools/RunE2E.SystemChanges.psm1</c> in PowerShell 7 with made-up test lists; nothing else runs and nothing of the
/// system changes. Without <c>pwsh</c> on PATH they are skipped (the end-to-end run needs it anyway).
/// </summary>
public sealed class RunE2EScriptTests
{
    private static readonly string Module = Path.Combine(TestWorktree.Root, "tools", "RunE2E.SystemChanges.psm1");

    private static readonly string ResultsModule = Path.Combine(TestWorktree.Root, "tools", "RunE2E.Results.psm1");

    [Fact]
    public async Task ScriptKnowsExactlyTheTraitValuesOfTheTests()
    {
        string[] names = await RunJsonAsync("Get-SystemChangeKindNames");

        Assert.Equal(SystemChangeTraits.All, names);
    }

    [Fact]
    public async Task SelectionHasOnlyTheKindsOfTheListedTestsInWarningOrder()
    {
        string list = TestList(
            ("Display.Scale", [SystemChangeTraits.DisplayScale]),
            ("Mixed.ThemeAndMonitor", [SystemChangeTraits.MonitorOff, SystemChangeTraits.Theme]),
            ("Again.Scale", [SystemChangeTraits.DisplayScale]));

        string[] kinds = await SelectAsync("xUnit.net v3 In-Process Runner", list);

        Assert.Equal([SystemChangeTraits.Theme, SystemChangeTraits.DisplayScale, SystemChangeTraits.MonitorOff], kinds);
    }

    [Fact]
    public async Task TestsWithoutTheTraitAddNothing()
    {
        string list = """[{"DisplayName":"Plain.Test","Traits":{"Category":["Slow"]}},{"DisplayName":"No.Traits","Traits":{}},{"DisplayName":"Null.Traits"}]""";

        Assert.Empty(await SelectAsync(list));
    }

    [Fact]
    public async Task NoSelectedTestMeansNoWarning()
    {
        Assert.Empty(await SelectAsync("[]"));
        Assert.Empty(await SelectAsync("xUnit.net v3 In-Process Runner v4.0.2", string.Empty));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("theme")]
    [InlineData("Brightness")]
    public async Task UnknownKindStopsTheRunInsteadOfAnIncompleteWarning(string value)
    {
        (int exitCode, string output) = await RunAsync($"Get-SelectedSystemChanges -ListOutput (Get-Content -LiteralPath '{WriteInput(TestList(("Some.Test", [value])))}')");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("unknown kind", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"SystemChanges={value}", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WarningListsOnlyTheSelectedKindsWithTheirManualRestore()
    {
        string[] lines = await RunJsonAsync($"Get-SystemChangesWarningLines -Kinds '{SystemChangeTraits.MonitorOff}'");
        string text = string.Join('\n', lines);

        Assert.Contains("CHANGE WINDOWS SETTINGS", text, StringComparison.Ordinal);
        Assert.Contains("monitor switched off", text, StringComparison.Ordinal);
        Assert.Contains("Win+P > Extend", text, StringComparison.Ordinal);
        Assert.Contains("tools/run-e2e.ps1 -RestoreOnly", text, StringComparison.Ordinal);
        Assert.Contains("Do not use the mouse or keyboard", text, StringComparison.Ordinal);
        Assert.DoesNotContain("display scale", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Personalization > Colors", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Do Not Disturb", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Credential Manager", text, StringComparison.Ordinal);
        Assert.DoesNotContain("toasts", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WarningOfEveryKindNamesEachOnceInWarningOrder()
    {
        string[] lines = await RunJsonAsync($"Get-SystemChangesWarningLines -Kinds @({string.Join(", ", SystemChangeTraits.All.Reverse().Select(static kind => $"'{kind}'"))})");
        string[] items = [.. lines.Where(static line => line.StartsWith("   - ", StringComparison.Ordinal) && !line.Contains("RestoreOnly", StringComparison.Ordinal) && !line.Contains("by hand", StringComparison.Ordinal) && !line.Contains("details", StringComparison.Ordinal))];

        Assert.Equal(SystemChangeTraits.All.Count, items.Length);
        Assert.Contains("light/dark", items[0], StringComparison.Ordinal);
        Assert.Contains("display scale", items[3], StringComparison.Ordinal);
        Assert.Contains("switched off", items[4], StringComparison.Ordinal);
        Assert.Contains("Credential Manager", items[6], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("@()")]
    [InlineData("'Brightness'")]
    public async Task WarningWithoutAKnownKindIsRefused(string kinds)
    {
        (int exitCode, _) = await RunAsync($"Get-SystemChangesWarningLines -Kinds {kinds}");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void ScriptUsesTheSelectionAndConfirmsOnlyTheListedKinds()
    {
        string script = File.ReadAllText(Path.Combine(TestWorktree.Root, "tools", "run-e2e.ps1"));

        Assert.Contains("RunE2E.SystemChanges.psm1", script, StringComparison.Ordinal);
        Assert.Contains("-list full/json -trait 'SystemChanges=*' @filters", script, StringComparison.Ordinal);
        Assert.Contains("Get-SelectedSystemChanges", script, StringComparison.Ordinal);
        Assert.Contains("Show-SystemChangesWarning -Kinds $systemChanges", script, StringComparison.Ordinal);
        Assert.Contains($"$env:{SystemChanges.KindsVariable} = $systemChanges -join ','", script, StringComparison.Ordinal);
        // The kinds are confirmed only after the countdown, and nothing inherited confirms them.
        Assert.True(
            script.IndexOf("Show-SystemChangesWarning -Kinds", StringComparison.Ordinal) < script.IndexOf($"$env:{SystemChanges.KindsVariable} =", StringComparison.Ordinal));
        Assert.Contains($"Env:{SystemChanges.KindsVariable}", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyJournalIsIgnoredWhenMissingAndRemovedWhenNothingIsPending()
    {
        // T-074: the journal folder of the app's former name, under a made-up LocalAppData.
        string localAppData = UnitTestDirectories.Create();
        string legacy = Path.Combine(localAppData, LegacyJournalFolderName);

        Assert.Empty(await RunJsonAsync($"Resolve-LegacyRestoreJournal -LocalAppData '{localAppData}'"));

        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "not-a-change.tmp"), string.Empty);
        Assert.Empty(await RunJsonAsync($"Resolve-LegacyRestoreJournal -LocalAppData '{localAppData}'"));
        Assert.False(Directory.Exists(legacy));
    }

    [Fact]
    public async Task PendingLegacyJournalStopsTheRunAndStaysForTheCodeOfTheFormerName()
    {
        string localAppData = UnitTestDirectories.Create();
        string legacy = Directory.CreateDirectory(Path.Combine(localAppData, LegacyJournalFolderName)).FullName;
        string entry = Path.Combine(legacy, "change.json");
        File.WriteAllText(entry, "{}");

        string[] lines = await RunJsonAsync($"Resolve-LegacyRestoreJournal -LocalAppData '{localAppData}'");

        Assert.Contains(lines, line => line.Contains(legacy, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("run-e2e.ps1 -RestoreOnly", StringComparison.Ordinal));
        Assert.True(File.Exists(entry));
        string script = File.ReadAllText(Path.Combine(TestWorktree.Root, "tools", "run-e2e.ps1"));
        // Checked before any restore or run of the new code.
        Assert.True(
            script.IndexOf("Resolve-LegacyRestoreJournal", StringComparison.Ordinal) is int check and > 0
            && check < script.IndexOf("if ($RestoreOnly) {", StringComparison.Ordinal));
    }

    /// <summary>The journal folder before the rename to LayerPeek (T-074).</summary>
    private const string LegacyJournalFolderName = "PrusaTrayDiagnostic.E2E.Restore";

    [Fact]
    public async Task SummaryOfAPassCountsPassedSkippedAndFailedTests()
    {
        // The xUnit.net v2 XML report of the runner (-result-xml); errors outside tests (e.g. the run's cleanup) count as failed.
        string report = WriteInput("""
            <?xml version="1.0" encoding="utf-8"?>
            <assemblies timestamp="10/23/2026 20:00:00">
              <assembly name="LayerPeek.App.E2E.Tests.dll" total="10" passed="7" failed="1" skipped="2" not-run="0" errors="1" time="12.5" />
            </assemblies>
            """);

        string[] lines = await RunJsonAsync(
            $"Format-TestPassSummary -Name 'Debug exe' -ExitCode 1 -Counts (Get-TestPassCounts -Path '{report}'); "
            + $"Format-TestPassSummary -Name 'Native AOT exe (Debug)' -ExitCode 0 -Counts (Get-TestPassCounts -Path '{report}')");

        Assert.Equal(
            [
                "Debug exe: FAILED (exit code 1) - 7 passed, 2 skipped, 2 failed (of 10)",
                "Native AOT exe (Debug): green - 7 passed, 2 skipped, 2 failed (of 10)",
            ],
            lines);
    }

    [Fact]
    public async Task SummaryOfAPassWithoutReportOrRunSaysSo()
    {
        string missing = Path.Combine(UnitTestDirectories.Create(), "missing.xml");

        string[] lines = await RunJsonAsync(
            $"Format-TestPassSummary -Name 'Debug exe' -ExitCode 3 -Counts (Get-TestPassCounts -Path '{missing}'); "
            + "Format-TestPassSummary -Name 'Native AOT exe (Debug)' -ExitCode $null -Counts $null -NotRunReason 'not run (the Debug pass failed)'");

        Assert.Equal(
            [
                "Debug exe: FAILED (exit code 3), no test report (the test process ended without one)",
                "Native AOT exe (Debug): not run (the Debug pass failed)",
            ],
            lines);
    }

    [Fact]
    public void ScriptRunsTheWholeSelectionAgainstBothExesWithOneWarning()
    {
        string script = File.ReadAllText(Path.Combine(TestWorktree.Root, "tools", "run-e2e.ps1"));

        // T-073: both passes run the same selection (-Class / -Method), the second against the Debug Native AOT exe.
        Assert.Contains("RunE2E.Results.psm1", script, StringComparison.Ordinal);
        Assert.Contains("@('run', '--project', $tests, '--no-build', '--') + $filters + @('-result-xml', $report)", script, StringComparison.Ordinal);
        // Not through a pipe: a test app left by a killed test process would hang the script before its restore.
        Assert.Contains("$process.WaitForExit()", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-TestPass -Name 'Debug exe' -Results $results", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-TestPass -Name 'Native AOT exe (Debug)' -Results $resultsAot -App (Join-Path $aotDebug 'LayerPeek.exe')", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-method '*LogHasRecordsAndAnExceptionWithItsStackTrace*'", script, StringComparison.Ordinal);
        // The warning is shown once, before the first pass.
        Assert.Equal(1, CountOf(script, "Show-SystemChangesWarning -Kinds"));
        Assert.True(
            script.IndexOf("Show-SystemChangesWarning -Kinds $systemChanges", StringComparison.Ordinal) < script.IndexOf("Invoke-TestPass -Name 'Debug exe'", StringComparison.Ordinal));
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string TestList(params (string Name, string[] Kinds)[] tests) =>
        JsonSerializer.Serialize(tests.Select(static test => new ListedTest(test.Name, new Dictionary<string, string[]> { [SystemChanges.Trait] = test.Kinds, ["Other"] = ["x"] })).ToArray(), ScriptTestJson.Default.ListedTestArray);

    private static async Task<string[]> SelectAsync(params string[] lines) =>
        await RunJsonAsync($"Get-SelectedSystemChanges -ListOutput (Get-Content -LiteralPath '{WriteInput(string.Join(Environment.NewLine, lines))}')");

    private static string WriteInput(string text)
    {
        string file = Path.Combine(UnitTestDirectories.Create(), "list.txt");
        File.WriteAllText(file, text);
        return file;
    }

    /// <summary>Runs <paramref name="command"/> after importing the module; its output as a JSON array of strings.</summary>
    private static async Task<string[]> RunJsonAsync(string command)
    {
        (int exitCode, string output) = await RunAsync($"ConvertTo-Json -InputObject @({command}) -Compress");
        Assert.True(exitCode == 0, output);
        return JsonSerializer.Deserialize(output.Trim(), ScriptTestJson.Default.StringArray) ?? [];
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string command)
    {
        string? pwsh = SystemChangeWatchdog.FindOnPath("pwsh.exe");
        Assert.SkipWhen(pwsh is null, "PowerShell 7 (pwsh.exe) is not on PATH.");
        string script = Path.Combine(UnitTestDirectories.Create(), "test.ps1");
        await File.WriteAllTextAsync(
            script,
            $"$ErrorActionPreference = 'Stop'{Environment.NewLine}Import-Module -Force -Name '{Module}'{Environment.NewLine}Import-Module -Force -Name '{ResultsModule}'{Environment.NewLine}{command}{Environment.NewLine}",
            TestContext.Current.CancellationToken);

        ProcessStartInfo start = new(pwsh!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-File", script })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        }
        finally
        {
            // Nothing stays behind, also after a timeout or a cancelled run.
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        return (process.ExitCode, (await output) + (await error));
    }
}

/// <summary>A test as <c>-list full/json</c> shows it (the parts the selection reads).</summary>
internal sealed record ListedTest(string DisplayName, Dictionary<string, string[]> Traits);

[System.Text.Json.Serialization.JsonSerializable(typeof(ListedTest[]))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string[]))]
internal sealed partial class ScriptTestJson : System.Text.Json.Serialization.JsonSerializerContext;
