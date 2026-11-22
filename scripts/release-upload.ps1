<#
.SYNOPSIS
    Uploads the release files built by scripts/release-build.ps1 into the DRAFT GitHub release of the tag vX.Y.Z.

.DESCRIPTION
    Normally run by scripts/release.ps1 on both machines (T-200), which creates the draft right after the tag push.
    Without a draft the first run (on either OS) creates it with the notes of the version from CHANGELOG.md, followed by
    the FFmpeg sentence with links to both FFmpeg source archives of the release (T-199); the run on the other OS adds
    its files to the same draft. Nothing is visible to others until scripts/release-publish.ps1 publishes the draft.
    Versions 0.x are marked as pre-releases. Releases are immutable since 1.0.0 (T-180): files go into a draft only.

    Refuses (and lists every reason) when:
      - the working tree is not clean (untracked files included),
      - HEAD has no tag vX.Y.Z matching <Version> in Directory.Build.props, the tag is not on origin (or the -Repo remote) or points to
        another commit there, or HEAD is not contained in its main branch,
      - CHANGELOG.md has no dated, non-empty section for the version,
      - the build output is missing, from another commit or version, from a dirty tree, contains a runtime that must
        not be uploaded (Uploadable = $false in the runtime table; none since win-arm64 became uploadable,
        2026-11-16, T-226), or does not match its checksum file,
      - the update feed is not signed (T-181): a trial build of release-build.ps1 -SkipFeedSignature, a build without the
        signature status, or a releases.<rid>.json without its releases.<rid>.json.sig,
      - gh is missing or not logged in,
      - the release is already published (immutable: nothing can be added), GitHub has more than one release for the
        tag (two drafts, T-200), or a file with the same name but different content is already uploaded (replace it
        only with -Force).
    Files already uploaded with identical content are skipped, so a re-run after a failed upload is safe.
    Upload order (Get-UploadBatches), one gh call each: the packages and the feed signatures (releases.<rid>.json.sig)
    first, then the checksum file and the Velopack feed files, so a feed is never there before its signature (T-181)
    and an OS counts as uploaded (its checksum file) only when all its files are there.
    A new draft is created empty first.
    Reads the remote with git ls-remote only: nothing is fetched or pushed.

.PARAMETER Repo
    GitHub repository owner/name of the release. Default: the repository of the git remote origin. Another
    repository (e.g. a trial repository, docs/release.md) needs its own git remote; the tag and main branch checks then
    use that remote, so the checks and the release always concern the same repository.

.PARAMETER DryRun
    Runs every check and prints what would be created or uploaded; changes nothing on GitHub.

.PARAMETER Force
    Replaces already uploaded files whose content differs (gh release upload --clobber). Drafts only.

.PARAMETER AddToPublished
    No longer supported (T-200): always refused with exit code 1 and the flow "both OS into the draft, then publish";
    releases are immutable since 1.0.0 (T-180). Kept so that an old command gets the explanation.

.EXAMPLE
    pwsh scripts/release-upload.ps1 -DryRun
    pwsh scripts/release-upload.ps1
    pwsh scripts/release-upload.ps1 -Repo pfoltynek/LayerPeek-release-trial -DryRun
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [string] $Repo,
    [switch] $DryRun,
    [switch] $Force,
    [switch] $AddToPublished
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools' 'ReleaseTools.psm1') -Force

# Physical path: dotnet restore breaks on symlinked paths (Resolve-PhysicalPath).
$repoRoot = Resolve-PhysicalPath (Join-Path $PSScriptRoot '..')

try {
    if ($AddToPublished) {
        # T-200: refused before anything is checked or asked on GitHub.
        Write-Problems -Problems @(Get-RetiredSwitchProblem -Switch 'AddToPublished') -Action 'Upload'
        exit 1
    }
    $preflight = Get-ReleasePreflight -RepoRoot $repoRoot -Repository $Repo
    $version = $preflight.Version
    $tag = $preflight.Tag
    $problems = $preflight.Problems
    Write-Host "Release $tag of $($preflight.Repository ?? '?') (git remote $($preflight.Remote ?? '?')), HEAD $($preflight.GitState.Head)"

    $notes = $null
    try { $notes = Get-ChangelogSection -Path (Join-Path $repoRoot 'CHANGELOG.md') -Version $version }
    catch { $problems.Add($_.Exception.Message) }

    # Build marker, checksum file and SHA256 of every file (shared with scripts/release.ps1).
    $output = Get-ReleaseBuildOutput -RepoRoot $repoRoot -Version $version -Head $preflight.GitState.Head
    foreach ($problem in @($output.Problems)) { $problems.Add($problem) }
    $distDir = $output.DistDirectory
    $localFiles = @($output.Files)

    if ($problems.Count -gt 0) {
        if ($DryRun -and $localFiles.Count -gt 0) {
            Write-Host "Files of the build in $($distDir):"
            foreach ($file in $localFiles) { Write-Host "  $($file.Name)" }
        }
        Write-Problems -Problems $problems -Action 'Upload'
        exit 1
    }
    # ffmpeg.org legal checklist point 9: the download page links the FFmpeg source (T-199).
    $notes = Get-ReleaseNotes -ChangelogSection $notes -Repository $preflight.Repository -Version $version

    # Reads the release (drafts included); nothing is changed yet.
    $release = Get-GitHubRelease -Repository $preflight.Repository -Tag $tag
    if ($release -and -not $release.isDraft) {
        # T-180 / T-200: a published release is immutable.
        $hasOs = @(Get-ReleaseOsStatus -Release $release -ExpectedOs @($output.BuildInfo.Os) | Where-Object Uploaded).Count -gt 0
        $state = if ($hasOs) { "It already has the $($output.BuildInfo.Os) files." }
        else { "It has no $($output.BuildInfo.Os) files, and published releases are immutable (T-180): they can never be added. This version stays without them; release the next version from both machines." }
        Write-Problems -Problems @("Release $tag is already published ($($release.url)). Files are uploaded only into a draft. $state (docs/release.md)") -Action 'Upload'
        exit 1
    }
    $duplicates = @(Get-DuplicateReleaseProblems -Tag $tag -Releases @(Get-GitHubTagReleases -Repository $preflight.Repository -Tag $tag))
    if ($duplicates.Count -gt 0) {
        Write-Problems -Problems $duplicates -Action 'Upload'
        exit 1
    }
    $plan = @(Get-UploadPlan -LocalFiles $localFiles -RemoteAssets @($release ? $release.assets : @()) -Force:$Force)
    $conflicts = @($plan | Where-Object Action -EQ 'Conflict')
    if ($conflicts.Count -gt 0) {
        Write-Problems -Problems @("Already uploaded with different content: $($conflicts.Name -join ', '). Re-run with -Force to replace them.") -Action 'Upload'
        exit 1
    }

    $prerelease = Test-PrereleaseVersion $version
    $prefix = if ($DryRun) { 'Would' } else { 'Will' }
    if ($release) { Write-Host "$prefix upload into the existing draft release $tag ($($release.url)):" }
    else { Write-Host "$prefix create the draft release $tag (pre-release: $($prerelease ? 'yes' : 'no'), notes from CHANGELOG.md section [$version]) with:" }
    foreach ($item in $plan) {
        $label = switch ($item.Action) { 'Upload' { 'upload' } 'Replace' { 'replace (-Force)' } 'Skip' { 'skip, identical file already uploaded' } }
        Write-Host ("  {0,-48} {1}" -f $item.Name, $label)
    }
    $toUpload = @($plan | Where-Object Action -In 'Upload', 'Replace' | ForEach-Object { Join-Path $distDir $_.Name })

    if ($DryRun) {
        if (-not $release) {
            Write-Host 'Release notes:'
            Write-Host ($notes -replace '(?m)^', '  | ')
        }
        Write-Host 'Dry run: nothing was changed on GitHub.' -ForegroundColor Green
        exit 0
    }

    if (-not $release) {
        # An empty draft first (and exactly one release for the tag, T-200); its files follow in the order of
        # Get-UploadBatches like in an existing release.
        New-ReleaseDraft -Repository $preflight.Repository -Version $version -Notes $notes
    }
    elseif ($toUpload.Count -eq 0) {
        Write-Host 'Nothing to upload: the release already contains all files.'
    }
    # The packages and the feed signatures first, the files that make the OS visible (checksum file, Velopack feed) in a
    # second call: a feed is never uploaded before its signature (T-181), nor before its packages (T-113).
    $replaced = @($plan | Where-Object Action -EQ 'Replace' | ForEach-Object Name)
    foreach ($batch in @(Get-UploadBatches -Paths $toUpload)) {
        $arguments = @('release', 'upload', $tag, '--repo', $preflight.Repository)
        if (@($batch.Files | Where-Object { $replaced -ccontains (Split-Path -Leaf $_) }).Count -gt 0) { $arguments += '--clobber' }
        $files = $batch.Files
        & gh @arguments @files
        if ($LASTEXITCODE -ne 0) { throw "gh release upload failed with exit code $LASTEXITCODE. Re-run the script: files already uploaded are skipped." }
    }

    $after = Get-GitHubRelease -Repository $preflight.Repository -Tag $tag
    $missing = @(Get-ReleaseOsStatus -Release $after | Where-Object { -not $_.Uploaded } | ForEach-Object Name)
    $next = if ($missing.Count -gt 0) {
        "Still missing: the $($missing -join ' and ') files. Upload them into this draft on that machine (pwsh scripts/release.ps1 -Version $version), then publish; never publish before (immutable, T-180)."
    }
    else { "The draft has the files of every OS. Publish: pwsh scripts/release-publish.ps1$(if ($Repo) { " -Repo $Repo" }) (release.ps1 offers it)." }
    Write-Host "Draft release $tag updated: $($after ? $after.url : '?'). $next" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "Upload FAILED: $_" -ForegroundColor Red
    exit 1
}
