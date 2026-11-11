<#
.SYNOPSIS
    Submits a published release to winget (microsoft/winget-pkgs) as package 3DPrint.LayerPeek (T-059). Windows only.

.DESCRIPTION
    The optional last step of a release (docs/release.md, "Publikace do winget"). release-publish.ps1 and
    release-upload.ps1 -AddToPublished offer it themselves once the release has the Windows installer; this script runs
    it on its own, e.g. later on a Windows machine.

    Refuses pre-releases (0.x; winget gets stable versions from 1.0.0 on), releases of another repository than
    pfoltynek/LayerPeek, a release that is missing, still a draft or has no LayerPeek-X.Y.Z-win-x64-Setup.exe with its
    SHA256, and an open pull request for the package that is in the way. The first version (the package is not in
    microsoft/winget-pkgs yet) is made from the reviewed templates in build/winget, checked with winget validate and sent
    with wingetcreate submit; every later version with wingetcreate update --submit (new URL, SHA256, version, release
    notes URL and date; the other fields stay as in the previous manifest). Asks before the pull request is opened.

    The GitHub token is read with gh auth token only after the confirmation and reaches wingetcreate only through
    WINGET_CREATE_GITHUB_TOKEN of this process while it runs: never as an argument (process list, history), never
    printed (wingetcreate's output is redacted), never stored. Before the plan the scopes of the gh token are checked
    (gh api; wingetcreate needs public_repo, which a gh OAuth login has): a fine-grained or unscoped token (for example
    GH_TOKEN of T-180) is refused with the steps of a temporary login (gh auth login --web, afterwards gh auth logout).
    wingetcreate's telemetry is switched off in its settings.json before it runs (other settings stay).

.PARAMETER Version
    The released version X.Y.Z. Default: <Version> of Directory.Build.props.

.PARAMETER Repo
    GitHub repository owner/name of the release; only pfoltynek/LayerPeek is submitted (a trial repository is refused).

.PARAMETER OutputDirectory
    Only writes the manifests of the version into this empty or new folder (any OS): for winget validate and
    winget install --manifest on Windows before the first submission, or for review. Submits nothing.

.PARAMETER DryRun
    Checks everything and prints what would be submitted; submits nothing.

.EXAMPLE
    pwsh scripts/release-winget.ps1 -Version 1.0.0 -OutputDirectory $env:TEMP\winget-1.0.0
    pwsh scripts/release-winget.ps1 -Version 1.0.0 -DryRun
    pwsh scripts/release-winget.ps1 -Version 1.0.0
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Repo,
    [string] $OutputDirectory,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools' 'ReleaseTools.psm1') -Force

$repoRoot = Resolve-PhysicalPath (Join-Path $PSScriptRoot '..')

try {
    if (-not $Version) { $Version = Get-SourceVersion -PropsPath (Join-Path $repoRoot 'Directory.Build.props') }
    $hostOs = if ($IsWindows) { 'windows' } elseif ($IsMacOS) { 'macos' } else { 'linux' }
    $done = Invoke-WingetRelease -RepoRoot $repoRoot -Version $Version -Repository $Repo -HostOs $hostOs -OutputDirectory $OutputDirectory -DryRun:$DryRun
    exit ($done ? 0 : 1)
}
catch {
    Write-Host "winget FAILED: $_" -ForegroundColor Red
    exit 1
}
