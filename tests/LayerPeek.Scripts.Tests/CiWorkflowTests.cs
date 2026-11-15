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

        Assert.True(starts.Length >= 4, $"Expected the jobs changes, build, cross-arm64 and result, found {starts.Length}.");
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

    /// <summary>
    /// T-236: the win-arm64 Native AOT publish is cross-compiled on the x64 Windows runner (it cannot run there) and every
    /// PE file of the publish folder is checked to be ARM64 (Get-PublishArchitectureCheck, T-224), together with the
    /// camera libraries, so a broken ARM64 toolchain, FFmpeg cross build or native package shows up before a release.
    /// </summary>
    [Fact]
    public void TheArm64JobCrossPublishesAndChecksTheArchitecture()
    {
        string[] job = JobLines("cross-arm64");
        string[] trimmed = [.. job.Select(static line => line.Trim())];

        Assert.Contains("runs-on: windows-2025", trimmed);
        Assert.Contains("needs: changes", trimmed);
        Assert.Contains("if: needs.changes.outputs.code == 'true'", trimmed);
        Assert.Contains("shell: pwsh", trimmed);

        int toolchain = Array.FindIndex(trimmed, static line => line.Contains("Get-NativeToolchainProblems -Runtimes win-arm64 -HostOs windows", StringComparison.Ordinal));
        int build = Array.FindIndex(trimmed, static line => line == "run: dotnet build src/LayerPeek.App -c Release -r win-arm64");
        int publish = Array.FindIndex(trimmed, static line => line == "run: dotnet publish src/LayerPeek.App -c Release -r win-arm64 -o $env:ARM64_PUBLISH_DIR");
        int check = Array.FindIndex(trimmed, static line => line.Contains("Get-PublishArchitectureCheck -Directory $env:ARM64_PUBLISH_DIR -Runtime win-arm64", StringComparison.Ordinal));
        Assert.True(toolchain >= 0, "The job checks the MSVC ARM64 build tools (fails in seconds instead of after the FFmpeg build).");
        Assert.True(build > toolchain, "The toolchain is checked before the build.");
        Assert.True(publish > build, "The app is published after the build that fills the caches.");
        Assert.True(check > publish, "The architecture is checked after the publish.");

        // The check fails the step, and the camera libraries must be in the publish folder (an FFmpeg build that was
        // switched off would pass the PE check).
        string[] after = trimmed[check..];
        Assert.Contains(after, static line => line.StartsWith("if ($check.Problems.Count -gt 0) { throw", StringComparison.Ordinal));
        Assert.Contains(after, static line => line.Contains("FFmpeg-LICENSE.txt", StringComparison.Ordinal));
        Assert.Contains(after, static line => line.Contains("'avformat-*.dll', 'avcodec-*.dll', 'avutil-*.dll', 'swscale-*.dll'", StringComparison.Ordinal));
        Assert.Contains(job, static line => line.Contains("\"ARM64_PUBLISH_DIR=$(Join-Path $env:RUNNER_TEMP publish-win-arm64)\" >> $env:GITHUB_ENV", StringComparison.Ordinal));

        // The application is never started: an x64 runner cannot run ARM64 binaries.
        Assert.DoesNotContain(trimmed, static line => line.Contains("aot-smoke", StringComparison.Ordinal) || line.Contains("LayerPeek.exe", StringComparison.Ordinal));
    }

    /// <summary>
    /// T-236: a warm run downloads and compiles nothing. The ARM64 FFmpeg cache has its own key and path (only the
    /// win-arm64 folder of the cache root, never llvm-mingw); llvm-mingw (~570 MB extracted) is restored only when the
    /// FFmpeg cache misses (the build needs it only then), keyed by its pinned SHA-256 in build/FFmpeg.targets; MSYS2 is
    /// installed only on an FFmpeg miss, with exactly the packages build/FFmpeg.targets names for win-arm64.
    /// </summary>
    [Fact]
    public void TheArm64JobCachesFFmpegAndLlvmMingw()
    {
        string[] job = JobLines("cross-arm64");
        string[] trimmed = [.. job.Select(static line => line.Trim())];
        string targets = File.ReadAllText(Path.Combine(TestWorktree.Root, "build", "FFmpeg.targets"));

        Assert.Contains("\"LayerPeekFFmpegCacheRoot=$(Join-Path $env:RUNNER_TEMP ffmpeg)\" >> $env:GITHUB_ENV", trimmed);

        // FFmpeg: exact key from the inputs of the cache folder name (FFmpeg.targets holds the source and llvm-mingw pins).
        int ffmpeg = Array.IndexOf(trimmed, "id: ffmpeg");
        int build = Array.FindIndex(trimmed, static line => line == "run: dotnet build src/LayerPeek.App -c Release -r win-arm64");
        Assert.True(ffmpeg >= 0 && build > ffmpeg, "The ARM64 FFmpeg cache is restored before the build.");
        string[] ffmpegStep = StepAround(trimmed, ffmpeg);
        Assert.Contains(ffmpegStep, static line => line.StartsWith("uses: actions/cache/restore@", StringComparison.Ordinal));
        Assert.Contains("path: ${{ runner.temp }}/ffmpeg/win-arm64-*", ffmpegStep);
        Assert.Contains("key: ffmpeg-v1-windows-2025-win-arm64-${{ hashFiles('build/FFmpeg.targets', 'build/windows/build-ffmpeg.sh') }}", ffmpegStep);
        Assert.DoesNotContain(ffmpegStep, static line => line.StartsWith("restore-keys:", StringComparison.Ordinal));

        // llvm-mingw: pin read from FFmpeg.targets, restored only on an FFmpeg miss.
        Assert.Matches(@"<LayerPeekLlvmMingwSha256>[0-9a-f]{64}</LayerPeekLlvmMingwSha256>", targets);
        Assert.Matches(@"<LayerPeekLlvmMingwFolder>[A-Za-z0-9._-]+</LayerPeekLlvmMingwFolder>", targets);
        Assert.Contains(trimmed, static line => line.Contains("'<LayerPeekLlvmMingwSha256>([0-9a-f]{64})</LayerPeekLlvmMingwSha256>'", StringComparison.Ordinal));
        Assert.Contains(trimmed, static line => line.Contains("'<LayerPeekLlvmMingwFolder>([A-Za-z0-9._-]+)</LayerPeekLlvmMingwFolder>'", StringComparison.Ordinal));
        int llvm = Array.IndexOf(trimmed, "id: llvm-mingw");
        Assert.True(llvm > ffmpeg && build > llvm, "llvm-mingw is restored after the FFmpeg cache (only on its miss) and before the build.");
        string[] llvmStep = StepAround(trimmed, llvm);
        Assert.Contains("if: steps.ffmpeg.outputs.cache-hit != 'true'", llvmStep);
        Assert.Contains("path: ${{ runner.temp }}/ffmpeg/${{ steps.llvm-mingw-pin.outputs.folder }}", llvmStep);
        Assert.Contains("key: llvm-mingw-v1-${{ runner.os }}-${{ steps.llvm-mingw-pin.outputs.sha256 }}", llvmStep);

        // MSYS2: only on an FFmpeg miss, the win-arm64 packages of FFmpeg.targets.
        Match packages = Msys2Arm64Packages().Match(targets);
        Assert.True(packages.Success, "build/FFmpeg.targets names no _LayerPeekFFmpegMsys2Packages for win-arm64.");
        int msys2 = Array.FindIndex(trimmed, static line => line.StartsWith("uses: msys2/setup-msys2@", StringComparison.Ordinal));
        Assert.True(msys2 > ffmpeg && build > msys2, "MSYS2 is installed after the FFmpeg cache is restored and before the build.");
        string[] msys2Step = StepAround(trimmed, msys2);
        Assert.Contains("if: steps.ffmpeg.outputs.cache-hit != 'true'", msys2Step);
        Assert.Contains("msystem: MSYS", msys2Step);
        Assert.Contains("update: false", msys2Step);
        Assert.Contains($"install: {packages.Groups[1].Value}", msys2Step);
        Assert.Contains("\"LayerPeekMsys2Root=$env:MSYS2_LOCATION\" >> $env:GITHUB_ENV", trimmed);

        // Saved right after the build (a failing AOT link does not lose them), only on a miss, under the restored key.
        int saveFFmpeg = Array.IndexOf(trimmed, "- name: Save FFmpeg (win-arm64)");
        int saveLlvm = Array.IndexOf(trimmed, "- name: Save llvm-mingw");
        int publish = Array.FindIndex(trimmed, static line => line.StartsWith("run: dotnet publish ", StringComparison.Ordinal));
        Assert.True(saveFFmpeg > build && publish > saveFFmpeg, "The FFmpeg cache is saved between the build and the publish.");
        Assert.True(saveLlvm > build && publish > saveLlvm, "llvm-mingw is saved between the build and the publish.");
        string[] saveFFmpegStep = trimmed[saveFFmpeg..(saveFFmpeg + 6)];
        Assert.Contains("if: steps.ffmpeg.outputs.cache-hit != 'true'", saveFFmpegStep);
        Assert.Contains("path: ${{ runner.temp }}/ffmpeg/win-arm64-*", saveFFmpegStep);
        Assert.Contains("key: ${{ steps.ffmpeg.outputs.cache-primary-key }}", saveFFmpegStep);
        string[] saveLlvmStep = trimmed[saveLlvm..(saveLlvm + 6)];
        Assert.Contains("if: steps.llvm-mingw.outcome == 'success' && steps.llvm-mingw.outputs.cache-hit != 'true'", saveLlvmStep);
        Assert.Contains("path: ${{ runner.temp }}/ffmpeg/${{ steps.llvm-mingw-pin.outputs.folder }}", saveLlvmStep);
        Assert.Contains("key: ${{ steps.llvm-mingw.outputs.cache-primary-key }}", saveLlvmStep);

        // NuGet: saved after the publish, also when the publish failed (a retry does not download the packs again), but
        // only after a successful build (its restore completed the package folder).
        int saveNuGet = Array.IndexOf(trimmed, "- name: Save NuGet packages");
        Assert.True(saveNuGet > publish, "The NuGet cache is saved after the publish.");
        Assert.Contains("if: ${{ !cancelled() && steps.build.outcome == 'success' && steps.nuget.outputs.cache-hit != 'true' }}", trimmed[saveNuGet..(saveNuGet + 3)]);
        Assert.Contains("id: build", StepAround(trimmed, build));

        // NuGet: its own key prefix, so the build job's fallback (nuget-<os>-) never picks up this smaller package set.
        Assert.Contains("key: nuget-win-arm64-${{ runner.os }}-${{ hashFiles('Directory.Packages.props', '**/*.csproj', 'global.json') }}", trimmed);
        Assert.Contains(job, static line => line.Contains("nuget-win-arm64-${{ runner.os }}-", StringComparison.Ordinal) && line.Trim().StartsWith("nuget-", StringComparison.Ordinal));
    }

    /// <summary>
    /// T-236: the required check "CI result" also waits for the win-arm64 cross build and fails when it did not pass, but
    /// a docs-only pull request still passes without it.
    /// </summary>
    [Fact]
    public void TheResultCheckIncludesTheArm64Job()
    {
        string[] result = [.. JobLines("result").Select(static line => line.Trim())];

        Assert.Contains("needs: [changes, build, cross-arm64]", result);
        Assert.Contains("ARM64: ${{ needs.cross-arm64.result }}", result);
        // Both failures are reported (failed=1 each), the step exits only at the end.
        int build = Array.FindIndex(result, static line => line.StartsWith("if [ \"$CODE\" = true ] && [ \"$BUILD\" != success ]; then", StringComparison.Ordinal));
        int arm64 = Array.FindIndex(result, static line => line.StartsWith("if [ \"$CODE\" = true ] && [ \"$ARM64\" != success ]; then", StringComparison.Ordinal));
        int exit = Array.IndexOf(result, "if [ \"$failed\" = 1 ]; then exit 1; fi");
        Assert.True(build >= 0 && arm64 >= 0, "The result check evaluates the build and the win-arm64 job.");
        Assert.All(new[] { result[build], result[arm64] }, static line =>
        {
            Assert.EndsWith("failed=1; fi", line, StringComparison.Ordinal);
            Assert.DoesNotContain("exit", line, StringComparison.Ordinal);
        });
        Assert.True(exit > build && exit > arm64, "The result check exits after evaluating every job.");
    }

    [Fact]
    public void DependabotKeepsThePinnedActionsCurrent()
    {
        string dependabot = File.ReadAllText(Path.Combine(TestWorktree.Root, ".github", "dependabot.yml"));

        Assert.Contains("package-ecosystem: github-actions", dependabot, StringComparison.Ordinal);
    }

    /// <summary>The lines of one job: from <c>  id:</c> up to the next job (or the end of the file).</summary>
    private static string[] JobLines(string id)
    {
        string[] lines = Lines();
        int start = Array.IndexOf(lines, $"  {id}:");
        Assert.True(start >= 0, $"No job '{id}' in ci.yml.");
        int end = Array.FindIndex(lines, start + 1, static line => JobLine().IsMatch(line));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    /// <summary>The trimmed lines of the step around line <paramref name="index"/>: from its "- " line to the next one.</summary>
    private static string[] StepAround(string[] trimmed, int index)
    {
        int start = Array.FindLastIndex(trimmed, index, static line => line.StartsWith("- ", StringComparison.Ordinal));
        int end = Array.FindIndex(trimmed, index + 1, static line => line.StartsWith("- ", StringComparison.Ordinal));
        return trimmed[start..(end < 0 ? trimmed.Length : end)];
    }

    [GeneratedRegex(@"<_LayerPeekFFmpegMsys2Packages Condition=""'\$\(_LayerPeekFFmpegRid\)' == 'win-x64'"">([^<]+)</_LayerPeekFFmpegMsys2Packages>")]
    private static partial Regex Msys2Packages();

    [GeneratedRegex(@"<_LayerPeekFFmpegMsys2Packages Condition=""'\$\(_LayerPeekFFmpegRid\)' == 'win-arm64'"">([^<]+)</_LayerPeekFFmpegMsys2Packages>")]
    private static partial Regex Msys2Arm64Packages();

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
