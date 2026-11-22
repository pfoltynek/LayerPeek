<#
.SYNOPSIS
    Publishes the draft GitHub release of the tag vX.Y.Z after the Windows and macOS files are uploaded.

.DESCRIPTION
    The last step of a release (docs/release.md); scripts/release.ps1 runs it (confirmed by typing the version) on the
    machine whose upload completes the draft (T-200). Releases are immutable since 1.0.0 (T-180): once published, no file
    can be changed or added, so only a complete draft is published. Runs the same git checks as
    scripts/release-upload.ps1 (clean tree, HEAD tagged vX.Y.Z matching Directory.Build.props, tag on origin or the -Repo remote, HEAD
    contained in its main branch) and refuses unless:
      - the release exists and is still a draft, and it is the only release of the tag (two drafts, T-200),
      - the checksum file of every OS (SHA256SUMS-windows.txt, SHA256SUMS-macos.txt) is uploaded,
      - every Velopack feed (releases.<rid>.json) and the feed of every released RID is there with its signature
        releases.<rid>.json.sig (T-181),
      - every file listed in the checksum files is uploaded with the listed SHA256 and no other file is,
      - no file of a runtime that must not be released (Uploadable = $false in the runtime table; none since
        win-arm64 became uploadable, 2026-11-16) is uploaded; no feed is expected for such a runtime.
    The checksum files are downloaded into a temporary folder that is always deleted. Versions 0.x stay marked as
    pre-releases.
    After publishing it offers the winget submission (T-059): on Windows, for a stable version whose release has the
    Windows installer, it asks whether to run it now (scripts/release-winget.ps1 does the same on its own); otherwise it
    says why not or how to submit later on Windows.

.PARAMETER Repo
    GitHub repository owner/name of the release. Default: the repository of the git remote origin. Another
    repository (e.g. a trial repository, docs/release.md) needs its own git remote; the tag and main branch checks then
    use that remote, so the checks and the release always concern the same repository.

.PARAMETER DryRun
    Runs every check and prints what would be published; changes nothing on GitHub.

.PARAMETER AllowIncomplete
    No longer supported (T-200): always refused with exit code 1 and the flow "both OS into the draft, then publish";
    releases are immutable since 1.0.0 (T-180), the missing OS could never be added. Kept so that an old command gets
    the explanation.

.EXAMPLE
    pwsh scripts/release-publish.ps1 -DryRun
    pwsh scripts/release-publish.ps1
    pwsh scripts/release-publish.ps1 -Repo pfoltynek/LayerPeek-release-trial -DryRun
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
    if ($AllowIncomplete) {
        # T-200: refused before anything is checked or asked on GitHub.
        Write-Problems -Problems @(Get-RetiredSwitchProblem -Switch 'AllowIncomplete') -Action 'Publish'
        exit 1
    }
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

    $expectedOs = @(Get-ExpectedReleaseOs -Config $config)
    $problems = [System.Collections.Generic.List[string]]::new()
    foreach ($problem in @(Get-PublishProblems -Release $release -Tag $tag -ExpectedOs $expectedOs -Checksums $checksums -Config $config)) { $problems.Add($problem) }
    foreach ($problem in @(Get-DuplicateReleaseProblems -Tag $tag -Releases @(Get-GitHubTagReleases -Repository $preflight.Repository -Tag $tag))) { $problems.Add($problem) }
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
    # T-059: the optional winget submission (Windows only, asks first; never fails the publish).
    Invoke-WingetReleaseOffer -RepoRoot $repoRoot -Version $preflight.Version -Repository $preflight.Repository -AssetNames @($release.assets | ForEach-Object name)
    exit 0
}
catch {
    Write-Host "Publish FAILED: $_" -ForegroundColor Red
    exit 1
}
finally {
    if ($downloadDir -and (Test-Path -LiteralPath $downloadDir)) { Remove-Item -LiteralPath $downloadDir -Recurse -Force -ErrorAction SilentlyContinue }
}
