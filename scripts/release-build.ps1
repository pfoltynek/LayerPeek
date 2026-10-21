#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the release files of this machine's OS: build + tests, Native AOT publish per RID, AOT smoke test,
    archives and SHA256 checksums. Uploads nothing (see scripts/release-upload.ps1).

.DESCRIPTION
    Native AOT does not compile across operating systems: Windows RIDs are built on Windows, macOS RIDs on a Mac.
    The version comes from <Version> in the root Directory.Build.props.

    Output (artifacts/ is gitignored), recreated on every run for the version:
        artifacts/release/<version>/publish/<rid>/   Native AOT publish folder
        artifacts/release/<version>/dist/            files to upload: archives (and the outputs of later packaging
                                                     steps, e.g. installers) + SHA256SUMS-<os>.txt
        artifacts/release/<version>/build-info.json  build marker (version, commit, clean tree, files) that
                                                     release-upload.ps1 checks so that no older build is uploaded
    Build all RIDs of this OS in one run: a new run deletes the previous output of the same version.

    The script may run on any commit (e.g. to try the packaging); only a build of the tagged release commit from a
    clean working tree can be uploaded.

.PARAMETER Runtime
    RIDs to build. Default: the default RIDs of this OS (Windows: win-x64, macOS: osx-arm64). win-arm64 must be
    requested explicitly and its files are not uploaded until T-057 is done. RIDs of another OS are refused.

.PARAMETER SmokeSeconds
    How long the published app must keep running in the AOT smoke test. Default 10.

.EXAMPLE
    pwsh scripts/release-build.ps1
    pwsh scripts/release-build.ps1 -Runtime win-x64, win-arm64
#>
[CmdletBinding()]
param(
    [string[]] $Runtime,
    [ValidateRange(1, 600)] [int] $SmokeSeconds = 10
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools' 'ReleaseTools.psm1') -Force

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
$solution = Join-Path $repoRoot 'PrusaTrayDiagnostic.slnx'
$appProject = Join-Path $repoRoot 'src' 'PrusaTrayDiagnostic.App' 'PrusaTrayDiagnostic.App.csproj'
$smokeScript = Join-Path $PSScriptRoot 'aot-smoke.ps1'

# Packaging steps, run for every RID in this order. Each step gets a context object (Version, Runtime, Os,
# PublishDirectory, OutputDirectory), creates its files directly in OutputDirectory (dist/) and returns their paths.
# Returned files go automatically into the checksum file, the build marker and the upload.
# Installers are added here as further steps (T-017: Windows installer, T-019: macOS .app + .dmg); the build and
# publish part above them does not change. Os limits a step to RIDs of that OS.
$packagingSteps = @(
    [pscustomobject]@{
        Name   = 'archive'
        Os     = @('windows', 'macos')
        Invoke = {
            param($Context)
            $archive = Join-Path $Context.OutputDirectory (Get-ArchiveFileName -Version $Context.Version -Runtime $Context.Runtime)
            New-ReleaseArchive -SourceDirectory $Context.PublishDirectory -Destination $archive
        }
    }
)

function Invoke-Native {
    param([Parameter(Mandatory)] [string] $Description, [Parameter(Mandatory)] [scriptblock] $Command)
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

function Assert-NativeToolchain {
    # Fails before the long build when the AOT toolchain of a requested RID is missing (docs/prerequisites.md).
    param([Parameter(Mandatory)] [string[]] $Runtimes, [Parameter(Mandatory)] [string] $HostOs)
    if ($HostOs -eq 'macos') {
        $null = & xcrun --find clang 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'Xcode Command Line Tools are missing (xcode-select --install). See docs/prerequisites.md.' }
        return
    }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio' 'Installer' 'vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw "Visual Studio Build Tools are missing ($vswhere not found). See docs/prerequisites.md." }
    $components = @{
        'win-x64'   = 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'
        'win-arm64' = 'Microsoft.VisualStudio.Component.VC.Tools.ARM64'
    }
    foreach ($rid in $Runtimes) {
        $component = $components[$rid]
        $found = @(& $vswhere -products * -requires $component -property installationPath)
        if ($LASTEXITCODE -ne 0 -or -not ($found | Where-Object { $_ })) {
            throw "The MSVC component $component needed for the $rid AOT publish is not installed. See docs/prerequisites.md."
        }
    }
}

function Get-FolderSnapshot {
    # Relative path, size and write time of every file: proves that the smoke run left the publish folder unchanged.
    param([Parameter(Mandatory)] [string] $Path)
    (Get-ChildItem -LiteralPath $Path -Recurse -Force -File |
        ForEach-Object { "$([System.IO.Path]::GetRelativePath($Path, $_.FullName))|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" } |
        Sort-Object) -join "`n"
}

try {
    $hostOs = Get-HostOs
    $hostArchitecture = Get-HostArchitecture
    $config = Get-ReleaseRuntimeConfig
    $runtimes = @(Resolve-ReleaseRuntimes -Requested $Runtime -HostOs $hostOs)
    $version = Get-SourceVersion (Join-Path $repoRoot 'Directory.Build.props')
    $head = Get-GitHead $repoRoot
    $dirty = @(Get-GitStatusLines $repoRoot).Count -gt 0

    Write-Host "Release build $version ($hostOs, $($runtimes -join ', ')) of commit $head"
    if ($dirty) {
        Write-Host 'The working tree has uncommitted or untracked files: this build cannot be uploaded.' -ForegroundColor Yellow
    }
    foreach ($rid in $runtimes) {
        if (-not $config[$rid].Uploadable) {
            Write-Host "$rid is built for local verification only and cannot be uploaded: $($config[$rid].BlockedReason)." -ForegroundColor Yellow
        }
    }

    Assert-NativeToolchain -Runtimes $runtimes -HostOs $hostOs

    $releaseDir = Join-Path $repoRoot 'artifacts' 'release' $version
    $publishRoot = Join-Path $releaseDir 'publish'
    $distDir = Join-Path $releaseDir 'dist'
    if (Test-Path -LiteralPath $releaseDir) {
        Write-Host "Deleting the previous output in $releaseDir"
        Remove-Item -LiteralPath $releaseDir -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Path $publishRoot, $distDir

    # -warnaserror: any MSBuild warning (including trim/AOT warnings of the ILC step) stops the release.
    Invoke-Native 'dotnet build (Release)' { dotnet build $solution -c Release -warnaserror }
    Invoke-Native 'dotnet test (Release)' { dotnet test --solution $solution -c Release --no-build }

    $smoke = [ordered]@{}
    foreach ($rid in $runtimes) {
        $publishDir = Join-Path $publishRoot $rid
        Invoke-Native "dotnet publish $rid (Native AOT)" { dotnet publish $appProject -c Release -r $rid -o $publishDir -warnaserror }

        $exe = Join-Path $publishDir ($hostOs -eq 'windows' ? 'PrusaTrayDiagnostic.exe' : 'PrusaTrayDiagnostic')
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published executable not found: $exe" }
        if ($hostOs -eq 'windows') {
            # ProductVersion is the informational version "X.Y.Z+<commit>"; it must come from Directory.Build.props.
            $productVersion = ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -split '\+')[0]
            if ($productVersion -ne $version) { throw "$exe has product version '$productVersion', expected $version." }
            Write-Host "Product version of the exe: $productVersion"
        }

        if (Test-RuntimeRunnable -Runtime $rid -HostOs $hostOs -HostArchitecture $hostArchitecture) {
            $before = Get-FolderSnapshot $publishDir
            Invoke-Native "AOT smoke test $rid" { & ([Environment]::ProcessPath) -NoProfile -File $smokeScript -Runtime $rid -PublishDir $publishDir -Seconds $SmokeSeconds }
            if ((Get-FolderSnapshot $publishDir) -ne $before) { throw "The smoke run changed the publish folder $publishDir; it must not be packaged." }
            $smoke[$rid] = 'passed'
        }
        else {
            $smoke[$rid] = "not verified by running (this machine is $hostOs-$hostArchitecture)"
        }
    }

    $files = [System.Collections.Generic.List[object]]::new()
    foreach ($rid in $runtimes) {
        $context = [pscustomobject]@{
            Version          = $version
            Runtime          = $rid
            Os               = $hostOs
            PublishDirectory = Join-Path $publishRoot $rid
            OutputDirectory  = $distDir
        }
        foreach ($step in @($packagingSteps | Where-Object { $_.Os -contains $hostOs })) {
            Write-Host "==> Packaging $rid ($($step.Name))" -ForegroundColor Cyan
            foreach ($path in @(& $step.Invoke $context)) {
                $item = Get-Item -LiteralPath $path
                if ($item.DirectoryName -ne (Get-Item -LiteralPath $distDir).FullName) { throw "Packaging step '$($step.Name)' created $path outside $distDir." }
                if ($files | Where-Object { $_.Name -ceq $item.Name }) { throw "Packaging step '$($step.Name)' returned $($item.Name) twice." }
                $files.Add([pscustomobject]@{ Name = $item.Name; Runtime = $rid; Step = $step.Name; Sha256 = Get-FileSha256 $item.FullName })
            }
        }
    }

    $checksumFile = Get-ChecksumFileName $hostOs
    $null = Write-Sha256Sums -Directory $distDir -FileName $checksumFile -Files @($files | ForEach-Object Name)
    $sumsProblems = @(Get-Sha256SumsProblems (Join-Path $distDir $checksumFile))
    if ($sumsProblems.Count -gt 0) { throw "Checksum verification failed: $($sumsProblems -join ' ')" }

    # Re-checked at the end: a change during the build also makes it unreleasable.
    $dirty = $dirty -or @(Get-GitStatusLines $repoRoot).Count -gt 0 -or (Get-GitHead $repoRoot) -ne $head
    $buildInfo = [ordered]@{
        Version      = $version
        Commit       = $head
        Dirty        = $dirty
        Os           = $hostOs
        Runtimes     = $runtimes
        ChecksumFile = $checksumFile
        Files        = $files.ToArray()
        Smoke        = $smoke
        CreatedUtc   = [DateTime]::UtcNow.ToString('o')
    }
    $buildInfo | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Get-BuildInfoPath $releaseDir) -Encoding utf8NoBOM

    Write-Host ''
    Write-Host "Release files in $($distDir):" -ForegroundColor Green
    foreach ($file in @($files) + [pscustomobject]@{ Name = $checksumFile }) {
        $length = (Get-Item -LiteralPath (Join-Path $distDir $file.Name)).Length
        Write-Host ("  {0,-48} {1,10:N1} MB" -f $file.Name, ($length / 1MB))
    }
    foreach ($rid in $smoke.Keys) { Write-Host "  AOT smoke test ${rid}: $($smoke[$rid])" }
    if ($dirty) { Write-Host 'Built from a dirty working tree: release-upload.ps1 will refuse this output.' -ForegroundColor Yellow }
    Write-Host 'Next step: pwsh scripts/release-upload.ps1 -DryRun' -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "Release build FAILED: $_" -ForegroundColor Red
    exit 1
}
