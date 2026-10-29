using LayerPeek.Tests.Shared;

namespace LayerPeek.Scripts.Tests;

/// <summary>
/// The user-run verification scripts tools/macos-verify.ps1 and tools/windows-verify.ps1 (T-105) in their unattended
/// mode, as part of dotnet test: <c>-Unattended -ListChecks</c> and <c>-Unattended -SelfTest</c> (the engine with stand-in
/// checks and a stand-in prompt; nothing of the system or the user profile is touched). Needs only pwsh (no Pester), so
/// it also runs where the Pester tests of <see cref="PesterScriptTests"/> are skipped.
/// <para>
/// On demand (<c>LAYERPEEK_VERIFY_UNATTENDED=&lt;check ids&gt;</c>, e.g. <c>mac.plist-localnetwork,mac.dock</c>): the real
/// unattended checks of the script of this OS, which never ask and never request a permission; their report goes to
/// artifacts/verify/ of the worktree as in a manual run.
/// </para>
/// </summary>
public sealed class VerifyScriptTests
{
    /// <summary>Check ids for <see cref="UnattendedChecksPassOnDemand"/>; unset = skipped.</summary>
    public const string OnDemandVariable = "LAYERPEEK_VERIFY_UNATTENDED";

    [Theory]
    [InlineData("tools/macos-verify.ps1", "mac.")]
    [InlineData("tools/windows-verify.ps1", "win.")]
    public async Task ListChecksNamesTheCriteriaOfEveryCheck(string script, string prefix)
    {
        PowerShell.Result result = await RunAsync(script, "-Unattended -ListChecks", TimeSpan.FromMinutes(2));

        Assert.True(result.ExitCode == 0, Describe(result));
        string[] lines = result.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string[] ids = [.. lines.Where(static line => line.Contains("  [", StringComparison.Ordinal)).Select(static line => line[..line.IndexOf(' ', StringComparison.Ordinal)])];
        Assert.True(ids.Length > 5, Describe(result));
        Assert.All(ids, id => Assert.StartsWith(prefix, id, StringComparison.Ordinal));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.True(lines.Count(static line => line.StartsWith("closes: T-", StringComparison.Ordinal)) >= ids.Length, Describe(result));
    }

    [Theory]
    [InlineData("tools/macos-verify.ps1")]
    [InlineData("tools/windows-verify.ps1")]
    public async Task SelfTestPassesWithoutLeavingFiles(string script)
    {
        string temp = Path.Combine(TestDirectories.Root, "v" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        try
        {
            PowerShell.Result result = await RunAsync(script, "-Unattended -SelfTest", TimeSpan.FromMinutes(3), temp);

            Assert.True(result.ExitCode == 0, Describe(result));
            Assert.Contains("SELF-TEST PASSED", result.Output, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(temp, "layerpeek-*"));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task UnattendedChecksPassOnDemand()
    {
        string? only = Environment.GetEnvironmentVariable(OnDemandVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(only), $"On demand only: {OnDemandVariable}=<check ids> runs those checks of the verification script of this OS with -Unattended.");
        Assert.SkipUnless(OperatingSystem.IsMacOS() || OperatingSystem.IsWindows(), "The verification scripts run on macOS and Windows.");
        string script = OperatingSystem.IsMacOS() ? "tools/macos-verify.ps1" : "tools/windows-verify.ps1";

        PowerShell.Result result = await RunAsync(script, $"-Unattended -Only '{only!.Replace("'", string.Empty, StringComparison.Ordinal)}'", TimeSpan.FromMinutes(60));

        TestContext.Current.SendDiagnosticMessage(result.Output);
        Assert.True(result.ExitCode == 0, Describe(result));
    }

    private static async Task<PowerShell.Result> RunAsync(string script, string arguments, TimeSpan timeout, string? temp = null)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Assert.SkipUnless(PowerShell.IsAvailable, $"PowerShell 7 ({PowerShell.Executable}) is not on PATH; see docs/prerequisites.md.");
        string path = Path.Combine(TestWorktree.Root, script);
        Assert.True(File.Exists(path), $"Script not found: {path}");
        Dictionary<string, string> environment = [];
        if (temp is not null)
        {
            environment["TEMP"] = temp;
            environment["TMP"] = temp;
            environment["TMPDIR"] = temp;
        }

        return await PowerShell.RunAsync($"& '{path.Replace("'", "''", StringComparison.Ordinal)}' {arguments}; exit $LASTEXITCODE", environment, timeout, cancellationToken);
    }

    private static string Describe(PowerShell.Result result) =>
        $"exit code {result.ExitCode}:{Environment.NewLine}{result.Output}{Environment.NewLine}{result.Error}";
}
