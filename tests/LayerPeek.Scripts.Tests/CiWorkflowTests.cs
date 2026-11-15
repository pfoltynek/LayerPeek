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
    public void EveryJobHasATimeoutAndSupersededRunsAreCancelled()
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

        Assert.Contains(lines, static line => line.Trim() == "cancel-in-progress: true");
    }

    [Fact]
    public void DependabotKeepsThePinnedActionsCurrent()
    {
        string dependabot = File.ReadAllText(Path.Combine(TestWorktree.Root, ".github", "dependabot.yml"));

        Assert.Contains("package-ecosystem: github-actions", dependabot, StringComparison.Ordinal);
    }

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
