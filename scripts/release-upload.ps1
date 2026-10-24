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

.PARAMETER Repo
    GitHub repository owner/name of the release. Default: the repository of the git remote origin. Another
    repository (e.g. a trial repository, docs/release.md) needs its own git remote; the tag and main branch checks then
    use that remote, so the checks and the release always concern the same repository.

.PARAMETER DryRun
    Runs every check and prints what would be created or uploaded; changes nothing on GitHub.

.PARAMETER Force
    Replaces already uploaded files whose content differs (gh release upload --clobber).

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
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools' 'ReleaseTools.psm1') -Force

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
$notesFile = $null

try {
    $preflight = Get-ReleasePreflight -RepoRoot $repoRoot -Repository $Repo
    $version = $preflight.Version
    $tag = $preflight.Tag
    $problems = $preflight.Problems
    Write-Host "Release $tag of $($preflight.Repository ?? '?') (git remote $($preflight.Remote ?? '?')), HEAD $($preflight.GitState.Head)"

    $notes = $null
    try { $notes = Get-ChangelogSection -Path (Join-Path $repoRoot 'CHANGELOG.md') -Version $version }
    catch { $problems.Add($_.Exception.Message) }

    $releaseDir = Join-Path $repoRoot 'artifacts' 'release' $version
    $distDir = Join-Path $releaseDir 'dist'
    $buildInfoPath = Get-BuildInfoPath $releaseDir
    $buildInfo = if (Test-Path -LiteralPath $buildInfoPath -PathType Leaf) { Get-Content -LiteralPath $buildInfoPath -Raw | ConvertFrom-Json } else { $null }
    $distFiles = if (Test-Path -LiteralPath $distDir) { @(Get-ChildItem -LiteralPath $distDir -File -Force | ForEach-Object Name) } else { @() }
    $buildProblems = @(Get-BuildInfoProblems -BuildInfo $buildInfo -Version $version -Head $preflight.GitState.Head -DistFiles $distFiles)
    foreach ($problem in $buildProblems) { $problems.Add($problem) }
    if ($buildInfo -and $buildProblems.Count -eq 0) {
        foreach ($problem in @(Get-Sha256SumsProblems (Join-Path $distDir $buildInfo.ChecksumFile))) { $problems.Add($problem) }
        foreach ($file in @($buildInfo.Files)) {
            if ((Get-FileSha256 (Join-Path $distDir $file.Name)) -ne $file.Sha256) { $problems.Add("$($file.Name) changed after the build. Rebuild.") }
        }
    }

    $localFiles = if ($buildInfo -and $buildProblems.Count -eq 0) {
        @($buildInfo.Files | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Sha256 = $_.Sha256 } }) +
        [pscustomobject]@{ Name = $buildInfo.ChecksumFile; Sha256 = Get-FileSha256 (Join-Path $distDir $buildInfo.ChecksumFile) }
    }
    else { @() }

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
    if ($release -and -not $release.isDraft) {
        Write-Problems -Problems @("Release $tag is already published ($($release.url)). Files are uploaded only into a draft release.") -Action 'Upload'
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
        Write-Host 'Release notes:'
        Write-Host ($notes -replace '(?m)^', '  | ')
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
    elseif ($toUpload.Count -gt 0) {
        $arguments = @('release', 'upload', $tag, '--repo', $preflight.Repository)
        if (@($plan | Where-Object Action -EQ 'Replace').Count -gt 0) { $arguments += '--clobber' }
        & gh @arguments @toUpload
        if ($LASTEXITCODE -ne 0) { throw "gh release upload failed with exit code $LASTEXITCODE. Re-run the script: files already uploaded are skipped." }
    }
    else {
        Write-Host 'Nothing to upload: the draft already contains all files.'
    }

    Write-Host "Draft release $tag updated. Upload the build of the other OS, then publish: pwsh scripts/release-publish.ps1" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "Upload FAILED: $_" -ForegroundColor Red
    exit 1
}
finally {
    if ($notesFile -and (Test-Path -LiteralPath $notesFile)) { Remove-Item -LiteralPath $notesFile -Force -ErrorAction SilentlyContinue }
}
