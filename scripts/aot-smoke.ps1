#Requires -Version 7.0
<#
.SYNOPSIS
    Native AOT smoke check: publishes the app, starts the native executable and verifies it keeps running.

.DESCRIPTION
    Catches failures that only show up in the AOT-published binary (e.g. missing compiled XAML),
    which dotnet build / dotnet test cannot detect. Publishes to a temporary directory outside the
    repository; the process and the directory are always cleaned up.

.PARAMETER Runtime
    Runtime identifier (win-x64, win-arm64, osx-arm64, osx-x64). Defaults to the current OS/architecture.

.PARAMETER Seconds
    How long the app must stay alive after start. Default 10.

.EXAMPLE
    pwsh scripts/aot-smoke.ps1
    pwsh scripts/aot-smoke.ps1 -Runtime osx-x64 -Seconds 15
#>
[CmdletBinding()]
param(
    [string] $Runtime,
    [ValidateRange(1, 600)] [int] $Seconds = 10
)

$ErrorActionPreference = 'Stop'

if (-not $Runtime) {
    $os = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { throw 'Unsupported OS; pass -Runtime explicitly.' }
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $Runtime = "$os-$arch"
}

$project = Join-Path $PSScriptRoot '..' 'src' 'PrusaTrayDiagnostic.App'
$publishDir = Join-Path ([System.IO.Path]::GetTempPath()) "prusatray-aot-smoke-$([guid]::NewGuid().ToString('N'))"
$exeName = if ($Runtime.StartsWith('win')) { 'PrusaTrayDiagnostic.exe' } else { 'PrusaTrayDiagnostic' }
$process = $null

try {
    Write-Host "Publishing $Runtime to $publishDir ..."
    dotnet publish $project -c Release -r $Runtime -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    $exe = Join-Path $publishDir $exeName
    if (-not (Test-Path $exe)) { throw "Published executable not found: $exe" }

    $stdout = Join-Path $publishDir 'smoke-stdout.txt'
    $stderr = Join-Path $publishDir 'smoke-stderr.txt'
    Write-Host "Starting $exeName, expecting it to run for $Seconds s ..."
    $process = Start-Process -FilePath $exe -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr

    if ($process.WaitForExit($Seconds * 1000)) {
        $output = (Get-Content $stdout, $stderr -Raw -ErrorAction SilentlyContinue) -join [Environment]::NewLine
        throw "App exited after start with code $($process.ExitCode).$([Environment]::NewLine)$output"
    }

    Write-Host "AOT smoke check PASSED: app is still running after $Seconds s." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "AOT smoke check FAILED: $_" -ForegroundColor Red
    exit 1
}
finally {
    if ($process -and -not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit()
    }
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
