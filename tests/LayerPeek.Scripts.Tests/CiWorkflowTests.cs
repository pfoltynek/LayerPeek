using System.Text.RegularExpressions;

using LayerPeek.Tests.Shared;

namespace LayerPeek.Scripts.Tests;

/// <summary>
/// The security rules of the GitHub Actions workflow (T-218) that a later edit could quietly break: actions pinned to a
/// commit with their version as a comment, a read-only token, no secrets, no <c>pull_request_target</c>, no persisted
/// checkout credentials, a timeout on every job and superseded runs cancelled. Line-based (no YAML parser): the file is
/// kept in the simple block style these checks expect.
/// </summary>
public sealed partial class CiWorkflowTests
{
    private static readonly string WorkflowPath = Path.Combine(TestWorktree.Root, ".github", "workflows", "ci.yml");

    private static string[] Lines() => File.ReadAllLines(WorkflowPath);

    [Fact]
    public void EveryActionIsPinnedToACommitWithItsVersionAsComment()
    {
        string[] uses = [.. Lines().Where(static line => UsesLine().IsMatch(line))];

        Assert.NotEmpty(uses);
        Assert.All(uses, static line => Assert.Matches(PinnedUses(), line));
    }

    [Fact]
    public void TheTokenIsReadOnlyAndNoSecretOrPrivilegedTriggerIsUsed()
    {
        // Without the comment lines, which may name what is not used.
        string text = string.Join('\n', Lines().Where(static line => !line.TrimStart().StartsWith('#'))) + "\n";

        Assert.Contains("\npermissions:\n  contents: read\n", text, StringComparison.Ordinal);
        // Only the workflow-level block: a job must not widen the token.
        Assert.Single(Lines(), static line => line.TrimStart().StartsWith("permissions:", StringComparison.Ordinal));
        Assert.DoesNotContain("secrets.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request_target", text, StringComparison.Ordinal);
        Assert.DoesNotContain("workflow_run", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCheckoutDropsItsCredentials()
    {
        string[] lines = Lines();
        int[] checkouts = [.. Enumerable.Range(0, lines.Length).Where(i => lines[i].Contains("uses: actions/checkout@", StringComparison.Ordinal))];

        Assert.NotEmpty(checkouts);
        Assert.All(checkouts, i => Assert.Contains(
            lines.Skip(i + 1).TakeWhile(static line => !UsesLine().IsMatch(line) && !line.TrimStart().StartsWith("- ", StringComparison.Ordinal)),
            static line => line.Trim() == "persist-credentials: false"));
    }

    [Fact]
    public void EveryJobHasATimeout()
    {
        string[] lines = Lines();
        int jobs = Array.IndexOf(lines, "jobs:");
        Assert.True(jobs >= 0, "No top-level jobs: block.");
        int[] starts = [.. Enumerable.Range(jobs + 1, lines.Length - jobs - 1).Where(i => JobLine().IsMatch(lines[i]))];

        Assert.True(starts.Length >= 3, $"Expected the jobs changes, build and result, found {starts.Length}.");
        for (int j = 0; j < starts.Length; j++)
        {
            int end = j + 1 < starts.Length ? starts[j + 1] : lines.Length;
            Assert.True(
                lines[starts[j]..end].Any(static line => TimeoutLine().IsMatch(line)),
                $"Job '{lines[starts[j]].Trim()}' has no timeout-minutes of its own.");
        }
    }

    /// <summary>
    /// T-218: a push run is never cancelled and never takes the docs-only shortcut. A docs-only push once cancelled the run
    /// of a code push and went green without testing that code; only a pull request (whose merge commit diff is the whole
    /// pull request) may cancel its older run and skip the build.
    /// </summary>
    [Fact]
    public void OnlyPullRequestsCancelOlderRunsAndSkipTheBuild()
    {
        string[] lines = Lines();
        string[] code = [.. lines.Where(static line => !line.TrimStart().StartsWith('#'))];

        Assert.Single(lines, static line => line.Trim().StartsWith("cancel-in-progress:", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.Trim() == "cancel-in-progress: ${{ github.event_name == 'pull_request' }}");
        // The classification never looks at the push's own diff.
        Assert.DoesNotContain(code, static line => line.Contains("event.before", StringComparison.Ordinal) || line.Contains("BEFORE", StringComparison.Ordinal));
        Assert.Single(code, static line => line.Contains("files=$(git diff", StringComparison.Ordinal));
        int diff = Array.FindIndex(code, static line => line.Contains("files=$(git diff --name-only HEAD^1 HEAD)", StringComparison.Ordinal));
        Assert.True(diff > 0 && code[diff - 1].Trim() == "if [ \"$EVENT\" = pull_request ]; then", "The changed files are read for pull requests only.");
    }

    [Fact]
    public void TheWindowsJobInstallsTheFFmpegToolchainBeforeTheFirstBuild()
    {
        // T-202: build/FFmpeg.targets compiles FFmpeg on a cache miss with MSYS2 + MinGW-w64 and never installs anything
        // itself, so the job installs exactly the packages the build names in its error message, before the first build,
        // and points the build to the installation.
        string[] lines = Lines();
        string targets = File.ReadAllText(Path.Combine(TestWorktree.Root, "build", "FFmpeg.targets"));
        Match packages = Msys2Packages().Match(targets);
        Assert.True(packages.Success, "build/FFmpeg.targets names no _LayerPeekFFmpegMsys2Packages for win-x64.");

        int install = Array.FindIndex(lines, static line => line.Contains("uses: msys2/setup-msys2@", StringComparison.Ordinal));
        int build = Array.FindIndex(lines, static line => line.Trim() == "- name: Build (Release)");
        int restore = Array.FindIndex(lines, static line => line.Trim() == "id: ffmpeg");
        Assert.True(install > restore && restore >= 0, "MSYS2 must be installed after the FFmpeg cache is restored (only on a miss).");
        Assert.True(build > install, "MSYS2 must be installed before the first build.");

        string[] step = [.. lines.Skip(install - 3).Take(12).Select(static line => line.Trim())];
        Assert.Contains("if: matrix.rid == 'win-x64' && steps.ffmpeg.outputs.cache-hit != 'true'", step);
        Assert.Contains("msystem: MINGW64", step);
        Assert.Contains("update: false", step);
        Assert.Contains($"install: {packages.Groups[1].Value}", step);
        Assert.Contains(lines, static line => line.Contains("\"LayerPeekMsys2Root=$env:MSYS2_LOCATION\" >> $env:GITHUB_ENV", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.Trim() == "ffmpeg-script: build/windows/build-ffmpeg.sh");
    }

    [Fact]
    public void DependabotKeepsThePinnedActionsCurrent()
    {
        string dependabot = File.ReadAllText(Path.Combine(TestWorktree.Root, ".github", "dependabot.yml"));

        Assert.Contains("package-ecosystem: github-actions", dependabot, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"<_LayerPeekFFmpegMsys2Packages Condition=""'\$\(_LayerPeekFFmpegRid\)' == 'win-x64'"">([^<]+)</_LayerPeekFFmpegMsys2Packages>")]
    private static partial Regex Msys2Packages();

    [GeneratedRegex(@"^\s*(-\s+)?uses:")]
    private static partial Regex UsesLine();

    // owner/repo[/path]@<40 hex> # vMAJOR[.MINOR[.PATCH]]
    [GeneratedRegex(@"^\s*(-\s+)?uses: [A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(/[A-Za-z0-9_./-]+)?@[0-9a-f]{40} # v\d+(\.\d+){0,2}$")]
    private static partial Regex PinnedUses();

    [GeneratedRegex(@"^  [A-Za-z0-9_-]+:$")]
    private static partial Regex JobLine();

    [GeneratedRegex(@"^    timeout-minutes: \S+")]
    private static partial Regex TimeoutLine();
}
