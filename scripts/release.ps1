<#
.SYNOPSIS
    One command for a release up to a DRAFT on GitHub: Gitflow release branch, merge into main with tag vX.Y.Z, merge
    back into develop, build of the tag (scripts/release-build.ps1) and upload into the draft (scripts/release-upload.ps1).
    Never publishes: publishing stays the separate manual step scripts/release-publish.ps1 (docs/release.md).

.DESCRIPTION
    Steps (docs/release.md, "Postup" steps 1-3):
      1. release/X.Y.Z from develop with the commit "Release X.Y.Z": <Version> in Directory.Build.props, release date of
         the version in CHANGELOG.md (the section '## [X.Y.Z] - unreleased' gets the date, or the entries of
         '## [Unreleased]' move into a new dated section and an empty '## [Unreleased]' stays).
      2. merge --no-ff into main, annotated tag vX.Y.Z, push main + tag (confirmed by typing the version), merge --no-ff
         back into develop, push develop (confirmed y/N), delete the local release branch.
      3. checkout of the tag (detached, clean tree), release-build.ps1, release-upload.ps1 -DryRun, then (confirmed y/N)
         release-upload.ps1, which creates the draft or fills it. Back to develop at the end and on failure.
    The commits and merges of steps 1-2 are made without touching the working tree (a separate index, git merge-tree),
    so a failure leaves no half-done merge; branch refs move only by compare-and-swap or fast-forward. Nothing is ever
    force-pushed and no pushed tag is deleted.

    All preconditions are checked first and reported at once: clean tree (untracked files included), on develop and
    equal to origin/develop, origin/main and origin/develop fetched, version X.Y.Z greater than every release tag, tag
    not taken, CHANGELOG.md has entries for the version, gh installed and logged in, MSVC / vpk present for the build.
    A real run first fetches the release remote (git fetch --no-tags: remote-tracking branches only, no local branch or
    tag changes); -DryRun fetches nothing, reads the remote with git ls-remote and asks for git fetch when needed.
    An existing release/X.Y.Z (from an earlier run or made by hand) is checked like a new one: version, dated changelog,
    based on the current develop. Its commits that develop does not have yet are listed. Resumed on a later day before
    the tag exists, the branch gets a commit that dates the changelog section with the new release day.
    Pushes go by commit SHA (<merge>:refs/heads/main, the tag, <merge>:refs/heads/develop); right before each push the
    local refs are compared with the confirmed SHAs.

    Confirmations are read with Read-Host, so they can also come from piped input, one line per question (the Pester
    tests do this: '0.1.0', 'y', 'y' | pwsh scripts/release.ps1 -Version 0.1.0). A non-interactive session counts as "no".

    Resumable: a re-run with the same version detects what is done (release branch, local tag, tag and merge on the
    remote, merge in develop, build of the tag, files in the draft) and continues with the first missing step.

.PARAMETER Version
    The version to release, X.Y.Z (e.g. 0.1.0). The tag is vX.Y.Z, the branch release/X.Y.Z.

.PARAMETER Repo
    GitHub repository owner/name, as for release-upload.ps1 (e.g. a trial repository). Default: the repository of the
    git remote origin. The git steps (push of main, tag and develop) use the remote of that repository.
    Run it only in a separate temporary clone whose origin is not another repository (docs/release.md, trial release):
    a clone with an origin of another repository is refused, so that its main and develop do not get the merges.

.PARAMETER DryRun
    Checks everything and prints the plan (which steps are done, which would run). Changes nothing: no git change, no
    push, nothing on GitHub (gh only reads). Exit code 1 when a precondition fails.

.EXAMPLE
    pwsh scripts/release.ps1 -Version 0.1.0 -DryRun
    pwsh scripts/release.ps1 -Version 0.1.0
    pwsh scripts/release.ps1 -Version 0.1.0 -Repo pfoltynek/LayerPeek-release-trial
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Repo,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools' 'ReleaseTools.psm1') -Force

# Physical path: dotnet restore breaks on symlinked paths (Resolve-PhysicalPath).
$repoRoot = Resolve-PhysicalPath (Join-Path $PSScriptRoot '..')
$tempDir = $null
$detached = $false

function Confirm-ReleaseStep {
    # Interactive confirmation; a non-interactive session (Read-Host fails) counts as "no".
    param([Parameter(Mandatory)] [string] $Question, [string] $Expected)
    try { $answer = Read-Host $Question }
    catch { return $false }
    if ($Expected) { return "$answer".Trim() -ceq $Expected }
    "$answer".Trim() -match '^(?i:y|yes)$'
}

function Stop-NotConfirmed {
    param([Parameter(Mandatory)] [string] $Step)
    Write-Host "Stopped before: $Step (not confirmed). Nothing of this step was done; re-run the same command to continue." -ForegroundColor Yellow
    exit 1
}

function Invoke-ReleaseScript {
    # Runs a release script of the checked-out tag in a child pwsh; throws on a non-zero exit code.
    param([Parameter(Mandatory)] [string] $Name, [string[]] $Arguments = @())
    $path = Join-Path $repoRoot 'scripts' $Name
    Write-Host "==> pwsh scripts/$Name $($Arguments -join ' ')".TrimEnd() -ForegroundColor Cyan
    Invoke-Pwsh -NoProfile -File $path @Arguments
    if ($LASTEXITCODE -ne 0) { throw "scripts/$Name failed with exit code $LASTEXITCODE." }
}

try {
    if (-not (Test-ReleaseVersion $Version)) {
        Write-Problems -Problems @(Get-ReleaseVersionProblems -Version $Version) -Action 'Release'
        exit 1
    }
    if (-not $DryRun) {
        # A real run fetches the release remote first (decided 2026-10-24), so that the checks see its current state.
        # --no-tags: only the remote-tracking branches change; no local branch and no tag is created or moved.
        $target = Resolve-ReleaseRemote -Remotes (Get-GitRemotes $repoRoot) -Repository $Repo
        if ($target.Remote) {
            Write-Host "==> git fetch --no-tags $($target.Remote)" -ForegroundColor Cyan
            $fetch = Invoke-Git $repoRoot @('fetch', '--quiet', '--no-tags', $target.Remote)
            if ($fetch.ExitCode -ne 0) { throw "git fetch $($target.Remote) failed: $($fetch.Error)" }
        }
    }
    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-release-$([guid]::NewGuid().ToString('N'))"
    $null = New-Item -ItemType Directory -Path $tempDir
    $date = (Get-Date).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)

    $facts = Get-ReleaseFlowFacts -RepoRoot $repoRoot -Version $Version -Repository $Repo -TempDirectory $tempDir -Date $date
    $plan = Get-ReleaseFlowPlan -Facts $facts
    $remote = $facts.Remote
    $tag = $facts.Tag
    $branch = $facts.Branch

    $mode = if ($DryRun) { ' (dry run)' } else { '' }
    Write-Host "Release $Version$mode of $($facts.Repository ?? '?') (git remote $($remote ?? '?')), $($facts.HostOs), release date $date"
    Write-Host 'Plan:'
    foreach ($line in @(Format-ReleaseFlowPlan -Plan $plan -Facts $facts)) { Write-Host $line }

    if ($plan.Problems.Count -gt 0) {
        Write-Problems -Problems $plan.Problems -Action 'Release'
        if (@($plan.Steps | Where-Object Done).Count -gt 0) {
            Write-Host 'Steps marked done were detected from an earlier run; fix the problems and re-run to continue.' -ForegroundColor Yellow
        }
        exit 1
    }
    $steps = @{}
    foreach ($step in $plan.Steps) { $steps[$step.Id] = $step }

    if ($DryRun) {
        Write-Host 'Dry run: nothing was changed (no git change, no push, nothing on GitHub).' -ForegroundColor Green
        foreach ($line in @(Get-ReleaseNextSteps -Version $Version -Repository $Repo)) { Write-Host $line }
        exit 0
    }

    # Step 1: release branch with the version and the dated changelog.
    $releaseCommit = $facts.ReleaseCommit
    if (-not $steps.Branch.Done) {
        Write-Host "==> $($steps.Branch.Title)" -ForegroundColor Cyan
        $releaseCommit = if ($facts.LocalBranchCommit) {
            # Resumed on a later day before the tag: the existing branch gets the release day (decided 2026-10-24).
            New-ReleaseDateCommit -RepoRoot $repoRoot -Version $Version -Date $date -ExpectedTip $facts.LocalBranchCommit -TempDirectory $tempDir
        }
        else { New-ReleaseBranch -RepoRoot $repoRoot -Version $Version -Date $date -BaseCommit $facts.LocalDevelop -TempDirectory $tempDir }
        Write-Host "  $branch at $releaseCommit"
    }

    # Step 2: main, tag, develop.
    # The commits to push are fixed here (made in this run, or taken from the facts when resumed) and pushed by SHA.
    $mainCommit = $facts.LocalTagCommit
    if (-not $steps.MergeMain.Done) {
        Write-Host "==> $($steps.MergeMain.Title)" -ForegroundColor Cyan
        $mainCommit = Invoke-ReleaseMainMerge -RepoRoot $repoRoot -Version $Version -Date $date -ReleaseCommit $releaseCommit -Base $facts.RemoteMain `
            -LocalMain $facts.LocalMain -Remote $remote -ExistingTagCommit $facts.LocalTagCommit -TempDirectory $tempDir
        Write-Host "  main and $tag at $mainCommit"
    }
    if (-not $steps.PushMain.Done) {
        Write-Host "==> $($steps.PushMain.Title)" -ForegroundColor Cyan
        Write-Host "  Commits that go to $remote/main (main and $tag = $mainCommit):"
        foreach ($line in @(Invoke-GitChecked $repoRoot @('log', '--oneline', '--first-parent', "$($facts.RemoteMain)..$mainCommit"))) { Write-Host "    $line" }
        if (-not (Confirm-ReleaseStep -Question "Type $Version to push main and $tag to $remote" -Expected $Version)) { Stop-NotConfirmed $steps.PushMain.Title }
        Invoke-ReleasePush -RepoRoot $repoRoot -Remote $remote -Branches @{ main = $mainCommit } -Tags @{ $tag = $mainCommit }
    }
    $developCommit = $facts.LocalDevelop
    if (-not $steps.MergeDevelop.Done) {
        Write-Host "==> $($steps.MergeDevelop.Title)" -ForegroundColor Cyan
        $developCommit = Invoke-ReleaseDevelopMerge -RepoRoot $repoRoot -Version $Version -ReleaseCommit $releaseCommit -Develop $facts.LocalDevelop
        Write-Host "  develop at $developCommit"
    }
    if (-not $steps.PushDevelop.Done) {
        Write-Host "==> $($steps.PushDevelop.Title)" -ForegroundColor Cyan
        if (-not (Confirm-ReleaseStep -Question "Push develop ($developCommit) to $($remote)? [y/N]")) { Stop-NotConfirmed $steps.PushDevelop.Title }
        Invoke-ReleasePush -RepoRoot $repoRoot -Remote $remote -Branches @{ develop = $developCommit }
    }
    if (-not $steps.DeleteBranch.Done) {
        Write-Host "==> $($steps.DeleteBranch.Title)" -ForegroundColor Cyan
        $null = Invoke-GitChecked $repoRoot @('branch', '-d', $branch)
    }

    # Step 3: build of the tag and upload into the draft.
    $repoArguments = if ($Repo) { @('-Repo', $Repo) } else { @() }
    if (-not $steps.Build.Done -or -not $steps.Upload.Done) {
        Write-Host "==> git switch --detach $tag" -ForegroundColor Cyan
        $null = Invoke-GitChecked $repoRoot @('switch', '--quiet', '--detach', "refs/tags/$tag")
        $detached = $true
        if (@(Get-GitStatusLines $repoRoot).Count -gt 0) { throw "The checkout of $tag is not clean." }
    }
    if (-not $steps.Build.Done) {
        Invoke-ReleaseScript 'release-build.ps1'
    }
    if (-not $steps.Upload.Done) {
        Invoke-ReleaseScript 'release-upload.ps1' (@('-DryRun') + $repoArguments)
        if (-not (Confirm-ReleaseStep -Question "Upload these files into the draft release $tag on GitHub? [y/N]")) { Stop-NotConfirmed $steps.Upload.Title }
        Invoke-ReleaseScript 'release-upload.ps1' $repoArguments
    }

    $release = try { Get-GitHubRelease -Repository $facts.Repository -Tag $tag } catch { $null }
    Write-Host ''
    if ($release -and -not $release.isDraft) { Write-Host "Release $tag is already published: $($release.url)" -ForegroundColor Green }
    else {
        Write-Host "Draft release $tag is ready: $($release ? $release.url : '?')" -ForegroundColor Green
        foreach ($line in @(Get-ReleaseNextSteps -Version $Version -Repository $Repo)) { Write-Host $line }
    }
    exit 0
}
catch {
    Write-Host "Release FAILED: $_" -ForegroundColor Red
    Write-Host "Fix the cause and re-run: pwsh scripts/release.ps1 -Version $Version$(if ($Repo) { " -Repo $Repo" }). Finished steps are detected and skipped (docs/release.md)." -ForegroundColor Yellow
    exit 1
}
finally {
    if ($detached) {
        # Back to the branch the release started from (develop); never forced, so nothing is lost.
        $back = Invoke-Git $repoRoot @('switch', '--quiet', 'develop')
        if ($back.ExitCode -ne 0) { Write-Host "Could not switch back to develop: $($back.Error). Run: git switch develop" -ForegroundColor Red }
    }
    if ($tempDir -and (Test-Path -LiteralPath $tempDir)) { Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue }
}
