#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the on-demand Windows end-to-end tests of the real app (T-044).

.DESCRIPTION
    Builds the Debug app (the exe under test, with the Debug-only test mode) and the Release assembly (checked for the
    absence of every test switch), optionally publishes the Native AOT exe (also checked), then runs
    tests/PrusaTrayDiagnostic.App.E2E.Tests with PTD_E2E=1. A normal `dotnet test` skips these tests.

    The started apps run in the isolated test mode: their own data folder in %TEMP% (reserved per worktree, removed after
    the run and swept after a crash), their own instance names, no Credential Manager, no toasts, no Start menu shortcut.
    The user's running PrusaTrayDiagnostic is never touched. The crash tests of T-046 end two test apps on purpose:
    Windows records each crash as a ".NET Runtime" error in the Application event log (accepted; a dump or error report
    Windows may keep is removed again). Windows appear on the desktop for a few minutes; do not use the mouse or keyboard
    meanwhile. Runs of different worktrees share the one desktop, so they take turns: a run waits
    (up to 30 minutes) while another worktree's run holds the machine-wide lock %TEMP%\PrusaTrayDiagnostic.App.E2E.Desktop.lock.

    Screenshots of every checked state and the log of every started app go to the results folder, which is printed at
    the end (replaced by the next run).

.PARAMETER Class
    Only test classes matching this xUnit filter (e.g. '*CameraWindowTests').

.PARAMETER Method
    Only test methods matching this xUnit filter (e.g. '*NoPrinter*').

.PARAMETER PublishAot
    Also publish the Native AOT exe (win-x64, needs the C++ build tools) and check it for test switches, and publish the
    Debug configuration as Native AOT too (it keeps the test mode) and run the log and crash tests against it, whatever
    -Class and -Method select (T-064, T-046: stack traces with method names in AOT builds; results in a folder of their
    own).

.EXAMPLE
    ./tools/run-e2e.ps1
    ./tools/run-e2e.ps1 -Class '*SettingsAndDiagnosticsTests'
#>
param(
    [string] $Class,
    [string] $Method,
    [switch] $PublishAot
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) {
    throw 'The end-to-end tests of the real app run on Windows only.'
}

$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')))
$app = Join-Path $root 'src/PrusaTrayDiagnostic.App'
$tests = Join-Path $root 'tests/PrusaTrayDiagnostic.App.E2E.Tests'

# Same id as tests/Shared/TestWorktree.cs: first 4 bytes of SHA-256 of the upper-case worktree path.
$hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($root.ToUpperInvariant()))
$id = [Convert]::ToHexString($hash, 0, 4).ToLowerInvariant()
$results = Join-Path ([IO.Path]::GetTempPath()) "PrusaTrayDiagnostic.App.E2E.Results.$id"

dotnet build $app -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Building the Debug app failed.' }
dotnet build $app -c Release
if ($LASTEXITCODE -ne 0) { throw 'Building the Release app failed.' }
$aotDebug = Join-Path $app 'bin/Debug/net11.0/win-x64/publish'
if ($PublishAot) {
    dotnet publish $app -c Release -r win-x64
    if ($LASTEXITCODE -ne 0) { throw 'The Native AOT publish failed.' }
    dotnet publish $app -c Debug -r win-x64 -o $aotDebug
    if ($LASTEXITCODE -ne 0) { throw 'The Native AOT publish of the Debug app failed.' }
}

dotnet build $tests
if ($LASTEXITCODE -ne 0) { throw 'Building the end-to-end tests failed.' }

$filters = @()
if ($Class) { $filters += @('-class', $Class) }
if ($Method) { $filters += @('-method', $Method) }

$resultsAot = "$results-aot"
$previousApp = $env:PTD_E2E_APP
$env:PTD_E2E = '1'
$env:PTD_E2E_RESULTS = $results
try {
    dotnet run --project $tests --no-build -- @filters
    $exitCode = $LASTEXITCODE
    if ($PublishAot -and $exitCode -eq 0) {
        # The log in a Native AOT build: the log and crash tests (whatever -Class/-Method say) against the Debug AOT exe
        # (T-064, T-046), with its own results folder, so that the results of the run above are kept.
        Write-Host ''
        Write-Host 'Log and crash tests against the Native AOT exe of the Debug app:'
        $env:PTD_E2E_APP = Join-Path $aotDebug 'PrusaTrayDiagnostic.exe'
        $env:PTD_E2E_RESULTS = $resultsAot
        dotnet run --project $tests --no-build -- -method '*LogHasRecordsAndAnExceptionWithItsStackTrace*' -method '*CrashLoggingTests*'
        $exitCode = $LASTEXITCODE
    }
}
finally {
    Remove-Item Env:PTD_E2E, Env:PTD_E2E_RESULTS -ErrorAction SilentlyContinue
    if ($null -eq $previousApp) { Remove-Item Env:PTD_E2E_APP -ErrorAction SilentlyContinue } else { $env:PTD_E2E_APP = $previousApp }
}

Write-Host ''
Write-Host "Screenshots and app logs: $results"
if ($PublishAot) { Write-Host "Native AOT log test: $resultsAot" }
exit $exitCode
