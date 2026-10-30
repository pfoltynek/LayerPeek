<#
.SYNOPSIS
    Uploads the release files built by scripts/release-build.ps1 into the DRAFT GitHub release of the tag vX.Y.Z.

.DESCRIPTION
    The first run (on either OS) creates the draft release with the notes of the version from CHANGELOG.md; the run
    on the other OS adds its files to the same draft. Nothing is visible to others until scripts/release-publish.ps1
    publishes the draft. Versions 0.x are marked as pre-releases.

    Refuses (and lists every reason) when:
      - the working tree is not clean (untracked files included),
      - HEAD has no tag vX.Y.Z matching <Version> in Directory.Build.props, the tag is not on origin (or the -Repo remote) or points to
        another commit there, or HEAD is not contained in its main branch,
      - CHANGELOG.md has no dated, non-empty section for the version,
      - the build output is missing, from another commit or version, from a dirty tree, contains a runtime that must
        not be uploaded (win-arm64 until T-057), or does not match its checksum file,
      - gh is missing or not logged in,
      - the release is already published, or a file with the same name but different content is already uploaded
        (replace it only with -Force).
    Files already uploaded with identical content are skipped, so a re-run after a failed upload is safe.
    Reads the remote with git ls-remote only: nothing is fetched or pushed.

    With -AddToPublished (T-113) the files of the OS of this build are added to the PUBLISHED release instead, when
    the other OS went first (docs/release.md, release on two machines). All the checks above still apply, and it also
    refuses when the release is missing or still a draft, already contains the checksum file of this OS, or contains
    a file of this build with other content or unfinished (state 'starter'). Nothing is ever replaced; identical files
    are skipped (a re-run after an interrupted upload). The checksum file and the Velopack feed files are uploaded
    last, so that no app sees the OS in the release before its packages are there. The release text and its
    pre-release flag are never changed. Afterwards the whole release is checked like release-publish.ps1 does
    (every file listed in the checksum files with its SHA256, nothing else, no win-arm64).

.PARAMETER Repo
    GitHub repository owner/name of the release. Default: the repository of the git remote origin. Another
    repository (e.g. a trial repository, docs/release.md) needs its own git remote; the tag and main branch checks then
    use that remote, so the checks and the release always concern the same repository.

.PARAMETER DryRun
    Runs every check and prints what would be created or uploaded; changes nothing on GitHub.

.PARAMETER Force
    Replaces already uploaded files whose content differs (gh release upload --clobber). Drafts only: never together
    with -AddToPublished.

.PARAMETER AddToPublished
    Adds the files of the OS of this build to the published release that does not have them yet (T-113). Never
    replaces anything.

.EXAMPLE
    pwsh scripts/release-upload.ps1 -DryRun
    pwsh scripts/release-upload.ps1
    pwsh scripts/release-upload.ps1 -Repo pfoltynek/LayerPeek-release-trial -DryRun
    pwsh scripts/release-upload.ps1 -AddToPublished -DryRun
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
$notesFile = $null
$downloadDir = $null

try {
    if ($AddToPublished -and $Force) {
        Write-Problems -Problems @('-AddToPublished never replaces a file, so it cannot be combined with -Force.') -Action 'Upload'
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

    # Reads the release (drafts included); nothing is changed yet.
    $release = Get-GitHubRelease -Repository $preflight.Repository -Tag $tag
    if (-not $AddToPublished -and $release -and -not $release.isDraft) {
        $repoArgument = if ($Repo) { " -Repo $Repo" } else { '' }
        Write-Problems -Problems @("Release $tag is already published ($($release.url)). Files are uploaded only into a draft release. To add the files of an OS that the published release does not have yet: pwsh scripts/release-upload.ps1$repoArgument -AddToPublished (docs/release.md).") -Action 'Upload'
        exit 1
    }
    $plan = @(Get-UploadPlan -LocalFiles $localFiles -RemoteAssets @($release ? $release.assets : @()) -Force:$Force)
    if ($AddToPublished) {
        $addProblems = @(Get-AddToPublishedProblems -Release $release -Tag $tag -ChecksumFile $output.BuildInfo.ChecksumFile -Plan $plan)
        if ($addProblems.Count -gt 0) {
            Write-Problems -Problems $addProblems -Action 'Upload'
            exit 1
        }
    }
    $conflicts = @($plan | Where-Object Action -EQ 'Conflict')
    if ($conflicts.Count -gt 0) {
        Write-Problems -Problems @("Already uploaded with different content: $($conflicts.Name -join ', '). Re-run with -Force to replace them.") -Action 'Upload'
        exit 1
    }

    $prerelease = Test-PrereleaseVersion $version
    $prefix = if ($DryRun) { 'Would' } else { 'Will' }
    if ($AddToPublished) { Write-Host "$prefix add the $($output.BuildInfo.Os) files to the published release $tag ($($release.url)); its text and pre-release flag stay as they are, the checksum file and the Velopack feed go last:" }
    elseif ($release) { Write-Host "$prefix upload into the existing draft release $tag ($($release.url)):" }
    else { Write-Host "$prefix create the draft release $tag (pre-release: $($prerelease ? 'yes' : 'no'), notes from CHANGELOG.md section [$version]) with:" }
    foreach ($item in $plan) {
        $label = switch ($item.Action) { 'Upload' { 'upload' } 'Replace' { 'replace (-Force)' } 'Skip' { 'skip, identical file already uploaded' } }
        Write-Host ("  {0,-48} {1}" -f $item.Name, $label)
    }
    $toUpload = @($plan | Where-Object Action -In 'Upload', 'Replace' | ForEach-Object { Join-Path $distDir $_.Name })

    if ($DryRun) {
        if (-not $AddToPublished) {
            Write-Host 'Release notes:'
            Write-Host ($notes -replace '(?m)^', '  | ')
        }
        Write-Host 'Dry run: nothing was changed on GitHub.' -ForegroundColor Green
        exit 0
    }

    if (-not $release) {
        $notesFile = Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-release-notes-$([guid]::NewGuid().ToString('N')).md"
        [System.IO.File]::WriteAllText($notesFile, $notes + "`n", [System.Text.UTF8Encoding]::new($false))
        $arguments = @('release', 'create', $tag, '--repo', $preflight.Repository, '--draft', '--verify-tag',
            '--title', "LayerPeek $version", '--notes-file', $notesFile)
        if ($prerelease) { $arguments += '--prerelease' }
        & gh @arguments @toUpload
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed with exit code $LASTEXITCODE. Re-run the script: files already uploaded are skipped." }
    }
    elseif ($AddToPublished) {
        # The packages first, the files that make the OS visible (checksum file, Velopack feed) in a second call.
        $packages = @($toUpload | Where-Object { -not (Test-ReleaseMarkerFileName (Split-Path -Leaf $_)) })
        $markers = @($toUpload | Where-Object { Test-ReleaseMarkerFileName (Split-Path -Leaf $_) })
        foreach ($group in @{ Files = $packages }, @{ Files = $markers }) {
            if ($group.Files.Count -eq 0) { continue }
            $files = $group.Files
            & gh release upload $tag --repo $preflight.Repository @files
            if ($LASTEXITCODE -ne 0) { throw "gh release upload failed with exit code $LASTEXITCODE. Re-run the script with -AddToPublished: files already uploaded are skipped." }
        }
    }
    elseif ($toUpload.Count -gt 0) {
        $arguments = @('release', 'upload', $tag, '--repo', $preflight.Repository)
        if (@($plan | Where-Object Action -EQ 'Replace').Count -gt 0) { $arguments += '--clobber' }
        & gh @arguments @toUpload
        if ($LASTEXITCODE -ne 0) { throw "gh release upload failed with exit code $LASTEXITCODE. Re-run the script: files already uploaded are skipped." }
    }
    else {
        Write-Host 'Nothing to upload: the draft already contains all files.'
    }

    if ($AddToPublished) {
        # The same check as release-publish.ps1, of the published release (T-113).
        $after = Get-GitHubRelease -Repository $preflight.Repository -Tag $tag
        $downloadDir = Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-release-sums-$([guid]::NewGuid().ToString('N'))"
        $checksums = Get-ReleaseChecksums -Release $after -Repository $preflight.Repository -Tag $tag -Directory $downloadDir
        $config = Get-ReleaseRuntimeConfig
        $expectedOs = @($config.Values | Where-Object Uploadable | ForEach-Object Os | Sort-Object -Unique)
        $check = @(Get-PublishProblems -Release $after -Tag $tag -ExpectedOs $expectedOs -Checksums $checksums -Config $config -Published)
        if ($check.Count -gt 0) {
            Write-Problems -Problems $check -Action "Check of the published release $tag"
            Write-Host 'The files of this build are uploaded, but the release is not complete or not consistent. Fix it by hand (docs/release.md).' -ForegroundColor Red
            exit 1
        }
        Write-Host "The $($output.BuildInfo.Os) files are added to the published release $tag; the release is complete and every file matches its checksum file: $($after.url)" -ForegroundColor Green
        exit 0
    }
    Write-Host "Draft release $tag updated. Upload the build of the other OS, then publish: pwsh scripts/release-publish.ps1 (or publish with -AllowIncomplete and add the other OS later with -AddToPublished, docs/release.md)" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "Upload FAILED: $_" -ForegroundColor Red
    exit 1
}
finally {
    if ($notesFile -and (Test-Path -LiteralPath $notesFile)) { Remove-Item -LiteralPath $notesFile -Force -ErrorAction SilentlyContinue }
    if ($downloadDir -and (Test-Path -LiteralPath $downloadDir)) { Remove-Item -LiteralPath $downloadDir -Recurse -Force -ErrorAction SilentlyContinue }
}
