#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the on-demand Windows end-to-end tests of the real app (T-044), including the tests that temporarily change
    Windows settings (T-065), and restores those settings after an interrupted run.

.DESCRIPTION
    Builds the Debug app (the exe under test, with the Debug-only test mode) and the Release assembly (checked for the
    absence of every test switch), optionally publishes the Native AOT exe (also checked), then runs
    tests/LayerPeek.App.E2E.Tests with LAYERPEEK_E2E=1. A normal `dotnet test` skips these tests.

    The started apps run in the isolated test mode: their own data folder in %TEMP% (reserved per worktree, removed after
    the run and swept after a crash), their own instance names, no Credential Manager, no toasts, no Start menu shortcut
    (except the tests that opt in, which are system changes with the warning below: silent toasts of the test app
    LayerPeek.E2E with its own Start menu shortcut "LayerPeek E2E" and its own COM toast activator
    under HKCU\Software\Classes\CLSID, T-067/T-068; session-only Credential Manager entries LayerPeek.E2E/...,
    T-073; all are removed after the test, also after a crash).
    The user's running LayerPeek is never touched. The crash tests of T-046 end two test apps on purpose:
    Windows records each crash as a ".NET Runtime" error in the Application event log (accepted; a dump or error report
    Windows may keep is removed again). Windows appear on the desktop for a few minutes; do not use the mouse or keyboard
    meanwhile. Runs of different worktrees share the one desktop, so they take turns: a run waits
    (up to 30 minutes) while another worktree's run holds the machine-wide lock %TEMP%\LayerPeek.App.E2E.Desktop.lock.

    System changes (T-065, T-066): tests with the trait SystemChanges temporarily change Windows settings (light/dark
    theme, Do Not Disturb, the test tray icon on the taskbar, the display scale of a monitor, a switched-off second
    monitor, silent toasts of the test app with the notifications of Windows and of the test app switched on when they
    are off (T-068), session-only Credential Manager entries LayerPeek.E2E/... (T-073)). Each such test
    carries one trait value per kind of change (SystemChanges=Theme|DoNotDisturb|TrayPromotion|DisplayScale|MonitorOff|Toasts|Credentials|TestProbe; texts in
    tools/RunE2E.SystemChanges.psm1). They run by default: when the selected tests contain one, the script first shows a
    warning listing exactly the kinds the selected tests change and counts down 5 s (Ctrl+C aborts, nothing has changed
    then); only then it sets LAYERPEEK_E2E_SYSTEM_CHANGES=1 and LAYERPEEK_E2E_SYSTEM_CHANGE_KINDS=<the listed kinds>. A change of a
    kind the warning did not list is refused by the tests.
    The original state of every change is written to %LOCALAPPDATA%\LayerPeek.E2E.Restore before the change
    and restored afterwards - also after a failed test, by the next run's start, by the script's own cleanup (even after
    Ctrl+C), and by a hidden watchdog process (this script with -RestoreOnly -AfterProcess) that outlives a killed test
    process. -NoSystemChanges skips those tests without the warning. Known traces Windows may keep: the test app
    LayerPeek.E2E in the notification database %LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db
    and in the app list of Settings > System > Notifications (T-068, accepted 2026-10-22; the run reports every trace it
    finds in system-state-end.txt of the results folder).

    Screenshots of every checked state and the log of every started app go to the results folder, which is printed at
    the end (replaced by the next run).

.PARAMETER Class
    Only test classes matching this xUnit filter (e.g. '*CameraWindowTests').

.PARAMETER Method
    Only test methods matching this xUnit filter (e.g. '*NoPrinter*').

.PARAMETER PublishAot
    Also publish the Native AOT exe (win-x64, needs the C++ build tools) and check it for test switches, and publish the
    Debug configuration as Native AOT too (it keeps the test mode). After a green pass against the Debug exe, the same
    selected tests (-Class / -Method) run once more against that Native AOT exe (T-073), with a results folder of their
    own (<results>-aot); tests that make no sense there are skipped with the reason. The warning about system changes at
    the start covers both passes. The summary at the end shows both passes with their passed, skipped and failed tests.

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
$app = Join-Path $root 'src/LayerPeek.App'
$tests = Join-Path $root 'tests/LayerPeek.App.E2E.Tests'
$testsExe = Join-Path $tests 'bin/Debug/net11.0-windows/LayerPeek.App.E2E.Tests.exe'
# Same folder as SystemChanges.JournalDirectory in the tests (machine-wide: the system state is shared by all worktrees).
$journal = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LayerPeek.E2E.Restore'

# Same id as tests/Shared/TestWorktree.cs: first 4 bytes of SHA-256 of the upper-case worktree path.
$hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($root.ToUpperInvariant()))
$id = [Convert]::ToHexString($hash, 0, 4).ToLowerInvariant()
$results = Join-Path ([IO.Path]::GetTempPath()) "LayerPeek.App.E2E.Results.$id"
$script:logFile = $null
Import-Module (Join-Path $PSScriptRoot 'RunE2E.SystemChanges.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'RunE2E.Results.psm1') -Force

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
    foreach ($name in 'LAYERPEEK_E2E', 'LAYERPEEK_E2E_SYSTEM_CHANGES', 'LAYERPEEK_E2E_SYSTEM_CHANGE_KINDS', 'LAYERPEEK_E2E_RESTORE', 'LAYERPEEK_E2E_RESTORE_REPORT', 'LAYERPEEK_E2E_WATCHDOG') {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name)
    }
    try {
        [Environment]::SetEnvironmentVariable('LAYERPEEK_E2E', $null)
        [Environment]::SetEnvironmentVariable('LAYERPEEK_E2E_SYSTEM_CHANGES', $null)
        [Environment]::SetEnvironmentVariable('LAYERPEEK_E2E_SYSTEM_CHANGE_KINDS', $null)
        [Environment]::SetEnvironmentVariable('LAYERPEEK_E2E_RESTORE', '1')
        [Environment]::SetEnvironmentVariable('LAYERPEEK_E2E_RESTORE_REPORT', $report)
        # The restore makes no change of its own, so it needs no watchdog.
        [Environment]::SetEnvironmentVariable('LAYERPEEK_E2E_WATCHDOG', '0')
        $output = & $testsExe -method 'LayerPeek.App.E2E.Tests.SystemChangeRestore.RestorePendingSystemChanges' -noColor -noLogo 2>&1
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

# A journal of the app's former name (T-074) must be restored by the code of that name before anything else runs.
$legacyJournal = Resolve-LegacyRestoreJournal -LocalAppData ([Environment]::GetFolderPath('LocalApplicationData'))
if ($legacyJournal) {
    foreach ($line in $legacyJournal) { Write-Log $line Red }
    exit 1
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
Remove-Item Env:LAYERPEEK_E2E_SYSTEM_CHANGES, Env:LAYERPEEK_E2E_SYSTEM_CHANGE_KINDS, Env:LAYERPEEK_E2E_WATCHDOG, Env:LAYERPEEK_E2E_CHILD, Env:LAYERPEEK_E2E_RESTORE, Env:LAYERPEEK_E2E_RESTORE_REPORT -ErrorAction SilentlyContinue

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
$previousApp = $env:LAYERPEEK_E2E_APP
$passes = [Collections.Generic.List[object]]::new()
$exitCode = 1

# One pass of the selected tests against one exe, with its own results folder. The runner's report goes to a file
# outside first (the run empties its results folder at its start) and is kept in the results folder as test-results.xml.
function Invoke-TestPass([string] $Name, [string] $Results, [string] $App) {
    $env:LAYERPEEK_E2E_RESULTS = $Results
    if ($App) { $env:LAYERPEEK_E2E_APP = $App }
    $report = Join-Path ([IO.Path]::GetTempPath()) "LayerPeek.App.E2E.Report.$id.xml"
    Remove-Item -LiteralPath $report -ErrorAction SilentlyContinue
    $passExitCode = 1
    $counts = $null
    try {
        # Started as a process of its own that writes straight to this console: a pipe (e.g. `| Out-Host`, needed to keep
        # the output out of the function's result) would wait for every process that inherited it, and a test app left
        # running by a killed test process would hang the script before its restore (T-073).
        $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
        foreach ($argument in @('run', '--project', $tests, '--no-build', '--') + $filters + @('-result-xml', $report)) {
            $start.ArgumentList.Add($argument)
        }
        $start.UseShellExecute = $false
        $process = [Diagnostics.Process]::Start($start)
        try {
            $process.WaitForExit()
            $passExitCode = $process.ExitCode
        }
        finally {
            $process.Dispose()
        }
        $counts = Get-TestPassCounts -Path $report
        if ((Test-Path -LiteralPath $report) -and (Test-Path -LiteralPath $Results)) {
            Copy-Item -LiteralPath $report -Destination (Join-Path $Results 'test-results.xml') -Force
        }
    }
    finally {
        Remove-Item -LiteralPath $report -ErrorAction SilentlyContinue
        $passes.Add([pscustomobject] @{ Name = $Name; ExitCode = $passExitCode; Counts = $counts; Results = $Results })
    }
    return $passExitCode
}

try {
    if ($NoSystemChanges) {
        Write-Host 'System changes: off (-NoSystemChanges); the tests that change Windows settings are skipped.' -ForegroundColor Cyan
    }
    elseif ($systemChanges.Count -eq 0) {
        Write-Host 'System changes: none of the selected tests changes Windows settings.' -ForegroundColor Cyan
    }
    else {
        # One warning for both passes: the Native AOT pass of -PublishAot runs the same selection.
        Show-SystemChangesWarning -Kinds $systemChanges
        # Only after the countdown: Ctrl+C above leaves the tests unstarted and the system untouched. The tests refuse
        # any change of a kind the warning did not list.
        $env:LAYERPEEK_E2E_SYSTEM_CHANGE_KINDS = $systemChanges -join ','
        $env:LAYERPEEK_E2E_SYSTEM_CHANGES = '1'
    }

    $env:LAYERPEEK_E2E = '1'
    $exitCode = Invoke-TestPass -Name 'Debug exe' -Results $results
    if ($PublishAot -and $exitCode -eq 0) {
        # The same selection against the Native AOT exe of the Debug configuration (T-073: it keeps the test mode), with
        # its own results folder, so that the results of the pass above are kept.
        Write-Host ''
        Write-Host 'The selected tests against the Native AOT exe of the Debug app:'
        $exitCode = Invoke-TestPass -Name 'Native AOT exe (Debug)' -Results $resultsAot -App (Join-Path $aotDebug 'LayerPeek.exe')
    }
}
finally {
    Remove-Item Env:LAYERPEEK_E2E, Env:LAYERPEEK_E2E_RESULTS, Env:LAYERPEEK_E2E_SYSTEM_CHANGES, Env:LAYERPEEK_E2E_SYSTEM_CHANGE_KINDS -ErrorAction SilentlyContinue
    if ($null -eq $previousApp) { Remove-Item Env:LAYERPEEK_E2E_APP -ErrorAction SilentlyContinue } else { $env:LAYERPEEK_E2E_APP = $previousApp }
    # Also after Ctrl+C or a crashed test process (entries of a test process that still ends are left to its watchdog).
    if (-not (Invoke-SystemChangeRestore 'after the run')) { $exitCode = 1 }
}

Write-Host ''
Write-Host 'Summary:'
foreach ($pass in $passes) {
    $color = if ($pass.ExitCode -eq 0) { [ConsoleColor]::Green } else { [ConsoleColor]::Red }
    Write-Host "  $(Format-TestPassSummary -Name $pass.Name -ExitCode $pass.ExitCode -Counts $pass.Counts)" -ForegroundColor $color
    Write-Host "    screenshots, app logs, test-results.xml: $($pass.Results)"
}
if ($PublishAot -and $passes.Count -eq 1) {
    Write-Host "  $(Format-TestPassSummary -Name 'Native AOT exe (Debug)' -ExitCode $null -Counts $null -NotRunReason 'not run (the Debug pass failed)')" -ForegroundColor Red
}
exit $exitCode
