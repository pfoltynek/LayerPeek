<#
.SYNOPSIS
    One command for a release on both machines (Windows and macOS, either one first, T-200): Gitflow release branch,
    merge into main with tag vX.Y.Z, the draft on GitHub, merge back into develop, build of the tag
    (scripts/release-build.ps1), upload into the draft (scripts/release-upload.ps1) and, once the draft has the files of
    both OS, the publish (scripts/release-publish.ps1, confirmed by typing the version).

.DESCRIPTION
    The same command runs on the Windows machine and on the Mac, in either order (docs/release.md, release on two
    machines). What is done is detected from the state, not from a record of an earlier run:
      - First machine: every step up to its files in the draft. The draft is created right after the tag push, so the
        other machine may start as soon as the tag is on GitHub. It ends with NEXT STEP for the other machine.
      - Other machine: the Gitflow steps are done on the remote (tag on origin, origin/main contains it, origin/develop
        contains the release commit) and count as done; a real run fetches exactly the tag vX.Y.Z (never moves a local
        tag). It builds the tag, uploads into the same draft and, with the files of both OS there, offers to publish.
    Releases are immutable since 1.0.0 (T-180): a published release can never get files, so it is published only with
    the files of both OS in the draft, and a release published without the files of this OS is refused.

    Steps (docs/release.md, "Postup"):
      1. release/X.Y.Z from develop with the commit "Release X.Y.Z": <Version> in Directory.Build.props, release date of
         the version in CHANGELOG.md (the section '## [X.Y.Z] - unreleased' gets the date, or the entries of
         '## [Unreleased]' move into a new dated section and an empty '## [Unreleased]' stays).
      2. merge --no-ff into main, annotated tag vX.Y.Z, push main + tag (confirmed by typing the version), the empty
         draft release with the notes of the tag (T-200), merge --no-ff back into develop, push develop (confirmed y/N),
         delete the local release branch.
      3. checkout of the tag (detached, clean tree), release-build.ps1, release-upload.ps1 -DryRun, then (confirmed y/N)
         release-upload.ps1 into the draft. The build has only the RIDs that may be uploaded: on Windows win-x64 and
         win-arm64 (the default build, T-226), on macOS osx-arm64.
         release-build.ps1 signs the update feed (T-181) with the key in LAYERPEEK_FEED_SIGNING_KEY and asks for its
         password twice (key check before the build, signing after it; keep the key medium connected until the build
         ends); without the variable the build step fails (docs/release.md, "Podpis feedu aktualizací").
      4. with the files of every OS in the draft: release-publish.ps1 -DryRun, then (confirmed by typing the version)
         release-publish.ps1, which also offers the winget submission on Windows (T-059). Without the files of the other
         OS the run ends with NEXT STEP for that machine. Back to develop at the end and on failure.
    The commits and merges of steps 1-2 are made without touching the working tree (a separate index, git merge-tree),
    so a failure leaves no half-done merge; branch refs move only by compare-and-swap or fast-forward. Nothing is ever
    force-pushed and no pushed tag is deleted.

    All preconditions are checked first and reported at once: clean tree (untracked files included), on develop and
    equal to origin/develop, origin/main and origin/develop fetched, version X.Y.Z greater than every release tag, tag
    not taken, CHANGELOG.md has entries for the version, gh installed and logged in, exactly one GitHub release of the
    tag, the build toolchain present (Windows: MSVC and vpk, macOS: Xcode Command Line Tools).
    A real run first fetches the release remote (git fetch --no-tags: remote-tracking branches only) and, when the tag
    is on the remote but not local (the other machine went first), exactly that tag (refs/tags/vX.Y.Z, without +, so an
    existing local tag is never moved); -DryRun fetches nothing, reads the remote with git ls-remote and asks for a
    fetch when needed.
    An existing release/X.Y.Z (from an earlier run or made by hand) is checked like a new one: version, dated changelog,
    based on the current develop. Its commits that develop does not have yet are listed. Resumed on a later day before
    the tag exists, the branch gets a commit that dates the changelog section with the new release day.
    Pushes go by commit SHA (<merge>:refs/heads/main, the tag, <merge>:refs/heads/develop); right before each push the
    local refs are compared with the confirmed SHAs.

    Confirmations are read with Read-Host, so they can also come from piped input, one line per question (the Pester
    tests do this: '0.1.0', 'y', 'y' | pwsh scripts/release.ps1 -Version 0.1.0). A non-interactive session counts as "no".
    The last lines (NEXT STEP) always say what to run next and on which machine, also after a failure.

.PARAMETER Version
    The version to release, X.Y.Z (e.g. 0.1.0). The tag is vX.Y.Z, the branch release/X.Y.Z.

.PARAMETER Repo
    GitHub repository owner/name, as for release-upload.ps1 (e.g. a trial repository). Default: the repository of the
    git remote origin. The git steps (push of main, tag and develop) use the remote of that repository.
    Run it only in a separate temporary clone whose origin is not another repository (docs/release.md, trial release):
    a clone with an origin of another repository is refused, so that its main and develop do not get the merges.

.PARAMETER DryRun
    Checks everything and prints the plan (which steps are done, which would run, the files of each OS on GitHub) and
    the next step. Changes nothing: no git change, no fetch, no push, nothing on GitHub (gh only reads). Exit code 1
    when a precondition fails.

.EXAMPLE
    pwsh scripts/release.ps1 -Version 0.1.0 -DryRun
    pwsh scripts/release.ps1 -Version 0.1.0
    caffeinate -d -i -u pwsh scripts/release.ps1 -Version 0.1.0
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
    Write-Host "NEXT STEP: re-run on this machine: $(Get-ReleaseRunCommand -Version $Version -Repository $Repo -Os (Get-HostOs))" -ForegroundColor Yellow
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

function Enter-TagCheckout {
    # The build, the upload and the publish run in the clean checkout of the tag (their scripts check HEAD).
    param([Parameter(Mandatory)] [string] $Tag)
    if ($script:detached) { return }
    Write-Host "==> git switch --detach $Tag" -ForegroundColor Cyan
    $null = Invoke-GitChecked $repoRoot @('switch', '--quiet', '--detach', "refs/tags/$Tag")
    $script:detached = $true
    if (@(Get-GitStatusLines $repoRoot).Count -gt 0) { throw "The checkout of $Tag is not clean." }
}

function Write-NextSteps {
    param([Parameter(Mandatory)] [string] $Situation, [string[]] $WaitingFor = @())
    Write-Host ''
    foreach ($line in @(Get-ReleaseNextSteps -Version $Version -Repository $Repo -HostOs (Get-HostOs) -Situation $Situation -WaitingFor $WaitingFor)) {
        Write-Host $line -ForegroundColor $(if ($line -like 'NEXT STEP*') { 'Green' } else { 'Gray' })
    }
}

try {
    if (-not (Test-ReleaseVersion $Version)) {
        Write-Problems -Problems @(Get-ReleaseVersionProblems -Version $Version) -Action 'Release'
        exit 1
    }
    $tag = ConvertTo-ReleaseTag $Version
    if (-not $DryRun) {
        # A real run fetches the release remote first (decided 2026-10-24), so that the checks see its current state.
        # --no-tags: only the remote-tracking branches change; no local branch and no tag is created or moved.
        $target = Resolve-ReleaseRemote -Remotes (Get-GitRemotes $repoRoot) -Repository $Repo
        if ($target.Remote) {
            Write-Host "==> git fetch --no-tags $($target.Remote)" -ForegroundColor Cyan
            $fetch = Invoke-Git $repoRoot @('fetch', '--quiet', '--no-tags', $target.Remote)
            if ($fetch.ExitCode -ne 0) { throw "git fetch $($target.Remote) failed: $($fetch.Error)" }
            # T-200 (user decision 2026-11-22): the other machine went first, so the tag of this release is on the
            # remote. Exactly that tag is fetched; without '+' an existing local tag is never moved (a different one
            # is reported by the plan).
            $remoteTag = Invoke-Git $repoRoot @('ls-remote', '--tags', $target.Remote, "refs/tags/$tag")
            $localTag = Invoke-Git $repoRoot @('rev-parse', '--verify', '--quiet', "refs/tags/$tag")
            if ($remoteTag.ExitCode -eq 0 -and @($remoteTag.Output | Where-Object { $_ }).Count -gt 0 -and $localTag.ExitCode -ne 0) {
                Write-Host "==> git fetch --no-tags $($target.Remote) refs/tags/$($tag):refs/tags/$tag" -ForegroundColor Cyan
                $fetchTag = Invoke-Git $repoRoot @('fetch', '--quiet', '--no-tags', $target.Remote, "refs/tags/$($tag):refs/tags/$tag")
                if ($fetchTag.ExitCode -ne 0) { throw "git fetch of tag $tag from $($target.Remote) failed: $($fetchTag.Error)" }
            }
        }
    }
    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-release-$([guid]::NewGuid().ToString('N'))"
    $null = New-Item -ItemType Directory -Path $tempDir
    $date = (Get-Date).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)

    $facts = Get-ReleaseFlowFacts -RepoRoot $repoRoot -Version $Version -Repository $Repo -TempDirectory $tempDir -Date $date
    $plan = Get-ReleaseFlowPlan -Facts $facts
    $remote = $facts.Remote
    $branch = $facts.Branch
    $repoArguments = if ($Repo) { @('-Repo', $Repo) } else { @() }

    $mode = if ($DryRun) { ' (dry run)' } else { '' }
    Write-Host "Release $Version$mode of $($facts.Repository ?? '?') (git remote $($remote ?? '?')), $($facts.HostOs), release date $date"
    Write-Host 'Plan:'
    foreach ($line in @(Format-ReleaseFlowPlan -Plan $plan -Facts $facts)) { Write-Host $line }

    if ($plan.Problems.Count -gt 0) {
        Write-Problems -Problems $plan.Problems -Action 'Release'
        if (@($plan.Steps | Where-Object Done).Count -gt 0) {
            Write-Host 'Steps marked done were detected from an earlier run (here or on the other machine).' -ForegroundColor Yellow
        }
        Write-Host "NEXT STEP: fix the problems above as they say, then re-run on this machine: $(Get-ReleaseRunCommand -Version $Version -Repository $Repo -Os $facts.HostOs -DryRun:$DryRun)" -ForegroundColor Yellow
        exit 1
    }
    $steps = @{}
    foreach ($step in $plan.Steps) { $steps[$step.Id] = $step }

    if ($DryRun) {
        Write-Host 'Dry run: nothing was changed (no git change, no fetch, no push, nothing on GitHub).' -ForegroundColor Green
        $waiting = @($plan.Waiting)
        $todo = @($plan.Steps | Where-Object { -not $_.Done -and -not ($_.Id -eq 'Publish' -and $waiting.Count -gt 0) })
        if ($plan.Published) { Write-NextSteps 'Published' }
        elseif ($todo.Count -eq 0) { Write-NextSteps 'OtherOs' $waiting }
        else {
            Write-NextSteps 'Run'
            if ($waiting.Count -gt 0) { Write-Host "  It ends with the files of this OS in the draft; the publish follows on the machine of the $($waiting -join ' and ') files." }
        }
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

    # Step 2: main, tag, draft, develop.
    # The commits to push are fixed here (made in this run, or taken from the facts when resumed) and pushed by SHA.
    $mainCommit = $facts.LocalTagCommit
    if (-not $steps.MergeMain.Done) {
        Write-Host "==> $($steps.MergeMain.Title)" -ForegroundColor Cyan
        $mainCommit = Invoke-ReleaseMainMerge -RepoRoot $repoRoot -Version $Version -Date $date -ReleaseCommit $releaseCommit -Base $facts.RemoteMain `
            -LocalMain $facts.LocalMain -Remote $remote -ExistingTagCommit $facts.LocalTagCommit -TempDirectory $tempDir
        Write-Host "  main and $tag at $mainCommit"
    }
    $draftConfirmed = $false
    if (-not $steps.PushMain.Done) {
        Write-Host "==> $($steps.PushMain.Title)" -ForegroundColor Cyan
        Write-Host "  Commits that go to $remote/main (main and $tag = $mainCommit):"
        foreach ($line in @(Invoke-GitChecked $repoRoot @('log', '--oneline', '--first-parent', "$($facts.RemoteMain)..$mainCommit"))) { Write-Host "    $line" }
        $draftNote = if (-not $steps.Draft.Done) { ' and create the draft release' } else { '' }
        if (-not (Confirm-ReleaseStep -Question "Type $Version to push main and $tag to $remote$draftNote" -Expected $Version)) { Stop-NotConfirmed $steps.PushMain.Title }
        $draftConfirmed = $true
        Invoke-ReleasePush -RepoRoot $repoRoot -Remote $remote -Branches @{ main = $mainCommit } -Tags @{ $tag = $mainCommit }
    }
    if (-not $steps.Draft.Done) {
        # T-200: the draft right after the tag push, so that the other machine (which may start now) finds it.
        Write-Host "==> $($steps.Draft.Title)" -ForegroundColor Cyan
        if (-not $draftConfirmed -and -not (Confirm-ReleaseStep -Question "Create the draft release $tag on GitHub (only maintainers see it)? [y/N]")) { Stop-NotConfirmed $steps.Draft.Title }
        $notes = Get-ReleaseNotesAtCommit -RepoRoot $repoRoot -Commit $mainCommit -Version $Version -Repository $facts.Repository -TempDirectory $tempDir
        New-ReleaseDraft -Repository $facts.Repository -Version $Version -Notes $notes
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
        # T-200: from here the other machine finds everything it needs on GitHub (tag, main, develop, the draft).
        $otherOs = if ($facts.HostOs -eq 'windows') { 'macOS' } else { 'Windows' }
        Write-Host "  Tag $tag, develop and the draft are on GitHub: the $otherOs machine may start its run now (pwsh scripts/release.ps1 -Version $Version), also in parallel with this one." -ForegroundColor Green
    }
    if (-not $steps.DeleteBranch.Done) {
        Write-Host "==> $($steps.DeleteBranch.Title)" -ForegroundColor Cyan
        $null = Invoke-GitChecked $repoRoot @('branch', '-d', $branch)
    }

    # Step 3: build of the tag and upload into the draft.
    if (-not $steps.Build.Done) {
        Enter-TagCheckout $tag
        # T-226: only the uploadable RIDs (no -Runtime while every default RID is uploadable).
        Invoke-ReleaseScript 'release-build.ps1' @(Get-ReleaseBuildRuntimeArguments -Os $facts.HostOs)
    }
    if (-not $steps.Upload.Done) {
        Enter-TagCheckout $tag
        Invoke-ReleaseScript 'release-upload.ps1' (@('-DryRun') + $repoArguments)
        if (-not (Confirm-ReleaseStep -Question "Upload these files into the draft release $tag on GitHub? [y/N]")) { Stop-NotConfirmed $steps.Upload.Title }
        Invoke-ReleaseScript 'release-upload.ps1' $repoArguments
    }

    # Step 4 (T-200): publish once the draft has the files of every OS; otherwise the other machine follows.
    $release = Get-GitHubRelease -Repository $facts.Repository -Tag $tag
    $status = @(Get-ReleaseOsStatus -Release $release -ExpectedOs $facts.ExpectedOs)
    Write-Host ''
    Write-Host "GitHub $tag ($($release ? $release.url : '?')): $(@($status | ForEach-Object { "$($_.Name) files $(if ($_.Uploaded) { 'uploaded' } else { 'missing' })" }) -join ', ')."
    if ($release -and -not $release.isDraft) {
        Write-Host "Release $tag is already published." -ForegroundColor Green
        Write-NextSteps 'Published'
        exit 0
    }
    $missing = @($status | Where-Object { -not $_.Uploaded } | ForEach-Object Name)
    if ($missing.Count -gt 0) {
        Write-Host "Draft release $tag has the files of this machine; it waits for the $($missing -join ' and ') files." -ForegroundColor Green
        Write-NextSteps 'OtherOs' $missing
        exit 0
    }
    Enter-TagCheckout $tag
    Invoke-ReleaseScript 'release-publish.ps1' (@('-DryRun') + $repoArguments)
    if (-not (Confirm-ReleaseStep -Question "Type $Version to PUBLISH $tag now (published releases are immutable: no file can be changed or added afterwards)" -Expected $Version)) {
        Write-Host "Not published: the draft $tag stays complete and private." -ForegroundColor Yellow
        Write-NextSteps 'Publish'
        exit 1
    }
    Invoke-ReleaseScript 'release-publish.ps1' $repoArguments
    # The URL of a draft (untagged-...) is dead after publishing: read the release again.
    $published = try { Get-GitHubRelease -Repository $facts.Repository -Tag $tag } catch { $null }
    Write-Host "Release $tag is published: $(if ($published) { $published.url } else { $release.url })" -ForegroundColor Green
    Write-NextSteps 'Published'
    exit 0
}
catch {
    Write-Host "Release FAILED: $_" -ForegroundColor Red
    Write-Host "NEXT STEP: fix the cause and re-run on this machine: $(Get-ReleaseRunCommand -Version $Version -Repository $Repo -Os (Get-HostOs)). Finished steps (also those of the other machine) are detected and skipped (docs/release.md)." -ForegroundColor Yellow
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
