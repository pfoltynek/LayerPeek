<#
.SYNOPSIS
    Publishes the draft GitHub release of the tag vX.Y.Z after the Windows and macOS files (or, with -AllowIncomplete, the
    files of one OS) are uploaded.

.DESCRIPTION
    The separate, explicit last step of a release (docs/release.md). Runs the same git checks as
    scripts/release-upload.ps1 (clean tree, HEAD tagged vX.Y.Z matching Directory.Build.props, tag on origin or the -Repo remote, HEAD
    contained in its main branch) and refuses unless:
      - the release exists and is still a draft,
      - the checksum file of every OS (SHA256SUMS-windows.txt, SHA256SUMS-macos.txt) is uploaded,
      - every file listed in the checksum files is uploaded with the listed SHA256 and no other file is,
      - no file of a runtime that must not be released (win-arm64 until T-057) is uploaded.
    The checksum files are downloaded into a temporary folder that is always deleted. Versions 0.x stay marked as
    pre-releases.

.PARAMETER Repo
    GitHub repository owner/name of the release. Default: the repository of the git remote origin. Another
    repository (e.g. a trial repository, docs/release.md) needs its own git remote; the tag and main branch checks then
    use that remote, so the checks and the release always concern the same repository.

.PARAMETER DryRun
    Runs every check and prints what would be published; changes nothing on GitHub.

.PARAMETER AllowIncomplete
    Publishes even when the files of an OS are missing (e.g. the other OS follows later on another machine). Every
    uploaded file is still checked. The files of the missing OS are added to the published release later with
    scripts/release-upload.ps1 -AddToPublished (T-113, docs/release.md).

.EXAMPLE
    pwsh scripts/release-publish.ps1 -DryRun
    pwsh scripts/release-publish.ps1
    pwsh scripts/release-publish.ps1 -Repo pfoltynek/LayerPeek-release-trial -AllowIncomplete
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [string] $Repo,
    [switch] $DryRun,
    [switch] $AllowIncomplete
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools' 'ReleaseTools.psm1') -Force

# Physical path: dotnet restore breaks on symlinked paths (Resolve-PhysicalPath).
$repoRoot = Resolve-PhysicalPath (Join-Path $PSScriptRoot '..')
$downloadDir = $null

try {
    $preflight = Get-ReleasePreflight -RepoRoot $repoRoot -Repository $Repo
    $tag = $preflight.Tag
    Write-Host "Release $tag of $($preflight.Repository ?? '?') (git remote $($preflight.Remote ?? '?')), HEAD $($preflight.GitState.Head)"
    if ($preflight.Problems.Count -gt 0) {
        Write-Problems -Problems $preflight.Problems -Action 'Publish'
        exit 1
    }

    $config = Get-ReleaseRuntimeConfig
    $release = Get-GitHubRelease -Repository $preflight.Repository -Tag $tag
    $downloadDir = Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-release-sums-$([guid]::NewGuid().ToString('N'))"
    $checksums = Get-ReleaseChecksums -Release $release -Repository $preflight.Repository -Tag $tag -Directory $downloadDir

    # @(...) around the whole if: an if whose branch outputs an empty array yields $null, which -ExpectedOs refuses.
    $expectedOs = @(if (-not $AllowIncomplete) { $config.Values | Where-Object Uploadable | ForEach-Object Os | Sort-Object -Unique })
    $problems = [System.Collections.Generic.List[string]]::new()
    foreach ($problem in @(Get-PublishProblems -Release $release -Tag $tag -ExpectedOs $expectedOs -Checksums $checksums -Config $config)) { $problems.Add($problem) }
    if ($problems.Count -gt 0) {
        Write-Problems -Problems $problems -Action 'Publish'
        exit 1
    }

    $prerelease = Test-PrereleaseVersion $preflight.Version
    $prefix = if ($DryRun) { 'Would publish' } else { 'Publishing' }
    Write-Host "$prefix $tag (pre-release: $($prerelease ? 'yes' : 'no')) with $(@($release.assets).Count) files: $($release.url)"
    foreach ($asset in @($release.assets)) { Write-Host "  $($asset.name)" }
    if ($DryRun) {
        Write-Host 'Dry run: nothing was changed on GitHub.' -ForegroundColor Green
        exit 0
    }

    & gh release edit $tag --repo $preflight.Repository --draft=false "--prerelease=$($prerelease.ToString().ToLowerInvariant())"
    if ($LASTEXITCODE -ne 0) { throw "gh release edit failed with exit code $LASTEXITCODE." }
    Write-Host "Release $tag published." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "Publish FAILED: $_" -ForegroundColor Red
    exit 1
}
finally {
    if ($downloadDir -and (Test-Path -LiteralPath $downloadDir)) { Remove-Item -LiteralPath $downloadDir -Recurse -Force -ErrorAction SilentlyContinue }
}
