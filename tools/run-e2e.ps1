#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the on-demand Windows end-to-end tests of the real app (T-044), including the tests that temporarily change
    Windows settings (T-065), and restores those settings after an interrupted run.

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

    System changes (T-065, T-066): tests with the trait SystemChanges temporarily change Windows settings (light/dark
    theme, Do Not Disturb, the test tray icon on the taskbar, the display scale of a monitor, a switched-off second
    monitor; later tasks add toasts and Credential Manager entries). Each such test carries one trait value per kind of
    change (SystemChanges=Theme|DoNotDisturb|TrayPromotion|DisplayScale|MonitorOff|Toasts|Credentials|TestProbe; texts in
    tools/RunE2E.SystemChanges.psm1). They run by default: when the selected tests contain one, the script first shows a
    warning listing exactly the kinds the selected tests change and counts down 5 s (Ctrl+C aborts, nothing has changed
    then); only then it sets PTD_E2E_SYSTEM_CHANGES=1 and PTD_E2E_SYSTEM_CHANGE_KINDS=<the listed kinds>. A change of a
    kind the warning did not list is refused by the tests.
    The original state of every change is written to %LOCALAPPDATA%\PrusaTrayDiagnostic.E2E.Restore before the change
    and restored afterwards - also after a failed test, by the next run's start, by the script's own cleanup (even after
    Ctrl+C), and by a hidden watchdog process (this script with -RestoreOnly -AfterProcess) that outlives a killed test
    process. -NoSystemChanges skips those tests without the warning. Known traces Windows may keep: the test app
    PrusaTrayDiagnostic.E2E in the notification database / Settings > Notifications (T-068, accepted).

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
    own). The warning about system changes at the start covers this second pass too.

.PARAMETER NoSystemChanges
    Do not change any Windows setting: no warning, no countdown; the tests with the trait SystemChanges are skipped.

.PARAMETER RestoreOnly
    Only restore system changes left by an interrupted run (e.g. after a restart of the computer in the middle of a run)
    and print what was restored; no app is built and no test runs. Without pending changes nothing happens. Needs only
    the end-to-end test project (built when missing). See docs/troubleshooting.md.

.PARAMETER AfterProcess
    With -RestoreOnly: first wait until the process with this id ends (the watchdog of a test run, started by the
    tests themselves; its output goes to system-changes-watchdog.log in the results folder).

.EXAMPLE
    ./tools/run-e2e.ps1
    ./tools/run-e2e.ps1 -Class '*SettingsAndDiagnosticsTests'
    ./tools/run-e2e.ps1 -NoSystemChanges
    ./tools/run-e2e.ps1 -RestoreOnly
#>
param(
    [string] $Class,
    [string] $Method,
    [switch] $PublishAot,
    [switch] $NoSystemChanges,
    [switch] $RestoreOnly,
    [int] $AfterProcess
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) {
    throw 'The end-to-end tests of the real app run on Windows only.'
}

$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')))
$app = Join-Path $root 'src/PrusaTrayDiagnostic.App'
$tests = Join-Path $root 'tests/PrusaTrayDiagnostic.App.E2E.Tests'
$testsExe = Join-Path $tests 'bin/Debug/net11.0-windows/PrusaTrayDiagnostic.App.E2E.Tests.exe'
# Same folder as SystemChanges.JournalDirectory in the tests (machine-wide: the system state is shared by all worktrees).
$journal = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PrusaTrayDiagnostic.E2E.Restore'

# Same id as tests/Shared/TestWorktree.cs: first 4 bytes of SHA-256 of the upper-case worktree path.
$hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($root.ToUpperInvariant()))
$id = [Convert]::ToHexString($hash, 0, 4).ToLowerInvariant()
$results = Join-Path ([IO.Path]::GetTempPath()) "PrusaTrayDiagnostic.App.E2E.Results.$id"
$script:logFile = $null
Import-Module (Join-Path $PSScriptRoot 'RunE2E.SystemChanges.psm1') -Force

function Write-Log([string] $Text, [ConsoleColor] $Color = [ConsoleColor]::Gray) {
    Write-Host $Text -ForegroundColor $Color
    if ($script:logFile) {
        Add-Content -LiteralPath $script:logFile -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $Text"
    }
}

function Get-PendingSystemChanges {
    if (Test-Path -LiteralPath $journal) { @(Get-ChildItem -LiteralPath $journal -Filter '*.json' -File) } else { @() }
}

# Restores pending system changes through the test project (SystemChangeRestore); returns $true when nothing is left
# that could not be restored. Without pending changes nothing is started.
function Invoke-SystemChangeRestore([string] $Reason) {
    if (-not (Test-Path -LiteralPath $journal)) {
        Write-Log "System changes ($Reason): nothing pending."
        return $true
    }

    $pending = Get-PendingSystemChanges
    Write-Log "System changes ($Reason): restoring $($pending.Count) pending change(s) from $journal ..." Yellow
    if (-not (Test-Path -LiteralPath $testsExe)) {
        dotnet build $tests | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Log 'Building the end-to-end tests for the restore failed.' Red
            return $false
        }
    }

    $report = [IO.Path]::GetTempFileName()
    $previous = @{}
    foreach ($name in 'PTD_E2E', 'PTD_E2E_SYSTEM_CHANGES', 'PTD_E2E_SYSTEM_CHANGE_KINDS', 'PTD_E2E_RESTORE', 'PTD_E2E_RESTORE_REPORT', 'PTD_E2E_WATCHDOG') {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name)
    }
    try {
        [Environment]::SetEnvironmentVariable('PTD_E2E', $null)
        [Environment]::SetEnvironmentVariable('PTD_E2E_SYSTEM_CHANGES', $null)
        [Environment]::SetEnvironmentVariable('PTD_E2E_SYSTEM_CHANGE_KINDS', $null)
        [Environment]::SetEnvironmentVariable('PTD_E2E_RESTORE', '1')
        [Environment]::SetEnvironmentVariable('PTD_E2E_RESTORE_REPORT', $report)
        # The restore makes no change of its own, so it needs no watchdog.
        [Environment]::SetEnvironmentVariable('PTD_E2E_WATCHDOG', '0')
        $output = & $testsExe -method 'PrusaTrayDiagnostic.App.E2E.Tests.SystemChangeRestore.RestorePendingSystemChanges' -noColor -noLogo 2>&1
        $exitCode = $LASTEXITCODE
        $text = if ((Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).Length -gt 0) { Get-Content -LiteralPath $report -Raw } else { $null }
        if ($text) {
            Write-Log $text.TrimEnd() ($(if ($exitCode -eq 0) { [ConsoleColor]::Green } else { [ConsoleColor]::Red }))
        }
        if ($exitCode -ne 0 -or -not $text) {
            Write-Log (($output | Out-String).TrimEnd()) Red
            Write-Log 'Some system changes were NOT restored. What to do by hand: docs/troubleshooting.md (section on an interrupted E2E run); then run tools/run-e2e.ps1 -RestoreOnly again.' Red
            return $false
        }
        return $true
    }
    finally {
        foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
        Remove-Item -LiteralPath $report -ErrorAction SilentlyContinue
    }
}

# The warning lists only the kinds of change of the selected tests (T-066; texts in RunE2E.SystemChanges.psm1).
function Show-SystemChangesWarning([string[]] $Kinds) {
    foreach ($line in (Get-SystemChangesWarningLines -Kinds $Kinds)) { Write-Host $line -ForegroundColor Yellow }
    for ($i = 5; $i -ge 1; $i--) {
        Write-Host -NoNewline "`r  Starting in $i s   (Ctrl+C = abort, nothing has changed yet) " -ForegroundColor Yellow
        Start-Sleep -Seconds 1
    }
    Write-Host ''
    Write-Host ''
}

if ($RestoreOnly) {
    if ($AfterProcess) {
        if (Test-Path -LiteralPath $results) {
            $script:logFile = Join-Path $results 'system-changes-watchdog.log'
        }
        Write-Log "Watchdog: waiting for the test process $AfterProcess to end."
        $process = Get-Process -Id $AfterProcess -ErrorAction SilentlyContinue
        if ($process) { $process.WaitForExit() }
        Write-Log "Watchdog: the test process $AfterProcess ended."
    }
    $ok = Invoke-SystemChangeRestore 'restore only'
    exit $(if ($ok) { 0 } else { 1 })
}

# Nothing inherited from the shell may confirm system changes without the warning below, switch the watchdog off or
# make the tests act as a child / restore process (which skip the locks and the start cleanup).
Remove-Item Env:PTD_E2E_SYSTEM_CHANGES, Env:PTD_E2E_SYSTEM_CHANGE_KINDS, Env:PTD_E2E_WATCHDOG, Env:PTD_E2E_CHILD, Env:PTD_E2E_RESTORE, Env:PTD_E2E_RESTORE_REPORT -ErrorAction SilentlyContinue

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

# Leftovers of an interrupted run go back before anything else (the tests' start does the same, under the desktop lock).
if (-not (Invoke-SystemChangeRestore 'before the run')) {
    throw 'System changes of an earlier run could not be restored; fix them first (docs/troubleshooting.md).'
}

# What do the selected tests change? Every such test has one trait SystemChanges=<kind> per kind of change (T-066); the
# trait filter combines with -class / -method as AND.
$systemChanges = @()
if (-not $NoSystemChanges) {
    $listed = & $testsExe -list full/json -trait 'SystemChanges=*' @filters 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Listing the tests failed: $($listed | Out-String)" }
    $systemChanges = @(Get-SelectedSystemChanges -ListOutput @($listed | ForEach-Object { "$_" }))
}

$resultsAot = "$results-aot"
$previousApp = $env:PTD_E2E_APP
$exitCode = 1
try {
    if ($NoSystemChanges) {
        Write-Host 'System changes: off (-NoSystemChanges); the tests that change Windows settings are skipped.' -ForegroundColor Cyan
    }
    elseif ($systemChanges.Count -eq 0) {
        Write-Host 'System changes: none of the selected tests changes Windows settings.' -ForegroundColor Cyan
    }
    else {
        Show-SystemChangesWarning -Kinds $systemChanges
        # Only after the countdown: Ctrl+C above leaves the tests unstarted and the system untouched. The tests refuse
        # any change of a kind the warning did not list.
        $env:PTD_E2E_SYSTEM_CHANGE_KINDS = $systemChanges -join ','
        $env:PTD_E2E_SYSTEM_CHANGES = '1'
    }

    $env:PTD_E2E = '1'
    $env:PTD_E2E_RESULTS = $results
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
    Remove-Item Env:PTD_E2E, Env:PTD_E2E_RESULTS, Env:PTD_E2E_SYSTEM_CHANGES, Env:PTD_E2E_SYSTEM_CHANGE_KINDS -ErrorAction SilentlyContinue
    if ($null -eq $previousApp) { Remove-Item Env:PTD_E2E_APP -ErrorAction SilentlyContinue } else { $env:PTD_E2E_APP = $previousApp }
    # Also after Ctrl+C or a crashed test process (entries of a test process that still ends are left to its watchdog).
    if (-not (Invoke-SystemChangeRestore 'after the run')) { $exitCode = 1 }
}

Write-Host ''
Write-Host "Screenshots and app logs: $results"
if ($PublishAot) { Write-Host "Native AOT log test: $resultsAot" }
exit $exitCode
